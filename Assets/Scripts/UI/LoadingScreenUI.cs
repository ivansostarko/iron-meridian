using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// Full-screen loading overlay: background artwork, a progress bar and a
    /// status line, shown while a screen gets itself ready.
    ///
    /// It builds its **own canvas** at a high sorting order rather than joining
    /// the screen's canvas. That way it can be shown before the screen's UI
    /// exists and still draw on top of whatever is built afterwards — uGUI
    /// otherwise orders by hierarchy, and a loader created first would end up
    /// behind everything it is supposed to cover.
    ///
    /// The overlay always goes away. Progress that stalls, a tileset that never
    /// finishes, a missing Cesium token — every path ends in
    /// <see cref="Dismiss"/>, because a loader that can trap the player is worse
    /// than no loader at all.
    /// </summary>
    public class LoadingScreenUI : MonoBehaviour
    {
        /// <summary>Above every screen canvas, which all use the default order of 0.</summary>
        public const int SortingOrder = 500;

        /// <summary>Give up waiting and let the player in, however far loading got.</summary>
        public const float DefaultTimeoutSeconds = 30f;

        /// <summary>Held on screen at least this long, so a warm cache does not make it flash.</summary>
        const float MinimumVisibleSeconds = 0.8f;
        const float FadeSeconds = 0.45f;

        /// <summary>Bar units per second — a smoothed bar reads as progress, a snapping one as a glitch.</summary>
        const float BarEaseRate = 0.9f;

        CanvasGroup _group;
        RectTransform _barFill;

        /// <summary>The wordmark shown above the title — see docs/11-GAME-MENU.md §2.2.</summary>
        const string LogoPath = "Graphics/Logo/game-logo";
        /// <summary>Width the logo is fitted into; its height follows the artwork's aspect.</summary>
        const float LogoWidth = 620f;
        Text _percentText, _statusText;

        Func<float> _progress;
        Func<bool> _isComplete;
        float _shownAt, _timeoutSeconds = DefaultTimeoutSeconds;
        float _displayed;          // eased, monotonic bar position
        bool _dismissing;

        /// <summary>
        /// Builds and shows the overlay immediately.
        ///
        /// <paramref name="title"/> is dropped when it only repeats the
        /// wordmark — passing <see cref="GameConfig.GameName"/>, as the map
        /// loader does, leaves the logo to say it. A title that names something
        /// the logo does not, such as the mission being loaded, is drawn.
        /// </summary>
        public static LoadingScreenUI Show(string title, string subtitle)
        {
            var canvas = UIFactory.CreateCanvas("LoadingCanvas");
            canvas.sortingOrder = SortingOrder;

            var loader = canvas.gameObject.AddComponent<LoadingScreenUI>();
            loader.Build(canvas, title, subtitle);
            return loader;
        }

        void Build(Canvas canvas, string title, string subtitle)
        {
            _shownAt = Time.unscaledTime;

            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;   // swallow clicks meant for the screen underneath

            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default,
                BackgroundCatalog.LoaderScrim);

            // The wordmark, not the game's name set in the UI font. A loader is
            // the first thing a player sees on the way into a mission and the
            // logo is the one piece of the game that is *designed*; setting its
            // name in the interface typeface instead was the screen introducing
            // itself in somebody else's handwriting.
            var logo = UIFactory.LoadSprite(LogoPath);
            if (logo != null)
            {
                var mark = UIFactory.CreateImage(canvas.transform, logo, "Logo");
                mark.raycastTarget = false;
                mark.preserveAspect = true;

                float height = logo.rect.width > 0f
                    ? LogoWidth * (logo.rect.height / logo.rect.width)
                    : LogoWidth * 0.34f;
                UIFactory.Place((RectTransform)mark.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 150), new Vector2(LogoWidth, height));
            }

            // **The game's own name is not repeated under the mark.** The map
            // loader passes GameConfig.GameName as its title, so the screen was
            // showing IRON MERIDIAN in artwork and IRON MERIDIAN in the UI font
            // forty pixels below it — the wordmark saying it once is the whole
            // point of having a wordmark. A title that says something the logo
            // does not, such as the mission being loaded, still earns its line.
            //
            // Without the logo there is nothing else naming the screen, so the
            // title comes back at full size: a loader that identified itself
            // with nothing at all would be worse than one that repeated itself.
            bool titleSaysWhatTheLogoSays = logo != null && !string.IsNullOrEmpty(title) &&
                string.Equals(title.Trim(), GameConfig.GameName,
                    System.StringComparison.OrdinalIgnoreCase);

            if (!titleSaysWhatTheLogoSays && !string.IsNullOrEmpty(title))
            {
                var titleText = UIFactory.CreateText(canvas.transform, title.ToUpperInvariant(),
                    logo != null ? 34 : 84,
                    GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Place(titleText.rectTransform, new Vector2(0.5f, 0.5f),
                    new Vector2(0, logo != null ? 74 : 120), new Vector2(1500, logo != null ? 44 : 110));
            }

            var subtitleText = UIFactory.CreateText(canvas.transform, subtitle, 26,
                GameConfig.UiTextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(subtitleText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0, 50), new Vector2(1200, 40));

            // --- progress bar ---
            var track = UIFactory.CreatePanel(canvas.transform, "ProgressTrack",
                new Color(0f, 0f, 0f, 0.55f));
            UIFactory.Place(track, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(900, 14));
            track.GetComponent<Image>().raycastTarget = false;

            _barFill = UIFactory.CreatePanel(track, "ProgressFill", GameConfig.UiAccent);
            // Left-anchored and driven by anchorMax.x, so the fill scales with
            // the track at any resolution instead of needing a pixel width.
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            _barFill.GetComponent<Image>().raycastTarget = false;

            // The figure and the status line are both **centred under the bar**,
            // stacked rather than pinned to its two ends.
            //
            // Splayed to the corners they were 900 px apart: reading "STREAMING
            // TERRAIN" on the left and then "42%" on the right is two glances
            // across the width of the screen for one piece of information — what
            // is happening and how far along it is. Centred, they are one block
            // under the line they describe, on the axis the logo and the bar
            // already sit on, and the eye never leaves the middle of the screen.
            _percentText = UIFactory.CreateText(canvas.transform, "0%", 22,
                GameConfig.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_percentText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0, -92), new Vector2(400, 30));

            _statusText = UIFactory.CreateText(canvas.transform, "Preparing…", 20,
                GameConfig.UiTextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_statusText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0, -128), new Vector2(1000, 28));

            ApplyBar(0f);
        }

        /// <summary>
        /// Drives the bar from a live progress source and dismisses when
        /// <paramref name="isComplete"/> says so — or when the timeout expires,
        /// whichever comes first.
        /// </summary>
        public void Track(Func<float> progress01, Func<bool> isComplete,
            float timeoutSeconds = DefaultTimeoutSeconds)
        {
            _progress = progress01;
            _isComplete = isComplete;
            _timeoutSeconds = timeoutSeconds;
        }

        public void SetStatus(string status)
        {
            if (_statusText != null) _statusText.text = status;
        }

        void Update()
        {
            if (_dismissing) return;

            if (_progress != null)
            {
                // Never run the bar backwards. Cesium's estimate drops when the
                // camera moves and new tiles are needed; a bar that retreats
                // reads as a fault rather than as honest reporting.
                float target = Mathf.Clamp01(_progress());
                _displayed = Mathf.Max(_displayed,
                    Mathf.MoveTowards(_displayed, target, BarEaseRate * Time.unscaledDeltaTime));
                ApplyBar(_displayed);
            }

            float elapsed = Time.unscaledTime - _shownAt;
            if (elapsed < MinimumVisibleSeconds) return;

            if (_isComplete != null && _isComplete()) { Dismiss(); return; }
            if (elapsed >= _timeoutSeconds)
                Dismiss("Terrain is still streaming — entering the map.");
        }

        void ApplyBar(float value01)
        {
            if (_barFill != null) _barFill.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
            if (_percentText != null) _percentText.text = $"{Mathf.RoundToInt(value01 * 100f)}%";
        }

        /// <summary>Fades the overlay out and destroys it. Safe to call more than once.</summary>
        public void Dismiss(string finalStatus = null)
        {
            if (_dismissing) return;
            _dismissing = true;

            if (finalStatus != null) SetStatus(finalStatus);
            // Show the bar full on the way out; stopping at 87% looks like a failure.
            ApplyBar(1f);

            _group.blocksRaycasts = false;
            StartCoroutine(FadeOutRoutine());
        }

        IEnumerator FadeOutRoutine()
        {
            // Unscaled: the pause menu zeroes timeScale, and a loader that
            // freezes half-faded would be a trap.
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
