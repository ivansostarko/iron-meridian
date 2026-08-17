using System.Collections.Generic;
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
    ///   MAP EDITOR      -> the main game screen (Lyon dev map)
    ///   UNITS LIST      -> every unit type and weapon system, editable
    ///   PARTICLES       -> every VfxId, shown in 3D with its sound
    ///   AUDIO           -> every music, ambience and effect sound, with transport
    ///
    /// **MAP EAST FRANCE is gone.** It was a card leading to a page that said
    /// "under development" — a menu entry whose whole content was the news that
    /// it had no content. Scenario maps are chosen inside the map editor, which
    /// is where a second one will appear when there is one. The scene and its
    /// placeholder script still exist and are still in Build Settings; nothing
    /// links to them.
    ///
    /// **The grid is placed by hand, not by a layout group.** It was briefly a
    /// <see cref="GridLayoutGroup"/> swapped into a scroll view's content, which
    /// does not work: <c>LayoutGroup</c> is <c>[DisallowMultipleComponent]</c>,
    /// and <c>Destroy</c> on the vertical group already there is deferred to end
    /// of frame — so <c>AddComponent&lt;GridLayoutGroup&gt;</c> returned null and
    /// the cards stayed in a vertical stack. Five fixed entries do not need a
    /// layout engine; the rows below are arithmetic, and each row is centred so
    /// a short last row does not hang off to one side.
    ///
    /// The scene is still called "Testing" (<see cref="GameConfig.SceneTesting"/>).
    /// Renaming the scene asset would break every build-settings entry and every
    /// <c>LoadScene</c> call until somebody re-ran Setup Project, and the scene
    /// name is not something a player ever sees.
    /// </summary>
    public class TestingUI : MonoBehaviour
    {
        const float CardW = 430f, CardH = 240f, CardGap = 26f;
        /// <summary>
        /// Cards per row. Four entries read as 3 + 1; the rows centre
        /// themselves, so the odd one sits under the middle of the three rather
        /// than off to one side.
        /// </summary>
        const int Columns = 3;
        /// <summary>Top of the first row, measured from the top of the screen.</summary>
        const float GridTop = 210f;

        /// <summary>One entry, before it is placed.</summary>
        struct Entry
        {
            public Sprite Glyph;
            public string Title;
            public string Body;
            public Color Tone;
            public string Scene;
        }

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

            var entries = new List<Entry>
            {
                new Entry
                {
                    Glyph = UiIcons.Layers, Title = "MAP EDITOR",
                    Body = "The main game screen. Cesium 3D terrain — deploy units, draw control " +
                           "measures, author missions and fight.",
                    Tone = new Color(0.13f, 0.24f, 0.38f), Scene = GameConfig.SceneGame
                },
                new Entry
                {
                    Glyph = UiIcons.Shield, Title = "UNITS LIST",
                    Body = "Every unit type, artillery nature, airframe, UAV, missile system and " +
                           "naval gun — with every value editable and saved to your own copy.",
                    Tone = new Color(0.20f, 0.22f, 0.14f), Scene = GameConfig.SceneUnitsList
                },
                new Entry
                {
                    Glyph = UiIcons.Flame, Title = "PARTICLES",
                    Body = "Every effect in the catalogue, played in 3D with its own sound — the " +
                           "authored prefab where there is one, the procedural stand-in where there is not.",
                    Tone = new Color(0.32f, 0.18f, 0.10f), Scene = GameConfig.SceneEffectsList
                },
                new Entry
                {
                    Glyph = UiIcons.Pulse, Title = "AUDIO",
                    Body = "Every music bed, weather ambience and effect sound, with its name, its " +
                           "resource path and a transport to play it.",
                    Tone = new Color(0.14f, 0.24f, 0.26f), Scene = GameConfig.SceneAudioList
                }
            };

            Place(canvas.transform, entries);
        }

        /// <summary>
        /// Lays the cards out in centred rows. Anchored to the top-centre of the
        /// screen so the block stays put at any window shape — the canvas scaler
        /// matches width and height equally, so a grid pinned to the left edge
        /// would drift as the aspect changed.
        /// </summary>
        void Place(Transform parent, List<Entry> entries)
        {
            int rows = Mathf.CeilToInt(entries.Count / (float)Columns);

            for (int row = 0; row < rows; row++)
            {
                int first = row * Columns;
                int count = Mathf.Min(Columns, entries.Count - first);
                // Centre each row on its own count, so a trailing row of two
                // sits under the middle of the three above rather than to one side.
                float rowWidth = count * CardW + (count - 1) * CardGap;
                float x = -rowWidth * 0.5f + CardW * 0.5f;
                float y = -(GridTop + row * (CardH + CardGap)) - CardH * 0.5f;

                for (int col = 0; col < count; col++)
                {
                    var entry = entries[first + col];
                    var rt = Card(parent, entry);
                    UIFactory.Place(rt, new Vector2(0.5f, 1f),
                        new Vector2(x + col * (CardW + CardGap), y), new Vector2(CardW, CardH));
                    // Place uses the anchor as the pivot; these are positioned
                    // by their centres, so the pivot has to be re-centred after.
                    rt.pivot = new Vector2(0.5f, 0.5f);
                }
            }
        }

        /// <summary>
        /// One entry. Same three devices as the main menu's rows — bordered
        /// surface, accent strip, glyph — so the two screens read as one
        /// interface rather than as two generations of it.
        /// </summary>
        RectTransform Card(Transform parent, Entry entry)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Card_" + entry.Title,
                UiTheme.Surface, UiTheme.Border);

            var btn = UIFactory.CreateButton(frame, "",
                () => SceneManager.LoadScene(entry.Scene), new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            // The head band carries the card's own tone, which is what tells the
            // five entries apart at a glance before any of them is read.
            var head = UIFactory.CreatePanel(frame, "Head", entry.Tone);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1);
            head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -80);
            head.offsetMax = Vector2.zero;
            head.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(head, entry.Glyph, "Glyph");
            icon.color = GameConfig.UiAccent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(32, 32));

            var t = UIFactory.CreateText(head, entry.Title, 26, GameConfig.UiText,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(70, 0),
                new Vector2(CardW - 94f, 34));
            UIFactory.Fit(t, 15);

            var b = UIFactory.CreateText(frame, entry.Body, 17, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(b.rectTransform, 24f, 100f, CardW - 48f, CardH - 132f);
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

            return frame;
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
