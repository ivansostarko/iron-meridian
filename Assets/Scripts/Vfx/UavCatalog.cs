using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>Unmanned types the strike menu can task.</summary>
    public enum UavType
    {
        /// <summary>Loitering munition: flies to the objective and is expended on it.</summary>
        KamikazeDrone,

        /// <summary>
        /// Long-range one-way attack drone of the Shahed class. Bigger warhead,
        /// longer run-in and a shallower terminal dive than a tactical
        /// loitering munition — it arrives from the operational depth rather
        /// than from the next ridge.
        /// </summary>
        ShahedDrone,

        /// <summary>
        /// Tactical ISR drone. The one unmanned type here that carries no
        /// warhead: it flies to a point, holds an orbit over it, lifts the fog
        /// off everything within ten kilometres, and goes home.
        /// </summary>
        ReconDrone
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

        /// <summary>
        /// Ground fire left burning where the drone went in, or
        /// <see cref="VfxId.Dust"/>-style "none" via <see cref="wreckFireSeconds"/>
        /// at zero. A tactical munition leaves a scorch and nothing else; a
        /// fifty-kilogram warhead with the airframe's fuel behind it leaves
        /// something burning, and that is what marks the place afterwards.
        /// </summary>
        public VfxId wreckFire;
        public float wreckFireSeconds;

        /// <summary>
        /// Looping engine note carried by the airframe. A quadcopter and a
        /// two-stroke delta wing do not sound remotely alike, and the engine is
        /// the first thing that identifies which one is coming.
        /// </summary>
        public Audio.EffectSound engineSound = Audio.EffectSound.DroneBuzz;

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

        // ------------------------------------------------- reconnaissance
        //
        // The fields below are read only when <see cref="isRecon"/> is set. A
        // recon type has no warhead, so burst / smoke / lethal radius mean
        // nothing for it, and it has an orbit and a dwell that mean nothing for
        // the attack types. One class rather than two because everything *up to*
        // the objective — arming, the ring following the cursor, the countdown,
        // the ten-second commit — is identical, and that is most of the work.

        /// <summary>
        /// True for the type that looks instead of detonating. Branches
        /// <see cref="UavStrikeSystem"/> into its reconnaissance mission.
        /// </summary>
        public bool isRecon;

        /// <summary>Ground radius the sensor covers while on station, km. Recon only.</summary>
        public float reconRadiusKm;

        /// <summary>
        /// Scenario minutes spent working the objective before the drone leaves.
        /// Operational minutes, not real ones: five minutes on station is five
        /// minutes on the clock, so it costs the player five minutes of the
        /// battle rather than five minutes of their afternoon.
        /// </summary>
        public float onStationMinutes;

        /// <summary>Real seconds the transit in — and the transit out — take. Recon only.</summary>
        public float transitSeconds;

        /// <summary>Radius of the orbit flown over the objective, metres. Recon only.</summary>
        public float orbitRadiusMeters;
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
                // No rotor spec: this airframe is built in code and carries its
                // own animation clip, which already turns the propeller. A
                // RotorSpinner on top of that would be two things driving the
                // same transform, and the loser would be whichever ran second.
                markerColor = new Color(0.80f, 0.60f, 1.00f)
            },

            new UavDef
            {
                uav = UavType.ShahedDrone,
                label = "SHAHED DRONE",
                name = "Shahed drone",
                detail = "Long-range one-way attack — deep strike, heavy warhead",
                // Wider than the tactical munition: a 50 kg class warhead
                // delivered by a drone with a metre or two of guidance error is
                // an area weapon in a way a 5 kg one is not.
                radiusMeters = 160f,
                modelId = UnitModelLibrary.ShahedDrone,
                spanMeters = 130f,
                noseYawOffsetDeg = 0f,
                // Comes in low and long. A delta-wing airframe cruising at 500 m
                // over a 6 km run-in is the picture people recognise, and it
                // gives the player time to see it coming.
                cruiseAltitudeMeters = 520f,
                approachKm = 6.0f,
                cruiseSeconds = 8.5f,
                diveSeconds = 3.0f,
                // Shallower than the tactical drone's 62°: this class glides
                // onto the target rather than tipping vertically onto it.
                diveAngleDeg = 38f,
                burst = VfxId.ShahedWarheadBurst,
                smoke = VfxId.ShahedWarheadSmoke,
                smokeSeconds = 22f,
                burstScale = 1.9f,
                lethalRadiusM = 45f, blastRadiusM = 170f, maxDamage = 0.62f,
                // The PolyPack airframe is a delta wing with a nose propeller;
                // the mesh names are matched by substring, so both spellings the
                // pack has used are covered.
                rotors = new[]
                {
                    new RotorSpinner.Spec { nameContains = "Prop", axis = Vector3.forward, rpm = 2200f }
                },
                wreckFire = VfxId.ShahedWreckFire,
                wreckFireSeconds = 40f,
                engineSound = Audio.EffectSound.ShahedEngine,
                markerColor = new Color(1.00f, 0.52f, 0.30f)
            },

            new UavDef
            {
                uav = UavType.ReconDrone,
                label = "RECONNAISSANCE DRONE",
                name = "Reconnaissance drone",
                detail = "Looks — 10 km search area, 5 minutes on station",
                isRecon = true,
                // The target area *is* the search area: ten kilometres of ground
                // the fog comes off. Shown as a ring under the cursor before the
                // commit, exactly as a beaten zone is, so the player places a
                // sensor footprint rather than guessing where one will land.
                radiusMeters = 10000f,
                reconRadiusKm = 10f,
                onStationMinutes = 5f,
                modelId = UnitModelLibrary.ReconDrone,
                // Larger on the map than the attack drones. It is a bigger
                // airframe, and it has to stay readable at the altitude a 10 km
                // ring forces the camera back to.
                spanMeters = 220f,
                noseYawOffsetDeg = 0f,
                // High and slow: the profile that keeps a sensor pointed at the
                // ground rather than at the horizon.
                cruiseAltitudeMeters = 1500f,
                approachKm = 9f,
                transitSeconds = 9f,
                orbitRadiusMeters = 2600f,
                // Unused by the recon mission, but left honest rather than zero:
                // FlightSeconds is public and something will read it one day.
                cruiseSeconds = 9f,
                diveSeconds = 0f,
                engineSound = Audio.EffectSound.DroneBuzz,
                markerColor = new Color(0.42f, 0.78f, 1.00f)
            }
        };

        /// <summary>Applies the player's tuning of these types — see <see cref="Save.TuningStore"/>.</summary>
        static bool _tuned;
        static void EnsureTuned()
        {
            if (_tuned) return;
            _tuned = true;      // set first: Apply must never re-enter this
            foreach (var d in Defs)
                Save.TuningStore.Apply(Data.GameCatalogs.Uav, d.uav.ToString(), d);
        }

        public static IReadOnlyList<UavDef> All { get { EnsureTuned(); return Defs; } }

        static Dictionary<UavType, UavDef> _byType;

        public static UavDef Get(UavType type)
        {
            EnsureTuned();
            if (_byType == null)
            {
                _byType = new Dictionary<UavType, UavDef>(Defs.Length);
                foreach (var d in Defs) _byType[d.uav] = d;
            }
            return _byType.TryGetValue(type, out var def) ? def : null;
        }
    }
}
