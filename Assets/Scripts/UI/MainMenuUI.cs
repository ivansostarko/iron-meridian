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
    ///   "DEVELOPMENT" told a returning player nothing and a new one less. Every
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
    /// Three things carry the finish, and each replaced something assembled at
    /// runtime out of interface parts:
    ///
    /// • **The logo is artwork**, not a procedural shield beside the game's name
    ///   set in the UI font under a two-part rule. See <see cref="BuildMasthead"/>.
    ///
    /// • **The field fades into the map** rather than ending at a hairline. A
    ///   flat panel with an edge reads as a dialog over a photograph; a gradient
    ///   reads as shadow. See <see cref="BuildBoardField"/>.
    ///
    /// • **The rows are flat**, divided by rules rather than boxed one by one.
    ///   Six bordered cards is six frames to cross to read six things; the
    ///   border is what the hover adds. See <see cref="MenuEntry"/>.
    ///
    /// Built entirely at runtime like every other screen (golden rule 2), from
    /// <see cref="UIFactory"/> and the <see cref="UiTheme"/> palette.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        /// <summary>
        /// The board: a column down the left-hand edge carrying the logo, the
        /// entries and the version line. Everything else on the screen is the
        /// artwork behind it.
        /// </summary>
        const float BoardX = 56f, BoardWidth = 500f;
        /// <summary>
        /// The darkened field the board stands on: solid this far, then
        /// <see cref="FadeWidth"/> of gradient — see
        /// <see cref="BuildBoardField"/>. The interface sits on a column of
        /// shadow that dissolves into the map rather than ending at a seam.
        /// </summary>
        const float FieldWidth = BoardX + BoardWidth + 40f;
        const float FadeWidth = 260f;
        /// <summary>Opacity of the field under the interface, and the colour both parts use.</summary>
        const float FieldAlpha = 0.88f;

        static Color FieldColour(float alpha) => new Color(0.012f, 0.020f, 0.031f, alpha);

        /// <summary>The hairline down the board's leading edge, and its inset.</summary>
        const float RuleX = BoardX - 12f;

        /// <summary>Logo block: top-left inset, and the width it is fitted into.</summary>
        const float LogoX = BoardX + 20f, LogoTop = 74f, LogoWidth = 470f;

        /// <summary>Y the list starts at, measured from the top of the screen.</summary>
        const float MenuTop = -292f;
        /// <summary>Clear space under the list, above the version line.</summary>
        const float MenuBottom = 92f;

        /// <summary>
        /// One entry row. Tall enough for a 24 px title over a wrapped line of
        /// detail with air around both — the rows are the screen's one piece of
        /// interface and they are read before they are clicked.
        /// </summary>
        const float EntryHeight = 96f, EntryGap = 2f;
        /// <summary>Accent strip down an entry's leading edge, at rest and under the cursor.</summary>
        const float StripRest = 0f, StripHover = 4f;
        /// <summary>Left inset of a row's glyph, and of the text column beside it.</summary>
        const float GlyphX = 30f, TextX = 92f;

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
            IronMeridian.Core.DisplaySettings.Apply();

            // The opening film, once per launch. The menu is built behind it
            // either way — the video's canvas sorts on top — so nothing here
            // waits on it except the music, which would otherwise play under a
            // film that has a score of its own.
            IntroVideoUI.PlayOnce(() =>
                IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme));

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
        /// The darkened field the board stands on: a column down the left-hand
        /// edge, **faded out along its inboard side** rather than cut off at a
        /// hard seam.
        ///
        /// The seam was the tell. A flat panel with a hairline down its edge
        /// reads as a dialog laid over a photograph; a gradient reads as
        /// shadow, and the artwork appears to run under the interface instead
        /// of stopping behind it. It also lets the field be *darker* where the
        /// text is, which is where contrast is actually needed.
        /// </summary>
        void BuildBoardField(Transform parent)
        {
            // Solid under the interface, fading only *past* it. A gradient that
            // started at the screen's edge would be palest exactly where the
            // second line of each entry is — the field's job is contrast under
            // the text, and the fade's job is the join, so they are two rects.
            var field = UIFactory.CreatePanel(parent, "BoardField", FieldColour(FieldAlpha));
            field.anchorMin = new Vector2(0, 0);
            field.anchorMax = new Vector2(0, 1);
            field.pivot = new Vector2(0, 0.5f);
            field.sizeDelta = new Vector2(FieldWidth, 0);
            field.anchoredPosition = Vector2.zero;
            field.GetComponent<Image>().raycastTarget = false;

            var fade = UIFactory.CreateHorizontalFade(parent, "BoardFade",
                FieldColour(1f), FieldAlpha, 0f);
            fade.anchorMin = new Vector2(0, 0);
            fade.anchorMax = new Vector2(0, 1);
            fade.pivot = new Vector2(0, 0.5f);
            fade.sizeDelta = new Vector2(FadeWidth, 0);
            fade.anchoredPosition = new Vector2(FieldWidth, 0);

            // The one hard line on the screen, down the board's leading edge —
            // a spine for the logo and the list to hang off. It stops short of
            // both ends so it reads as a rule rather than as a screen border.
            var rule = UIFactory.CreatePanel(parent, "Spine", new Color(1f, 1f, 1f, 0.10f));
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(0, 1);
            rule.pivot = new Vector2(0, 0.5f);
            rule.offsetMin = new Vector2(RuleX, 60f);
            rule.offsetMax = new Vector2(RuleX + 1f, -60f);
            rule.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>
        /// The masthead is the logo artwork and nothing else.
        ///
        /// It used to be a procedural shield beside the game's name set in the
        /// UI font, with a two-part rule under it — a wordmark assembled at
        /// runtime out of the parts the interface happened to have. The real
        /// logo carries the name, the emblem and the strapline together, in the
        /// typeface they were drawn in, so all three of those devices go.
        ///
        /// Fitted by width with its aspect preserved: the artwork is 2140×735,
        /// and a logo stretched to a rect is the most obvious sign of a menu
        /// built by somebody who was not looking at it.
        /// </summary>
        void BuildMasthead(Transform parent)
        {
            var sprite = UIFactory.LoadSprite(LogoPath);
            if (sprite == null) return;      // LoadSprite has already warned

            var logo = UIFactory.CreateImage(parent, sprite, "Logo");
            logo.raycastTarget = false;
            logo.preserveAspect = true;

            float height = sprite.rect.width > 0f
                ? LogoWidth * (sprite.rect.height / sprite.rect.width)
                : LogoWidth * 0.34f;
            UIFactory.PlaceTopLeft((RectTransform)logo.transform, LogoX, LogoTop, LogoWidth, height);
        }

        /// <summary>Resources path of the wordmark — see docs/11-GAME-MENU.md §2.2.</summary>
        const string LogoPath = "Graphics/Logo/game-logo";

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
            // The scrollbar shows itself only when the list is actually longer
            // than the board. Six entries fit any window worth supporting, so a
            // permanent bar was a permanent claim that there was more below.
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform content,
                withScrollbar: true, autoHideScrollbar: true);
            // The board field behind it already carries the darkening; the
            // scroll view's own default wash would double it up into a slab.
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(BoardX, MenuBottom);
            rt.offsetMax = new Vector2(BoardX + BoardWidth, MenuTop);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = EntryGap;
            layout.padding = new RectOffset(0, 0, 0, 12);

            MenuEntry(content, UiIcons.Layers, "SINGLE PLAYER",
                "Fight a scenario against the enemy commander",
                () => SceneManager.LoadScene(GameConfig.SceneSinglePlayer));
            MenuEntry(content, UiIcons.Swords, "MULTIPLAYER",
                "Take one side against another commander",
                () => SceneManager.LoadScene(GameConfig.SceneMultiplayer));
            MenuEntry(content, UiIcons.Blueprint, "DEVELOPMENT",
                "The map editor and the reference labs \u2014 units, effects and audio",
                () => SceneManager.LoadScene(GameConfig.SceneTesting));
            MenuEntry(content, UiIcons.Folder, "EXTRAS",
                "Background material, credits and reference reading",
                () => SceneManager.LoadScene(GameConfig.SceneExtras));
            MenuEntry(content, UiIcons.Gear, "SETTINGS",
                "Display, audio and map data",
                () => SceneManager.LoadScene(GameConfig.SceneSettings));
            MenuEntry(content, UiIcons.Exit, "QUIT",
                "Leave Iron Meridian", ShowQuitModal);
        }

        /// <summary>
        /// One entry: a flat row carrying a glyph, a title and a line of detail.
        ///
        /// **Flat, not a card.** The rows used to be hairline-bordered panels \u2014
        /// six boxes stacked with a gap between each, which is six frames the
        /// eye has to cross to read a list of six things. They are now one
        /// column of faint fills separated by a rule, so the list reads as a
        /// single object; the border is what the *hover* adds, along with the
        /// accent strip and a lift in the fill.
        ///
        /// **The glyph column is separated by a rule**, not by whitespace alone.
        /// At 26 px an icon beside a 25 px title competes with it; behind a rule
        /// it reads as an index down the side of the list.
        /// </summary>
        void MenuEntry(Transform parent, Sprite glyph, string label, string detail,
            UnityEngine.Events.UnityAction action)
        {
            var frame = UIFactory.CreatePanel(parent, "Entry_" + label, RowRest);
            // Width is driven by the layout group; only the height is ours.
            frame.sizeDelta = new Vector2(0, EntryHeight);

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            // CreateButton always makes a caption; this row draws its own two
            // lines instead, so the one it made is switched off rather than
            // left to render an empty string over the top of them.
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var strip = UIFactory.CreatePanel(frame, "Strip", UiTheme.Accent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.sizeDelta = new Vector2(StripRest, 0);
            strip.GetComponent<Image>().raycastTarget = false;

            // Hairline under the row. The last one is harmless: the list ends
            // where the version line begins, and a rule there closes it.
            var rule = UIFactory.CreatePanel(frame, "Rule", HairLine);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.sizeDelta = new Vector2(0, 1);
            rule.anchoredPosition = Vector2.zero;
            rule.GetComponent<Image>().raycastTarget = false;

            var divider = UIFactory.CreatePanel(frame, "GlyphRule", HairLine);
            divider.anchorMin = new Vector2(0, 0); divider.anchorMax = new Vector2(0, 1);
            divider.pivot = new Vector2(0, 0.5f);
            divider.offsetMin = new Vector2(TextX - 22f, 22f);
            divider.offsetMax = new Vector2(TextX - 21f, -22f);
            divider.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(GlyphX, 0), new Vector2(26, 26));

            // Placed by hand rather than through CreateStackedLabels: that
            // helper is built for the compact 12/11 px pairs the map panels use
            // and fits its text into 16 px rows, which would shrink a 25 px
            // menu label straight back down to panel size.
            //
            // The column stops clear of the scrollbar's lane whether or not the
            // bar is showing, so the text does not reflow as it appears.
            float textWidth = BoardWidth - TextX - UIFactory.ScrollbarWidth - 16f;

            var title = UIFactory.CreateText(frame, label, 25, UiTheme.Text,
                TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, TextX, 22f, textWidth, 30f);
            UIFactory.Fit(title, 16);

            // Two lines of room: DEVELOPMENT's description does not fit one, and
            // a row that grew for it alone would break the column's rhythm.
            var sub = UIFactory.CreateText(frame, detail, 15, UiTheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(sub.rectTransform, TextX, 54f, textWidth, 38f);

            var entry = new Entry
            {
                Fill = frame.GetComponent<Image>(),
                Strip = strip,
                Glyph = icon,
                Label = title,
                Detail = sub
            };
            // Hover is painted by hand rather than through Button's own colour
            // tint: the tint multiplies the whole row including the glyph and
            // the strip, which washes the accent out instead of lifting it.
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.PointerEnter, () => Paint(entry, true));
            AddEvent(trigger, EventTriggerType.PointerExit, () => Paint(entry, false));
            Paint(entry, false);
        }

        /// <summary>Row fill at rest and under the cursor \u2014 washes over the artwork, not slabs.</summary>
        static readonly Color RowRest = new Color(1f, 1f, 1f, 0.030f);
        static readonly Color RowHover = new Color(0.180f, 0.506f, 0.941f, 0.16f);
        /// <summary>Every rule on this screen, so they cannot drift apart.</summary>
        static readonly Color HairLine = new Color(1f, 1f, 1f, 0.055f);

        static void Paint(Entry e, bool hover)
        {
            e.Fill.color = hover ? RowHover : RowRest;
            e.Strip.sizeDelta = new Vector2(hover ? StripHover : StripRest, 0);
            e.Glyph.color = hover ? Color.white : UiTheme.Accent;
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

        /// <summary>
        /// The version line, and the small accent tick that marks the foot of
        /// the board's spine.
        ///
        /// Quiet on purpose: a build number is for the person filing a bug, not
        /// for the person about to play, so it is the faintest text on the
        /// screen and sits under everything else. The tick is what stops it
        /// floating — it ties the line back to the rule the whole column hangs
        /// off.
        /// </summary>
        void BuildFooter(Transform parent)
        {
            var tick = UIFactory.CreatePanel(parent, "FooterTick", UiTheme.Accent);
            UIFactory.Place(tick, new Vector2(0f, 0f), new Vector2(RuleX - 3f, 40f), new Vector2(7, 7));
            tick.GetComponent<Image>().raycastTarget = false;

            var version = UIFactory.CreateText(parent,
                $"{GameConfig.GameName}   ·   {GameConfig.Version}", 14, UiTheme.TextFaint,
                TextAnchor.LowerLeft);
            UIFactory.Place(version.rectTransform, new Vector2(0f, 0f),
                new Vector2(BoardX + 6f, 36f), new Vector2(BoardWidth, 20));

            // No "Esc — quit" line. QUIT is the last row of the list, in words,
            // where somebody looking for the way out will look — a shortcut
            // caption under it was a second answer to a question the menu had
            // already answered. Escape still works; see Update.
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
            // While the opening film is up, every key belongs to it — Escape
            // skips the video rather than opening the quit dialog behind it.
            if (IntroVideoUI.Showing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_quitModal != null && _quitModal.activeSelf) HideQuitModal();
                else ShowQuitModal();
            }
        }
    }
}
