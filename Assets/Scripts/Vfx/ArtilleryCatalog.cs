using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Whose inventory a nature comes from.
    ///
    /// The second value is **Enemy**, not a nationality. The game's two sides
    /// are User and Enemy (see <see cref="Data.Team"/>), and naming one
    /// inventory after a real country while the other is named after an alliance
    /// was both inconsistent and a claim the game has no business making. The
    /// natures themselves are unchanged — they are still the Soviet-pattern
    /// calibres, and the detail line on each button still names the real gun.
    /// </summary>
    public enum ArtilleryOrigin
    {
        Nato,
        Enemy
    }

    /// <summary>
    /// Mortar or gun. Not cosmetic: a mortar bomb arrives almost vertically and
    /// throws far more soil than fire, which is a different event on the map
    /// from a shell arriving on a flat trajectory.
    /// </summary>
    public enum ArtilleryKind
    {
        Mortar,
        Gun
    }

    /// <summary>
    /// The natures the fire-support menu can call for, named by origin and
    /// calibre because that is how a fire mission is actually called.
    /// </summary>
    public enum ArtilleryCaliber
    {
        // --- NATO mortars ---
        NatoMortar60,
        NatoMortar81,
        NatoMortar120,
        // --- NATO guns and howitzers ---
        NatoGun105,
        NatoGun155,
        NatoGun203,
        // --- Enemy-pattern mortars ---
        EnemyMortar82,
        EnemyMortar120,
        EnemyMortar160,
        EnemyMortar240,
        // --- Enemy-pattern guns and howitzers ---
        EnemyGun122,
        EnemyGun130,
        EnemyGun152,
        EnemyGun203
    }

    /// <summary>One nature: what it looks like, sounds like, and how wide it lands.</summary>
    public class ArtilleryDef
    {
        public ArtilleryCaliber caliber;
        public ArtilleryOrigin origin;
        public ArtilleryKind kind;

        /// <summary>Bore in millimetres — the number the button leads with.</summary>
        public int calibreMm;

        /// <summary>Button caption — the calibre alone, as it is called.</summary>
        public string label;
        /// <summary>Full name for the countdown banner and messages.</summary>
        public string name;
        /// <summary>One line under the button: the real weapon and what it is for.</summary>
        public string detail;

        /// <summary>
        /// Radius of the target area in metres — the circle the player places on
        /// the map, and the area the rounds are scattered inside. Heavier tubes
        /// fire a wider sheaf.
        /// </summary>
        public float radiusMeters;

        /// <summary>Burst effect for one round. See docs/08-PARTICLE-SYSTEMS.md.</summary>
        public VfxId burst;
        /// <summary>Smoke left behind by one round.</summary>
        public VfxId smoke;
        /// <summary>Seconds the smoke hangs before it is told to disperse.</summary>
        public float smokeSeconds;

        /// <summary>Extra scale on the burst, on top of the effect's own size.</summary>
        public float burstScale;

        /// <summary>
        /// Seconds between rounds in the salvo. A battery does not land five
        /// rounds simultaneously, and staggering them is also what makes the
        /// strike read as a sequence of hits rather than one big bang. Heavier
        /// tubes have a slower rate of fire, so they are spaced wider.
        /// </summary>
        public float shellIntervalSeconds;

        /// <summary>Colour of the target-area marker and the countdown banner.</summary>
        public Color markerColor;

        // The report is not listed here: it belongs to the burst effect's
        // catalogue row (VfxCatalog), and VfxInstance plays it automatically
        // when the burst spawns. Naming it in both places would let them drift.

        // --- what a round does to a formation (see BlastDamage) ---
        //
        // Derived from calibre rather than listed per nature, because that is
        // genuinely what decides it: charge mass, and therefore lethal area,
        // scales with the bore. Fourteen hand-tuned triples would be fourteen
        // numbers to keep plausible against each other; one relation stays
        // consistent by construction, and a new nature gets sensible numbers
        // the moment its calibre is written down.

        /// <summary>Inside this, a formation is destroyed outright. Metres.</summary>
        public float LethalRadiusM => calibreMm * 0.16f;

        /// <summary>Outer edge of the damage falloff. Metres.</summary>
        public float BlastRadiusM => calibreMm * 0.85f;

        /// <summary>Strength removed at the lethal edge, before the square falloff.</summary>
        public float MaxDamage => Mathf.Clamp(calibreMm / 700f, 0.06f, 0.40f);
    }

    /// <summary>
    /// The single source of truth for artillery strikes. The left rail's
    /// ARTILLERY STRIKE section, the target marker and the impact sequence are
    /// all driven from these rows — add a nature here rather than special-casing
    /// one in the UI, and update docs/17-ARTILLERY.md in the same change.
    ///
    /// **Fourteen natures, four burst signatures.** Each nature does *not* get
    /// its own effect: what separates a 122 mm shell from a 152 mm one on a map
    /// three kilometres wide is how big the hole is, not what the flash looks
    /// like. So natures map onto four signatures — a light burst, a mortar's
    /// soil column, a standard HE burst and a heavy blast — and are told apart
    /// by target radius, burst scale and rate of fire, all of which are real
    /// differences the player can see and use. Inventing fourteen near-identical
    /// particle effects would be fourteen things to keep in step for no gain.
    /// </summary>
    public static class ArtilleryCatalog
    {
        /// <summary>Rounds in one fire mission.</summary>
        public const int ShellsPerMission = 5;

        /// <summary>
        /// Seconds between the call for fire and the first round landing. This
        /// is the whole point of the feature — the player commits to a piece of
        /// ground and then has to live with that decision for ten seconds.
        /// </summary>
        public const float CountdownSeconds = 10f;

        // Marker colours run from pale yellow through orange to deep red with
        // increasing weight, deliberately regardless of origin: the colour tells
        // the player how big the beaten zone is, which is what they are choosing
        // between. Whose gun it is, is already written on the button.
        static readonly Color Feather = new Color(1.00f, 0.92f, 0.55f);
        static readonly Color Light = new Color(1.00f, 0.86f, 0.35f);
        static readonly Color Soil = new Color(0.85f, 0.72f, 0.45f);
        static readonly Color Medium = new Color(1.00f, 0.62f, 0.22f);
        static readonly Color Heavy = new Color(1.00f, 0.45f, 0.18f);
        static readonly Color Siege = new Color(0.95f, 0.28f, 0.22f);

        static readonly ArtilleryDef[] Defs =
        {
            // ---------------------------------------------------- NATO mortars
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoMortar60, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Mortar, calibreMm = 60,
                label = "60 mm", name = "60 mm light mortar",
                detail = "M224 — company fire, fast and close",
                radiusMeters = 70f,
                burst = VfxId.ArtilleryMortarBurst, smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 6f, burstScale = 0.50f, shellIntervalSeconds = 0.20f,
                markerColor = Feather
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoMortar81, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Mortar, calibreMm = 81,
                label = "81 mm", name = "81 mm medium mortar",
                detail = "L16 / M252 — battalion's own fire support",
                radiusMeters = 100f,
                burst = VfxId.ArtilleryMortarBurst, smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 8f, burstScale = 0.68f, shellIntervalSeconds = 0.28f,
                markerColor = Light
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoMortar120, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Mortar, calibreMm = 120,
                label = "120 mm", name = "120 mm heavy mortar",
                detail = "M120 / RT-61 — steep angle, throws soil not fire",
                radiusMeters = 130f,
                burst = VfxId.ArtilleryMortarBurst, smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 11f, burstScale = 0.95f, shellIntervalSeconds = 0.42f,
                markerColor = Soil
            },

            // ------------------------------------------- NATO guns & howitzers
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoGun105, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Gun, calibreMm = 105,
                label = "105 mm", name = "105 mm light howitzer",
                detail = "M119 / L118 — tight sheaf, quick rounds",
                radiusMeters = 140f,
                burst = VfxId.ArtilleryLightBurst, smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 9f, burstScale = 0.85f, shellIntervalSeconds = 0.30f,
                markerColor = Light
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoGun155, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Gun, calibreMm = 155,
                label = "155 mm", name = "155 mm medium howitzer",
                detail = "M777 / PzH 2000 — the workhorse",
                radiusMeters = 190f,
                burst = VfxId.ArtilleryMediumBurst, smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 15f, burstScale = 1.00f, shellIntervalSeconds = 0.55f,
                markerColor = Medium
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.NatoGun203, origin = ArtilleryOrigin.Nato,
                kind = ArtilleryKind.Gun, calibreMm = 203,
                label = "203 mm", name = "203 mm heavy howitzer",
                detail = "M110 — fortifications and depots",
                radiusMeters = 260f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 22f, burstScale = 1.25f, shellIntervalSeconds = 0.85f,
                markerColor = Heavy
            },

            // -------------------------------------------- Enemy-pattern mortars
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyMortar82, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Mortar, calibreMm = 82,
                label = "82 mm", name = "82 mm mortar",
                detail = "2B14 Podnos — company fire",
                radiusMeters = 105f,
                burst = VfxId.ArtilleryMortarBurst, smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 8f, burstScale = 0.70f, shellIntervalSeconds = 0.28f,
                markerColor = Light
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyMortar120, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Mortar, calibreMm = 120,
                label = "120 mm", name = "120 mm mortar",
                detail = "2B11 Sani — battalion heavy mortar",
                radiusMeters = 135f,
                burst = VfxId.ArtilleryMortarBurst, smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 11f, burstScale = 0.95f, shellIntervalSeconds = 0.42f,
                markerColor = Soil
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyMortar160, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Mortar, calibreMm = 160,
                label = "160 mm", name = "160 mm heavy mortar",
                detail = "M-160 — breaks field fortifications",
                radiusMeters = 185f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 16f, burstScale = 1.10f, shellIntervalSeconds = 0.60f,
                markerColor = Heavy
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyMortar240, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Mortar, calibreMm = 240,
                label = "240 mm", name = "240 mm siege mortar",
                detail = "2S4 Tyulpan — the heaviest mortar in service",
                radiusMeters = 300f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 26f, burstScale = 1.55f, shellIntervalSeconds = 1.10f,
                markerColor = Siege
            },

            // ----------------------------------- Enemy-pattern guns & howitzers
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyGun122, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Gun, calibreMm = 122,
                label = "122 mm", name = "122 mm howitzer",
                detail = "D-30 / 2S1 Gvozdika — divisional workhorse",
                radiusMeters = 150f,
                burst = VfxId.ArtilleryMediumBurst, smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 12f, burstScale = 0.90f, shellIntervalSeconds = 0.35f,
                markerColor = Medium
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyGun130, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Gun, calibreMm = 130,
                label = "130 mm", name = "130 mm field gun",
                detail = "M-46 — long range counter-battery",
                radiusMeters = 175f,
                burst = VfxId.ArtilleryMediumBurst, smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 14f, burstScale = 0.98f, shellIntervalSeconds = 0.45f,
                markerColor = Medium
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyGun152, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Gun, calibreMm = 152,
                label = "152 mm", name = "152 mm howitzer",
                detail = "2S3 Akatsiya / 2S19 Msta-S — general destructive fire",
                radiusMeters = 200f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 17f, burstScale = 1.05f, shellIntervalSeconds = 0.55f,
                markerColor = Heavy
            },
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.EnemyGun203, origin = ArtilleryOrigin.Enemy,
                kind = ArtilleryKind.Gun, calibreMm = 203,
                label = "203 mm", name = "203 mm heavy gun",
                detail = "2S7 Pion — army-level siege fire",
                radiusMeters = 285f,
                burst = VfxId.ArtilleryHeavyBurst, smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 24f, burstScale = 1.35f, shellIntervalSeconds = 0.95f,
                markerColor = Siege
            }
        };

        /// <summary>
        /// Applies the player's tuning of these natures — see
        /// <see cref="Save.TuningStore"/>. Every accessor below runs it first,
        /// so the values the game fires with and the values the DEVELOPMENT
        /// screen shows cannot drift apart.
        /// </summary>
        static bool _tuned;
        static void EnsureTuned()
        {
            if (_tuned) return;
            _tuned = true;      // set first: Apply must never re-enter this
            foreach (var d in Defs)
                Save.TuningStore.Apply(Data.GameCatalogs.Artillery, d.caliber.ToString(), d);
        }

        public static IReadOnlyList<ArtilleryDef> All { get { EnsureTuned(); return Defs; } }

        /// <summary>Natures from one inventory, mortars first then guns, ascending by calibre.</summary>
        public static IEnumerable<ArtilleryDef> OfOrigin(ArtilleryOrigin origin)
        {
            EnsureTuned();
            foreach (var d in Defs) if (d.origin == origin && d.kind == ArtilleryKind.Mortar) yield return d;
            foreach (var d in Defs) if (d.origin == origin && d.kind == ArtilleryKind.Gun) yield return d;
        }

        static Dictionary<ArtilleryCaliber, ArtilleryDef> _byCaliber;

        public static ArtilleryDef Get(ArtilleryCaliber caliber)
        {
            EnsureTuned();
            if (_byCaliber == null)
            {
                _byCaliber = new Dictionary<ArtilleryCaliber, ArtilleryDef>(Defs.Length);
                foreach (var d in Defs) _byCaliber[d.caliber] = d;
            }
            return _byCaliber.TryGetValue(caliber, out var def) ? def : null;
        }

        /// <summary>
        /// How long the whole salvo takes to land, from the first round to the
        /// last. Used to keep the target marker alive until the shooting stops.
        /// </summary>
        public static float SalvoSeconds(ArtilleryDef def) =>
            def == null ? 0f : def.shellIntervalSeconds * (ShellsPerMission - 1);
    }
}
