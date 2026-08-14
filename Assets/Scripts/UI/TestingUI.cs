using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// The DEVELOPMENT hub — the way in to the map editor and to the three
    /// reference labs that let the game's own data be inspected without playing
    /// a scenario to find it.
    ///
    ///   MAP EDITOR       -> the main game screen (Lyon dev map)
    ///   UNITS AND WEAPONS-> every unit type and weapon system, editable
    ///   PARTICLE EFFECTS -> every VfxId, shown in 3D with its sound
    ///   AUDIO            -> every music, ambience and effect sound, with transport
    ///   MAP EAST FRANCE  -> placeholder page ("Under development")
    ///
    /// **Why a grid rather than a row.** It carried three fixed cards at
    /// ±580 px, which is a layout that only works for exactly three and only at
    /// 16:9 — a fourth would have run off the edge of the screen. The entries
    /// now flow in a wrapping grid sized from the window, so adding a lab is a
    /// line of code rather than a re-layout.
    ///
    /// The scene is still called "Testing" (<see cref="GameConfig.SceneTesting"/>).
    /// Renaming the scene asset would break every build-settings entry and every
    /// <c>LoadScene</c> call until somebody re-ran Setup Project, and the scene
    /// name is not something a player ever sees.
    /// </summary>
    public class TestingUI : MonoBehaviour
    {
        const float CardW = 440f, CardH = 260f, CardGap = 24f;
        /// <summary>Top of the card grid, from the top of the screen.</summary>
        const float GridTop = -220f;
        const float SideMargin = 80f, BottomMargin = 48f;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("DevelopmentCanvas");

            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default);

            var title = UIFactory.CreateText(canvas.transform, "DEVELOPMENT", 56,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(700, 80));

            var sub = UIFactory.CreateText(canvas.transform,
                "Author scenarios, and inspect the data the game is built from.",
                20, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(80, -126), new Vector2(900, 28));

            UIFactory.CreateBackButton(canvas.transform, "BACK TO MAIN MENU",
                () => SceneManager.LoadScene(GameConfig.SceneMainMenu),
                new Vector2(1f, 1f), new Vector2(-80, -62), new Vector2(300, 62));

            var grid = BuildGrid(canvas.transform);

            Card(grid, UiIcons.Layers, "MAP EDITOR",
                "The main game screen. Cesium 3D terrain, deploy units, draw control " +
                "measures, author missions and fight.",
                new Color(0.13f, 0.24f, 0.38f),
                () => SceneManager.LoadScene(GameConfig.SceneGame));

            Card(grid, UiIcons.Shield, "UNITS AND WEAPONS",
                "Every unit type, artillery nature, airframe, UAV, missile system and " +
                "naval gun — with every value editable and saved to your own copy.",
                new Color(0.20f, 0.22f, 0.14f),
                () => SceneManager.LoadScene(GameConfig.SceneUnitsList));

            Card(grid, UiIcons.Flame, "PARTICLE EFFECTS",
                "Every effect in the catalogue, played in 3D with its own sound — the " +
                "authored prefab where there is one, the procedural stand-in where there is not.",
                new Color(0.32f, 0.18f, 0.10f),
                () => SceneManager.LoadScene(GameConfig.SceneEffectsList));

            Card(grid, UiIcons.Pulse, "AUDIO",
                "Every music bed, weather ambience and effect sound, with its name, its " +
                "resource path and a transport to play it.",
                new Color(0.14f, 0.24f, 0.26f),
                () => SceneManager.LoadScene(GameConfig.SceneAudioList));

            Card(grid, UiIcons.Pin, "MAP EAST FRANCE",
                "Eastern France scenario map. Operational theatre from Lyon to the Rhine.",
                new Color(0.28f, 0.16f, 0.13f),
                () => SceneManager.LoadScene(GameConfig.SceneEastFrance));
        }

        /// <summary>
        /// The wrapping card grid. A <see cref="GridLayoutGroup"/> rather than
        /// hand-placed cards: the number of entries is what changes here, and a
        /// layout that has to be re-measured every time one is added is a layout
        /// that will eventually be wrong.
        /// </summary>
        RectTransform BuildGrid(Transform parent)
        {
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform content, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(SideMargin, BottomMargin);
            rt.offsetMax = new Vector2(-SideMargin, GridTop);

            // CreateScrollView fits a vertical stack; this content is a grid, so
            // its layout group is swapped rather than configured.
            Destroy(content.GetComponent<VerticalLayoutGroup>());
            var layout = content.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CardW, CardH);
            layout.spacing = new Vector2(CardGap, CardGap);
            layout.padding = new RectOffset(0, 0, 0, 16);
            layout.constraint = GridLayoutGroup.Constraint.Flexible;
            return content;
        }

        /// <summary>
        /// One entry. Same three devices as the main menu's rows — bordered
        /// surface, accent strip, glyph — so the two screens read as one
        /// interface rather than as two generations of it.
        /// </summary>
        void Card(Transform parent, Sprite glyph, string title, string body, Color tone,
            UnityEngine.Events.UnityAction onClick)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Card_" + title, UiTheme.Surface, UiTheme.Border);

            var btn = UIFactory.CreateButton(frame, "", onClick, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            // The head band carries the card's own tone, which is what tells the
            // five entries apart at a glance before any of them is read.
            var head = UIFactory.CreatePanel(frame, "Head", tone);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1);
            head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -84);
            head.offsetMax = Vector2.zero;
            head.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(head, glyph, "Glyph");
            icon.color = GameConfig.UiAccent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(34, 34));

            var t = UIFactory.CreateText(head, title, 26, GameConfig.UiText,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(72, 0),
                new Vector2(CardW - 96f, 34));
            UIFactory.Fit(t, 15);

            var b = UIFactory.CreateText(frame, body, 17, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(b.rectTransform, 24f, 104f, CardW - 48f, CardH - 140f);
            UIFactory.Fit(b, 12);

            var strip = UIFactory.CreatePanel(frame, "Strip", GameConfig.UiAccent);
            strip.anchorMin = Vector2.zero; strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = new Vector2(0, 6);
            strip.GetComponent<Image>().raycastTarget = false;

            var fill = frame.Find("Fill").GetComponent<Image>();
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddHover(trigger, EventTriggerType.PointerEnter, () => Paint(fill, strip, true));
            AddHover(trigger, EventTriggerType.PointerExit, () => Paint(fill, strip, false));
            Paint(fill, strip, false);
        }

        static void Paint(Image fill, RectTransform strip, bool hover)
        {
            fill.color = hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            strip.offsetMax = new Vector2(0, hover ? 10f : 6f);
        }

        static void AddHover(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneMainMenu);
        }
    }
}
