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
    /// The five offensive tasks the battle order bar offers. Add a row here
    /// rather than branching on <see cref="AttackTask"/> at a call site, and
    /// update docs/15-COMBAT-ORDERS.md in the same change.
    /// </summary>
    public static class AttackTaskCatalog
    {
        // Colours are intent, not decoration: an assault is the hottest thing
        // on the map, suppression is amber because it is about holding the
        // target down, and an ambush is muted because it is not moving.
        static readonly Color Hot = new Color(1.00f, 0.45f, 0.25f);
        static readonly Color Deliberate = new Color(1.00f, 0.68f, 0.28f);
        static readonly Color Amber = new Color(0.95f, 0.83f, 0.30f);
        static readonly Color Concealed = new Color(0.62f, 0.55f, 0.85f);
        static readonly Color Riposte = new Color(0.45f, 0.85f, 0.95f);

        static readonly AttackTaskDef[] Defs =
        {
            new AttackTaskDef
            {
                task = AttackTask.Attack,
                name = "ATTACK",
                detail = "Close and destroy",
                engageRangeFraction = 0.85f,
                damageMultiplier = 1.0f,
                shockMultiplier = 1.0f,
                returnFireMultiplier = 1.0f,
                advances = true,
                openingMultiplier = 1.0f,
                arrowTint = Deliberate
            },

            new AttackTaskDef
            {
                task = AttackTask.Assault,
                name = "ASSAULT",
                detail = "Close right up — decisive, costly",
                // Onto the objective, not near it. At this range both sides are
                // fully exposed, which is why return fire is worse than normal.
                engageRangeFraction = 0.22f,
                damageMultiplier = 1.85f,
                shockMultiplier = 1.4f,
                returnFireMultiplier = 1.45f,
                advances = true,
                openingMultiplier = 1.25f,
                // The ground the assault goes in over catches fire and stays lit.
                openingEffect = VfxId.GroundFire,
                openingEffectSeconds = 20f,
                arrowTint = Hot
            },

            new AttackTaskDef
            {
                task = AttackTask.Suppress,
                name = "SUPPRESS",
                detail = "Pin from maximum range",
                engageRangeFraction = 1.0f,
                // Suppression is not attrition: it takes very little strength
                // off the target and a great deal of its ability to function.
                damageMultiplier = 0.40f,
                shockMultiplier = 2.6f,
                returnFireMultiplier = 0.55f,
                advances = true,
                openingMultiplier = 1.0f,
                pins = true,
                openingEffect = VfxId.SmokeScreen,
                openingEffectSeconds = 0f,          // hangs for as long as the order does
                arrowTint = Amber
            },

            new AttackTaskDef
            {
                task = AttackTask.Ambush,
                name = "AMBUSH",
                detail = "Hold concealed, strike on contact",
                engageRangeFraction = 0.75f,
                damageMultiplier = 1.15f,
                shockMultiplier = 1.8f,
                returnFireMultiplier = 0.85f,
                // The whole task is *not* moving. Giving away the position by
                // advancing on the target would be the one thing an ambush cannot do.
                advances = false,
                openingMultiplier = 2.4f,
                openingIsFree = true,
                arrowTint = Concealed
            },

            new AttackTaskDef
            {
                task = AttackTask.Counterattack,
                name = "COUNTERATTACK",
                detail = "Strike a committed enemy",
                engageRangeFraction = 0.70f,
                damageMultiplier = 1.35f,
                shockMultiplier = 1.5f,
                returnFireMultiplier = 0.9f,
                advances = true,
                // A formation already committed to its own attack is not set to
                // receive one; that is worth a heavy opening blow.
                openingMultiplier = 1.6f,
                arrowTint = Riposte
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
