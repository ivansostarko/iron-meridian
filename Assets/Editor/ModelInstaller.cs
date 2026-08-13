using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronMeridian.Models;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Turns an imported character pack into something the game can load at
    /// runtime, and generates the prefab <see cref="ModelPreview"/> asks for.
    ///
    /// Two things have to happen, and neither can be done from runtime code:
    ///
    /// 1. **Rig type → Legacy.** The project builds every scene and prefab from
    ///    code, so there is no Animator Controller asset to reference and none
    ///    can be created at runtime. Legacy <see cref="Animation"/> can take a
    ///    clip handed to it directly, which is the only fully runtime-driven
    ///    path — and it matches the project's legacy Input/uGUI stack.
    ///
    /// 2. **A prefab under Resources.** `Resources.Load` is the only lookup path
    ///    available (see <see cref="IronMeridian.Core.RuntimeMaterials"/> for the
    ///    same reasoning about shaders). The generated prefab references the
    ///    original FBX by GUID, so the pack is not duplicated.
    ///
    /// Re-run this after importing or updating a character pack. Then update
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

        const string SourceModelFbx = "Soldier_demo";
        const string GeneratedPrefab = "Soldier_Rifleman";

        [MenuItem("Tools/Iron Meridian/Install Unit Models", priority = 11)]
        public static void Install()
        {
            string modelPath = FindAsset(SourceModelFbx, "t:Model");
            if (modelPath == null)
            {
                Debug.LogError($"[ModelInstaller] Could not find '{SourceModelFbx}.FBX' in the project. " +
                    "Import the Low Poly Soldiers Demo pack — see docs/09-3D-MODELS.md.");
                return;
            }

            EnsureFolder();

            // --- 1. force Legacy rigs so the clips are usable without a controller
            bool reimported = SetLegacy(modelPath);
            var clipAssets = new List<(AnimationClip clip, string name)>();

            foreach (var (fbx, clipName) in Clips)
            {
                string path = FindAsset(fbx, "t:Model");
                if (path == null)
                {
                    Debug.LogWarning($"[ModelInstaller] Animation '{fbx}.FBX' not found — '{clipName}' will be unavailable.");
                    continue;
                }

                reimported |= SetLegacy(path);

                var clip = LoadClip(path);
                if (clip == null)
                    Debug.LogWarning($"[ModelInstaller] '{fbx}.FBX' contains no legacy AnimationClip after reimport.");
                else
                    clipAssets.Add((clip, clipName));
            }

            // --- 2. build the prefab: model instance + Animation with every clip
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
            {
                Debug.LogError($"[ModelInstaller] Could not instantiate '{modelPath}'.");
                return;
            }

            try
            {
                instance.name = GeneratedPrefab;

                var anim = instance.GetComponent<Animation>();
                if (anim == null) anim = instance.AddComponent<Animation>();

                foreach (var (clip, name) in clipAssets) anim.AddClip(clip, name);

                foreach (var (clip, name) in clipAssets)
                    if (name == ModelClips.CombatIdle) { anim.clip = clip; break; }

                anim.playAutomatically = false;   // ModelPreview decides what to play
                anim.wrapMode = WrapMode.Loop;

                string dest = $"{TargetFolder}/{GeneratedPrefab}.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, dest);
                Debug.Log($"[ModelInstaller] Installed {dest} with {clipAssets.Count} clip(s): " +
                          string.Join(", ", clipAssets.ConvertAll(c => c.name)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (reimported)
                Debug.Log("[ModelInstaller] Rig type set to Legacy on the source FBX files. " +
                          "That is a change to the imported pack, and is what lets the game play clips " +
                          "without an Animator Controller asset (docs/09-3D-MODELS.md).");
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(TargetFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "Models");
        }

        static string FindAsset(string name, string filter)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} {filter}"))
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
