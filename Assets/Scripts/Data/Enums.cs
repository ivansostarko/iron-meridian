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

    /// <summary>
    /// How a unit type BEHAVES — the one thing gameplay code branches on.
    ///
    /// This is deliberately not the player-facing taxonomy: that is
    /// <see cref="UnitBranch"/>. A MANPADS team and a fighter squadron are both
    /// air defence to a planner, but only one of them stands on the ground, and
    /// this enum is about the standing.
    /// </summary>
    public enum UnitCategory
    {
        /// <summary>Formations of people and vehicles on the ground. They hold terrain and get a ground model.</summary>
        CoreGround,
        /// <summary>Unmanned air systems. No terrain, and no model — a rifleman would misrepresent them.</summary>
        Drone,
        /// <summary>Crewed aircraft and helicopters. No terrain, no ground model.</summary>
        Air,
        /// <summary>Vessels. No terrain, no ground model.</summary>
        Naval
    }

    public static class UnitCategoryInfo
    {
        public static string DisplayName(UnitCategory c) => c switch
        {
            UnitCategory.CoreGround => "Core Ground",
            UnitCategory.Drone => "Drone",
            UnitCategory.Air => "Air",
            UnitCategory.Naval => "Naval",
            _ => c.ToString()
        };
    }

    /// <summary>
    /// Which arm of service a unit type belongs to — the taxonomy the player
    /// sees and filters by on the Units screen. Purely descriptive: nothing in
    /// the combat or movement code reads it.
    ///
    /// <see cref="Other"/> is the deliberate catch-all for combat support that
    /// belongs to no single arm (engineers, signals, ISR, cyber, influence).
    /// Forcing those into one of the eight would say something false about them.
    /// </summary>
    public enum UnitBranch
    {
        Infantry,
        Armour,
        Mechanised,
        Artillery,
        AntiAircraft,
        Air,
        Navy,
        Logistics,
        Other
    }

    public static class UnitBranchInfo
    {
        /// <summary>Every branch in the order the Units screen lists them.</summary>
        public static readonly UnitBranch[] All =
        {
            UnitBranch.Infantry, UnitBranch.Armour, UnitBranch.Mechanised,
            UnitBranch.Artillery, UnitBranch.AntiAircraft, UnitBranch.Air,
            UnitBranch.Navy, UnitBranch.Logistics, UnitBranch.Other
        };

        /// <summary>Display name. Only the two-word ones differ from the enum name.</summary>
        public static string DisplayName(UnitBranch b) => b switch
        {
            UnitBranch.AntiAircraft => "Anti-Aircraft",
            _ => b.ToString()
        };

        /// <summary>
        /// Short form for the Units screen's filter buttons, which carry a count
        /// beside the label in 96 px. "ARTILLERY 13" does not fit; "ARTY 13" does.
        /// </summary>
        public static string ShortName(UnitBranch b) => b switch
        {
            UnitBranch.Infantry => "INF",
            UnitBranch.Armour => "ARMR",
            UnitBranch.Mechanised => "MECH",
            UnitBranch.Artillery => "ARTY",
            UnitBranch.AntiAircraft => "AA",
            UnitBranch.Air => "AIR",
            UnitBranch.Navy => "NAVY",
            UnitBranch.Logistics => "LOG",
            UnitBranch.Other => "OTHER",
            _ => b.ToString().ToUpperInvariant()
        };

        /// <summary>What the branch is, one line, for the Units screen.</summary>
        public static string Blurb(UnitBranch b) => b switch
        {
            UnitBranch.Infantry => "Dismounted manoeuvre formations. They fight on their feet and hold the worst ground.",
            UnitBranch.Armour => "Tanks, cavalry and the anti-armour arm that exists to kill them.",
            UnitBranch.Mechanised => "Infantry that rides to the fight. Armour and speed come from the vehicle.",
            UnitBranch.Artillery => "Everything that shoots at what it cannot see, and the observers that aim it.",
            UnitBranch.AntiAircraft => "Ground-based air and missile defence, with the radars and C2 that make it a system.",
            UnitBranch.Air => "Crewed aviation and unmanned systems. Neither holds ground.",
            UnitBranch.Navy => "Vessels. Weeks of fuel and rations, and no terrain to take.",
            UnitBranch.Logistics => "Sustainment. What the rest of the order of battle is fighting to protect.",
            UnitBranch.Other => "Combat support belonging to no single arm: engineers, signals, ISR, influence, cyber.",
            _ => ""
        };
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
        PhaseLine,

        /// <summary>
        /// Battle position: the ground a formation defends from, oriented on
        /// the enemy. Drawn as a closed area behind the defence line it belongs
        /// to (FM 3-90 ch.8).
        /// </summary>
        BattlePosition
    }

    /// <summary>
    /// Offensive tasks a unit can be given against a chosen enemy (FM 3-90
    /// ch.3 — forms of the attack). They differ in how close the attacker
    /// closes, how hard it hits, and what it is trying to achieve: destruction,
    /// suppression, or surprise.
    /// </summary>
    public enum AttackTask
    {
        /// <summary>Deliberate attack: close to effective range and destroy the target.</summary>
        Attack,
        /// <summary>Close assault: get right on top of the objective. Decisive and expensive.</summary>
        Assault,
        /// <summary>Suppressive fire from maximum range: pin the target rather than kill it.</summary>
        Suppress,
        /// <summary>Lie in wait, concealed, and strike the target when it comes into range.</summary>
        Ambush,
        /// <summary>Strike back at an enemy that is committed to its own attack.</summary>
        Counterattack
    }

    /// <summary>
    /// Reconnaissance and security tasks (FM 3-98). Every one of them exists to
    /// answer a question about the enemy, which with fog of war on is the only
    /// way to see anything beyond a unit's own eyes.
    /// </summary>
    public enum ReconTask
    {
        /// <summary>Move to a place and find out what is in it.</summary>
        ReconArea,
        /// <summary>Find out what is along a route, scanning the whole way there.</summary>
        ReconRoute,
        /// <summary>Sit still and watch. The furthest-seeing task, and the only static one.</summary>
        Observe,
        /// <summary>Fly a sensor out and back. Fast, wide, and it does not last.</summary>
        UavRecon,
        /// <summary>Patrol forward expecting to fight for the information.</summary>
        CombatPatrol
    }

    /// <summary>
    /// Point graphics pinned to the map by a defensive task. Unlike lines these
    /// mark a place rather than a limit, so they carry the owning unit and the
    /// direction the task is oriented on.
    /// </summary>
    public enum MarkerKind
    {
        /// <summary>Retain the terrain and accept no withdrawal from it.</summary>
        Hold,
        /// <summary>Screen the protected force forward of it, fighting within supporting range.</summary>
        Guard,
        /// <summary>Centre of a prepared defence, where its line and battle position meet.</summary>
        Defend
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
