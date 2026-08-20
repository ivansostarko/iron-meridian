using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace IronMeridian.Core
{
    /// <summary>
    /// Reads a file out of <c>StreamingAssets</c> on any platform this game
    /// ships to.
    ///
    /// **Why this exists at all.** On Windows, macOS and Linux, StreamingAssets
    /// is a folder beside the player and <see cref="File.ReadAllText(string)"/>
    /// works. On **Android it is a compressed entry inside the APK**, addressed
    /// by a <c>jar:file://…!/assets/…</c> URL, and on **WebGL it is a path on
    /// the web server** the build is hosted from. <c>System.IO</c> knows nothing
    /// about either: every <c>File.Exists</c> returns false and every read
    /// throws, which means the unit catalogue, the formation names, the mission
    /// list, the shipped maps and the Cesium ion token all silently fail to
    /// load. The game boots to an empty globe with no units in it and nothing in
    /// the log that says why.
    ///
    /// So every read of a shipped data file goes through here, and the platform
    /// difference is answered once instead of in six places.
    ///
    /// **Three platforms, two strategies:**
    ///
    /// | Platform | Path is | Strategy |
    /// |---|---|---|
    /// | Desktop, editor | a directory | <c>File.ReadAllText</c>, unchanged |
    /// | Android | a jar URL | <c>UnityWebRequest</c>, read synchronously |
    /// | WebGL | an http URL | <c>UnityWebRequest</c>, **preloaded** — see below |
    ///
    /// **Android may block; WebGL must not.** <c>UnityWebRequest</c> is
    /// asynchronous and the loaders that need these files are not. On Android
    /// that is survivable: Unity services a <c>jar:</c> URL on a worker thread,
    /// so spinning on <c>isDone</c> finishes without the main loop coming back
    /// round, and the wait is a local decompression of a few milliseconds.
    ///
    /// On WebGL the same spin **hangs the browser tab, permanently**. There is
    /// one thread; the fetch cannot progress unless the frame returns to the
    /// JavaScript event loop, and a busy-wait never returns. So WebGL does not
    /// read on demand at all: <see cref="PreloadRoutine"/> fetches every shipped
    /// file up front, <see cref="ReadAllText"/> serves the cache, and
    /// <see cref="Ready"/> is what the loading screen waits on
    /// (<c>SceneLoader</c>). A read that arrives before the preload finishes is
    /// reported as an error rather than answered — silently returning null there
    /// would look exactly like a missing file.
    ///
    /// **Writes never come here.** StreamingAssets is read-only in a build on
    /// every platform; anything the player changes belongs in
    /// <c>Application.persistentDataPath</c> — a real directory on Android, and
    /// a browser-backed one on WebGL (<see cref="WebStorage"/>).
    ///
    /// See docs/40-ANDROID.md and docs/41-WEB.md.
    /// </summary>
    public static class StreamingAssetsFile
    {
        /// <summary>
        /// How long a single **Android** read may take before it is given up on.
        /// Generous: this is a decompression from local storage, not a network
        /// fetch, and a device under load at first launch is the case it has to
        /// survive. WebGL never uses it — it never blocks.
        /// </summary>
        const float TimeoutSeconds = 10f;

        /// <summary>
        /// Everything a build ships that some part of the game reads. The WebGL
        /// preload walks this list; every other platform ignores it.
        ///
        /// Kept here, next to the reader, rather than collected from the five
        /// classes that read them. A registry those classes had to add
        /// themselves to at startup would put the whole thing at the mercy of
        /// static-constructor order, and the file that was missed would be one
        /// that works everywhere except in the browser.
        /// </summary>
        public static readonly string[] CoreFiles =
        {
            "cesium-token.txt",
            "Data/units.json",
            "Data/unit-names.json",
            "Data/missions.json",
            "Data/credits.json",
            MapIndexFile
        };

        /// <summary>The index of shipped scenarios — see <c>SaveSystem.ShippedMaps</c>.</summary>
        public const string MapIndexFile = "Maps/index.json";

        /// <summary>
        /// Just enough of the index's schema to know which maps to fetch.
        ///
        /// Deliberately a duplicate of the one in <c>SaveSystem</c>. The
        /// alternative is Core reaching up into Save, which is backwards, and
        /// four lines of schema is a cheaper price than that inversion.
        /// </summary>
        [System.Serializable]
        class MapIndex { public List<string> maps = new List<string>(); }

        static Dictionary<string, string> _cache;

        /// <summary>
        /// True where StreamingAssets is a real directory the file APIs can see.
        ///
        /// Read off the path rather than off the platform enum: what actually
        /// decides this is whether the path is a URL, and that is the same
        /// question for any future platform that packs or hosts its assets.
        /// </summary>
        public static bool IsDirectory => !Application.streamingAssetsPath.Contains("://");

        /// <summary>
        /// True where a read cannot be waited on inline — the browser, where the
        /// one thread that would do the waiting is also the one that has to
        /// service the fetch.
        /// </summary>
        public static bool RequiresPreload =>
            !IsDirectory && Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>
        /// Whether shipped data can be read right now. Always true except on
        /// WebGL before <see cref="PreloadRoutine"/> has run.
        /// </summary>
        public static bool Ready => !RequiresPreload || _cache != null;

        /// <summary>How far the preload has got, 0..1 — the loading bar reads this.</summary>
        public static float PreloadProgress { get; private set; }

        /// <summary>The full path or URL of a file under StreamingAssets.</summary>
        public static string PathFor(string relativePath) =>
            IsDirectory
                ? Path.Combine(Application.streamingAssetsPath, relativePath)
                // Never Path.Combine on a URL: it inserts a backslash on Windows
                // and normalises away the "jar:file://" scheme's double slash.
                : Application.streamingAssetsPath + "/" + relativePath.Replace('\\', '/');

        /// <summary>
        /// Whether a shipped file is there.
        ///
        /// On a packed or hosted platform the only way to know is to open it, so
        /// this is a read whose contents are thrown away. Callers that are about
        /// to read it anyway should use <see cref="ReadAllText"/> and test for
        /// null instead — that is why every one in this project does.
        /// </summary>
        public static bool Exists(string relativePath) =>
            IsDirectory ? File.Exists(PathFor(relativePath))
                        : ReadAllText(relativePath) != null;

        /// <summary>
        /// The file's contents, or <c>null</c> if it is missing or unreadable.
        ///
        /// Null rather than an exception because every caller here has a
        /// fallback — a built-in catalogue, an empty mission book, a token from
        /// the constant — and a missing optional file is not an error worth
        /// unwinding the load for.
        /// </summary>
        public static string ReadAllText(string relativePath)
        {
            if (_cache != null && _cache.TryGetValue(relativePath, out string cached))
                return cached;

            if (IsDirectory)
            {
                string path = PathFor(relativePath);
                try { return File.Exists(path) ? File.ReadAllText(path) : null; }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[StreamingAssets] Could not read {relativePath}: {e.Message}");
                    return null;
                }
            }

            if (RequiresPreload)
            {
                // Deliberately loud. On WebGL this is not "the file is missing",
                // it is "something asked for shipped data before the preload
                // finished, or asked for a file the preload does not know
                // about" — and both are bugs in this file's own list rather
                // than in the build.
                Debug.LogError(
                    $"[StreamingAssets] {relativePath} was not preloaded. On WebGL every shipped " +
                    "file must be in CoreFiles or the map index, and nothing may read one before " +
                    "StreamingAssetsFile.Ready. See docs/41-WEB.md.");
                return null;
            }

            return ReadBlocking(relativePath);
        }

        /// <summary>
        /// A synchronous fetch. **Android only** — see the class summary for why
        /// this is safe there and fatal in a browser.
        /// </summary>
        static string ReadBlocking(string relativePath)
        {
            string path = PathFor(relativePath);
            try
            {
                using (var request = UnityWebRequest.Get(path))
                {
                    request.SendWebRequest();

                    float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                    while (!request.isDone)
                    {
                        if (Time.realtimeSinceStartup <= deadline) continue;
                        Debug.LogError($"[StreamingAssets] Timed out reading {relativePath}.");
                        return null;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        // Not an error by itself: an absent optional file
                        // reports as a failed request on this path, and the
                        // callers all have a fallback for that.
                        Debug.Log($"[StreamingAssets] {relativePath} not available ({request.error}).");
                        return null;
                    }

                    return request.downloadHandler.text;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[StreamingAssets] Could not read {relativePath}: {e}");
                return null;
            }
        }

        // --------------------------------------------------------- preload

        /// <summary>
        /// Fetches every shipped file into memory. A no-op anywhere it is not
        /// needed, so a caller can always yield on it.
        ///
        /// Two passes, because the second depends on the first: the core files
        /// include the map index, and the index names the scenarios. A build
        /// with no index gets a warning rather than a stall — the game is
        /// playable without a shipped scenario, and hanging the loading screen
        /// over a missing optional file is the failure golden rule 7 exists to
        /// prevent.
        ///
        /// A file that fails is cached as an empty string rather than skipped,
        /// so a later read reports "missing" through the normal path instead of
        /// the "was not preloaded" error, which would be the wrong diagnosis.
        /// </summary>
        public static IEnumerator PreloadRoutine()
        {
            if (!RequiresPreload || _cache != null)
            {
                PreloadProgress = 1f;
                yield break;
            }

            var cache = new Dictionary<string, string>();
            var queue = new List<string>(CoreFiles);
            int done = 0;

            // The count grows when the index arrives, so the bar is driven off a
            // running total rather than off the initial list.
            for (int i = 0; i < queue.Count; i++)
            {
                string relative = queue[i];
                yield return FetchInto(cache, relative);
                done++;
                PreloadProgress = Mathf.Clamp01(done / (float)Mathf.Max(1, queue.Count));

                if (relative != MapIndexFile) continue;

                // The index is in hand: add the scenarios it names.
                string json = cache[relative];
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning("[StreamingAssets] No map index in this build — no shipped " +
                                     "scenario can be opened. Run Tools > Iron Meridian > Setup Project.");
                    continue;
                }

                MapIndex index = null;
                try { index = JsonUtility.FromJson<MapIndex>(json); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[StreamingAssets] {MapIndexFile} is malformed: {e.Message}");
                }

                if (index?.maps == null) continue;
                foreach (var map in index.maps)
                    if (!string.IsNullOrEmpty(map)) queue.Add("Maps/" + map);
            }

            _cache = cache;
            PreloadProgress = 1f;
            Debug.Log($"[StreamingAssets] Preloaded {cache.Count} shipped file(s).");
        }

        /// <summary>
        /// Starts the preload before the first scene is built.
        ///
        /// It has to begin here rather than in a scene, because the earliest
        /// screen is also the one the player is looking at while it runs: the
        /// fetch wants every second of the main menu it can get, and the wait
        /// that would otherwise be visible is spent behind a screen that is
        /// doing something else anyway.
        ///
        /// Nothing waits on it *here*, though. <c>SceneLoader</c> does, because
        /// every screen that reads shipped data is entered through it — see
        /// docs/41-WEB.md.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeginPreload()
        {
            if (!RequiresPreload) return;

            // DontDestroyOnLoad and nothing else: the coroutine has to outlive
            // the first scene, and HideAndDontSave on top of that is two
            // overlapping claims on the same lifetime for the sake of tidying
            // one object out of a hierarchy nobody is looking at.
            var go = new GameObject("ShippedDataPreload");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PreloadRunner>().StartCoroutine(RunPreload(go));
        }

        static IEnumerator RunPreload(GameObject owner)
        {
            yield return PreloadRoutine();
            Object.Destroy(owner);
        }

        /// <summary>A coroutine host, and nothing else. A static class cannot run one.</summary>
        class PreloadRunner : MonoBehaviour { }

        static IEnumerator FetchInto(Dictionary<string, string> cache, string relativePath)
        {
            using (var request = UnityWebRequest.Get(PathFor(relativePath)))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    cache[relativePath] = request.downloadHandler.text;
                }
                else
                {
                    // Empty, not absent — see PreloadRoutine's summary.
                    cache[relativePath] = "";
                    Debug.LogWarning($"[StreamingAssets] {relativePath} could not be fetched " +
                                     $"({request.error}).");
                }
            }
        }
    }
}
