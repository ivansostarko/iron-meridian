using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// Ground-based air defence: a deployed anti-aircraft formation finds the
    /// drones flying over it and shoots them down.
    ///
    /// **Why it is automatic.** Air defence is the one thing on the map that
    /// genuinely does not wait to be told. A SAM battery's whole purpose is to
    /// engage what enters its envelope, in seconds, without reference to
    /// anything a commander is doing at the time — asking the player to click
    /// on an incoming drone would be modelling a decision nobody makes. So this
    /// system has no orders and no UI: deploying the launcher *is* the order.
    ///
    /// **The sequence, and what each part of it is for.**
    ///
    ///  1. **Acquisition.** Four times a second, every track in
    ///     <see cref="AirTarget.All"/> is measured against every living
    ///     anti-aircraft formation on the *other* side. A launcher must have the
    ///     track inside both its weapon range and its view range, in **slant**
    ///     range — the diagonal to something four hundred metres up, not the
    ///     distance across the map to the ground beneath it — and must have
    ///     clear line of sight to it through the terrain.
    ///  2. **The contact is shown.** A ring goes on the ground under the track,
    ///     captioned with what it is, and the HUD says which battery has it.
    ///     Being shot at by something you never saw would be the same failure as
    ///     not modelling the shot at all.
    ///  3. **Two seconds later the missile leaves the rail.** That delay is the
    ///     one part of the engagement the player can act inside, and it is what
    ///     makes an air-defence envelope read as a hazard rather than as an
    ///     instant-death zone.
    ///  4. **It hits.** Always. See below.
    ///
    /// **Why the missile cannot miss.** A probabilistic interception would be
    /// more realistic and much worse to play against: the outcome of sending a
    /// drone into a defended sector would be unknowable, so the only rational
    /// play would be to send drones and see, which is not a decision. Making the
    /// envelope absolute makes it *information* — a defended sector is a place
    /// your drones do not come back from, the counter is to find the launcher
    /// and kill it first, and both of those are decisions. The two-second
    /// reaction and the finite envelope are where the play is.
    ///
    /// **One missile per track, one track per launcher.** A track is marked
    /// engaged the moment a battery commits to it, so six launchers do not empty
    /// themselves into the same piece of sky; and a battery already firing is
    /// not offered another track until its own is resolved.
    ///
    /// See docs/24-AIR-DEFENCE.md.
    /// </summary>
    public class AirDefenceSystem : MonoBehaviour
    {
        /// <summary>HUD line — acquisition and kill reports.</summary>
        public System.Action<string> Flash;

        /// <summary>Seconds between acquiring a track and the missile leaving the rail.</summary>
        public const float ReactionSeconds = 2f;

        /// <summary>Seconds between sweeps of the air picture.</summary>
        const float ScanIntervalSeconds = 0.25f;
        /// <summary>
        /// Anti-air rating below which a formation cannot take a shot, however
        /// it is filed. See <see cref="IsAirDefence"/>.
        /// </summary>
        const float MinAntiAir = 50f;
        /// <summary>Interceptor speed, m/s — roughly Mach 2.6, ordinary for the class.</summary>
        const float MissileSpeedMps = 900f;
        /// <summary>Flight time is clamped: too short to see, or long enough to look lost.</summary>
        const float MinFlightSeconds = 0.9f, MaxFlightSeconds = 7f;
        /// <summary>
        /// Radius of the ground ring marking a tracked contact, km. Bigger than
        /// it needs to be to read: <see cref="RangeRing"/> rebuilds its geometry
        /// — and re-samples the terrain under it — once the centre has moved a
        /// fraction of the radius, and a tight ring chasing a drone at fifty
        /// metres a second would be rebuilding constantly.
        /// </summary>
        const float ContactRingKm = 0.6f;
        /// <summary>Rounds a single engagement costs the launcher.</summary>
        const int RoundsPerEngagement = 2;

        /// <summary>One battery's commitment to one track, from acquisition to burst.</summary>
        class Engagement
        {
            public AirTarget target;
            public UnitActor launcher;
            /// <summary>Unscaled seconds still to run before the missile leaves the rail.</summary>
            public float countdown;
            public bool launched;
            public RangeRing contact;
            /// <summary>Unscaled seconds until the contact ring is moved again — see <see cref="ContactRingKm"/>.</summary>
            public float ringTimer;
        }

        readonly List<Engagement> _engagements = new List<Engagement>();
        CesiumGeoreference _geo;
        float _scanTimer;

        public void Init(CesiumGeoreference geo) => _geo = geo;

        /// <summary>Live engagements — the HUD's "batteries firing" figure, if it ever wants one.</summary>
        public int ActiveEngagements => _engagements.Count;

        void Update()
        {
            if (_geo == null) return;

            // Unscaled throughout: the drones this fights are flown on unscaled
            // time too, and an engagement measured on the operational clock
            // would freeze mid-flight every time the battle was paused while
            // the thing it was shooting at kept coming.
            float dt = Time.unscaledDeltaTime;

            TickEngagements(dt);

            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanIntervalSeconds;
            Scan();
        }

        // ------------------------------------------------------- acquisition

        void Scan()
        {
            var tracks = AirTarget.All;
            if (tracks.Count == 0) return;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track == null || track.Destroyed || track.Engaged) continue;

                var launcher = BestLauncherFor(track);
                if (launcher == null) continue;

                Commit(track, launcher);
            }
        }

        /// <summary>
        /// The formation that takes the shot: the nearest capable launcher that
        /// can see and reach the track and is not already firing at something
        /// else. Nearest rather than best-armed, because the near battery is the
        /// one whose envelope the drone has actually flown into.
        /// </summary>
        UnitActor BestLauncherFor(AirTarget track)
        {
            UnitActor best = null;
            double bestRange = double.MaxValue;

            foreach (var unit in UnitRegistry.All)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.State.TeamEnum == track.Team) continue;      // nobody shoots their own
                if (!IsAirDefence(unit.Def)) continue;
                if (IsBusy(unit)) continue;
                if (unit.State.ammo <= 0) continue;                   // an empty battery is a spectator

                double slantKm = SlantRangeKm(unit, track);
                if (slantKm > unit.Def.weaponRangeKm) continue;
                if (slantKm > unit.Def.viewRangeKm) continue;
                if (slantKm >= bestRange) continue;
                if (!HasLineOfSight(unit, track)) continue;

                best = unit;
                bestRange = slantKm;
            }

            return best;
        }

        /// <summary>
        /// Whether a unit type is a launcher: something on the ground that can
        /// physically put a missile into a drone.
        ///
        /// **Both catalogue fields are required, and that is the point.**
        /// <c>canCounterUas</c> alone would arm the electronic-warfare vans —
        /// they are filed as counter-UAS because jamming a drone is exactly what
        /// they do, and a SAM leaving the roof of a jammer would be the model
        /// saying something false about how the drone was defeated. A high
        /// <c>antiAir</c> alone would arm the air-defence radar, which sees
        /// everything and shoots nothing. Wanting to fight drones *and* having
        /// the rating to reach them is what a launcher is.
        ///
        /// <c>HoldsGround</c> keeps this to ground formations: aircraft and
        /// ships carry anti-air ratings too, and air-to-air is a different
        /// engagement this system does not model. See docs/04-UNITS.md.
        /// </summary>
        public static bool IsAirDefence(UnitDefinition def) =>
            def != null && def.HoldsGround &&
            def.canCounterUas && def.antiAir >= MinAntiAir;

        bool IsBusy(UnitActor unit)
        {
            foreach (var e in _engagements)
                if (e.launcher == unit) return true;
            return false;
        }

        /// <summary>
        /// Range to the track along the diagonal, not across the ground. A drone
        /// four hundred metres up and two kilometres away is 2.04 km from the
        /// launcher, and a short-range system whose envelope ends at two would
        /// otherwise be handed a shot it does not have.
        /// </summary>
        static double SlantRangeKm(UnitActor unit, AirTarget track)
        {
            double groundKm = GeoUtils.DistanceKm(
                unit.State.latitude, unit.State.longitude, track.Latitude, track.Longitude);
            double upKm = track.AltitudeMeters / 1000.0;
            return System.Math.Sqrt(groundKm * groundKm + upKm * upKm);
        }

        // Reused across sweeps: this runs against every launcher/track pair four
        // times a second, and RaycastAll would allocate an array each time.
        static readonly RaycastHit[] _sightHits = new RaycastHit[16];

        /// <summary>
        /// Can the launcher actually see the track, or is there a ridge between
        /// them?
        ///
        /// A raycast along the sight line, where **only Cesium terrain counts as
        /// blocking**. The scene is full of colliders that are not ground: unit
        /// icons carry one because they are click targets, and a control measure
        /// carries an invisible ribbon so the line can be picked — a battery
        /// standing on a phase line would otherwise be unable to see anything at
        /// all. Testing positively for a tileset is the only way to be sure what
        /// was hit was the world.
        ///
        /// Cesium streams its tiles, so the terrain genuinely may not be loaded;
        /// a raycast that hits nothing is therefore read as **clear**, which is
        /// the safe failure: an engagement that should not have happened is
        /// visible and arguable, whereas a battery that silently never fires
        /// because the ground under it has not finished downloading is neither.
        /// </summary>
        bool HasLineOfSight(UnitActor unit, AirTarget track)
        {
            // From the launcher's own height, not from the ground it stands on —
            // a radar and a launch rail are above the terrain, and starting the
            // ray at ground level clips the first metre of the hillside the
            // battery is sitting on.
            Vector3 from = GeoUtils.GeoToUnity(_geo,
                unit.State.latitude, unit.State.longitude, unit.State.heightMeters + SensorHeightMeters);
            Vector3 to = track.transform.position;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1f) return true;

            int count = Physics.RaycastNonAlloc(from, delta / distance, _sightHits, distance);
            for (int i = 0; i < count; i++)
            {
                var collider = _sightHits[i].collider;
                if (collider == null) continue;
                if (collider.GetComponentInParent<Cesium3DTileset>() != null) return false;
            }
            return true;
        }

        /// <summary>How high above its own ground point a launcher's sensors sit, metres.</summary>
        const float SensorHeightMeters = 20f;

        void Commit(AirTarget track, UnitActor launcher)
        {
            track.MarkEngaged();

            var engagement = new Engagement
            {
                target = track,
                launcher = launcher,
                countdown = ReactionSeconds,
                ringTimer = ScanIntervalSeconds,
                contact = RangeRing.Create(_geo, _geo.transform,
                    GameConfig.ViewRangeColor, "AIR CONTACT")
            };
            _engagements.Add(engagement);

            UpdateContactRing(engagement);

            Flash?.Invoke($"Air contact — {track.Label} tracked by {LauncherName(launcher)}. " +
                          $"Engaging in {Mathf.RoundToInt(ReactionSeconds)} seconds.");
        }

        static string LauncherName(UnitActor unit) =>
            string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;

        // -------------------------------------------------------- engagement

        void TickEngagements(float dt)
        {
            for (int i = _engagements.Count - 1; i >= 0; i--)
            {
                var e = _engagements[i];

                // The track can end on its own — the sortie flew home, or the
                // scene was reloaded. Nothing to shoot at any more.
                if (e.target == null || e.target.Destroyed)
                {
                    Retire(i);
                    continue;
                }

                // The launcher can die inside its own two seconds. The
                // commitment is released rather than dropped, so another battery
                // can take the track on — a drone that flew home because the
                // launcher that had it was destroyed mid-count would be an
                // engagement quietly evaporating.
                if (e.launcher == null || !e.launcher.IsAlive)
                {
                    e.target.ReleaseEngagement();
                    Retire(i);
                    continue;
                }

                e.ringTimer -= dt;
                if (e.ringTimer <= 0f)
                {
                    e.ringTimer = ScanIntervalSeconds;
                    UpdateContactRing(e);
                }

                if (e.launched) continue;

                e.countdown -= dt;
                if (e.countdown > 0f) continue;

                Fire(e);
                // Nothing further to do here: the missile owns the rest, and
                // calls back on arrival.
            }
        }

        void Fire(Engagement e)
        {
            e.launched = true;

            double slantKm = SlantRangeKm(e.launcher, e.target);
            float flightSeconds = Mathf.Clamp(
                (float)(slantKm * 1000.0) / MissileSpeedMps, MinFlightSeconds, MaxFlightSeconds);

            var run = InterceptorRun.Launch(_geo, e.target,
                e.launcher.State.latitude, e.launcher.State.longitude, flightSeconds);

            // A launcher that shoots has fired: the round count is what stops an
            // air-defence formation being an infinite envelope.
            e.launcher.State.ammo = Mathf.Max(0, e.launcher.State.ammo - RoundsPerEngagement);
            e.launcher.NotifyFiring();

            string label = e.target.Label;
            string battery = LauncherName(e.launcher);

            // The only way a launch is refused is a track that ended inside the
            // two-second count. There is nothing to shoot at and nothing to
            // report; the engagement is retired on the next tick.
            if (run == null) return;

            run.Intercept = (lat, lon, altitude) =>
            {
                VfxSystem.PlayAloft(VfxId.AirInterceptBurst, lat, lon, altitude);
                Kill(e.target, label, battery);
            };
        }

        void Kill(AirTarget track, string label, string battery)
        {
            if (track == null || track.Destroyed) return;
            track.Kill();
            Flash?.Invoke($"{label} shot down by {battery}.");
        }

        /// <summary>Keeps the contact ring under the track it is marking.</summary>
        void UpdateContactRing(Engagement e)
        {
            if (e.contact == null) return;
            e.contact.Show(e.target.Latitude, e.target.Longitude, ContactRingKm,
                $"AIR CONTACT  ·  {e.target.Label.ToUpperInvariant()}");
        }

        void Retire(int index)
        {
            var e = _engagements[index];
            if (e.contact != null) Destroy(e.contact.gameObject);
            _engagements.RemoveAt(index);
        }

        /// <summary>Drops every engagement — used when the map is reloaded or reset.</summary>
        public void CancelAll()
        {
            for (int i = _engagements.Count - 1; i >= 0; i--)
            {
                _engagements[i].target?.ReleaseEngagement();
                Retire(i);
            }
        }

        void OnDestroy() => CancelAll();
    }
}
