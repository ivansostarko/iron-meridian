using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Save
{
    [Serializable]
    public class TuningField
    {
        public string name = "";
        /// <summary>The value as an invariant string — see <see cref="TunableField.Read"/>.</summary>
        public string value = "";
    }

    /// <summary>Every override applied to one record of one catalogue.</summary>
    [Serializable]
    public class TuningEntry
    {
        /// <summary>Catalogue key — see <see cref="Data.GameCatalogs"/>.</summary>
        public string catalog = "";
        /// <summary>Record id within that catalogue (a unit id, or an enum name).</summary>
        public string id = "";
        public List<TuningField> fields = new List<TuningField>();
    }

    [Serializable]
    public class TuningBook
    {
        public string savedAtUtc = "";
        public List<TuningEntry> entries = new List<TuningEntry>();
    }

    /// <summary>
    /// The player's own tuning of the game's data tables, written by the
    /// DEVELOPMENT → UNITS LIST screen.
    ///
    ///   Player : %USERPROFILE%/AppData/LocalLow/…/Iron Meridian/tuning.json
    ///
    /// **Why an override file rather than editing the source of truth.** Unit
    /// stats are generated — <c>scripts/generate_units.py</c> writes
    /// <c>units.json</c> — and the weapon catalogues are C# arrays. A screen
    /// that wrote back into either would either be overwritten by the next
    /// generator run or would need to rewrite source code. A sparse patch layer
    /// leaves both authoring routes intact: regenerate the catalogue whenever
    /// you like and your tuning still applies on top, field by field.
    ///
    /// **Baselines make REVERT real.** The shipped value of every field touched
    /// is captured the first time a record is patched, before anything is
    /// written. Without that, reverting a C# catalogue would mean restarting the
    /// game, because the array the override was applied to *is* the default.
    ///
    /// Sparse on purpose: only fields that actually differ are stored, so a file
    /// stays readable and a catalogue that gains a field does not need migrating.
    /// </summary>
    public static class TuningStore
    {
        public const string FileName = "tuning.json";

        static string UserPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>True once the player has saved any tuning at all.</summary>
        public static bool HasOverrides => File.Exists(UserPath);

        /// <summary>Raised after a save or a reset, so open screens can repaint.</summary>
        public static event Action Changed;

        // --------------------------------------------------------------- read

        static TuningBook _book;

        public static TuningBook Book
        {
            get
            {
                if (_book == null) _book = Read();
                return _book;
            }
        }

        static TuningBook Read()
        {
            if (!File.Exists(UserPath)) return new TuningBook();

            TuningBook book = null;
            try { book = JsonUtility.FromJson<TuningBook>(File.ReadAllText(UserPath)); }
            catch (Exception e)
            {
                Debug.LogError($"[Tuning] Could not read {UserPath}: {e.Message}. Starting from the shipped values.");
            }

            if (book == null) book = new TuningBook();
            if (book.entries == null) book.entries = new List<TuningEntry>();
            Debug.Log($"[Tuning] Loaded overrides for {book.entries.Count} record(s) from {UserPath}");
            return book;
        }

        static TuningEntry Find(string catalog, string id)
        {
            foreach (var e in Book.entries)
                if (e.catalog == catalog && e.id == id) return e;
            return null;
        }

        static TuningEntry FindOrAdd(string catalog, string id)
        {
            var entry = Find(catalog, id);
            if (entry != null) return entry;
            entry = new TuningEntry { catalog = catalog, id = id };
            Book.entries.Add(entry);
            return entry;
        }

        // ----------------------------------------------------------- apply

        /// <summary>
        /// Captures <paramref name="record"/>'s shipped values and then applies
        /// whatever the player has saved for it. Idempotent: calling it twice on
        /// the same record re-applies the same patch and never re-baselines, so
        /// a catalogue can call it from a lazy initialiser without guarding.
        /// </summary>
        public static void Apply(string catalog, string id, object record)
        {
            if (record == null || string.IsNullOrEmpty(id)) return;

            CaptureBaseline(catalog, id, record);

            var entry = Find(catalog, id);
            if (entry == null) return;

            foreach (var f in entry.fields)
            {
                var field = TunableField.Find(record, f.name);
                if (field == null)
                {
                    // The catalogue changed under a saved override. Say so once
                    // and carry on — dropping the whole record's tuning because
                    // one field was renamed would be worse than ignoring it.
                    Debug.LogWarning($"[Tuning] {catalog}/{id} has an override for '{f.name}', " +
                                     "which no longer exists. Ignoring it.");
                    continue;
                }
                if (!field.Write(record, f.value))
                    Debug.LogWarning($"[Tuning] {catalog}/{id}.{f.name}: could not read '{f.value}'. " +
                                     "Keeping the shipped value.");
            }
        }

        // -------------------------------------------------------- baselines

        // Key is "catalog/id"; the value is field name -> shipped text.
        static readonly Dictionary<string, Dictionary<string, string>> _baselines =
            new Dictionary<string, Dictionary<string, string>>();

        static string Key(string catalog, string id) => catalog + "/" + id;

        static void CaptureBaseline(string catalog, string id, object record)
        {
            string key = Key(catalog, id);
            if (_baselines.ContainsKey(key)) return;

            var snapshot = new Dictionary<string, string>();
            foreach (var f in TunableField.Of(record)) snapshot[f.Name] = f.Read(record);
            _baselines[key] = snapshot;
        }

        /// <summary>
        /// The value this field shipped with, or null when the record has never
        /// been through <see cref="Apply"/>. Screens use it to mark the rows
        /// that have actually been changed.
        /// </summary>
        public static string Baseline(string catalog, string id, string fieldName)
        {
            if (!_baselines.TryGetValue(Key(catalog, id), out var snapshot)) return null;
            return snapshot.TryGetValue(fieldName, out string v) ? v : null;
        }

        public static bool IsOverridden(string catalog, string id, string fieldName)
        {
            var entry = Find(catalog, id);
            if (entry == null) return false;
            foreach (var f in entry.fields) if (f.name == fieldName) return true;
            return false;
        }

        // ----------------------------------------------------------- write

        /// <summary>
        /// Records the record's current value of one field as an override — or
        /// drops the override when the value has come back to the shipped one,
        /// which is what keeps the file sparse and what makes "changed" mean
        /// something in the UI.
        ///
        /// Nothing reaches disk here; <see cref="Save"/> does that.
        /// </summary>
        public static void Record(string catalog, string id, object record, TunableField field)
        {
            if (record == null || field == null) return;

            string current = field.Read(record);
            string shipped = Baseline(catalog, id, field.Name);

            if (shipped != null && shipped == current)
            {
                Forget(catalog, id, field.Name);
                return;
            }

            var entry = FindOrAdd(catalog, id);
            foreach (var f in entry.fields)
                if (f.name == field.Name) { f.value = current; return; }

            entry.fields.Add(new TuningField { name = field.Name, value = current });
        }

        static void Forget(string catalog, string id, string fieldName)
        {
            var entry = Find(catalog, id);
            if (entry == null) return;

            entry.fields.RemoveAll(f => f.name == fieldName);
            if (entry.fields.Count == 0) Book.entries.Remove(entry);
        }

        /// <summary>Writes every override to the player's file and returns the path.</summary>
        public static string Save()
        {
            var book = Book;
            book.savedAtUtc = DateTime.UtcNow.ToString("o");

            File.WriteAllText(UserPath, JsonUtility.ToJson(book, true));
            // Through to the browser's IndexedDB. A no-op everywhere else,
            // and the difference between a save that survives closing the
            // tab and one that does not - docs/41-WEB.md.
            Core.WebStorage.Flush();
            Debug.Log($"[Tuning] Saved overrides for {book.entries.Count} record(s) -> {UserPath}");
            Changed?.Invoke();
            return UserPath;
        }

        /// <summary>
        /// Puts one record back to its shipped values, in memory and in the
        /// file. Returns false when there was nothing to revert.
        /// </summary>
        public static bool Revert(string catalog, string id, object record)
        {
            var entry = Find(catalog, id);
            bool had = entry != null;

            if (_baselines.TryGetValue(Key(catalog, id), out var snapshot) && record != null)
                foreach (var f in TunableField.Of(record))
                    if (snapshot.TryGetValue(f.Name, out string shipped)) f.Write(record, shipped);

            if (had) Book.entries.Remove(entry);
            return had;
        }

        /// <summary>
        /// Throws away every override and deletes the file. Records already in
        /// memory are restored from their baselines by the caller passing them
        /// through <see cref="Revert"/> — this only clears the store, because
        /// <see cref="TuningStore"/> does not hold the catalogues.
        /// </summary>
        public static void Clear()
        {
            _book = new TuningBook();
            if (File.Exists(UserPath))
            {
                File.Delete(UserPath);
                Debug.Log($"[Tuning] Deleted {UserPath}. The shipped values are back.");
            }
            Changed?.Invoke();
        }

        /// <summary>Number of records carrying at least one override.</summary>
        public static int OverriddenRecordCount => Book.entries.Count;
    }
}
