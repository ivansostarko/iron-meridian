using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using IronMeridian.Core;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// One-click project setup. Run once after opening the project:
    ///
    ///     Tools > Iron Meridian > Setup Project
    ///
    /// Creates all scenes (MainMenu, Settings, Testing, EastFrance, SinglePlayer,
    /// Multiplayer, Extras, UnitsList, EffectsList, AudioList, VideoList,
    /// ModelList, UnitLibrary, Dlc, Credits, Game),
    /// wires them into Build Settings and configures player settings.
    /// All UI and world content is built at runtime by the scripts attached
    /// here, so the scenes stay tiny and merge-friendly.
    /// </summary>
    public static class ProjectBootstrap
    {
        const string ScenesDir = "Assets/Scenes";

        [MenuItem("Tools/Iron Meridian/Setup Project", priority = 0)]
        public static void SetupProject()
        {
            PlayerSettings.companyName = "IvanSostarko";
            PlayerSettings.productName = GameConfig.GameName;
            // One version, defined in code, so the player, the installer's file
            // name and a Steam build description cannot disagree about which
            // build this is.
            PlayerSettings.bundleVersion = GameConfig.Version;
            ApplyAppIcon();

            System.IO.Directory.CreateDirectory(ScenesDir);

            var scenes = new List<EditorBuildSettingsScene>
            {
                MakeScene(GameConfig.SceneMainMenu, "IronMeridian.UI.MainMenuUI"),
                MakeScene(GameConfig.SceneSettings, "IronMeridian.UI.SettingsUI"),
                MakeScene(GameConfig.SceneTesting, "IronMeridian.UI.TestingUI"),
                MakeScene(GameConfig.SceneEastFrance, "IronMeridian.UI.EastFranceUI"),
                MakeScene(GameConfig.SceneSinglePlayer, "IronMeridian.UI.SinglePlayerUI"),
                MakeScene(GameConfig.SceneMultiplayer, "IronMeridian.UI.MultiplayerUI"),
                MakeScene(GameConfig.SceneExtras, "IronMeridian.UI.ExtrasUI"),
                MakeScene(GameConfig.SceneUnitsList, "IronMeridian.UI.UnitsListUI"),
                MakeScene(GameConfig.SceneEffectsList, "IronMeridian.UI.EffectsListUI"),
                MakeScene(GameConfig.SceneAudioList, "IronMeridian.UI.AudioListUI"),
                MakeScene(GameConfig.SceneVideoList, "IronMeridian.UI.VideoListUI"),
                MakeScene(GameConfig.SceneModelList, "IronMeridian.UI.ModelListUI"),
                MakeScene(GameConfig.SceneUnitLibrary, "IronMeridian.UI.UnitLibraryUI"),
                MakeScene(GameConfig.SceneDlc, "IronMeridian.UI.DlcUI"),
                MakeScene(GameConfig.SceneCredits, "IronMeridian.UI.CreditsUI"),
                MakeScene(GameConfig.SceneGame, "IronMeridian.Core.GameController", menuCamera: false),
            };
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene($"{ScenesDir}/{GameConfig.SceneMainMenu}.unity");

            Debug.Log("[Iron Meridian] Setup complete. Press Play to start at the main menu.\n" +
                      "Remember to add your Cesium ion token — see docs/02-CESIUM.md.");
        }

        static EditorBuildSettingsScene MakeScene(string name, string bootstrapType,
            bool menuCamera = true)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (menuCamera)
            {
                // Menu scenes: plain camera with a solid background. The Game
                // scene creates its own strategy camera at runtime.
                var camGo = new GameObject("Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = GameConfig.UiBackground;
                cam.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }

            var app = new GameObject("App");
            var type = FindType(bootstrapType);
            if (type != null) app.AddComponent(type);
            else Debug.LogError($"[Iron Meridian] Bootstrap type not found: {bootstrapType}");

            string path = $"{ScenesDir}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
            return new EditorBuildSettingsScene(path, true);
        }

        static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        const string IconDir = "Assets/AppIcon";

        /// <summary>
        /// Points the player's Windows icon at <c>Assets/AppIcon/icon-*.png</c>.
        ///
        /// Without this the build carries Unity's own logo — in the taskbar, in
        /// Alt-Tab, on the desktop shortcut and in a Steam library entry. The
        /// PNGs are generated from the game logo by
        /// <c>scripts/generate_installer_art.py</c>; run that first if the
        /// folder is empty. Missing sizes are skipped rather than fatal, so a
        /// half-generated folder degrades instead of breaking setup.
        /// </summary>
        [MenuItem("Tools/Iron Meridian/Apply App Icon", priority = 12)]
        public static void ApplyAppIcon()
        {
            var target = NamedBuildTarget.Standalone;
            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Any);
            if (sizes == null || sizes.Length == 0) return;

            var icons = new Texture2D[sizes.Length];
            int found = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconDir}/icon-{sizes[i]}.png");
                icons[i] = tex;
                if (tex != null) found++;
            }

            if (found == 0)
            {
                Debug.LogWarning(
                    $"[Iron Meridian] No icons in {IconDir} — the build will use Unity's default. " +
                    "Run: python scripts/generate_installer_art.py");
                return;
            }

            PlayerSettings.SetIcons(target, icons, IconKind.Any);
            Debug.Log($"[Iron Meridian] App icon applied ({found}/{sizes.Length} sizes).");
        }

        [MenuItem("Tools/Iron Meridian/Open Docs Folder", priority = 20)]
        public static void OpenDocs() =>
            EditorUtility.RevealInFinder(System.IO.Path.GetFullPath("docs"));
    }
}
