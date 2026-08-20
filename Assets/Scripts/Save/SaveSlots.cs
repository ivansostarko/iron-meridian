using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.Save
{
    /// <summary>Where a save lives — see <see cref="SaveSlots"/>.</summary>
    public enum SaveDestination
    {
        /// <summary>This machine, under <c>persistentDataPath/Saves</c>.</summary>
        Local,
        /// <summary>
        /// The player's account. **A preview** — see
        /// <see cref="SaveSlots.CloudIsMock"/>.
        /// </summary>
        Cloud
    }

    /// <summary>
    /// One saved game: the whole scenario, plus enough about it to be listed
    /// without being loaded.
    ///
    /// **The header exists so the browser is cheap.** A save file holds an
    /// entire order of battle; a list of twelve of them would be twelve full
    /// parses to draw twelve rows. Everything the list shows — the name, when,
    /// which mission, how many formations, whether the battle had started — is
    /// duplicated at the top of the file and read from there. The duplication is
    /// deliberate and one-way: the header is written from the map, never the
    /// other way round, so it cannot drift into being the truth.
    /// </summary>
    [Serializable]
    public class GameSave
    {
        /// <summary>What the player called it. Also the file stem, sanitised.</summary>
        public string slot = "";

        /// <summary>ISO-8601 UTC. Sorted on, and shown in the player's own time.</summary>
        public string savedAtUtc = "";

        /// <summary>Mission this was saved from, or empty for a free-play editor map.</summary>
        public string missionId = "";
        /// <summary>The mission's title at the time, so a deleted mission still lists sensibly.</summary>
        public string missionName = "";
        /// <summary>Scenario file the save was taken from — what a load falls back to.</summary>
        public string mapFile = "";

        public int unitCount;
        /// <summary>True when the save was taken with a battle running.</summary>
        public bool battleRunning;

        /// <summary>The scenario itself — the same record the map editor writes.</summary>
        public MapSaveData map = new MapSaveData();

        public DateTime SavedAt
        {
            get
            {
                return DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var when)
                    ? when.ToLocalTime()
                    : DateTime.MinValue;
            }
        }

        /// <summary>The second line of a list row: what this save is of.</summary>
        public string Describe()
        {
            string what = string.IsNullOrEmpty(missionName)
                ? (string.IsNullOrEmpty(map?.mapName) ? "Scenario" : map.mapName)
                : missionName;
            string when = SavedAt == DateTime.MinValue ? "—" : SavedAt.ToString("yyyy-MM-dd HH:mm");
            return $"{what}  ·  {unitCount} unit(s)  ·  {(battleRunning ? "in battle" : "in editor")}  ·  {when}";
        }
    }

    /// <summary>
    /// Named saved games, in two places: this machine and — eventually — the
    /// player's account.
    ///
    /// **Why slots at all.** The game had exactly one save per scenario: F5 wrote
    /// over the map file and F9 read it back. That is a scratchpad, not a save
    /// system. You cannot keep the state before an attack and the state after
    /// it, you cannot keep two attempts at the same mission, and there is no way
    /// to see what you have without opening it. Everything else in this file
    /// follows from wanting those three things.
    ///
    /// **F5 and F9 are not this.** They write and read the scenario's own map
    /// file, which is the pair a designer uses while laying one out, and they
    /// keep doing exactly that — see docs/45-SAVE-AND-LOAD.md §1. Slots are the
    /// player's tool and are reached from the pause menu. Two mechanisms
    /// because they are two jobs, not because one of them was left half-built.
    ///
    /// <a id="cloud"></a>
    /// **The cloud destination is a mock, and says so.** There is no backend and
    /// no account to sign into. What it does is keep its files in a *separate
    /// folder on this machine* and report a simulated account, so the whole flow
    /// — pick a destination, see what is in it, save, load, delete, and the
    /// "these are different places" mental model — is real and testable while
    /// the transport is not. <see cref="CloudIsMock"/> is true, every surface
    /// that shows the cloud says PREVIEW, and nothing here ever claims a file
    /// left the machine.
    ///
    /// Replacing the mock is one class: give <see cref="ICloudBackend"/> a real
    /// implementation and hand it to <see cref="SetCloudBackend"/>. Nothing in
    /// the UI or the controller knows which one is installed.
    ///
    /// See docs/45-SAVE-AND-LOAD.md.
    /// </summary>
    public static class SaveSlots
    {
        /// <summary>Longest a slot name may be. A file name, and a list row.</summary>
        public const int MaxNameLength = 40;

        /// <summary>True while the cloud destination is the built-in stand-in rather than a service.</summary>
        public static bool CloudIsMock => _cloud is MockCloudBackend;

        // --------------------------------------------------------- local disk

        public static string LocalDir => EnsureDir(Path.Combine(Application.persistentDataPath, "Saves"));

        static string EnsureDir(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ------------------------------------------------------- cloud (mock)

        /// <summary>
        /// What a cloud save service has to be able to do. Four verbs, because
        /// four verbs is what a save browser asks for — anything more would be
        /// this file guessing at an API nobody has written yet.
        /// </summary>
        public interface ICloudBackend
        {
            string AccountLabel { get; }
            bool Available { get; }
            List<GameSave> List();
            bool Write(GameSave save);
            GameSave Read(string slot);
            bool Delete(string slot);
        }

        static ICloudBackend _cloud = new MockCloudBackend();

        /// <summary>Installs a real backend. Called by nothing yet — see the class remarks.</summary>
        public static void SetCloudBackend(ICloudBackend backend) =>
            _cloud = backend ?? new MockCloudBackend();

        public static string CloudAccountLabel => _cloud.AccountLabel;
        public static bool CloudAvailable => _cloud.Available;

        /// <summary>
        /// The stand-in. Files go to a folder beside the local saves rather than
        /// anywhere off this machine, which is the honest thing a mock can do:
        /// the two destinations really are separate stores with separate
        /// contents, so every question the interface asks about them has a true
        /// answer.
        /// </summary>
        class MockCloudBackend : ICloudBackend
        {
            public string AccountLabel => "commander@example.com";
            public bool Available => true;

            static string Dir => EnsureDir(Path.Combine(Application.persistentDataPath, "CloudSaves"));

            public List<GameSave> List() => ReadFolder(Dir);
            public bool Write(GameSave save) => WriteTo(Dir, save);
            public GameSave Read(string slot) => ReadOne(Path.Combine(Dir, FileName(slot)));
            public bool Delete(string slot) => DeleteAt(Path.Combine(Dir, FileName(slot)));
        }

        // ---------------------------------------------------------------- API

        /// <summary>Everything in one destination, newest first.</summary>
        public static List<GameSave> List(SaveDestination destination)
        {
            var list = destination == SaveDestination.Cloud ? _cloud.List() : ReadFolder(LocalDir);
            list.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
            return list;
        }

        /// <summary>Writes a save. Returns false and logs on failure rather than throwing at a UI.</summary>
        public static bool Write(SaveDestination destination, GameSave save)
        {
            if (save == null) return false;
            save.slot = Sanitise(save.slot);
            if (string.IsNullOrEmpty(save.slot)) return false;
            save.savedAtUtc = DateTime.UtcNow.ToString("o");

            return destination == SaveDestination.Cloud ? _cloud.Write(save) : WriteTo(LocalDir, save);
        }

        public static GameSave Read(SaveDestination destination, string slot)
        {
            slot = Sanitise(slot);
            if (string.IsNullOrEmpty(slot)) return null;
            return destination == SaveDestination.Cloud
                ? _cloud.Read(slot)
                : ReadOne(Path.Combine(LocalDir, FileName(slot)));
        }

        public static bool Delete(SaveDestination destination, string slot)
        {
            slot = Sanitise(slot);
            if (string.IsNullOrEmpty(slot)) return false;
            return destination == SaveDestination.Cloud
                ? _cloud.Delete(slot)
                : DeleteAt(Path.Combine(LocalDir, FileName(slot)));
        }

        public static bool Exists(SaveDestination destination, string slot)
        {
            foreach (var s in List(destination))
                if (string.Equals(s.slot, Sanitise(slot), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------- naming

        /// <summary>
        /// A slot name that is safe as a file name on three operating systems,
        /// and short enough to be a list row.
        ///
        /// Sanitising rather than rejecting: a player who types "Bridge — 3rd
        /// try" should get a save called that, not an error about colons.
        /// </summary>
        public static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
                else sb.Append('-');
                if (sb.Length >= MaxNameLength) break;
            }
            return sb.ToString().Trim();
        }

        /// <summary>A name nothing in the destination is using yet: "Save", "Save 2", "Save 3"…</summary>
        public static string NextFreeName(SaveDestination destination, string stem = "Save")
        {
            if (!Exists(destination, stem)) return stem;
            for (int i = 2; i < 999; i++)
            {
                string candidate = $"{stem} {i}";
                if (!Exists(destination, candidate)) return candidate;
            }
            return stem + " " + DateTime.UtcNow.Ticks;
        }

        static string FileName(string slot) => slot.Replace(' ', '_') + ".json";

        // -------------------------------------------------------------- files

        static List<GameSave> ReadFolder(string dir)
        {
            var result = new List<GameSave>();
            string[] files;
            try { files = Directory.GetFiles(dir, "*.json"); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Saves] Could not list {dir}: {e.Message}");
                return result;
            }

            foreach (var f in files)
            {
                var save = ReadOne(f);
                if (save != null) result.Add(save);
            }
            return result;
        }

        static GameSave ReadOne(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var save = JsonUtility.FromJson<GameSave>(File.ReadAllText(path));
                if (save == null) return null;
                // A file hand-copied under a different name is listed under the
                // name it actually has, not the one written inside it.
                if (string.IsNullOrEmpty(save.slot))
                    save.slot = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
                return save;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Saves] Could not read {path}: {e.Message}");
                return null;
            }
        }

        static bool WriteTo(string dir, GameSave save)
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, FileName(save.slot)),
                    JsonUtility.ToJson(save, true));
                // Through to the browser's IndexedDB. A no-op everywhere else,
                // and the difference between a save that survives closing the
                // tab and one that does not — docs/41-WEB.md.
                WebStorage.Flush();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Saves] Could not write '{save.slot}': {e.Message}");
                return false;
            }
        }

        static bool DeleteAt(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                WebStorage.Flush();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Saves] Could not delete {path}: {e.Message}");
                return false;
            }
        }
    }
}
