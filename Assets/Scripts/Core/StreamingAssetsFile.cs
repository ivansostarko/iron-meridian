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
    /// works. On **Android it is not a folder** — it is a compressed entry
    /// inside the APK, addressed by a <c>jar:file://…!/assets/…</c> URL that
    /// <c>System.IO</c> knows nothing about. Every <c>File.Exists</c> there
    /// returns false and every read throws, which means the unit catalogue, the
    /// formation names, the mission list, the shipped maps and the Cesium ion
    /// token all silently fail to load: the game boots to an empty globe with no
    /// units in it and nothing in the log that says why.
    ///
    /// So every read of a shipped data file goes through here, and the platform
    /// difference is answered once instead of in six places.
    ///
    /// **The Android read is synchronous, on purpose.** <c>UnityWebRequest</c> is
    /// the only API that can open a jar entry, and it is asynchronous; the
    /// loaders that need these files are not, and are called lazily from
    /// wherever a unit definition is first asked for. Making them async would
    /// mean an await at every call site and an ordering guarantee across sixteen
    /// scenes, to save a wait on a **local decompression** that takes a few
    /// milliseconds. The spin is bounded by <see cref="TimeoutSeconds"/> so a
    /// pathological case reports rather than hangs.
    ///
    /// **Writes never come here.** StreamingAssets is read-only in a build on
    /// every platform; anything the player changes belongs in
    /// <c>Application.persistentDataPath</c>, which is a real directory on
    /// Android too. See docs/40-ANDROID.md.
    /// </summary>
    public static class StreamingAssetsFile
    {
        /// <summary>
        /// How long a single read may take before it is given up on. Generous:
        /// this is a decompression from local storage, not a network fetch, and
        /// a device under load at first launch is the case it has to survive.
        /// </summary>
        const float TimeoutSeconds = 10f;

        /// <summary>
        /// True where StreamingAssets is a real directory the file APIs can see.
        ///
        /// Read off the path rather than off the platform enum: what actually
        /// decides this is whether the path is a URL, and that is the same
        /// question for any future platform that packs its assets into an
        /// archive.
        /// </summary>
        public static bool IsDirectory => !Application.streamingAssetsPath.Contains("://");

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
        /// On a packed platform the only way to know is to open it, so this is a
        /// read whose contents are thrown away. Callers that are about to read
        /// it anyway should use <see cref="ReadAllText"/> and test for null
        /// instead — that is why every one in this project does.
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
            string path = PathFor(relativePath);

            if (IsDirectory)
            {
                try { return File.Exists(path) ? File.ReadAllText(path) : null; }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[StreamingAssets] Could not read {relativePath}: {e.Message}");
                    return null;
                }
            }

            try
            {
                using (var request = UnityWebRequest.Get(path))
                {
                    request.SendWebRequest();

                    // Spinning on isDone is safe for a jar/file URL: Unity
                    // services those on a worker thread and does not need the
                    // main loop to come back round for the operation to finish.
                    // It would not be safe for an http one, and nothing here
                    // reads over the network.
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
    }
}
