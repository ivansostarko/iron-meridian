using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
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

        public static string DefaultMapsDir =>
            Path.Combine(Application.streamingAssetsPath, "Maps");

        /// <summary>Load a map by name. User saves shadow shipped defaults.</summary>
        public static MapSaveData LoadMap(string mapFileName)
        {
            string user = Path.Combine(UserMapsDir, mapFileName);
            string shipped = Path.Combine(DefaultMapsDir, mapFileName);
            string path = File.Exists(user) ? user : shipped;
            if (!File.Exists(path))
            {
                Debug.LogError($"[Save] Map not found: {mapFileName}");
                return null;
            }
            var data = JsonUtility.FromJson<MapSaveData>(File.ReadAllText(path));
            Debug.Log($"[Save] Loaded '{data.mapName}' ({data.units.Count} units) from {path}");
            return data;
        }

        public static string SaveMap(MapSaveData data, string mapFileName)
        {
            data.savedAtUtc = DateTime.UtcNow.ToString("o");
            string path = Path.Combine(UserMapsDir, mapFileName);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            Debug.Log($"[Save] Saved '{data.mapName}' -> {path}");
            return path;
        }

        public static List<string> ListMaps()
        {
            var names = new HashSet<string>();
            if (Directory.Exists(DefaultMapsDir))
                foreach (var f in Directory.GetFiles(DefaultMapsDir, "*.json"))
                    names.Add(Path.GetFileName(f));
            foreach (var f in Directory.GetFiles(UserMapsDir, "*.json"))
                names.Add(Path.GetFileName(f));
            return new List<string>(names);
        }
    }
}
