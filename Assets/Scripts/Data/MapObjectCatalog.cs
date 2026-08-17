namespace IronMeridian.Data
{
    /// <summary>
    /// Things on the ground that are not formations and not control measures:
    /// the infrastructure an operational plan is written around.
    ///
    /// **Why they are drawn rather than dropped.** A depot is a place and a
    /// marker says it well enough (see <see cref="LogisticsKind"/>); a bridge,
    /// an airfield or a city is an *extent*. What matters about them is how much
    /// ground they cover, where their ends are, and what has to be held to hold
    /// them — none of which a point can say. So each is a polygon of at least
    /// four corners, drawn on the terrain.
    /// </summary>
    public enum MapObjectKind
    {
        Bridge,
        Airfield,
        Hospital,
        Port,
        RailYard,
        PowerStation,
        FuelTerminal,
        Factory,
        BuiltUpArea,
        Dam
    }

    /// <summary>One kind of map object: what it is called and how it draws.</summary>
    public class MapObjectDef
    {
        public MapObjectKind kind;

        /// <summary>Name on the button and on the ground.</summary>
        public string name;

        /// <summary>Outline colour, "#RRGGBB". Tinted toward the side that owns it when drawn.</summary>
        public string colorHex;

        /// <summary>Outline width on the ground, metres.</summary>
        public float widthMeters;

        /// <summary>What it is, and what it is worth to a plan — shown on the panel.</summary>
        public string description;
    }

    /// <summary>
    /// The register of map objects in code. Keep it in step with
    /// docs/33-MAP-OBJECTS.md, the human-readable version of this table.
    /// </summary>
    public static class MapObjectCatalog
    {
        /// <summary>
        /// Corners a map object must have before it can be closed.
        ///
        /// Four, not three: everything here is a built thing with an extent —
        /// a span, a runway, a yard, a quarter of a town — and a triangle is a
        /// shape none of them are. It also stops a stray double-click from
        /// leaving a sliver on the map that has to be found again to delete.
        /// </summary>
        public const int MinCorners = 4;

        static readonly MapObjectDef[] Defs =
        {
            new MapObjectDef
            {
                kind = MapObjectKind.Bridge, name = "BRIDGE",
                colorHex = "#E8C15A", widthMeters = 90f,
                description = "A crossing and its approaches. The one object whose loss can stop " +
                              "a manoeuvre outright — see docs/15-COMBAT-ORDERS.md on bridge seizure."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.Airfield, name = "AIRFIELD",
                colorHex = "#6FB3E8", widthMeters = 140f,
                description = "Runway, apron and dispersals. Where air support and air supply come from."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.Hospital, name = "HOSPITAL",
                colorHex = "#E86F86", widthMeters = 90f,
                description = "A medical facility. Protected under the laws of armed conflict — " +
                              "worth marking so it is not fired on by accident."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.Port, name = "PORT",
                colorHex = "#5ED0C0", widthMeters = 140f,
                description = "Quays and the water they front. A theatre's throughput, and the " +
                              "reason a coastal flank is worth a division."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.RailYard, name = "RAIL YARD",
                colorHex = "#B48FE0", widthMeters = 110f,
                description = "Sidings and a transhipment point. Rail is how heavy formations move " +
                              "any distance worth the name."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.PowerStation, name = "POWER STATION",
                colorHex = "#E8A25A", widthMeters = 110f,
                description = "Generation and its switchyard. Taking it out is an operational-level " +
                              "decision with civil consequences."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.FuelTerminal, name = "FUEL TERMINAL",
                colorHex = "#D8E85A", widthMeters = 110f,
                description = "Tank farm and pumping. What an armoured force runs on — see " +
                              "docs/27-SUSTAINMENT.md."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.Factory, name = "FACTORY",
                colorHex = "#9AA5B1", widthMeters = 110f,
                description = "Industry worth holding, denying or repairing."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.BuiltUpArea, name = "BUILT-UP AREA",
                colorHex = "#C8CDD4", widthMeters = 160f,
                description = "A town or a quarter of a city. Slow going, short sight lines, and " +
                              "ground that costs a battalion a day."
            },
            new MapObjectDef
            {
                kind = MapObjectKind.Dam, name = "DAM",
                colorHex = "#7FE87F", widthMeters = 110f,
                description = "A dam and its reservoir edge. Breaching one changes the going for " +
                              "everybody downstream."
            }
        };

        public static MapObjectDef Get(MapObjectKind kind)
        {
            foreach (var d in Defs) if (d.kind == kind) return d;
            return Defs[0];
        }

        public static System.Collections.Generic.IReadOnlyList<MapObjectDef> All => Defs;
    }
}
