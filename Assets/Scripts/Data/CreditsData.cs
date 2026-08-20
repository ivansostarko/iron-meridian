using System;
using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.Data
{
    /// <summary>One credited role and everybody who held it.</summary>
    [Serializable]
    public class CreditRole
    {
        /// <summary>What they did — "Lead Designer", "Programmers".</summary>
        public string role = "";

        /// <summary>
        /// Who did it, in the order they should be listed.
        ///
        /// A list rather than one name, because most roles on most projects are
        /// held by more than one person and a schema that assumes otherwise
        /// makes the second one a second role with the same title.
        /// </summary>
        public List<string> names = new List<string>();
    }

    /// <summary>A block of the roll — "PRODUCTION", "AUDIO" — and the roles under it.</summary>
    [Serializable]
    public class CreditSection
    {
        public string heading = "";
        public List<CreditRole> roles = new List<CreditRole>();
    }

    /// <summary>
    /// The credits roll, as data.
    ///
    /// **Why this is a file and not a screen builder.** Credits change for
    /// reasons that have nothing to do with the program: somebody joins, a name
    /// is spelled wrong, a contractor has to be added before a submission. Every
    /// one of those is a text edit, and none of them should need a compiler, a
    /// programmer or a build. The screen reads this; anybody can edit it.
    ///
    /// It follows the same rule as every other shipped data file — under
    /// <c>StreamingAssets/Data</c>, read through
    /// <see cref="StreamingAssetsFile"/> so the web and Android builds can
    /// reach it, and named in <see cref="StreamingAssetsFile.CoreFiles"/> so the
    /// web build preloads it. See docs/44-CREDITS.md.
    /// </summary>
    [Serializable]
    public class CreditsData
    {
        public string title = "IRON MERIDIAN";
        public string subtitle = "";
        /// <summary>Shown at the foot of the roll, on its own.</summary>
        public string website = "";

        public List<CreditSection> sections = new List<CreditSection>();

        /// <summary>
        /// Lines after the roll: the engine, the data, the standards. Not a
        /// licence register — that is docs/37-THIRD-PARTY.md, which is the file
        /// a lawyer reads and this is the one a player does.
        /// </summary>
        public List<string> acknowledgements = new List<string>();

        public string copyright = "";

        // ------------------------------------------------------------- loading

        /// <summary>Where the file lives, relative to StreamingAssets.</summary>
        public const string ResourcePath = "Data/credits.json";

        static CreditsData _cache;

        /// <summary>
        /// The roll, read once per session.
        ///
        /// **Never null.** A credits screen that came up blank because a file
        /// was missing would be the most conspicuous possible failure — it is
        /// the one screen whose whole content is other people's names. A
        /// missing or unreadable file falls back to a minimal roll that still
        /// says what the game is, and logs why.
        /// </summary>
        public static CreditsData Load()
        {
            if (_cache != null) return _cache;

            string json = StreamingAssetsFile.ReadAllText(ResourcePath);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<CreditsData>(json);
                    if (parsed != null && parsed.sections != null)
                    {
                        _cache = parsed;
                        return _cache;
                    }
                    Debug.LogError($"[Credits] {ResourcePath} is not a credits file.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Credits] Could not read {ResourcePath}: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"[Credits] {ResourcePath} not found — showing the fallback roll.");
            }

            _cache = Fallback();
            return _cache;
        }

        /// <summary>Drops the cache so an edited file is picked up without a restart.</summary>
        public static void Reload() => _cache = null;

        static CreditsData Fallback() => new CreditsData
        {
            title = GameConfig.GameName.ToUpperInvariant(),
            subtitle = "A real-terrain operational wargame",
            website = "www.example.com",
            sections = new List<CreditSection>
            {
                new CreditSection
                {
                    heading = "CREDITS",
                    roles = new List<CreditRole>
                    {
                        new CreditRole { role = "Credits file missing", names = { "Data/credits.json" } }
                    }
                }
            }
        };

        /// <summary>Total people listed, however many times each appears. Shown as a count on the screen.</summary>
        public int NameCount()
        {
            int n = 0;
            if (sections == null) return 0;
            foreach (var s in sections)
            {
                if (s?.roles == null) continue;
                foreach (var r in s.roles) n += r?.names?.Count ?? 0;
            }
            return n;
        }
    }
}
