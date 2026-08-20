using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.Save
{
    /// <summary>
    /// Loads/saves map scenarios as JSON. Every map is a separate file that
    /// fully describes unit positions, status and drawn lines.
    ///
    ///   Shipped defaults : Assets/StreamingAssets/Maps/*.json
    ///   Player saves     : %USERPROFILE%/AppData/LocalLow/IvanSostarko/Iron Meridian/Maps/*.json
    /// </summary>
    public static class SaveSystem
    {
        public static string UserMapsDir
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Maps");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Where the shipped scenarios are. A **relative** path now, not an
        /// absolute one: on Android StreamingAssets is an archive rather than a
        /// directory, and the only thing that can address an entry in it is
        /// <see cref="StreamingAssetsFile"/>. See docs/40-ANDROID.md.
        /// </summary>
        const string DefaultMapsDir = "Maps";

        /// <summary>
        /// The index of shipped scenarios, written next to them by
        /// <c>ProjectBootstrap</c>.
        ///
        /// A packed platform cannot list a folder — there is no folder — so the
        /// build writes down what it packed. Generated rather than hand-kept,
        /// because an index that drifts from the files beside it is a scenario
        /// that exists in the build and cannot be opened.
        /// </summary>
        public const string ShippedIndexFile = StreamingAssetsFile.MapIndexFile;

        [Serializable]
        class MapIndex { public List<string> maps = new List<string>(); }

        /// <summary>Load a map by name. User saves shadow shipped defaults.</summary>
        public static MapSaveData LoadMap(string mapFileName)
        {
            string user = Path.Combine(UserMapsDir, mapFileName);
            if (File.Exists(user)) return ReadUser(user, mapFileName);
            return LoadShippedMap(mapFileName);
        }

        /// <summary>
        /// Loads the **shipped** scenario, ignoring any user save that shadows
        /// it. This is what the editor's RESET restores to: a reset that put you
        /// back to your own last save would not be a reset.
        /// </summary>
        public static MapSaveData LoadShippedMap(string mapFileName)
        {
            string relative = DefaultMapsDir + "/" + mapFileName;
            string json = StreamingAssetsFile.ReadAllText(relative);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[Save] Map not found: {mapFileName} " +
                               $"(looked in {StreamingAssetsFile.PathFor(relative)})");
                return null;
            }
            return Parse(json, mapFileName, relative);
        }

        static MapSaveData ReadUser(string path, string mapFileName)
        {
            try { return Parse(File.ReadAllText(path), mapFileName, path); }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not read {mapFileName}: {e.Message}");
                return null;
            }
        }

        static MapSaveData Parse(string json, string mapFileName, string source)
        {
            var data = JsonUtility.FromJson<MapSaveData>(json);
            if (data == null)
            {
                Debug.LogError($"[Save] {mapFileName} is not a scenario file ({source}).");
                return null;
            }
            Debug.Log($"[Save] Loaded '{data.mapName}' ({data.units.Count} units) from {source}");
            return data;
        }

        public static string SaveMap(MapSaveData data, string mapFileName)
        {
            data.savedAtUtc = DateTime.UtcNow.ToString("o");
            string path = Path.Combine(UserMapsDir, mapFileName);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            // Through to the browser's IndexedDB. A no-op everywhere else,
            // and the difference between a save that survives closing the
            // tab and one that does not - docs/41-WEB.md.
            WebStorage.Flush();
            Debug.Log($"[Save] Saved '{data.mapName}' -> {path}");
            return path;
        }

        /// <summary>
        /// Every scenario the player can open: the shipped ones plus their own
        /// saves, with a save that shadows a shipped map counted once.
        ///
        /// The shipped half comes from the index rather than from a directory
        /// walk, because on Android there is no directory to walk. On desktop
        /// the folder is walked as well, so a scenario dropped in by hand while
        /// the editor is open still shows up without regenerating anything.
        /// </summary>
        public static List<string> ListMaps()
        {
            var names = new HashSet<string>(ShippedMaps());
            foreach (var f in Directory.GetFiles(UserMapsDir, "*.json"))
                names.Add(Path.GetFileName(f));
            return new List<string>(names);
        }

        /// <summary>The shipped scenarios, from the index — and from the folder where there is one.</summary>
        public static List<string> ShippedMaps()
        {
            var names = new HashSet<string>();

            string json = StreamingAssetsFile.ReadAllText(ShippedIndexFile);
            if (!string.IsNullOrEmpty(json))
            {
                var index = JsonUtility.FromJson<MapIndex>(json);
                if (index?.maps != null)
                    foreach (var name in index.maps)
                        if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            if (StreamingAssetsFile.IsDirectory)
            {
                string dir = StreamingAssetsFile.PathFor(DefaultMapsDir);
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        string name = Path.GetFileName(f);
                        if (name != "index.json") names.Add(name);
                    }
            }
            else if (names.Count == 0)
            {
                Debug.LogWarning($"[Save] No shipped scenarios listed. {ShippedIndexFile} is " +
                                 "missing from the build — run Tools > Iron Meridian > Setup Project.");
            }

            return new List<string>(names);
        }
    }
}
