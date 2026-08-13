using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronMeridian.Vfx;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Copies authored particle prefabs from an imported effects pack into
    /// <c>Assets/Resources/VFX/</c>, which is the only place
    /// <see cref="VfxSystem"/> can load them from at runtime.
    ///
    /// Why a copy rather than a reference: scenes and prefabs are generated in
    /// this project, so there is no serialised field anywhere to point at an
    /// asset — everything is resolved by <see cref="Resources.Load"/>. Copying
    /// with <see cref="AssetDatabase.CopyAsset"/> keeps the GUID references to
    /// the pack's own materials, shaders and textures intact, so the copies stay
    /// one file each rather than duplicating the whole pack.
    ///
    /// Effects that have no copy here simply fall back to
    /// <see cref="ProceduralVfx"/>, so running this is optional.
    /// </summary>
    public static class VfxInstaller
    {
        const string TargetFolder = "Assets/Resources/VFX";

        [MenuItem("Tools/Iron Meridian/Install VFX Prefabs", priority = 10)]
        public static void Install()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(TargetFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "VFX");

            var wanted = new HashSet<string>();
            foreach (var def in VfxCatalog.All)
            {
                if (string.IsNullOrEmpty(def.prefabPath)) continue;
                // "VFX/VFX_Fire_01_Small_Smoke" -> "VFX_Fire_01_Small_Smoke"
                int slash = def.prefabPath.LastIndexOf('/');
                wanted.Add(slash >= 0 ? def.prefabPath.Substring(slash + 1) : def.prefabPath);
            }

            int copied = 0, already = 0;
            var missing = new List<string>(wanted);

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(TargetFolder)) continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!wanted.Contains(name)) continue;

                missing.Remove(name);
                string dest = $"{TargetFolder}/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(dest) != null) { already++; continue; }

                if (AssetDatabase.CopyAsset(path, dest)) copied++;
                else Debug.LogError($"[VfxInstaller] Failed to copy {path} -> {dest}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VfxInstaller] {copied} prefab(s) installed, {already} already present, " +
                      $"{missing.Count} not found in the project.");

            if (missing.Count > 0)
                Debug.LogWarning("[VfxInstaller] No source prefab found for: " + string.Join(", ", missing) +
                    ". Those effects will use the procedural fallback. Import the pack named in " +
                    "docs/08-PARTICLE-SYSTEMS.md, or clear the prefabPath in VfxCatalog.");

            WarnIfPipelineMismatch();
        }

        /// <summary>
        /// The Free Fire VFX pack is URP-only. Under the built-in pipeline its
        /// shaders have no matching sub-shader and every particle draws magenta,
        /// so say that here rather than letting it surface as a broken-looking
        /// battle at runtime.
        /// </summary>
        static void WarnIfPipelineMismatch()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;

                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in r.sharedMaterials)
                        if (mat != null && mat.shader != null && !mat.shader.isSupported)
                        {
                            Debug.LogWarning(
                                $"[VfxInstaller] '{mat.shader.name}' has no sub-shader supported by the active " +
                                "render pipeline. Iron Meridian runs the built-in pipeline and this pack is " +
                                "URP-only, so VfxSystem will use procedural fire and smoke instead. " +
                                "See docs/08-PARTICLE-SYSTEMS.md for the options.");
                            return;
                        }
            }
        }
    }
}
