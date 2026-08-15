using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>Airframes the strike menu can task.</summary>
    public enum StrikeAircraft
    {
        /// <summary>B-2 Spirit — heavy stealth bomber, one high pass, a full stick.</summary>
        B2Spirit,
        /// <summary>Strike fighter — fast, low, a tight stick on one run.</summary>
        StrikeFighter,
        /// <summary>Attack helicopter — slow, very low, walks its load across the target.</summary>
        AttackHelicopter
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

        /// <summary>
        /// Sorties of this airframe a scenario may fly. The scarcity that makes
        /// picking one a decision: two B-2 sorties against eight helicopter
        /// runs is the whole argument for ever tasking the helicopter.
        /// Counted by <see cref="StrikeBudget"/> — see docs/18-AIR-STRIKES.md.
        /// </summary>
        public int missions = 4;

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

        // --- what one weapon does to a formation (see BlastDamage) ---
        // Listed rather than derived: there are three airframes, and an
        // air-dropped weapon has no calibre to derive from.

        /// <summary>Inside this, a formation is destroyed outright. Metres.</summary>
        public float lethalRadiusM = 60f;
        /// <summary>Outer edge of the damage falloff. Metres.</summary>
        public float blastRadiusM = 300f;
        /// <summary>Strength removed at the lethal edge, before the square falloff.</summary>
        public float maxDamage = 0.55f;

        /// <summary>
        /// Bank held through the run, degrees. A fixed-wing aircraft leans into
        /// its attack; a helicopter flying a level pass barely does.
        /// </summary>
        public float bankDegrees = 8f;

        /// <summary>
        /// Nose-down attitude through the run, degrees. Gunships fly nose-low;
        /// a bomber does not.
        /// </summary>
        public float pitchDegrees;

        /// <summary>
        /// Rotors and propellers to spin, matched by mesh-name substring. Empty
        /// for a jet. See <see cref="RotorSpinner"/>.
        /// </summary>
        public RotorSpinner.Spec[] rotors = System.Array.Empty<RotorSpinner.Spec>();

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
        /// <summary>
        /// Weapons released in one pass.
        ///
        /// Nine rather than five. The stick is now spread over the whole target
        /// circle rather than walked along a line (see
        /// <see cref="StrikeImpact.ScatterInCircle"/>), and five points scattered
        /// across a 320 m disc leave most of it visibly untouched — the pass
        /// looked like it had missed the area it was given. Nine covers it
        /// without the releases running into each other.
        /// </summary>
        public const int BombsPerStrike = 9;

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
                aircraft = StrikeAircraft.B2Spirit, missions = 2,
                label = "B-2 SPIRIT",
                name = "B-2 Spirit stealth bomber",
                detail = "One pass, a full stick — hardened targets",
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
                bankDegrees = 8f,
                lethalRadiusM = 65f, blastRadiusM = 320f, maxDamage = 0.58f,
                markerColor = new Color(0.45f, 0.72f, 1.00f)
            },

            // Fast and low. The whole pass is over in half the bomber's time and
            // the stick is tight, which is the trade: a smaller beaten zone
            // delivered quickly, rather than a wide one delivered from altitude.
            new AircraftDef
            {
                aircraft = StrikeAircraft.StrikeFighter, missions = 6,
                label = "STRIKE FIGHTER",
                name = "Multirole strike fighter",
                detail = "Fast and low — one tight pass",
                radiusMeters = 220f,
                modelId = UnitModelLibrary.StrikeFighter,
                wingspanMeters = 150f,
                noseYawOffsetDeg = 0f,
                altitudeMeters = 650f,
                approachKm = 5.5f,
                egressKm = 5.5f,
                approachSeconds = 3.0f,
                egressSeconds = 2.6f,
                releaseIntervalSeconds = 0.20f,
                fallSeconds = 0.75f,
                burst = VfxId.AerialBombBurst,
                smoke = VfxId.AerialBombSmoke,
                smokeSeconds = 20f,
                burstScale = 0.90f,
                // A fast jet rolls hard into its run.
                bankDegrees = 22f,
                pitchDegrees = 6f,
                lethalRadiusM = 50f, blastRadiusM = 240f, maxDamage = 0.46f,
                markerColor = new Color(0.55f, 0.85f, 0.95f)
            },

            // Slow, very low, nose-down, rotors turning. It is on screen longer
            // than either fixed-wing aircraft, which is the point — a gunship
            // run is something you watch happen rather than something that has
            // already happened by the time you look up.
            new AircraftDef
            {
                aircraft = StrikeAircraft.AttackHelicopter, missions = 10,
                label = "ATTACK HELICOPTER",
                name = "Attack helicopter",
                detail = "Slow and very low — walks its load in",
                radiusMeters = 180f,
                modelId = UnitModelLibrary.AttackHelicopter,
                wingspanMeters = 170f,
                noseYawOffsetDeg = 0f,
                altitudeMeters = 220f,
                approachKm = 2.2f,
                egressKm = 2.0f,
                approachSeconds = 6.5f,
                egressSeconds = 5.0f,
                releaseIntervalSeconds = 0.55f,
                fallSeconds = 0.55f,
                burst = VfxId.AerialBombBurst,
                smoke = VfxId.AerialBombSmoke,
                smokeSeconds = 16f,
                burstScale = 0.70f,
                // Barely banked, distinctly nose-down: how a gunship runs in.
                bankDegrees = 4f,
                pitchDegrees = 10f,
                lethalRadiusM = 35f, blastRadiusM = 170f, maxDamage = 0.34f,
                rotors = new[]
                {
                    // Main rotor over the fuselage, tail rotor on the boom. The
                    // axes are the model's own local ones — if a rotor spins in
                    // the wrong plane, these two vectors are the fix.
                    new RotorSpinner.Spec { nameContains = "Screw_Main", axis = Vector3.up,    rpm = 380f },
                    new RotorSpinner.Spec { nameContains = "Screw_Back", axis = Vector3.right, rpm = 620f }
                },
                markerColor = new Color(0.55f, 0.90f, 0.65f)
            }
        };

        /// <summary>Applies the player's tuning of these airframes — see <see cref="Save.TuningStore"/>.</summary>
        static bool _tuned;
        static void EnsureTuned()
        {
            if (_tuned) return;
            _tuned = true;      // set first: Apply must never re-enter this
            foreach (var d in Defs)
                Save.TuningStore.Apply(Data.GameCatalogs.AirStrike, d.aircraft.ToString(), d);
        }

        public static IReadOnlyList<AircraftDef> All { get { EnsureTuned(); return Defs; } }

        static Dictionary<StrikeAircraft, AircraftDef> _byAircraft;

          /// <summary>
        /// The key this system's missions are counted under. Prefixed by family
        /// because the five strike catalogues have five unrelated enums whose
        /// member names can collide. See <see cref="StrikeBudget"/>.
        /// </summary>
        public static string BudgetKey(StrikeAircraft aircraft) => StrikeBudget.Key("air", aircraft);

      public static AircraftDef Get(StrikeAircraft aircraft)
        {
            EnsureTuned();
            if (_byAircraft == null)
            {
                _byAircraft = new Dictionary<StrikeAircraft, AircraftDef>(Defs.Length);
                foreach (var d in Defs) _byAircraft[d.aircraft] = d;
            }
            return _byAircraft.TryGetValue(aircraft, out var def) ? def : null;
        }
    }
}
