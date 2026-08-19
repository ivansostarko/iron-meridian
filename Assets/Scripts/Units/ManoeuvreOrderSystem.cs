using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// Movement orders and the standing commands that sit under them.
    ///
    /// Three jobs, and they belong together because they are all answers to
    /// "what is this formation doing when nobody is telling it anything":
    ///
    ///  • **The three plain moves** — MOVE, FAST MOVE, TACTICAL MOVE — are one
    ///    march at three speeds, with the readiness cost that buys. They execute
    ///    the moment they are given.
    ///  • **The two contingencies** — WITHDRAW and RETREAT — are *not* executed
    ///    when ordered. The formation carries the objective and goes when its own
    ///    strength falls to the task's trigger. That is the point: a commander
    ///    cannot decide what happens when a battalion breaks *at the moment it
    ///    breaks*, so they decide beforehand and the formation carries it out.
    ///  • **The standing commands** — STOP, FREE MOVEMENT, AUTOMATIC ATTACK —
    ///    have no objective and never complete.
    ///
    /// **Why the contingencies are checked here and not in the mover.** A
    /// contingency is a property of the *order*, not of the march: the unit is
    /// standing still with a line drawn behind it, and something has to be
    /// watching its strength. Putting the watch in `UnitMover` would mean a
    /// component that only moves things also owning the decision to start.
    ///
    /// See docs/15-COMBAT-ORDERS.md.
    /// </summary>
    public class ManoeuvreOrderSystem : MonoBehaviour
    {
        /// <summary>HUD line.</summary>
        public System.Action<string> Flash;

        /// <summary>Seconds between strength checks and free-movement decisions.</summary>
        const float TickSeconds = 1.0f;
        /// <summary>How far a free-moving formation goes in one hop, km.</summary>
        const double FreeHopMinKm = 2.0, FreeHopMaxKm = 9.0;
        /// <summary>Seconds a free-moving formation waits between hops, on the scenario clock.</summary>
        const float FreeHopIdleSeconds = 25f;

        TaskAreaSystem _areas;
        float _timer;

        /// <summary>One formation's standing contingency: where it goes, and when.</summary>
        class Contingency
        {
            public MoveTask task;
            public double lat, lon;
            public bool executed;
        }

        readonly Dictionary<string, Contingency> _contingencies = new Dictionary<string, Contingency>();
        /// <summary>Seconds a free-moving formation has been idle, keyed by unit.</summary>
        readonly Dictionary<string, float> _idle = new Dictionary<string, float>();

        public void Init(TaskAreaSystem areas) => _areas = areas;

        // ------------------------------------------------------------ orders

        /// <summary>
        /// Gives a formation a movement task. The three plain moves march at
        /// once; the two contingencies draw their objective and wait.
        /// </summary>
        public bool Order(UnitActor unit, MoveTask task, double lat, double lon)
        {
            if (unit == null || !unit.IsAlive)
            {
                Flash?.Invoke("Select a formation before ordering a move.");
                return false;
            }

            var def = MoveTaskCatalog.Get(task);
            float axis = GeoUtils.BearingDeg(unit.State.latitude, unit.State.longitude, lat, lon);
            double km = GeoUtils.DistanceKm(unit.State.latitude, unit.State.longitude, lat, lon);

            if (def.isContingency)
            {
                _contingencies[unit.State.instanceId] = new Contingency
                {
                    task = task, lat = lat, lon = lon
                };

                // The graphic faces *back* along the axis for a withdrawal: the
                // line is between the formation and the ground it is giving up,
                // so it is laid across the way it came rather than across the
                // way it is going.
                _areas?.Show(unit, def.shape, def.marker, def.name,
                    lat, lon, ContingencyRadiusKm(unit, km), axis, def.tint, VfxId.TaskAreaMove);

                Flash?.Invoke(
                    $"{Designation(unit)} — {def.name} to a point {km:0.#} km away, " +
                    $"executed at {def.triggerStrength * 100f:0}% strength.");
                return true;
            }

            _areas?.Show(unit, def.shape, MarkerKind.Hold, def.name,
                lat, lon, ObjectiveRadiusKm(unit), axis, def.tint, VfxId.TaskAreaMove);

            unit.Mover.SpeedMultiplier = def.speedMultiplier;
            if (!unit.Mover.MoveTo(lat, lon))
            {
                // Outside battle the mover refuses; placing the counter is the
                // honest equivalent, and it is what the editor wants anyway.
                unit.SetPosition(lat, lon);
                unit.SetHeading(axis);
            }

            Flash?.Invoke($"{Designation(unit)} — {def.name}, {km:0.#} km at " +
                          $"{unit.Def.speedKmh * def.speedMultiplier:0} km/h.");
            return true;
        }

        /// <summary>
        /// What a formation is worth if it is caught mid-march, from the task it
        /// is running. Read by nothing yet — the damage model does not know a
        /// unit is moving — but the figure belongs with the task rather than
        /// being invented at the point it is finally needed.
        /// </summary>
        public float InTransitMultiplier(UnitActor unit)
        {
            if (unit == null || unit.Mover == null || !unit.Mover.IsMoving) return 1f;
            return Mathf.Clamp(unit.Mover.SpeedMultiplier <= 0.8f ? 1.15f
                             : unit.Mover.SpeedMultiplier >= 1.4f ? 0.55f : 1f, 0.2f, 1.5f);
        }

        // -------------------------------------------------- standing commands

        /// <summary>
        /// Cancels everything this formation is doing: its march, its
        /// contingency and every graphic either of them put on the map. It does
        /// **not** touch the standing switches — STOP means "stop what you are
        /// doing", not "forget what you are".
        /// </summary>
        public void Stop(UnitActor unit)
        {
            if (unit == null) return;

            unit.Mover.Cancel();
            unit.Mover.SpeedMultiplier = 1f;
            _contingencies.Remove(unit.State.instanceId);
            _idle.Remove(unit.State.instanceId);
            _areas?.ClearFor(unit);
            unit.State.status = nameof(UnitStatus.Idle);

            Flash?.Invoke($"{Designation(unit)} — all orders cancelled.");
        }

        /// <summary>
        /// Turns roaming on or off. Switching it on anchors the radius to where
        /// the formation stands *now*, so it works in the ground it was given
        /// rather than drifting across the map one hop at a time.
        /// </summary>
        public void SetFreeMovement(UnitActor unit, bool on)
        {
            if (unit == null) return;

            unit.State.freeMovement = on;
            if (on)
            {
                unit.State.freeMovementLatitude = unit.State.latitude;
                unit.State.freeMovementLongitude = unit.State.longitude;
            }
            _idle.Remove(unit.State.instanceId);

            Flash?.Invoke(on
                ? $"{Designation(unit)} — free movement on, {CommandInfo.FreeMovementRadiusKm:0} km of ground."
                : $"{Designation(unit)} — free movement off.");
        }

        public void SetAutomaticAttack(UnitActor unit, bool on)
        {
            if (unit == null) return;
            unit.State.automaticAttack = on;
            Flash?.Invoke(on
                ? $"{Designation(unit)} — engaging anything in range."
                : $"{Designation(unit)} — holding fire unless ordered.");
        }

        /// <summary>Forgets a formation entirely. Called when it dies or leaves the map.</summary>
        public void Forget(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            _contingencies.Remove(unitId);
            _idle.Remove(unitId);
        }

        public void CancelAll()
        {
            _contingencies.Clear();
            _idle.Clear();
        }

        // -------------------------------------------------------------- tick

        void Update()
        {
            if (!CombatSystem.BattleRunning) return;

            _timer += Time.deltaTime;
            if (_timer < TickSeconds) return;
            float dt = _timer;
            _timer = 0f;

            TickContingencies();
            TickFreeMovement(dt);
        }

        /// <summary>
        /// Sends anything that has fallen to its trigger. Checked once a second
        /// rather than per tick of combat: a contingency is a decision about a
        /// formation's state, and a state that changes between two combat ticks
        /// has not meaningfully changed.
        /// </summary>
        void TickContingencies()
        {
            foreach (var unit in UnitRegistry.All)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (!_contingencies.TryGetValue(unit.State.instanceId, out var pending)) continue;
                if (pending.executed) continue;

                var def = MoveTaskCatalog.Get(pending.task);
                if (unit.State.strength > def.triggerStrength) continue;

                pending.executed = true;
                unit.Mover.SpeedMultiplier = def.speedMultiplier;

                if (unit.Mover.MoveTo(pending.lat, pending.lon))
                {
                    unit.State.status = nameof(UnitStatus.Moving);
                    Flash?.Invoke($"{Designation(unit)} is {def.name.ToLowerInvariant()}ing — " +
                                  $"down to {unit.State.strength * 100f:0}% strength.");
                }
            }
        }

        /// <summary>
        /// Moves idle free-roaming formations about inside their allowance.
        ///
        /// **Only when genuinely idle**: not marching, not in contact, and with
        /// no contingency waiting. Free movement is the lowest-priority thing a
        /// formation does, and a unit that wandered off in the middle of a fight
        /// because a switch was on would be the switch overriding the battle.
        /// </summary>
        void TickFreeMovement(float dt)
        {
            foreach (var unit in UnitRegistry.All)
            {
                if (unit == null || !unit.IsAlive || !unit.State.freeMovement) continue;
                if (unit.Mover.IsMoving) { _idle.Remove(unit.State.instanceId); continue; }
                if (CombatSystem.InContact(unit)) { _idle.Remove(unit.State.instanceId); continue; }
                if (_contingencies.ContainsKey(unit.State.instanceId)) continue;

                string id = unit.State.instanceId;
                _idle.TryGetValue(id, out float waited);
                waited += dt;
                if (waited < FreeHopIdleSeconds) { _idle[id] = waited; continue; }
                _idle[id] = 0f;

                if (TryPickFreeHop(unit, out double lat, out double lon))
                {
                    unit.Mover.SpeedMultiplier = 1f;
                    unit.Mover.MoveTo(lat, lon);
                }
            }
        }

        /// <summary>
        /// A destination inside the formation's allowance. Tried a handful of
        /// times rather than solved: rejection sampling on a circle converges in
        /// one or two goes, and a closed-form answer would still have to be
        /// clamped against the anchor.
        /// </summary>
        static bool TryPickFreeHop(UnitActor unit, out double lat, out double lon)
        {
            lat = lon = 0;
            var s = unit.State;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                double bearing = UnityEngine.Random.Range(0f, 360f);
                double km = UnityEngine.Random.Range((float)FreeHopMinKm, (float)FreeHopMaxKm);
                GeoUtils.Destination(s.latitude, s.longitude, bearing, km, out lat, out lon);

                double fromAnchor = GeoUtils.DistanceKm(
                    s.freeMovementLatitude, s.freeMovementLongitude, lat, lon);
                if (fromAnchor <= CommandInfo.FreeMovementRadiusKm) return true;
            }
            return false;
        }

        // ----------------------------------------------------------- helpers

        /// <summary>
        /// Radius of a plain move's objective ring. Scaled off the formation's
        /// own bulk, so a division's objective is a piece of ground and a
        /// platoon's is a place.
        /// </summary>
        static double ObjectiveRadiusKm(UnitActor unit)
        {
            double bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            return System.Math.Min(6.0, System.Math.Max(0.5, 0.45 * System.Math.Sqrt(bulk)));
        }

        /// <summary>
        /// Half-frontage of a contingency's graphic. Wider than a move objective
        /// — a withdrawal line has to be wide enough to be behind the whole
        /// formation — and grown a little with the distance being given up.
        /// </summary>
        static double ContingencyRadiusKm(UnitActor unit, double distanceKm)
        {
            double bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            double km = 0.7 * System.Math.Sqrt(bulk) + distanceKm * 0.12;
            return System.Math.Min(20.0, System.Math.Max(1.0, km));
        }

        static string Designation(UnitActor u) =>
            string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
    }
}
