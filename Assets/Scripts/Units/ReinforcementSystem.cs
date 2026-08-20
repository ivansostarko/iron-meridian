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

        /// <summary>
        /// Puts a type on the schedule, or adds one to the count of a row that
        /// is already there.
        ///
        /// Merging rather than appending is what keeps the list readable: asking
        /// for four battalions is four presses of the same card, and four
        /// identical rows would be a list nobody can scan and a removal nobody
        /// can aim. Identical means every field a designer chose — the type, the
        /// side, the size and the minute — so two rows that differ in any of
        /// them stay two rows.
        /// </summary>
        public ReinforcementEntry Add(UnitDefinition def, Team team, Echelon echelon, int arrivalMinutes)
        {
            if (def == null) return null;

            int minutes = Mathf.Max(0, arrivalMinutes);
            string teamName = team.ToString();
            string echelonName = echelon.ToString();

            foreach (var existing in _schedule)
            {
                if (existing.defId != def.id || existing.team != teamName ||
                    existing.echelon != echelonName || existing.arrivalMinutes != minutes) continue;
                existing.count = Mathf.Clamp(existing.count + 1, MinCount, MaxCount);
                Changed?.Invoke();
                return existing;
            }

            var entry = new ReinforcementEntry
            {
                defId = def.id,
                team = teamName,
                echelon = echelonName,
                arrivalMinutes = minutes,
                count = 1
            };
            _schedule.Add(entry);
            SortSchedule();
            Changed?.Invoke();
            return entry;
        }

        /// <summary>Fewest and most formations one row may bring on.</summary>
        public const int MinCount = 1;
        public const int MaxCount = 24;

        /// <summary>
        /// Steps a row's quantity. One is the floor rather than zero: a row
        /// bringing nothing on is a row that should have been removed, and
        /// offering it as a state would leave the schedule full of arrivals that
        /// silently do nothing.
        /// </summary>
        public void StepCount(ReinforcementEntry entry, int delta)
        {
            if (entry == null) return;
            int next = Mathf.Clamp(entry.count + delta, MinCount, MaxCount);
            if (next == entry.count) return;
            entry.count = next;
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

        /// <summary>
        /// Brings one **scheduled** row on now rather than waiting for its
        /// minute, and marks it as having arrived so it does not come again.
        ///
        /// The battle panel's only verb. A commander who can see a reserve is
        /// due at H+40 and wants it at H+12 is making a decision the scenario
        /// left them, and the alternative — waiting, or a second catalogue to
        /// pick the same formation out of again — is worse than either.
        ///
        /// Refused once it has arrived: the row on screen is then a record of
        /// something that happened, and pressing it again would quietly double
        /// the force the designer laid on.
        /// </summary>
        public bool BringForward(ReinforcementEntry entry)
        {
            if (entry == null || entry.arrived) return false;
            int index = _schedule.IndexOf(entry);
            if (index < 0) return false;

            entry.arrived = true;
            Deliver(entry, index);
            return true;
        }

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

        /// <summary>Rows on one side's schedule.</summary>
        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var e in _schedule) if (e.team == team.ToString()) n++;
            return n;
        }

        /// <summary>Formations on one side's schedule — rows times their counts.</summary>
        public int FormationsFor(Team team)
        {
            int n = 0;
            string name = team.ToString();
            foreach (var e in _schedule) if (e.team == name) n += Mathf.Max(1, e.count);
            return n;
        }

        /// <summary>One side's rows, earliest first. The order both panels list them in.</summary>
        public List<ReinforcementEntry> For(Team team)
        {
            var rows = new List<ReinforcementEntry>();
            string name = team.ToString();
            foreach (var e in _schedule) if (e.team == name) rows.Add(e);
            return rows;
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

            // The whole row at once, each formation on its own scatter index, so
            // three battalions arrive as a laydown rather than as three counters
            // stacked on one point. Offset by the row's position in the schedule
            // so two rows due in the same minute do not land on each other.
            int n = Mathf.Clamp(entry.count, MinCount, MaxCount);
            for (int i = 0; i < n; i++)
            {
                Place(team, index * MaxCount + i, out double lat, out double lon);
                Spawn(def, team, echelon, lat, lon);
            }
            Changed?.Invoke();

            string many = n > 1 ? $"{n} × " : "";
            Flash?.Invoke($"Reinforcement — {many}{def.name} ({echelon}) arrives at " +
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
                    arrivalMinutes = e.arrivalMinutes,
                    count = e.count
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
                    // A file written before the field existed parses as 0, which
                    // would be a row that brought nothing on.
                    e.count = Mathf.Clamp(e.count <= 0 ? 1 : e.count, MinCount, MaxCount);
                    _schedule.Add(e);
                }
            SortSchedule();
            Changed?.Invoke();
        }
    }
}
