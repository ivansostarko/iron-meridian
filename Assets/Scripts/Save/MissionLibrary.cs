using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Save
{
    /// <summary>
    /// The single-player mission list: read by the campaign screens, written by
    /// the map editor's MISSIONS panel.
    ///
    ///   Shipped : Assets/StreamingAssets/Data/missions.json
    ///   Player  : %USERPROFILE%/AppData/LocalLow/…/Iron Meridian/missions.json
    ///
    /// **The user file shadows the shipped one wholesale**, exactly as
    /// <see cref="SaveSystem"/> does for maps. Merging the two per-mission was
    /// the obvious alternative and is worse: a player who deletes a shipped
    /// mission in the editor would watch it reappear, because a merge cannot
    /// tell "never had it" from "got rid of it". Shadowing means the editor
    /// owns the list once it has been touched, and deleting the user file is
    /// the way back to the shipped one.
    ///
    /// **Why the game and the editor share this.** A mission is its record here
    /// plus its map file, and the editor writes both — so "change it in the
    /// editor and the game gets it" is not a sync step that could fail, it is
    /// the two screens reading the same two files. See docs/22-MISSIONS.md.
    /// </summary>
    public static class MissionLibrary
    {
        public const string FileName = "missions.json";

        /// <summary>
        /// The mission the player picked, handed from the campaign screen to the
        /// Game scene.
        ///
        /// A static rather than a scene parameter because Unity has no way to
        /// pass one: <c>SceneManager.LoadScene</c> takes a name and nothing
        /// else. Cleared by <see cref="Clear"/> when the editor is entered
        /// directly, so a stale pick from an earlier session cannot hijack the
        /// dev map.
        /// </summary>
        public static MissionDefinition Selected { get; private set; }

        /// <summary>True while the Game scene is playing a mission rather than being the map editor.</summary>
        public static bool InMission => Selected != null;

        public static void Select(MissionDefinition mission) => Selected = mission;
        public static void Clear() => Selected = null;

        static string UserPath => Path.Combine(Application.persistentDataPath, FileName);
        static string ShippedPath =>
            Path.Combine(Application.streamingAssetsPath, "Data", FileName);

        /// <summary>True once the player's own list exists — i.e. the editor has saved.</summary>
        public static bool HasUserBook => File.Exists(UserPath);

        // ------------------------------------------------------------- read

        static MissionBook _cache;

        /// <summary>
        /// The mission list. Cached, because the campaign board and the editor
        /// panel both read it repeatedly while nothing has changed; every write
        /// path invalidates it.
        /// </summary>
        public static MissionBook Book
        {
            get
            {
                if (_cache == null) _cache = Read();
                return _cache;
            }
        }

        public static void Reload() => _cache = null;

        static MissionBook Read()
        {
            string path = File.Exists(UserPath) ? UserPath : ShippedPath;

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Missions] No mission list found (looked in {path}). " +
                    "Starting empty — the editor's MISSIONS panel can create one.");
                return new MissionBook();
            }

            MissionBook book = null;
            try { book = JsonUtility.FromJson<MissionBook>(File.ReadAllText(path)); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Missions] Could not read {path}: {e.Message}");
            }

            if (book == null) book = new MissionBook();
            if (book.missions == null) book.missions = new List<MissionDefinition>();

            // A record with no id cannot be saved, deleted or given a map file,
            // so give it one now rather than letting it fail later.
            foreach (var m in book.missions)
            {
                if (string.IsNullOrEmpty(m.id)) m.id = MakeId(m.name);
                // Missions written before areas existed have no `area` object at
                // all, and one written by hand can have a null point list. Both
                // mean "unbounded", and every reader is entitled to a non-null
                // area to ask that question of.
                if (m.area == null) m.area = new MissionArea();
                if (m.area.points == null) m.area.points = new List<GeoPoint>();
            }

            Debug.Log($"[Missions] Loaded {book.missions.Count} mission(s) from {path}");
            return book;
        }

        /// <summary>Missions in one campaign, in board order. Unavailable ones are omitted.</summary>
        public static List<MissionDefinition> OfCampaign(Campaign campaign, bool includeHidden = false)
        {
            var list = new List<MissionDefinition>();
            foreach (var m in Book.missions)
            {
                if (m.CampaignEnum != campaign) continue;
                if (!includeHidden && !m.available) continue;
                list.Add(m);
            }
            // Stable: equal orders keep the file's own sequence, so a list with
            // no orders set at all still reads the way it was written.
            list.Sort((a, b) => a.order.CompareTo(b.order));
            return list;
        }

        public static MissionDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in Book.missions) if (m.id == id) return m;
            return null;
        }

        // ------------------------------------------------------------ write

        /// <summary>
        /// Writes the whole list to the player's file. Every edit goes through
        /// here — there is no partial write, because the file is one object and
        /// JsonUtility has no concept of patching one member of it.
        /// </summary>
        public static string SaveBook()
        {
            var book = Book;
            book.savedAtUtc = System.DateTime.UtcNow.ToString("o");

            string path = UserPath;
            File.WriteAllText(path, JsonUtility.ToJson(book, true));
            Debug.Log($"[Missions] Saved {book.missions.Count} mission(s) -> {path}");
            return path;
        }

        /// <summary>
        /// Adds a mission and returns it. The id is derived from the name and
        /// made unique, so two missions called "Berlin" cannot end up writing
        /// each other's map file.
        /// </summary>
        public static MissionDefinition Create(Campaign campaign, string name,
            double latitude, double longitude)
        {
            var mission = new MissionDefinition
            {
                id = UniqueId(MakeId(name)),
                campaign = campaign.ToString(),
                name = name,
                latitude = latitude,
                longitude = longitude,
                order = NextOrder(campaign)
            };
            mission.mapFile = $"{mission.id}.json";

            Book.missions.Add(mission);
            SaveBook();
            return mission;
        }

        /// <summary>
        /// Removes a mission from the list. **Its map file is left alone** — a
        /// scenario can take an evening to lay out and the delete button is one
        /// mis-click, so removing the record is reversible by hand and removing
        /// the work would not be.
        /// </summary>
        public static bool Delete(MissionDefinition mission)
        {
            if (mission == null) return false;
            bool removed = Book.missions.Remove(mission);
            if (removed)
            {
                if (Selected == mission) Clear();
                SaveBook();
            }
            return removed;
        }

        static int NextOrder(Campaign campaign)
        {
            int max = -1;
            foreach (var m in Book.missions)
                if (m.CampaignEnum == campaign && m.order > max) max = m.order;
            return max + 1;
        }

        // --------------------------------------------------------- the map

        /// <summary>
        /// The scenario behind a mission.
        ///
        /// Falls back to a **map synthesised from the mission's own fields**
        /// when the file does not exist, rather than failing. A mission created
        /// in the editor has a record before it has a map, and a new mission
        /// that cannot be opened until somebody remembers to press save would be
        /// a trap — this way it opens on empty ground at the right place, and
        /// the first save writes the file.
        /// </summary>
        public static MapSaveData LoadMap(MissionDefinition mission)
        {
            if (mission == null) return null;

            var data = SaveSystem.LoadMap(mission.ResolvedMapFile);
            if (data == null)
            {
                Debug.Log($"[Missions] '{mission.name}' has no map file yet — " +
                    "opening empty ground at its start point.");
                data = new MapSaveData();
            }

            // The mission's own settings win over whatever the map file carries.
            // The map is the content; these are the mission, and the editor's
            // MISSIONS panel is where they are edited.
            ApplyTo(mission, data);
            return data;
        }

        /// <summary>Stamps a mission's settings onto a map save.</summary>
        public static void ApplyTo(MissionDefinition mission, MapSaveData data)
        {
            if (mission == null || data == null) return;

            data.mapName = mission.name;
            data.centerLatitude = mission.latitude;
            data.centerLongitude = mission.longitude;
            data.cameraHeightMeters = mission.startAltitudeMeters;
            data.viewMode = mission.viewMode;
            data.mapStyle = mission.mapStyle;
            data.showBuildings = mission.showBuildings;
            data.startDateTime = mission.startDateTime;
            data.skyPhase = mission.skyPhase;
            data.weatherCondition = mission.weatherCondition;
            data.autoDayNight = mission.autoDayNight;
        }

        /// <summary>
        /// Copies the settings a map save owns back onto its mission, so saving
        /// from the editor records the view and weather the designer actually
        /// left it in rather than whatever the record said when it was opened.
        /// Position and name are **not** taken from here — those are edited in
        /// the MISSIONS panel and would otherwise be silently overwritten by
        /// wherever the camera happened to be.
        /// </summary>
        public static void ReadBackFrom(MissionDefinition mission, MapSaveData data)
        {
            if (mission == null || data == null) return;

            mission.viewMode = data.viewMode;
            mission.mapStyle = data.mapStyle;
            mission.showBuildings = data.showBuildings;
            mission.startDateTime = data.startDateTime;
            mission.skyPhase = data.skyPhase;
            mission.weatherCondition = data.weatherCondition;
            mission.autoDayNight = data.autoDayNight;
        }

        // ------------------------------------------------------------- ids

        /// <summary>
        /// A file-safe stem from a display name: "New York" → "new_york".
        /// Anything that is not a letter or a digit becomes an underscore,
        /// because this ends up as a filename on three operating systems.
        /// </summary>
        public static string MakeId(string name)
        {
            if (string.IsNullOrEmpty(name)) return "mission";

            var sb = new StringBuilder(name.Length);
            bool lastUnderscore = false;
            foreach (char c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(c); lastUnderscore = false; }
                else if (!lastUnderscore && sb.Length > 0) { sb.Append('_'); lastUnderscore = true; }
            }

            string id = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(id) ? "mission" : id;
        }

        static string UniqueId(string stem)
        {
            if (Get(stem) == null) return stem;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = $"{stem}_{n}";
                if (Get(candidate) == null) return candidate;
            }
            return $"{stem}_{System.Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }
    }
}
