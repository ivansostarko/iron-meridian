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
