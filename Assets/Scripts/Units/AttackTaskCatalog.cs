using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// What one offensive task does, in numbers. Everything that separates an
    /// assault from a suppression lives in this row rather than in
    /// <see cref="AttackOrderSystem"/> — the system runs the same loop for all
    /// five, and the table is what makes them feel different.
    /// </summary>
    public class AttackTaskDef
    {
        public AttackTask task;

        /// <summary>Menu caption and the one line under it.</summary>
        public string name;
        public string detail;

        /// <summary>
        /// How close the attacker wants to be, as a fraction of its own weapon
        /// range. Suppression fires from the very edge of it; an assault closes
        /// right up, which is what makes it decisive and expensive.
        /// </summary>
        public float engageRangeFraction;

        /// <summary>Damage scaling against the ordinary exchange.</summary>
        public float damageMultiplier;

        /// <summary>
        /// Extra morale and organisation damage. Suppression barely scratches a
        /// formation's strength but wrecks its ability to act, which is the
        /// whole point of it.
        /// </summary>
        public float shockMultiplier;

        /// <summary>
        /// How hard the target hits back, as a fraction of a normal exchange.
        /// 0 means the attacker is not exposed at all this tick.
        /// </summary>
        public float returnFireMultiplier;

        /// <summary>
        /// Whether the attacker closes on the target. An ambush does not — it
        /// stays where it is and lets the target walk into range.
        /// </summary>
        public bool advances;

        /// <summary>
        /// One-off multiplier on the first volley. Surprise is worth a great
        /// deal once and nothing afterwards.
        /// </summary>
        public float openingMultiplier;

        /// <summary>The first volley draws no return fire — the target was not expecting it.</summary>
        public bool openingIsFree;

        /// <summary>Drives the target's status to Suppressed while the order runs.</summary>
        public bool pins;

        /// <summary>Obscuration laid on the target when the engagement opens; null for none.</summary>
        public VfxId? openingEffect;

        /// <summary>Seconds the opening effect burns/hangs before it is stopped; 0 = for the order's life.</summary>
        public float openingEffectSeconds;

        /// <summary>Colour of this task's approach arrow on the map.</summary>
        public Color arrowTint;
    }

    /// <summary>
    /// The offensive tasks the battle order bar offers — one, today. Add a row
    /// here rather than branching on <see cref="AttackTask"/> at a call site,
    /// and update docs/15-COMBAT-ORDERS.md in the same change.
    ///
    /// The def keeps every field the five tasks used (shock, return fire,
    /// opening volley, obscuration) because those are what a second task would
    /// be *made of*; the table having one row is a statement about the menu,
    /// not about the model underneath it.
    /// </summary>
    public static class AttackTaskCatalog
    {
        /// <summary>Attack orange — the one hot colour on the map, and it means this.</summary>
        static readonly Color Deliberate = new Color(1.00f, 0.68f, 0.28f);

        static readonly AttackTaskDef[] Defs =
        {
            new AttackTaskDef
            {
                task = AttackTask.Attack,
                name = "ATTACK",
                detail = "Close and destroy what is there",
                engageRangeFraction = 0.85f,
                damageMultiplier = 1.0f,
                shockMultiplier = 1.0f,
                returnFireMultiplier = 1.0f,
                advances = true,
                openingMultiplier = 1.0f,
                arrowTint = Deliberate
            }
        };

        static Dictionary<AttackTask, AttackTaskDef> _byTask;

        public static AttackTaskDef Get(AttackTask task)
        {
            if (_byTask == null)
            {
                _byTask = new Dictionary<AttackTask, AttackTaskDef>(Defs.Length);
                foreach (var d in Defs) _byTask[d.task] = d;
            }
            return _byTask.TryGetValue(task, out var def) ? def : Defs[0];
        }

        public static IReadOnlyList<AttackTaskDef> All => Defs;
    }
}
