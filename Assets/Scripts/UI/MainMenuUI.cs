using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// The main menu, laid out as a **command board down the left-hand edge**
    /// rather than as a stack of buttons across the middle.
    ///
    /// Three things drove the reorganisation:
    ///
    /// • **The artwork was being covered by the thing that needed it least.** A
    ///   centred column of six identical bars sat over the middle of a
    ///   full-bleed background, which is exactly where a photograph has its
    ///   subject. Moving the interface to one side leaves the picture visible
    ///   and gives the menu a place to grow into.
    ///
    /// • **Six unlabelled words are not a menu, they are a list.** "EXTRAS" and
    ///   "TESTING" told a returning player nothing and a new one less. Every
    ///   entry now carries a one-line description of what is behind it, which is
    ///   the difference between a menu you read and a menu you guess at.
    ///
    /// • **The board scrolls.** The entries live in a scroll view rather than at
    ///   fixed offsets, so the list can outgrow the window instead of running
    ///   off the bottom of a short one — which is what a fixed column does the
    ///   moment a seventh entry is added or the game is played at 1280×720.
    ///
    /// The OPERATIONS / REFERENCE / SYSTEM group headings and the descriptive
    /// blurb under the masthead are **gone**. Each entry already states what it
    /// is on its own second line, so the headings were captions over captions,
    /// and between them the two devices cost roughly 150 px of the very
    /// vertical space the entries needed. QUIT keeps its place at the bottom of
    /// the list — position, not a heading, is what keeps it clear of the cursor's
    /// resting place.
    ///
    /// Built entirely at runtime like every other screen (golden rule 2), from
    /// <see cref="UIFactory"/> and the <see cref="UiTheme"/> palette.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        /// <summary>Left inset of the command board, and the width it occupies.</summary>
        const float BoardX = 96f, BoardWidth = 560f;
        /// <summary>Width of the darkened panel behind the board — wider than the board so it reads as a field, not a box.</summary>
        const float ScrimWidth = BoardX * 2f + BoardWidth;
        /// <summary>Y of the emblem row, measured from the top of the screen.</summary>
        const float TitleY = -96f;
        /// <summary>
        /// Y the scrolling list starts at, measured from the top of the screen.
        /// Higher than it used to be: the blurb that sat between the masthead
        /// rule and the first entry is gone, and the list took the space.
        /// </summary>
        const float MenuTop = -212f;
        /// <summary>Clear space under the list, above the footer rule and version line.</summary>
        const float MenuBottom = 100f;

        const float EntryHeight = 72f, EntryGap = 6f;
        /// <summary>Accent strip down an entry's left edge, at rest and under the cursor.</summary>
        const float StripRest = 4f, StripHover = 8f;

        GameObject _quitModal;

        /// <summary>
        /// The parts of one menu row that change under the cursor. Held together
        /// so the hover paint is one call rather than five scattered lookups.
        /// </summary>
        class Entry
        {
            public Image Fill;
            public RectTransform Strip;
            public Image Glyph;
            public Text Label;
            public Text Detail;
        }

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("MainMenuCanvas");

            // A light scrim overall: the board carries its own darker field, so
            // the artwork on the right stays as bright as it was authored.
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default, 0.42f);

            BuildBoardField(canvas.transform);
            BuildMasthead(canvas.transform);
            BuildMenu(canvas.transform);
            BuildFooter(canvas.transform);
            BuildQuitModal(canvas.transform);
        }

        // ---------------------------------------------------------- masthead

        /// <summary>
        /// The darkened field the board stands on. Anchored to the left edge and
        /// stretched full height so it reads as part of the frame rather than as
        /// a floating card, and so the entries have guaranteed contrast whatever
        /// the background image happens to be doing behind them.
        /// </summary>
        void BuildBoardField(Transform parent)
        {
            var field = UIFactory.CreatePanel(parent, "BoardField", new Color(0.02f, 0.03f, 0.05f, 0.80f));
            field.anchorMin = new Vector2(0, 0);
            field.anchorMax = new Vector2(0, 1);
            field.pivot = new Vector2(0, 0.5f);
            field.sizeDelta = new Vector2(ScrimWidth, 0);
            field.GetComponent<Image>().raycastTarget = false;

            // Hairline down its inboard edge, where the board meets the artwork.
            var edge = UIFactory.CreatePanel(field, "Edge", UiTheme.BorderStrong);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;
        }

        void BuildMasthead(Transform parent)
        {
            var emblem = UIFactory.CreateImage(parent, UiIcons.Shield, "Emblem");
            emblem.color = UiTheme.Accent;
            emblem.raycastTarget = false;
            UIFactory.Place((RectTransform)emblem.transform, new Vector2(0f, 1f),
                new Vector2(BoardX, TitleY), new Vector2(44, 44));

            var title = UIFactory.CreateText(parent, GameConfig.GameName.ToUpperInvariant(),
                64, UiTheme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f),
                new Vector2(BoardX + 60f, TitleY + 6f), new Vector2(BoardWidth - 60f, 64));

            var subtitle = UIFactory.CreateText(parent, "REAL-TERRAIN OPERATIONAL WARGAME",
                18, UiTheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(subtitle.rectTransform, new Vector2(0f, 1f),
                new Vector2(BoardX + 62f, TitleY - 34f), new Vector2(BoardWidth, 26));

            // Rule under the masthead: accent for the first stretch, then a
            // hairline running out to the edge of the board. The two-part rule
            // is the same device the section headers below use, so the whole
            // column reads as one system.
            var accentRule = UIFactory.CreatePanel(parent, "AccentRule", UiTheme.Accent);
            UIFactory.Place(accentRule, new Vector2(0f, 1f),
                new Vector2(BoardX, TitleY - 86f), new Vector2(84, 3));
            accentRule.GetComponent<Image>().raycastTarget = false;

            var rule = UIFactory.CreatePanel(parent, "Rule", UiTheme.Border);
            UIFactory.Place(rule, new Vector2(0f, 1f),
                new Vector2(BoardX + 90f, TitleY - 86f), new Vector2(BoardWidth - 90f, 1));
            rule.GetComponent<Image>().raycastTarget = false;
        }

        // -------------------------------------------------------------- menu

        /// <summary>
        /// The entries, in a scroll view spanning the board between the masthead
        /// and the footer.
        ///
        /// The order is the argument the groups used to make, and it makes it
        /// without spending a row: play first, because it is what most people
        /// opened the program to do; reference next; system last, so QUIT sits
        /// at the far end of the list from where the cursor comes to rest.
        /// </summary>
        void BuildMenu(Transform parent)
        {
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform content,
                withScrollbar: true);
            // The board field behind it already carries the darkening; the
            // scroll view's own default wash would double it up into a slab.
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(BoardX, MenuBottom);
            rt.offsetMax = new Vector2(BoardX + BoardWidth, MenuTop);

            // The shared scroll defaults are tuned for the map editor's small
            // cards; these rows are 72 px tall and carry their own left inset,
            // so they want no padding of their own.
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = EntryGap;
            layout.padding = new RectOffset(0, 0, 0, 12);

            MenuEntry(content, UiIcons.Flag, "SINGLE PLAYER",
                "Fight a scenario against the enemy commander",
                () => SceneManager.LoadScene(GameConfig.SceneSinglePlayer));
            MenuEntry(content, UiIcons.Person, "MULTIPLAYER",
                "Take one side against another commander",
                () => SceneManager.LoadScene(GameConfig.SceneMultiplayer));
            MenuEntry(content, UiIcons.Layers, "TESTING",
                "The map editor, the unit catalogue and the development scenarios",
                () => SceneManager.LoadScene(GameConfig.SceneTesting));
            MenuEntry(content, UiIcons.Chart, "EXTRAS",
                "Background material, credits and reference reading",
                () => SceneManager.LoadScene(GameConfig.SceneExtras));
            MenuEntry(content, UiIcons.Gear, "SETTINGS",
                "Display, audio and map data",
                () => SceneManager.LoadScene(GameConfig.SceneSettings));
            MenuEntry(content, UiIcons.Close, "QUIT",
                "Leave Iron Meridian", ShowQuitModal, danger: true);
        }

        void MenuEntry(Transform parent, Sprite glyph, string label, string detail,
            UnityEngine.Events.UnityAction action, bool danger = false)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Entry_" + label,
                UiTheme.Surface, UiTheme.Border);
            // Width is driven by the layout group; only the height is ours.
            frame.sizeDelta = new Vector2(0, EntryHeight);

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            // CreateButton always makes a caption; this row draws its own two
            // lines instead, so the one it made is switched off rather than
            // left to render an empty string over the top of them.
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            Color accent = danger ? UiTheme.Hostile : UiTheme.Accent;

            var strip = UIFactory.CreatePanel(frame, "Strip", accent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.sizeDelta = new Vector2(StripRest, 0);
            strip.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.color = accent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(30, 0), new Vector2(24, 24));

            // Placed by hand rather than through CreateStackedLabels: that
            // helper is built for the compact 12/11 px pairs the map panels use
            // and fits its text into 16 px rows, which would shrink a 24 px
            // menu label straight back down to panel size.
            const float TextX = 72f;
            // The row is as wide as the board less the scrollbar the list now
            // carries, so the text column stops clear of both.
            float textWidth = BoardWidth - UIFactory.ScrollbarWidth - TextX - 24f;

            var title = UIFactory.CreateText(frame, label, 24, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, TextX, 13f, textWidth, 28f);
            UIFactory.Fit(title, 16);

            var sub = UIFactory.CreateText(frame, detail, 15, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(sub.rectTransform, TextX, 41f, textWidth, 20f);
            UIFactory.Fit(sub, 11);

            var entry = new Entry
            {
                Fill = frame.Find("Fill").GetComponent<Image>(),
                Strip = strip,
                Glyph = icon,
                Label = title,
                Detail = sub
            };
            // Hover is painted by hand rather than through Button's own colour
            // tint: the tint multiplies the whole row including the glyph and
            // the strip, which washes the accent out instead of lifting it.
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.PointerEnter, () => Paint(entry, accent, true));
            AddEvent(trigger, EventTriggerType.PointerExit, () => Paint(entry, accent, false));
            Paint(entry, accent, false);
        }

        static void Paint(Entry e, Color accent, bool hover)
        {
            e.Fill.color = hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            e.Strip.sizeDelta = new Vector2(hover ? StripHover : StripRest, 0);
            e.Glyph.color = hover ? Color.white : accent;
            e.Label.color = hover ? Color.white : UiTheme.Text;
            e.Detail.color = hover ? UiTheme.Text : UiTheme.TextDim;
        }

        static void AddEvent(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        // ------------------------------------------------------------ footer

        void BuildFooter(Transform parent)
        {
            var rule = UIFactory.CreatePanel(parent, "FooterRule", UiTheme.Border);
            UIFactory.Place(rule, new Vector2(0f, 0f), new Vector2(BoardX, 76), new Vector2(BoardWidth, 1));
            rule.GetComponent<Image>().raycastTarget = false;

            var version = UIFactory.CreateText(parent,
                $"{GameConfig.GameName}  ·  {GameConfig.Version}", 15, UiTheme.TextDim,
                TextAnchor.LowerLeft);
            UIFactory.Place(version.rectTransform, new Vector2(0f, 0f),
                new Vector2(BoardX, 44), new Vector2(BoardWidth, 24));

            var hint = UIFactory.CreateText(parent, "Esc — quit", 14, UiTheme.TextFaint,
                TextAnchor.LowerLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f),
                new Vector2(BoardX, 22), new Vector2(BoardWidth, 22));
        }

        // ------------------------------------------------------------- modal

        void BuildQuitModal(Transform canvas)
        {
            var overlay = UIFactory.CreatePanel(canvas, "QuitModal", new Color(0, 0, 0, 0.78f));
            UIFactory.Stretch(overlay);
            overlay.gameObject.AddComponent<Button>()  // click outside = cancel
                .onClick.AddListener(HideQuitModal);

            var box = UIFactory.CreateBorderedPanel(overlay, "Box", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 268));

            // Swallows clicks that land on the box so they do not reach the
            // dismiss handler on the overlay behind it.
            box.gameObject.AddComponent<Button>();

            var txt = UIFactory.CreateText(box, "QUIT IRON MERIDIAN?", 32, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(560, 50));

            var sub = UIFactory.CreateText(box, "Any unsaved map changes will be lost.",
                18, UiTheme.TextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(560, 34));

            var yes = UIFactory.CreateButton(box, "QUIT", QuitGame, UiTheme.Danger, Color.white, 22);
            UIFactory.Place((RectTransform)yes.transform, new Vector2(0.5f, 0f),
                new Vector2(-118, 44), new Vector2(210, 60));

            var no = UIFactory.CreateButton(box, "CANCEL", HideQuitModal, UiTheme.Surface, UiTheme.Text, 22);
            UIFactory.Place((RectTransform)no.transform, new Vector2(0.5f, 0f),
                new Vector2(118, 44), new Vector2(210, 60));

            _quitModal = overlay.gameObject;
            _quitModal.SetActive(false);
        }

        void ShowQuitModal() => _quitModal.SetActive(true);
        void HideQuitModal() => _quitModal.SetActive(false);

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_quitModal != null && _quitModal.activeSelf) HideQuitModal();
                else ShowQuitModal();
            }
        }
    }
}
