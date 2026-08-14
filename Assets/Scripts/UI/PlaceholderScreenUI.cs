using UnityEngine;
using UnityEngine.SceneManagement;
using IronMeridian.Audio;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// A screen that exists but is not built yet: title, a plain statement that
    /// it is under development, a line about what it will be, and a way back.
    ///
    /// One base rather than one class per page, because the pages differ only in
    /// their words, their artwork and their music. Each concrete screen is a few
    /// lines at the bottom of this file — which is also what
    /// <c>ProjectBootstrap</c> needs, since it builds a scene by naming the
    /// component to drop into it.
    ///
    /// **Every placeholder still gets a background and a track**, through the
    /// normal catalogue route (golden rules 8 and 9). Neither has its own file
    /// yet, so both fall back to the shared menu artwork and bed — see the
    /// `fallback` fields in <see cref="BackgroundCatalog"/> and
    /// <see cref="AudioCatalog"/>. Dropping the real files in at the paths those
    /// rows name is the whole of the work; no code changes.
    /// </summary>
    public abstract class PlaceholderScreenUI : MonoBehaviour
    {
        /// <summary>Heading, e.g. "SINGLE PLAYER".</summary>
        protected abstract string Title { get; }
        /// <summary>One line on what this screen will eventually be.</summary>
        protected abstract string Promise { get; }
        /// <summary>Artwork for this screen. Falls back to the shared image until it has its own.</summary>
        protected abstract BackgroundId Background { get; }
        /// <summary>Music for this screen. Falls back to the menu theme until it has its own.</summary>
        protected abstract MusicTrack Track { get; }

        /// <summary>Where BACK goes. The main menu for everything reached from it.</summary>
        protected virtual string BackScene => GameConfig.SceneMainMenu;
        protected virtual string BackLabel => "< BACK TO MAIN MENU";

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(Track);

            var canvas = UIFactory.CreateCanvas(GetType().Name + "Canvas");
            UIFactory.CreateScreenBackground(canvas.transform, Background);

            var title = UIFactory.CreateText(canvas.transform, Title, 48,
                GameConfig.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -120), new Vector2(1200, 80));

            var msg = UIFactory.CreateText(canvas.transform, "UNDER DEVELOPMENT", 72,
                GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(msg.rectTransform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1400, 120));

            var sub = UIFactory.CreateText(canvas.transform, Promise, 26, GameConfig.UiTextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0, -90), new Vector2(1200, 60));

            var back = UIFactory.CreateButton(canvas.transform, BackLabel,
                GoBack, GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0.5f, 0f),
                new Vector2(0, 90), new Vector2(380, 70));
        }

        void GoBack() => SceneManager.LoadScene(BackScene);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }
    }

    /// <summary>Single-player campaign. Bootstrapped into the SinglePlayer scene.</summary>
    public class SinglePlayerUI : PlaceholderScreenUI
    {
        protected override string Title => "SINGLE PLAYER";
        protected override string Promise =>
            "Campaign and skirmish against the computer will be available in a future build. " +
            "The map editor under TESTING is playable now.";
        protected override BackgroundId Background => BackgroundId.SinglePlayer;
        protected override MusicTrack Track => MusicTrack.SinglePlayerTheme;
    }

    /// <summary>Multiplayer lobby. Bootstrapped into the Multiplayer scene.</summary>
    public class MultiplayerUI : PlaceholderScreenUI
    {
        protected override string Title => "MULTIPLAYER";
        protected override string Promise =>
            "Head-to-head and co-operative play over a network will be available in a future build.";
        protected override BackgroundId Background => BackgroundId.Multiplayer;
        protected override MusicTrack Track => MusicTrack.MultiplayerTheme;
    }

    /// <summary>Extras. Bootstrapped into the Extras scene.</summary>
    public class ExtrasUI : PlaceholderScreenUI
    {
        protected override string Title => "EXTRAS";
        protected override string Promise =>
            "Encyclopaedia, replays and scenario tools will be available in a future build. " +
            "The unit reference under TESTING is browsable now.";
        protected override BackgroundId Background => BackgroundId.Extras;
        protected override MusicTrack Track => MusicTrack.ExtrasTheme;
    }
}
