namespace IronMeridian.Data
{
    /// <summary>Gameplay side. The player commands Blue, the AI commands Red.</summary>
    public enum Team
    {
        User,   // Blue
        Enemy   // Red
    }

    /// <summary>APP-6 style standard identity / affiliation.</summary>
    public enum Affiliation
    {
        Friendly,
        Hostile,
        Neutral,
        Unknown
    }

    /// <summary>Unit echelon from smallest to largest.</summary>
    public enum Echelon
    {
        Team,
        Squad,
        Section,
        Platoon,
        Company,
        Battalion,
        Regiment,
        Brigade,
        Division,
        Corps,
        Army
    }

    public enum UnitCategory
    {
        CoreGround,
        Drone
    }

    /// <summary>High level status of a deployed unit.</summary>
    public enum UnitStatus
    {
        Idle,
        Moving,
        Engaging,
        Suppressed,
        Routed,
        Destroyed
    }

    public enum ViewMode
    {
        Mode2D,
        Mode3D
    }

    /// <summary>Imagery draped on the terrain tileset.</summary>
    public enum MapStyle
    {
        Satellite,       // Bing Maps Aerial (ion asset 2)
        Terrain,         // bare shaded relief, no imagery overlay
        Roads,           // Bing Maps Road (ion asset 4)
        SatelliteLabels, // Bing Maps Aerial with place labels (ion asset 3)
        Sentinel2,       // Sentinel-2 cloudless mosaic (ion asset 3954)
        OpenStreetMap    // OSM raster tiles, via a URL-template overlay
    }

    /// <summary>
    /// Tactical control measures, following APP-6A / FM 101-5-1 naming.
    /// Older saves only contain Boundary and DefensiveLine; both are kept so
    /// existing scenario files keep loading.
    /// </summary>
    public enum LineKind
    {
        Boundary,        // legacy: the auto front line between the two teams
        DefensiveLine,   // legacy: hand-drawn fortified line

        /// <summary>
        /// Lateral boundary: the left/right limit of a formation's area of
        /// operations. Runs rear-to-front, roughly perpendicular to the front,
        /// and extends beyond the FLOT (FM 3-90 ch.8).
        /// </summary>
        LateralBoundary,

        /// <summary>
        /// Rear boundary: defines the rear of a formation's AO. Runs parallel
        /// to the front; the area behind it belongs to the higher commander.
        /// </summary>
        RearBoundary,

        /// <summary>
        /// Forward Edge of the Battle Area — the foremost limit of the areas
        /// where ground combat units are deployed, excluding screening forces.
        /// This is the "defence line" connecting the forward defending units.
        /// </summary>
        Feba,

        /// <summary>Named reference line for control and coordination.</summary>
        PhaseLine
    }

    public static class EchelonInfo
    {
        /// <summary>Manpower multiplier applied to a definition's base (company-level) manpower.</summary>
        public static float ManpowerMultiplier(Echelon e) => e switch
        {
            Echelon.Team      => 0.04f,
            Echelon.Squad     => 0.08f,
            Echelon.Section   => 0.12f,
            Echelon.Platoon   => 0.30f,
            Echelon.Company   => 1.00f,
            Echelon.Battalion => 4.5f,
            Echelon.Regiment  => 12f,
            Echelon.Brigade   => 30f,
            Echelon.Division  => 120f,
            Echelon.Corps     => 350f,
            Echelon.Army      => 900f,
            _ => 1f
        };

        /// <summary>APP-6 echelon indicator drawn above the unit frame.</summary>
        public static string Indicator(Echelon e) => e switch
        {
            Echelon.Team      => "Ø",
            Echelon.Squad     => "●",
            Echelon.Section   => "●●",
            Echelon.Platoon   => "●●●",
            Echelon.Company   => "|",
            Echelon.Battalion => "||",
            Echelon.Regiment  => "|||",
            Echelon.Brigade   => "X",
            Echelon.Division  => "XX",
            Echelon.Corps     => "XXX",
            Echelon.Army      => "XXXX",
            _ => ""
        };
    }
}
