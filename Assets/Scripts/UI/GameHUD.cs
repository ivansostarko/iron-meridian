using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Top command bar: emblem and screen name on the left, mode chip beside
    /// them, and the battle control plus settings on the right. Save/Load live
    /// in the pause menu (Esc/P) rather than here.
    ///
    /// Styled from <see cref="UiTheme"/> — dark chrome, a hairline bottom
    /// border, and green reserved for the one "go" action on the screen.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        CombatSystem _combat;
        GameClock _clock;

        Button _battleBtn, _pauseBtn;
        Image _battleFill, _battleGlyph;
        Text _battleLabel;
        Text _status, _modeLabel;
        RectTransform _modeChip;
        Image _modeChipFill;

        RectTransform _clockPanel;
        Text _clockDate, _clockTime, _clockSpeed;

        public void Build(Canvas canvas, CombatSystem combat, GameClock clock)
        {
            _combat = combat;
            _clock = clock;

            var bar = UIFactory.CreatePanel(canvas.transform, "TopBar", UiTheme.Chrome);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(0, -UiTheme.TopBarHeight);
            bar.offsetMax = Vector2.zero;

            // Hairline under the bar: what separates chrome from map without a
            // heavy drop shadow.
            var rule = UIFactory.CreateDivider(bar, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.anchoredPosition = Vector2.zero;

            BuildIdentity(bar);
            BuildModeChip(bar);
            BuildRightControls(bar);
            BuildClock(bar);

            _combat.RunningChanged += OnBattleChanged;
            OnBattleChanged(_combat.Running);

            // Transient messages sit just under the bar, centred over the map.
            _status = UIFactory.CreateText(canvas.transform, "", UiTheme.FontBody, UiTheme.Accent);
            UIFactory.Place(_status.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -UiTheme.TopBarHeight - 14), new Vector2(900, 24));

            var help = UIFactory.CreateText(canvas.transform,
                "LMB select   •   RMB place / move   •   C face   •   Ctrl+C/V copy-paste   •   Ctrl+Z undo   •   WASD pan   •   Wheel zoom   •   Q/E rotate   •   Esc/P pause",
                UiTheme.FontSmall, UiTheme.TextFaint);
            UIFactory.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 10), new Vector2(1180, 22));
        }

        /// <summary>Emblem + screen name, the anchor of the whole bar.</summary>
        void BuildIdentity(RectTransform bar)
        {
            var emblem = UIFactory.CreateImage(bar, UiIcons.Shield, "Emblem");
            emblem.color = UiTheme.Accent;
            emblem.raycastTarget = false;
            UIFactory.Place((RectTransform)emblem.transform, new Vector2(0f, 0.5f),
                new Vector2(18, 0), new Vector2(30, 30));
            ((RectTransform)emblem.transform).pivot = new Vector2(0, 0.5f);

            var title = UIFactory.CreateText(bar, "MAP EDITOR", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(58, 0), new Vector2(210, 34));

            // Clicking the emblem block leaves the editor — the same affordance
            // as a logo in a web app, and it frees bar space for real controls.
            var home = UIFactory.CreateButton(bar, "", () => SceneManager.LoadScene(GameConfig.SceneMainMenu),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Place((RectTransform)home.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(250, 44));
            ((RectTransform)home.transform).pivot = new Vector2(0, 0.5f);
            ((RectTransform)home.transform).SetAsFirstSibling();
        }

        /// <summary>Which rule set right-click and movement are following.</summary>
        void BuildModeChip(RectTransform bar)
        {
            _modeChip = UIFactory.CreateBorderedPanel(bar, "ModeChip", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(_modeChip, new Vector2(0f, 0.5f), new Vector2(296, 0), new Vector2(160, 36));
            _modeChip.pivot = new Vector2(0, 0.5f);
            _modeChipFill = _modeChip.Find("Fill").GetComponent<Image>();

            _modeLabel = UIFactory.CreateText(_modeChip, "SCENARIO MODE", UiTheme.FontSmall,
                UiTheme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_modeLabel.rectTransform);
        }

        void BuildRightControls(RectTransform bar)
        {
            var settings = UIFactory.CreateBorderedPanel(bar, "SettingsButton", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(settings, new Vector2(1f, 0.5f), new Vector2(-18, 0), new Vector2(40, 40));
            settings.pivot = new Vector2(1f, 0.5f);
            var settingsBtn = UIFactory.CreateIconButton(settings, UiIcons.Gear,
                () => SceneManager.LoadScene(GameConfig.SceneSettings), new Color(0, 0, 0, 0), UiTheme.TextDim, 10f);
            UIFactory.Stretch((RectTransform)settingsBtn.transform);

            _battleBtn = UIFactory.CreateButton(bar, "", () => _combat.Toggle(), UiTheme.Success, UiTheme.Text, 1);
            var brt = (RectTransform)_battleBtn.transform;
            UIFactory.Place(brt, new Vector2(1f, 0.5f), new Vector2(-70, 0), new Vector2(196, 40));
            brt.pivot = new Vector2(1f, 0.5f);
            _battleFill = _battleBtn.GetComponent<Image>();

            // The factory's centred caption is replaced by an icon + label pair
            // so the play triangle sits tight against the text, as in the design.
            var caption = _battleBtn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            _battleGlyph = UIFactory.CreateImage(brt, UiIcons.Play, "BattleGlyph");
            _battleGlyph.color = UiTheme.Text;
            _battleGlyph.raycastTarget = false;
            UIFactory.Place((RectTransform)_battleGlyph.transform, new Vector2(0f, 0.5f), new Vector2(22, 0), new Vector2(15, 15));

            _battleLabel = UIFactory.CreateText(brt, "START BATTLE", UiTheme.FontHeading, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_battleLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(46, 0), new Vector2(150, 24));
        }

        /// <summary>Operational clock, left of the battle button. Game mode only.</summary>
        void BuildClock(RectTransform bar)
        {
            _clockPanel = UIFactory.CreateBorderedPanel(bar, "GameClock", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(_clockPanel, new Vector2(1f, 0.5f), new Vector2(-278, 0), new Vector2(262, 40));
            _clockPanel.pivot = new Vector2(1f, 0.5f);

            _clockDate = UIFactory.CreateText(_clockPanel, "", UiTheme.FontLabel, UiTheme.TextDim, TextAnchor.LowerLeft);
            UIFactory.Place(_clockDate.rectTransform, new Vector2(0f, 0.5f), new Vector2(12, 1), new Vector2(84, 16));

            _clockTime = UIFactory.CreateText(_clockPanel, "", UiTheme.FontHeading, UiTheme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_clockTime.rectTransform, new Vector2(0f, 0.5f), new Vector2(12, -1), new Vector2(84, 20));

            _clockSpeed = UIFactory.CreateText(_clockPanel, "", UiTheme.FontSmall, UiTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_clockSpeed.rectTransform, new Vector2(0f, 0.5f), new Vector2(100, 0), new Vector2(52, 20));

            ClockBtn(-10, "»", _clock.Faster, "Speed up");
            _pauseBtn = ClockBtn(-40, "❚❚", _clock.TogglePause, "Pause / resume");
            ClockBtn(-70, "«", _clock.Slower, "Slow down");

            _clock.SpeedChanged += RefreshClockSpeed;
            _clockPanel.gameObject.SetActive(false);
        }

        Button ClockBtn(float x, string glyph, UnityEngine.Events.UnityAction action, string tooltip)
        {
            var b = UIFactory.CreateButton(_clockPanel, glyph, action, UiTheme.Panel, UiTheme.TextDim, UiTheme.FontSmall);
            b.name = "Clock_" + tooltip;
            UIFactory.Place((RectTransform)b.transform, new Vector2(1f, 0.5f), new Vector2(x, 0), new Vector2(26, 26));
            return b;
        }

        void RefreshClockSpeed()
        {
            if (_clockSpeed == null || _clock == null || _pauseBtn == null) return;
            _clockSpeed.text = _clock.SpeedText;
            _clockSpeed.color = _clock.Paused ? UiTheme.Warning : UiTheme.Accent;
            _pauseBtn.GetComponentInChildren<Text>(true).text = _clock.Paused ? "▶" : "❚❚";
        }

        void Update()
        {
            if (_clock == null || !_clock.Running || _clockDate == null) return;
            _clockDate.text = _clock.DateText;
            _clockTime.text = _clock.TimeText;
        }

        void OnBattleChanged(bool running)
        {
            if (_battleLabel != null) _battleLabel.text = running ? "PAUSE BATTLE" : "START BATTLE";
            if (_battleFill != null) _battleFill.color = running ? UiTheme.Warning : UiTheme.Success;
            if (_battleGlyph != null) _battleGlyph.sprite = running ? UiIcons.PauseBars : UiIcons.Play;

            if (_modeLabel != null)
            {
                _modeLabel.text = running ? "BATTLE MODE" : "SCENARIO MODE";
                _modeLabel.color = running ? UiTheme.Success : UiTheme.TextDim;
            }
            if (_modeChipFill != null)
                _modeChipFill.color = running ? new Color(0.106f, 0.631f, 0.361f, 0.16f) : UiTheme.Surface;

            if (_clockPanel != null) _clockPanel.gameObject.SetActive(running);
            if (running) RefreshClockSpeed();
        }

        public void Flash(string message) => _status.text = message;
    }
}
