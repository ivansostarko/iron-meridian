using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// The **command board** — the column of entries down the left-hand edge
    /// that the main menu is built from, and now the EXTRAS board with it.
    ///
    /// It lives here rather than inside <see cref="MainMenuUI"/> because two
    /// screens draw it and a copied layout is a layout that drifts: the moment
    /// one of them changes a row height, a strip width or a hover colour, the
    /// two screens stop looking like the same program. A player walking from
    /// the main menu into EXTRAS should see the same board with different rows
    /// on it, not a second interface that resembles the first.
    ///
    /// What the board is, in three devices — each of which replaced something
    /// assembled out of interface parts:
    ///
    /// • **A field, not a panel.** The interface stands on a column of shadow
    ///   that dissolves into the artwork rather than ending at a hairline. A
    ///   flat panel with an edge reads as a dialog laid over a photograph.
    ///   See <see cref="BuildField"/>.
    ///
    /// • **Flat rows, not cards.** Entries are divided by rules rather than
    ///   boxed one by one; six bordered cards is six frames to cross to read
    ///   six things. The border is what the hover adds. See <see cref="Entry"/>.
    ///
    /// • **The rows say where they lead.** Each carries a line of detail, and
    ///   each can swap the screen's artwork to a picture of its destination as
    ///   the cursor crosses it — see <see cref="ScreenBackdrop"/>.
    ///
    /// The board **scrolls**, so a list can outgrow the window instead of
    /// running off the bottom of a short one.
    ///
    /// Built at runtime like every other screen (golden rule 2), from
    /// <see cref="UIFactory"/> and the <see cref="UiTheme"/> palette.
    /// </summary>
    public static class MenuBoard
    {
        // ------------------------------------------------------------ layout

        /// <summary>Left inset of the board, and the width the entries fill.</summary>
        public const float BoardX = 56f, BoardWidth = 500f;

        /// <summary>
        /// The darkened field the board stands on: solid this far, then
        /// <see cref="FadeWidth"/> of gradient.
        /// </summary>
        public const float FieldWidth = BoardX + BoardWidth + 40f;
        public const float FadeWidth = 260f;

        /// <summary>Opacity of the field under the interface.</summary>
        public const float FieldAlpha = 0.88f;

        /// <summary>The hairline down the board's leading edge, and its inset.</summary>
        public const float RuleX = BoardX - 12f;

        /// <summary>
        /// One entry row. Tall enough for a 24 px title over a wrapped line of
        /// detail with air around both — the rows are the screen's one piece of
        /// interface and they are read before they are clicked.
        /// </summary>
        public const float EntryHeight = 96f, EntryGap = 2f;

        /// <summary>Accent strip down an entry's leading edge, at rest and under the cursor.</summary>
        const float StripRest = 0f, StripHover = 4f;

        /// <summary>
        /// How far a row's contents are inset from its own left edge.
        ///
        /// The row's fill and its accent strip still run to the edge — the strip
        /// is the thing that marks the row, and a strip that started 40 px in
        /// would be a floating tick rather than a leading edge. What moves is
        /// everything you read: the glyph, the rule beside it and both lines of
        /// text.
        /// </summary>
        const float ContentInset = 40f;

        /// <summary>Left inset of a row's glyph, and of the text column beside it.</summary>
        const float GlyphX = ContentInset + 30f, TextX = ContentInset + 92f;

        // ------------------------------------------------------------ colour

        /// <summary>Row fill at rest and under the cursor — washes over the artwork, not slabs.</summary>
        static readonly Color RowRest = new Color(1f, 1f, 1f, 0.030f);
        static readonly Color RowHover = new Color(0.180f, 0.506f, 0.941f, 0.16f);

        /// <summary>Every rule on a board screen, so they cannot drift apart.</summary>
        public static readonly Color HairLine = new Color(1f, 1f, 1f, 0.055f);

        static Color FieldColour(float alpha) => new Color(0.012f, 0.020f, 0.031f, alpha);

        // ------------------------------------------------------------- field

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
        public static void BuildField(Transform parent)
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
            // a spine for the masthead and the list to hang off. It stops short
            // of both ends so it reads as a rule rather than as a screen border.
            var rule = UIFactory.CreatePanel(parent, "Spine", HairLine);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(0, 1);
            rule.pivot = new Vector2(0, 0.5f);
            rule.offsetMin = new Vector2(RuleX, 60f);
            rule.offsetMax = new Vector2(RuleX + 1f, -60f);
            rule.GetComponent<Image>().raycastTarget = false;
        }

        // -------------------------------------------------------------- list

        /// <summary>
        /// The scroll view the entries live in, spanning the board between the
        /// masthead and the footer. <paramref name="top"/> and
        /// <paramref name="bottom"/> are the screen's own offsets — the masthead
        /// is the part each screen writes for itself.
        ///
        /// The scrollbar shows itself only when the list is actually longer than
        /// the board: a handful of entries fit any window worth supporting, so a
        /// permanent bar was a permanent claim that there was more below.
        /// </summary>
        public static RectTransform BuildList(Transform parent, float top, float bottom)
        {
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform content,
                withScrollbar: true, autoHideScrollbar: true);
            // The board field behind it already carries the darkening; the
            // scroll view's own default wash would double it up into a slab.
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(BoardX, bottom);
            rt.offsetMax = new Vector2(BoardX + BoardWidth, top);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = EntryGap;
            layout.padding = new RectOffset(0, 0, 0, 12);
            return content;
        }

        // ------------------------------------------------------------- entry

        /// <summary>
        /// The parts of one row that change under the cursor. Held together so
        /// the hover paint is one call rather than five scattered lookups.
        /// </summary>
        class Row
        {
            public Image Fill;
            public RectTransform Strip;
            public Image Glyph;
            public Text Label;
            public Text Detail;
        }

        /// <summary>
        /// One entry: a flat row carrying a glyph, a title and a line of detail.
        ///
        /// **Flat, not a card.** The rows are one column of faint fills
        /// separated by a rule, so the list reads as a single object; the border
        /// is what the *hover* adds, along with the accent strip and a lift in
        /// the fill.
        ///
        /// **The glyph column is separated by a rule**, not by whitespace alone.
        /// At 26 px an icon beside a 25 px title competes with it; behind a rule
        /// it reads as an index down the side of the list.
        ///
        /// Pass a <paramref name="backdrop"/> and a <paramref name="preview"/>
        /// and the row shows where it leads while the cursor is on it. A row
        /// with no preview named leaves the screen's own artwork up rather than
        /// clearing it to something blank — which is what keeps adding a row
        /// that has no picture yet cheap.
        /// </summary>
        public static void Entry(Transform parent, Sprite glyph, string label, string detail,
            UnityEngine.Events.UnityAction action,
            ScreenBackdrop backdrop = null, BackgroundId? preview = null)
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
            // where the footer begins, and a rule there closes it.
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

            // Two lines of room: the longest description does not fit one, and a
            // row that grew for it alone would break the column's rhythm.
            var sub = UIFactory.CreateText(frame, detail, 15, UiTheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(sub.rectTransform, TextX, 54f, textWidth, 38f);

            var row = new Row
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
            AddEvent(trigger, EventTriggerType.PointerEnter, () =>
            {
                Paint(row, true);
                if (backdrop != null && preview.HasValue) backdrop.Preview(preview.Value);
            });
            AddEvent(trigger, EventTriggerType.PointerExit, () =>
            {
                Paint(row, false);
                if (backdrop != null && preview.HasValue) backdrop.ClearPreview();
            });
            Paint(row, false);
        }

        static void Paint(Row r, bool hover)
        {
            r.Fill.color = hover ? RowHover : RowRest;
            r.Strip.sizeDelta = new Vector2(hover ? StripHover : StripRest, 0);
            r.Glyph.color = hover ? Color.white : UiTheme.Accent;
            r.Label.color = hover ? Color.white : UiTheme.Text;
            r.Detail.color = hover ? UiTheme.Text : UiTheme.TextDim;
        }

        static void AddEvent(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        // ------------------------------------------------------------ footer

        /// <summary>
        /// A quiet line at the foot of the board, with the small accent tick
        /// that marks the bottom of the spine.
        ///
        /// The tick is what stops the line floating — it ties it back to the
        /// rule the whole column hangs off.
        /// </summary>
        public static Text BuildFooter(Transform parent, string text)
        {
            var tick = UIFactory.CreatePanel(parent, "FooterTick", UiTheme.Accent);
            UIFactory.Place(tick, new Vector2(0f, 0f), new Vector2(RuleX - 3f, 40f), new Vector2(7, 7));
            tick.GetComponent<Image>().raycastTarget = false;

            var line = UIFactory.CreateText(parent, text, 14, UiTheme.TextFaint, TextAnchor.LowerLeft);
            UIFactory.Place(line.rectTransform, new Vector2(0f, 0f),
                new Vector2(BoardX + 6f, 36f), new Vector2(BoardWidth, 20));
            return line;
        }
    }
}
