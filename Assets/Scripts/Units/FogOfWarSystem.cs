using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Limited intelligence: the player sees the enemy only where something of
    /// theirs is actually looking.
    ///
    /// An enemy formation inside a friendly unit's view range (or a recon
    /// sensor's footprint — see <see cref="ReconOrderSystem"/>) is drawn
    /// normally. One that is not simply disappears, and in its place the map
    /// keeps a **contact**: a ring centred on where it was last seen, captioned
    /// with the scenario time of the sighting, growing as the estimate ages.
    /// The radius is how far that formation could have driven since — so the
    /// ring states the uncertainty rather than pretending to a position.
    ///
    /// **Battle mode only, and off by default.** The scenario editor exists to
    /// lay out both sides; hiding half of what is being edited would make it
    /// useless. Turning fog on in the editor arms it for the next battle rather
    /// than blanking the map you are working on.
    ///
    /// What this does *not* yet hide is listed in docs/16-FOG-OF-WAR.md —
    /// derived graphics (auto front line, red sectors) still read every enemy
    /// position, because they are computed from the truth rather than from what
    /// the player has seen.
    /// </summary>
    public class FogOfWarSystem : MonoBehaviour
    {
        /// <summary>Seconds between detection sweeps. Detection is not frame-critical and each check is cheap but O(n·m).</summary>
        const float SweepSeconds = 0.4f;
        /// <summary>Contact rings start at least this wide, so a fresh contact is still readable.</summary>
        const float MinUncertaintyKm = 0.4f;
        /// <summary>However stale a contact gets, the ring stops here rather than covering the map.</summary>
        const float MaxUncertaintyKm = 30f;
        /// <summary>
        /// Extra range a unit keeps a contact at once it has one — losing and
        /// regaining a formation every second as it walks the edge of a view
        /// arc reads as a bug, not as intelligence.
        /// </summary>
        const float HoldHysteresisKm = 0.6f;

        /// <summary>A detection footprint that is not a unit's own eyes — a recon task's.</summary>
        public class Sensor
        {
            public double latitude, longitude;
            public float radiusKm;
            /// <summary>What put it there, for the record; not used in the maths.</summary>
            public string label;
        }

        public static FogOfWarSystem Active { get; private set; }

        /// <summary>Armed by the player. Only actually blinds anything while a battle runs.</summary>
        public bool Enabled { get; private set; }
        public event System.Action<bool> EnabledChanged;

        /// <summary>Raised when a unit is hidden, so the selection can let go of it.</summary>
        public System.Action<UnitActor> UnitHidden;

        CesiumGeoreference _geo;
        GameClock _clock;
        float _timer;

        /// <summary>
        /// The dark over unobserved ground. Hiding the enemy's counters while
        /// leaving every road and ridge in plain view is not intelligence, so the
        /// terrain itself goes dark outside what the player is covering.
        /// </summary>
        FogBlanket _blanket;

        readonly List<Sensor> _sensors = new List<Sensor>();
        readonly Dictionary<UnitActor, Contact> _contacts = new Dictionary<UnitActor, Contact>();

        /// <summary>What is remembered about a formation that is no longer visible.</summary>
        class Contact
        {
            public double latitude, longitude;
            public string seenAt;          // scenario clock reading at the moment contact was lost
            public float lostAtRealtime;
            public float speedKmh;         // how fast it could be moving away
            public string designation;
            public RangeRing ring;
            /// <summary>Radius the ring was last rebuilt at — see <see cref="RefreshContact"/>.</summary>
            public float shownRadiusKm;
        }

        public void Init(CesiumGeoreference geo, GameClock clock)
        {
            _geo = geo;
            _clock = clock;
            _blanket = FogBlanket.Create(geo);
            _blanket.SetArea(null, maskOnly: true);
            Active = this;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>True while fog is actually blinding the player — armed *and* in battle.</summary>
        public bool InEffect => Enabled && CombatSystem.BattleRunning;

        /// <summary>
        /// The mission's ground. Set from the mission record when one is opened;
        /// null in the editor and for an unbounded mission.
        /// </summary>
        MissionArea _area;

        public bool HasArea => _area != null && _area.HasArea;

        /// <summary>
        /// True while the terrain blanket should be laid: fog armed, **or** the
        /// mission bounds its ground. A bounded mission blacks out everything
        /// outside its area in battle whether or not the player is fighting
        /// blind — the boundary is what the scenario is, not an intelligence
        /// setting. See <see cref="MissionArea"/>.
        /// </summary>
        public bool BlanketInEffect => (Enabled || HasArea) && CombatSystem.BattleRunning;

        /// <summary>
        /// Points the fog at a mission's ground. Safe to call with null (the
        /// editor's own map) and safe to call repeatedly.
        /// </summary>
        public void SetArea(MissionArea area)
        {
            _area = area != null && area.HasArea ? area : null;
            if (_blanket != null) _blanket.SetArea(_area, maskOnly: !Enabled);
            _blanketLaid = false;
        }

        /// <summary>
        /// Whether a formation is on the mission's ground at all. Anything
        /// outside it is off the battlefield: it is hidden from the player, and
        /// it is not tracked as a contact either, because a contact states that
        /// something was seen *and could still be somewhere* — which is the
        /// wrong claim about a formation that is out of the scenario.
        /// </summary>
        public bool InBounds(UnitActor unit) =>
            unit == null || _area == null || _area.Contains(unit.State.latitude, unit.State.longitude);

        public void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            // The blanket's job changes with the switch even when it stays up:
            // armed it tracks what is watched, disarmed it only frames the area.
            if (_blanket != null) _blanket.SetArea(_area, maskOnly: !on);
            if (!InEffect) RevealAll();
            EnabledChanged?.Invoke(on);
        }

        /// <summary>True when this unit is currently hidden from the player by fog.</summary>
        public bool IsHidden(UnitActor unit) => unit != null && unit.HiddenByFog;

        // ------------------------------------------------------- sensors

        /// <summary>
        /// Registers a detection footprint owned by something other than a
        /// unit's own eyes. The caller keeps the handle and moves it; the fog
        /// reads its position every sweep.
        /// </summary>
        public Sensor AddSensor(double lat, double lon, float radiusKm, string label)
        {
            var s = new Sensor { latitude = lat, longitude = lon, radiusKm = radiusKm, label = label };
            _sensors.Add(s);
            return s;
        }

        public void RemoveSensor(Sensor sensor)
        {
            if (sensor != null) _sensors.Remove(sensor);
        }

        // ------------------------------------------------------- sweep

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = SweepSeconds;

            if (!BlanketInEffect)
            {
                // Covers "fog off with no area" and "battle stopped" without
                // either caller having to remember to clean up.
                if (_contacts.Count > 0 || AnyHidden()) RevealAll();
                if (_blanket != null) _blanket.SetWanted(false);
                return;
            }

            Sweep();
        }

        void Sweep()
        {
            var watchers = new List<UnitActor>(UnitRegistry.OfTeam(Team.User));
            // Something standing off the mission's ground is not watching any of
            // it — and letting it clear the blanket would punch a hole in the
            // dark from outside the battlefield.
            watchers.RemoveAll(w => !InBounds(w));

            UpdateBlanket(watchers);

            foreach (var enemy in new List<UnitActor>(UnitRegistry.OfTeam(Team.Enemy)))
            {
                if (enemy == null || !enemy.IsAlive) continue;

                // Off the battlefield: hidden outright, no contact kept.
                if (!InBounds(enemy))
                {
                    if (!enemy.HiddenByFog)
                    {
                        enemy.SetHiddenByFog(true);
                        UnitHidden?.Invoke(enemy);
                    }
                    DropContact(enemy);
                    continue;
                }

                // In bounds and fog disarmed: the area is framing the battle,
                // not blinding the player, so nothing is hidden.
                if (!InEffect)
                {
                    if (enemy.HiddenByFog) enemy.SetHiddenByFog(false);
                    DropContact(enemy);
                    continue;
                }

                bool held = _contacts.ContainsKey(enemy);
                bool seen = Detected(enemy, watchers, held ? 0f : HoldHysteresisKm);

                if (seen)
                {
                    if (enemy.HiddenByFog) enemy.SetHiddenByFog(false);
                    DropContact(enemy);
                }
                else
                {
                    if (!enemy.HiddenByFog)
                    {
                        enemy.SetHiddenByFog(true);
                        UnitHidden?.Invoke(enemy);
                        RecordContact(enemy);
                    }
                    RefreshContact(enemy);
                }
            }

            // A contact whose unit has been destroyed while unobserved stays as
            // the last thing the player actually knew, until it is looked at.
            PruneDeadContacts();
        }

        /// <summary>
        /// Keeps the terrain blanket laid over the right ground and repaints it
        /// from the same watchers the unit sweep uses, so what the map shows and
        /// what the counters show can never disagree.
        /// </summary>
        void UpdateBlanket(List<UnitActor> watchers)
        {
            if (_blanket == null) return;

            _blanket.SetWanted(true);

            // Lay the grid on the first sweep of a battle, and re-lay it if the
            // advance has carried a formation out toward the edge of it. A
            // bounded mission never re-fits: the grid covers the mission's own
            // ground and is not supposed to follow anybody off it.
            bool refit = !_blanketLaid;
            if (!refit && !HasArea)
                foreach (var w in watchers)
                    if (_blanket.NeedsRefit(w)) { refit = true; break; }

            if (refit)
            {
                if (HasArea) _blanket.FitToArea(_area);
                else _blanket.Fit(UnitRegistry.All);
                _blanketLaid = true;
            }

            _blanket.Refresh(watchers, _sensors);
        }

        /// <summary>False until the blanket has been laid over this battle's ground.</summary>
        bool _blanketLaid;

        /// <summary>
        /// Whether anything of the player's can see this formation.
        /// <paramref name="bonusKm"/> is the hysteresis: a formation already
        /// being watched is kept slightly past the edge of the arc.
        /// </summary>
        bool Detected(UnitActor enemy, List<UnitActor> watchers, float bonusKm)
        {
            foreach (var w in watchers)
            {
                if (w == null || !w.IsAlive) continue;
                double km = GeoUtils.DistanceKm(w.State.latitude, w.State.longitude,
                    enemy.State.latitude, enemy.State.longitude);
                if (km <= w.Def.viewRangeKm + bonusKm) return true;
            }

            foreach (var s in _sensors)
            {
                double km = GeoUtils.DistanceKm(s.latitude, s.longitude,
                    enemy.State.latitude, enemy.State.longitude);
                if (km <= s.radiusKm + bonusKm) return true;
            }

            return false;
        }

        // ------------------------------------------------------- contacts

        void RecordContact(UnitActor enemy)
        {
            var contact = new Contact
            {
                latitude = enemy.State.latitude,
                longitude = enemy.State.longitude,
                seenAt = _clock != null ? _clock.TimeText : "--:--",
                lostAtRealtime = Time.time,
                speedKmh = Mathf.Max(1f, enemy.Def.speedKmh),
                designation = string.IsNullOrEmpty(enemy.State.customName)
                    ? enemy.Def.name : enemy.State.customName,
                ring = RangeRing.Create(_geo, _geo.transform, GameConfig.RedTeam, "CONTACT")
            };
            _contacts[enemy] = contact;
        }

        /// <summary>
        /// Grows the uncertainty ring. The radius is how far the formation could
        /// have travelled since it was last seen, on the same clock movement
        /// runs on — so the ring is a real statement about where it could be,
        /// not a decorative pulse.
        ///
        /// Elapsed time is <see cref="Time.time"/>, which is already scaled by
        /// the chosen game speed, so a contact lost while time is running at
        /// x60 becomes uncertain sixty times faster — which is exactly right:
        /// sixty times as much of its march has happened.
        /// </summary>
        void RefreshContact(UnitActor enemy)
        {
            if (!_contacts.TryGetValue(enemy, out var contact) || contact.ring == null) return;

            float elapsed = Time.time - contact.lostAtRealtime;
            float kmPerSecond = contact.speedKmh * (float)GameClock.GameSecondsPerRealSecond / 3600f;
            float radius = Mathf.Clamp(MinUncertaintyKm + kmPerSecond * elapsed,
                MinUncertaintyKm, MaxUncertaintyKm);

            // Rebuilding a ring re-samples the terrain under 96 vertices, so it
            // is only worth doing when the estimate has visibly moved. Growing
            // it a few pixels every sweep would cost a few hundred raycasts a
            // second for something nobody can see change.
            if (radius <= contact.shownRadiusKm * 1.05f && contact.shownRadiusKm > 0f) return;
            contact.shownRadiusKm = radius;

            contact.ring.Show(contact.latitude, contact.longitude, radius,
                $"{contact.designation}\nLAST SEEN {contact.seenAt}  ·  ±{radius:0.#} km");
        }

        void DropContact(UnitActor enemy)
        {
            if (!_contacts.TryGetValue(enemy, out var contact)) return;
            if (contact.ring != null) Destroy(contact.ring.gameObject);
            _contacts.Remove(enemy);
        }

        void PruneDeadContacts()
        {
            List<UnitActor> gone = null;
            foreach (var kv in _contacts)
                if (kv.Key == null) (gone ??= new List<UnitActor>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var k in gone)
            {
                if (_contacts.TryGetValue(k, out var c) && c.ring != null) Destroy(c.ring.gameObject);
                _contacts.Remove(k);
            }
        }

        bool AnyHidden()
        {
            foreach (var u in UnitRegistry.All)
                if (u != null && u.HiddenByFog) return true;
            return false;
        }

        /// <summary>Puts everything back on the map and clears every contact.</summary>
        public void RevealAll()
        {
            foreach (var u in UnitRegistry.All)
                if (u != null && u.HiddenByFog) u.SetHiddenByFog(false);

            foreach (var kv in _contacts)
                if (kv.Value.ring != null) Destroy(kv.Value.ring.gameObject);
            _contacts.Clear();

            // The blanket fades itself out; what has to go is the memory of
            // where the player has been, so the next battle starts blind.
            if (_blanket != null)
            {
                _blanket.SetWanted(false);
                _blanket.ResetExploration();
            }
            _blanketLaid = false;
        }
    }
}
