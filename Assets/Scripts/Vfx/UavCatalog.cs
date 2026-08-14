using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>Unmanned types the strike menu can task.</summary>
    public enum UavType
    {
        /// <summary>Loitering munition: flies to the objective and is expended on it.</summary>
        KamikazeDrone
    }

    /// <summary>One unmanned type: how it flies, what it does at the end of the flight.</summary>
    public class UavDef
    {
        public UavType uav;

        public string label;
        public string name;
        public string detail;

        /// <summary>Radius of the target area in metres.</summary>
        public float radiusMeters;

        /// <summary>Model id in <see cref="UnitModelLibrary"/>. Never a Resources path — golden rule 10.</summary>
        public string modelId;

        /// <summary>Span the model is scaled to, in metres. Exaggerated for map legibility.</summary>
        public float spanMeters;

        /// <summary>Yaw correction if the model's nose does not point along its local +Z.</summary>
        public float noseYawOffsetDeg;

        /// <summary>Height above the target's terrain that the drone cruises in at.</summary>
        public float cruiseAltitudeMeters;

        /// <summary>Ground distance covered on the run-in.</summary>
        public float approachKm;

        /// <summary>Seconds spent cruising toward the objective before the dive begins.</summary>
        public float cruiseSeconds;

        /// <summary>Seconds spent in the terminal dive.</summary>
        public float diveSeconds;

        /// <summary>Nose-down attitude held through the dive, degrees.</summary>
        public float diveAngleDeg;

        /// <summary>Warhead effects and how long its smoke hangs.</summary>
        public VfxId burst;
        public VfxId smoke;
        public float smokeSeconds;
        public float burstScale;

        // --- what the warhead does to a formation (see BlastDamage) ---
        // Small on purpose. A loitering munition carries a few kilograms, so it
        // kills what it lands on and leaves the formation beside it standing —
        // which is what makes it a precision tool rather than cheap artillery.

        /// <summary>Inside this, a formation is destroyed outright. Metres.</summary>
        public float lethalRadiusM = 18f;
        /// <summary>Outer edge of the damage falloff. Metres.</summary>
        public float blastRadiusM = 70f;
        /// <summary>Strength removed at the lethal edge, before the square falloff.</summary>
        public float maxDamage = 0.30f;

        /// <summary>Propellers to spin, matched by mesh-name substring.</summary>
        public RotorSpinner.Spec[] rotors = System.Array.Empty<RotorSpinner.Spec>();

        /// <summary>Colour of the target-area marker and the countdown banner.</summary>
        public Color markerColor;

        /// <summary>Total seconds from launch to impact.</summary>
        public float FlightSeconds => cruiseSeconds + diveSeconds;
    }

    /// <summary>
    /// The single source of truth for UAV strikes. The left rail's UAV STRIKES
    /// section, the target marker and the terminal dive are all driven from
    /// these rows — add a type here rather than special-casing one in the UI,
    /// and update docs/19-UAV-STRIKES.md in the same change.
    /// </summary>
    public static class UavCatalog
    {
        /// <summary>
        /// Seconds between tasking and launch. The same ten seconds artillery
        /// and air strikes use, for the same reason: the ground is committed to
        /// before anything happens.
        /// </summary>
        public const float CountdownSeconds = 10f;

        static readonly UavDef[] Defs =
        {
            new UavDef
            {
                uav = UavType.KamikazeDrone,
                label = "KAMIKAZE DRONE",
                name = "Kamikaze drone",
                detail = "Loitering munition — one target, one warhead",
                radiusMeters = 90f,
                modelId = UnitModelLibrary.KamikazeDrone,
                spanMeters = 90f,
                noseYawOffsetDeg = 0f,
                cruiseAltitudeMeters = 420f,
                approachKm = 1.8f,
                cruiseSeconds = 5.5f,
                diveSeconds = 2.2f,
                diveAngleDeg = 62f,
                burst = VfxId.UavWarheadBurst,
                smoke = VfxId.UavWarheadSmoke,
                smokeSeconds = 12f,
                burstScale = 1.0f,
                lethalRadiusM = 18f, blastRadiusM = 70f, maxDamage = 0.30f,
                rotors = new[]
                {
                    // The Quad's two propeller meshes. They spin about the
                    // model's own up axis; if they turn in the wrong plane, this
                    // vector is the fix.
                    new RotorSpinner.Spec { nameContains = "Propeller", axis = Vector3.up, rpm = 1400f }
                },
                markerColor = new Color(0.80f, 0.60f, 1.00f)
            }
        };

        public static IReadOnlyList<UavDef> All => Defs;

        static Dictionary<UavType, UavDef> _byType;

        public static UavDef Get(UavType type)
        {
            if (_byType == null)
            {
                _byType = new Dictionary<UavType, UavDef>(Defs.Length);
                foreach (var d in Defs) _byType[d.uav] = d;
            }
            return _byType.TryGetValue(type, out var def) ? def : null;
        }
    }
}
