using System;
using UnityEngine;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// Modal for setting the scenario's H-hour: five stepper rows (day, month,
    /// year, hour, minute) over a live preview, with Cancel and Apply.
    ///
    /// Steppers rather than text fields on purpose — every reachable state is a
    /// valid date. A typed "31/02/1990" has to be parsed, rejected and
    /// explained; a stepper simply cannot produce it, because the day clamps to
    /// the length of the selected month as soon as the month or year moves.
    /// </summary>
    public class DateTimeDialog : MonoBehaviour
    {
        /// <summary>True while the modal is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        const float PanelW = 420f;
        const float PanelH = 366f;
        const float RowH = 38f;
        /// <summary>Minutes move in fives — finer than an operational order needs.</summary>
        const int MinuteStep = 5;

        static DateTimeDialog _active;

        Action<DateTime> _onApply;
        Text _preview;
        Text _dayValue, _monthValue, _yearValue, _hourValue, _minuteValue;
        int _year, _month, _day, _hour, _minute;

        static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        /// <summary>Opens the modal, replacing one already on screen.</summary>
        public static DateTimeDialog Open(Canvas canvas, DateTime initial, Action<DateTime> onApply)
        {
            if (_active != null) Destroy(_active.gameObject);

            var root = UIFactory.CreateGroup(canvas.transform, "DateTimeDialog");
            UIFactory.Stretch(root);
            root.SetAsLastSibling();

            var dialog = root.gameObject.AddComponent<DateTimeDialog>();
            dialog._onApply = onApply;
            dialog.Build(root, initial);

            _active = dialog;
            IsOpen = true;
            return dialog;
        }

        void Build(RectTransform root, DateTime initial)
        {
            _year = initial.Year; _month = initial.Month; _day = initial.Day;
            _hour = initial.Hour; _minute = initial.Minute;

            // Scrim: dims the map and, being a raycast target, stops clicks
            // reaching the UI behind the modal.
            var scrim = UIFactory.CreatePanel(root, "Scrim", new Color(0f, 0f, 0f, 0.62f));
            UIFactory.Stretch(scrim);

            var panel = UIFactory.CreateBorderedPanel(root, "Panel", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));

            var icon = UIFactory.CreateImage(panel, UiIcons.Clock, "Icon");
            icon.color = UiTheme.Accent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 1f), new Vector2(20, -18), new Vector2(18, 18));

            var title = UIFactory.CreateText(panel, "SCENARIO START", UiTheme.FontHeading, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(46, -17), new Vector2(300, 22));

            var close = UIFactory.CreateButton(panel, "✕", Cancel, new Color(0, 0, 0, 0), UiTheme.TextDim, 16);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-8, -8), new Vector2(30, 30));

            var rule = UIFactory.CreateDivider(panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -48);

            float y = -60f;
            _dayValue = Stepper(panel, "Day", ref y, d => { _day += d; Normalise(); });
            _monthValue = Stepper(panel, "Month", ref y, d => { _month += d; Normalise(); });
            _yearValue = Stepper(panel, "Year", ref y, d => { _year += d; Normalise(); });
            _hourValue = Stepper(panel, "Hour", ref y, d => { _hour += d; Normalise(); });
            _minuteValue = Stepper(panel, "Minute", ref y, d => { _minute += d * MinuteStep; Normalise(); });

            _preview = UIFactory.CreateText(panel, "", UiTheme.FontHeading, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_preview.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 66), new Vector2(PanelW - 40, 24));

            var cancelFrame = UIFactory.CreateBorderedPanel(panel, "CancelFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(cancelFrame, new Vector2(0f, 0f), new Vector2(20, 18), new Vector2(184, 38));
            var cancelBtn = UIFactory.CreateButton(cancelFrame, "CANCEL", Cancel,
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontBody);
            UIFactory.Stretch((RectTransform)cancelBtn.transform);

            var applyBtn = UIFactory.CreateButton(panel, "APPLY", Apply,
                UiTheme.Accent, Color.white, UiTheme.FontBody);
            UIFactory.Place((RectTransform)applyBtn.transform, new Vector2(1f, 0f), new Vector2(-20, 18), new Vector2(184, 38));
            ((RectTransform)applyBtn.transform).pivot = new Vector2(1f, 0f);

            Normalise();
        }

        /// <summary>One "‹ label   value ›" row.</summary>
        Text Stepper(RectTransform panel, string label, ref float y, Action<int> step)
        {
            var row = UIFactory.CreateGroup(panel, "Row_" + label);
            UIFactory.Place(row, new Vector2(0f, 1f), new Vector2(0, y), new Vector2(PanelW, RowH));

            var text = UIFactory.CreateText(row, label, UiTheme.FontBody, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(120, 20));

            var down = UIFactory.CreateBorderedPanel(row, "Down", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(down, new Vector2(0f, 0.5f), new Vector2(150, 0), new Vector2(34, 28));
            var downBtn = UIFactory.CreateButton(down, "◄", () => step(-1), new Color(0, 0, 0, 0), UiTheme.TextDim, 13);
            UIFactory.Stretch((RectTransform)downBtn.transform);

            var value = UIFactory.CreateText(row, "", UiTheme.FontBody, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(190, 0), new Vector2(160, 20));

            var up = UIFactory.CreateBorderedPanel(row, "Up", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(up, new Vector2(1f, 0.5f), new Vector2(-20, 0), new Vector2(34, 28));
            up.pivot = new Vector2(1f, 0.5f);
            var upBtn = UIFactory.CreateButton(up, "►", () => step(1), new Color(0, 0, 0, 0), UiTheme.TextDim, 13);
            UIFactory.Stretch((RectTransform)upBtn.transform);

            y -= RowH;
            return value;
        }

        /// <summary>
        /// Wraps every field into range and clamps the day to the selected
        /// month's length, so the dialog can never hold an impossible date.
        /// </summary>
        void Normalise()
        {
            _year = Mathf.Clamp(_year, 1900, 2100);
            _month = Wrap(_month - 1, 12) + 1;
            _hour = Wrap(_hour, 24);
            _minute = Wrap(_minute, 60);
            // Snap to the step grid: rounding keeps the value stable when the
            // starting minute was off-grid (a preset or a loaded save).
            _minute = Mathf.RoundToInt(_minute / (float)MinuteStep) * MinuteStep % 60;

            int daysInMonth = DateTime.DaysInMonth(_year, _month);
            _day = Wrap(_day - 1, daysInMonth) + 1;

            _dayValue.text = _day.ToString("00");
            _monthValue.text = $"{_month:00}  {MonthNames[_month - 1]}";
            _yearValue.text = _year.ToString();
            _hourValue.text = _hour.ToString("00");
            _minuteValue.text = _minute.ToString("00");
            _preview.text = Current.ToString("HH:mm  ·  dddd dd MMMM yyyy");
        }

        static int Wrap(int value, int range) => ((value % range) + range) % range;

        DateTime Current => new DateTime(_year, _month, _day, _hour, _minute, 0);

        void Apply()
        {
            _onApply?.Invoke(Current);
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
            // Covers the scene being torn down while the modal is up.
            if (_active == this) { _active = null; IsOpen = false; }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Apply();
        }
    }
}
