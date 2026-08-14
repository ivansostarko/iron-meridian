using System.Collections.Generic;
using UnityEditor;
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
    /// Multiplayer, Extras, UnitsList, Game),
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

        [MenuItem("Tools/Iron Meridian/Open Docs Folder", priority = 20)]
        public static void OpenDocs() =>
            EditorUtility.RevealInFinder(System.IO.Path.GetFullPath("docs"));
    }
}
