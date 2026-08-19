using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace IronMeridian.Core
{
    /// <summary>
    /// Stills and video, written to the player's own Pictures folder.
    ///
    /// A <see cref="UnityEngine.Object.DontDestroyOnLoad"/> singleton for the
    /// same reason <see cref="Audio.MusicManager"/> is one: a recording has to
    /// survive the screen it was started from, and a per-scene component would
    /// end the take on the first navigation.
    ///
    /// **Video is encoded by ffmpeg, run as a child process.** Unity has no
    /// runtime video encoder — <c>MediaEncoder</c> and Unity Recorder are both
    /// editor-only — so the choice is a native plugin or an external encoder.
    /// ffmpeg is the external encoder every other tool in this space uses, it
    /// needs no plugin in the project, and it writes a real .mp4.
    ///
    /// Frames go over the pipe as JPEG rather than raw RGBA: 1080p raw is about
    /// 8 MB a frame and 250 MB/s at 30 fps, which is a lot of pipe for a
    /// difference no one can see once x264 has finished with it.
    /// </summary>
    public class CaptureSystem : MonoBehaviour
    {
        /// <summary>Frames a second the video is captured at, and plays back at.</summary>
        public const int RecordFps = 30;

        /// <summary>
        /// Stop after this many frames — about ten minutes at
        /// <see cref="RecordFps"/>. A recording nobody remembered to stop
        /// should not quietly fill the disk.
        /// </summary>
        public const int MaxFrames = 18000;

        /// <summary>
        /// Frames allowed to queue for the encoder. Bounded on purpose: with
        /// <see cref="Time.captureFramerate"/> set the game is already off the
        /// wall clock, so blocking the capture until the encoder catches up
        /// costs nothing but real time and guarantees no frame is ever dropped.
        /// </summary>
        const int QueueDepth = 60;

        static CaptureSystem _active;

        public static bool Recording { get; private set; }

        /// <summary>Frames written in the current or last take.</summary>
        public static int FrameCount { get; private set; }

        /// <summary>Where the last still or take went, for the panel to show.</summary>
        public static string LastOutput { get; private set; } = "";

        /// <summary>Set when a take could not start, for the panel to show instead.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>Raised when recording starts, stops, or another second is in the can.</summary>
        public static event Action Changed;

        Process _encoder;
        Stream _encoderInput;
        BlockingCollection<byte[]> _queue;
        Thread _writer;
        int _frameWidth, _frameHeight;
        int _lastReportedSecond;
        string _takePath;

        // --------------------------------------------------------- the folder

        /// <summary>
        /// <c>Pictures/Iron Meridian</c>. Falls back to the save folder on a
        /// machine that has no Pictures folder — losing the take entirely is
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

        // ----------------------------------------------------------- the encoder

        static string _ffmpegPath;
        static bool _ffmpegSearched;

        /// <summary>
        /// Where ffmpeg is, or null. Searched once and remembered.
        ///
        /// StreamingAssets first, so a build *can* ship its own copy — see
        /// docs/39-CAPTURE.md §4 for the licensing that decision carries. Then
        /// PATH, then the usual install locations, so a machine that already
        /// has ffmpeg needs nothing done to it.
        /// </summary>
        public static string FfmpegPath
        {
            get
            {
                if (_ffmpegSearched) return _ffmpegPath;
                _ffmpegSearched = true;

                string exe = Application.platform == RuntimePlatform.WindowsPlayer
                          || Application.platform == RuntimePlatform.WindowsEditor
                    ? "ffmpeg.exe" : "ffmpeg";

                var candidates = new System.Collections.Generic.List<string>
                {
                    Path.Combine(Application.streamingAssetsPath, "ffmpeg", exe),
                };

                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    if (!string.IsNullOrWhiteSpace(dir))
                        candidates.Add(Path.Combine(dir.Trim(), exe));
                }

                candidates.Add(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe");
                candidates.Add("/usr/bin/ffmpeg");
                candidates.Add("/usr/local/bin/ffmpeg");

                foreach (var c in candidates)
                {
                    try { if (File.Exists(c)) { _ffmpegPath = c; break; } }
                    catch { /* a malformed PATH entry is not worth failing over */ }
                }

                if (_ffmpegPath == null)
                    Debug.LogWarning("[Capture] ffmpeg not found — video recording is unavailable. See docs/39-CAPTURE.md.");

                return _ffmpegPath;
            }
        }

        /// <summary>Whether a take can be started at all.</summary>
        public static bool CanRecord => FfmpegPath != null;

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
                LastError = "Could not write to Pictures.";
                Changed?.Invoke();
                return null;
            }

            string path = Path.Combine(ScreenshotDir, $"IronMeridian_{Stamp()}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Capture] Screenshot -> {path}");

            LastError = "";
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

            string ffmpeg = FfmpegPath;
            if (ffmpeg == null)
            {
                LastError = "ffmpeg not found — see docs/39-CAPTURE.md";
                Changed?.Invoke();
                return;
            }

            if (!TryPrepare(RecordingDir, out string error))
            {
                Debug.LogError($"[Capture] Cannot write to {RecordingDir}: {error}");
                LastError = "Could not write to Pictures.";
                Changed?.Invoke();
                return;
            }

            var host = Ensure();
            host._takePath = Path.Combine(RecordingDir, $"IronMeridian_{Stamp()}.mp4");
            host._frameWidth = Screen.width;
            host._frameHeight = Screen.height;
            host._lastReportedSecond = -1;

            if (!host.StartEncoder(ffmpeg)) return;

            FrameCount = 0;
            Recording = true;
            LastError = "";
            LastOutput = host._takePath;

            // The idiomatic Unity answer to "the encoder is slower than the
            // game": time advances in fixed 1/RecordFps steps regardless of how
            // long each frame really took, so the video comes out smooth and
            // correctly timed even though the game visibly runs in slow motion
            // while capturing.
            Time.captureFramerate = RecordFps;

            host.StartCoroutine(host.CaptureLoop());
            Debug.Log($"[Capture] Recording -> {host._takePath}");
            Changed?.Invoke();
        }

        bool StartEncoder(string ffmpeg)
        {
            // -f image2pipe          frames arrive on stdin, already JPEG
            // -vf scale=trunc(..)    yuv420p needs even dimensions, and a window
            //                        can be any odd size the player dragged it to
            // -pix_fmt yuv420p       what every player and browser can decode
            // -movflags +faststart   metadata at the front, so it streams
            string args =
                $"-y -f image2pipe -framerate {RecordFps} -i - " +
                "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" " +
                "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart " +
                $"\"{_takePath}\"";

            try
            {
                _encoder = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpeg, args)
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = false,
                };

                _encoder.Start();
                _encoderInput = _encoder.StandardInput.BaseStream;

                // ffmpeg is chatty on stderr and will block once the pipe fills
                // if nobody drains it — which would stall the encode entirely.
                _encoder.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && e.Data.Contains("Error"))
                        Debug.LogWarning($"[Capture] ffmpeg: {e.Data}");
                };
                _encoder.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Capture] Could not start ffmpeg: {e.Message}");
                LastError = "Could not start ffmpeg.";
                Changed?.Invoke();
                return false;
            }

            _queue = new BlockingCollection<byte[]>(QueueDepth);
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "IronMeridian.Capture" };
            _writer.Start();
            return true;
        }

        /// <summary>Feeds queued frames to the encoder, off the main thread.</summary>
        void WriteLoop()
        {
            try
            {
                foreach (var frame in _queue.GetConsumingEnumerable())
                    _encoderInput.Write(frame, 0, frame.Length);
            }
            catch (Exception e)
            {
                // A broken pipe means ffmpeg died; the coroutine notices next frame.
                Debug.LogError($"[Capture] Encoder pipe closed: {e.Message}");
            }
        }

        static void Stop()
        {
            if (!Recording) return;
            Recording = false;
            Time.captureFramerate = 0;
            _active?.FinishEncoder();
            Debug.Log($"[Capture] Stopped after {FrameCount} frame(s) -> {LastOutput}");
            Changed?.Invoke();
        }

        void FinishEncoder()
        {
            try
            {
                // Order matters: stop accepting frames, let the writer drain,
                // then close stdin so ffmpeg knows the stream ended and writes
                // its trailer. Killing it here would leave an unplayable file.
                _queue?.CompleteAdding();
                _writer?.Join(5000);
                _encoderInput?.Flush();
                _encoderInput?.Dispose();

                if (_encoder != null && !_encoder.WaitForExit(15000))
                {
                    Debug.LogWarning("[Capture] ffmpeg did not finish in time — the file may be truncated.");
                    try { _encoder.Kill(); } catch { }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Capture] Finishing the encode failed: {e.Message}");
            }
            finally
            {
                _encoder?.Dispose();
                _encoder = null;
                _encoderInput = null;
                _queue?.Dispose();
                _queue = null;
                _writer = null;
            }
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

                if (_encoder == null || _encoder.HasExited)
                {
                    Debug.LogError("[Capture] The encoder exited — stopping.");
                    LastError = "ffmpeg stopped unexpectedly.";
                    Stop();
                    yield break;
                }

                // A window resized mid-take would change the frame size, which
                // image2pipe cannot follow. Ending the take keeps the file
                // playable instead of corrupting it from that frame on.
                if (Screen.width != _frameWidth || Screen.height != _frameHeight)
                {
                    Debug.LogWarning("[Capture] Window resized during a take — stopping.");
                    LastError = "Window resized — take ended.";
                    Stop();
                    yield break;
                }

                byte[] jpg = null;
                Texture2D frame = null;
                try
                {
                    frame = ScreenCapture.CaptureScreenshotAsTexture();
                    jpg = frame.EncodeToJPG(95);
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

                // Blocks when the encoder is behind, which is the point.
                try { _queue.Add(jpg); }
                catch (Exception) { Stop(); yield break; }

                FrameCount++;

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

        /// <summary>Length of the current take, in seconds of finished video.</summary>
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

        void OnApplicationQuit()
        {
            // Quitting mid-take must still close the stream cleanly, or the
            // .mp4 has no trailer and nothing will play it.
            if (Recording) Stop();
        }

        void OnDisable()
        {
            // Leaving captureFramerate set would keep the whole game running on
            // a fixed clock long after the take ended.
            if (Recording) Stop();
        }
    }
}
