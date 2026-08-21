using UnityEngine;
using IronMeridian.Models;
using IronMeridian.Vfx;

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
        /// <summary>Drawn on the map and nothing more. No kind uses this now.</summary>
        None,
        /// <summary>Rounds. Refills <c>UnitState.ammo</c> toward the type's establishment.</summary>
        Ammunition,
        /// <summary>Fuel. Refills <c>UnitState.fuel</c>.</summary>
        Fuel,
        /// <summary>
        /// Recovery and repair. Returns deadlined equipment to the road —
        /// refills <c>UnitState.serviceability</c>, and nothing else does.
        ///
        /// It restores **equipment, not people**, which is what keeps it from
        /// being a second medical point. A formation that walks takes nothing
        /// from a workshop and costs it nothing.
        /// </summary>
        Repair,
        /// <summary>
        /// Casualty treatment. Returns lightly wounded to duty — a slow recovery
        /// of strength, capped well short of full: a medical point treats
        /// casualties, it does not replace a destroyed battalion.
        /// </summary>
        Medical,
        /// <summary>Everything above, at a reduced rate. The forward supply point.</summary>
        General
    }

    /// <summary>
    /// One kind of installation, as the panel, the map graphic, the 3D model
    /// and the tuning screen all read it.
    ///
    /// **A class rather than a struct**, and its fields are writable. Every
    /// other data table in the game is a class for one reason —
    /// <see cref="TunableField"/> reflects over public instance fields and
    /// writes them, so DEVELOPMENT → UNITS LIST can tune a record live and
    /// <c>TuningStore</c> can patch it on load. A readonly struct is a value
    /// copy the moment it is boxed, so an edit would land on a copy and
    /// silently do nothing. See docs/26-LOGISTICS.md.
    /// </summary>
    public class LogisticsDef
    {
        /// <summary>
        /// Which kind this row is. Writable only because every field on a
        /// reflected record has to be — the tuning screen is told to show it
        /// read-only through <c>CatalogGroup.readOnlyFields</c>, the same way
        /// an artillery nature's calibre is: it is identity, not tuning.
        /// </summary>
        public LogisticsKind kind;
        /// <summary>Name on the button and on the map caption.</summary>
        public string name;
        /// <summary>The one line under it — what the installation is for.</summary>
        public string detail;
        /// <summary>
        /// Ground it serves, km. Drawn as a terrain-draped ring around the site
        /// so the coverage of a laydown can be seen rather than guessed — the
        /// same instrument, and the same <see cref="Units.RangeRing"/>, a unit's
        /// weapon range is drawn with. See docs/26-LOGISTICS.md §4.
        /// </summary>
        public float serviceRadiusKm;

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
        public double defaultStock;

        /// <summary>
        /// Accent for the marker and the button glyph. The *side* decides the
        /// marker's frame colour; this distinguishes one function from another
        /// within a side, which is the harder read on a busy rear area.
        ///
        /// Not tunable — <see cref="TunableField"/> skips colours, which is the
        /// right call: a colour picker is not a text field.
        /// </summary>
        public Color tint;

        /// <summary>
        /// What a formation drawing on this installation gets back — see
        /// <see cref="Logistics.ResupplySystem"/>. <c>None</c> for the kinds
        /// that are still only a map graphic.
        /// </summary>
        public SupplyService service;

        /// <summary>
        /// The 3D model stood on the ground under the marker when unit models
        /// are switched on — an id in <see cref="UnitModelLibrary"/>, resolved
        /// through the library and never with a <c>Resources.Load</c> at a call
        /// site (golden rule 10). See docs/09-3D-MODELS.md.
        /// </summary>
        public string modelId;

        /// <summary>
        /// The looping effect played over a working installation.
        ///
        /// On the row rather than at the call site, because the catalogue is
        /// where an effect's appearance lives (golden rule 11) — and because
        /// this is the hook a kind that eventually wants its own signature
        /// (vapour over fuel, dust over a repair bay) hangs on without the site
        /// code learning what kind it is drawing.
        /// </summary>
        public VfxId siteVfx;

        public LogisticsDef(LogisticsKind kind, string name, string detail,
            float serviceRadiusKm, double defaultStock, Color tint, string modelId,
            SupplyService service = SupplyService.None,
            VfxId siteVfx = VfxId.LogisticsSiteHaze)
        {
            this.kind = kind;
            this.name = name;
            this.detail = detail;
            this.serviceRadiusKm = serviceRadiusKm;
            this.defaultStock = defaultStock;
            this.tint = tint;
            this.modelId = modelId;
            this.service = service;
            this.siteVfx = siteVfx;
        }
    }

    /// <summary>
    /// The logistics register in numbers — the single source the LOGISTICS
    /// panel, the map graphic, the 3D model library, the DEVELOPMENT and EXTRAS
    /// reference screens and the save file all read.
    ///
    /// A catalogue rather than six hard-coded buttons, for the same reason the
    /// artillery natures and the movement tasks are catalogues: the panel is
    /// generated from <see cref="All"/>, so a seventh kind appears in the
    /// interface, on the map, in the reference screens and in the save without
    /// any of them being touched.
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
                "Rearms, refuels, repairs and treats", 25f, 40, new Color(0.55f, 0.75f, 1.00f),
                UnitModelLibrary.SupplyDepotSite, SupplyService.General),
            new LogisticsDef(LogisticsKind.SupplyPoint, "SUPPLY POINT",
                "Forward supply — everything, at a reduced rate", 12f, 20,
                new Color(0.45f, 0.85f, 0.70f),
                UnitModelLibrary.SupplyPointSite, SupplyService.General),
            new LogisticsDef(LogisticsKind.FuelPoint, "FUEL POINT",
                "Refuel vehicles", 10f, 12, new Color(1.00f, 0.72f, 0.28f),
                UnitModelLibrary.FuelPointSite, SupplyService.Fuel),
            new LogisticsDef(LogisticsKind.AmmoPoint, "AMMO POINT",
                "Replenish ammunition", 10f, 12, new Color(1.00f, 0.55f, 0.30f),
                UnitModelLibrary.AmmoPointSite, SupplyService.Ammunition),
            // Repair used to restore nothing, because vehicle state was not
            // modelled apart from a formation's strength and healing strength
            // here would have made it a second medical point. It is modelled now
            // — UnitState.serviceability — so the workshop has the one job on
            // this map that is genuinely its own.
            new LogisticsDef(LogisticsKind.RepairPoint, "REPAIR POINT",
                "Return deadlined vehicles to the road", 8f, 12,
                new Color(0.75f, 0.78f, 0.85f),
                UnitModelLibrary.RepairPointSite, SupplyService.Repair),
            new LogisticsDef(LogisticsKind.MedicalPoint, "MEDICAL POINT",
                "Treat and evacuate casualties", 8f, 12, new Color(0.95f, 0.42f, 0.45f),
                UnitModelLibrary.MedicalPointSite, SupplyService.Medical)
        };

        public static LogisticsDef Get(LogisticsKind kind)
        {
            foreach (var d in All) if (d.kind == kind) return d;
            return All[0];
        }

        /// <summary>
        /// Issues a hand-placed installation of this kind is stocked with when
        /// the scenario does not say — read off the row, so tuning it in
        /// DEVELOPMENT → UNITS LIST changes what the next site is laid out with.
        /// </summary>
        public static double DefaultStock(LogisticsKind kind) => Get(kind).defaultStock;

        /// <summary>Parses a saved kind name, falling back rather than throwing on an old file.</summary>
        public static LogisticsKind Parse(string name) =>
            System.Enum.TryParse(name, out LogisticsKind kind) ? kind : LogisticsKind.SupplyPoint;
    }
}
