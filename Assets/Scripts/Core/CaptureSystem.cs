using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Stills and recordings, written to the player's own Pictures folder.
    ///
    /// A <see cref="UnityEngine.Object.DontDestroyOnLoad"/> singleton for the
    /// same reason <see cref="Audio.MusicManager"/> is one: a recording has to
    /// survive the screen it was started from, and a per-scene component would
    /// end the take on the first navigation.
    ///
    /// **Recording writes a numbered frame sequence, not a video file.** Unity
    /// ships no runtime video encoder — Unity Recorder is an editor tool and
    /// cannot run in a build — so an .mp4 here would mean bundling a native
    /// encoder. A frame sequence is what a video editor wants as input anyway,
    /// and it is what Recorder itself produces.
    /// </summary>
    public class CaptureSystem : MonoBehaviour
    {
        /// <summary>Frames a second the sequence is captured at, and plays back at.</summary>
        public const int RecordFps = 30;

        /// <summary>
        /// Stop after this many frames — about ten minutes at
        /// <see cref="RecordFps"/>. A recording nobody remembered to stop
        /// should not quietly fill the disk.
        /// </summary>
        public const int MaxFrames = 18000;

        static CaptureSystem _active;

        public static bool Recording { get; private set; }

        /// <summary>Frames written in the current or last take.</summary>
        public static int FrameCount { get; private set; }

        /// <summary>Where the last still or take went, for the panel to show.</summary>
        public static string LastOutput { get; private set; } = "";

        /// <summary>Raised when recording starts, stops, or another second is in the can.</summary>
        public static event Action Changed;

        string _takeDir;
        int _lastReportedSecond;

        // --------------------------------------------------------- the folder

        /// <summary>
        /// <c>Pictures/Iron Meridian</c>. Falls back to the save folder on a
        /// machine that has no Pictures folder — losing the shot entirely is
        /// worse than putting it somewhere less obvious.
        /// </summary>
        public static string OutputRoot
        {
            get
            {
                string pictures = "";
                try { pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); }
                catch { /* the fallback below covers it */ }

                if (string.IsNullOrEmpty(pictures) || !Directory.Exists(pictures))
                    pictures = Application.persistentDataPath;

                return Path.Combine(pictures, "Iron Meridian");
            }
        }

        public static string ScreenshotDir => Path.Combine(OutputRoot, "Screenshots");
        public static string RecordingDir => Path.Combine(OutputRoot, "Recordings");

        static bool TryPrepare(string dir, out string error)
        {
            error = null;
            try { Directory.CreateDirectory(dir); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }

        static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // ------------------------------------------------------------ stills

        /// <summary>
        /// Writes one PNG. Unity captures at the end of the frame, so the file
        /// appears a moment after this returns — the path is reported straight
        /// away because it is already decided.
        /// </summary>
        public static string TakeScreenshot()
        {
            if (!TryPrepare(ScreenshotDir, out string error))
            {
                Debug.LogError($"[Capture] Cannot write to {ScreenshotDir}: {error}");
                LastOutput = "Could not write to Pictures.";
                Changed?.Invoke();
                return null;
            }

            string path = Path.Combine(ScreenshotDir, $"IronMeridian_{Stamp()}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Capture] Screenshot -> {path}");

            LastOutput = path;
            Changed?.Invoke();
            return path;
        }

        // --------------------------------------------------------- recording

        public static void ToggleRecording()
        {
            if (Recording) Stop(); else Begin();
        }

        static void Begin()
        {
            if (Recording) return;

            string dir = Path.Combine(RecordingDir, Stamp());
            if (!TryPrepare(dir, out string error))
            {
                Debug.LogError($"[Capture] Cannot write to {dir}: {error}");
                LastOutput = "Could not write to Pictures.";
                Changed?.Invoke();
                return;
            }

            var host = Ensure();
            host._takeDir = dir;
            host._lastReportedSecond = -1;
            FrameCount = 0;
            Recording = true;
            LastOutput = dir;

            // The idiomatic Unity answer to "the encoder is slower than the
            // game": time advances in fixed 1/RecordFps steps regardless of how
            // long each frame really took, so the sequence comes out smooth
            // even though the game visibly runs in slow motion while capturing.
            Time.captureFramerate = RecordFps;

            host.StartCoroutine(host.CaptureLoop());
            Debug.Log($"[Capture] Recording -> {dir}");
            Changed?.Invoke();
        }

        static void Stop()
        {
            if (!Recording) return;
            Recording = false;
            Time.captureFramerate = 0;
            Debug.Log($"[Capture] Stopped after {FrameCount} frame(s).");
            Changed?.Invoke();
        }

        IEnumerator CaptureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (Recording)
            {
                // The backbuffer is only readable once everything, UI included,
                // has been drawn for this frame.
                yield return endOfFrame;
                if (!Recording) break;

                byte[] jpg = null;
                Texture2D frame = null;
                try
                {
                    frame = ScreenCapture.CaptureScreenshotAsTexture();
                    // JPG, not PNG: a lossless 1080p frame costs several times
                    // the encode time and the disk of a quality-90 JPG, and the
                    // sequence is an intermediate for a video editor rather
                    // than an archival still.
                    jpg = frame.EncodeToJPG(90);
                }
                catch (Exception e)
                {
                    // Without this the exception would leave the coroutine dead
                    // and Recording still true: a take that had silently
                    // stopped, with a button that still said STOP.
                    Debug.LogError($"[Capture] Frame capture failed: {e.Message}");
                }
                finally
                {
                    if (frame != null) Destroy(frame);
                }

                if (jpg == null) { Stop(); yield break; }

                string path = Path.Combine(_takeDir, $"frame_{FrameCount:000000}.jpg");
                FrameCount++;

                // The write is the one part that does not need the main thread,
                // and it is most of the wall clock.
                byte[] bytes = jpg;
                _ = Task.Run(() =>
                {
                    try { File.WriteAllBytes(path, bytes); }
                    catch (Exception e) { Debug.LogError($"[Capture] Frame write failed: {e.Message}"); }
                });

                int second = FrameCount / RecordFps;
                if (second != _lastReportedSecond)
                {
                    _lastReportedSecond = second;
                    Changed?.Invoke();
                }

                if (FrameCount >= MaxFrames)
                {
                    Debug.LogWarning($"[Capture] Frame cap ({MaxFrames}) reached — stopping.");
                    Stop();
                }
            }
        }

        /// <summary>Length of the current take, in seconds of finished footage.</summary>
        public static float RecordedSeconds => FrameCount / (float)RecordFps;

        // ---------------------------------------------------------- plumbing

        static CaptureSystem Ensure()
        {
            if (_active != null) return _active;
            var go = new GameObject("[Capture]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _active = go.AddComponent<CaptureSystem>();
            return _active;
        }

        /// <summary>Opens the output folder in the desktop's file browser.</summary>
        public static void OpenFolder()
        {
            if (!TryPrepare(OutputRoot, out string error))
            {
                Debug.LogError($"[Capture] Cannot open {OutputRoot}: {error}");
                return;
            }
            Application.OpenURL("file:///" + OutputRoot.Replace("\\", "/"));
        }

        void OnDisable()
        {
            // Leaving captureFramerate set would keep the whole game running on
            // a fixed clock long after the take ended.
            if (Recording) Stop();
        }
    }
}
