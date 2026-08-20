using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.EditorTools
{
    /// <summary>
    /// WebGL player settings and the batch build behind
    /// <c>scripts/build-web.ps1</c>.
    ///
    /// Kept apart from <see cref="ProjectBootstrap"/> for the same reason
    /// <see cref="AndroidBuild"/> is: setup is about the project, this is about
    /// one platform's own settings.
    ///
    /// **What actually makes this port possible** is not in this file. It is
    /// <see cref="Core.StreamingAssetsFile"/>'s preload — a browser has one
    /// thread, so the synchronous read that works on Android would hang the tab
    /// permanently — and <see cref="Core.WebStorage"/>, because a save that is
    /// not flushed to IndexedDB is gone when the tab closes. See docs/41-WEB.md.
    /// </summary>
    public static class WebBuild
    {
        /// <summary>
        /// **Brotli**, not gzip and not none.
        ///
        /// This build is large — a Cesium native, a full IL2CPP runtime and the
        /// game — and it is fetched over a network before anything happens, so
        /// transfer size is the loading screen. Brotli beats gzip by a useful
        /// margin on WASM. The cost is that the server must send
        /// <c>Content-Encoding: br</c>; a host that cannot be configured to do
        /// that wants <see cref="WebGLCompressionFormat.Disabled"/> and a decompression
        /// fallback instead — see docs/41-WEB.md §5.
        /// </summary>
        public const WebGLCompressionFormat Compression = WebGLCompressionFormat.Brotli;

        [MenuItem("Tools/Iron Meridian/Web/Apply Player Settings", priority = 50)]
        public static void ApplySettings()
        {
            PlayerSettings.companyName = "IvanSostarko";
            PlayerSettings.productName = GameConfig.GameName;
            PlayerSettings.bundleVersion = GameConfig.Version;

            var web = UnityEditor.Build.NamedBuildTarget.WebGL;

            // IL2CPP is the only backend WebGL has; naming it means the setting
            // is not silently whatever the project was last left on.
            PlayerSettings.SetScriptingBackend(web, ScriptingImplementation.IL2CPP);

            // **Size over speed.** The whole build is downloaded before the
            // first frame, so a smaller binary is a shorter wait for every
            // player, every time — and this game is not CPU-bound in the
            // browser, it is bound by how fast tiles arrive.
            PlayerSettings.SetIl2CppCompilerConfiguration(web, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.High);

            PlayerSettings.WebGL.compressionFormat = Compression;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.decompressionFallback = false;

            // Explicitly-thrown only: full exception support costs both size and
            // speed, and this game throws nothing it relies on catching. A
            // development build turns it back up — see Build().
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            // WebGL 2 is WebGL's floor for what Cesium's shaders need, and every
            // browser that can run a build this size has had it for years.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            });

            // Terrain tiles are the memory here and Cesium budgets them itself;
            // a browser tab that asks for a gigabyte up front fails to start on
            // machines that would otherwise have run it.
            PlayerSettings.WebGL.memorySize = 512;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

            // The map fills the page and is dragged with the mouse; a browser
            // that shows the tab's own scrollbars over it is one more thing
            // between the player and the terrain.
            PlayerSettings.runInBackground = false;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Iron Meridian] WebGL player settings applied " +
                      $"({Compression} compression, {PlayerSettings.WebGL.memorySize} MB heap).");
        }

        /// <summary>
        /// The batch entry point, called by <c>scripts/build-web.ps1</c>.
        ///
        /// Reads its arguments off the command line because
        /// <c>-executeMethod</c> can only call a no-arg static:
        ///
        ///   <c>-ironmeridian-output &lt;dir&gt;</c>   where to write the build
        ///   <c>-ironmeridian-development</c>       development build, full exceptions
        ///   <c>-ironmeridian-uncompressed</c>      no Brotli, for a dumb static host
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string output = Arg("-ironmeridian-output");
            bool development = Flag("-ironmeridian-development");
            bool uncompressed = Flag("-ironmeridian-uncompressed");

            if (string.IsNullOrEmpty(output))
                output = Path.Combine("Builds", "Web");

            Build(output, development, uncompressed);
        }

        public static void Build(string outputDir, bool development, bool uncompressed)
        {
            ApplySettings();

            if (uncompressed)
            {
                // A host that will not send Content-Encoding gets files it can
                // serve as they are. Bigger over the wire, but it *works*, which
                // a build the browser cannot decompress does not.
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.decompressionFallback = false;
                Debug.Log("[Iron Meridian] Building uncompressed — larger download, no server config.");
            }

            if (development)
            {
                // A stack trace with no symbols is a stack trace nobody can act
                // on, and the whole point of a development build is the report.
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
                PlayerSettings.SetIl2CppCompilerConfiguration(
                    UnityEditor.Build.NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Debug);
            }

            Directory.CreateDirectory(Path.GetFullPath(outputDir));

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            if (scenes.Count == 0)
                throw new Exception(
                    "No scenes in the build. Run Tools > Iron Meridian > Setup Project first — " +
                    "it is also what writes StreamingAssets/Maps/index.json, which the WebGL " +
                    "preload reads to know which scenarios to fetch.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"WebGL build {summary.result}: " +
                                    $"{summary.totalErrors} error(s). See the log.");

            Debug.Log($"[Iron Meridian] WebGL build ok — {outputDir} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalMinutes:0.#} min).");
        }

        [MenuItem("Tools/Iron Meridian/Web/Build", priority = 51)]
        static void BuildMenu() =>
            Build(Path.Combine("Builds", "Web"), development: false, uncompressed: false);

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
