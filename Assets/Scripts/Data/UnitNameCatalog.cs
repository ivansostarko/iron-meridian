using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IronMeridian.Data
{
    /// <summary>One family of formation names — see StreamingAssets/Data/unit-names.json.</summary>
    [Serializable]
    public class UnitNameGroup
    {
        /// <summary>Identifies the group in the file and in log messages. Not shown to the player.</summary>
        public string key;
        /// <summary>Type ids from units.json this group names outright.</summary>
        public string[] unitIds;
        /// <summary>Branches this group names when no id group claimed the type.</summary>
        public string[] branches;
        /// <summary>The names themselves, without an echelon or a number.</summary>
        public string[] titles;
    }

    [Serializable]
    public class UnitNameFile
    {
        public string pattern = "{ordinal} {title}";
        public string[] ordinals;
        public List<UnitNameGroup> groups = new List<UnitNameGroup>();
        public string[] fallbackTitles;
    }

    /// <summary>
    /// Gives every formation on the map a name of its own — "3rd Lancers", "12th
    /// Field Battery" — instead of the type name every unit of that type shares.
    ///
    /// **The names are data, not code.** They live in
    /// <c>StreamingAssets/Data/unit-names.json</c> beside <c>units.json</c>, in
    /// families that are matched to a type by its id first and its branch
    /// second, so an armoured unit draws from the cavalry list and a sapper unit
    /// from the engineer one. Editing the file is the whole of retuning this;
    /// there is no generator behind it and nothing in C# to change.
    ///
    /// **A name is issued once and then kept.** The generated name is written to
    /// <see cref="UnitState.customName"/> at spawn, which is a saved field — so
    /// it survives a save/load round trip and the unit is called the same thing
    /// tomorrow. That is also why this only ever runs for a unit whose
    /// <c>customName</c> is empty: a name from a save, or one the player typed,
    /// is not something to overwrite.
    ///
    /// Uniqueness is checked against whatever is already on the map rather than
    /// against a set kept here. A register would have to be cleared on every map
    /// load and reset, and the one that was missed would be a generator that
    /// slowly ran out of names; asking the map is always right.
    /// </summary>
    public static class UnitNameCatalog
    {
        const string FileName = "unit-names.json";

        static UnitNameFile _file;
        static Dictionary<string, UnitNameGroup> _byUnitId;
        static Dictionary<string, UnitNameGroup> _byBranch;

        /// <summary>
        /// A name for one formation, unique among those <paramref name="isTaken"/>
        /// reports as already in use.
        /// </summary>
        /// <param name="def">The type being deployed — decides which family the name comes from.</param>
        /// <param name="seed">
        /// Anything stable and per-unit (the instance id). It only picks the
        /// starting point in the family, so two units of the same type deployed
        /// one after the other are not handed consecutive numbers every time.
        /// </param>
        /// <param name="isTaken">
        /// True if a name is already on the map. Passed in rather than read from
        /// <c>UnitRegistry</c> so this stays in Data and does not reach up into
        /// the runtime it is loaded by.
        /// </param>
        public static string Generate(UnitDefinition def, string seed, Func<string, bool> isTaken)
        {
            EnsureLoaded();
            if (def == null) return "";

            string[] titles = TitlesFor(def);
            string[] ordinals = _file.ordinals;
            if (titles == null || titles.Length == 0 || ordinals == null || ordinals.Length == 0)
                return def.name;

            int total = titles.Length * ordinals.Length;
            int start = (int)(StableHash(seed) % (uint)total);

            // Ordinal-minor, so consecutive probes walk the numbers of one title
            // — 1st Rifles, 2nd Rifles, 3rd Rifles — which is how an order of
            // battle actually reads, rather than scattering across the list.
            for (int i = 0; i < total; i++)
            {
                int idx = (start + i) % total;
                string name = Compose(ordinals[idx % ordinals.Length], titles[idx / ordinals.Length]);
                if (isTaken == null || !isTaken(name)) return name;
            }

            // Every combination in the family is on the map already. Numbering
            // past the end is ugly, but a duplicate name is worse and refusing
            // to name the unit at all is worse still.
            for (int n = 2; n < 1000; n++)
            {
                string name = Compose(ordinals[0], titles[0]) + " (" + n + ")";
                if (isTaken == null || !isTaken(name)) return name;
            }
            return def.name;
        }

        /// <summary>
        /// The family a type draws from: its own id if a group claims it,
        /// otherwise its branch, otherwise the fallback list. Null only if the
        /// file has neither a matching group nor a fallback.
        /// </summary>
        public static string[] TitlesFor(UnitDefinition def)
        {
            EnsureLoaded();
            if (def == null) return _file.fallbackTitles;

            if (!string.IsNullOrEmpty(def.id) && _byUnitId.TryGetValue(def.id, out var byId))
                return byId.titles;
            if (!string.IsNullOrEmpty(def.branch) && _byBranch.TryGetValue(def.branch, out var byBranch))
                return byBranch.titles;
            return _file.fallbackTitles;
        }

        static string Compose(string ordinal, string title) =>
            _file.pattern.Replace("{ordinal}", ordinal).Replace("{title}", title);

        /// <summary>
        /// FNV-1a over the seed. <see cref="string.GetHashCode"/> is explicitly
        /// not stable between runs or runtimes, and a unit that is called
        /// something different every time the game starts is exactly what this
        /// is meant to avoid for the case where the name was never saved.
        /// </summary>
        static uint StableHash(string s)
        {
            uint h = 2166136261u;
            if (string.IsNullOrEmpty(s)) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            return h;
        }

        static void EnsureLoaded()
        {
            if (_file != null) return;

            _file = ReadFile() ?? Fallback();

            if (string.IsNullOrEmpty(_file.pattern)) _file.pattern = "{ordinal} {title}";
            if (_file.ordinals == null || _file.ordinals.Length == 0)
                _file.ordinals = new[] { "1st", "2nd", "3rd", "4th", "5th" };
            if (_file.fallbackTitles == null || _file.fallbackTitles.Length == 0)
                _file.fallbackTitles = new[] { "Detachment" };

            // First group to claim an id or a branch wins, so the file reads
            // top-down: the specific families are written above the branch-wide
            // ones and a later duplicate is ignored rather than overriding.
            _byUnitId = new Dictionary<string, UnitNameGroup>();
            _byBranch = new Dictionary<string, UnitNameGroup>();
            foreach (var g in _file.groups)
            {
                if (g == null || g.titles == null || g.titles.Length == 0) continue;
                if (g.unitIds != null)
                    foreach (var id in g.unitIds)
                        if (!string.IsNullOrEmpty(id) && !_byUnitId.ContainsKey(id)) _byUnitId[id] = g;
                if (g.branches != null)
                    foreach (var b in g.branches)
                        if (!string.IsNullOrEmpty(b) && !_byBranch.ContainsKey(b)) _byBranch[b] = g;
            }

            Debug.Log($"[UnitNameCatalog] {_file.groups.Count} name group(s), " +
                      $"{_byUnitId.Count} type(s) and {_byBranch.Count} branch(es) matched.");
        }

        /// <summary>
        /// Reads the file, reporting rather than throwing. A missing or broken
        /// name list must not stop units being deployed — the map is playable
        /// with every formation called after its type, which is what the game
        /// did before this existed.
        /// </summary>
        static UnitNameFile ReadFile()
        {
            try
            {
                // Through StreamingAssetsFile: on Android this is an APK entry
                // rather than a file — docs/40-ANDROID.md.
                string json = Core.StreamingAssetsFile.ReadAllText("Data/" + FileName);
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning($"[UnitNameCatalog] {FileName} not found — units keep their type names.");
                    return null;
                }
                var parsed = JsonUtility.FromJson<UnitNameFile>(json);
                if (parsed == null || parsed.groups == null)
                {
                    Debug.LogWarning($"[UnitNameCatalog] {FileName} has no groups — units keep their type names.");
                    return null;
                }
                return parsed;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnitNameCatalog] Could not read {FileName} — units keep their type names.\n{e}");
                return null;
            }
        }

        /// <summary>Enough of a catalogue to keep the game running without the file.</summary>
        static UnitNameFile Fallback() => new UnitNameFile
        {
            pattern = "{ordinal} {title}",
            ordinals = new[] { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th" },
            groups = new List<UnitNameGroup>(),
            fallbackTitles = new[] { "Detachment", "Support Group", "Field Section" }
        };
    }
}
