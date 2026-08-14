using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Audio;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// The natures the fire-support menu can call for. Named by calibre because
    /// that is how a fire mission is actually called, and because calibre is
    /// what decides everything else in the row below — how wide the sheaf is,
    /// how the round bursts, and how long the smoke hangs.
    /// </summary>
    public enum ArtilleryCaliber
    {
        /// <summary>105 mm light field howitzer — fast, tight, comparatively small burst.</summary>
        Light105,
        /// <summary>120 mm heavy mortar — steep angle, throws far more soil than fire.</summary>
        Mortar120,
        /// <summary>155 mm medium howitzer — the NATO workhorse.</summary>
        Medium155,
        /// <summary>203 mm heavy howitzer — the heaviest tube here; slow and enormous.</summary>
        Heavy203
    }

    /// <summary>One nature: what it looks like, sounds like, and how wide it lands.</summary>
    public class ArtilleryDef
    {
        public ArtilleryCaliber caliber;

        /// <summary>Button caption — the calibre alone, as it is called.</summary>
        public string label;
        /// <summary>Full name for the countdown banner and messages.</summary>
        public string name;
        /// <summary>One line under the button saying what this nature is for.</summary>
        public string detail;

        /// <summary>
        /// Radius of the target area in metres — the circle the player places on
        /// the map, and the area the five rounds are scattered inside. Heavier
        /// tubes fire a wider sheaf.
        /// </summary>
        public float radiusMeters;

        /// <summary>Burst effect for one round. Distinct per nature — see docs/08-PARTICLE-SYSTEMS.md.</summary>
        public VfxId burst;
        /// <summary>Smoke left behind by one round. Also distinct per nature.</summary>
        public VfxId smoke;
        /// <summary>Seconds the smoke hangs before it is told to disperse.</summary>
        public float smokeSeconds;

        /// <summary>Extra scale on the burst, on top of the effect's own size.</summary>
        public float burstScale;

        /// <summary>
        /// Seconds between rounds in the salvo. A battery does not land five
        /// rounds simultaneously, and staggering them is also what makes the
        /// strike read as a sequence of hits rather than one big bang.
        /// </summary>
        public float shellIntervalSeconds;

        /// <summary>Colour of the target-area marker and the countdown banner.</summary>
        public Color markerColor;

        // The report is not listed here: it belongs to the burst effect's
        // catalogue row (VfxCatalog), and VfxInstance plays it automatically
        // when the burst spawns. Naming it in both places would let them drift.
    }

    /// <summary>
    /// The single source of truth for artillery strikes. The left rail's
    /// ARTILLERY STRIKE section, the target marker and the impact sequence are
    /// all driven from these rows — add a nature here rather than special-casing
    /// one in the UI, and update docs/17-ARTILLERY.md in the same change.
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

        // Ordered by calibre rather than by the order they were asked for: a
        // munitions list that runs 105 → 120 → 155 → 203 is scannable, and the
        // radius grows monotonically down the panel, which makes the trade-off
        // between the natures legible without reading a word.
        static readonly ArtilleryDef[] Defs =
        {
            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.Light105,
                label = "105 mm",
                name = "105 mm light howitzer",
                detail = "Tight sheaf, quick rounds — troops in the open",
                radiusMeters = 140f,
                burst = VfxId.ArtilleryLightBurst,
                smoke = VfxId.ArtilleryLightSmoke,
                smokeSeconds = 9f,
                burstScale = 0.85f,
                shellIntervalSeconds = 0.30f,
                markerColor = new Color(1.00f, 0.86f, 0.35f)
            },

            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.Mortar120,
                label = "120 mm",
                name = "120 mm heavy mortar",
                detail = "Steep angle — throws soil, not fire",
                radiusMeters = 120f,
                burst = VfxId.ArtilleryMortarBurst,
                smoke = VfxId.ArtilleryMortarSmoke,
                smokeSeconds = 11f,
                burstScale = 0.95f,
                shellIntervalSeconds = 0.42f,
                markerColor = new Color(0.85f, 0.72f, 0.45f)
            },

            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.Medium155,
                label = "155 mm",
                name = "155 mm medium howitzer",
                detail = "The workhorse — general destructive fire",
                radiusMeters = 190f,
                burst = VfxId.ArtilleryMediumBurst,
                smoke = VfxId.ArtilleryMediumSmoke,
                smokeSeconds = 15f,
                burstScale = 1.00f,
                shellIntervalSeconds = 0.55f,
                markerColor = new Color(1.00f, 0.55f, 0.18f)
            },

            new ArtilleryDef
            {
                caliber = ArtilleryCaliber.Heavy203,
                label = "203 mm",
                name = "203 mm heavy howitzer",
                detail = "Slow, enormous — fortifications and depots",
                radiusMeters = 260f,
                burst = VfxId.ArtilleryHeavyBurst,
                smoke = VfxId.ArtilleryHeavySmoke,
                smokeSeconds = 22f,
                burstScale = 1.25f,
                shellIntervalSeconds = 0.85f,
                markerColor = new Color(0.95f, 0.32f, 0.22f)
            }
        };

        public static IReadOnlyList<ArtilleryDef> All => Defs;

        static Dictionary<ArtilleryCaliber, ArtilleryDef> _byCaliber;

        public static ArtilleryDef Get(ArtilleryCaliber caliber)
        {
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
