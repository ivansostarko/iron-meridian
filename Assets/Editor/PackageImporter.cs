using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Imports asset packs that ship as a nested <c>.unitypackage</c> rather than
    /// as loose files.
    ///
    /// Some Asset Store packages do not contain the model at all — they contain
    /// one archive per render pipeline and expect you to import the right one by
    /// hand. Dropping such a package into <c>Assets/</c> therefore looks like an
    /// import but installs nothing, and the model installer correctly reports the
    /// mesh as missing because it genuinely is. This command closes that gap.
    ///
    /// **Built-In only.** This project runs the built-in render pipeline
    /// (see CLAUDE.md golden rule 5), so URP and HDRP variants are deliberately
    /// skipped — importing one would install materials whose shaders resolve to
    /// magenta here.
    ///
    /// Run this once after adding such a pack, then
    /// <c>Tools > Iron Meridian > Install Unit Models</c> to build the prefab.
    /// The two are separate commands because <see cref="AssetDatabase.ImportPackage"/>
    /// finishes asynchronously: the new assets do not exist until the import
    /// callback has run, so nothing in the same invocation can use them.
    /// </summary>
    public static class PackageImporter
    {
        /// <summary>Archives whose name contains one of these is for another pipeline.</summary>
        static readonly string[] ForeignPipelines = { "URP", "HDRP" };

        [MenuItem("Tools/Iron Meridian/Import Bundled Packages", priority = 10)]
        public static void ImportBundled()
        {
            var all = Directory.GetFiles(Application.dataPath, "*.unitypackage", SearchOption.AllDirectories);

            if (all.Length == 0)
            {
                Debug.Log("[PackageImporter] No bundled .unitypackage files under Assets/ — nothing to do.");
                return;
            }

            var chosen = new List<string>();
            var skipped = new List<string>();

            foreach (string full in all.OrderBy(p => p))
            {
                string name = Path.GetFileNameWithoutExtension(full);

                if (ForeignPipelines.Any(p => ContainsToken(name, p)))
                {
                    skipped.Add($"  · {name} (built for another render pipeline)");
                    continue;
                }

                chosen.Add(full);
            }

            foreach (string full in chosen)
            {
                Debug.Log($"[PackageImporter] Importing '{Path.GetFileName(full)}'…");
                // false = do not show the import dialog; the whole package is taken.
                AssetDatabase.ImportPackage(full, false);
            }

            Report(chosen, skipped);
        }

        /// <summary>
        /// Whole-word match, so a pack called "Urban Props" is not mistaken for
        /// a URP build while "Bomber - URP" still is.
        /// </summary>
        static bool ContainsToken(string name, string token)
        {
            int i = name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                bool startOk = i == 0 || !char.IsLetterOrDigit(name[i - 1]);
                int end = i + token.Length;
                bool endOk = end >= name.Length || !char.IsLetterOrDigit(name[end]);
                if (startOk && endOk) return true;
                i = name.IndexOf(token, i + 1, System.StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        static void Report(List<string> chosen, List<string> skipped)
        {
            var sb = new StringBuilder();
            sb.Append($"[PackageImporter] Started import of {chosen.Count} package(s)");
            if (chosen.Count > 0)
                sb.Append(": ").Append(string.Join(", ", chosen.Select(Path.GetFileName)));
            sb.Append('.');

            if (skipped.Count > 0)
                sb.AppendLine().AppendLine()
                  .AppendLine($"Skipped {skipped.Count}:")
                  .Append(string.Join("\n", skipped));

            if (chosen.Count > 0)
                sb.AppendLine().AppendLine()
                  .Append("Importing finishes asynchronously. Once the project has finished reimporting, run ")
                  .Append("Tools > Iron Meridian > Install Unit Models to build the prefabs. ")
                  .Append("See docs/09-3D-MODELS.md.");

            Debug.Log(sb.ToString());
        }
    }
}
