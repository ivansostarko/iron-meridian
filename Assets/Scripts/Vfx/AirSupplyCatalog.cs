using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>The three loads an air supply mission can drop.</summary>
    public enum SupplyKind
    {
        /// <summary>Ammunition of every nature — the load a force in contact runs out of first.</summary>
        Ammo,
        /// <summary>Fuel and lubricants — what stops an armoured formation moving.</summary>
        Oil,
        /// <summary>Medical stores — what a formation taking casualties runs out of.</summary>
        Medic
    }

    /// <summary>One kind of drop: what it is, how it flies in, and what it leaves behind.</summary>
    public class SupplyDropDef
    {
        public SupplyKind kind;

        /// <summary>Button caption.</summary>
        public string label;
        /// <summary>Full name for the countdown banner and messages.</summary>
        public string name;
        /// <summary>One line under the button.</summary>
        public string detail;

        /// <summary>Radius of the drop zone in metres — the circle placed on the map.</summary>
        public float radiusMeters;
        /// <summary>Sorties of this load a scenario may fly. Counted by <see cref="StrikeBudget"/>.</summary>
        public int missions;
        /// <summary>Bundles released on one pass.</summary>
        public int bundles;

        /// <summary>Colour of the drop-zone marker, the countdown banner and the canopy tint.</summary>
        public Color markerColor;

        /// <summary>
        /// The installation a landed bundle leaves on the map. This is the whole
        /// point of the mission: a drop is not an effect, it is a supply point
        /// that was not there before — see <see cref="Data.LogisticsCatalog"/>
        /// and docs/26-LOGISTICS.md.
        /// </summary>
        public LogisticsKind leaves;
        /// <summary>Caption on the site it leaves, so an airdropped point is distinguishable from a placed one.</summary>
        public string cacheLabel;
    }

    /// <summary>
    /// The air supply register: three loads, one airframe, and the numbers the
    /// mission is flown by.
    ///
    /// **Why this is not an air strike with a different payload.** Everything
    /// about the *call* is the same — arm, place a zone, wait out a countdown —
    /// which is why it shares <see cref="CalledStrikeSystem{TKey}"/> with the
    /// artillery and the bombers. Everything about the *arrival* is opposite: a
    /// transport rather than a bomber, canopies rather than a stick, and a thing
    /// left standing on the ground rather than a hole in it. The catalogue is
    /// where that difference is stated.
    ///
    /// See docs/29-AIR-SUPPLY.md.
    /// </summary>
    public static class AirSupplyCatalog
    {
        /// <summary>Seconds between the call and the transport being overhead. The brief's ten.</summary>
        public const float CountdownSeconds = 10f;

        /// <summary>Model id for the airlifter — never a Resources path, golden rule 10.</summary>
        public const string TransportModelId = UnitModelLibrary.TransportAircraft;
        /// <summary>Model id for one load under canopy.</summary>
        public const string BundleModelId = UnitModelLibrary.SupplyBundle;

        // --- how the transport flies ---

        /// <summary>Wingspan the model is scaled to, metres. Oversized like every airframe here, so it reads at map zoom.</summary>
        public const float WingspanMeters = 420f;
        /// <summary>
        /// Run-in altitude above the drop zone's terrain.
        ///
        /// Much lower than a bomber's: a supply drop is flown low and slow, and
        /// — more practically — the bundles have to be watchable all the way
        /// down. From 3 km the canopies would be two pixels for twenty seconds.
        /// </summary>
        public const float AltitudeMeters = 700f;

        public const float ApproachKm = 6f;
        public const float EgressKm = 6f;
        public const float ApproachSeconds = 7f;
        public const float EgressSeconds = 6f;

        /// <summary>Seconds between bundles leaving the ramp.</summary>
        public const float ReleaseIntervalSeconds = 0.55f;

        /// <summary>Metres per second a bundle descends under canopy.</summary>
        public const float DescentMetersPerSecond = 55f;
        /// <summary>Metres a bundle drifts down-track while it falls, as a fraction of its drop height.</summary>
        public const float DriftFraction = 0.22f;
        /// <summary>Height of one bundle model, metres. Oversized to match the airframe.</summary>
        public const float BundleHeightMeters = 90f;

        /// <summary>Declaration order is the order the panel lists them in.</summary>
        public static readonly SupplyDropDef[] All =
        {
            new SupplyDropDef
            {
                kind = SupplyKind.Ammo,
                label = "AMMO SUPPLY",
                name = "Ammunition drop",
                detail = "Rounds of every nature",
                radiusMeters = 420f,
                missions = 4,
                bundles = 5,
                markerColor = new Color(1.00f, 0.55f, 0.30f),
                leaves = LogisticsKind.AmmoPoint,
                cacheLabel = "AIRDROP · AMMO"
            },
            new SupplyDropDef
            {
                kind = SupplyKind.Oil,
                label = "OIL SUPPLY",
                name = "Fuel drop",
                detail = "Fuel and lubricants",
                radiusMeters = 420f,
                missions = 3,
                bundles = 4,
                markerColor = new Color(1.00f, 0.72f, 0.28f),
                leaves = LogisticsKind.FuelPoint,
                cacheLabel = "AIRDROP · FUEL"
            },
            new SupplyDropDef
            {
                kind = SupplyKind.Medic,
                label = "MEDIC SUPPLY",
                name = "Medical drop",
                detail = "Casualty treatment stores",
                radiusMeters = 360f,
                missions = 3,
                bundles = 3,
                markerColor = new Color(0.95f, 0.45f, 0.48f),
                leaves = LogisticsKind.MedicalPoint,
                cacheLabel = "AIRDROP · MEDICAL"
            }
        };

        public static SupplyDropDef Get(SupplyKind kind)
        {
            foreach (var d in All) if (d.kind == kind) return d;
            return All[0];
        }

        /// <summary>Total seconds the transport is on screen.</summary>
        public static float RunSeconds => ApproachSeconds + EgressSeconds;

        /// <summary>Allowance key — one per load, so a spent ammo drop leaves the medical one flyable.</summary>
        public static string BudgetKey(SupplyKind kind) => "airsupply:" + kind;
    }
}
