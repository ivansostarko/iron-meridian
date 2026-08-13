using System;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Lines;

namespace IronMeridian.UI
{
    /// <summary>
    /// Sets up a control measure before it is drawn: kind, owning side, colour,
    /// width, planned/actual and caption — then starts the draw tool with those
    /// settings.
    ///
    /// Everything is chosen up front rather than edited afterwards because the
    /// line's kind changes how it should be drawn on the ground (a rear
    /// boundary runs parallel to the front, a lateral one runs into it), so the
    /// player needs to have decided before placing the first vertex.
    /// </summary>
    public class BoundaryOptionsDialog : MonoBehaviour
    {
        /// <summary>True while the modal is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        const float PanelW = 480f;
        const float PanelH = 470f;
        const float RowH = 40f;

        static BoundaryOptionsDialog _active;

        /// <summary>
        /// The kinds worth offering, in the order a planner thinks about them.
        /// Legacy `Boundary` is omitted — it is what the auto front line uses
        /// and is not something to draw by hand.
        /// </summary>
        static readonly (LineKind kind, string name, string detail)[] Kinds =
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
        Action _onStart;

        int _kind, _colour, _width;
        int _team;              // 0 = none, 1 = friendly, 2 = enemy
        bool _planned;
        bool _draw3D = true;

        Text _kindValue, _kindDetail, _teamValue, _colourValue, _widthValue, _plannedValue, _clampValue;
        Image _colourSwatch;
        InputField _labelField;

        public static BoundaryOptionsDialog Open(Canvas canvas, LineDrawTool tool, Action onStart)
        {
            if (_active != null) Destroy(_active.gameObject);

            var root = UIFactory.CreateGroup(canvas.transform, "BoundaryOptionsDialog");
            UIFactory.Stretch(root);
            root.SetAsLastSibling();

            var dialog = root.gameObject.AddComponent<BoundaryOptionsDialog>();
            dialog._tool = tool;
            dialog._onStart = onStart;
            dialog.Build(root);

            _active = dialog;
            IsOpen = true;
            return dialog;
        }

        void Build(RectTransform root)
        {
            // Start from whatever the tool is currently set to, so re-opening
            // the dialog shows the last choice rather than resetting it.
            _kind = Mathf.Max(0, Array.FindIndex(Kinds, k => k.kind == _tool.PendingKind));
            _colour = Mathf.Max(0, Array.FindIndex(Colours, c => c.hex == _tool.PendingColorHex));
            _width = Mathf.Max(0, Array.FindIndex(Widths, w => Mathf.Approximately(w.metres, _tool.PendingWidth)));
            _team = _tool.PendingTeam.HasValue ? (_tool.PendingTeam.Value == Team.User ? 1 : 2) : 0;
            _planned = _tool.PendingPlanned;
            _draw3D = _tool.Draw3D;

            var scrim = UIFactory.CreatePanel(root, "Scrim", new Color(0f, 0f, 0f, 0.62f));
            UIFactory.Stretch(scrim);

            var panel = UIFactory.CreateBorderedPanel(root, "Panel", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));

            var icon = UIFactory.CreateImage(panel, UiIcons.Square, "Icon");
            icon.color = UiTheme.Accent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 1f), new Vector2(20, -18), new Vector2(18, 18));

            var title = UIFactory.CreateText(panel, "CONTROL MEASURE", UiTheme.FontHeading, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(46, -17), new Vector2(320, 22));

            var close = UIFactory.CreateButton(panel, "✕", Cancel, new Color(0, 0, 0, 0), UiTheme.TextDim, 16);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-8, -8), new Vector2(30, 30));

            var rule = UIFactory.CreateDivider(panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -48);

            float y = -58f;
            _kindValue = Stepper(panel, "Type", ref y, d => { _kind = Wrap(_kind + d, Kinds.Length); Refresh(); });

            // The kind's meaning matters more than its name — a planner picking
            // "rear boundary" needs to know it runs parallel to the front.
            _kindDetail = UIFactory.CreateText(panel, "", UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.Place(_kindDetail.rectTransform, new Vector2(0f, 1f), new Vector2(20, y + 4), new Vector2(PanelW - 40, 16));
            y -= 18f;

            _teamValue = Stepper(panel, "Owning side", ref y, d => { _team = Wrap(_team + d, 3); Refresh(); });
            _colourValue = Stepper(panel, "Colour", ref y, d => { _colour = Wrap(_colour + d, Colours.Length); Refresh(); });

            _colourSwatch = UIFactory.CreatePanel(panel, "Swatch", Color.white).GetComponent<Image>();
            UIFactory.Place((RectTransform)_colourSwatch.transform, new Vector2(0f, 1f),
                new Vector2(150, y + RowH + 12), new Vector2(14, 14));
            _colourSwatch.raycastTarget = false;

            _widthValue = Stepper(panel, "Width", ref y, d => { _width = Wrap(_width + d, Widths.Length); Refresh(); });
            _plannedValue = Stepper(panel, "Status", ref y, d => { _planned = !_planned; Refresh(); });
            _clampValue = Stepper(panel, "Terrain clamp", ref y, d => { _draw3D = !_draw3D; Refresh(); });

            var labelCaption = UIFactory.CreateText(panel, "Caption", UiTheme.FontBody, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(labelCaption.rectTransform, new Vector2(0f, 1f), new Vector2(20, y - 8), new Vector2(120, 20));

            var frame = UIFactory.CreateBorderedPanel(panel, "LabelFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(150, y - 4), new Vector2(PanelW - 172, 32));
            _labelField = UIFactory.CreateInputField(frame, "e.g. PL BLUE", UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)_labelField.transform);
            _labelField.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            _labelField.text = _tool.PendingLabel;

            var cancelFrame = UIFactory.CreateBorderedPanel(panel, "CancelFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(cancelFrame, new Vector2(0f, 0f), new Vector2(20, 18), new Vector2(210, 38));
            var cancelBtn = UIFactory.CreateButton(cancelFrame, "CANCEL", Cancel,
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontBody);
            UIFactory.Stretch((RectTransform)cancelBtn.transform);

            var startBtn = UIFactory.CreateButton(panel, "START DRAWING", StartDrawing,
                UiTheme.Accent, Color.white, UiTheme.FontBody);
            UIFactory.Place((RectTransform)startBtn.transform, new Vector2(1f, 0f), new Vector2(-20, 18), new Vector2(210, 38));
            ((RectTransform)startBtn.transform).pivot = new Vector2(1f, 0f);

            Refresh();
        }

        Text Stepper(RectTransform panel, string label, ref float y, Action<int> step)
        {
            var row = UIFactory.CreateGroup(panel, "Row_" + label);
            UIFactory.Place(row, new Vector2(0f, 1f), new Vector2(0, y), new Vector2(PanelW, RowH));

            var text = UIFactory.CreateText(row, label, UiTheme.FontBody, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(130, 20));

            var down = UIFactory.CreateBorderedPanel(row, "Down", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(down, new Vector2(0f, 0.5f), new Vector2(170, 0), new Vector2(32, 26));
            var downBtn = UIFactory.CreateButton(down, "◄", () => step(-1), new Color(0, 0, 0, 0), UiTheme.TextDim, 13);
            UIFactory.Stretch((RectTransform)downBtn.transform);

            var value = UIFactory.CreateText(row, "", UiTheme.FontBody, UiTheme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(208, 0), new Vector2(210, 20));

            var up = UIFactory.CreateBorderedPanel(row, "Up", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(up, new Vector2(1f, 0.5f), new Vector2(-20, 0), new Vector2(32, 26));
            up.pivot = new Vector2(1f, 0.5f);
            var upBtn = UIFactory.CreateButton(up, "►", () => step(1), new Color(0, 0, 0, 0), UiTheme.TextDim, 13);
            UIFactory.Stretch((RectTransform)upBtn.transform);

            y -= RowH;
            return value;
        }

        static int Wrap(int value, int range) => ((value % range) + range) % range;

        void Refresh()
        {
            _kindValue.text = Kinds[_kind].name;
            _kindDetail.text = Kinds[_kind].detail;
            _teamValue.text = _team == 0 ? "None (separates both)" : _team == 1 ? "Friendly (Blue)" : "Enemy (Red)";
            _colourValue.text = Colours[_colour].name;
            _widthValue.text = Widths[_width].metres > 0f ? $"{Widths[_width].name} ({Widths[_width].metres:0} m)" : "Default";
            _plannedValue.text = _planned ? "Planned (broken)" : "Actual (solid)";
            _clampValue.text = _draw3D ? "3D — follows terrain" : "2D — flat band";

            // "Doctrinal" has no hex; show the colour the kind and side imply.
            if (Colours[_colour].hex.Length > 0 &&
                ColorUtility.TryParseHtmlString(Colours[_colour].hex, out var c))
            {
                _colourSwatch.color = c;
                _colourSwatch.enabled = true;
            }
            else
            {
                _colourSwatch.enabled = false;
            }
        }

        void StartDrawing()
        {
            _tool.PendingKind = Kinds[_kind].kind;
            _tool.PendingTeam = _team == 0 ? (Team?)null : _team == 1 ? Team.User : Team.Enemy;
            _tool.PendingColorHex = Colours[_colour].hex;
            _tool.PendingWidth = Widths[_width].metres;
            _tool.PendingPlanned = _planned;
            _tool.PendingLabel = _labelField != null ? _labelField.text : "";
            _tool.Draw3D = _draw3D;

            _tool.StartDrawingStyled();
            _onStart?.Invoke();
            Close();
        }

        void Cancel() => Close();

        void Close()
        {
            IsOpen = false;
            if (_active == this) _active = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_active == this) { _active = null; IsOpen = false; }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }
    }
}
