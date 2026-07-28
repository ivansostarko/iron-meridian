using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Top command bar of the game screen: navigation and battle control.
    /// Save/Load live in the pause menu (Esc/P) rather than here.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        CombatSystem _combat;
        GameClock _clock;

        Button _battleBtn, _pauseBtn;
        Text _status, _modeLabel;
        RectTransform _modeChip;

        RectTransform _clockPanel;
        Text _clockDate, _clockTime, _clockSpeed;

        static readonly Color EditorModeColor = new Color(0.30f, 0.36f, 0.46f);
        static readonly Color GameModeColor = new Color(0.16f, 0.45f, 0.20f);

        public void Build(Canvas canvas, CombatSystem combat, GameClock clock)
        {
            _combat = combat;
            _clock = clock;

            var bar = UIFactory.CreatePanel(canvas.transform, "TopBar", GameConfig.UiPanel);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(0, -50);
            bar.offsetMax = Vector2.zero;

            float x = 8;
            Btn(bar, ref x, 96, "< MENU", () => SceneManager.LoadScene(GameConfig.SceneMainMenu));

            var title = UIFactory.CreateText(bar, "MAP EDITOR", 18, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(x + 8, 0), new Vector2(150, 38));
            x += 162;

            // Mode chip: which set of rules the right-click/movement behaviour
            // is currently following.
            _modeChip = UIFactory.CreatePanel(bar, "ModeChip", EditorModeColor);
            UIFactory.Place(_modeChip, new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(132, 28));
            _modeChip.pivot = new Vector2(0, 0.5f);

            _modeLabel = UIFactory.CreateText(_modeChip, "EDITOR MODE", 13, GameConfig.UiText,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_modeLabel.rectTransform);

            _battleBtn = UIFactory.CreateButton(bar, "▶ START BATTLE", () => _combat.Toggle(),
                new Color(0.16f, 0.45f, 0.2f), GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)_battleBtn.transform, new Vector2(1f, 0.5f), new Vector2(-8, 0), new Vector2(160, 38));

            BuildClock(bar);

            _status = UIFactory.CreateText(canvas.transform, "", 15, GameConfig.UiAccent);
            UIFactory.Place(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -68), new Vector2(900, 26));

            _combat.RunningChanged += OnBattleChanged;

            var help = UIFactory.CreateText(canvas.transform,
                "LMB select   •   RMB place / move   •   C face   •   Ctrl+C/V copy-paste   •   Ctrl+Z undo   •   WASD pan   •   Wheel zoom   •   Q/E rotate   •   Esc/P pause",
                13, GameConfig.UiTextDim);
            UIFactory.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(1400, 24));
        }

        /// <summary>
        /// Operational clock, pinned to the right of the top bar just inside
        /// the battle button. Game mode only — hidden while editing.
        /// </summary>
        void BuildClock(RectTransform bar)
        {
            _clockPanel = UIFactory.CreatePanel(bar, "GameClock", GameConfig.UiPanelLight);
            UIFactory.Place(_clockPanel, new Vector2(1f, 0.5f), new Vector2(-176, 0), new Vector2(268, 40));

            _clockDate = UIFactory.CreateText(_clockPanel, "", 12, GameConfig.UiTextDim, TextAnchor.LowerLeft);
            UIFactory.Place(_clockDate.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 1), new Vector2(84, 17));

            _clockTime = UIFactory.CreateText(_clockPanel, "", 18, GameConfig.UiText, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(_clockTime.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, -1), new Vector2(84, 21));

            _clockSpeed = UIFactory.CreateText(_clockPanel, "", 12, GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_clockSpeed.rectTransform, new Vector2(0f, 0.5f), new Vector2(96, 0), new Vector2(56, 20));

            ClockBtn(-8, "»", _clock.Faster, "Speed up");
            _pauseBtn = ClockBtn(-40, "❚❚", _clock.TogglePause, "Pause / resume");
            ClockBtn(-72, "«", _clock.Slower, "Slow down");

            _clock.SpeedChanged += RefreshClockSpeed;
            _clockPanel.gameObject.SetActive(false);
        }

        Button ClockBtn(float x, string glyph, UnityEngine.Events.UnityAction action, string tooltip)
        {
            var b = UIFactory.CreateButton(_clockPanel, glyph, action, GameConfig.UiPanel, GameConfig.UiText, 13);
            b.name = "Clock_" + tooltip;
            UIFactory.Place((RectTransform)b.transform, new Vector2(1f, 0.5f), new Vector2(x, 0), new Vector2(28, 28));
            return b;
        }

        void RefreshClockSpeed()
        {
            if (_clockSpeed == null || _clock == null || _pauseBtn == null) return;
            _clockSpeed.text = _clock.SpeedText;
            _clockSpeed.color = _clock.Paused ? new Color(0.95f, 0.55f, 0.25f) : GameConfig.UiAccent;
            _pauseBtn.GetComponentInChildren<Text>(true).text = _clock.Paused ? "▶" : "❚❚";
        }

        void Update()
        {
            if (_clock == null || !_clock.Running || _clockDate == null) return;
            _clockDate.text = _clock.DateText;
            _clockTime.text = _clock.TimeText;
        }

        Button Btn(RectTransform bar, ref float x, float w, string label,
            UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(bar, label, action, GameConfig.UiPanelLight, null, 15);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(w, 38));
            ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
            x += w + 6;
            return b;
        }

        void OnBattleChanged(bool running)
        {
            _battleBtn.GetComponentInChildren<Text>(true).text = running ? "■ PAUSE BATTLE" : "▶ START BATTLE";
            _battleBtn.image.color = running
                ? new Color(0.55f, 0.32f, 0.12f) : GameModeColor;

            _modeLabel.text = running ? "GAME MODE" : "EDITOR MODE";
            _modeChip.GetComponent<Image>().color = running ? GameModeColor : EditorModeColor;

            if (_clockPanel != null) _clockPanel.gameObject.SetActive(running);
            if (running) RefreshClockSpeed();
        }

        public void Flash(string message) => _status.text = message;
    }
}
