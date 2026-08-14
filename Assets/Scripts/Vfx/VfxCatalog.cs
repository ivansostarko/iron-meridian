using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Audio;

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
        Dust,

        // --- artillery (see docs/17-ARTILLERY.md) ---
        // One burst and one smoke per nature. They are separate ids rather than
        // one scaled effect because the natures genuinely do not look alike: a
        // 105 mm round is a bright crack, a 120 mm mortar bomb is a column of
        // soil, and a 203 mm shell is a fireball. Scaling one effect four ways
        // would make them all the same event at four sizes.

        /// <summary>105 mm round landing — sharp, bright, little soil.</summary>
        ArtilleryLightBurst,
        /// <summary>120 mm mortar bomb landing — steep, narrow column of earth.</summary>
        ArtilleryMortarBurst,
        /// <summary>155 mm round landing — the standard HE burst.</summary>
        ArtilleryMediumBurst,
        /// <summary>203 mm round landing — heavy fireball with a debris throw.</summary>
        ArtilleryHeavyBurst,

        /// <summary>Thin pale smoke off a 105 mm burst — looping until dispersed.</summary>
        ArtilleryLightSmoke,
        /// <summary>Brown soil haze off a mortar bomb — looping until dispersed.</summary>
        ArtilleryMortarSmoke,
        /// <summary>Grey-black smoke off a 155 mm burst — looping until dispersed.</summary>
        ArtilleryMediumSmoke,
        /// <summary>Heavy oily column off a 203 mm burst — looping until dispersed.</summary>
        ArtilleryHeavySmoke,

        // --- air strikes (see docs/18-AIR-STRIKES.md) ---

        /// <summary>Air-dropped weapon landing — the largest blast in the game.</summary>
        AerialBombBurst,
        /// <summary>Black column off an air-dropped weapon — looping until dispersed.</summary>
        AerialBombSmoke,

        // --- UAV strikes (see docs/19-UAV-STRIKES.md) ---

        /// <summary>Loitering-munition warhead — small, sharp, precise.</summary>
        UavWarheadBurst,
        /// <summary>Thin smoke off a drone warhead — looping until dispersed.</summary>
        UavWarheadSmoke,

        /// <summary>Shahed-class warhead — a heavy one-way drone, not a shell.</summary>
        ShahedWarheadBurst,
        /// <summary>Oily black column off a Shahed warhead — looping until dispersed.</summary>
        ShahedWarheadSmoke,
        /// <summary>Burning ground left where a one-way drone went in — looping.</summary>
        ShahedWreckFire,

        // --- missile systems (see docs/20-MISSILE-SYSTEMS.md) ---

        /// <summary>Interceptor/short-range missile impact — fast, bright, contained.</summary>
        MissileLightBurst,
        /// <summary>Theatre missile impact — the standard heavy warhead.</summary>
        MissileMediumBurst,
        /// <summary>IRBM / heavy ballistic impact — the largest detonation in the game.</summary>
        MissileHeavyBurst,

        /// <summary>Smoke off a light missile impact — looping until dispersed.</summary>
        MissileLightSmoke,
        /// <summary>Smoke off a medium missile impact — looping until dispersed.</summary>
        MissileMediumSmoke,
        /// <summary>Towering column off a heavy missile impact — looping until dispersed.</summary>
        MissileHeavySmoke,

        /// <summary>Exhaust plume trailing a missile in flight — looping, killed on impact.</summary>
        MissileTrail
    }

    /// <summary>Which procedural builder stands in when no prefab is available.</summary>
    public enum VfxFallback
    {
        Explosion,
        Impact,
        Fire,
        Smoke,
        Dust,
        /// <summary>High-order round: white-hot flash, flat shrapnel ring, minimal soil.</summary>
        ArtilleryAirBurst,
        /// <summary>Mortar bomb: narrow near-vertical column of earth, small flash.</summary>
        ArtilleryDirtColumn,
        /// <summary>Heavy shell: fireball, ground shock ring and arcing debris.</summary>
        ArtilleryHeavyBlast
    }

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

        /// <summary>
        /// Positional audio played with the effect. Looping sounds live as long
        /// as the effect does; one-shots fire once. See docs/10-AUDIO.md.
        /// </summary>
        public EffectSound sound = EffectSound.None;

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
                         tint = new Color(1.00f, 0.62f, 0.20f), priority = 100,
                         sound = EffectSound.Explosion },

            new VfxDef { id = VfxId.ImpactBurst, prefabPath = null,
                         fallback = VfxFallback.Impact,    scaleMeters = 110f, lifeSeconds = 1.1f,
                         tint = new Color(0.72f, 0.66f, 0.58f), priority = 40,
                         sound = EffectSound.Impact },

            new VfxDef { id = VfxId.WeaponFire,  prefabPath = null,
                         fallback = VfxFallback.Impact,    scaleMeters = 80f,  lifeSeconds = 0.7f,
                         tint = new Color(1.00f, 0.85f, 0.45f), priority = 20 },

            new VfxDef { id = VfxId.FireSmall,   prefabPath = "VFX/VFX_Fire_01_Small_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 100f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.55f, 0.15f), priority = 60,
                         sound = EffectSound.Fire },

            new VfxDef { id = VfxId.FireMedium,  prefabPath = "VFX/VFX_Fire_01_Medium_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 170f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.52f, 0.13f), priority = 70,
                         sound = EffectSound.Fire },

            new VfxDef { id = VfxId.FireLarge,   prefabPath = "VFX/VFX_Fire_01_Big_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 280f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.48f, 0.10f), priority = 80,
                         sound = EffectSound.Fire },

            new VfxDef { id = VfxId.GroundFire,  prefabPath = "VFX/VFX_Fire_Floor_01_Smoke",
                         fallback = VfxFallback.Fire,      scaleMeters = 230f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.45f, 0.12f), priority = 55,
                         sound = EffectSound.Fire },

            new VfxDef { id = VfxId.SmokePlume,  prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 300f, lifeSeconds = 0f,
                         tint = new Color(0.24f, 0.23f, 0.22f), priority = 50,
                         sound = EffectSound.Smoke },

            new VfxDef { id = VfxId.SmokeScreen, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 620f, lifeSeconds = 0f,
                         tint = new Color(0.72f, 0.72f, 0.70f), priority = 65,
                         sound = EffectSound.Smoke },

            new VfxDef { id = VfxId.Dust,        prefabPath = null,
                         fallback = VfxFallback.Dust,      scaleMeters = 140f, lifeSeconds = 1.5f,
                         tint = new Color(0.68f, 0.62f, 0.52f), priority = 10 },

            // --- artillery bursts (docs/17-ARTILLERY.md) ---
            // Priority above a plain explosion: a called fire mission is the
            // thing the player is watching, and must never be the effect the
            // concurrency budget throws away.
            new VfxDef { id = VfxId.ArtilleryLightBurst,  prefabPath = null,
                         fallback = VfxFallback.ArtilleryAirBurst,   scaleMeters = 210f, lifeSeconds = 2.2f,
                         tint = new Color(1.00f, 0.88f, 0.52f), priority = 120,
                         sound = EffectSound.ArtilleryLight },

            new VfxDef { id = VfxId.ArtilleryMortarBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryDirtColumn, scaleMeters = 190f, lifeSeconds = 2.8f,
                         tint = new Color(0.62f, 0.50f, 0.34f), priority = 120,
                         sound = EffectSound.ArtilleryMortar },

            new VfxDef { id = VfxId.ArtilleryMediumBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 300f, lifeSeconds = 3.0f,
                         tint = new Color(1.00f, 0.58f, 0.18f), priority = 125,
                         sound = EffectSound.ArtilleryMedium },

            new VfxDef { id = VfxId.ArtilleryHeavyBurst,  prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 430f, lifeSeconds = 3.6f,
                         tint = new Color(1.00f, 0.44f, 0.12f), priority = 130,
                         sound = EffectSound.ArtilleryHeavy },

            // --- artillery smoke ---
            // All loop (lifeSeconds = 0) and are dispersed explicitly by
            // ArtilleryStrikeSystem, the same way PlayWreck burns a wreck out.
            // Lower priority than the bursts: if the budget has to give, it
            // should give up lingering smoke rather than a round landing.
            new VfxDef { id = VfxId.ArtilleryLightSmoke,  prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 180f, lifeSeconds = 0f,
                         tint = new Color(0.78f, 0.78f, 0.76f), priority = 45,
                         sound = EffectSound.None },

            new VfxDef { id = VfxId.ArtilleryMortarSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 200f, lifeSeconds = 0f,
                         tint = new Color(0.55f, 0.45f, 0.32f), priority = 45,
                         sound = EffectSound.None },

            new VfxDef { id = VfxId.ArtilleryMediumSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 280f, lifeSeconds = 0f,
                         tint = new Color(0.32f, 0.31f, 0.30f), priority = 48,
                         sound = EffectSound.Smoke },

            new VfxDef { id = VfxId.ArtilleryHeavySmoke,  prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 380f, lifeSeconds = 0f,
                         tint = new Color(0.16f, 0.15f, 0.15f), priority = 52,
                         sound = EffectSound.Smoke },

            // --- air strike (docs/18-AIR-STRIKES.md) ---
            // The heaviest blast in the game, and the highest priority: an air
            // strike is a scheduled, watched event and must never be the thing
            // the concurrency budget discards.
            new VfxDef { id = VfxId.AerialBombBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 560f, lifeSeconds = 4.2f,
                         tint = new Color(1.00f, 0.50f, 0.14f), priority = 140,
                         sound = EffectSound.AerialBomb },

            new VfxDef { id = VfxId.AerialBombSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 460f, lifeSeconds = 0f,
                         tint = new Color(0.12f, 0.11f, 0.11f), priority = 56,
                         sound = EffectSound.Smoke },

            // --- UAV strike (docs/19-UAV-STRIKES.md) ---
            // Deliberately the smallest strike blast in the game. A loitering
            // munition carries a few kilograms of warhead, not a shell — reading
            // as smaller than a 60 mm mortar bomb is the honest depiction, and it
            // is what makes the drone a precision tool rather than a cheap
            // artillery substitute.
            new VfxDef { id = VfxId.UavWarheadBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryAirBurst, scaleMeters = 150f, lifeSeconds = 2.0f,
                         tint = new Color(1.00f, 0.80f, 0.42f), priority = 135,
                         sound = EffectSound.UavWarhead },

            new VfxDef { id = VfxId.UavWarheadSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 150f, lifeSeconds = 0f,
                         tint = new Color(0.40f, 0.38f, 0.36f), priority = 42,
                         sound = EffectSound.None },

            // --- Shahed-class one-way drone ---
            // Its own rows rather than a scaled UAV warhead: at fifty-odd
            // kilograms this is closer to a 155 mm shell than to the few
            // kilograms a tactical loitering munition carries, and the fire it
            // leaves behind is the part that reads on the map afterwards.

            new VfxDef { id = VfxId.ShahedWarheadBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 300f, lifeSeconds = 3.0f,
                         tint = new Color(1.00f, 0.62f, 0.26f), priority = 150,
                         sound = EffectSound.ShahedWarhead },

            new VfxDef { id = VfxId.ShahedWarheadSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 320f, lifeSeconds = 0f,
                         tint = new Color(0.22f, 0.21f, 0.20f), priority = 46,
                         sound = EffectSound.None },

            new VfxDef { id = VfxId.ShahedWreckFire, prefabPath = null,
                         fallback = VfxFallback.Fire,      scaleMeters = 180f, lifeSeconds = 0f,
                         tint = new Color(1.00f, 0.55f, 0.18f), priority = 60,
                         sound = EffectSound.Fire },

            // --- missile systems (docs/20-MISSILE-SYSTEMS.md) ---
            // Three weights rather than one per system: ten launchers firing the
            // same effect at three sizes would be honest, and ten distinct
            // effects would be ten effects nobody could tell apart. The weight
            // is what a player is actually choosing between.

            new VfxDef { id = VfxId.MissileLightBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryAirBurst, scaleMeters = 220f, lifeSeconds = 2.4f,
                         tint = new Color(1.00f, 0.86f, 0.48f), priority = 152,
                         sound = EffectSound.MissileLight },

            new VfxDef { id = VfxId.MissileMediumBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 420f, lifeSeconds = 3.4f,
                         tint = new Color(1.00f, 0.58f, 0.22f), priority = 158,
                         sound = EffectSound.MissileMedium },

            new VfxDef { id = VfxId.MissileHeavyBurst, prefabPath = null,
                         fallback = VfxFallback.ArtilleryHeavyBlast, scaleMeters = 760f, lifeSeconds = 4.6f,
                         tint = new Color(1.00f, 0.42f, 0.16f), priority = 165,
                         sound = EffectSound.MissileHeavy },

            new VfxDef { id = VfxId.MissileLightSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 240f, lifeSeconds = 0f,
                         tint = new Color(0.44f, 0.43f, 0.42f), priority = 44,
                         sound = EffectSound.None },

            new VfxDef { id = VfxId.MissileMediumSmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 440f, lifeSeconds = 0f,
                         tint = new Color(0.26f, 0.25f, 0.24f), priority = 48,
                         sound = EffectSound.None },

            new VfxDef { id = VfxId.MissileHeavySmoke, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 820f, lifeSeconds = 0f,
                         tint = new Color(0.16f, 0.15f, 0.15f), priority = 52,
                         sound = EffectSound.None },

            // Attached to the missile itself and killed on impact, so it has no
            // life of its own. Low priority: a trail is the first thing that
            // should be dropped when the concurrent budget is reached, because
            // losing it costs a flourish rather than an event.
            new VfxDef { id = VfxId.MissileTrail, prefabPath = null,
                         fallback = VfxFallback.Smoke,     scaleMeters = 90f, lifeSeconds = 0f,
                         tint = new Color(0.78f, 0.78f, 0.80f), priority = 30,
                         sound = EffectSound.None }
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
