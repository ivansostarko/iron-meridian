using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Lines;

namespace IronMeridian.UI
{
    /// <summary>
    /// Options for the control measure about to be drawn: owning side, colour,
    /// width, planned or actual, caption, and whether it stands up in 3D.
    ///
    /// **This used to be a modal dialog and is now a docked right-hand panel.**
    /// A modal was the wrong shape for the job twice over. It blocked the map
    /// while the settings it was collecting are all about *where* the line will
    /// go — you could not look at the ground you were about to draw on. And it
    /// had to be dismissed before drawing, so changing your mind about a colour
    /// meant cancelling the line and starting again. Docked, the settings stay
    /// on screen beside the map, the terrain stays visible and clickable behind
    /// them, and a change applies to the next line without a round trip.
    ///
    /// The **kind** of measure is not chosen here — it is chosen in the left
    /// rail's CONTROL MEASURES section, which is what opens this panel. Kind
    /// changes how a line should be drawn on the ground (a rear boundary runs
    /// parallel to the front, a lateral one runs into it), so it belongs with
    /// the other "what am I doing" choices rather than among the styling.
    /// </summary>
    public class BoundaryPanelUI : MonoBehaviour
    {
        /// <summary>True while the panel is showing.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when the panel opens, so competing right-hand panels can stand down.</summary>
        public System.Action Opened;

        const float Pad = UiTheme.PanelPadding;
        static readonly Color SwatchBorder = new Color(1f, 1f, 1f, 0.25f);

        /// <summary>
        /// The kinds worth offering, in the order a planner thinks about them.
        /// Legacy <c>Boundary</c> is omitted — it is what the auto front line
        /// uses and is not something to draw by hand.
        /// </summary>
        public static readonly (LineKind kind, string name, string detail)[] Kinds =
        {
            (LineKind.LateralBoundary, "LATERAL BOUNDARY", "Left/right limit of a formation's AO"),
            (LineKind.RearBoundary,    "REAR BOUNDARY",    "Rear limit of a formation's AO"),
            (LineKind.Feba,            "FEBA",             "Forward edge of the battle area"),
            (LineKind.PhaseLine,       "PHASE LINE",       "Named line for control and coordination"),
            (LineKind.DefensiveLine,   "DEFENSIVE LINE",   "Hand-drawn fortified line")
        };

        static readonly (string name, string hex)[] Colours =
        {
            ("Doctrinal", ""),
            ("Blue",   "#3B82F6"),
            ("Red",    "#E5484D"),
            ("Yellow", "#FFD91A"),
            ("Green",  "#3FBF67"),
            ("White",  "#E8EDF3"),
            ("Orange", "#F08A24")
        };

        static readonly (string name, float metres)[] Widths =
        {
            ("Default", 0f), ("Thin", 25f), ("Medium", 60f), ("Thick", 110f)
        };

        LineDrawTool _tool;
        Action _onStartDrawing;

        RectTransform _panel;
        Text _kindName, _kindDetail;
        InputField _labelField;

        int _kind, _colour, _width, _team;
        bool _planned, _draw3D = true;

        readonly List<(int index, Image fill, Text label)> _sideButtons = new List<(int, Image, Text)>();
        readonly List<(int index, Image frame)> _colourButtons = new List<(int, Image)>();
        readonly List<(int index, Image fill, Text label)> _widthButtons = new List<(int, Image, Text)>();
        Image _plannedLamp, _threeDLamp;
        Text _plannedLabel, _threeDLabel;

        public static BoundaryPanelUI Create(Canvas canvas, LineDrawTool tool, Action onStartDrawing)
        {
            var go = new GameObject("BoundaryPanel");
            go.transform.SetParent(canvas.transform, false);

            var panel = go.AddComponent<BoundaryPanelUI>();
            panel._tool = tool;
            panel._onStartDrawing = onStartDrawing;
            panel.Build(canvas);
            return panel;
        }

        // ------------------------------------------------------------- build

        void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "BoundaryOptions", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0);
            _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-UiTheme.RightPanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -UiTheme.TopBarHeight);

            // Hairline down the panel's left edge, separating it from the map.
            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            float inner = UiTheme.RightPanelWidth - Pad * 2f;

            var title = UIFactory.CreateText(_panel, "CONTROL MEASURE", UiTheme.FontHeading,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -14),
                new Vector2(inner - 30f, 22));

            var close = UIFactory.CreateIconButton(_panel, UiIcons.Close, Hide,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 7f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-Pad, -12), new Vector2(26, 26));

            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -38);

            // The kind chosen in the left rail, echoed here so the panel always
            // says what it is configuring.
            var kindFrame = UIFactory.CreateBorderedPanel(_panel, "Kind", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(kindFrame, new Vector2(0f, 1f), new Vector2(Pad, -48), new Vector2(inner, 46));
            (_kindName, _kindDetail) = UIFactory.CreateStackedLabels(kindFrame, "", "", 10f, inner - 20f, 7f);

            float y = -104f;

            Label("SIDE", ref y, inner);
            BuildSides(y, inner); y -= 40f;

            Label("COLOUR", ref y, inner);
            BuildColours(y, inner); y -= 40f;

            Label("WIDTH", ref y, inner);
            BuildWidths(y, inner); y -= 40f;

            Label("STATUS", ref y, inner);
            _plannedLamp = ToggleRow(_panel, "PLANNED", y, inner, () =>
            {
                _planned = !_planned;
                Refresh();
            }, out _plannedLabel);
            y -= 40f;

            // Not offered for boundaries and phase lines: those are drawn on the
            // map, not built on the ground, so there is nothing for them to
            // stand up in. The row stays visible and reports DRAPED rather than
            // vanishing — a control that appears and disappears as the kind
            // changes is harder to read than one that says why it is off.
            _threeDLamp = ToggleRow(_panel, "STAND UP IN 3D", y, inner, () =>
            {
                if (FlatKind) return;
                _draw3D = !_draw3D;
                Refresh();
            }, out _threeDLabel);
            y -= 46f;

            Label("CAPTION", ref y, inner);
            _labelField = UIFactory.CreateInputField(_panel, "e.g. PL BLUE", UiTheme.FontBody);
            UIFactory.Place(_labelField.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(Pad, y), new Vector2(inner, 32));
            y -= 48f;

            var start = UIFactory.CreateBorderedPanel(_panel, "StartDrawing", UiTheme.Success, UiTheme.Success);
            UIFactory.Place(start, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 40));
            var startBtn = UIFactory.CreateButton(start, "START DRAWING", Apply,
                new Color(0, 0, 0, 0), Color.white, UiTheme.FontBody);
            UIFactory.Stretch((RectTransform)startBtn.transform);
            y -= 48f;

            var hint = UIFactory.CreateText(_panel,
                "Click the map to place each vertex. Enter or double-click finishes the line; " +
                "Esc abandons it. These settings stay put, so the next line of the same kind needs no setup.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y),
                new Vector2(inner, 90));

            _panel.gameObject.SetActive(false);
        }

        void Label(string text, ref float y, float inner)
        {
            var t = UIFactory.CreateSectionHeader(_panel, text, UiTheme.TextFaint);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 14));
            y -= 20f;
        }

        void BuildSides(float y, float inner)
        {
            string[] names = { "NEUTRAL", "BLUE", "RED" };
            float w = (inner - 8f) / 3f;

            for (int i = 0; i < names.Length; i++)
            {
                int captured = i;
                var frame = UIFactory.CreateBorderedPanel(_panel, "Side" + i, UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 4f), y), new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => { _team = captured; Refresh(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);

                _sideButtons.Add((i, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        void BuildColours(float y, float inner)
        {
            float size = (inner - 6f * 6f) / 7f;

            for (int i = 0; i < Colours.Length; i++)
            {
                int captured = i;
                var frame = UIFactory.CreateBorderedPanel(_panel, "Colour" + i, SwatchColour(i), SwatchBorder);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (size + 6f), y),
                    new Vector2(size, 30));

                var btn = UIFactory.CreateButton(frame, "", () => { _colour = captured; Refresh(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)btn.transform);
                var caption = btn.GetComponentInChildren<Text>(true);
                if (caption != null) caption.gameObject.SetActive(false);

                // The border marks the selection, not the fill: the fill *is*
                // the colour being chosen and must not be tampered with. In a
                // bordered panel the outer image is the border and the "Fill"
                // child is the interior, so it is the frame itself that is tinted.
                _colourButtons.Add((i, frame.GetComponent<Image>()));
            }
        }

        /// <summary>Swatch fill. "Doctrinal" has no colour of its own, so it shows as neutral surface.</summary>
        static Color SwatchColour(int index)
        {
            if (string.IsNullOrEmpty(Colours[index].hex)) return UiTheme.Surface;
            return ColorUtility.TryParseHtmlString(Colours[index].hex, out var c) ? c : UiTheme.Surface;
        }

        void BuildWidths(float y, float inner)
        {
            float w = (inner - 12f) / 4f;

            for (int i = 0; i < Widths.Length; i++)
            {
                int captured = i;
                var frame = UIFactory.CreateBorderedPanel(_panel, "Width" + i, UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 4f), y), new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, Widths[i].name.ToUpperInvariant(),
                    () => { _width = captured; Refresh(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);

                _widthButtons.Add((i, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        Image ToggleRow(RectTransform parent, string label, float y, float inner,
            UnityEngine.Events.UnityAction action, out Text valueLabel)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Toggle_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 32));

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var lamp = UIFactory.CreatePanel(frame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(8, 8));
            lamp.GetComponent<Image>().raycastTarget = false;

            var name = UIFactory.CreateText(frame, label, UiTheme.FontLabel, UiTheme.Text, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(26, 0), new Vector2(inner - 90f, 16));

            valueLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextDim, TextAnchor.MiddleRight);
            valueLabel.raycastTarget = false;
            UIFactory.Place(valueLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(60, 16));

            return lamp.GetComponent<Image>();
        }

        // ------------------------------------------------------------- state

        /// <summary>Opens the panel configured for one kind of control measure.</summary>
        public void Show(LineKind kind)
        {
            _kind = Mathf.Max(0, Array.FindIndex(Kinds, k => k.kind == kind));

            // Everything except the kind carries over from the last line drawn,
            // so drawing five phase lines in a row needs the settings once.
            _colour = Mathf.Max(0, Array.FindIndex(Colours, c => c.hex == _tool.PendingColorHex));
            _width = Mathf.Max(0, Array.FindIndex(Widths, w => Mathf.Approximately(w.metres, _tool.PendingWidth)));
            _team = _tool.PendingTeam.HasValue ? (_tool.PendingTeam.Value == Team.User ? 1 : 2) : 0;
            _planned = _tool.PendingPlanned;
            _draw3D = _tool.Draw3D;
            if (_labelField != null) _labelField.text = _tool.PendingLabel;

            _panel.gameObject.SetActive(true);
            IsOpen = true;
            Opened?.Invoke();
            Refresh();
        }

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
            IsOpen = false;
        }

        void Refresh()
        {
            _kindName.text = Kinds[_kind].name;
            _kindDetail.text = Kinds[_kind].detail;

            foreach (var (i, fill, label) in _sideButtons)
            {
                bool on = i == _team;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }

            foreach (var (i, border) in _colourButtons)
                border.color = i == _colour ? UiTheme.Accent : SwatchBorder;

            foreach (var (i, fill, label) in _widthButtons)
            {
                bool on = i == _width;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }

            _plannedLamp.color = _planned ? UiTheme.Warning : UiTheme.TextFaint;
            _plannedLabel.text = _planned ? "PLANNED" : "ACTUAL";

            bool standing = _draw3D && !FlatKind;
            _threeDLamp.color = standing ? UiTheme.Success : UiTheme.TextFaint;
            _threeDLabel.text = FlatKind ? "DRAPED" : standing ? "ON" : "FLAT";
        }

        /// <summary>True when the chosen kind is always drawn on the ground — see <see cref="MapLine.FlatOnly"/>.</summary>
        bool FlatKind => LineDrawTool.IsFlatKind(Kinds[_kind].kind);

        /// <summary>Pushes the settings onto the draw tool and arms it.</summary>
        void Apply()
        {
            _tool.PendingKind = Kinds[_kind].kind;
            _tool.PendingTeam = _team == 0 ? (Team?)null : _team == 1 ? Team.User : Team.Enemy;
            _tool.PendingColorHex = Colours[_colour].hex;
            _tool.PendingWidth = Widths[_width].metres;
            _tool.PendingPlanned = _planned;
            _tool.PendingLabel = _labelField != null ? _labelField.text : "";
            _tool.Draw3D = _draw3D;

            _tool.StartDrawingStyled();
            _onStartDrawing?.Invoke();
        }

        void OnDestroy()
        {
            if (IsOpen) IsOpen = false;
        }
    }
}
