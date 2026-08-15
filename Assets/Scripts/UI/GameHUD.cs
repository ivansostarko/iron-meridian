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
        /// <summary>Raised by the RESET button; the controller owns what "reset" means.</summary>
        public System.Action ResetRequested;

        CombatSystem _combat;
        GameClock _clock;

        Button _battleBtn, _pauseBtn;
        Image _battleFill, _battleGlyph;
        Text _battleLabel;
        Text _status, _modeLabel;
        RectTransform _modeChip;
        Image _modeChipFill;

        /// <summary>
        /// The bar itself and the editor furniture on it. Held so mission mode
        /// can take them off the screen — see <see cref="SetMissionMode"/>.
        /// </summary>
        RectTransform _bar, _identity, _resetFrame;
        Text _help;

        RectTransform _clockPanel;
        Text _clockDate, _clockTime, _clockSpeed;

        /// <summary>
        /// What the bar's identity block says. "MAP EDITOR" by default; a
        /// mission replaces it with its own name, because the same scene does
        /// both jobs and the bar is where the player finds out which.
        /// </summary>
        Text _title;

        /// <summary>
        /// Where the emblem block goes. The main menu from the editor, and the
        /// campaign browser from a mission — a player who came in through
        /// SINGLE PLAYER should come out there.
        /// </summary>
        public string HomeScene = GameConfig.SceneMainMenu;

        /// <summary>Renames the identity block. Safe before or after Build.</summary>
        public void SetTitle(string title)
        {
            if (_title != null) _title.text = title;
        }

        public void Build(Canvas canvas, CombatSystem combat, GameClock clock)
        {
            _combat = combat;
            _clock = clock;

            var bar = UIFactory.CreatePanel(canvas.transform, "TopBar", UiTheme.Chrome);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(0, -UiTheme.TopBarHeight);
            bar.offsetMax = Vector2.zero;
            _bar = bar;

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

            BuildCountdown(canvas);
            BuildAlert(canvas);

            _help = UIFactory.CreateText(canvas.transform,
                "LMB select   •   RMB place / move   •   C face   •   Ctrl+C/V copy-paste   •   Ctrl+Z undo   •   WASD pan   •   Wheel zoom   •   Q/E rotate   •   Esc/P pause",
                UiTheme.FontSmall, UiTheme.TextFaint);
            UIFactory.Place(_help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 10), new Vector2(1180, 22));
        }

        /// <summary>
        /// Strips the bar down to the operational clock for a single-player
        /// mission, leaving the map and the timer.
        ///
        /// **Why the editor's furniture goes.** A mission is a fight on a piece
        /// of ground somebody else laid out. The identity block, the mode chip,
        /// RESET, the battle control and the editor key list are all about
        /// *authoring* a scenario, and every one of them is either useless or
        /// destructive once the scenario is the thing being played.
        ///
        /// **What stays, and why it has to.** The clock — the mission's timer.
        /// The flash line, the strike countdown and the alert banner: those are
        /// gameplay feedback, not chrome, and a strike with no countdown is a
        /// strike the player cannot time. Esc still opens the pause menu, which
        /// is the way out now that the emblem's home button is gone.
        /// </summary>
        public void SetMissionMode(bool on)
        {
            if (_identity != null) _identity.gameObject.SetActive(!on);
            if (_modeChip != null) _modeChip.gameObject.SetActive(!on);
            if (_resetFrame != null) _resetFrame.gameObject.SetActive(!on);
            if (_battleBtn != null) _battleBtn.gameObject.SetActive(!on);
            if (_help != null) _help.gameObject.SetActive(!on);

            // The clock takes the space the battle control and RESET have
            // vacated, so it sits against the right edge rather than floating
            // 334 px short of it with nothing to its right.
            if (_clockPanel != null)
                _clockPanel.anchoredPosition = new Vector2(on ? -18f : -334f, 0f);

            // The bar stays: it is what the clock is mounted on, and a clock
            // floating on the terrain with nothing behind it is unreadable over
            // snow, desert or a lit city.
            if (_bar != null) _bar.GetComponent<Image>().color =
                on ? new Color(UiTheme.Chrome.r, UiTheme.Chrome.g, UiTheme.Chrome.b, 0.72f) : UiTheme.Chrome;
        }

        /// <summary>Emblem + screen name, the anchor of the whole bar.</summary>
        void BuildIdentity(RectTransform bar)
        {
            // One container for the emblem, the title and the invisible home
            // button over them, so mission mode can take the whole block off in
            // a single call rather than remembering three pieces.
            _identity = UIFactory.CreateGroup(bar, "Identity");
            UIFactory.Stretch(_identity);
            bar = _identity;

            var emblem = UIFactory.CreateImage(bar, UiIcons.Shield, "Emblem");
            emblem.color = UiTheme.Accent;
            emblem.raycastTarget = false;
            UIFactory.Place((RectTransform)emblem.transform, new Vector2(0f, 0.5f),
                new Vector2(18, 0), new Vector2(30, 30));
            ((RectTransform)emblem.transform).pivot = new Vector2(0, 0.5f);

            _title = UIFactory.CreateText(bar, "MAP EDITOR", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0f, 0.5f), new Vector2(58, 0), new Vector2(210, 34));
            UIFactory.Fit(_title, 12);

            // Clicking the emblem block leaves the editor — the same affordance
            // as a logo in a web app, and it frees bar space for real controls.
            var home = UIFactory.CreateButton(bar, "", () => SceneManager.LoadScene(HomeScene),
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
            // RESET replaces the settings shortcut that used to sit here. Video
            // and audio options belong to the main menu, and a one-click hop out
            // of the editor to a different scene was a trap next to the battle
            // control; putting the scenario reset in that slot gives the editor
            // the one destructive action it was missing.
            var reset = UIFactory.CreateBorderedPanel(bar, "ResetButton", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(reset, new Vector2(1f, 0.5f), new Vector2(-18, 0), new Vector2(96, 40));
            reset.pivot = new Vector2(1f, 0.5f);
            _resetFrame = reset;

            var resetBtn = UIFactory.CreateButton(reset, "RESET", () => ResetRequested?.Invoke(),
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)resetBtn.transform);
            UiTooltip.Attach(resetBtn.gameObject,
                "Reset the editor — reload the scenario and put every setting back to its default",
                UiTooltip.Side.Below);

            _battleBtn = UIFactory.CreateButton(bar, "", () => _combat.Toggle(), UiTheme.Success, UiTheme.Text, 1);
            var brt = (RectTransform)_battleBtn.transform;
            // Right-to-left along the bar: RESET (96) · gap · BATTLE (196) · gap · CLOCK (320).
            UIFactory.Place(brt, new Vector2(1f, 0.5f), new Vector2(-126, 0), new Vector2(196, 40));
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

        /// <summary>
        /// Operational clock, left of the battle button. Game mode only.
        ///
        /// Time and date sit side by side on one line rather than stacked: the
        /// bar is 40 px tall, and two lines of type inside it left each of them
        /// too small to read at a glance. Time leads on the left at full size
        /// because it is what the player checks constantly; the date trails on
        /// the right, dimmed, because it rarely changes mid-battle.
        /// </summary>
        void BuildClock(RectTransform bar)
        {
            _clockPanel = UIFactory.CreateBorderedPanel(bar, "GameClock", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(_clockPanel, new Vector2(1f, 0.5f), new Vector2(-334, 0), new Vector2(320, 40));
            _clockPanel.pivot = new Vector2(1f, 0.5f);

            _clockTime = UIFactory.CreateText(_clockPanel, "--:--", 19, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_clockTime.rectTransform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(60, 26));

            // Right-aligned in its own slot, so the date's right edge stays put
            // as the day/month digits change width.
            _clockDate = UIFactory.CreateText(_clockPanel, "--.--.----", UiTheme.FontSmall, UiTheme.TextDim,
                TextAnchor.MiddleRight);
            UIFactory.Place(_clockDate.rectTransform, new Vector2(0f, 0.5f), new Vector2(74, 0), new Vector2(90, 20));

            var sep = UIFactory.CreatePanel(_clockPanel, "Separator", UiTheme.Border);
            UIFactory.Place(sep, new Vector2(0f, 0.5f), new Vector2(174, 0), new Vector2(1, 22));
            sep.GetComponent<Image>().raycastTarget = false;

            _clockSpeed = UIFactory.CreateText(_clockPanel, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            // Wide enough for "PAUSED" and for the top rate, which is now x300
            // rather than x8 — see GameClock, where x1 became real time.
            UIFactory.Place(_clockSpeed.rectTransform, new Vector2(0f, 0.5f), new Vector2(178, 0), new Vector2(56, 20));
            UIFactory.Fit(_clockSpeed, 9);

            ClockBtn(-8, "»", _clock.Faster, "Speed up");
            _pauseBtn = ClockBtn(-36, "❚❚", _clock.TogglePause, "Pause / resume");
            ClockBtn(-64, "«", _clock.Slower, "Slow down");

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
            TickAlert();

            // Driven by panel visibility rather than by Running, so the readout
            // is correct the instant battle starts — including while paused at
            // speed 0, when the clock is not advancing.
            if (_clock == null || _clockDate == null) return;
            if (_clockPanel == null || !_clockPanel.gameObject.activeSelf) return;
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

        // ------------------------------------------------------------- alerts

        RectTransform _alert;
        Image _alertFill, _alertGlyph;
        Text _alertText;
        CanvasGroup _alertGroup;
        float _alertRemaining;

        /// <summary>Seconds the banner spends fading out at the end of its life.</summary>
        const float AlertFadeSeconds = 0.5f;

        /// <summary>
        /// A transient banner for things that have gone wrong outside the game's
        /// control — losing the network being the one that matters, because the
        /// map is streamed and stops filling in without it.
        ///
        /// Bottom-centre rather than under the top bar, which is already carrying
        /// the status flash and the fire-mission countdown. An alert that covers
        /// a running countdown trades one piece of urgent information for
        /// another; down here it competes with nothing.
        /// </summary>
        void BuildAlert(Canvas canvas)
        {
            _alert = UIFactory.CreateBorderedPanel(canvas.transform, "Alert",
                UiTheme.Chrome, UiTheme.Warning);
            UIFactory.Place(_alert, new Vector2(0.5f, 0f), new Vector2(0, 56), new Vector2(660, 46));
            _alertFill = _alert.Find("Fill").GetComponent<Image>();

            _alertGroup = _alert.gameObject.AddComponent<CanvasGroup>();
            // Never intercept a click: it appears unbidden over the map.
            _alertGroup.blocksRaycasts = false;
            _alertGroup.interactable = false;

            _alertGlyph = UIFactory.CreateImage(_alert, UiIcons.Info, "Glyph");
            _alertGlyph.raycastTarget = false;
            UIFactory.Place((RectTransform)_alertGlyph.transform, new Vector2(0f, 0.5f),
                new Vector2(16, 0), new Vector2(20, 20));

            _alertText = UIFactory.CreateText(_alert, "", UiTheme.FontBody, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            _alertText.raycastTarget = false;
            UIFactory.Place(_alertText.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(46, 0), new Vector2(600, 30));
            UIFactory.Fit(_alertText, 11);

            _alert.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows an alert for <paramref name="seconds"/>, then fades it out.
        /// Calling again while one is up replaces it and restarts the clock,
        /// so a burst of related problems reads as one message rather than a
        /// queue the player has to wait out.
        /// </summary>
        public void ShowAlert(string message, float seconds = 5f, bool warning = true)
        {
            if (_alert == null) return;

            _alertText.text = message;
            var accent = warning ? UiTheme.Warning : UiTheme.Success;
            _alert.GetComponent<Image>().color = accent;
            _alertGlyph.color = accent;
            _alertFill.color = UiTheme.Chrome;

            _alertRemaining = Mathf.Max(0.1f, seconds);
            _alertGroup.alpha = 1f;
            _alert.gameObject.SetActive(true);
        }

        void TickAlert()
        {
            if (_alert == null || !_alert.gameObject.activeSelf) return;

            // Unscaled: an alert about the network must not be frozen by a
            // paused battle, which is exactly when the player is most likely to
            // be sitting still and reading.
            _alertRemaining -= Time.unscaledDeltaTime;

            if (_alertRemaining <= 0f)
            {
                _alert.gameObject.SetActive(false);
                return;
            }

            _alertGroup.alpha = Mathf.Clamp01(_alertRemaining / AlertFadeSeconds);
        }

        // ------------------------------------------------------- fire mission

        RectTransform _countdown;
        Image _countdownFill, _countdownBar;
        Text _countdownTitle, _countdownSeconds, _countdownCaption;

        /// <summary>Width of the countdown banner's depleting bar.</summary>
        const float CountdownBarWidth = 300f;

        /// <summary>
        /// The fire-mission countdown, hidden until something is in the air.
        ///
        /// It sits below the flash line rather than replacing it: the flash says
        /// what was just ordered, this says how long until it arrives, and
        /// during a fire mission the player wants both. Centred over the map
        /// because it is the one thing on screen that cannot be acted on — there
        /// is no button here, only a clock running down.
        /// </summary>
        void BuildCountdown(Canvas canvas)
        {
            _countdown = UIFactory.CreateBorderedPanel(canvas.transform, "FireMission",
                UiTheme.Chrome, UiTheme.BorderStrong);
            UIFactory.Place(_countdown, new Vector2(0.5f, 1f),
                new Vector2(0, -UiTheme.TopBarHeight - 44), new Vector2(340, 84));
            _countdownFill = _countdown.Find("Fill").GetComponent<Image>();

            _countdownCaption = UIFactory.CreateText(_countdown, "FIRE MISSION", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_countdownCaption.rectTransform, new Vector2(0f, 1f),
                new Vector2(14, -10), new Vector2(200, 14));

            _countdownTitle = UIFactory.CreateText(_countdown, "", UiTheme.FontBody,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_countdownTitle.rectTransform, new Vector2(0f, 1f),
                new Vector2(14, -28), new Vector2(232, 20));

            // The seconds are the point of the banner, so they are the largest
            // thing in it and sit on their own on the right.
            _countdownSeconds = UIFactory.CreateText(_countdown, "", 34,
                UiTheme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_countdownSeconds.rectTransform, new Vector2(1f, 1f),
                new Vector2(-14, -22), new Vector2(80, 40));

            // Depleting bar: the same information again as a shape, which is
            // what the eye reads at a glance while it is busy watching the map.
            var track = UIFactory.CreatePanel(_countdown, "Track", UiTheme.Surface);
            UIFactory.Place(track, new Vector2(0.5f, 0f), new Vector2(0, 16),
                new Vector2(CountdownBarWidth, 6));

            _countdownBar = UIFactory.CreatePanel(track, "Bar", UiTheme.Accent).GetComponent<Image>();
            var barRect = (RectTransform)_countdownBar.transform;
            barRect.anchorMin = new Vector2(0, 0); barRect.anchorMax = new Vector2(0, 1);
            barRect.pivot = new Vector2(0, 0.5f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(CountdownBarWidth, 0);

            _countdown.gameObject.SetActive(false);
        }

        /// <summary>
        /// Drives the countdown banner. Passing a null <paramref name="title"/>
        /// hides it — the caller does not have to track whether it is showing.
        /// </summary>
        public void SetFireMission(string title, float remaining, float total, Color accent)
        {
            if (_countdown == null) return;

            if (string.IsNullOrEmpty(title) || total <= 0f)
            {
                if (_countdown.gameObject.activeSelf) _countdown.gameObject.SetActive(false);
                return;
            }

            if (!_countdown.gameObject.activeSelf) _countdown.gameObject.SetActive(true);

            float t01 = Mathf.Clamp01(remaining / total);
            _countdownTitle.text = title;
            // Ceiling, so the banner reads "01" for the whole final second and
            // only ever shows "00" when the rounds are actually landing.
            _countdownSeconds.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString("00");

            ((RectTransform)_countdownBar.transform).sizeDelta =
                new Vector2(CountdownBarWidth * t01, 0);

            // Everything heats toward danger as the clock runs out, matching the
            // target marker on the map so the two read as one event.
            var hot = Color.Lerp(accent, UiTheme.Hostile, 1f - t01);
            _countdownBar.color = hot;
            _countdownSeconds.color = hot;
            _countdownCaption.color = Color.Lerp(UiTheme.TextFaint, hot, 1f - t01);

            // A slow pulse on the panel itself in the last three seconds.
            float urgency = remaining <= 3f
                ? (Mathf.Sin(Time.unscaledTime * 9f) + 1f) * 0.5f
                : 0f;
            _countdownFill.color = Color.Lerp(UiTheme.Chrome, new Color(0.35f, 0.09f, 0.07f, 0.98f), urgency * 0.8f);
        }
    }
}
