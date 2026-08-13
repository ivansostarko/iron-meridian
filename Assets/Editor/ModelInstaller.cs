using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Turns imported model packs into something the game can load at runtime,
    /// and generates the prefabs <see cref="ModelPreview"/> asks for.
    ///
    /// Two things have to happen, and neither can be done from runtime code:
    ///
    /// 1. **Rig type → Legacy**, for anything animated. The project builds every
    ///    scene and prefab from code, so there is no Animator Controller asset to
    ///    reference and none can be created at runtime. Legacy
    ///    <see cref="Animation"/> can take a clip handed to it directly, which is
    ///    the only fully runtime-driven path — and it matches the project's
    ///    legacy Input/uGUI stack. Static props skip this entirely: forcing a rig
    ///    type onto a mesh with no skeleton reimports the pack for nothing.
    ///
    /// 2. **A prefab under Resources.** `Resources.Load` is the only lookup path
    ///    available (see <see cref="IronMeridian.Core.RuntimeMaterials"/> for the
    ///    same reasoning about shaders). The generated prefab references the
    ///    original mesh by GUID, so the pack is not duplicated.
    ///
    /// It is driven by <see cref="UnitModelLibrary"/> rather than its own list,
    /// so adding a model is one entry there. **Packs that are not imported are
    /// skipped with a note naming the file names it looked for** — the installer
    /// is expected to run on a project with only some of the packs present, and
    /// must never fail the ones that are there because of the ones that are not.
    ///
    /// Re-run this after importing or updating a pack. Then update
    /// docs/09-3D-MODELS.md.
    /// </summary>
    public static class ModelInstaller
    {
        const string TargetFolder = "Assets/Resources/Models";

        /// <summary>Source FBX names (without extension) and the clip name the game uses for each.</summary>
        static readonly (string fbx, string clipName)[] Clips =
        {
            ("demo_combat_idle",  ModelClips.CombatIdle),
            ("demo_combat_run",   ModelClips.CombatRun),
            ("demo_combat_shoot", ModelClips.CombatShoot)
        };

        [MenuItem("Tools/Iron Meridian/Install Unit Models", priority = 11)]
        public static void Install()
        {
            EnsureFolder();

            var installed = new List<string>();
            var missing = new List<string>();
            bool reimported = false;

            foreach (var entry in UnitModelLibrary.Entries)
            {
                var def = entry.Value;
                string modelPath = FindFirst(def.sourceCandidates);

                if (modelPath == null)
                {
                    missing.Add($"  · {def.sourceAsset} → looked for {Join(def.sourceCandidates)}");
                    continue;
                }

                if (InstallOne(entry.Key, def, modelPath, ref reimported)) installed.Add(entry.Key);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Report(installed, missing, reimported);
        }

        /// <summary>Builds one prefab. Returns false if the source could not be instantiated.</summary>
        static bool InstallOne(string modelId, UnitModelDef def, string modelPath, ref bool reimported)
        {
            var clipAssets = new List<(AnimationClip clip, string name)>();

            if (def.animated)
            {
                reimported |= SetLegacy(modelPath);

                foreach (var (fbx, clipName) in Clips)
                {
                    string path = FindAsset(fbx);
                    if (path == null)
                    {
                        Debug.LogWarning($"[ModelInstaller] Animation '{fbx}.FBX' not found — " +
                                         $"'{clipName}' will be unavailable on {modelId}.");
                        continue;
                    }

                    reimported |= SetLegacy(path);

                    var clip = LoadClip(path);
                    if (clip == null)
                        Debug.LogWarning($"[ModelInstaller] '{fbx}.FBX' contains no legacy AnimationClip after reimport.");
                    else
                        clipAssets.Add((clip, clipName));
                }
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
            {
                Debug.LogError($"[ModelInstaller] Could not instantiate '{modelPath}' for {modelId}.");
                return false;
            }

            string prefabName = System.IO.Path.GetFileName(def.resourcePath);

            try
            {
                instance.name = prefabName;

                if (clipAssets.Count > 0)
                {
                    var anim = instance.GetComponent<Animation>() ?? instance.AddComponent<Animation>();
                    foreach (var (clip, name) in clipAssets) anim.AddClip(clip, name);

                    foreach (var (clip, name) in clipAssets)
                        if (name == def.idleClip) { anim.clip = clip; break; }

                    anim.playAutomatically = false;   // ModelPreview decides what to play
                    anim.wrapMode = WrapMode.Loop;
                }

                string dest = $"{TargetFolder}/{prefabName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, dest);

                string clipNote = clipAssets.Count > 0
                    ? $"{clipAssets.Count} clip(s)"
                    : "static (no animation)";
                Debug.Log($"[ModelInstaller] {modelId}: {dest} from '{modelPath}' — {clipNote}.");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void Report(List<string> installed, List<string> missing, bool reimported)
        {
            var sb = new StringBuilder();
            sb.Append($"[ModelInstaller] Installed {installed.Count} model prefab(s)");
            if (installed.Count > 0) sb.Append(": ").Append(string.Join(", ", installed));
            sb.Append('.');

            if (missing.Count > 0)
            {
                sb.AppendLine().AppendLine()
                  .AppendLine($"{missing.Count} model(s) had no source mesh in the project:")
                  .AppendLine(string.Join("\n", missing))
                  .AppendLine()
                  .Append("Import the pack, or — if it is imported under a different file name — add that ")
                  .Append("name to the entry's sourceCandidates in UnitModelLibrary.cs. ")
                  .Append("See docs/09-3D-MODELS.md.");
            }

            if (missing.Count > 0) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());

            if (reimported)
                Debug.Log("[ModelInstaller] Rig type set to Legacy on the animated source FBX files. " +
                          "That is a change to the imported pack, and is what lets the game play clips " +
                          "without an Animator Controller asset (docs/09-3D-MODELS.md).");
        }

        static string Join(string[] names) =>
            names == null || names.Length == 0 ? "(nothing configured)" : string.Join(", ", names);

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(TargetFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "Models");
        }

        /// <summary>First candidate name that resolves to a model asset, or null.</summary>
        static string FindFirst(string[] candidates)
        {
            if (candidates == null) return null;
            foreach (string name in candidates)
            {
                string path = FindAsset(name);
                if (path != null) return path;
            }
            return null;
        }

        static string FindAsset(string name)
        {
            // Quoted so names containing '-' (ZIL-130) are not split into terms.
            foreach (string guid in AssetDatabase.FindAssets($"\"{name}\" t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return path;
            }
            return null;
        }

        /// <summary>Switches an FBX to a Legacy rig. Returns true if a reimport was needed.</summary>
        static bool SetLegacy(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return false;
            if (importer.animationType == ModelImporterAnimationType.Legacy) return false;

            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.SaveAndReimport();
            return true;
        }

        static AnimationClip LoadClip(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                // Skip the __preview__ clips Unity generates alongside the real one.
                if (asset is AnimationClip clip && clip.legacy && !clip.name.StartsWith("__"))
                    return clip;
            return null;
        }
    }
}
