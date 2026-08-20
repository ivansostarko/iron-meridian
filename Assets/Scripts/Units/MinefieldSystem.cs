using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// What happens when a formation drives into the enemy's mines.
    ///
    /// Until this existed the barrier plan was a picture. A designer could lay a
    /// belt across the one road into the objective, the attacker could march
    /// straight down it, and nothing whatever happened — which made every
    /// minefield in the game a decoration and every decision about where to put
    /// one a decision about nothing. Mines are the cheapest way there is to buy
    /// time and shape an attack, and a wargame in which they do not is missing
    /// one of the few things a defender can actually do.
    ///
    /// **It only fires on the move.** A formation sitting inside a belt is not
    /// taking casualties from it — it has stopped, and a stopped unit is
    /// breaching, probing or picking its way. The whole cost of a minefield is
    /// paid by whoever tries to *cross* it at speed, which is exactly the
    /// decision the graphic is there to make expensive. It also keeps the rule
    /// legible: the player sees a column driving into mines and losing strength,
    /// not units quietly bleeding wherever they happen to be parked.
    ///
    /// **Mines belong to a side and do not know their own.** A belt only catches
    /// the *other* side's formations. Real minefields are not so polite, but a
    /// scenario editor with no gapping, no lane marking and no recorded breach
    /// plan would turn friendly-fire mines into a bug report rather than a
    /// decision — the player laid them, the player cannot mark them, and the
    /// player's own attack dies in them.
    ///
    /// **Each strike is one detonation, then a wait.** A formation crossing a
    /// belt takes a hit as it enters and another every
    /// <see cref="StrikeIntervalSeconds"/> of scenario time it is still moving
    /// inside it, so a wide field costs more than a thin one and driving round
    /// the edge costs almost nothing. A single hit on entry would make a
    /// two-kilometre belt no worse than a hundred-metre one; continuous damage
    /// would kill anything that touched a field at all.
    ///
    /// The damage itself goes through <see cref="UnitActor.ApplyDamage"/>, so
    /// the burning, routing and death sequences all follow without this class
    /// knowing about any of them — the same route
    /// <see cref="BlastDamage"/> takes.
    ///
    /// See docs/31-OBSTACLES.md.
    /// </summary>
    public class MinefieldSystem : MonoBehaviour
    {
        /// <summary>Short user-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;

        /// <summary>
        /// Seconds of scenario time between successive detonations under one
        /// formation in one belt. Long enough that crossing a normal field
        /// costs two or three strikes rather than a continuous bleed; short
        /// enough that a formation cannot drive through a big one for free.
        /// </summary>
        const float StrikeIntervalSeconds = 22f;

        /// <summary>
        /// How often the check runs. A third of a second is far finer than
        /// any formation moves — the fastest thing on the map covers about
        /// ten metres in it — and cheap enough to run against every belt.
        /// </summary>
        const float TickSeconds = 0.35f;

        /// <summary>
        /// Ground a stamped mine symbol is taken to cover, as a fraction of the
        /// catalogue's drawn width.
        ///
        /// Half, i.e. the drawn circle's radius — the symbol is centred on what
        /// it marks, so the graphic on the map and the ground that is dangerous
        /// are the same ground. An outlined belt does not use this at all: its
        /// polygon *is* the answer, which is the argument for outlining one.
        /// </summary>
        const float SymbolFootprintShare = 0.5f;

        /// <summary>
        /// Strength removed by one detonation under a full formation, before the
        /// type and echelon adjustments below. Small on purpose: mines maim and
        /// delay, they do not destroy battalions, and a belt that wiped out what
        /// crossed it would be a wall rather than an obstacle.
        /// </summary>
        const float BaseDamage = 0.055f;

        /// <summary>
        /// Shock dealt per point of strength lost — the same currency
        /// <see cref="BlastDamage"/> spends. Higher than a shell's, because the
        /// point of a minefield is what it does to an attack's momentum rather
        /// than to its order of battle.
        /// </summary>
        const float ShockMultiplier = 80f;

        MapManager _map;
        ObstacleSystem _obstacles;
        GameClock _clock;

        float _timer;

        /// <summary>
        /// When each formation last set off a mine in each belt, in scenario
        /// seconds. Keyed on both, so a column that crosses two overlapping
        /// belts is caught by both rather than by whichever it entered first.
        /// </summary>
        readonly Dictionary<(string unit, string field), float> _lastStrike =
            new Dictionary<(string, string), float>();

        public void Init(MapManager map, ObstacleSystem obstacles, GameClock clock)
        {
            _map = map;
            _obstacles = obstacles;
            _clock = clock;
        }

        void Update()
        {
            // Battle mode only, like everything else that changes the order of
            // battle. In the editor the same units are being dragged across the
            // same ground on purpose.
            if (!CombatSystem.BattleRunning || _obstacles == null) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;

            Sweep();
        }

        void Sweep()
        {
            var markers = _obstacles.Markers;
            if (markers == null || markers.Count == 0) return;

            float now = ScenarioSeconds();

            // A copy: a detonation can kill a formation, which unregisters it
            // and would otherwise mutate the list being walked.
            var units = new List<UnitActor>(UnitRegistry.All);

            foreach (var unit in units)
            {
                if (unit == null || !unit.IsAlive) continue;
                // Standing still is not crossing — see the class remarks.
                if (unit.Mover == null || !unit.Mover.IsMoving) continue;

                foreach (var field in markers)
                {
                    if (field == null || !IsMine(field.Kind)) continue;
                    if (field.Data.team == unit.State.team) continue;   // your own belt
                    if (!Covers(field, unit.State.latitude, unit.State.longitude)) continue;

                    var key = (unit.State.instanceId, field.Data.id);
                    if (_lastStrike.TryGetValue(key, out float last) &&
                        now - last < StrikeIntervalSeconds) continue;

                    _lastStrike[key] = now;
                    Detonate(field, unit);
                }
            }
        }

        static bool IsMine(ObstacleKind kind) =>
            ObstacleCatalog.Get(kind).family == ObstacleFamily.Mines;

        /// <summary>
        /// Whether a formation is in the belt — its polygon when it has one, and
        /// the ground under its symbol when it does not.
        ///
        /// Measured to the formation's centre rather than to the edge of its
        /// footprint, which is the opposite of what <see cref="BlastDamage"/>
        /// does and is right for the opposite reason. A shell is aimed at a
        /// point and the question is whether it reached the formation; a
        /// minefield is ground and the question is whether the formation went
        /// into it. Crediting the footprint would set a division off from a
        /// kilometre away, which is not driving into mines.
        /// </summary>
        static bool Covers(ObstacleMarker field, double lat, double lon)
        {
            if (field.IsArea) return ContainsPoint(field.Data.points, lat, lon);

            float radius = ObstacleCatalog.Get(field.Kind).widthMeters * SymbolFootprintShare;
            double metres = GeoUtils.DistanceKm(field.Data.latitude, field.Data.longitude,
                                                lat, lon) * 1000.0;
            return metres <= radius;
        }

        /// <summary>
        /// Even-odd crossing test in plate carrée, the same one
        /// <see cref="MissionArea.Contains"/> uses and for the same reasons —
        /// it is exact in any consistent planar frame, and lon/lat is one over
        /// the few kilometres a belt covers.
        /// </summary>
        static bool ContainsPoint(List<GeoPoint> polygon, double lat, double lon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = polygon[i].latitude, xi = polygon[i].longitude;
                double yj = polygon[j].latitude, xj = polygon[j].longitude;

                if (yi > lat == yj > lat) continue;
                double x = (xj - xi) * (lat - yi) / (yj - yi) + xi;
                if (lon < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// One mine going off: the blast where the formation actually is, the
        /// dust it leaves, and the strength it costs.
        ///
        /// The effect is played at the *formation*, not at the belt's centre. A
        /// column strung out along a road is a kilometre long, and a detonation
        /// drawn at the middle of the field while the unit is at its edge would
        /// read as something else happening nearby.
        /// </summary>
        void Detonate(ObstacleMarker field, UnitActor unit)
        {
            double lat = unit.State.latitude, lon = unit.State.longitude;

            VfxSystem.Play(VfxId.MineBlast, lat, lon);
            var smoke = VfxSystem.Play(VfxId.MineSmoke, lat, lon);
            if (smoke != null && VfxSystem.Active != null)
                VfxSystem.Active.StopAfter(smoke, SmokeSeconds);

            float damage = DamageFor(field.Kind, unit);
            float before = unit.State.strength;
            unit.ApplyDamage(damage);
            unit.ApplyShock(Mathf.Min(damage, before) * ShockMultiplier);

            // Only the side that drove into them hears about it. The player who
            // laid the belt learns it worked from the map — a formation slowing,
            // burning and turning back — which is what a real report looks like,
            // and a flash line for every mine on a busy front would be noise.
            if (unit.State.TeamEnum == Team.User)
            {
                var def = ObstacleCatalog.Get(field.Kind);
                string who = string.IsNullOrEmpty(unit.State.customName)
                    ? (unit.Def != null ? unit.Def.name : "A formation")
                    : unit.State.customName;
                Flash?.Invoke(unit.IsAlive
                    ? $"{who} is in {def.name.ToLowerInvariant()} — mine strike, " +
                      $"{unit.State.strength * 100f:0} % strength."
                    : $"{who} destroyed in {def.name.ToLowerInvariant()}.");
            }
        }

        /// <summary>Seconds the mine dust hangs about. Short: it is a charge, not a shell.</summary>
        const float SmokeSeconds = 14f;

        /// <summary>
        /// What one detonation costs this formation.
        ///
        /// Three adjustments, each of which is the reason the type exists:
        ///
        /// • **AT mines against vehicles, AP mines against men.** A pressure
        ///   plate built to break a track does very little to a rifle company on
        ///   foot, and a fragmentation mine does very little to a tank. Laying
        ///   the right sort for what is expected to come down the road is the
        ///   one interesting decision the catalogue offers, and it is worth
        ///   nothing unless the model can tell them apart.
        ///
        /// • **Bigger formations shrug it off.** A mine strike is a vehicle or a
        ///   section, and that is a larger share of a company than of a
        ///   division. Scaled by the echelon's manpower multiplier, so the
        ///   *absolute* loss is roughly constant and the proportional one falls.
        ///
        /// • **Armour helps, but only against fragments.** Halved at most:
        ///   armour is protection against blast from the side, and a mine is
        ///   underneath.
        /// </summary>
        static float DamageFor(ObstacleKind kind, UnitActor unit)
        {
            float damage = BaseDamage;

            bool mounted = unit.Def != null &&
                           (unit.Def.armour > 5f || unit.Def.Branch == UnitBranch.Armour ||
                            unit.Def.Branch == UnitBranch.Mechanised);

            switch (kind)
            {
                case ObstacleKind.AntiTankMines:
                    damage *= mounted ? 2.0f : 0.35f;
                    break;
                case ObstacleKind.AntiPersonnelMines:
                    damage *= mounted ? 0.40f : 1.8f;
                    break;
                default:
                    // MINES and MINEFIELD are mixed belts by definition — the
                    // catalogue calls one "unspecified" and the other "laid and
                    // recorded", and both are laid against whatever comes.
                    break;
            }

            float bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            damage /= Mathf.Sqrt(bulk);

            if (unit.Def != null && unit.Def.armour > 0f)
                damage *= Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(unit.Def.armour / 100f));

            return Mathf.Max(0.005f, damage);
        }

        float ScenarioSeconds() =>
            _clock != null ? (float)(_clock.Now - System.DateTime.MinValue).TotalSeconds
                           : Time.unscaledTime;

        /// <summary>
        /// Forgets every record of who has stepped on what.
        ///
        /// Called when the battle stops or a map is loaded: the cooldowns are
        /// about one run of one fight, and a formation that crossed a belt in
        /// the last battle must not be immune at the start of the next one.
        /// </summary>
        public void Reset() => _lastStrike.Clear();
    }
}
