using System;
using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>
    /// One deployed unit inside a map save. Positions are geodetic (WGS84),
    /// so saves are independent of the Unity origin.
    /// </summary>
    [Serializable]
    public class UnitState
    {
        public string instanceId;       // unique per placed unit
        public string defId;            // UnitDefinition.id
        public string team;             // "User" | "Enemy"
        public string affiliation;      // Friendly | Hostile | Neutral | Unknown
        public string echelon;          // Echelon name
        public string customName;       // e.g. "2nd Battalion, 4th Infantry"
        public string groupId;          // shared id for units grouped together, "" if none
        public string groupName;        // player-chosen group name, "" if none
        /// <summary>
        /// <see cref="CommanderState.id"/> of the officer commanding this
        /// formation, "" if none. One commander, many units — the inverse
        /// (a list on the commander) would need keeping in step with this on
        /// every reassignment and every deletion.
        /// </summary>
        public string commanderId = "";

        public double latitude;
        public double longitude;
        public double heightMeters;
        public float headingDeg;

        public float strength;          // 0..1 remaining strength
        public float organisation;      // 0..100 current
        public float morale;            // 0..100 current
        public string status;           // UnitStatus name

        // Current sustainment state (starts from definition stocks)
        public int ammo;
        public float fuel;
        public int foodDays;

        // --- standing commands (see UnitCommand and docs/15-COMBAT-ORDERS.md) ---
        //
        // Not orders: switches on how the formation behaves when nothing else is
        // telling it what to do. Saved with the unit, because "this battery does
        // not shoot at what wanders past" is a property of the scenario as much
        // as its position is.

        /// <summary>
        /// Roam within <see cref="CommandInfo.FreeMovementRadiusKm"/> of where
        /// it was released, when idle. **Off by default**: a formation that
        /// wandered off the ground the player put it on, because they did not
        /// know a switch existed, would be the game losing their scenario for
        /// them.
        /// </summary>
        public bool freeMovement;

        /// <summary>
        /// Engage anything that comes into range without being told. **On by
        /// default**, because that is what every formation did before this
        /// existed and a map full of units that had silently stopped fighting
        /// would be inexplicable. Turning it off is how a screen or a
        /// reconnaissance element is kept out of a fight it cannot win.
        /// </summary>
        public bool automaticAttack = true;

        /// <summary>
        /// Where FREE MOVEMENT is measured from — set when it is switched on, so
        /// the radius is anchored to the ground the player released the unit on
        /// rather than creeping along behind it.
        /// </summary>
        public double freeMovementLatitude, freeMovementLongitude;

        /// <summary>
        /// Deep copy. UnitState is a reference type shared with the live actor,
        /// so clipboard and undo snapshots must copy it or they would track the
        /// unit's later edits instead of recording its state at the time.
        /// </summary>
        public UnitState Clone() => new UnitState
        {
            instanceId = instanceId,
            defId = defId,
            team = team,
            affiliation = affiliation,
            echelon = echelon,
            customName = customName,
            groupId = groupId,
            groupName = groupName,
            commanderId = commanderId,
            latitude = latitude,
            longitude = longitude,
            heightMeters = heightMeters,
            headingDeg = headingDeg,
            strength = strength,
            organisation = organisation,
            morale = morale,
            status = status,
            ammo = ammo,
            fuel = fuel,
            foodDays = foodDays,
            freeMovement = freeMovement,
            automaticAttack = automaticAttack,
            freeMovementLatitude = freeMovementLatitude,
            freeMovementLongitude = freeMovementLongitude
        };

        public Team TeamEnum => team == "Enemy" ? Team.Enemy : Team.User;
        public Affiliation AffiliationEnum =>
            Enum.TryParse<Affiliation>(affiliation, out var a) ? a : Affiliation.Unknown;
        public Echelon EchelonEnum =>
            Enum.TryParse<Echelon>(echelon, out var e) ? e : Echelon.Company;
    }

    [Serializable]
    public class GeoPoint
    {
        public double latitude;
        public double longitude;
        public double heightMeters;
    }

    /// <summary>
    /// A tactical control measure drawn on the map — boundary, FEBA, phase
    /// line. Modelled on MIL-STD-2525 N-point tactical graphics (an ordered
    /// vertex list plus amplifiers), so these could be exported to KML/CoT
    /// later without reshaping the data.
    ///
    /// Fields added after the original schema default harmlessly on old saves:
    /// JsonUtility leaves missing fields at their initialiser values.
    /// </summary>
    [Serializable]
    public class MapLineData
    {
        public string id;
        public string kind;             // LineKind name
        public string team;             // owning side ("" when it separates both)
        public bool is3D;               // rendered clamped to terrain (3D) or flat (2D)
        public bool autoGenerated;      // true = maintained by a system, regenerated on demand
        public List<GeoPoint> points = new List<GeoPoint>();

        // ---- amplifiers (APP-6A / FM 101-5-1 labelling) ----

        /// <summary>Caption drawn on the line, e.g. "FEBA" or "PL BLUE".</summary>
        public string label = "";
        /// <summary>Echelon size marking straddling a boundary, e.g. "II", "X".</summary>
        public string echelon = "";
        /// <summary>Designation of the formation on each side of a boundary.</summary>
        public string leftLabel = "";
        public string rightLabel = "";
        /// <summary>Planned / on-order control measures are drawn broken, actual ones solid.</summary>
        public bool planned;

        /// <summary>
        /// Optional "#RRGGBB" override. Empty means the line takes the colour
        /// its kind and owning side imply, which is the doctrinally correct
        /// default — this exists for scenarios that need to distinguish more
        /// formations than the standard palette has colours for.
        /// </summary>
        public string colorHex = "";

        /// <summary>Optional width override in metres; 0 keeps the width implied by the kind.</summary>
        public float widthMeters;
    }

    /// <summary>
    /// A point graphic pinned to the map by a defensive task — a hold position,
    /// a guard position, or the centre of a prepared defence.
    ///
    /// Kept separate from <see cref="MapLineData"/> rather than stored as a
    /// one-vertex line: a marker states a place and a task, has no length, and
    /// belongs to the unit that was given the order, which is what lets it be
    /// cleared when that unit is re-tasked or destroyed.
    /// </summary>
    [Serializable]
    public class MapMarkerData
    {
        public string id;
        public string kind;             // MarkerKind name
        public string team;             // owning side
        /// <summary>instanceId of the unit holding this position, "" if none.</summary>
        public string unitId = "";
        /// <summary>Caption drawn under the marker, e.g. "HOLD".</summary>
        public string label = "";

        public double latitude;
        public double longitude;
        public double heightMeters;
        /// <summary>Direction the task is oriented on (deg from north) — usually the threat.</summary>
        public float headingDeg;
    }

    /// <summary>
    /// One mine or obstacle graphic on the map — see docs/31-OBSTACLES.md.
    ///
    /// A control measure rather than a unit or a task marker: it states
    /// something about the ground, belongs to the scenario, and outlives every
    /// formation that walks past it.
    /// </summary>
    [Serializable]
    public class ObstacleSiteData
    {
        public string id;
        /// <summary>ObstacleKind name — see <see cref="ObstacleCatalog"/>.</summary>
        public string kind;
        /// <summary>Owning side, a <see cref="Team"/> name.</summary>
        public string team;
        /// <summary>Caption drawn under the graphic; empty takes the catalogue's name.</summary>
        public string label = "";

        public double latitude;
        public double longitude;
        public double heightMeters;
        /// <summary>Direction the graphic is laid along, degrees from north.</summary>
        public float headingDeg;

        /// <summary>
        /// The ground the barrier actually covers, as a closed polygon — empty
        /// for a point graphic.
        ///
        /// **Why some barriers are areas and others are symbols.** A roadblock
        /// closes one place and a wire fence runs along one line; a *minefield*
        /// is a piece of ground, and the only thing anybody needs to know about
        /// it is where its edge is. Drawing it as a symbol with a nominal width
        /// meant the map could not answer that, and neither could the code: a
        /// unit driving into mines has to be tested against the ground they are
        /// in, not against a circle chosen to look right at one zoom level.
        ///
        /// <see cref="latitude"/>/<see cref="longitude"/> stay filled in for an
        /// area too — they carry its centroid, so picking, focusing and the list
        /// row all work without knowing which kind of graphic this is.
        ///
        /// See docs/31-OBSTACLES.md.
        /// </summary>
        public List<GeoPoint> points = new List<GeoPoint>();

        /// <summary>True when this record is a drawn area rather than a point symbol.</summary>
        public bool HasArea => points != null && points.Count >= 3;
    }

    /// <summary>
    /// One drawn map object — a bridge, an airfield, a built-up area. A polygon
    /// of at least four corners with a side that owns it; see
    /// <see cref="MapObjectCatalog"/> and docs/33-MAP-OBJECTS.md.
    /// </summary>
    [Serializable]
    public class MapObjectData
    {
        public string id = "";
        /// <summary>MapObjectKind name.</summary>
        public string kind = nameof(MapObjectKind.Bridge);
        /// <summary>Owning side, a <see cref="Team"/> name.</summary>
        public string team = nameof(Team.User);
        /// <summary>Caption drawn on the ground; empty takes the catalogue's name.</summary>
        public string label = "";

        public List<GeoPoint> points = new List<GeoPoint>();

        public MapObjectKind KindEnum =>
            Enum.TryParse(kind, out MapObjectKind k) ? k : MapObjectKind.Bridge;
        public Team TeamEnum => team == nameof(Team.Enemy) ? Team.Enemy : Team.User;

        public MapObjectData Clone() => new MapObjectData
        {
            id = id, kind = kind, team = team, label = label,
            points = points == null ? new List<GeoPoint>() : new List<GeoPoint>(points)
        };
    }

    /// <summary>
    /// One logistic installation on the map — a depot, a supply point, or one
    /// of the four function-specific points. See docs/26-LOGISTICS.md.
    ///
    /// Deliberately not a <see cref="MapMarkerData"/>. A task marker belongs to
    /// the unit that was given the order and is swept off the map when that
    /// unit goes; an installation belongs to the *scenario* and outlives every
    /// formation that draws on it.
    /// </summary>
    [Serializable]
    public class LogisticsSiteData
    {
        public string id;
        /// <summary>LogisticsKind name — see <see cref="LogisticsCatalog"/>.</summary>
        public string kind;
        /// <summary>Owning side, a <see cref="Team"/> name.</summary>
        public string team;
        /// <summary>Caption drawn under the marker; empty takes the catalogue's name.</summary>
        public string label = "";

        public double latitude;
        public double longitude;
        public double heightMeters;

        /// <summary>
        /// What the installation is holding, in **issues** — one issue being
        /// one formation's worth of whatever this kind hands out.
        ///
        /// A count of issues rather than litres and rounds because one number is
        /// what the player has to reason about ("this cache is good for four
        /// more battalions") and because the three loads are not comparable in
        /// their own units. What an issue actually restores is decided per kind
        /// by <see cref="Logistics.ResupplySystem"/>.
        ///
        /// Zero on a map saved before caches had stock, which
        /// <see cref="Capacity"/> reads as "not tracked" and treats as
        /// inexhaustible — the correct reading of a hand-placed depot on an old
        /// scenario, which was never meant to run out.
        /// </summary>
        public double stock;

        /// <summary>What it held when it was placed, so the panel can draw a fraction.</summary>
        public double capacity;

        /// <summary>
        /// True for a cache pushed out of an aeroplane rather than laid out by
        /// the designer.
        ///
        /// It changes how the thing is drawn — a dropped cache is a **3D model**
        /// standing on the ground, a placed installation is a doctrinal symbol
        /// on the overlay — because they are different sorts of object. A depot
        /// is a *place*: what matters is which one it is and how far it reaches,
        /// which is what a symbol says. A cache is a *thing somebody just put
        /// there*, and the player wants to see it land and then find it again.
        /// </summary>
        public bool airdropped;

        /// <summary>True when this site tracks a finite stock at all.</summary>
        public bool TracksStock => capacity > 0.0;
    }

    /// <summary>
    /// One side's stock of one resource — see docs/27-SUSTAINMENT.md.
    ///
    /// Only the *stocks* are saved. Consumption is derived from the units on
    /// the map every time it is asked for, so a scenario can never carry a burn
    /// rate that disagrees with its own order of battle.
    /// </summary>
    [Serializable]
    public class ResourceStockData
    {
        /// <summary>Owning side, a <see cref="Team"/> name.</summary>
        public string team;
        /// <summary>ResourceKind name — see <see cref="ResourceCatalog"/>.</summary>
        public string kind;
        public double quantity;
    }

    /// <summary>
    /// One formation that is not on the map yet, and when it arrives — see
    /// docs/30-REINFORCEMENTS.md.
    ///
    /// Scenario **minutes after the battle starts**, not an absolute clock
    /// time: a designer thinks in "forty minutes in", the figure survives
    /// changing H-hour, and it means the same thing at every game speed.
    /// </summary>
    [Serializable]
    public class ReinforcementEntry
    {
        /// <summary>UnitDefinition id — the type that arrives.</summary>
        public string defId;
        /// <summary>Owning side, a <see cref="Team"/> name.</summary>
        public string team;
        /// <summary>Echelon name — the size it arrives at.</summary>
        public string echelon = "Battalion";
        /// <summary>Minutes after the battle starts.</summary>
        public int arrivalMinutes = 30;

        /// <summary>
        /// How many formations of this type arrive together.
        ///
        /// One row rather than N identical rows in the schedule: a designer
        /// laying on a counter-attack is thinking "three battalions at H+40",
        /// not writing the same line three times, and a list that made them do
        /// the latter would also make removing one of the three a hunt. The
        /// arrival scatters them over the deployment zone as it would any other
        /// group.
        ///
        /// Defaults to 1 so a file written before this field existed loads as
        /// the single formation it was.
        /// </summary>
        public int count = 1;

        /// <summary>
        /// Runtime only: whether this arrival has come on in the battle now
        /// being fought. Never saved — a scenario file is a starting state.
        /// </summary>
        [NonSerialized] public bool arrived;
    }

    /// <summary>
    /// A complete scenario/map save. Serialized to JSON, one file per map, in
    /// persistentDataPath/Maps (user saves) or StreamingAssets/Maps (shipped defaults).
    /// </summary>
    [Serializable]
    public class MapSaveData
    {
        public string mapName = "New Map";
        public string savedAtUtc;

        // Camera / view
        public double centerLatitude;
        public double centerLongitude;
        public double cameraHeightMeters = 12000;
        public string viewMode = "Mode3D"; // Mode2D | Mode3D
        /// <summary>MapStyle name — see docs/02-CESIUM.md for the full list.</summary>
        public string mapStyle = "Satellite";
        /// <summary>3D buildings layer. Independent of viewMode; see docs/02-CESIUM.md.</summary>
        public bool showBuildings = true;

        /// <summary>
        /// Scenario H-hour, round-tripped as "yyyy-MM-dd HH:mm" — a sortable,
        /// culture-independent form that stays readable when someone edits the
        /// JSON by hand. Empty or unparseable falls back to GameClock.DefaultStart.
        /// See docs/13-DATE-AND-TIME.md.
        /// </summary>
        public string startDateTime = "1990-01-01 14:00";

        // Weather — see docs/14-WEATHER.md. Sky and condition are separate axes,
        // so a night storm round-trips correctly.
        /// <summary>SkyPhase name: Day | Sunset | Night. Ignored when autoDayNight is true.</summary>
        public string skyPhase = "Day";
        /// <summary>WeatherCondition name: Clear | Overcast | Fog | Rain | Storm | Snow.</summary>
        public string weatherCondition = "Clear";
        /// <summary>When true the scenario clock drives the sky and skyPhase is ignored.</summary>
        public bool autoDayNight;

        public List<UnitState> units = new List<UnitState>();
        public List<MapLineData> lines = new List<MapLineData>();
        /// <summary>Hold / guard / defend positions — see docs/03-GAMEPLAY.md.</summary>
        public List<MapMarkerData> markers = new List<MapMarkerData>();
        /// <summary>
        /// Depots and supply, fuel, ammunition, repair and medical points.
        /// Empty on a map saved before logistics existed, which reads correctly
        /// as "this scenario has no rear area" — see docs/26-LOGISTICS.md.
        /// </summary>
        public List<LogisticsSiteData> logistics = new List<LogisticsSiteData>();

        /// <summary>
        /// Drawn infrastructure — bridges, airfields, built-up areas. Empty on
        /// every map written before they existed, which reads correctly as "this
        /// scenario names none". See docs/33-MAP-OBJECTS.md.
        /// </summary>
        public List<MapObjectData> mapObjects = new List<MapObjectData>();
        /// <summary>
        /// What each side has in stock — fuel, ammunition natures, replacements
        /// and the rest. Empty on an older map, which reads as a force with
        /// nothing behind it; the panel's STOCK FROM FORCE fills it in one
        /// click. See docs/27-SUSTAINMENT.md.
        /// </summary>
        public List<ResourceStockData> resources = new List<ResourceStockData>();

        /// <summary>
        /// FlotMode name — how the front line is produced (Automatic, Manual,
        /// Hybrid). The manual trace itself is an ordinary line in
        /// <see cref="lines"/> (id "flot-manual"), so only the mode needs its
        /// own field. Empty on an older map reads as Automatic.
        /// </summary>
        public string flotMode = "";

        /// <summary>
        /// Formations scheduled to arrive after the battle starts. Empty on an
        /// older map, which reads as "everything this scenario has is already
        /// on it" — see docs/30-REINFORCEMENTS.md.
        /// </summary>
        public List<ReinforcementEntry> reinforcements = new List<ReinforcementEntry>();

        /// <summary>
        /// Mine and obstacle graphics — the barrier plan. Empty on a map saved
        /// before they existed, which reads correctly as "nothing is mined".
        /// See docs/31-OBSTACLES.md.
        /// </summary>
        public List<ObstacleSiteData> obstacles = new List<ObstacleSiteData>();
        /// <summary>
        /// The order of battle above the units: who commands what, on both
        /// sides. Empty on a map saved before commanders existed, which reads
        /// correctly as "nobody is in command" — see docs/23-COMMANDERS.md.
        /// </summary>
        public List<CommanderState> commanders = new List<CommanderState>();

        /// <summary>
        /// The sides of the fight and who is playing them. Empty on a map saved
        /// before players existed, in which case
        /// <see cref="PlayerRegistry.EnsureDefaults"/> fills in the arrangement
        /// such a map was always implicitly being played under — Blue by the
        /// user, Red by the computer. See docs/25-PLAYERS.md.
        /// </summary>
        public List<TeamState> teams = new List<TeamState>();
        public List<PlayerState> players = new List<PlayerState>();
    }
}
