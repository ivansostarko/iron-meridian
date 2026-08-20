using UnityEngine;

namespace IronMeridian.Data
{
    /// <summary>
    /// The six kinds of logistic installation a scenario can be given.
    ///
    /// Ordered from the rear forward — depot, supply point, then the four
    /// function-specific points — because that is the order they are laid out
    /// in and the order the panel reads down.
    /// </summary>
    public enum LogisticsKind
    {
        /// <summary>Strategic stock, well to the rear. The source everything else draws from.</summary>
        SupplyDepot,
        /// <summary>Forward stock, in the formation's own rear area.</summary>
        SupplyPoint,
        /// <summary>Refuelling.</summary>
        FuelPoint,
        /// <summary>Ammunition resupply.</summary>
        AmmoPoint,
        /// <summary>Recovery and repair of vehicles.</summary>
        RepairPoint,
        /// <summary>Casualty collection and treatment.</summary>
        MedicalPoint
    }

    /// <summary>
    /// What an installation actually does for a formation standing inside it.
    ///
    /// One service per kind rather than a set, deliberately. A fuel point
    /// refuels and an ammunition point rearms; something that did both would be
    /// a supply point, which is exactly what <see cref="LogisticsKind.SupplyPoint"/>
    /// is for — and it is the one kind here that legitimately hands out
    /// everything.
    /// </summary>
    public enum SupplyService
    {
        /// <summary>Drawn on the map and nothing more — repair has no state to restore yet.</summary>
        None,
        /// <summary>Rounds. Refills <c>UnitState.ammo</c> toward the type's establishment.</summary>
        Ammunition,
        /// <summary>Fuel. Refills <c>UnitState.fuel</c>.</summary>
        Fuel,
        /// <summary>
        /// Casualty treatment. Returns lightly wounded to duty — a slow recovery
        /// of strength, capped well short of full: a medical point treats
        /// casualties, it does not replace a destroyed battalion.
        /// </summary>
        Medical,
        /// <summary>Everything above, at a reduced rate. The forward supply point.</summary>
        General
    }

    /// <summary>One kind of installation, as the panel and the map graphic read it.</summary>
    public readonly struct LogisticsDef
    {
        public readonly LogisticsKind kind;
        /// <summary>Name on the button and on the map caption.</summary>
        public readonly string name;
        /// <summary>The one line under it — what the installation is for.</summary>
        public readonly string detail;
        /// <summary>
        /// Ground it serves, km. Drawn as a flat ring around the site so the
        /// coverage of a laydown can be seen rather than guessed — the same
        /// reason a unit's weapon range is drawn.
        /// </summary>
        public readonly float serviceRadiusKm;
        /// <summary>
        /// Accent for the marker and the button glyph. The *side* decides the
        /// marker's frame colour; this distinguishes one function from another
        /// within a side, which is the harder read on a busy rear area.
        /// </summary>
        public readonly Color tint;

        /// <summary>
        /// What a formation drawing on this installation gets back — see
        /// <see cref="Logistics.ResupplySystem"/>. <c>None</c> for the kinds
        /// that are still only a map graphic.
        /// </summary>
        public readonly SupplyService service;

        public LogisticsDef(LogisticsKind kind, string name, string detail,
            float serviceRadiusKm, Color tint, SupplyService service = SupplyService.None)
        {
            this.kind = kind;
            this.name = name;
            this.detail = detail;
            this.serviceRadiusKm = serviceRadiusKm;
            this.tint = tint;
            this.service = service;
        }
    }

    /// <summary>
    /// The logistics register in numbers — the single source the LOGISTICS
    /// panel, the map graphic and the save file all read.
    ///
    /// A catalogue rather than six hard-coded buttons, for the same reason the
    /// artillery natures and the movement tasks are catalogues: the panel is
    /// generated from <see cref="All"/>, so a seventh kind appears in the
    /// interface, on the map and in the save without any of them being touched.
    ///
    /// **The radii are service ranges, not blast radii.** They say how far the
    /// installation's ground extends, which is what makes a laydown judgeable:
    /// a depot covering the whole sector is in the wrong place if it is inside
    /// the enemy's reach, and a fuel point that reaches none of the armour is a
    /// fuel point in the wrong valley. See docs/26-LOGISTICS.md.
    /// </summary>
    public static class LogisticsCatalog
    {
        /// <summary>Declaration order is the order the panel lists them in.</summary>
        public static readonly LogisticsDef[] All =
        {
            new LogisticsDef(LogisticsKind.SupplyDepot, "SUPPLY DEPOT",
                "Strategic supply location", 25f, new Color(0.55f, 0.75f, 1.00f),
                SupplyService.General),
            new LogisticsDef(LogisticsKind.SupplyPoint, "SUPPLY POINT",
                "Forward supply location", 12f, new Color(0.45f, 0.85f, 0.70f),
                SupplyService.General),
            new LogisticsDef(LogisticsKind.FuelPoint, "FUEL POINT",
                "Refuel vehicles", 10f, new Color(1.00f, 0.72f, 0.28f),
                SupplyService.Fuel),
            new LogisticsDef(LogisticsKind.AmmoPoint, "AMMO POINT",
                "Replenish ammunition", 10f, new Color(1.00f, 0.55f, 0.30f),
                SupplyService.Ammunition),
            // Repair is the one kind with nothing to restore: vehicle state is
            // not modelled separately from a formation's strength, and quietly
            // healing strength here would make it a second medical point.
            new LogisticsDef(LogisticsKind.RepairPoint, "REPAIR POINT",
                "Recover and repair vehicles", 8f, new Color(0.75f, 0.78f, 0.85f)),
            new LogisticsDef(LogisticsKind.MedicalPoint, "MEDICAL POINT",
                "Treat and evacuate casualties", 8f, new Color(0.95f, 0.42f, 0.45f),
                SupplyService.Medical)
        };

        public static LogisticsDef Get(LogisticsKind kind)
        {
            foreach (var d in All) if (d.kind == kind) return d;
            return All[0];
        }

        /// <summary>
        /// Issues a **hand-placed** installation of this kind is stocked with
        /// when a scenario does not say.
        ///
        /// Generous, and generous on purpose: a depot the designer laid out is
        /// the rear area, not a cache, and running one dry in an afternoon's
        /// battle would turn a piece of scenario furniture into a timer. An
        /// airdrop is the opposite case and carries what the sortie carried —
        /// see <c>AirSupplyCatalog</c>.
        /// </summary>
        public static double DefaultStock(LogisticsKind kind) => kind switch
        {
            LogisticsKind.SupplyDepot => 40,
            LogisticsKind.SupplyPoint => 20,
            _ => 12
        };

        /// <summary>Parses a saved kind name, falling back rather than throwing on an old file.</summary>
        public static LogisticsKind Parse(string name) =>
            System.Enum.TryParse(name, out LogisticsKind kind) ? kind : LogisticsKind.SupplyPoint;
    }
}
