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
    /// Offensive tasks a unit can be given (FM 3-90 ch.3).
    ///
    /// **One task, deliberately.** There used to be five — attack, assault,
    /// suppress, ambush, counterattack — and they were five rows in a menu
    /// separated by numbers the player could not see. What a commander is
    /// actually deciding at this level is *where to attack*, and the answer is a
    /// place on the map or a formation standing on one. The variations belong to
    /// a later model that can show what they buy.
    /// </summary>
    public enum AttackTask
    {
        /// <summary>Close to effective range and destroy whatever is in the objective.</summary>
        Attack
    }

    /// <summary>
    /// Reconnaissance tasks (FM 3-98). One, for the same reason the attack menu
    /// has one: with fog of war on, the question is always *which ground do I
    /// want to see*, and the answer is an area.
    /// </summary>
    public enum ReconTask
    {
        /// <summary>Move to an area and search it.</summary>
        ReconArea
    }

    /// <summary>
    /// How a formation moves. Every one of these ends with the unit somewhere
    /// else; what differs is the speed it accepts, the readiness it keeps, and —
    /// for the last two — whether the move is ordered now or held as a
    /// contingency against the unit's own strength.
    /// </summary>
    public enum MoveTask
    {
        /// <summary>March at the formation's own speed.</summary>
        Move,
        /// <summary>Road march: faster, strung out, and in no state to fight on arrival.</summary>
        FastMove,
        /// <summary>Bounding advance: slower, in contact-ready formation.</summary>
        TacticalMove,
        /// <summary>
        /// Break contact to a line to the rear, **when the formation is down to
        /// half strength**. A planned move, executed by the unit rather than by
        /// the player.
        /// </summary>
        Withdraw,
        /// <summary>
        /// Fall back to a rally point, **when the formation is down to a third**.
        /// The harder trigger and the more urgent move.
        /// </summary>
        Retreat
    }

    /// <summary>
    /// The standing behaviours a formation carries between orders. Unlike a task
    /// these have no objective and never complete — they are switches on how the
    /// unit behaves when nothing else is telling it what to do.
    /// </summary>
    public enum UnitCommand
    {
        /// <summary>Cancel every order and stand still.</summary>
        Stop,
        /// <summary>Roam within <see cref="FreeMovementRadiusKm"/> when idle.</summary>
        FreeMovement,
        /// <summary>Engage anything that comes into range without being told.</summary>
        AutomaticAttack
    }

    /// <summary>
    /// A drawn intention rather than an order. The planner puts the shape of an
    /// operation on the map; nothing executes it.
    /// </summary>
    public enum PlanKind
    {
        /// <summary>The decisive effort — the heavy axis.</summary>
        MainAttack,
        /// <summary>The shaping effort — a lighter axis that fixes or diverts.</summary>
        SupportingAttack
    }

    /// <summary>
    /// The shape a task draws on the ground. One of three, because the three
    /// answer different questions: *how far from here* (a ring), *which line do
    /// I hold* (a line), and *which ground do I cover* (quadrants).
    /// </summary>
    public enum TaskAreaShape
    {
        /// <summary>A circle about a point, with its radius called out on the rim.</summary>
        Ring,
        /// <summary>A bowed line across the threat axis, labelled along its length.</summary>
        Line,
        /// <summary>Four sectors about a point, each labelled on its own border.</summary>
        Quadrants
    }

    /// <summary>
    /// Point graphics pinned to the map by a task. Unlike lines these mark a
    /// place rather than a limit, so they carry the owning unit and the
    /// direction the task is oriented on.
    /// </summary>
    public enum MarkerKind
    {
        /// <summary>Retain the terrain and accept no withdrawal from it.</summary>
        Hold,
        /// <summary>Screen the protected force forward of it, fighting within supporting range.</summary>
        Guard,
        /// <summary>Centre of a prepared defence, where its line and battle position meet.</summary>
        Defend,
        /// <summary>The ground an attack is being made onto.</summary>
        Attack,
        /// <summary>The area a formation has been told to search.</summary>
        Recon,
        /// <summary>The line a formation breaks contact to at half strength.</summary>
        Withdraw,
        /// <summary>The rally point a formation falls back to at a third strength.</summary>
        Retreat
    }

    /// <summary>Numbers the standing commands are defined by.</summary>
    public static class CommandInfo
    {
        /// <summary>
        /// How far a formation on FREE MOVEMENT will wander from where it was
        /// released, in km. Deliberately large: this is not a patrol radius, it
        /// is "you have this much of the map to work in", and anything under a
        /// few tens of kilometres would make an operational formation look
        /// tethered.
        /// </summary>
        public const double FreeMovementRadiusKm = 50.0;

        /// <summary>Strength at or below which a WITHDRAW order executes itself.</summary>
        public const float WithdrawTriggerStrength = 0.50f;
        /// <summary>Strength at or below which a RETREAT order executes itself.</summary>
        public const float RetreatTriggerStrength = 0.30f;
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

        /// <summary>
        /// How much ground a formation of this size actually occupies, as a
        /// radius in metres.
        ///
        /// **Why this has to exist.** A unit is stored as a single lat/lon and
        /// drawn as one counter, but a battalion is not a point — it is a
        /// kilometre or so of dispersed sub-units, vehicles and positions. Any
        /// code that asks "did this shell land on that formation" by measuring
        /// to the stored coordinate is asking whether it landed on the
        /// formation's *centre*, which is a far harder question and the wrong
        /// one. A 155 mm round has a blast radius of about 130 m; against a
        /// point that means you have to drop it within 130 m of one specific
        /// spot, and a fire mission that visibly straddles a brigade does
        /// nothing at all. See <see cref="Units.BlastDamage"/>.
        ///
        /// The figures are deliberately conservative — a real deployed
        /// battalion frontage is wider than 550 m — because the counter is what
        /// the player is aiming at and the footprint must not stretch so far
        /// beyond the symbol that damage arrives from somewhere they can see is
        /// clear of it.
        /// </summary>
        public static float FootprintRadiusMeters(Echelon e) => e switch
        {
            Echelon.Team      => 25f,
            Echelon.Squad     => 40f,
            Echelon.Section   => 55f,
            Echelon.Platoon   => 110f,
            Echelon.Company   => 220f,
            Echelon.Battalion => 550f,
            Echelon.Regiment  => 900f,
            Echelon.Brigade   => 1300f,
            Echelon.Division  => 2400f,
            Echelon.Corps     => 4200f,
            Echelon.Army      => 7000f,
            _ => 220f
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
