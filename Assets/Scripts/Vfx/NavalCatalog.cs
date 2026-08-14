using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Whose fleet a gun comes from. The same two-value split the artillery and
    /// missile catalogues use, and for the same reason: the game's sides are
    /// User and Enemy (<see cref="Data.Team"/>), so one inventory is NATO and
    /// the other is the Soviet/Chinese-pattern one the enemy fields. The detail
    /// line on each button still names the real mounting.
    /// </summary>
    public enum NavalOrigin
    {
        Nato,
        Enemy
    }

    /// <summary>
    /// Naval guns the fire-support menu can call on, named by mounting and
    /// calibre because that is how naval gunfire support is actually requested.
    /// </summary>
    public enum NavalGun
    {
        // --- NATO ---
        NatoMk110_57,
        NatoOto76,
        NatoMk45_127,
        NatoAgs155,
        // --- Enemy pattern: Russian ---
        EnemyAk176_76,
        EnemyAk100,
        EnemyAk130,
        // --- Enemy pattern: Chinese ---
        EnemyPj26_76,
        EnemyPj38_130
    }

    /// <summary>
    /// One naval gun: what it throws, how wide, how fast, and what that does to
    /// whatever is underneath.
    /// </summary>
    public class NavalGunDef
    {
        public NavalGun gun;
        public NavalOrigin origin;

        /// <summary>Bore in millimetres — the number the button leads with.</summary>
        public int calibreMm;

        /// <summary>Button caption.</summary>
        public string label;
        /// <summary>Full name for the countdown banner and messages.</summary>
        public string name;
        /// <summary>One line under the button: the real mounting and what it is for.</summary>
        public string detail;

        /// <summary>
        /// Radius of the target area in metres — the ring the player places on
        /// the map, and the ground the salvo is scattered across. A ship firing
        /// from over the horizon at a range no land gun matches does not put its
        /// rounds in a tighter group than a howitzer does, so these run wider
        /// than the equivalent field piece.
        /// </summary>
        public float radiusMeters;

        /// <summary>
        /// Rounds in one fire mission. Naval mountings are automatic and the
        /// difference between a 57 mm close-in gun and a 155 mm AGS is as much
        /// rate of fire as it is shell weight — so this is per gun rather than a
        /// constant, unlike the artillery catalogue's five.
        /// </summary>
        public int roundsPerMission;

        /// <summary>Seconds between rounds. The other half of rate of fire.</summary>
        public float roundIntervalSeconds;

        /// <summary>Burst effect for one round. See docs/08-PARTICLE-SYSTEMS.md.</summary>
        public VfxId burst;
        /// <summary>Smoke left behind by one round.</summary>
        public VfxId smoke;
        /// <summary>Seconds the smoke hangs before it is told to disperse.</summary>
        public float smokeSeconds;

        /// <summary>Extra scale on the burst, on top of the effect's own size.</summary>
        public float burstScale;

        /// <summary>Colour of the target-area marker and the countdown banner.</summary>
        public Color markerColor;

        // --- what a round does to a formation (see BlastDamage) ---
        //
        // Derived from calibre, exactly as ArtilleryDef derives its numbers and
        // for the same reason: charge mass, and therefore lethal area, scales
        // with the bore. Nine hand-tuned triples would be nine numbers to keep
        // plausible against each other and against the land guns.

        /// <summary>Inside this, a formation is destroyed outright. Metres.</summary>
        public float LethalRadiusM => calibreMm * 0.16f;

        /// <summary>Outer edge of the damage falloff. Metres.</summary>
        public float BlastRadiusM => calibreMm * 0.85f;

        /// <summary>Strength removed at the lethal edge, before the square falloff.</summary>
        public float MaxDamage => Mathf.Clamp(calibreMm / 700f, 0.06f, 0.40f);

        /// <summary>How long the whole mission takes to land, first round to last.</summary>
        public float SalvoSeconds => roundIntervalSeconds * Mathf.Max(0, roundsPerMission - 1);
    }

    /// <summary>
    /// The single source of truth for naval gunfire support. The left rail's
    /// NAVY STRIKE section, the target marker and the impact sequence are all
    /// driven from these rows — add a gun here rather than special-casing one in
    /// the UI, and update docs/21-NAVAL-GUNFIRE.md in the same change.
    ///
    /// **Why naval gunfire is not just more artillery.** It is the same physics
    /// and deliberately shares the same burst effects — a 127 mm shell landing
    /// is a 127 mm shell landing, whoever fired it, and inventing a second set of
    /// near-identical particle effects would be nine more things to keep in step
    /// for no gain the player can see. What differs is the *shape of the
    /// mission*, and that is where the character lives:
    ///
    ///  • **Rate of fire.** A Mk 110 puts 220 rounds a minute onto a point. No
    ///    land gun in the game fires anything like that, so the salvo is longer
    ///    and much faster — the strike reads as a hosing rather than as five
    ///    distinct impacts.
    ///  • **Dispersion.** Rounds arrive from a moving platform at extreme range,
    ///    so the beaten zone is wider than a howitzer's for the same calibre.
    ///  • **Availability.** It comes from a ship, so it does not care where the
    ///    player's guns are — but it spends the same shared allowance every
    ///    other called strike does (<see cref="StrikeBudget"/>).
    /// </summary>
    public static class NavalCatalog
    {
        /// <summary>
        /// Seconds between the call for fire and the first round landing. The
        /// same ten every other called strike uses, for the same reason: the
        /// ground is committed to before anything happens.
        /// </summary>
        public const float CountdownSeconds = 10f;

        // Marker colours run cool — steel blue through to a hot orange with
        // increasing weight. Deliberately a different family from the artillery
        // menu's yellows and reds, so a naval target area is identifiable on the
        // map as naval without reading the banner.
        static readonly Color Light = new Color(0.55f, 0.82f, 0.95f);
        static readonly Color Medium = new Color(0.42f, 0.70f, 0.98f);
        static readonly Color Heavy = new Color(0.55f, 0.60f, 0.95f);
        static readonly Color Siege = new Color(0.85f, 0.55f, 0.85f);

        static readonly NavalGunDef[] Defs =
        {
            // ------------------------------------------------------------ NATO
            new NavalGunDef
            {
                gun = NavalGun.NatoMk110_57, origin = NavalOrigin.Nato, calibreMm = 57,
                label = "57 mm", name = "57 mm Mk 110",
                detail = "Bofors, littoral combat ship — 220 rounds a minute",
                radiusMeters = 120f,
                // The extreme case of the rate-of-fire argument: a small shell,
                // a lot of them, very fast. It cannot break a dug-in formation
                // and it will not leave one alone either.
                roundsPerMission = 12, roundIntervalSeconds = 0.16f,
                burst = VfxId.ArtilleryLightBurst, smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 6f, burstScale = 0.42f,
                markerColor = Light
            },
            new NavalGunDef
            {
                gun = NavalGun.NatoOto76, origin = NavalOrigin.Nato, calibreMm = 76,
                label = "76 mm", name = "76 mm OTO Melara Super Rapid",
                detail = "Frigate main gun — the NATO workhorse",
                radiusMeters = 150f,
                roundsPerMission = 10, roundIntervalSeconds = 0.22f,
                burst = VfxId.ArtilleryLightBurst, smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 8f, burstScale = 0.60f,
                markerColor = Light
            },
            new NavalGunDef
            {
                gun = NavalGun.NatoMk45_127, origin = NavalOrigin.Nato, calibreMm = 127,
                label = "127 mm", name = "127 mm Mk 45 Mod 4",
                detail = "Five-inch destroyer gun — the standard NGFS mount",
                radiusMeters = 230f,
                roundsPerMission = 8, roundIntervalSeconds = 0.38f,
                burst = VfxId.ArtilleryMediumBurst, smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 14f, burstScale = 0.92f,
                markerColor = Medium
            },
            new NavalGunDef
            {
                gun = NavalGun.NatoAgs155, origin = NavalOrigin.Nato, calibreMm = 155,
                label = "155 mm", name = "155 mm Advanced Gun System",
                detail = "Zumwalt-class — the heaviest gun afloat",
                radiusMeters = 300f,
                roundsPerMission = 6, roundIntervalSeconds = 0.62f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 20f, burstScale = 1.20f,
                markerColor = Heavy
            },

            // -------------------------------------------------- Enemy: Russian
            new NavalGunDef
            {
                gun = NavalGun.EnemyAk176_76, origin = NavalOrigin.Enemy, calibreMm = 76,
                label = "76 mm", name = "76 mm AK-176",
                detail = "Corvette mount — Russian pattern, fast and light",
                radiusMeters = 155f,
                roundsPerMission = 10, roundIntervalSeconds = 0.20f,
                burst = VfxId.ArtilleryLightBurst, smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 8f, burstScale = 0.60f,
                markerColor = Light
            },
            new NavalGunDef
            {
                gun = NavalGun.EnemyAk100, origin = NavalOrigin.Enemy, calibreMm = 100,
                label = "100 mm", name = "100 mm AK-100",
                detail = "Frigate main gun — Russian pattern",
                radiusMeters = 190f,
                roundsPerMission = 9, roundIntervalSeconds = 0.30f,
                burst = VfxId.ArtilleryMediumBurst, smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 11f, burstScale = 0.76f,
                markerColor = Medium
            },
            new NavalGunDef
            {
                gun = NavalGun.EnemyAk130, origin = NavalOrigin.Enemy, calibreMm = 130,
                label = "130 mm", name = "130 mm AK-130",
                detail = "Twin automatic mount — 80 rounds a minute, both barrels",
                radiusMeters = 250f,
                // The twin mount is the point: more rounds than the single 127 mm
                // and closer together, over a wider beaten zone.
                roundsPerMission = 10, roundIntervalSeconds = 0.30f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 18f, burstScale = 1.00f,
                markerColor = Heavy
            },

            // -------------------------------------------------- Enemy: Chinese
            new NavalGunDef
            {
                gun = NavalGun.EnemyPj26_76, origin = NavalOrigin.Enemy, calibreMm = 76,
                label = "76 mm", name = "76 mm H/PJ-26",
                detail = "Type 054A frigate — Chinese pattern",
                radiusMeters = 150f,
                roundsPerMission = 10, roundIntervalSeconds = 0.22f,
                burst = VfxId.ArtilleryLightBurst, smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 8f, burstScale = 0.60f,
                markerColor = Light
            },
            new NavalGunDef
            {
                gun = NavalGun.EnemyPj38_130, origin = NavalOrigin.Enemy, calibreMm = 130,
                label = "130 mm", name = "130 mm H/PJ-38",
                detail = "Type 052D destroyer — Chinese pattern, long reach",
                radiusMeters = 265f,
                roundsPerMission = 8, roundIntervalSeconds = 0.40f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 18f, burstScale = 1.05f,
                markerColor = Siege
            }
        };

        public static IReadOnlyList<NavalGunDef> All => Defs;

        /// <summary>Guns from one fleet, ascending by calibre.</summary>
        public static IEnumerable<NavalGunDef> OfOrigin(NavalOrigin origin)
        {
            foreach (var d in Defs) if (d.origin == origin) yield return d;
        }

        static Dictionary<NavalGun, NavalGunDef> _byGun;

        public static NavalGunDef Get(NavalGun gun)
        {
            if (_byGun == null)
            {
                _byGun = new Dictionary<NavalGun, NavalGunDef>(Defs.Length);
                foreach (var d in Defs) _byGun[d.gun] = d;
            }
            return _byGun.TryGetValue(gun, out var def) ? def : null;
        }
    }
}
