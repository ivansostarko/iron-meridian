using UnityEngine;

namespace IronMeridian.Data
{
    /// <summary>What a piece of obstacle belongs to — the two halves of the panel.</summary>
    public enum ObstacleFamily { Mines, Obstacles }

    /// <summary>
    /// The mine and obstacle graphics a scenario can be given, in the order a
    /// panel lists them: mines first, then the obstacles they are usually tied
    /// into.
    /// </summary>
    public enum ObstacleKind
    {
        MinesGeneral,
        Minefield,
        AntiPersonnelMines,
        AntiTankMines,
        WireFence,
        AntiTankDitch,
        ObstacleGeneral,
        Roadblock
    }

    /// <summary>One obstacle type: what it is, what it looks like, and how big it draws.</summary>
    public readonly struct ObstacleDef
    {
        public readonly ObstacleKind kind;
        public readonly ObstacleFamily family;
        /// <summary>Name on the button and on the map caption.</summary>
        public readonly string name;
        /// <summary>The one line under it — what the thing does to an attacker.</summary>
        public readonly string detail;
        /// <summary>
        /// How wide the graphic is drawn on the ground, in metres.
        ///
        /// Doctrinal graphics have no inherent size — a minefield symbol says
        /// "mines here", not "mines over exactly this" — so the figure is chosen
        /// to read at the zoom this map is played at, and to be proportionate:
        /// a belt is wider than a single mine cluster, a roadblock narrower than
        /// either.
        /// </summary>
        public readonly float widthMeters;
        /// <summary>Tint. Obstacle graphics are conventionally drawn in the owning side's colour.</summary>
        public readonly Color tint;

        public ObstacleDef(ObstacleKind kind, ObstacleFamily family, string name, string detail,
            float widthMeters, Color tint)
        {
            this.kind = kind;
            this.family = family;
            this.name = name;
            this.detail = detail;
            this.widthMeters = widthMeters;
            this.tint = tint;
        }
    }

    /// <summary>
    /// The obstacle register — the single source the MINES AND OBSTACLES panel,
    /// the map graphic and the save file all read.
    ///
    /// **These are control measures, not units.** A minefield graphic says
    /// "there are mines here" to whoever is reading the map; it is not a thing
    /// that fights, is not counted in an order of battle, and does not belong to
    /// a formation. That is why they are their own system rather than units with
    /// no weapons — the same argument the logistic installations make
    /// (docs/26-LOGISTICS.md §1).
    ///
    /// **Nothing enforces them yet.** They are drawn, saved and removable; no
    /// movement or combat code reads them. See docs/31-OBSTACLES.md §6.
    /// </summary>
    public static class ObstacleCatalog
    {
        /// <summary>Mines are red-orange whoever lays them: the colour of danger, not of a side.</summary>
        static readonly Color MineTint = new Color(0.95f, 0.42f, 0.30f);
        /// <summary>Constructed obstacles in engineer green.</summary>
        static readonly Color WorkTint = new Color(0.60f, 0.82f, 0.55f);

        /// <summary>Declaration order is the order the panel lists them in.</summary>
        public static readonly ObstacleDef[] All =
        {
            new ObstacleDef(ObstacleKind.MinesGeneral, ObstacleFamily.Mines,
                "MINES", "Mines of unspecified type", 260f, MineTint),
            new ObstacleDef(ObstacleKind.Minefield, ObstacleFamily.Mines,
                "MINEFIELD", "A laid and recorded belt", 520f, MineTint),
            new ObstacleDef(ObstacleKind.AntiPersonnelMines, ObstacleFamily.Mines,
                "AP MINES", "Against dismounted infantry", 300f, MineTint),
            new ObstacleDef(ObstacleKind.AntiTankMines, ObstacleFamily.Mines,
                "AT MINES", "Against armour and vehicles", 320f, MineTint),

            new ObstacleDef(ObstacleKind.WireFence, ObstacleFamily.Obstacles,
                "WIRE FENCE", "Delays and channels infantry", 420f, WorkTint),
            new ObstacleDef(ObstacleKind.AntiTankDitch, ObstacleFamily.Obstacles,
                "AT DITCH", "Stops vehicles, not men", 480f, WorkTint),
            new ObstacleDef(ObstacleKind.ObstacleGeneral, ObstacleFamily.Obstacles,
                "OBSTACLE", "Obstacle of unspecified type", 380f, WorkTint),
            new ObstacleDef(ObstacleKind.Roadblock, ObstacleFamily.Obstacles,
                "ROADBLOCK", "Closes a route", 240f, WorkTint)
        };

        public static ObstacleDef Get(ObstacleKind kind)
        {
            foreach (var d in All) if (d.kind == kind) return d;
            return All[0];
        }

        /// <summary>Parses a saved kind name, falling back rather than throwing on an old file.</summary>
        public static ObstacleKind Parse(string name) =>
            System.Enum.TryParse(name, out ObstacleKind kind) ? kind : ObstacleKind.MinesGeneral;
    }
}
