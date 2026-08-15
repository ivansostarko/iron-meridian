using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// The casualty list — TAB in battle mode.
    ///
    /// **Why a modal and not a HUD panel.** Losses are not something a
    /// commander watches tick up; they are something they stop and read. The
    /// numbers only mean anything side by side — your battalions against
    /// theirs, this hour against the last — and side by side needs a page, not
    /// a corner. A permanent readout would also be a permanent distraction from
    /// the map, which is the thing the player is actually commanding from.
    ///
    /// **Two columns, and they are deliberately identical.** The whole question
    /// the screen answers is "who is winning the attrition", and that is a
    /// comparison. Anything that made the friendly side read differently from
    /// the hostile one — a different sort, an extra column, a summary only one
    /// of them gets — would make the two halves incomparable and the screen
    /// pointless.
    ///
    /// **Two numbers per row.** FORM is counters destroyed outright: what the
    /// player has stopped being able to command. MEN is the manpower behind
    /// every point of strength lost, across destroyed *and surviving*
    /// formations — the number that keeps climbing during an exchange where
    /// nothing on the map has died yet. See <see cref="LossLedger"/> for how
    /// they are booked.
    ///
    /// Follows the shape of <see cref="ConfirmDialog"/>: a static open/close, an
    /// <see cref="IsOpen"/> flag the map's input guards read so clicks and keys
    /// do not fall through to the terrain, and a scrim that swallows anything
    /// aimed past it.
    /// </summary>
    public class LossesDialog : MonoBehaviour
    {
        /// <summary>True while the list is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        const float PanelW = 940f;
        const float PanelH = 620f;
        const float Pad = 26f;
        /// <summary>Gutter between the two sides' columns.</summary>
        const float ColumnGap = 24f;
        /// <summary>Height of the per-side summary block above each table.</summary>
        const float SummaryH = 86f;
        const float HeaderH = 92f;
        const float FooterH = 52f;
        const float RowHeight = 26f;

        // Column shares inside one side's table. Name takes what is left.
        const float FormationsWidth = 62f;
        const float PersonnelWidth = 88f;

        static LossesDialog _active;

        RectTransform _friendlyBody, _hostileBody;
        Text _friendlySummary, _hostileSummary;
        /// <summary>Width of one side's column. Held rather than measured — see <see cref="Fill"/>.</summary>
        float _columnWidth;

        // ------------------------------------------------------------ opening

        public static void Toggle(Canvas canvas)
        {
            if (IsOpen) Close();
            else Open(canvas);
        }

        public static void Open(Canvas canvas)
        {
            if (canvas == null) return;
            Close();

            var go = new GameObject("LossesDialog");
            go.transform.SetParent(canvas.transform, false);
            _active = go.AddComponent<LossesDialog>();
            _active.Build(go.transform);
            IsOpen = true;

            // Rebuilt on every booking rather than polled: a tick of combat can
            // add a row, and a table that only refreshed on a timer would show
            // a total that disagreed with the rows above it.
            LossLedger.Changed += _active.Refresh;
        }

        public static void Close()
        {
            if (_active != null)
            {
                LossLedger.Changed -= _active.Refresh;
                Destroy(_active.gameObject);
            }
            _active = null;
            IsOpen = false;
        }

        // ------------------------------------------------------------- build

        void Build(Transform root)
        {
            var scrim = UIFactory.CreatePanel(root, "Scrim", new Color(0.02f, 0.03f, 0.05f, 0.78f));
            UIFactory.Stretch(scrim);

            var panel = UIFactory.CreateBorderedPanel(root, "Panel", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));

            var heading = UIFactory.CreateText(panel, "BATTLE LOSSES", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(heading.rectTransform, Pad, 22f, PanelW - Pad * 2f - 40f, 28f);

            var blurb = UIFactory.CreateText(panel,
                "FORM is formations destroyed outright. MEN is the manpower behind every point of " +
                "strength lost, in surviving formations as well as dead ones.",
                UiTheme.FontSmall, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(blurb.rectTransform, Pad, 54f, PanelW - Pad * 2f - 40f, 32f);

            var close = UIFactory.CreateIconButton(panel, UiIcons.Close, Close,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 8f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-14, -14), new Vector2(30, 30));

            var rule = UIFactory.CreateDivider(panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -HeaderH);

            float columnW = (PanelW - Pad * 2f - ColumnGap) / 2f;
            _columnWidth = columnW;

            BuildSide(panel, Team.User, "FRIENDLY LOSSES", UiTheme.Friendly, Pad, columnW,
                out _friendlySummary, out _friendlyBody);
            BuildSide(panel, Team.Enemy, "HOSTILE LOSSES", UiTheme.Hostile, Pad + columnW + ColumnGap,
                columnW, out _hostileSummary, out _hostileBody);

            var hint = UIFactory.CreateText(panel, "TAB or ESC to close",
                UiTheme.FontSmall, UiTheme.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-Pad, 18f),
                new Vector2(300f, 20f));

            Refresh();
        }

        /// <summary>One side's column: a title, a summary block and the table under it.</summary>
        void BuildSide(RectTransform panel, Team team, string title, Color accent,
            float x, float width, out Text summary, out RectTransform body)
        {
            var column = UIFactory.CreateGroup(panel, "Column_" + team);
            UIFactory.Place(column, new Vector2(0f, 1f), new Vector2(x, -(HeaderH + 14f)),
                new Vector2(width, PanelH - HeaderH - FooterH - 14f));

            var caption = UIFactory.CreateText(column, title, UiTheme.FontHeading, accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(caption.rectTransform, 0f, 0f, width, 20f);

            var frame = UIFactory.CreateBorderedPanel(column, "Summary", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(0f, -26f), new Vector2(width, SummaryH));

            summary = UIFactory.CreateText(frame, "", UiTheme.FontBody, UiTheme.Text, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(summary.rectTransform, 14f, 12f, width - 28f, SummaryH - 20f);

            // Table headings, in the same three columns as the rows below.
            float headTop = 26f + SummaryH + 14f;
            var headRow = UIFactory.CreateGroup(column, "TableHead");
            UIFactory.Place(headRow, new Vector2(0f, 1f), new Vector2(0f, -headTop), new Vector2(width, 22f));
            HeadCell(headRow, "FORMATION", width, 0);
            HeadCell(headRow, "FORM", width, 1);
            HeadCell(headRow, "MEN", width, 2);

            var headRule = UIFactory.CreateDivider(headRow, UiTheme.Border);
            headRule.anchorMin = new Vector2(0, 0); headRule.anchorMax = new Vector2(1, 0);
            headRule.pivot = new Vector2(0.5f, 0);
            headRule.anchoredPosition = Vector2.zero;

            var scroll = UIFactory.CreateScrollView(column, out body, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 0);
            srt.offsetMax = new Vector2(0, -(headTop + 24f));

            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 0;
            layout.padding = new RectOffset(0, 0, 4, 8);
        }

        void HeadCell(RectTransform row, string label, float width, int column)
        {
            var t = UIFactory.CreateText(row, label, UiTheme.FontLabel, UiTheme.TextFaint,
                column == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, FontStyle.Bold);
            t.raycastTarget = false;
            PlaceCell(t.rectTransform, width, column);
        }

        /// <summary>
        /// Puts a cell in one of the three columns. Measured from the right edge
        /// inward, so the two numeric columns keep a fixed width and the name
        /// takes whatever is left — a name column that shrank as a number grew
        /// would make the two sides' tables stop lining up with each other.
        /// </summary>
        static void PlaceCell(RectTransform rt, float width, int column)
        {
            float scrollbar = UIFactory.ScrollbarWidth + 6f;
            switch (column)
            {
                case 0:
                    UIFactory.Place(rt, new Vector2(0f, 0.5f), new Vector2(2f, 0f),
                        new Vector2(width - FormationsWidth - PersonnelWidth - scrollbar - 8f, 18f));
                    break;
                case 1:
                    UIFactory.Place(rt, new Vector2(1f, 0.5f),
                        new Vector2(-(PersonnelWidth + scrollbar), 0f), new Vector2(FormationsWidth, 18f));
                    break;
                default:
                    UIFactory.Place(rt, new Vector2(1f, 0.5f), new Vector2(-scrollbar, 0f),
                        new Vector2(PersonnelWidth, 18f));
                    break;
            }
        }

        // ------------------------------------------------------------ content

        void Refresh()
        {
            if (_friendlyBody == null) return;
            Fill(Team.User, _friendlySummary, _friendlyBody);
            Fill(Team.Enemy, _hostileSummary, _hostileBody);
        }

        void Fill(Team team, Text summary, RectTransform body)
        {
            var (formations, personnel) = LossLedger.Total(team);
            int surviving = LossLedger.Surviving(team);

            summary.text =
                $"{formations:n0} formation(s) destroyed  ·  {surviving:n0} still on the map\n" +
                $"{Mathf.RoundToInt(personnel):n0} personnel lost";
            summary.color = formations > 0 ? UiTheme.Text : UiTheme.TextDim;

            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = body.childCount - 1; i >= 0; i--)
            {
                var child = body.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            var rows = LossLedger.For(team);
            if (rows.Count == 0)
            {
                var none = UIFactory.CreateText(body, "No losses recorded.", UiTheme.FontSmall,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)none.transform).sizeDelta = new Vector2(0, 40);
                return;
            }

            // The held width rather than the viewport's measured one: this runs
            // during Build, before uGUI has laid anything out, so rect.width is
            // still zero and every cell would be placed at nothing wide.
            foreach (var row in rows) BuildRow(body, row, _columnWidth);
        }

        void BuildRow(RectTransform body, LossLedger.Row data, float width)
        {
            var row = UIFactory.CreatePanel(body, "Loss_" + data.defId, new Color(1f, 1f, 1f, 0.02f));
            row.sizeDelta = new Vector2(0, RowHeight);

            var name = UIFactory.CreateText(row, data.name, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            PlaceCell(name.rectTransform, width, 0);
            UIFactory.Fit(name, 9);

            var formations = UIFactory.CreateText(row, data.formations.ToString(), UiTheme.FontSmall,
                data.formations > 0 ? UiTheme.Warning : UiTheme.TextFaint,
                TextAnchor.MiddleRight, FontStyle.Bold);
            formations.raycastTarget = false;
            PlaceCell(formations.rectTransform, width, 1);

            var personnel = UIFactory.CreateText(row, $"{Mathf.RoundToInt(data.personnel):n0}",
                UiTheme.FontSmall, UiTheme.TextDim, TextAnchor.MiddleRight);
            personnel.raycastTarget = false;
            PlaceCell(personnel.rectTransform, width, 2);
        }

        // ---------------------------------------------------------- lifecycle

        void Update()
        {
            // Escape only. TAB closes this too, but that is handled by whoever
            // opened it (<c>GameController.Update</c>) and deliberately not
            // here: two MonoBehaviours reading the same key in the same frame
            // run in an undefined order, so this one closing on TAB while the
            // other saw an unopened dialog and opened it again would make the
            // page impossible to dismiss.
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnDestroy()
        {
            if (_active != this) return;
            LossLedger.Changed -= Refresh;
            _active = null;
            IsOpen = false;
        }
    }
}
