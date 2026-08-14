using System;
using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>
    /// The theatres a single-player mission can belong to.
    ///
    /// A closed enum rather than a free string on the mission, because the
    /// campaign is a *navigation* concept: the single-player screen shows one
    /// board per campaign, and a mission whose campaign nobody recognises would
    /// be a mission with nowhere to appear. Missions are data and can be added
    /// freely; campaigns are structure and are added deliberately, in code, with
    /// a display name and a blurb to go with them.
    /// </summary>
    public enum Campaign
    {
        WestEurope,
        EastEurope,
        NorthAmerica
    }

    /// <summary>Display data for a campaign — what the selection board reads off.</summary>
    public static class CampaignInfo
    {
        /// <summary>Declaration order is the order the boards are drawn in.</summary>
        public static readonly Campaign[] All =
        {
            Campaign.WestEurope, Campaign.EastEurope, Campaign.NorthAmerica
        };

        public static string DisplayName(Campaign c) => c switch
        {
            Campaign.WestEurope => "WEST EUROPE",
            Campaign.EastEurope => "EAST EUROPE",
            Campaign.NorthAmerica => "NORTH AMERICA",
            _ => c.ToString().ToUpperInvariant()
        };

        public static string Blurb(Campaign c) => c switch
        {
            Campaign.WestEurope =>
                "The North German Plain and the northern flank — the ground NATO planned to hold.",
            Campaign.EastEurope =>
                "The Pannonian basin and the Sava corridor — river lines, cities and short distances.",
            Campaign.NorthAmerica =>
                "The continental interior and the eastern seaboard — depth, and very little of it defended.",
            _ => ""
        };

        /// <summary>Parses a saved campaign name, falling back rather than throwing on an old file.</summary>
        public static Campaign Parse(string name) =>
            Enum.TryParse(name, out Campaign c) ? c : Campaign.WestEurope;
    }

    /// <summary>
    /// One single-player mission.
    ///
    /// **A mission is the metadata; the map file is the content.** Everything a
    /// player deploys — units, control measures, task markers — lives in the
    /// <see cref="MapSaveData"/> named by <see cref="mapFile"/>, which is the
    /// same format and the same loader the map editor already uses. This record
    /// carries what the *selection screens* need before any of that is loaded
    /// (name, campaign, where in the world, a briefing) plus the handful of
    /// scenario settings that are genuinely the mission's own rather than the
    /// map's.
    ///
    /// That split is what makes "edit it in the editor and the game gets it"
    /// true without any syncing step: the editor writes the same two files the
    /// game reads. See docs/22-MISSIONS.md.
    ///
    /// Fields added later default harmlessly on old files — `JsonUtility` leaves
    /// missing fields at their initialiser values.
    /// </summary>
    [Serializable]
    public class MissionDefinition
    {
        /// <summary>
        /// Stable identifier, and the stem of the mission's map file. Lower-case
        /// and file-safe, because it is used as a filename — see
        /// <see cref="Save.MissionLibrary.MakeId"/>.
        /// </summary>
        public string id = "";

        /// <summary>Campaign name — a <see cref="Campaign"/> value.</summary>
        public string campaign = Campaign.WestEurope.ToString();

        /// <summary>Mission title, as the board shows it.</summary>
        public string name = "New mission";

        /// <summary>Where in the world, in words. Shown under the title.</summary>
        public string location = "";

        /// <summary>One or two lines of what the mission is about.</summary>
        public string briefing = "";

        /// <summary>
        /// Scenario file under Maps/, e.g. "berlin.json". Empty means the
        /// mission has no map yet and one is synthesised from the fields below
        /// the first time it is opened — a new mission is playable immediately
        /// rather than being broken until somebody remembers to save it.
        /// </summary>
        public string mapFile = "";

        // ------------------------------------------------------ opening view

        /// <summary>Where the map opens, WGS84.</summary>
        public double latitude;
        public double longitude;

        /// <summary>Camera height above the ground when the mission opens, metres.</summary>
        public double startAltitudeMeters = 12000;

        /// <summary>Mode3D | Mode2D.</summary>
        public string viewMode = "Mode3D";

        /// <summary>MapStyle name — see docs/02-CESIUM.md.</summary>
        public string mapStyle = "Satellite";

        /// <summary>3D buildings layer on entry.</summary>
        public bool showBuildings = true;

        // -------------------------------------------------- scenario settings

        /// <summary>H-hour, "yyyy-MM-dd HH:mm" — see docs/13-DATE-AND-TIME.md.</summary>
        public string startDateTime = "1990-01-01 14:00";

        /// <summary>SkyPhase name: Day | Sunset | Night. Ignored when autoDayNight is set.</summary>
        public string skyPhase = "Day";
        /// <summary>WeatherCondition name — see docs/14-WEATHER.md.</summary>
        public string weatherCondition = "Clear";
        /// <summary>When true the scenario clock drives the sky.</summary>
        public bool autoDayNight;

        /// <summary>
        /// Fog of war armed when the mission opens. A mission is a fight rather
        /// than a layout exercise, so this is the one editor toggle a mission
        /// gets to decide for the player.
        /// </summary>
        public bool fogOfWar = true;

        /// <summary>
        /// The ground this mission is fought over, drawn in the editor's
        /// MISSIONS panel. Empty on every mission written before this existed,
        /// which means unbounded and is the correct reading of an old file —
        /// see <see cref="MissionArea"/>.
        /// </summary>
        public MissionArea area = new MissionArea();

        /// <summary>
        /// Position within its campaign board, ascending. Ties fall back to the
        /// order the file lists them in, so a hand-edited file with no orders at
        /// all still reads sensibly.
        /// </summary>
        public int order;

        /// <summary>
        /// False for a mission the player should not see yet. Shipped missions
        /// are all available; the flag exists so a work-in-progress mission can
        /// live in the file without appearing on the board.
        /// </summary>
        public bool available = true;

        public Campaign CampaignEnum => CampaignInfo.Parse(campaign);

        /// <summary>The map file this mission uses, derived from the id when unset.</summary>
        public string ResolvedMapFile =>
            string.IsNullOrEmpty(mapFile) ? $"{id}.json" : mapFile;

        public MissionDefinition Clone() => new MissionDefinition
        {
            id = id,
            campaign = campaign,
            name = name,
            location = location,
            briefing = briefing,
            mapFile = mapFile,
            latitude = latitude,
            longitude = longitude,
            startAltitudeMeters = startAltitudeMeters,
            viewMode = viewMode,
            mapStyle = mapStyle,
            showBuildings = showBuildings,
            startDateTime = startDateTime,
            skyPhase = skyPhase,
            weatherCondition = weatherCondition,
            autoDayNight = autoDayNight,
            fogOfWar = fogOfWar,
            // Deep-copied: the area is a reference type shared with the live
            // editor overlay, and a shallow copy would track later edits rather
            // than recording the mission as it stood.
            area = area?.Clone() ?? new MissionArea(),
            order = order,
            available = available
        };
    }

    /// <summary>
    /// The whole mission list, as one JSON file.
    ///
    /// One file rather than one per mission because it is read in full by the
    /// campaign screen before anything is chosen, and seven file reads to draw
    /// one board would be seven chances to half-load a menu. The *maps* are
    /// still one file each — those are big, and only ever one is wanted.
    /// </summary>
    [Serializable]
    public class MissionBook
    {
        public string savedAtUtc = "";
        public List<MissionDefinition> missions = new List<MissionDefinition>();
    }
}
