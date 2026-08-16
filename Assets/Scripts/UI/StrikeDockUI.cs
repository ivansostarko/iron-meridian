using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// The fire menus, as a cluster of icons under the top bar's right-hand end
    /// and one panel that docks beneath them.
    ///
    /// **Why they left the left rail.** Artillery, air, UAV, missile and naval
    /// strikes were five of the rail's fifteen rows — a third of a list whose
    /// other ten rows are things you *set up* a scenario with. That is not what
    /// these are. They are the things you *do* during one, they are all the same
    /// verb (put explosives on a piece of ground), and mixing them into the
    /// authoring nav meant the rail read as a settings menu with weapons in it.
    /// Pulling them into their own cluster says what they have in common, gets
    /// them to one click from anywhere, and gives the rail back to the job it
    /// was doing.
    ///
    /// **Why top-right.** The left is where the scenario is built and the
    /// bottom is where the selected formation's own orders live
    /// (<see cref="UnitActionBarUI"/>). The top-right corner is the only piece
    /// of chrome that is never doing anything else, and it is where a player
    /// already looks for the state of the battle — the clock and the battle
    /// button are along that bar.
    ///
    /// **The cluster is battle-mode only** — see <see cref="SetBattleMode"/>.
    /// Calling for fire is something you do *during* a fight; in scenario mode
    /// there is no clock running, nothing moves between the call and the
    /// impact, and a strike laid on a static laydown is just a hole in a map
    /// that is still being drawn. Hiding the cluster there also gives the
    /// corner back to the editor and makes the mode switch legible: the fire
    /// menus and the minimap appear together the moment the battle starts.
    ///
    /// **Within battle it never hides.** Every other right-hand panel — the
    /// unit inspector, the group panel, the front-line options — begins *below*
    /// the icon strip (<see cref="UiTheme.StrikeDockHeight"/>) rather than
    /// under the top bar, so the fire menus can be reached with a formation
    /// selected. The panel itself shares the right edge with them, because two
    /// panels cannot occupy one strip of screen: opening a fire menu drops the
    /// selection, and selecting a formation closes the fire menu.
    ///
    /// One panel with five pages rather than five panels: only one can be open,
    /// they are all the same width, and a shared header means the title is the
    /// only thing that changes when you switch between them.
    /// </summary>
    public class StrikeDockUI : MonoBehaviour
    {
        /// <summary>The five ways of putting explosives on a piece of ground.</summary>
        public enum Menu { Artillery, AirStrike, UavStrike, Missiles, NavalStrike }

        /// <summary>True while a fire menu is showing, so the map's input guards can read it.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when a menu opens, so competing right-hand panels can stand down.</summary>
        public System.Action Opened;
        /// <summary>Raised when a menu closes, with which one — the system behind it stands down.</summary>
        public System.Action<Menu> Closed;
        /// <summary>Raised with the width the dock occupies on the right (0 when shut).</summary>
        public System.Action<float> RightInsetChanged;

        /// <summary>
        /// Panel width. Deliberately the same as the rail's section panel: the
        /// missile board used to be 46 px wider to give its comparison rows more
        /// room, and one panel in a set of five being a different width is a
        /// worse tell than any row is helped by the space.
        /// </summary>
        public const float PanelWidth = UiTheme.SectionPanelWidth;

        const float Pad = UiTheme.PanelPadding;
        const float HeaderHeight = 44f;
        const float ButtonSize = 44f, ButtonGap = 6f;

        static readonly (Menu menu, string title, string tip)[] Menus =
        {
            (Menu.Artillery,   "ARTILLERY STRIKE", "Artillery — call for fire"),
            (Menu.AirStrike,   "AIR STRIKE",       "Air strike — task an airframe"),
            (Menu.UavStrike,   "UAV STRIKES",      "UAV — task an unmanned sortie"),
            (Menu.Missiles,    "MISSILE SYSTEMS",  "Missile systems — ten launchers"),
            (Menu.NavalStrike, "NAVY STRIKE",      "Naval gunfire support")
        };

        static Sprite GlyphFor(Menu menu) => menu switch
        {
            Menu.Artillery => UiIcons.Artillery,
            Menu.AirStrike => UiIcons.FlyingWing,
            Menu.UavStrike => UiIcons.Quadcopter,
            Menu.Missiles => UiIcons.Interceptor,
            _ => UiIcons.Warship
        };

        RectTransform _panel, _cluster;
        Text _title;
        Menu _menu = Menu.Artillery;
        bool _open;

        readonly Dictionary<Menu, RectTransform> _pages = new Dictionary<Menu, RectTransform>();
        readonly List<(Menu menu, Image fill, Image glyph)> _buttons =
            new List<(Menu, Image, Image)>();

        // ------------------------------------------------------------- build

        public void Build(Canvas canvas)
        {
            BuildPanel(canvas);
            BuildCluster(canvas);
            Hide();
            ApplyClusterVisibility();
        }

        void BuildPanel(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "StrikeDock", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

            // Hairline down the inboard edge, where the panel meets the map.
            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            _title = UIFactory.CreateText(_panel, "", UiTheme.FontHeading, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -13), new Vector2(PanelWidth - Pad - 44f, 22));
            UIFactory.Fit(_title);

            var close = UIFactory.CreateIconButton(_panel, UiIcons.Close, Hide,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 8f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-Pad + 4f, -8f), new Vector2(28, 28));

            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -HeaderHeight + 1f);

            var body = UIFactory.CreateGroup(_panel, "Body");
            body.anchorMin = new Vector2(0, 0); body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(0, 0);
            body.offsetMax = new Vector2(0, -HeaderHeight);

            // One page per menu, all laid out at the same origin; only the
            // selected one is active. Whoever owns a menu's controls fills its
            // page through PageFor — this class owns the chrome, not the
            // contents, so a fire menu's builder stays where its data is.
            foreach (var (menu, _, _) in Menus)
            {
                var page = UIFactory.CreateGroup(body, "StrikePage_" + menu);
                UIFactory.Stretch(page);
                _pages[menu] = page;
            }
        }

        /// <summary>
        /// The icon strip. It sits *outside* the panel so it survives the panel
        /// being hidden, and above every other right-hand panel so those can
        /// never cover it.
        /// </summary>
        void BuildCluster(Canvas canvas)
        {
            float width = ButtonSize * Menus.Length + ButtonGap * (Menus.Length - 1);

            _cluster = UIFactory.CreatePanel(canvas.transform, "StrikeDockIcons", UiTheme.Chrome);
            UIFactory.Place(_cluster, new Vector2(1f, 1f),
                new Vector2(-Pad, -(UiTheme.TopBarHeight + 4f)),
                new Vector2(width + Pad * 2f, ButtonSize + 8f));

            var frame = UIFactory.CreatePanel(_cluster, "Border", UiTheme.Border);
            UIFactory.Stretch(frame);
            frame.GetComponent<Image>().raycastTarget = false;
            var fill = UIFactory.CreatePanel(frame, "Fill", UiTheme.Chrome);
            UIFactory.Stretch(fill);
            fill.offsetMin = new Vector2(1, 1); fill.offsetMax = new Vector2(-1, -1);
            fill.GetComponent<Image>().raycastTarget = false;

            for (int i = 0; i < Menus.Length; i++)
            {
                var (menu, _, tip) = Menus[i];
                var captured = menu;

                var button = UIFactory.CreateBorderedPanel(_cluster, "Strike_" + menu,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(button, new Vector2(0f, 0.5f),
                    new Vector2(Pad + i * (ButtonSize + ButtonGap), 0),
                    new Vector2(ButtonSize, ButtonSize));

                var btn = UIFactory.CreateIconButton(button, GlyphFor(menu),
                    () => Toggle(captured), new Color(0, 0, 0, 0), UiTheme.TextDim, 11f);
                UIFactory.Stretch((RectTransform)btn.transform);

                // Icon-only controls need a caption on hover or they are five
                // shapes the player has to click to identify.
                // Below, not beside: the strip is hard against the right edge,
                // and a caption to the right of the last icon would be off it.
                UiTooltip.Attach(btn.gameObject, tip, UiTooltip.Side.Below);

                _buttons.Add((menu, button.Find("Fill").GetComponent<Image>(),
                    btn.transform.Find("Glyph").GetComponent<Image>()));
            }

            // The strip gets its own sorting layer rather than relying on being
            // the last sibling. Sibling order is not defensible here: the unit
            // inspector calls SetAsLastSibling every time it is shown, so any
            // arrangement made at build time is undone the first time a
            // formation is clicked. A canvas with overrideSorting is the same
            // device LoadingScreenUI uses to guarantee it draws on top.
            var sorter = _cluster.gameObject.AddComponent<Canvas>();
            sorter.overrideSorting = true;
            sorter.sortingOrder = 50;
            _cluster.gameObject.AddComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// The page a menu's controls are built into. Called once per menu
        /// during construction, before anything is shown.
        /// </summary>
        public RectTransform PageFor(Menu menu) =>
            _pages.TryGetValue(menu, out var page) ? page : null;

        // ------------------------------------------------------------- state

        public void Show(Menu menu)
        {
            _menu = menu;
            _open = true;
            IsOpen = true;

            _panel.gameObject.SetActive(true);
            // Above the unit inspector and the group panel, which share this
            // edge and are built after it. The icon strip needs no such nudge —
            // it has a sorting layer of its own, see BuildCluster.
            _panel.SetAsLastSibling();

            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == menu);
            foreach (var (m, title, _) in Menus) if (m == menu) _title.text = title;

            Paint();
            Opened?.Invoke();
            RightInsetChanged?.Invoke(PanelWidth);
        }

        public void Hide()
        {
            if (!_open)
            {
                // Still make sure the panel is down: Build() calls this before
                // anything has been opened.
                if (_panel != null) _panel.gameObject.SetActive(false);
                IsOpen = false;
                Paint();
                return;
            }

            _open = false;
            IsOpen = false;
            if (_panel != null) _panel.gameObject.SetActive(false);
            Paint();

            // Closing a fire menu stands its system down: leaving a launcher
            // armed behind a panel that is no longer on screen would turn the
            // next click on the map into a strike nobody asked for.
            Closed?.Invoke(_menu);
            RightInsetChanged?.Invoke(0f);
        }

        public void Toggle(Menu menu)
        {
            if (_open && _menu == menu) { Hide(); return; }

            // Switching menus stands the previous one's system down before the
            // new one arms anything.
            if (_open && _menu != menu) Closed?.Invoke(_menu);
            Show(menu);
        }

        void Paint()
        {
            foreach (var (menu, fill, glyph) in _buttons)
            {
                bool on = _open && menu == _menu;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                glyph.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        /// <summary>
        /// Takes the whole dock off the screen for a single-player mission,
        /// which is played on the map rather than authored on it. Matches
        /// <c>UnitPaletteUI.SetChromeVisible</c>.
        /// </summary>
        public void SetChromeVisible(bool visible)
        {
            _chromeVisible = visible;
            ApplyClusterVisibility();
        }

        /// <summary>
        /// Battle running or not. The fire menus belong to a battle — see the
        /// class remarks — so the cluster only exists while one is on, and any
        /// menu left open when the battle stops comes down with it (which also
        /// stands its weapon system down, through <see cref="Hide"/>).
        /// </summary>
        public void SetBattleMode(bool running)
        {
            if (_battleRunning == running) return;
            _battleRunning = running;
            ApplyClusterVisibility();
        }

        /// <summary>Both switches have to be on; either one closes an open menu.</summary>
        void ApplyClusterVisibility()
        {
            bool show = _chromeVisible && _battleRunning;
            if (!show) Hide();
            if (_cluster != null) _cluster.gameObject.SetActive(show);
        }

        bool _chromeVisible = true;
        /// <summary>
        /// False until a battle starts. The editor opens in scenario mode, so
        /// the cluster is built hidden rather than appearing for the frame
        /// before the controller gets to say otherwise.
        /// </summary>
        bool _battleRunning;

        void OnDestroy()
        {
            if (IsOpen) IsOpen = false;
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
