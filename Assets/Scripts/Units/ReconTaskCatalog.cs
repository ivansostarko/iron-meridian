using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// What one reconnaissance task does, in numbers. The system runs one loop
    /// for every task; this table is what would make two of them different.
    /// </summary>
    public class ReconTaskDef
    {
        public ReconTask task;
        public string name;
        public string detail;

        /// <summary>
        /// Detection radius the task grants, as a multiple of the unit's own
        /// view range. Everything here is &gt; 1 — a unit given a recon task is
        /// looking rather than merely being somewhere.
        /// </summary>
        public float sensorRangeFactor;

        /// <summary>Whether the unit itself travels to the objective.</summary>
        public bool moves;

        /// <summary>
        /// True when the sensor rides the unit the whole way rather than
        /// switching on at the objective. That is the difference between
        /// reconnoitring a route and reconnoitring the area at the end of it.
        /// </summary>
        public bool scansWhileMoving;

        /// <summary>
        /// A sensor that flies to the objective on its own, ignoring terrain and
        /// the unit's speed, and expires. This is the UAV.
        /// </summary>
        public bool airborne;
        /// <summary>Ground speed of an airborne sensor, km/h.</summary>
        public float airborneSpeedKmh;
        /// <summary>Seconds an airborne sensor lasts before it goes home. 0 = no limit.</summary>
        public float airborneEnduranceSeconds;

        /// <summary>Patrols run out to the objective and back until cancelled.</summary>
        public bool patrols;

        /// <summary>Colour of this task's axis arrow on the map.</summary>
        public Color arrowTint;
    }

    /// <summary>
    /// The reconnaissance tasks the battle order bar offers — one, today. Add a
    /// row here rather than branching on <see cref="ReconTask"/> at a call site,
    /// and update docs/16-FOG-OF-WAR.md in the same change.
    ///
    /// The def keeps the fields the five tasks used — scanning on the move, an
    /// airborne sensor, patrolling — because those are what a second task would
    /// be made of. <see cref="ReconOrderSystem"/> still honours every one of
    /// them; the table simply has nothing that sets them yet.
    /// </summary>
    public static class ReconTaskCatalog
    {
        /// <summary>Recon green — the colour of looking rather than shooting.</summary>
        static readonly Color Scout = new Color(0.45f, 0.85f, 0.70f);

        static readonly ReconTaskDef[] Defs =
        {
            new ReconTaskDef
            {
                task = ReconTask.ReconArea,
                name = "RECON AREA",
                detail = "Move there and search it",
                sensorRangeFactor = 1.9f,
                moves = true,
                arrowTint = Scout
            }
        };

        static Dictionary<ReconTask, ReconTaskDef> _byTask;

        public static ReconTaskDef Get(ReconTask task)
        {
            if (_byTask == null)
            {
                _byTask = new Dictionary<ReconTask, ReconTaskDef>(Defs.Length);
                foreach (var d in Defs) _byTask[d.task] = d;
            }
            return _byTask.TryGetValue(task, out var def) ? def : Defs[0];
        }

        public static IReadOnlyList<ReconTaskDef> All => Defs;
    }
}
