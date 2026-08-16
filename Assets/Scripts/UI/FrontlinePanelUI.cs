using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;

namespace IronMeridian.UI
{
    /// <summary>
    /// Settings for the automatic front line, opened by **clicking the line on
    /// the map**.
    ///
    /// Clicking the thing you want to configure is the only discoverable way in.
    /// The alternative was another row in the left rail, which would have put a
    /// permanently-visible control next to nine others for a line that is only
    /// interesting while you are looking at it — and would still not have
    /// answered "what is this red line and why is it there?", which is the
    /// question a player actually has.
    ///
    /// It shares the right-hand edge with the unit info panel and the control
    /// measure options, so opening it drops the unit selection; see
    /// <see cref="Opened"/>.
    ///
    /// The settings are the ones that change what the line *says*, not just how
    /// it looks: resolution and smoothing decide how faithfully it follows the
    /// forces, and influence width decides how far along the front one formation
    /// is allowed to speak for. See <see cref="FrontlineSystem"/> for what each
    /// one does to the solve.
    /// </summary>
    public class FrontlinePanelUI : MonoBehaviour
    {
        /// <summary>True while the panel is showing.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when the panel opens, so competing right-hand panels can stand down.</summary>
        public Action Opened;

        const float Pad = UiTheme.PanelPadding;
        static readonly Color SwatchBorder = new Color(1f, 1f, 1f, 0.25f);

        static readonly (string name, string hex)[] Colours =
        {
            ("Front red", ""),          // empty = the system's own default
            ("Crimson", "#B3121B"),
            ("Orange",  "#F08A24"),
            ("Amber",   "#FFD91A"),
            ("Violet",  "#A855F7"),
            ("White",   "#E8EDF3")
        };

        // "Standard" rather than "Default" for the zero: it is the width the
        // system draws the line at, and naming it after the setting rather than
        // after the absence of one puts it in the same language as the
        // RESOLUTION and INFLUENCE WIDTH rows below.
        static readonly (string name, float metres)[] Widths =
        {
            ("Standard", 0f), ("Thin", 35f), ("Medium", 70f), ("Heavy", 140f)
        };

        static readonly (string name, int bands)[] Resolutions =
        {
            ("Coarse", 17), ("Standard", 41), ("Fine", 73), ("Very fine", 121)
        };

        static readonly (string name, int passes)[] Smoothings =
        {
            ("Raw", 0), ("Light", 1), ("Smooth", 2), ("Silk", 3)
        };

        static readonly (string name, float km)[] Influences =
        {
            ("Tight", 2.5f), ("Standard", 6f), ("Broad", 14f), ("Sweeping", 28f)
        };

        FrontlineSystem _front;

        static readonly (string name, FlotMode mode)[] Modes =
        {
            ("AUTO", FlotMode.Automatic), ("MANUAL", FlotMode.Manual), ("HYBRID", FlotMode.Hybrid)
        };

        RectTransform _panel;
        Text _readout, _statusLabel, _segmentsLabel;
        RectTransform _autoLamp, _visibleLamp;
        Text _autoLabel, _visibleLabel;
        readonly List<(int index, Image fill, Text label)> _modeButtons = new List<(int, Image, Text)>();
        Button _drawBtn;

        // Standard width, standard resolution, standard influence width and
        // SILK smoothing — the shipped settings. They are indices into the
        // tables above and must agree with FrontlineSystem's own defaults, or
        // the panel opens lighting a button the line is not actually drawn with.
        int _colour, _width, _resolution = 1, _smoothing = 3, _influence = 1;

        readonly List<(int index, Image frame)> _colourButtons = new List<(int, Image)>();
        readonly List<(int index, Image fill, Text label)> _widthButtons = new List<(int, Image, Text)>();
        readonly List<(int index, Image fill, Text label)> _resButtons = new List<(int, Image, Text)>();
        readonly List<(int index, Image fill, Text label)> _smoothButtons = new List<(int, Image, Text)>();
        readonly List<(int index, Image fill, Text label)> _infButtons = new List<(int, Image, Text)>();

        public static FrontlinePanelUI Create(Canvas canvas, FrontlineSystem front)
        {
            var go = new GameObject("FrontlinePanel");
            go.transform.SetParent(canvas.transform, false);

            var panel = go.AddComponent<FrontlinePanelUI>();
            panel._front = front;
            panel.Build(canvas);
            front.Recomputed += panel.RefreshReadout;
            return panel;
        }

        void OnDestroy()
        {
            if (_front != null) _front.Recomputed -= RefreshReadout;
            if (IsOpen) IsOpen = false;
        }

        // ------------------------------------------------------------- build

        void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "FrontlineOptions", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0);
            _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-UiTheme.RightPanelWidth, 0);
            // Below the strike dock's icon strip — see StrikeDockUI.
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            float inner = UiTheme.RightPanelWidth - Pad * 2f;

            var title = UIFactory.CreateText(_panel, "FRONT LINE", UiTheme.FontHeading,
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

            // What the line currently is, before any of the controls that
            // change it: a panel about a computed object has to say what the
            // computation produced or the settings are being adjusted blind.
            var frame = UIFactory.CreateBorderedPanel(_panel, "Readout", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -48), new Vector2(inner, 92));

            _readout = UIFactory.CreateText(frame, "", UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_readout.rectTransform, 10f, 8f, inner - 20f, 20f);

            _statusLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_statusLabel.rectTransform, 10f, 30f, inner - 20f, 30f);

            // What each stretch of front is *doing* — the states are the whole
            // point of the line being a gameplay object rather than a drawing.
            _segmentsLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_segmentsLabel.rectTransform, 10f, 62f, inner - 20f, 26f);
            UIFactory.Fit(_segmentsLabel, 7);

            float y = -150f;

            Label("MODE", ref y, inner);
            BuildSegments(_modeButtons, Modes.Length, i => Modes[i].name, y, inner,
                i => { _front.SetMode(Modes[i].mode); Refresh(); });
            y -= 36f;

            // Drawing only means anything in MANUAL (or HYBRID before the
            // battle); the button says so instead of silently refusing.
            var drawFrame = UIFactory.CreateBorderedPanel(_panel, "DrawFlot", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(drawFrame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 30));
            _drawBtn = UIFactory.CreateButton(drawFrame, "DRAW FLOT ON MAP",
                () => { _front.StartDrawing(); Refresh(); },
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)_drawBtn.transform);
            y -= 38f;

            _visibleLamp = ToggleRow("SHOW ON MAP", y, inner, () =>
            {
                _front.SetVisible(!_front.Visible);
                Refresh();
            }, out _visibleLabel);
            y -= 38f;

            _autoLamp = ToggleRow("AUTO-UPDATE", y, inner, () =>
            {
                _front.AutoUpdate = !_front.AutoUpdate;
                if (_front.AutoUpdate) _front.Recompute();
                Refresh();
            }, out _autoLabel);
            y -= 44f;

            Label("COLOUR", ref y, inner);
            BuildColours(y, inner); y -= 38f;

            Label("WIDTH", ref y, inner);
            BuildSegments(_widthButtons, Widths.Length, i => Widths[i].name, y, inner,
                i => { _width = i; ApplyStyle(); }); y -= 38f;

            Label("RESOLUTION", ref y, inner);
            BuildSegments(_resButtons, Resolutions.Length, i => Resolutions[i].name, y, inner,
                i => { _resolution = i; _front.SetResolution(Resolutions[i].bands); Refresh(); });
            y -= 38f;

            Label("SMOOTHING", ref y, inner);
            BuildSegments(_smoothButtons, Smoothings.Length, i => Smoothings[i].name, y, inner,
                i => { _smoothing = i; _front.SetSmoothing(Smoothings[i].passes); Refresh(); });
            y -= 38f;

            Label("INFLUENCE WIDTH", ref y, inner);
            BuildSegments(_infButtons, Influences.Length, i => Influences[i].name, y, inner,
                i => { _influence = i; _front.SetInfluenceWidthKm(Influences[i].km); Refresh(); });
            y -= 46f;

            var recompute = UIFactory.CreateBorderedPanel(_panel, "Recompute", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(recompute, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 36));
            var recomputeBtn = UIFactory.CreateButton(recompute, "RECOMPUTE NOW",
                () => { _front.Recompute(); Refresh(); },
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)recomputeBtn.transform);
            y -= 44f;

            var hint = UIFactory.CreateText(_panel,
                "Each side gets its own forward edge, solved from its combat formations only — " +
                "logistics, artillery and broken units do not move the line, and an isolated group " +
                "becomes a POCKET rather than dragging the front to it. The ground between the two " +
                "edges is contested.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y),
                new Vector2(inner, 110));

            _panel.gameObject.SetActive(false);
        }

        void Label(string text, ref float y, float inner)
        {
            var t = UIFactory.CreateSectionHeader(_panel, text, UiTheme.TextFaint);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 14));
            y -= 18f;
        }

        void BuildColours(float y, float inner)
        {
            float size = (inner - 5f * 5f) / 6f;

            for (int i = 0; i < Colours.Length; i++)
            {
                int captured = i;
                var frame = UIFactory.CreateBorderedPanel(_panel, "Colour" + i, SwatchColour(i), SwatchBorder);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (size + 5f), y),
                    new Vector2(size, 28));

                var btn = UIFactory.CreateButton(frame, "", () => { _colour = captured; ApplyStyle(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)btn.transform);
                var caption = btn.GetComponentInChildren<Text>(true);
                if (caption != null) caption.gameObject.SetActive(false);

                // The border marks the selection: the fill *is* the colour being
                // chosen and must not be tinted to show state.
                _colourButtons.Add((i, frame.GetComponent<Image>()));
            }
        }

        static Color SwatchColour(int index)
        {
            if (string.IsNullOrEmpty(Colours[index].hex)) return GameConfig.FrontlineRed;
            return ColorUtility.TryParseHtmlString(Colours[index].hex, out var c) ? c : UiTheme.Surface;
        }

        /// <summary>A row of exclusive text buttons sharing the panel's inner width.</summary>
        void BuildSegments(List<(int, Image, Text)> into, int count, Func<int, string> name,
            float y, float inner, Action<int> onPick)
        {
            float w = (inner - (count - 1) * 4f) / count;

            for (int i = 0; i < count; i++)
            {
                int captured = i;
                var frame = UIFactory.CreateBorderedPanel(_panel, "Seg" + into.Count + "_" + i,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 4f), y),
                    new Vector2(w, 28));

                var btn = UIFactory.CreateButton(frame, name(i).ToUpperInvariant(),
                    () => onPick(captured), new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);
                var text = btn.GetComponentInChildren<Text>();
                UIFactory.Fit(text, 8);

                into.Add((i, frame.Find("Fill").GetComponent<Image>(), text));
            }
        }

        RectTransform ToggleRow(string label, float y, float inner,
            UnityEngine.Events.UnityAction action, out Text valueLabel)
        {
            var frame = UIFactory.CreateBorderedPanel(_panel, "Toggle_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 30));

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var lamp = UIFactory.CreatePanel(frame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(8, 8));
            lamp.GetComponent<Image>().raycastTarget = false;

            var name = UIFactory.CreateText(frame, label, UiTheme.FontLabel, UiTheme.Text, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(26, 0),
                new Vector2(inner - 90f, 16));

            valueLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextDim, TextAnchor.MiddleRight);
            valueLabel.raycastTarget = false;
            UIFactory.Place(valueLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(70, 16));

            return lamp;
        }

        // ------------------------------------------------------------- state

        public void Show()
        {
            _panel.gameObject.SetActive(true);
            IsOpen = true;
            Opened?.Invoke();
            SyncFromSystem();
            Refresh();
        }

        /// <summary>
        /// Reads the live settings back off the system and lights the buttons
        /// that match.
        ///
        /// The panel cannot be the only record of what the line is drawn with:
        /// the system ships its own defaults and the editor's RESET puts them
        /// back without going through here, so a panel that only ever wrote
        /// would come up highlighting whatever it happened to be showing when it
        /// was last closed.
        /// </summary>
        void SyncFromSystem()
        {
            if (_front == null) return;
            _resolution = NearestIndex(Resolutions.Length, i => Resolutions[i].bands, _front.Resolution, _resolution);
            _smoothing = NearestIndex(Smoothings.Length, i => Smoothings[i].passes, _front.SmoothingPasses, _smoothing);
            _influence = NearestIndex(Influences.Length, i => Influences[i].km, _front.InfluenceWidthKm, _influence);
        }

        /// <summary>Index whose value matches <paramref name="value"/>, or <paramref name="fallback"/>.</summary>
        static int NearestIndex(int count, Func<int, float> valueAt, float value, int fallback)
        {
            for (int i = 0; i < count; i++)
                if (Mathf.Abs(valueAt(i) - value) < 0.001f) return i;
            return fallback;
        }

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
            IsOpen = false;
        }

        public void Toggle()
        {
            if (IsOpen) Hide(); else Show();
        }

        void ApplyStyle()
        {
            _front.SetStyle(Colours[_colour].hex, Widths[_width].metres);
            Refresh();
        }

        void Refresh()
        {
            foreach (var (i, border) in _colourButtons)
                border.color = i == _colour ? UiTheme.Accent : SwatchBorder;

            Paint(_widthButtons, _width);
            Paint(_resButtons, _resolution);
            Paint(_smoothButtons, _smoothing);
            Paint(_infButtons, _influence);

            _autoLamp.GetComponent<Image>().color = _front.AutoUpdate ? UiTheme.Success : UiTheme.TextFaint;
            _autoLabel.text = _front.AutoUpdate ? "LIVE" : "FROZEN";

            _visibleLamp.GetComponent<Image>().color = _front.Visible ? UiTheme.Success : UiTheme.TextFaint;
            _visibleLabel.text = _front.Visible ? "SHOWN" : "HIDDEN";

            for (int i = 0; i < _modeButtons.Count; i++)
            {
                bool on = Modes[_modeButtons[i].index].mode == _front.Mode;
                _modeButtons[i].fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                _modeButtons[i].label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
            if (_drawBtn != null) _drawBtn.interactable = _front.Mode != FlotMode.Automatic;

            RefreshReadout();
        }

        static void Paint(List<(int index, Image fill, Text label)> buttons, int active)
        {
            foreach (var (i, fill, label) in buttons)
            {
                bool on = i == active;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        void RefreshReadout()
        {
            if (_readout == null || _front == null) return;

            if (!string.IsNullOrEmpty(_front.LastFailure))
            {
                _readout.text = "No front line";
                _readout.color = UiTheme.TextDim;
                _statusLabel.text = _front.LastFailure;
                _statusLabel.color = UiTheme.Warning;
                return;
            }

            _readout.text = $"{_front.LengthKm:0.#} km of friendly FLOT  ·  {_front.Segments.Count} segment(s)";
            _readout.color = UiTheme.Text;

            double moved = _front.MovementSinceKm();
            _statusLabel.text =
                $"{_front.BlueCount} friendly / {_front.RedCount} hostile formations eligible" +
                (moved > 0.05 ? $"  ·  moved {moved:0.#} km" : "");
            _statusLabel.color = UiTheme.TextFaint;

            // One clause per stretch: "FRIENDLY ADVANCING · ENEMY RETREATING ·
            // POCKET ISOLATED". The states are compared solve to solve, so
            // this line is where the battle's direction is read.
            var sb = new System.Text.StringBuilder();
            foreach (var seg in _front.Segments)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(seg.Pocket ? "POCKET"
                    : seg.Manual ? "DRAWN"
                    : seg.Team == Team.User ? "FRIENDLY" : "ENEMY");
                sb.Append(' ').Append(seg.State.ToString().ToUpperInvariant());
                if (seg.Estimated) sb.Append(" (EST)");
            }
            if (_segmentsLabel != null) _segmentsLabel.text = sb.ToString();
        }

        /// <summary>
        /// Moves the panel's top edge, so it can clear whatever is docked above
        /// it on this edge — the fire-menu cluster always, and the minimap too
        /// once a battle starts. One caller decides for all of them; see
        /// <c>GameController.RefreshRightDockTop</c>.
        /// </summary>
        public void SetTopInset(float pixels)
        {
            if (_panel != null) _panel.offsetMax = new Vector2(0, -pixels);
        }
    }
}
