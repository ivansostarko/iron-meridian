using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// What one movement task does, in numbers. Everything separating a road
    /// march from a bounding advance lives in this row rather than in
    /// <see cref="UnitMover"/> — the mover runs one march for all of them, and
    /// the table is what makes them different.
    /// </summary>
    public class MoveTaskDef
    {
        public MoveTask task;

        /// <summary>Menu caption and the one line under it.</summary>
        public string name;
        public string detail;

        /// <summary>
        /// Multiplier on the formation's own <c>speedKmh</c>. A road march is
        /// the fastest a formation travels and the worst state to be caught in;
        /// a tactical bound is the opposite.
        /// </summary>
        public float speedMultiplier;

        /// <summary>
        /// What the formation is worth if it is engaged *while moving*, as a
        /// multiplier on combat power. This is the price of speed: a column at
        /// road-march pace is strung out and is not fighting anybody.
        /// </summary>
        public float inTransitCombatMultiplier;

        /// <summary>
        /// True for the two tasks that are **not executed when ordered**. They
        /// are contingencies: the formation carries the objective and goes when
        /// its own strength falls to <see cref="triggerStrength"/>.
        /// </summary>
        public bool isContingency;

        /// <summary>Strength at or below which a contingency executes itself. 0..1.</summary>
        public float triggerStrength;

        /// <summary>How the objective is drawn on the map — see <see cref="TaskAreaSystem"/>.</summary>
        public TaskAreaShape shape;

        /// <summary>Marker pinned at the objective, for the contingency tasks.</summary>
        public MarkerKind marker;

        /// <summary>Colour of this task's graphics and its trail.</summary>
        public Color tint;
    }

    /// <summary>
    /// The five movement tasks the battle order bar offers.
    ///
    /// **Three are moves and two are plans.** MOVE, FAST MOVE and TACTICAL MOVE
    /// are ordered and executed at once, and differ only in the trade between
    /// speed and readiness — the whole of the choice is "how much of a hurry am
    /// I in, and what am I willing to be caught as". WITHDRAW and RETREAT are
    /// not journeys the player is ordering now: they are a line and a rally
    /// point the formation will take *itself* to when it has been hurt enough.
    /// Giving them to a fresh formation is how you decide in advance what
    /// happens when it breaks, which is the one decision a commander cannot make
    /// in the moment it is needed.
    ///
    /// Add a row here rather than branching on <see cref="MoveTask"/> at a call
    /// site, and update docs/15-COMBAT-ORDERS.md in the same change.
    /// </summary>
    public static class MoveTaskCatalog
    {
        // Movement runs cool — blues and greys — so a march never reads as an
        // attack. The two contingencies are amber and red because they are
        // states of a fight going badly.
        static readonly Color March = new Color(0.55f, 0.78f, 0.95f);
        static readonly Color Road = new Color(0.45f, 0.90f, 0.85f);
        static readonly Color Bound = new Color(0.62f, 0.72f, 0.88f);
        static readonly Color Amber = new Color(0.95f, 0.72f, 0.30f);
        static readonly Color Alarm = new Color(0.95f, 0.42f, 0.36f);

        static readonly MoveTaskDef[] Defs =
        {
            new MoveTaskDef
            {
                task = MoveTask.Move,
                name = "MOVE",
                detail = "March to a point",
                speedMultiplier = 1.0f,
                inTransitCombatMultiplier = 1.0f,
                shape = TaskAreaShape.Ring,
                tint = March
            },

            new MoveTaskDef
            {
                task = MoveTask.FastMove,
                name = "FAST MOVE",
                detail = "Road march — quick, strung out",
                speedMultiplier = 1.65f,
                // The cost of the speed, and the reason this is not simply the
                // better option: a column caught at road-march pace fights at
                // half weight.
                inTransitCombatMultiplier = 0.55f,
                shape = TaskAreaShape.Ring,
                tint = Road
            },

            new MoveTaskDef
            {
                task = MoveTask.TacticalMove,
                name = "TACTICAL MOVE",
                detail = "Bounding advance — slow, ready",
                speedMultiplier = 0.6f,
                // Better than standing still: the formation is moving in contact
                // formation, covering its own bounds.
                inTransitCombatMultiplier = 1.15f,
                shape = TaskAreaShape.Ring,
                tint = Bound
            },

            new MoveTaskDef
            {
                task = MoveTask.Withdraw,
                name = "WITHDRAW",
                detail = "Break contact at half strength",
                speedMultiplier = 1.25f,
                inTransitCombatMultiplier = 0.75f,
                isContingency = true,
                triggerStrength = CommandInfo.WithdrawTriggerStrength,
                // A line, not a point: a withdrawal is *to* somewhere behind you,
                // and what matters is the line you are getting behind.
                shape = TaskAreaShape.Line,
                marker = MarkerKind.Withdraw,
                tint = Amber
            },

            new MoveTaskDef
            {
                task = MoveTask.Retreat,
                name = "RETREAT",
                detail = "Fall back at a third strength",
                // Faster than a withdrawal and less orderly: this is the one
                // that happens when the formation has stopped being useful.
                speedMultiplier = 1.5f,
                inTransitCombatMultiplier = 0.45f,
                isContingency = true,
                triggerStrength = CommandInfo.RetreatTriggerStrength,
                // A rally point, so a ring: everyone is heading for one place.
                shape = TaskAreaShape.Ring,
                marker = MarkerKind.Retreat,
                tint = Alarm
            }
        };

        static Dictionary<MoveTask, MoveTaskDef> _byTask;

        public static MoveTaskDef Get(MoveTask task)
        {
            if (_byTask == null)
            {
                _byTask = new Dictionary<MoveTask, MoveTaskDef>(Defs.Length);
                foreach (var d in Defs) _byTask[d.task] = d;
            }
            return _byTask.TryGetValue(task, out var def) ? def : Defs[0];
        }

        public static IReadOnlyList<MoveTaskDef> All => Defs;
    }
}
