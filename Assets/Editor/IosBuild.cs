using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// iOS player settings, the Xcode project export, and the Info.plist keys
    /// Unity does not write for you.
    ///
    /// **Unity does not build an app here — it builds an Xcode project.** That
    /// is not a limitation of this script: on every platform Unity runs on, the
    /// iOS target produces an Xcode workspace, and turning that into a signed
    /// `.ipa` needs Xcode, which needs macOS. On Windows the export still works
    /// and is still worth running — it is what catches a missing module, a
    /// stripping failure or an IL2CPP error — but the last mile is a Mac. See
    /// docs/43-IOS.md §5.
    ///
    /// **Most of the port was already done.** iOS is a touch platform, so
    /// <see cref="Core.TouchInput"/> and the handheld canvas scale from the
    /// Android work apply unchanged; StreamingAssets is a real directory here
    /// (unlike Android), so <see cref="Core.StreamingAssetsFile"/> takes its
    /// plain-file path; recording is already off wherever there are no child
    /// processes. What is genuinely iOS's own is the **safe area**
    /// (<see cref="UI.SafeAreaCanvas"/>), the signing and capability settings
    /// below, and the plist keys in <see cref="OnPostprocessBuild"/>.
    /// </summary>
    public static class IosBuild
    {
        /// <summary>
        /// Reverse-DNS identifier, and the same one Android uses. Changing it
        /// after a release makes a different app as far as the App Store is
        /// concerned, so it is a constant rather than a parameter.
        /// </summary>
        public const string BundleIdentifier = "me.ivansostarko.ironmeridian";

        /// <summary>
        /// The oldest iOS this will run on. 15 rather than the 12 Unity
        /// defaults to: Metal 3 features, a modern Swift runtime for the
        /// toolchain, and — the practical reason — Cesium's native is built
        /// against a recent SDK and the devices below it have neither the
        /// memory nor the bandwidth to stream 3D terrain.
        /// </summary>
        public const string MinimumOsVersion = "15.0";

        [MenuItem("Tools/Iron Meridian/iOS/Apply Player Settings", priority = 70)]
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "IvanSostarko";
            PlayerSettings.productName = GameConfig.GameName;
            PlayerSettings.bundleVersion = GameConfig.Version;
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.iOS, BundleIdentifier);

            // ARM64 with IL2CPP, which on iOS is not a choice — it is the only
            // combination Apple accepts and the only one Unity offers.
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(UnityEditor.Build.NamedBuildTarget.iOS, 1);   // ARM64

            PlayerSettings.iOS.targetOSVersionString = MinimumOsVersion;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;

            // Metal only. There is nothing else on iOS any more, and leaving a
            // GLES entry in the list is a fallback to something that was removed
            // from the OS.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Metal
            });

            // A map is read across, and every panel here is laid out against a
            // landscape canvas. Both landscapes, because a phone held either way
            // up is the same phone.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // The map is the whole screen. A status bar over it is a strip of
            // terrain the player can neither see nor touch — and the safe area
            // (UI.SafeAreaCanvas) is what keeps the chrome out from under the
            // notch that replaces it.
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.useAnimatedAutorotation = true;

            // Signing is left alone on purpose. A team id belongs to whoever is
            // shipping and does not belong in a repository; Xcode's automatic
            // signing picks it up from the machine that opens the project.
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Iron Meridian] iOS player settings applied " +
                      $"({BundleIdentifier}, iOS {MinimumOsVersion}+, ARM64/IL2CPP/Metal).");
        }

        /// <summary>
        /// The batch entry point, called by <c>scripts/build-ios.ps1</c>.
        ///
        ///   <c>-ironmeridian-output &lt;dir&gt;</c>   where to put the Xcode project
        ///   <c>-ironmeridian-development</c>       development build
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string output = Arg("-ironmeridian-output");
            bool development = Flag("-ironmeridian-development");

            if (string.IsNullOrEmpty(output))
                output = Path.Combine("Builds", "iOS");

            Build(output, development);
        }

        public static void Build(string outputDir, bool development)
        {
            ApplySettings();
            Directory.CreateDirectory(Path.GetFullPath(outputDir));

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            if (scenes.Count == 0)
                throw new Exception(
                    "No scenes in the build. Run Tools > Iron Meridian > Setup Project first.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputDir,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"iOS export {summary.result}: " +
                                    $"{summary.totalErrors} error(s). See the log.");

            Debug.Log($"[Iron Meridian] Xcode project written to {outputDir} " +
                      $"({summary.totalTime.TotalMinutes:0.#} min). " +
                      "Open Unity-iPhone.xcodeproj on a Mac to build and sign it.");
        }

        [MenuItem("Tools/Iron Meridian/iOS/Export Xcode Project", priority = 71)]
        static void BuildMenu() => Build(Path.Combine("Builds", "iOS"), development: false);

        // ------------------------------------------------------------- plist

        /// <summary>
        /// Writes the Info.plist keys Unity leaves to you.
        ///
        /// Done as a post-process rather than by hand because the Xcode project
        /// is **regenerated on every export** — anything edited in Xcode is
        /// gone the next time somebody runs the build, and the failure shows up
        /// weeks later as a rejected submission.
        ///
        /// Runs on Windows too: the plist is written by Unity's own exporter,
        /// not by Xcode, so it is correct in the project a Mac is handed.
        /// </summary>
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[Iron Meridian] No Info.plist at {plistPath} — " +
                                 "the iOS keys were not written. See docs/43-IOS.md §6.");
                return;
            }

            // UNITY_IOS, not UNITY_EDITOR: UnityEditor.iOS.Xcode lives in the
            // iOS module's own assembly, and a machine that has not installed
            // that module would fail to compile this file at all. The define is
            // set whenever the active build target is iOS, which is the only
            // time this method has anything to do.
#if UNITY_IOS
            try
            {
                var plist = new UnityEditor.iOS.Xcode.PlistDocument();
                plist.ReadFromFile(plistPath);
                var root = plist.root;

                // Answers Apple's export-compliance question once, in the build,
                // instead of a human clicking "no" on every single upload. False
                // is the truth here: the game uses HTTPS and nothing else, which
                // is the exemption everybody qualifies for.
                root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

                // The map fills the screen and is dragged with a finger. Split
                // View would hand a third of it to another app and leave the
                // rail overlapping the ground it is supposed to sit beside.
                root.SetBoolean("UIRequiresFullScreen", true);

                // The home indicator is a white bar over the bottom of the map.
                // Dimming it is the most a game is allowed to do; the safe area
                // (UI.SafeAreaCanvas) keeps the chrome above it either way.
                root.SetBoolean("UIViewControllerBasedStatusBarAppearance", false);
                root.SetString("UIStatusBarStyle", "UIStatusBarStyleLightContent");

                // Why the app wants the network, in the words Apple shows the
                // user. Terrain is streamed; there is no offline mode.
                root.SetString("NSLocalNetworkUsageDescription",
                    "Iron Meridian streams 3D terrain from Cesium ion.");

                plist.WriteToFile(plistPath);
                Debug.Log("[Iron Meridian] Info.plist keys written " +
                          "(export compliance, full screen, status bar).");
            }
            catch (Exception e)
            {
                // Never fail the export for this. The project is still openable
                // and the keys can be set in Xcode; a build that stopped here
                // would be a worse outcome than one that needs a note.
                Debug.LogError($"[Iron Meridian] Could not write Info.plist keys: {e.Message}\n" +
                               "Set them in Xcode by hand — docs/43-IOS.md §6.");
            }
#endif
        }

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
