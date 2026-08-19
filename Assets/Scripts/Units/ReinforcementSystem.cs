using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Formations that are not on the map yet, and the clock that brings them
    /// on.
    ///
    /// **Why a scenario needs this.** Everything deployed in the editor is
    /// present at H-hour, which makes every battle a single roll of everything
    /// both sides own. Reinforcement is what turns that into a shape: a
    /// counter-attack that arrives at H+40, a reserve battalion the defender is
    /// waiting for, an enemy echelon the player knows is coming and has to be
    /// ready for. It is the cheapest possible addition of *time* to a game that
    /// otherwise only has space.
    ///
    /// **Scheduled in scenario minutes after the battle starts**, not at an
    /// absolute clock time. A designer thinks in "forty minutes in", the figure
    /// survives changing H-hour, and it reads the same whatever speed the
    /// battle is being watched at — the clock is the operational one, so x60
    /// brings the reserve on sixty times sooner in real seconds and at exactly
    /// the same moment in the fight.
    ///
    /// **They arrive in the deployment zone**, which the mission names (see
    /// docs/22-MISSIONS.md §1c). Without one they arrive behind their own side's
    /// centre of mass, which is the honest fallback: a reinforcement comes from
    /// the rear, and the rear is wherever the army already is.
    ///
    /// See docs/30-REINFORCEMENTS.md.
    /// </summary>
    public class ReinforcementSystem : MonoBehaviour
    {
        /// <summary>User-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the schedule changes, so the panel can repaint.</summary>
        public event System.Action Changed;

        /// <summary>
        /// Spawns one arrival: definition, side, echelon and where. Supplied by
        /// the controller, which owns the one path a unit comes onto the map by
        /// — undo record, deploy effect and all.
        /// </summary>
        public System.Action<UnitDefinition, Team, Echelon, double, double> Spawn;

        /// <summary>
        /// Where a side's arrivals appear: centre and radius in km. Null when
        /// the scenario names no zone for that side, in which case the fallback
        /// below applies.
        /// </summary>
        public System.Func<Team, (double lat, double lon, float radiusKm)?> ZoneFor;

        /// <summary>Km behind its own centre of mass a side's arrivals appear with no zone named.</summary>
        const double FallbackRearKm = 8.0;
        /// <summary>Radius arrivals scatter over when the fallback is used, km.</summary>
        const float FallbackRadiusKm = 3f;

        readonly List<ReinforcementEntry> _schedule = new List<ReinforcementEntry>();

        GameClock _clock;
        CombatSystem _combat;
        System.DateTime _battleStarted;
        bool _running;

        public IReadOnlyList<ReinforcementEntry> Schedule => _schedule;

        public void Init(GameClock clock, CombatSystem combat)
        {
            _clock = clock;
            _combat = combat;
            if (_combat != null) _combat.RunningChanged += OnRunningChanged;
        }

        void OnDestroy()
        {
            if (_combat != null) _combat.RunningChanged -= OnRunningChanged;
        }

        /// <summary>
        /// The battle starting is what starts the countdown — and starting it
        /// again re-arms every arrival.
        ///
        /// A schedule that kept running across a stop would be unusable in the
        /// editor, where the battle is started and stopped a dozen times while a
        /// scenario is being tested: the second run would begin with its
        /// reserves already spent.
        /// </summary>
        void OnRunningChanged(bool running)
        {
            _running = running;
            if (!running) return;

            _battleStarted = _clock != null ? _clock.Now : System.DateTime.MinValue;
            foreach (var e in _schedule) e.arrived = false;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------- schedule

        public void Add(UnitDefinition def, Team team, Echelon echelon, int arrivalMinutes)
        {
            if (def == null) return;

            _schedule.Add(new ReinforcementEntry
            {
                defId = def.id,
                team = team.ToString(),
                echelon = echelon.ToString(),
                arrivalMinutes = Mathf.Max(0, arrivalMinutes)
            });
            SortSchedule();
            Changed?.Invoke();
        }

        /// <summary>
        /// Brings one formation on **now**, in its side's deployment zone.
        ///
        /// The REINFORCEMENTS panel's whole verb. It shares the schedule's
        /// arrival machinery — the same <see cref="Place"/>, the same zone, the
        /// same golden-angle scatter — and skips only the waiting, so a
        /// formation called forward lands exactly where a scheduled one would
        /// have. That matters more than it sounds: a second placement rule would
        /// mean the deployment zone meant one thing to the designer's schedule
        /// and another to the commander's reserve.
        ///
        /// The scatter index runs across everything this method has ever placed
        /// rather than restarting per call, so clicking a type four times gives a
        /// laydown instead of four counters on one point.
        /// </summary>
        public void DeployNow(UnitDefinition def, Team team, Echelon echelon)
        {
            if (def == null || Spawn == null) return;

            Place(team, _deployedNow++, out double lat, out double lon);
            Spawn(def, team, echelon, lat, lon);

            bool zoned = ZoneFor?.Invoke(team) != null;
            Flash?.Invoke($"{def.name} ({echelon}) joins the " +
                          $"{(team == Team.User ? "friendly" : "enemy")} force — " +
                          (zoned ? "in its deployment zone." : "behind its own front; no deployment zone is set."));
        }

        /// <summary>How many formations <see cref="DeployNow"/> has placed, for the scatter.</summary>
        int _deployedNow;

        public void Remove(ReinforcementEntry entry)
        {
            if (entry == null) return;
            _schedule.Remove(entry);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _schedule.Clear();
            Changed?.Invoke();
        }

        /// <summary>Moves one arrival earlier or later. The panel's ± buttons.</summary>
        public void Reschedule(ReinforcementEntry entry, int deltaMinutes)
        {
            if (entry == null) return;
            entry.arrivalMinutes = Mathf.Clamp(entry.arrivalMinutes + deltaMinutes, 0, 24 * 60);
            SortSchedule();
            Changed?.Invoke();
        }

        /// <summary>Earliest first — the order the scenario will actually play out in.</summary>
        void SortSchedule() =>
            _schedule.Sort((a, b) => a.arrivalMinutes.CompareTo(b.arrivalMinutes));

        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var e in _schedule) if (e.team == team.ToString()) n++;
            return n;
        }

        /// <summary>Scenario minutes since the battle started, or 0 outside one.</summary>
        public double ElapsedMinutes =>
            !_running || _clock == null ? 0.0 : (_clock.Now - _battleStarted).TotalMinutes;

        // ------------------------------------------------------------ arrival

        void Update()
        {
            if (!_running || _clock == null || Spawn == null) return;

            double elapsed = ElapsedMinutes;

            // Walked by index and not broken out of: several arrivals can be due
            // in one frame at high game speed, and making them wait a frame each
            // would drip a brigade onto the map over half a second.
            for (int i = 0; i < _schedule.Count; i++)
            {
                var entry = _schedule[i];
                if (entry.arrived || elapsed < entry.arrivalMinutes) continue;
                entry.arrived = true;
                Deliver(entry, i);
            }
        }

        void Deliver(ReinforcementEntry entry, int index)
        {
            var def = UnitDatabase.Get(entry.defId);
            if (def == null)
            {
                Debug.LogWarning($"[ReinforcementSystem] No unit type '{entry.defId}' — arrival skipped.");
                return;
            }

            var team = entry.team == nameof(Team.Enemy) ? Team.Enemy : Team.User;
            if (!System.Enum.TryParse(entry.echelon, out Echelon echelon)) echelon = Echelon.Battalion;

            Place(team, index, out double lat, out double lon);
            Spawn(def, team, echelon, lat, lon);
            Changed?.Invoke();

            Flash?.Invoke($"Reinforcement — {def.name} ({echelon}) arrives at " +
                          $"H+{entry.arrivalMinutes} for {(team == Team.User ? "friendly" : "enemy")} forces.");
        }

        /// <summary>
        /// Where one arrival lands: scattered over its side's deployment zone,
        /// or behind its own force when the scenario names none.
        ///
        /// Scattered rather than stacked, on the same golden-angle disc the
        /// artillery sheaf uses, so an echelon of six battalions arrives as a
        /// laydown rather than as six counters on one point.
        /// </summary>
        void Place(Team team, int index, out double lat, out double lon)
        {
            var zone = ZoneFor?.Invoke(team);
            double centreLat, centreLon;
            float radiusKm;

            if (zone.HasValue)
            {
                centreLat = zone.Value.lat;
                centreLon = zone.Value.lon;
                radiusKm = Mathf.Max(0.3f, zone.Value.radiusKm);
            }
            else
            {
                RearOf(team, out centreLat, out centreLon);
                radiusKm = FallbackRadiusKm;
            }

            // Golden angle: successive arrivals never line up, and the disc
            // fills evenly however many there turn out to be.
            double angle = index * 137.508;
            double distance = radiusKm * System.Math.Sqrt((index % 8 + 0.5) / 8.0);
            GeoUtils.Destination(centreLat, centreLon, angle % 360.0, distance, out lat, out lon);
        }

        /// <summary>
        /// A point behind a side's centre of mass — the fallback deployment
        /// zone. "Behind" is away from the enemy's centre; with no enemy on the
        /// map it is the side's own centre, which is the only defensible answer.
        /// </summary>
        void RearOf(Team team, out double lat, out double lon)
        {
            double ownLat = 0, ownLon = 0, foeLat = 0, foeLon = 0;
            int own = 0, foe = 0;

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                if (u.State.TeamEnum == team) { ownLat += u.State.latitude; ownLon += u.State.longitude; own++; }
                else { foeLat += u.State.latitude; foeLon += u.State.longitude; foe++; }
            }

            if (own == 0)
            {
                // Nothing of this side on the map at all: the enemy's position
                // is the only reference there is, so arrive well away from it.
                if (foe == 0) { lat = 0; lon = 0; return; }
                foeLat /= foe; foeLon /= foe;
                GeoUtils.Destination(foeLat, foeLon, 180.0, FallbackRearKm * 2.0, out lat, out lon);
                return;
            }

            ownLat /= own; ownLon /= own;
            if (foe == 0) { lat = ownLat; lon = ownLon; return; }

            foeLat /= foe; foeLon /= foe;
            float towardEnemy = GeoUtils.BearingDeg(ownLat, ownLon, foeLat, foeLon);
            GeoUtils.Destination(ownLat, ownLon, towardEnemy + 180f, FallbackRearKm, out lat, out lon);
        }

        // ------------------------------------------------------------ saving

        public List<ReinforcementEntry> Serialize()
        {
            var copy = new List<ReinforcementEntry>(_schedule.Count);
            foreach (var e in _schedule)
                copy.Add(new ReinforcementEntry
                {
                    defId = e.defId,
                    team = e.team,
                    echelon = e.echelon,
                    arrivalMinutes = e.arrivalMinutes
                    // `arrived` is deliberately not written: a saved scenario is
                    // a starting state, and a reserve that had already come on
                    // when the file was written must still come on when it is
                    // played.
                });
            return copy;
        }

        public void LoadFrom(List<ReinforcementEntry> data)
        {
            _schedule.Clear();
            if (data != null)
                foreach (var e in data)
                {
                    if (e == null || string.IsNullOrEmpty(e.defId)) continue;
                    e.arrived = false;
                    _schedule.Add(e);
                }
            SortSchedule();
            Changed?.Invoke();
        }
    }
}
