using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// Android player settings and the batch build behind
    /// <c>scripts/build-android.ps1</c>.
    ///
    /// Kept apart from <see cref="ProjectBootstrap"/> on purpose. Setup is about
    /// the *project* — the scenes, the build list, the things every platform
    /// shares — and this is about one platform's own settings, most of which
    /// would be noise in a Windows build and one of which (the keystore) must
    /// never be written into a file that is checked in.
    ///
    /// **What actually makes this port possible** is not in this file: it is
    /// <see cref="Core.StreamingAssetsFile"/>, because on Android StreamingAssets
    /// is an archive rather than a folder, and <see cref="Core.TouchInput"/>,
    /// because there is no right mouse button. See docs/40-ANDROID.md.
    /// </summary>
    public static class AndroidBuild
    {
        /// <summary>
        /// Reverse-DNS identifier the store and the device both key off. Changing
        /// it after a release makes a *different app* as far as Android is
        /// concerned, saves and all — so it is a constant, not a parameter.
        /// </summary>
        public const string PackageName = "me.ivansostarko.ironmeridian";

        /// <summary>
        /// The oldest Android this will run on. 26 (Oreo, 2017) rather than the
        /// 22 Unity defaults to: Cesium's native library is built against a
        /// modern NDK, Vulkan is only worth having from 26 up, and the devices
        /// below it have neither the memory nor the bandwidth to stream 3D
        /// terrain in the first place.
        /// </summary>
        public const AndroidSdkVersions MinSdk = AndroidSdkVersions.AndroidApiLevel26;

        [MenuItem("Tools/Iron Meridian/Android/Apply Player Settings", priority = 40)]
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "IvanSostarko";
            PlayerSettings.productName = GameConfig.GameName;
            PlayerSettings.bundleVersion = GameConfig.Version;
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android, PackageName);

            // **ARM64 with IL2CPP, and nothing else.** Play has required a
            // 64-bit binary since 2019, Cesium ships an arm64 native, and Mono
            // is not an option for a 64-bit Android build at all. x86_64 is
            // emulator-only and doubles the APK, so it is off unless somebody
            // deliberately turns it on to test on one.
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.Android.minSdkVersion = MinSdk;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Vulkan first, GLES3 behind it. Cesium's terrain shading is heavy
            // enough that the driver matters, and leaving GLES3 in place means a
            // device with a bad Vulkan driver still starts rather than showing a
            // black screen.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            });

            // A map is read across, not down, and every panel in this game is
            // laid out against a landscape canvas.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // The terrain streams over HTTPS from Cesium ion, so the app is
            // useless without the network; asking for it here means the store
            // listing says so and a device without it is told why.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = false;

            // The map is the whole screen; a status bar over it is a strip of
            // terrain the player cannot see or touch.
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = false;

            // Terrain tiles are the memory cost here and they are already
            // budgeted by Cesium; a 60 fps target on a device that cannot hold
            // it just burns battery to drop frames.
            PlayerSettings.Android.blitType = AndroidBlitType.Auto;
            PlayerSettings.Android.optimizedFramePacing = true;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Iron Meridian] Android player settings applied " +
                      $"({PackageName}, min SDK {(int)MinSdk}, ARM64/IL2CPP).");
        }

        /// <summary>
        /// The batch entry point. Called by <c>scripts/build-android.ps1</c>,
        /// which is also where the switches below come from.
        ///
        /// Reads its arguments off the command line rather than taking
        /// parameters, because <c>-executeMethod</c> can only call a no-arg
        /// static:
        ///
        ///   <c>-ironmeridian-output &lt;path&gt;</c>   where to write the file
        ///   <c>-ironmeridian-aab</c>               an App Bundle instead of an APK
        ///   <c>-ironmeridian-development</c>       a development build, profiler attached
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string output = Arg("-ironmeridian-output");
            bool aab = Flag("-ironmeridian-aab");
            bool development = Flag("-ironmeridian-development");

            if (string.IsNullOrEmpty(output))
                output = Path.Combine("Builds", "Android",
                    aab ? "IronMeridian.aab" : "IronMeridian.apk");

            Build(output, aab, development);
        }

        public static void Build(string outputPath, bool appBundle, bool development)
        {
            ApplySettings();
            EditorUserBuildSettings.buildAppBundle = appBundle;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            if (scenes.Count == 0)
                throw new Exception(
                    "No scenes in the build. Run Tools > Iron Meridian > Setup Project first — " +
                    "it is also what writes StreamingAssets/Maps/index.json, without which the " +
                    "Android build ships scenarios it cannot list.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"Android build {summary.result}: " +
                                    $"{summary.totalErrors} error(s). See the log.");

            Debug.Log($"[Iron Meridian] Android build ok — {outputPath} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalMinutes:0.#} min).");
        }

        [MenuItem("Tools/Iron Meridian/Android/Build APK", priority = 41)]
        static void BuildApkMenu() =>
            Build(Path.Combine("Builds", "Android", "IronMeridian.apk"), appBundle: false, development: false);

        [MenuItem("Tools/Iron Meridian/Android/Build App Bundle (.aab)", priority = 42)]
        static void BuildAabMenu() =>
            Build(Path.Combine("Builds", "Android", "IronMeridian.aab"), appBundle: true, development: false);

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
