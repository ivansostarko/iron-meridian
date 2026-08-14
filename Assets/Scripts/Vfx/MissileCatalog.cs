using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>Whose inventory a missile system belongs to. Matches <see cref="ArtilleryOrigin"/>.</summary>
    public enum MissileOrigin
    {
        Nato,
        Enemy
    }

    /// <summary>
    /// What a system is for. This is the honest distinction, and it is the one
    /// the panel groups by — an air-defence battery and a ballistic missile are
    /// not two sizes of the same thing.
    /// </summary>
    public enum MissileRole
    {
        /// <summary>Air and missile defence. Engages what is in the air over an area.</summary>
        AirDefence,
        /// <summary>Surface-to-surface fires against a point on the ground.</summary>
        SurfaceStrike
    }

    /// <summary>Weight class, which decides the impact effect and the report.</summary>
    public enum MissileWeight
    {
        Light,
        Medium,
        Heavy
    }

    /// <summary>One missile system: what it reaches, what it does at the other end.</summary>
    public class MissileSystemDef
    {
        public MissileSystemId id;
        public MissileOrigin origin;
        public MissileRole role;
        public MissileWeight weight;

        /// <summary>Button caption — the designation, as it is actually called.</summary>
        public string label;
        /// <summary>Full name for messages and the countdown banner.</summary>
        public string name;
        /// <summary>One line under the label: what it is and what it is for.</summary>
        public string detail;

        /// <summary>
        /// Radius of the destruction area in metres — the ring drawn on the map
        /// and the outer edge of the blast.
        ///
        /// For a surface-to-surface system this is the area the warhead covers.
        /// For an air-defence system it is the **engagement footprint** it can
        /// clear, which is a much larger circle and a very different claim; the
        /// panel says which is which so the two numbers are never confused.
        /// </summary>
        public float radiusMeters;

        /// <summary>Ground range the missile is flown in over, in kilometres.</summary>
        public float approachKm;
        /// <summary>Peak altitude of the trajectory, in metres above the target's ground.</summary>
        public float apogeeMeters;
        /// <summary>Seconds from launch to impact, once the countdown has run out.</summary>
        public float flightSeconds;
        /// <summary>Length the missile body is drawn at, in metres. Exaggerated for map legibility.</summary>
        public float bodyMeters;

        /// <summary>Impact effects and how long the smoke hangs.</summary>
        public VfxId burst;
        public VfxId smoke;
        public float smokeSeconds;
        public float burstScale;

        /// <summary>Ground fire left burning at the impact point.</summary>
        public VfxId fire;
        public float fireSeconds;

        // --- what the warhead does to a formation (see BlastDamage) ---

        /// <summary>Inside this, a formation is destroyed outright. Metres.</summary>
        public float lethalRadiusM;
        /// <summary>Outer edge of the damage falloff. Metres.</summary>
        public float blastRadiusM;
        /// <summary>Strength removed at the lethal edge, before the square falloff.</summary>
        public float maxDamage;

        /// <summary>Colour of the destruction ring, the marker and the countdown banner.</summary>
        public Color markerColor;
    }

    /// <summary>Every system the missile panel offers.</summary>
    public enum MissileSystemId
    {
        // --- NATO ---
        Patriot,
        SampT,
        Nasams,
        Thaad,
        Himars,

        // --- Enemy ---
        S400,
        Iskander,
        Hq9,
        Df26,
        Bavar373
    }

    /// <summary>
    /// The single source of truth for missile systems. The MISSILE SYSTEMS
    /// panel, the destruction ring, the flight and the impact are all driven
    /// from these rows — add a system here rather than special-casing one in the
    /// UI, and update docs/20-MISSILE-SYSTEMS.md in the same change.
    ///
    /// **On the numbers.** Ranges, footprints and warhead weights for these
    /// systems are published in wildly inconsistent forms and most of the
    /// interesting figures are not public at all. What is here is deliberately
    /// *game* tuning: the systems are in the right order relative to each other,
    /// the footprints are legible at map scale, and nothing claims more
    /// precision than that. Treat the rows as a balance table, not as a
    /// reference work.
    /// </summary>
    public static class MissileCatalog
    {
        /// <summary>
        /// Seconds between tasking and launch — ten, the same as artillery, air
        /// and UAV strikes, for the same reason: the ground is committed to
        /// before anything happens, and one countdown across every fires system
        /// means the player learns it once.
        /// </summary>
        public const float CountdownSeconds = 10f;

        static readonly Color Interceptor = new Color(0.42f, 0.78f, 1.00f);
        static readonly Color AreaDefence = new Color(0.35f, 0.62f, 0.98f);
        static readonly Color Strike = new Color(1.00f, 0.62f, 0.24f);
        static readonly Color HeavyStrike = new Color(1.00f, 0.36f, 0.20f);
        static readonly Color EnemyDefence = new Color(0.94f, 0.48f, 0.42f);
        static readonly Color EnemyStrike = new Color(0.96f, 0.30f, 0.26f);

        static readonly MissileSystemDef[] Defs =
        {
            // ------------------------------------------------------- NATO

            new MissileSystemDef
            {
                id = MissileSystemId.Patriot, origin = MissileOrigin.Nato,
                role = MissileRole.AirDefence, weight = MissileWeight.Medium,
                label = "PATRIOT",
                name = "MIM-104 Patriot (PAC-3 MSE)",
                detail = "Area air and missile defence — hit-to-kill interceptor",
                radiusMeters = 2600f,
                approachKm = 14f, apogeeMeters = 7000f, flightSeconds = 5.0f, bodyMeters = 200f,
                burst = VfxId.MissileMediumBurst, smoke = VfxId.MissileMediumSmoke,
                smokeSeconds = 20f, burstScale = 1.0f,
                fire = VfxId.GroundFire, fireSeconds = 18f,
                lethalRadiusM = 60f, blastRadiusM = 260f, maxDamage = 0.55f,
                markerColor = AreaDefence
            },

            new MissileSystemDef
            {
                id = MissileSystemId.SampT, origin = MissileOrigin.Nato,
                role = MissileRole.AirDefence, weight = MissileWeight.Medium,
                label = "SAMP/T NG",
                name = "SAMP/T NG — Aster 30",
                detail = "European area defence — long-reach Aster 30 rounds",
                radiusMeters = 3000f,
                approachKm = 16f, apogeeMeters = 8000f, flightSeconds = 5.4f, bodyMeters = 210f,
                burst = VfxId.MissileMediumBurst, smoke = VfxId.MissileMediumSmoke,
                smokeSeconds = 20f, burstScale = 1.05f,
                fire = VfxId.GroundFire, fireSeconds = 18f,
                lethalRadiusM = 62f, blastRadiusM = 270f, maxDamage = 0.55f,
                markerColor = AreaDefence
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Nasams, origin = MissileOrigin.Nato,
                role = MissileRole.AirDefence, weight = MissileWeight.Light,
                label = "NASAMS",
                name = "NASAMS — AMRAAM-ER",
                detail = "Short/medium point defence — protects one position well",
                radiusMeters = 1300f,
                approachKm = 8f, apogeeMeters = 3200f, flightSeconds = 3.6f, bodyMeters = 130f,
                burst = VfxId.MissileLightBurst, smoke = VfxId.MissileLightSmoke,
                smokeSeconds = 12f, burstScale = 0.85f,
                fire = VfxId.GroundFire, fireSeconds = 10f,
                lethalRadiusM = 30f, blastRadiusM = 130f, maxDamage = 0.40f,
                markerColor = Interceptor
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Thaad, origin = MissileOrigin.Nato,
                role = MissileRole.AirDefence, weight = MissileWeight.Heavy,
                label = "THAAD",
                name = "THAAD — terminal high-altitude defence",
                detail = "Exo-atmospheric intercept — the widest umbrella here",
                radiusMeters = 5200f,
                approachKm = 26f, apogeeMeters = 16000f, flightSeconds = 7.0f, bodyMeters = 280f,
                burst = VfxId.MissileHeavyBurst, smoke = VfxId.MissileHeavySmoke,
                smokeSeconds = 30f, burstScale = 1.1f,
                fire = VfxId.GroundFire, fireSeconds = 22f,
                lethalRadiusM = 90f, blastRadiusM = 380f, maxDamage = 0.62f,
                markerColor = Interceptor
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Himars, origin = MissileOrigin.Nato,
                role = MissileRole.SurfaceStrike, weight = MissileWeight.Medium,
                label = "HIMARS",
                name = "HIMARS — ATACMS / PrSM",
                detail = "Precision deep fires — one launcher, one target, no warning",
                radiusMeters = 420f,
                approachKm = 22f, apogeeMeters = 12000f, flightSeconds = 6.2f, bodyMeters = 230f,
                burst = VfxId.MissileMediumBurst, smoke = VfxId.MissileMediumSmoke,
                smokeSeconds = 24f, burstScale = 1.25f,
                fire = VfxId.GroundFire, fireSeconds = 26f,
                lethalRadiusM = 95f, blastRadiusM = 420f, maxDamage = 0.80f,
                markerColor = Strike
            },

            // ------------------------------------------------------ ENEMY

            new MissileSystemDef
            {
                id = MissileSystemId.S400, origin = MissileOrigin.Enemy,
                role = MissileRole.AirDefence, weight = MissileWeight.Heavy,
                label = "S-400",
                name = "S-400 Triumf",
                detail = "Layered area defence — the reference threat umbrella",
                radiusMeters = 4200f,
                approachKm = 22f, apogeeMeters = 12000f, flightSeconds = 6.2f, bodyMeters = 250f,
                burst = VfxId.MissileHeavyBurst, smoke = VfxId.MissileHeavySmoke,
                smokeSeconds = 28f, burstScale = 1.0f,
                fire = VfxId.GroundFire, fireSeconds = 20f,
                lethalRadiusM = 80f, blastRadiusM = 340f, maxDamage = 0.60f,
                markerColor = EnemyDefence
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Iskander, origin = MissileOrigin.Enemy,
                role = MissileRole.SurfaceStrike, weight = MissileWeight.Heavy,
                label = "ISKANDER-M",
                name = "9K720 Iskander-M",
                detail = "Theatre ballistic strike — manoeuvring, hard to call early",
                radiusMeters = 620f,
                approachKm = 30f, apogeeMeters = 20000f, flightSeconds = 7.4f, bodyMeters = 300f,
                burst = VfxId.MissileHeavyBurst, smoke = VfxId.MissileHeavySmoke,
                smokeSeconds = 34f, burstScale = 1.5f,
                fire = VfxId.GroundFire, fireSeconds = 34f,
                lethalRadiusM = 150f, blastRadiusM = 620f, maxDamage = 0.95f,
                markerColor = EnemyStrike
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Hq9, origin = MissileOrigin.Enemy,
                role = MissileRole.AirDefence, weight = MissileWeight.Medium,
                label = "HQ-9B",
                name = "HQ-9B",
                detail = "Long-range area defence — the export umbrella",
                radiusMeters = 3400f,
                approachKm = 18f, apogeeMeters = 9000f, flightSeconds = 5.6f, bodyMeters = 220f,
                burst = VfxId.MissileMediumBurst, smoke = VfxId.MissileMediumSmoke,
                smokeSeconds = 22f, burstScale = 1.05f,
                fire = VfxId.GroundFire, fireSeconds = 18f,
                lethalRadiusM = 66f, blastRadiusM = 290f, maxDamage = 0.56f,
                markerColor = EnemyDefence
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Df26, origin = MissileOrigin.Enemy,
                role = MissileRole.SurfaceStrike, weight = MissileWeight.Heavy,
                label = "DF-26",
                name = "DF-26 Dongfeng",
                detail = "Intermediate-range strike — the heaviest warhead available",
                radiusMeters = 900f,
                approachKm = 38f, apogeeMeters = 30000f, flightSeconds = 8.6f, bodyMeters = 360f,
                burst = VfxId.MissileHeavyBurst, smoke = VfxId.MissileHeavySmoke,
                smokeSeconds = 42f, burstScale = 2.0f,
                fire = VfxId.GroundFire, fireSeconds = 45f,
                lethalRadiusM = 220f, blastRadiusM = 900f, maxDamage = 1.00f,
                markerColor = EnemyStrike
            },

            new MissileSystemDef
            {
                id = MissileSystemId.Bavar373, origin = MissileOrigin.Enemy,
                role = MissileRole.AirDefence, weight = MissileWeight.Light,
                label = "BAVAR-373",
                name = "Bavar-373",
                detail = "Indigenous area defence — shorter reach, mobile",
                radiusMeters = 1900f,
                approachKm = 11f, apogeeMeters = 5000f, flightSeconds = 4.4f, bodyMeters = 170f,
                burst = VfxId.MissileLightBurst, smoke = VfxId.MissileLightSmoke,
                smokeSeconds = 14f, burstScale = 0.95f,
                fire = VfxId.GroundFire, fireSeconds = 12f,
                lethalRadiusM = 44f, blastRadiusM = 190f, maxDamage = 0.46f,
                markerColor = EnemyDefence
            }
        };

        public static IReadOnlyList<MissileSystemDef> All => Defs;

        static Dictionary<MissileSystemId, MissileSystemDef> _byId;

        public static MissileSystemDef Get(MissileSystemId id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<MissileSystemId, MissileSystemDef>(Defs.Length);
                foreach (var d in Defs) _byId[d.id] = d;
            }
            return _byId.TryGetValue(id, out var def) ? def : Defs[0];
        }

        /// <summary>Systems of one inventory, in catalogue order.</summary>
        public static IEnumerable<MissileSystemDef> OfOrigin(MissileOrigin origin)
        {
            foreach (var d in Defs)
                if (d.origin == origin) yield return d;
        }

        /// <summary>"2.6 km" / "420 m" — the radius as it should appear on a button.</summary>
        public static string RadiusText(MissileSystemDef def) =>
            def.radiusMeters >= 1000f
                ? (def.radiusMeters / 1000f).ToString("0.#") + " km"
                : def.radiusMeters.ToString("0") + " m";
    }
}
