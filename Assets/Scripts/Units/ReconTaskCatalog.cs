using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// What one reconnaissance task does, in numbers. The system runs the same
    /// loop for all five; this table is what makes them different.
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
    /// The five reconnaissance tasks the battle order bar offers. Add a row here
    /// rather than branching on <see cref="ReconTask"/> at a call site, and
    /// update docs/16-FOG-OF-WAR.md in the same change.
    /// </summary>
    public static class ReconTaskCatalog
    {
        static readonly Color Scout = new Color(0.45f, 0.85f, 0.70f);
        static readonly Color Route = new Color(0.55f, 0.80f, 0.95f);
        static readonly Color Static = new Color(0.75f, 0.85f, 0.55f);
        static readonly Color Air = new Color(0.65f, 0.75f, 1.00f);
        static readonly Color Fighting = new Color(0.95f, 0.70f, 0.45f);

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
            },

            new ReconTaskDef
            {
                task = ReconTask.ReconRoute,
                name = "RECON ROUTE",
                detail = "Scan the whole way there",
                // Narrower than an area search: it is covering a line, not a box.
                sensorRangeFactor = 1.4f,
                moves = true,
                scansWhileMoving = true,
                arrowTint = Route
            },

            new ReconTaskDef
            {
                task = ReconTask.Observe,
                name = "OBSERVE",
                detail = "Hold position, watch",
                // The furthest-seeing task, because a unit that is not moving,
                // not fighting and has chosen its ground sees further than one
                // doing anything else.
                sensorRangeFactor = 2.6f,
                moves = false,
                scansWhileMoving = true,
                arrowTint = Static
            },

            new ReconTaskDef
            {
                task = ReconTask.UavRecon,
                name = "UAV RECON",
                detail = "Fly a sensor out and back",
                sensorRangeFactor = 2.2f,
                moves = false,          // the unit stays; only the sensor flies
                scansWhileMoving = true,
                airborne = true,
                airborneSpeedKmh = 140f,
                airborneEnduranceSeconds = 90f,
                arrowTint = Air
            },

            new ReconTaskDef
            {
                task = ReconTask.CombatPatrol,
                name = "COMBAT PATROL",
                detail = "Patrol out and back, ready to fight",
                sensorRangeFactor = 1.5f,
                moves = true,
                scansWhileMoving = true,
                patrols = true,
                arrowTint = Fighting
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
