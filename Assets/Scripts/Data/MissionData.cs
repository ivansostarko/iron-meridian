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
        /// <summary>
        /// One European theatre, from the Fulda Gap to the Suwałki corridor and
        /// the Balkan river lines.
        ///
        /// It was two — WEST EUROPE and EAST EUROPE — and the split was doing
        /// the player no favours: two boards of two or three missions each, in
        /// a browser whose whole job is to show what there is to play. A
        /// campaign is a *shelf*, and a shelf with two things on it is a list
        /// pretending to be a structure. Both names still load; see
        /// <see cref="CampaignInfo.Parse"/>.
        /// </summary>
        Europe,
        Africa,
        Asia,
        NorthAmerica,
        SouthAmerica,
        Australia
    }

    /// <summary>Display data for a campaign — what the selection board reads off.</summary>
    public static class CampaignInfo
    {
        /// <summary>
        /// Declaration order is the order the boards are drawn in. Europe first
        /// because it is where the game's own ground lies; the rest run
        /// west to east and north to south, which is an order a reader can
        /// predict rather than one they have to learn.
        /// </summary>
        public static readonly Campaign[] All =
        {
            Campaign.Europe, Campaign.Africa, Campaign.Asia,
            Campaign.NorthAmerica, Campaign.SouthAmerica, Campaign.Australia
        };

        public static string DisplayName(Campaign c) => c switch
        {
            Campaign.Europe => "EUROPE",
            Campaign.Africa => "AFRICA",
            Campaign.Asia => "ASIA",
            Campaign.NorthAmerica => "NORTH AMERICA",
            Campaign.SouthAmerica => "SOUTH AMERICA",
            Campaign.Australia => "AUSTRALIA",
            _ => c.ToString().ToUpperInvariant()
        };

        public static string Blurb(Campaign c) => c switch
        {
            Campaign.Europe =>
                "From the Fulda Gap to the Suwałki corridor and the Balkan river lines — the ground " +
                "every European plan of the last fifty years was written around.",
            Campaign.Africa =>
                "Desert, Sahel and the Horn — distances that beat an army before anybody does, and a " +
                "handful of ports and oases worth the fight.",
            Campaign.Asia =>
                "The DMZ, the Himalayan passes and the island chains — three theatres that share a " +
                "continent and nothing else.",
            Campaign.NorthAmerica =>
                "The continental interior, the Arctic approaches and both seaboards — depth, and very " +
                "little of it defended.",
            Campaign.SouthAmerica =>
                "Andes, Amazon and Patagonia — jungle with no roads, mountains with one road each, and " +
                "a coast at either end.",
            Campaign.Australia =>
                "The northern approaches and the outback behind them — a coastline nobody can watch and " +
                "a continent nobody can cross quickly.",
            _ => ""
        };

        /// <summary>
        /// Parses a saved campaign name, falling back rather than throwing on an
        /// old file.
        ///
        /// **The two retired names are mapped, not dropped.** `WestEurope` and
        /// `EastEurope` are written into every mission record and map file
        /// saved before the merge; letting them fall through to the default
        /// would work by luck rather than on purpose, and would silently move a
        /// North American mission if the default ever changed. Handled here so
        /// every reader of a campaign name gets the same answer.
        /// </summary>
        public static Campaign Parse(string name)
        {
            if (name == "WestEurope" || name == "EastEurope") return Campaign.Europe;
            return Enum.TryParse(name, out Campaign c) ? c : Campaign.Europe;
        }
    }

    /// <summary>
    /// One side's marked place on a mission's ground: where it is, and whether
    /// it has been placed at all. Used for both headquarters and deployment
    /// zones — they are the same record, and a second identical class would be
    /// two things to keep in step for no gain.
    ///
    /// **A point plus a radius, not a polygon.** A mission area is a shape
    /// because coastlines and valleys are shapes; a headquarters is a place,
    /// and the only thing about it that varies is how much ground around it
    /// counts as "the HQ" — a divisional main at five kilometres, a battalion
    /// step-up at one. The radius is the mission's
    /// (<see cref="MissionDefinition.hqRadiusKm"/>) rather than each zone's,
    /// because both sides' headquarters in one scenario are at the same
    /// echelon and giving each its own number would be a control nobody has a
    /// reason to touch.
    /// </summary>
    [Serializable]
    public class MissionZone
    {
        /// <summary>False until the designer has put it somewhere.</summary>
        public bool placed;
        public double latitude;
        public double longitude;

        public MissionZone Clone() => new MissionZone
        {
            placed = placed, latitude = latitude, longitude = longitude
        };
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
        public string campaign = Campaign.Europe.ToString();

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
        /// The two headquarters, drawn in the editor's MISSIONS panel. Unplaced
        /// on every mission written before this existed, which reads correctly
        /// as "this scenario names no headquarters" — see docs/22-MISSIONS.md.
        /// </summary>
        public MissionZone friendlyHq = new MissionZone();
        public MissionZone enemyHq = new MissionZone();

        /// <summary>Radius of both HQ zones, km. See <see cref="MissionZone"/>.</summary>
        public float hqRadiusKm = 3f;

        /// <summary>
        /// Where each side's **reinforcements arrive**. A scenario that can be
        /// reinforced has to say where from: a battalion that materialised in
        /// the middle of the fighting would be a spawn, not a reinforcement.
        /// Unplaced on an older mission, in which case arrivals fall back to
        /// the side's own rear — see <c>GameController.DeploymentZoneFor</c>.
        /// </summary>
        public MissionZone friendlyDeployment = new MissionZone();
        public MissionZone enemyDeployment = new MissionZone();

        /// <summary>
        /// Radius of both deployment zones, km. Larger than an HQ's by default:
        /// this is ground a formation arrives *into* and spreads across, not a
        /// command post.
        /// </summary>
        public float deploymentRadiusKm = 5f;

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
            // Deep-copied for the same reason the area is: the live editor
            // overlay holds these, and a shallow copy would track later edits.
            friendlyHq = friendlyHq?.Clone() ?? new MissionZone(),
            enemyHq = enemyHq?.Clone() ?? new MissionZone(),
            hqRadiusKm = hqRadiusKm,
            friendlyDeployment = friendlyDeployment?.Clone() ?? new MissionZone(),
            enemyDeployment = enemyDeployment?.Clone() ?? new MissionZone(),
            deploymentRadiusKm = deploymentRadiusKm,
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

        /// <summary>
        /// Shipped missions the player has deliberately deleted.
        ///
        /// The player's book is merged over the shipped one rather than
        /// replacing it — see <see cref="Save.MissionLibrary"/> — which on its
        /// own could not tell "never had it" from "got rid of it", and would
        /// resurrect a mission somebody removed on purpose. This is that
        /// distinction, written down: an id in here is a mission the merge
        /// leaves out.
        ///
        /// Empty on every book written before the merge existed, which reads
        /// correctly as "nothing has been deleted".
        /// </summary>
        public List<string> retiredIds = new List<string>();
    }
}
