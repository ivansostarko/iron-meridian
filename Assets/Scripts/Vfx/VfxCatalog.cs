using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Every particle effect the game can ask for, named by what it *means*
    /// rather than by which asset draws it. Call sites reference these; the
    /// catalogue below decides whether that resolves to a Vefects prefab or to
    /// a procedural stand-in.
    /// </summary>
    public enum VfxId
    {
        /// <summary>Unit destroyed / ammo dump hit — one-shot fireball.</summary>
        Explosion,
        /// <summary>Rounds landing on a unit taking damage — small one-shot puff.</summary>
        ImpactBurst,
        /// <summary>A unit shooting — brief muzzle//dust signature at the firer.</summary>
        WeaponFire,
        /// <summary>Company/battalion burning — looping.</summary>
        FireSmall,
        /// <summary>Brigade burning, or a struck vehicle park — looping.</summary>
        FireMedium,
        /// <summary>Division-scale conflagration, fuel/ammo fire — looping.</summary>
        FireLarge,
        /// <summary>Burning ground (wreck site, torched terrain) — looping, flat.</summary>
        GroundFire,
        /// <summary>Column of smoke rising off a wreck or fire — looping.</summary>
        SmokePlume,
        /// <summary>Deliberate obscuration laid by artillery/smoke generators — looping.</summary>
        SmokeScreen,
        /// <summary>Dust kicked up by movement or a deployment drop — one-shot.</summary>
        Dust
    }

    /// <summary>Which procedural builder stands in when no prefab is available.</summary>
    public enum VfxFallback { Explosion, Impact, Fire, Smoke, Dust }

    /// <summary>One catalogue row: what to spawn, how big, and for how long.</summary>
    public class VfxDef
    {
        public VfxId id;

        /// <summary>
        /// Resources path of the authored prefab, or null for procedural-only.
        /// Populate the Resources folder with Tools > Iron Meridian > Install VFX Prefabs.
        /// </summary>
        public string prefabPath;

        public VfxFallback fallback;

        /// <summary>
        /// Diameter in metres the effect should read as on the map. Authored
        /// prefabs and procedural builders are both normalised to roughly one
        /// unit, then scaled by this — a 2 m camp fire is invisible when the
        /// camera sits 20 km up, so strategic effects are deliberately huge.
        /// </summary>
        public float scaleMeters;

        /// <summary>Seconds before the effect self-destructs; 0 or less means it loops until stopped.</summary>
        public float lifeSeconds;

        /// <summary>Tint applied to procedural fallbacks (authored prefabs keep their own colours).</summary>
        public Color tint;

        /// <summary>
        /// Higher survives when the concurrent-effect budget forces an eviction.
        /// Explosions outrank ambient smoke.
        /// </summary>
        public int priority;

        public bool Loops => lifeSeconds <= 0f;
    }

    /// <summary>
    /// The single source of truth for what each effect looks like. Add a row
    /// here rather than tuning particle values at the call site, and update
    /// docs/08-PARTICLE-SYSTEMS.md in the same change.
    /// </summary>
    public static class VfxCatalog
    {
        // The Free Fire VFX pack (Assets/Vefects) ships fire and fire+smoke
        // prefabs only — there is no explosion, standalone smoke or dust prefab
        // in it, so those rows stay procedural until an effects pack that has
        // them is imported. See docs/08-PARTICLE-SYSTEMS.md.
        static readonly VfxDef[] Defs =
        {
            new VfxDef { id = VfxId.Explosion,   prefabPath = null,
                         fallback = VfxFallback.Explosion, scaleMeters = 320f, lifeSeconds = 2.6f,
                         tint = new Color(1.00f, 0.62f, 0.20f), priority = 100 },

            new VfxDef { id = VfxId.ImpactBurst, prefabPath = null,
                         fallback = VfxFallback.Impact,    scaleMeters = 110f, lifeSeconds = 1.1f,
                         tint = new Color(0.72f, 0.66f, 0.58f), priority = 40 },

            new VfxDef { id = VfxId.WeaponFire,  prefabPath = null,
                         fallback = VfxFallback.Impact,    scaleMeters = 80f,  lifeSeconds = 0.7f,
                         tint = new Color(1.00f, 0.85f, 0.45f), priority = 20 },

            new VfxDef { id = VfxId.FireSmall,   prefabPath = "VFX/VFX_Fire_01_Small_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 100f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.55f, 0.15f), priority = 60 },

            new VfxDef { id = VfxId.FireMedium,  prefabPath = "VFX/VFX_Fire_01_Medium_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 170f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.52f, 0.13f), priority = 70 },

            new VfxDef { id = VfxId.FireLarge,   prefabPath = "VFX/VFX_Fire_01_Big_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 280f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.48f, 0.10f), priority = 80 },

            new VfxDef { id = VfxId.GroundFire,  prefabPath = "VFX/VFX_Fire_Floor_01_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 230f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.45f, 0.12f), priority = 55 },

            new VfxDef { id = VfxId.SmokePlume,  prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 300f, lifeSeconds = 0f,
                         tint = new Color(0.24f, 0.23f, 0.22f), priority = 50 },

            new VfxDef { id = VfxId.SmokeScreen, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 620f, lifeSeconds = 0f,
                         tint = new Color(0.72f, 0.72f, 0.70f), priority = 65 },

            new VfxDef { id = VfxId.Dust,        prefabPath = null,
                         fallback = VfxFallback.Dust,      scaleMeters = 140f, lifeSeconds = 1.5f,
                         tint = new Color(0.68f, 0.62f, 0.52f), priority = 10 }
        };

        static Dictionary<VfxId, VfxDef> _byId;

        public static VfxDef Get(VfxId id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<VfxId, VfxDef>(Defs.Length);
                foreach (var d in Defs) _byId[d.id] = d;
            }
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static IReadOnlyList<VfxDef> All => Defs;

        /// <summary>Fire severity for a unit at the given strength — bigger formations burn bigger.</summary>
        public static VfxId FireForScale(float scale01)
        {
            if (scale01 >= 0.66f) return VfxId.FireLarge;
            if (scale01 >= 0.33f) return VfxId.FireMedium;
            return VfxId.FireSmall;
        }
    }
}
