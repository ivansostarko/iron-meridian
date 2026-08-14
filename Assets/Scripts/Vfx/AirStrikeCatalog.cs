using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>Airframes the strike menu can task.</summary>
    public enum StrikeAircraft
    {
        /// <summary>B-2 Spirit — heavy stealth bomber, one pass, a stick of five.</summary>
        B2Spirit
    }

    /// <summary>One airframe: what it flies like, what it drops, and how it sounds.</summary>
    public class AircraftDef
    {
        public StrikeAircraft aircraft;

        /// <summary>Button caption.</summary>
        public string label;
        /// <summary>Full name for the countdown banner and messages.</summary>
        public string name;
        /// <summary>One line under the button saying what this airframe is for.</summary>
        public string detail;

        /// <summary>Radius of the target area in metres — the circle placed on the map.</summary>
        public float radiusMeters;

        /// <summary>Model id in <see cref="UnitModelLibrary"/>. Never a Resources path — golden rule 10.</summary>
        public string modelId;

        /// <summary>
        /// Wingspan the model is scaled to, in metres.
        ///
        /// A real B-2 spans 52 m, which is a speck on a map whose unit icons are
        /// 260 m across and whose explosions are 300 m. The aircraft is drawn
        /// deliberately oversized so it reads at the zoom the game is actually
        /// played at — the same exaggeration the icons themselves use. Scaling
        /// is measured from the model's own bounds at load, so it does not
        /// matter what units the FBX was authored in.
        /// </summary>
        public float wingspanMeters;

        /// <summary>
        /// Yaw correction, in degrees, if the model's nose does not point along
        /// its local +Z. Set this if the bomber flies sideways or backwards.
        /// </summary>
        public float noseYawOffsetDeg;

        /// <summary>Altitude above the target's terrain that the aircraft runs in at.</summary>
        public float altitudeMeters;

        /// <summary>Ground distance the aircraft covers before and after the release point.</summary>
        public float approachKm;
        public float egressKm;

        /// <summary>Seconds spent on the inbound leg, and on the outbound leg.</summary>
        public float approachSeconds;
        public float egressSeconds;

        /// <summary>Seconds between bomb releases — the spacing of the stick.</summary>
        public float releaseIntervalSeconds;

        /// <summary>Seconds a weapon takes to fall from release altitude to the ground.</summary>
        public float fallSeconds;

        /// <summary>Burst and smoke for one weapon. See docs/08-PARTICLE-SYSTEMS.md.</summary>
        public VfxId burst;
        public VfxId smoke;
        public float smokeSeconds;
        public float burstScale;

        /// <summary>Colour of the target-area marker and the countdown banner.</summary>
        public Color markerColor;

        /// <summary>Total seconds the aircraft is on screen.</summary>
        public float RunSeconds => approachSeconds + egressSeconds;
    }

    /// <summary>
    /// The single source of truth for air strikes. The left rail's AIR STRIKE
    /// section, the target marker, the bomber run and the impacts are all driven
    /// from these rows — add an airframe here rather than special-casing one in
    /// the UI, and update docs/18-AIR-STRIKES.md in the same change.
    /// </summary>
    public static class AirStrikeCatalog
    {
        /// <summary>Weapons released in one pass.</summary>
        public const int BombsPerStrike = 5;

        /// <summary>
        /// Seconds between tasking the strike and the aircraft appearing. The
        /// same ten seconds artillery uses, for the same reason: the ground is
        /// committed to before anything happens, and the wait is the decision.
        /// </summary>
        public const float CountdownSeconds = 10f;

        static readonly AircraftDef[] Defs =
        {
            new AircraftDef
            {
                aircraft = StrikeAircraft.B2Spirit,
                label = "B-2 SPIRIT",
                name = "B-2 Spirit stealth bomber",
                detail = "One pass, a stick of five — hardened targets",
                radiusMeters = 320f,
                modelId = UnitModelLibrary.StealthBomber,
                wingspanMeters = 240f,
                noseYawOffsetDeg = 0f,
                altitudeMeters = 1500f,
                approachKm = 4.5f,
                egressKm = 4.5f,
                approachSeconds = 5.0f,
                egressSeconds = 4.0f,
                releaseIntervalSeconds = 0.34f,
                fallSeconds = 1.15f,
                burst = VfxId.AerialBombBurst,
                smoke = VfxId.AerialBombSmoke,
                smokeSeconds = 26f,
                burstScale = 1.15f,
                markerColor = new Color(0.45f, 0.72f, 1.00f)
            }
        };

        public static IReadOnlyList<AircraftDef> All => Defs;

        static Dictionary<StrikeAircraft, AircraftDef> _byAircraft;

        public static AircraftDef Get(StrikeAircraft aircraft)
        {
            if (_byAircraft == null)
            {
                _byAircraft = new Dictionary<StrikeAircraft, AircraftDef>(Defs.Length);
                foreach (var d in Defs) _byAircraft[d.aircraft] = d;
            }
            return _byAircraft.TryGetValue(aircraft, out var def) ? def : null;
        }
    }
}
