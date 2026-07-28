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

    public enum LineKind
    {
        Boundary,       // sector boundary separating the two teams
        DefensiveLine   // fortified defensive line
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
