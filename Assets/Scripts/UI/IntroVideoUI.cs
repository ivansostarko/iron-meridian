using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace IronMeridian.UI
{
    /// <summary>
    /// The game's own opening film, played once over a black screen after
    /// Unity's splash and before the main menu is usable.
    ///
    /// **It always ends.** Three separate things dismiss it — the clip reaching
    /// its end, the player pressing anything at all, and a hard timeout — and
    /// the caller's continuation runs exactly once whichever gets there first.
    /// That is the same rule the loading screens are held to (golden rule 7),
    /// and for the same reason: an intro that could trap the player behind it
    /// is worse than no intro. A missing video file is not an error either — the
    /// menu simply comes up, having warned once.
    ///
    /// **Any input skips it.** Not a labelled button: an opening film is a thing
    /// people sit through once and skip forever after, and hunting for the way
    /// out is exactly the wrong first impression. The prompt in the corner says
    /// so, faintly, rather than making the player guess.
    ///
    /// **Once per process, not once per scene.** <see cref="_played"/> is
    /// static, so backing out of a screen to the main menu does not replay the
    /// film. It is deliberately *not* a <see cref="PlayerPrefs"/> entry: the
    /// intro should run on every launch, as it does in every other game that
    /// has one.
    ///
    /// Its canvas sorts above everything, including the loading overlay's 500.
    /// </summary>
    public class IntroVideoUI : MonoBehaviour
    {
        /// <summary>Resources path of the film, without extension.</summary>
        const string ClipPath = "Videos/intro-video/game_intro";

        /// <summary>Above every other canvas the game builds, the loader included.</summary>
        const int SortingOrder = 1000;

        /// <summary>
        /// Backstop. A video that never reports its end — an unsupported codec,
        /// a stalled decoder — must not hold the menu behind it, and two minutes
        /// is longer than any opening film this game will have.
        /// </summary>
        const float TimeoutSeconds = 120f;

        /// <summary>Seconds of black before the film starts, so it does not cut in mid-fade.</summary>
        const float LeadInSeconds = 0.25f;

        static bool _played;

        /// <summary>
        /// True while an intro is on screen. The main menu holds its music back
        /// until then — the film has its own sound, and a menu bed under it
        /// would be two scores at once.
        /// </summary>
        public static bool Showing { get; private set; }

        System.Action _onFinished;
        VideoPlayer _video;
        RenderTexture _target;
        GameObject _root;
        float _elapsed;
        bool _finished;
        bool _started;

        /// <summary>
        /// Plays the film if it has not been played yet, then calls
        /// <paramref name="onFinished"/>. Calls it immediately — before
        /// returning — when there is nothing to play, so the caller never has to
        /// handle "was there an intro?" itself.
        /// </summary>
        public static void PlayOnce(System.Action onFinished)
        {
            if (_played) { onFinished?.Invoke(); return; }
            _played = true;

            var clip = Resources.Load<VideoClip>(ClipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[IntroVideoUI] Missing video: Resources/{ClipPath}. " +
                    "Video files must live under an Assets/Resources folder.");
                onFinished?.Invoke();
                return;
            }

            var host = new GameObject("IntroVideo");
            DontDestroyOnLoad(host);
            var intro = host.AddComponent<IntroVideoUI>();
            intro._onFinished = onFinished;
            intro.Build(clip);
        }

        void Build(VideoClip clip)
        {
            Showing = true;

            var canvas = UIFactory.CreateCanvas("IntroVideoCanvas");
            canvas.sortingOrder = SortingOrder;
            _root = canvas.gameObject;
            DontDestroyOnLoad(_root);

            // Opaque black behind the picture: the film is letterboxed to keep
            // its aspect, and whatever is behind the bars must not be the menu
            // half-built.
            var backdrop = UIFactory.CreatePanel(canvas.transform, "Backdrop", Color.black);
            UIFactory.Stretch(backdrop);
            backdrop.GetComponent<Image>().raycastTarget = true;   // swallow clicks meant for the menu

            _target = new RenderTexture((int)clip.width, (int)clip.height, 0)
            {
                name = "IntroVideo"
            };

            var picture = UIFactory.CreateRawImage(canvas.transform, "Picture");
            picture.texture = _target;
            picture.raycastTarget = false;
            var rt = picture.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            UIFactory.Stretch(rt);

            // Fit inside the screen rather than envelope it: a background image
            // may be cropped, a film may not — the frame is composed.
            var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = clip.height > 0 ? clip.width / (float)clip.height : 1.777f;

            var hint = UIFactory.CreateText(canvas.transform, "Press any key to skip",
                18, new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-48, 36),
                new Vector2(400, 26));

            _video = gameObject.AddComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.clip = clip;
            _video.renderMode = VideoRenderMode.RenderTexture;
            _video.targetTexture = _target;
            _video.isLooping = false;
            _video.skipOnDrop = true;

            // Through an AudioSource rather than Direct, so the film's own sound
            // obeys the master volume like everything else does.
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _video.SetTargetAudioSource(0, source);

            _video.loopPointReached += _ => Finish();
            _video.errorReceived += (_, message) =>
            {
                Debug.LogWarning($"[IntroVideoUI] {message}");
                Finish();
            };

            _video.Prepare();
        }

        void Update()
        {
            if (_finished) return;

            _elapsed += Time.unscaledDeltaTime;

            // Start as soon as the decoder is ready, after a beat of black.
            if (!_started && _elapsed >= LeadInSeconds && _video != null && _video.isPrepared)
            {
                _started = true;
                _video.Play();
            }

            // Any key, any mouse button. `anyKeyDown` covers the keyboard and
            // the mouse both; the explicit Escape test is for the pad and for
            // platforms where Escape is handled apart from the key stream.
            if (Input.anyKeyDown || Input.GetKeyDown(KeyCode.Escape)) { Finish(); return; }

            if (_elapsed > TimeoutSeconds) Finish();
        }

        /// <summary>
        /// Takes the film down and hands control back. Guarded, because three
        /// paths lead here and the continuation must run exactly once.
        /// </summary>
        void Finish()
        {
            if (_finished) return;
            _finished = true;
            Showing = false;

            if (_video != null) _video.Stop();
            if (_root != null) Destroy(_root);

            var callback = _onFinished;
            _onFinished = null;
            callback?.Invoke();

            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Showing = false;
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
                _target = null;
            }
        }
    }
}
