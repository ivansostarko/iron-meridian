using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Linux player settings and the batch build behind
    /// <c>scripts/build-linux.ps1</c> — which is to say, the **native Steam Deck
    /// build**, because SteamOS is Linux.
    ///
    /// **A native build is not required to support the Deck.** The Windows
    /// player runs on one through Proton, and for a game with no anti-cheat and
    /// no launcher that usually just works; plenty of Verified titles ship
    /// nothing else. What a native build buys is one fewer translation layer
    /// between this game and the GPU, a smaller memory footprint on a machine
    /// with 16 GB shared between both, and controller axes that arrive as
    /// themselves rather than through XInput emulation — see
    /// <see cref="Core.GamepadInput"/>, which has to know which of the two it is
    /// under.
    ///
    /// What actually makes the Deck playable is neither: it is
    /// <see cref="Core.GamepadInput"/> and <see cref="Core.SteamDeck"/>, because
    /// the machine has no keyboard and half this game's verbs were keys. See
    /// docs/42-STEAM-DECK.md.
    /// </summary>
    public static class LinuxBuild
    {
        /// <summary>
        /// The executable's name. No extension, and no spaces: this is what gets
        /// typed into a Steam launch option and into a shell, and "Iron
        /// Meridian.x86_64" is a filename that needs quoting in both.
        /// </summary>
        public const string ExecutableName = "IronMeridian.x86_64";

        [MenuItem("Tools/Iron Meridian/Linux/Apply Player Settings", priority = 60)]
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "IvanSostarko";
            PlayerSettings.productName = GameConfig.GameName;
            PlayerSettings.bundleVersion = GameConfig.Version;

            var linux = UnityEditor.Build.NamedBuildTarget.Standalone;

            // IL2CPP, as on Windows. Mono would build, and the Deck's own
            // Proton/native comparison is only honest if both sides use the same
            // backend as the shipping Windows player.
            PlayerSettings.SetScriptingBackend(linux, ScriptingImplementation.IL2CPP);

            // Vulkan first. Mesa's Vulkan driver on RDNA 2 is the path the Deck
            // is actually tuned for; GLES/GL is left behind it so a desktop
            // Linux box with an older stack still starts.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore
            });

            // The Deck is a 1280x800 handheld and the game is played fullscreen
            // on it; a windowed default would open a window the compositor then
            // has to fight with.
            PlayerSettings.defaultScreenWidth = SteamDeck.ScreenWidth;
            PlayerSettings.defaultScreenHeight = SteamDeck.ScreenHeight;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;

            // Steam's own overlay wants the game to keep rendering while it is
            // up, and a battle that froze behind the overlay would look hung.
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[Iron Meridian] Linux player settings applied " +
                      $"(IL2CPP, Vulkan, {SteamDeck.ScreenWidth}x{SteamDeck.ScreenHeight} default).");
        }

        /// <summary>
        /// The batch entry point, called by <c>scripts/build-linux.ps1</c>.
        ///
        ///   <c>-ironmeridian-output &lt;path&gt;</c>   the executable to write
        ///   <c>-ironmeridian-development</c>       development build, profiler attachable
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string output = Arg("-ironmeridian-output");
            bool development = Flag("-ironmeridian-development");

            if (string.IsNullOrEmpty(output))
                output = Path.Combine("Builds", "Linux", ExecutableName);

            Build(output, development);
        }

        public static void Build(string outputPath, bool development)
        {
            ApplySettings();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            if (scenes.Count == 0)
                throw new Exception(
                    "No scenes in the build. Run Tools > Iron Meridian > Setup Project first.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"Linux build {summary.result}: " +
                                    $"{summary.totalErrors} error(s). See the log.");

            Debug.Log($"[Iron Meridian] Linux build ok — {outputPath} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalMinutes:0.#} min).");
        }

        [MenuItem("Tools/Iron Meridian/Linux/Build", priority = 61)]
        static void BuildMenu() =>
            Build(Path.Combine("Builds", "Linux", ExecutableName), development: false);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static bool Flag(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (a == name) return true;
            return false;
        }
    }
}
