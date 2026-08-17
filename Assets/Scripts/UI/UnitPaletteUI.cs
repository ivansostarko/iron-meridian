using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// The editor's left chrome, in two pieces:
    ///
    ///  • a narrow **rail** that is always there — emblem, the section nav
    ///    (General, Units, Effects, Weather Conditions, Map, Date and Time) and
    ///    the tool strip along the bottom;
    ///  • a **section panel** that slides out from behind the rail carrying the
    ///    controls for whichever section is open, and can be closed.
    ///
    /// Splitting them is what lets the map breathe: the sections are deep — the
    /// weather and map ones run past 450 px — and a single fixed panel meant all
    /// that depth was permanently parked over the terrain whether the player was
    /// using it or not. Clicking the open section's nav row closes the panel, as
    /// does the ✕ in its header, leaving only the rail.
    ///
    /// The rail is what other on-map chrome measures from
    /// (<see cref="UiTheme.LeftPanelWidth"/>): the section panel is transient,
    /// and controls that jumped sideways every time it opened would read as a
    /// glitch rather than as a layout.
    ///
    /// The Units section carries the editor's core loop — pick team,
    /// affiliation and echelon, then DRAG a card onto the terrain. A live
    /// ground marker tracks the real 3D drop point during the drag, so what you
    /// see is exactly where the unit lands. Its list has two modes: AVAILABLE
    /// (the draggable catalogue, an accordion of one section per arm of
    /// service) and DEPLOYED (what is actually on the map — click a row to
    /// select that formation, double-click to fly the camera to it).
    /// </summary>
    public class UnitPaletteUI : MonoBehaviour
    {
        /// <summary>Deploy request carrying the exact geodetic point the preview ring was sitting on.</summary>
        public System.Action<UnitDefinition, Team, Affiliation, Echelon, double, double> DropRequested;
        public System.Action<string> DropRejected;

        // Tactical-graphics controls (GENERAL section).
        public System.Action GenerateSectorsRequested;
        public System.Action ClearSectorsRequested;
        public System.Action<bool> AutoSectorsChanged;
        /// <summary>Fog of war armed/disarmed — see docs/16-FOG-OF-WAR.md.</summary>
        public System.Action<bool> FogOfWarChanged;
        /// <summary>Line-of-sight ring on the selected unit shown/hidden.</summary>
        public System.Action<bool> LineOfSightChanged;
        /// <summary>Maximum-weapon-range ring on the selected unit shown/hidden.</summary>
        public System.Action<bool> WeaponRangeChanged;

        // Tool strip.
        public System.Action SelectToolRequested;

        // Deployed list.
        public System.Action<UnitActor> SelectUnitRequested;
        /// <summary>Double-click on a DEPLOYED row: select it *and* fly the camera to it.</summary>
        public System.Action<UnitActor> FocusUnitRequested;
        public System.Action<UnitActor> RemoveUnitRequested;

        enum Section
        {
            General, Units, Players, Commanders, Logistics, Sustainment, Reinforcements,
            Obstacles, Effects, Missions, Environment, Map,
            /// <summary>Reserved — see <see cref="BuildStatsSection"/>.</summary>
            Stats,
            /// <summary>Reserved — see <see cref="BuildZonesSection"/>.</summary>
            Zones,
            /// <summary>Reserved — see <see cref="BuildObjectsSection"/>.</summary>
            Objects,
            /// <summary>Reserved — see <see cref="BuildSuppliesSection"/>.</summary>
            Supplies,
            /// <summary>Battle-mode only — the nav row is hidden in the editor.</summary>
            Groups
        }
        enum ListMode { Available, Deployed }

        /// <summary>
        /// Ready-made H-hours. Time of day is the operationally interesting
        /// variable — light, not the calendar, decides how a scenario plays —
        /// so the presets are one date at three points in the day.
        /// </summary>
        static readonly (string name, string detail, System.DateTime when)[] StartPresets =
        {
            ("DAWN ATTACK",   "First light — limited visibility",
                new System.DateTime(1990, 6, 21, 5, 30, 0)),
            ("MIDDAY ADVANCE", "Full daylight — best observation",
                new System.DateTime(1990, 6, 21, 12, 0, 0)),
            ("NIGHT OPERATION", "Darkness — movement under cover",
                new System.DateTime(1990, 6, 21, 23, 0, 0))
        };

        // ------------------------------------------------------------ layout
        /// <summary>The always-present rail holding the nav and the tool strip.</summary>
        const float RailWidth = UiTheme.LeftPanelWidth;
        /// <summary>
        /// The section panel. Every section builder lays itself out against this,
        /// so it is deliberately the width the single panel used to be — the
        /// split moved the content, it did not reflow it.
        /// </summary>
        const float PanelWidth = UiTheme.SectionPanelWidth;
        const float Pad = UiTheme.PanelPadding;
        const float InnerWidth = PanelWidth - Pad * 2f;

        // --- unit list card metrics ---
        /// <summary>Icon column width. Was 36 px, at which an APP-6 frame was a smudge.</summary>
        const float CardIconSize = 44f;
        /// <summary>
        /// Left inset of a list card's icon, in both AVAILABLE and DEPLOYED.
        ///
        /// The cards sit inside a scroll viewport whose own edge was clipping
        /// the first few pixels of the APP-6 frame — an 8 px inset left the blue
        /// rectangle's left stroke half off the panel, and 23 px still had the
        /// icon crowding the rail that runs down the outside of the section
        /// panel. 43 px clears both and gives the column a gutter wide enough
        /// that the frame reads as a counter rather than as something pressed
        /// against the edge of the screen.
        ///
        /// <see cref="CardTextWidth"/> is derived from this, so the text column
        /// gives up exactly the width the icon column takes — everything in a
        /// card is best-fitted, so that is paid in type size, not truncation.
        /// </summary>
        const float CardIconX = 43f;
        /// <summary>Where a card's text column starts: icon inset + icon + gutter.</summary>
        const float CardTextX = CardIconX + CardIconSize + 6f;
        /// <summary>
        /// Text width inside a list card. The card is the content width less the
        /// layout padding, and the content is the viewport less the scrollbar,
        /// so all three come off the panel width here.
        /// </summary>
        const float CardTextWidth = InnerWidth - CardTextX - UIFactory.ScrollbarWidth - 8f;
        const float AvailableCardHeight = 58f;
        const float DeployedCardHeight = 66f;
        /// <summary>Y of the list's tab row, measured from the section's top edge.</summary>
        /// <summary>
        /// Y of the list's tab row, measured from the section's top edge. Moved
        /// up 40 px when the echelon dropdown was removed — the list took the
        /// space rather than leaving a gap where a control used to be.
        /// </summary>
        const float ListTop = -90f;
        /// <summary>
        /// Where the nav list starts, measured from the rail's top: clear of the
        /// emblem and the mode heading, which stay put while the list scrolls.
        /// </summary>
        const float NavTop = 40f;
        /// <summary>Pitch of a nav row — the row's own height plus its gap.</summary>
        const float NavRowPitch = 36f;
        /// <summary>Inset above the first nav row and below the last.</summary>
        const float NavPad = 6f;
        /// <summary>
        /// Nav rows are inset from the rail's right edge by the scrollbar, so a
        /// row's fill and its click target end where the viewport does rather
        /// than running under the bar.
        /// </summary>
        const float NavRowWidth = RailWidth - UIFactory.ScrollbarWidth;
        /// <summary>Caption row plus the icon row beneath it — the two must not share a band.</summary>
        const float ToolStripHeight = 74f;
        /// <summary>Section panel header: the open section's name and its close button.</summary>
        const float SectionHeaderHeight = 44f;
        /// <summary>Seconds the panel takes to slide fully open or shut.</summary>
        const float SlideSeconds = 0.16f;

        Team _team = Team.User;
        Affiliation _affiliation = Affiliation.Friendly;
        /// <summary>
        /// Size every unit is deployed at. Battalion because it is the echelon an
        /// operational map is actually drawn at — brigades are too coarse to
        /// manoeuvre and companies too many to command.
        /// </summary>
        const Echelon DefaultEchelon = Echelon.Battalion;

        readonly Echelon _echelon = DefaultEchelon;
        Section _section = Section.Units;
        ListMode _listMode = ListMode.Available;
        string _search = "";

        /// <summary>
        /// Which arms of service are expanded in the AVAILABLE list.
        ///
        /// Empty to start: 117 unit types under nine headings is a list you
        /// scroll rather than one you read, and the arms are what a player is
        /// actually choosing between first — "I want an armoured battalion"
        /// comes before "which one". Collapsed, the whole order of battle fits
        /// on one screen and picking a category is one click.
        ///
        /// A search overrides this and opens everything (see
        /// <see cref="PopulateAvailable"/>): typing a name is already a
        /// statement about which unit you want, and making the results wait
        /// behind a second click would be the control fighting the query.
        /// </summary>
        readonly HashSet<UnitBranch> _openBranches = new HashSet<UnitBranch>();
        /// <summary>Scratch list for one branch's matching types — reused, not reallocated per branch per keystroke.</summary>
        readonly List<UnitDefinition> _branchMatches = new List<UnitDefinition>();

        RectTransform _listContent;
        Button _blueTab, _redTab;
        Image _blueFill, _redFill;
        Text _listCount;
        Button _availableTabBtn, _deployedTabBtn;
        Image _dragGhost;
        Canvas _canvas;
        UnitDefinition _dragging;
        Button _autoSectorBtn;
        bool _autoSectors;
        RectTransform _fogLamp, _losLamp, _weaponLamp;
        Text _fogLabel, _losLabel, _weaponLabel;
        bool _fog;
        /// <summary>Mirrors GameController's defaults — both rings are on until they are turned off.</summary>
        bool _lineOfSight = true;
        bool _weaponRange = true;

        readonly List<(Section section, string title, Image fill, Image glyph, Text label, RectTransform bar)> _navRows =
            new List<(Section, string, Image, Image, Text, RectTransform)>();
        readonly Dictionary<Section, RectTransform> _sectionContent = new Dictionary<Section, RectTransform>();

        /// <summary>The always-present rail. Held so mission mode can take the whole editor chrome off.</summary>
        RectTransform _rail;

        // Section panel.
        RectTransform _sectionPanel;
        Text _sectionTitle;
        bool _panelOpen;
        /// <summary>0 = tucked behind the rail, 1 = fully out.</summary>
        float _slide;
        readonly List<(Image fill, Image glyph)> _tools = new List<(Image, Image)>();
        int _activeTool;

        Text _startValueLabel;

        // Weather section.
        readonly List<(SkyPhase phase, Button button)> _skyButtons = new List<(SkyPhase, Button)>();
        readonly List<(WeatherCondition condition, Image fill, Text label)> _conditionFrames =
            new List<(WeatherCondition, Image, Text)>();
        Button _autoDayNightBtn;
        RectTransform _autoDayNightLamp;
        Text _autoDayNightLabel;

        // Effects section.
        readonly List<(VfxId id, Image fill, Text label)> _effectButtons =
            new List<(VfxId, Image, Text)>();

        // Map section.
        RectTransform _buildingsLamp, _mapControlsLamp, _compassLamp;
        Text _buildingsLabel, _mapControlsLabel, _compassLabel, _labelSizeValue;

        MapManager _map;
        MapControlsUI _mapControls;
        /// <summary>
        /// The fire menus' host. The four sections built here that belong to it
        /// are laid out into its pages rather than into this rail's section
        /// panel — see <see cref="StrikeDockUI"/> for why they left the rail.
        /// </summary>
        StrikeDockUI _strikeDock;
        GameClock _clock;
        WeatherSystem _weather;
        EffectPlacementTool _effects;
        CameraRig _rig;
        Camera _worldCam;
        /// <summary>3D drop preview — the volume shown under the cursor while dragging.</summary>
        TargetAreaMarker _placementMarker;
        bool _lastDropValid;
        double _dropLat, _dropLon;

        static readonly MapStyle[] Styles =
        {
            MapStyle.Satellite, MapStyle.SatelliteLabels, MapStyle.Roads,
            MapStyle.Sentinel2, MapStyle.OpenStreetMap, MapStyle.Terrain
        };
        Dropdown _styleDropdown;
        // Cached rather than fetched via GetComponentInChildren: the MAP section
        // starts hidden, and that call skips inactive children.
        Text _viewBtnLabel;

        public void Build(Canvas canvas, MapManager map, Camera worldCam, CameraRig rig,
            GameClock clock, WeatherSystem weather, EffectPlacementTool effects,
            ArtilleryStrikeSystem artillery, AirStrikeSystem airStrike, AirSupplySystem airSupply,
            UavStrikeSystem uavStrike, NavalStrikeSystem naval,
            MapControlsUI mapControls, StrikeDockUI strikeDock,
            IronMeridian.Logistics.LogisticsSystem logistics,
            IronMeridian.Logistics.SustainmentSystem sustainment,
            ReinforcementSystem reinforcements, IronMeridian.Lines.ObstacleSystem obstacles)
        {
            _canvas = canvas;
            _map = map;
            _worldCam = worldCam;
            _rig = rig;
            _clock = clock;
            _weather = weather;
            _effects = effects;
            _artillery = artillery;
            _airStrike = airStrike;
            _airSupply = airSupply;
            _uavStrike = uavStrike;
            _naval = naval;
            _mapControls = mapControls;
            _strikeDock = strikeDock;
            _logistics = logistics;
            _sustainment = sustainment;
            _reinforcements = reinforcements;
            _obstacles = obstacles;

            // The section panel is created first so it draws *behind* the rail —
            // uGUI paints in hierarchy order, and the closed position tucks the
            // panel underneath the rail rather than off-screen.
            var body = BuildSectionPanel(canvas);

            var panel = UIFactory.CreatePanel(canvas.transform, "UnitPalette", UiTheme.Panel);
            _rail = panel;
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(0, 0);
            panel.offsetMax = new Vector2(RailWidth, -UiTheme.TopBarHeight);

            // Hairline down the rail's right edge, separating it from the map.
            var edge = UIFactory.CreatePanel(panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            BuildHeader(panel);

            _sectionContent[Section.General] = MakeSectionContent(body, "General");
            _sectionContent[Section.Units] = MakeSectionContent(body, "Units");
            _sectionContent[Section.Players] = MakeSectionContent(body, "Players");
            _sectionContent[Section.Commanders] = MakeSectionContent(body, "Commanders");
            _sectionContent[Section.Logistics] = MakeSectionContent(body, "Logistics");
            _sectionContent[Section.Sustainment] = MakeSectionContent(body, "Sustainment");
            _sectionContent[Section.Effects] = MakeSectionContent(body, "Effects");
            _sectionContent[Section.Missions] = MakeSectionContent(body, "Missions");
            _sectionContent[Section.Environment] = MakeSectionContent(body, "Environment");
            _sectionContent[Section.Map] = MakeSectionContent(body, "Map");
            _sectionContent[Section.Reinforcements] = MakeSectionContent(body, "Reinforcements");
            _sectionContent[Section.Obstacles] = MakeSectionContent(body, "Obstacles");
            _sectionContent[Section.Stats] = MakeSectionContent(body, "Stats");
            _sectionContent[Section.Zones] = MakeSectionContent(body, "Zones");
            _sectionContent[Section.Objects] = MakeSectionContent(body, "Objects");
            _sectionContent[Section.Supplies] = MakeSectionContent(body, "Supplies");
            _sectionContent[Section.Groups] = MakeSectionContent(body, "Groups");

            BuildGeneralSection(_sectionContent[Section.General]);
            BuildUnitsSection(_sectionContent[Section.Units]);
            BuildPlayersSection(_sectionContent[Section.Players]);
            BuildCommandersSection(_sectionContent[Section.Commanders]);
            BuildLogisticsSection(_sectionContent[Section.Logistics]);
            BuildSustainmentSection(_sectionContent[Section.Sustainment]);
            BuildEffectsSection(_sectionContent[Section.Effects]);
            BuildMissionsSection(_sectionContent[Section.Missions]);
            BuildEnvironmentSection(_sectionContent[Section.Environment]);
            BuildMapSection(_sectionContent[Section.Map]);
            BuildReinforcementSection(_sectionContent[Section.Reinforcements]);
            BuildObstacleSection(_sectionContent[Section.Obstacles]);
            BuildStatsSection(_sectionContent[Section.Stats]);
            BuildZonesSection(_sectionContent[Section.Zones]);
            BuildObjectsSection(_sectionContent[Section.Objects]);
            BuildSuppliesSection(_sectionContent[Section.Supplies]);
            BuildGroupsSection(_sectionContent[Section.Groups]);

            // The four fire menus live in the strike dock's pages rather than in
            // this rail. They are still built here because their controls are
            // driven from catalogues this class already holds references to —
            // what moved is where they are drawn, not who draws them.
            if (_strikeDock != null)
            {
                BuildArtillerySection(_strikeDock.PageFor(StrikeDockUI.Menu.Artillery));
                BuildAirStrikeSection(_strikeDock.PageFor(StrikeDockUI.Menu.AirStrike));
                BuildAirSupplySection(_strikeDock.PageFor(StrikeDockUI.Menu.AirSupply));
                BuildUavStrikeSection(_strikeDock.PageFor(StrikeDockUI.Menu.UavStrike));
                BuildNavalStrikeSection(_strikeDock.PageFor(StrikeDockUI.Menu.NavalStrike));
            }

            BuildToolStrip(panel);
            OpenSection(Section.Units);
            // Skip the opening slide: the panel is simply already out when the
            // editor appears.
            _slide = 1f;
            ApplySlide();

            // Drag ghost (top-most)
            var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            ghostGo.transform.SetParent(canvas.transform, false);
            _dragGhost = ghostGo.GetComponent<Image>();
            _dragGhost.raycastTarget = false;
            _dragGhost.preserveAspect = true;
            ((RectTransform)ghostGo.transform).sizeDelta = new Vector2(70, 70);
            ghostGo.SetActive(false);

            BuildGroundMarker();
            // Paints the side selector's initial state and fills the list.
            SetTeam(_team);

            _map.ViewModeChanged += OnViewModeChanged;
            _map.StyleChanged += OnStyleChanged;
            _map.BuildingsVisibilityChanged += _ => RefreshMapSection();
            UnitRegistry.Changed += OnUnitsChanged;
            // Loading a map sets the start after this panel is built, so the
            // label has to follow the clock rather than only read it once.
            if (_clock != null) _clock.StartChanged += RefreshStartLabel;
            // The system owns weather state; the panel only reflects it.
            if (_weather != null) _weather.Changed += RefreshWeather;
            if (_effects != null) _effects.ArmedChanged += RefreshEffects;
            if (_artillery != null) _artillery.ArmedChanged += RefreshArtillery;
            if (_airStrike != null) _airStrike.ArmedChanged += RefreshAirStrike;
            if (_uavStrike != null) _uavStrike.ArmedChanged += RefreshUavStrike;
            if (_naval != null) _naval.ArmedChanged += RefreshNavalStrike;

            // One allowance behind four menus — see StrikeBudget.
            StrikeBudget.Changed += RefreshStrikeBudget;
            RefreshStrikeBudget();
        }

        static RectTransform MakeSectionContent(RectTransform body, string name)
        {
            var rt = UIFactory.CreateGroup(body, "Section_" + name);
            UIFactory.Stretch(rt);
            return rt;
        }

        // ----------------------------------------------------- section panel

        /// <summary>
        /// Builds the sliding panel and returns the body every section's
        /// controls are parented to. Its rect is driven by
        /// <see cref="ApplySlide"/> rather than by anchors, so the panel can
        /// travel horizontally while staying stretched to the full map height.
        /// </summary>
        RectTransform BuildSectionPanel(Canvas canvas)
        {
            _sectionPanel = UIFactory.CreatePanel(canvas.transform, "SectionPanel", UiTheme.Panel);
            _sectionPanel.anchorMin = new Vector2(0, 0);
            _sectionPanel.anchorMax = new Vector2(0, 1);
            _sectionPanel.pivot = new Vector2(0, 0.5f);
            _sectionPanel.sizeDelta = new Vector2(PanelWidth, -UiTheme.TopBarHeight);
            _sectionPanel.anchoredPosition = new Vector2(RailWidth, -UiTheme.TopBarHeight * 0.5f);

            var edge = UIFactory.CreatePanel(_sectionPanel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            _sectionTitle = UIFactory.CreateText(_sectionPanel, "", UiTheme.FontHeading, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_sectionTitle.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -13), new Vector2(PanelWidth - Pad - 44f, 22));
            UIFactory.Fit(_sectionTitle);

            var close = UIFactory.CreateIconButton(_sectionPanel, UiIcons.Close, ClosePanel,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 8f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-Pad + 4f, -8f), new Vector2(28, 28));

            var rule = UIFactory.CreateDivider(_sectionPanel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -SectionHeaderHeight + 1f);

            var body = UIFactory.CreateGroup(_sectionPanel, "Body");
            body.anchorMin = new Vector2(0, 0); body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(0, 0);
            body.offsetMax = new Vector2(0, -SectionHeaderHeight);
            return body;
        }

        /// <summary>Opens a section, or closes the panel if that section is already showing.</summary>
        void OpenSection(Section section)
        {
            if (_panelOpen && _section == section) { ClosePanel(); return; }

            _section = section;
            _panelOpen = true;
            _sectionPanel.gameObject.SetActive(true);

            foreach (var kv in _sectionContent)
                kv.Value.gameObject.SetActive(kv.Key == section);

            // The commanders page skips rebuilds while it is shut — loading a
            // map would otherwise rebuild it once per formation spawned.
            if (section == Section.Commanders) _commanders?.OnShown();
            // Both of these read live state rather than holding their own, so
            // they are rebuilt on the way in rather than kept in step while shut.
            if (section == Section.Groups) RefreshGroups();
            if (section == Section.Logistics) RefreshLogistics();
            if (section == Section.Sustainment) RefreshSustainment();
            if (section == Section.Reinforcements) PopulateReinforcements();
            if (section == Section.Obstacles) RefreshObstacles();

            foreach (var row in _navRows)
                if (row.section == section && _sectionTitle != null) _sectionTitle.text = row.title;

            PaintNav();
        }

        public void ClosePanel()
        {
            if (!_panelOpen) return;
            _panelOpen = false;
            PaintNav();
        }

        /// <summary>
        /// Takes the whole editor chrome off the screen — the rail and its
        /// section panel — for a single-player mission, which is played on the
        /// map rather than authored on it.
        ///
        /// The panel is closed first rather than merely hidden: it would
        /// otherwise slide back out at its old width the moment the chrome
        /// returned, and the on-map controls measure their inset from
        /// <see cref="LeftChromeEdge"/>, which has to go to zero with it.
        /// </summary>
        public void SetChromeVisible(bool visible)
        {
            if (!visible) ClosePanel();
            _chromeHidden = !visible;

            if (_rail != null) _rail.gameObject.SetActive(visible);
            if (_sectionPanel != null && !visible) _sectionPanel.gameObject.SetActive(false);

            // Parked at the closed end so nothing is left mid-animation if the
            // chrome ever comes back.
            _slide = visible ? 1f : 0f;

            LeftChromeEdge = visible ? RailWidth + PanelWidth : 0f;
            if (_mapControls != null) _mapControls.SetLeftInset(LeftChromeEdge);
        }

        /// <summary>
        /// True while the whole rail is off the screen. The slide animation has
        /// to know: it runs from <see cref="Update"/> on the controller's own
        /// GameObject, which is still alive, and would otherwise keep pushing
        /// the on-map controls 232 px inboard of a rail that is not there.
        /// </summary>
        bool _chromeHidden;

        /// <summary>Which nav row reads as active — none at all while the panel is closed.</summary>
        void PaintNav()
        {
            foreach (var (s, _, fill, glyph, label, bar) in _navRows)
            {
                bool on = _panelOpen && s == _section;
                fill.color = on ? UiTheme.AccentWash : new Color(0, 0, 0, 0);
                glyph.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                label.color = on ? UiTheme.Text : UiTheme.TextDim;
                bar.gameObject.SetActive(on);
            }
        }

        void Update()
        {
            if (_chromeHidden) return;

            float target = _panelOpen ? 1f : 0f;
            if (Mathf.Approximately(_slide, target)) return;

            // Unscaled: the pause menu zeroes timeScale, and chrome that freezes
            // mid-slide would look broken.
            _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime / SlideSeconds);
            ApplySlide();
        }

        /// <summary>
        /// Positions the panel for the current slide value and switches it off
        /// once it is fully shut — a hidden-but-active panel would still be
        /// eating clicks meant for the map behind it.
        /// </summary>
        void ApplySlide()
        {
            if (_sectionPanel == null || _chromeHidden) return;

            float x = Mathf.Lerp(RailWidth - PanelWidth, RailWidth, Mathf.SmoothStep(0f, 1f, _slide));
            _sectionPanel.anchoredPosition = new Vector2(x, -UiTheme.TopBarHeight * 0.5f);

            // The on-map zoom cluster rides the panel's edge so it is never
            // buried underneath it — and so does anything else docked on this
            // side, which since the minimap moved over here is not only the
            // cluster.
            LeftChromeEdge = x + PanelWidth;
            if (_mapControls != null) _mapControls.SetLeftInset(LeftChromeEdge);
            LeftInsetChanged?.Invoke(LeftChromeEdge);

            if (_slide <= 0f) _sectionPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Raised with the rail's right-hand edge as the section panel slides.
        /// The on-map controls are driven directly; anything else that has to
        /// keep clear of the editor's left chrome hangs off this.
        /// </summary>
        public System.Action<float> LeftInsetChanged;

        /// <summary>
        /// Where the rail and its section panel currently end, in canvas pixels.
        /// The missile board docks in the same place and overrides this while it
        /// is up — see <see cref="ReassertMapInset"/>.
        /// </summary>
        public float LeftChromeEdge { get; private set; } = RailWidth + PanelWidth;

        /// <summary>
        /// Puts the on-map controls back against the rail's own edge. Called
        /// when the missile board closes: the slide animation is what normally
        /// drives the inset, and it is not running at that moment, so nothing
        /// else would ever take the board's width back off.
        /// </summary>
        public void ReassertMapInset()
        {
            if (_mapControls != null) _mapControls.SetLeftInset(LeftChromeEdge);
            LeftInsetChanged?.Invoke(LeftChromeEdge);
        }

        // ------------------------------------------------------------ header

        void BuildHeader(RectTransform panel)
        {
            // Emblem only. The "ORDER OF BATTLE" caption beside it is gone: the
            // rail's ten labelled nav rows already say what this is, and a
            // heading that repeats the obvious is a heading that costs a row of
            // vertical space the sections needed more.
            var emblem = UIFactory.CreateImage(panel, UiIcons.Shield, "Emblem");
            emblem.color = UiTheme.Accent;
            emblem.raycastTarget = false;
            UIFactory.Place((RectTransform)emblem.transform, new Vector2(0f, 1f), new Vector2(Pad, -14), new Vector2(19, 19));

            // Which set of rules the map is currently playing by. The mode chip
            // on the top bar says the same thing, but the rail is where the
            // player's hands are — and half these sections mean something
            // different depending on the answer, so it belongs at the head of
            // the list they are choosing from. See SetBattleMode.
            _modeHeading = UIFactory.CreateText(panel, "SCENARIO MODE", UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_modeHeading.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad + 26f, -14), new Vector2(RailWidth - Pad - 34f, 18));
            UIFactory.Fit(_modeHeading, 8);

            // The nav scrolls. Seventeen sections do not fit the rail on a 720p
            // screen, and a row that ran under the tool strip would be a section
            // that could not be opened at all — the one failure a nav is not
            // allowed. The emblem above and the tools below stay put; only the
            // list between them moves.
            var nav = BuildNavList(panel);

            // Every row the rail can show, in the order it shows them: the
            // ground rules, the forces, who is fighting, who commands, then the
            // dressing. Which of them are actually on screen depends on the
            // mode — see ApplyModeVisibility. The five fire menus are not here;
            // they went to the strike dock at the top right, being things you
            // *do* in a scenario rather than things you set one up with.
            AddNavRow(nav, Section.General, "GENERAL", UiIcons.Flag);
            AddNavRow(nav, Section.Units, "UNITS", UiIcons.Person);
            AddNavRow(nav, Section.Players, "PLAYERS", UiIcons.Shield);
            AddNavRow(nav, Section.Commanders, "COMMANDERS", UiIcons.Orders);
            AddNavRow(nav, Section.Logistics, "LOGISTICS", UiIcons.Pallet);
            AddNavRow(nav, Section.Sustainment, "SUSTAINMENT", UiIcons.FuelDrop);
            AddNavRow(nav, Section.Reinforcements, "REINFORCEMENTS", UiIcons.Parachute);
            AddNavRow(nav, Section.Obstacles, "MINES AND OBSTACLES", UiIcons.Obstacles);
            AddNavRow(nav, Section.Effects, "EFFECTS", UiIcons.Flame);
            // The single-player campaign's missions, edited here and played from
            // the main menu — see docs/22-MISSIONS.md.
            AddNavRow(nav, Section.Missions, "MISSIONS", UiIcons.Pin);
            // Time and weather are one section, not two: they are the same
            // decision — what the light and the going are like — and a designer
            // who sets a night attack is choosing both in the same breath.
            AddNavRow(nav, Section.Environment, "ENVIRONMENT", UiIcons.Cloud);
            AddNavRow(nav, Section.Map, "MAP CONFIG", UiIcons.Layers);
            AddNavRow(nav, Section.Stats, "STATS", UiIcons.Chart);
            AddNavRow(nav, Section.Zones, "ZONES", UiIcons.Square);
            AddNavRow(nav, Section.Objects, "OBJECTS", UiIcons.Equipment);
            AddNavRow(nav, Section.Supplies, "SUPPLIES", UiIcons.Crates);
            AddNavRow(nav, Section.Groups, "GROUPS", UiIcons.Group);

            ApplyModeVisibility(false);
        }

        // ------------------------------------------------------ nav by mode

        /// <summary>
        /// The rail in **scenario mode**: everything a scenario is laid out
        /// with. Order follows the rows' build order, not this array.
        /// </summary>
        static readonly Section[] ScenarioSections =
        {
            // UNITS is here because it is the one section a scenario cannot be
            // built without — it is the palette every formation on the map is
            // dragged from.
            Section.General, Section.Units, Section.Players, Section.Commanders,
            Section.Logistics, Section.Sustainment, Section.Obstacles, Section.Effects,
            Section.Missions, Section.Environment, Section.Map, Section.Zones, Section.Objects
        };

        /// <summary>
        /// The rail in **battle mode**: what a fight in progress is managed
        /// with. Deliberately short — during a battle the rail is not where the
        /// player's attention should be.
        /// </summary>
        static readonly Section[] BattleSections =
        {
            Section.Reinforcements, Section.Stats, Section.Supplies
        };

        static bool Allowed(Section section, bool battle) =>
            System.Array.IndexOf(battle ? BattleSections : ScenarioSections, section) >= 0;

        /// <summary>
        /// Shows the rows this mode has and re-flows the list around the ones it
        /// does not.
        ///
        /// **Hidden rows are re-flowed, not merely switched off.** The rows are
        /// placed at absolute offsets inside the scrolling list, so a hidden one
        /// would otherwise leave a 36 px hole where it used to be and the rail
        /// would read as a list with pieces missing rather than as a shorter
        /// list.
        ///
        /// If the open section is not in the new mode's list, the panel closes.
        /// Switching it to some other section would be the rail deciding what
        /// the player is looking at; closing hands the screen back to the map,
        /// which is what starting a battle is for.
        /// </summary>
        void ApplyModeVisibility(bool battle)
        {
            float y = -NavPad;

            foreach (var (section, row) in _navOrder)
            {
                bool show = Allowed(section, battle);
                row.gameObject.SetActive(show);
                if (!show) continue;

                row.anchoredPosition = new Vector2(0, y);
                y -= NavRowPitch;
            }

            // The list is as tall as the rows actually in it, so the scroll
            // knows when there is nothing more to show — and does not scroll at
            // all when the mode's list fits.
            if (_navList != null) _navList.sizeDelta = new Vector2(0, -y + NavPad);

            if (_panelOpen && !Allowed(_section, battle)) ClosePanel();
        }

        /// <summary>Every nav row in build order, so the list can be re-flowed by mode.</summary>
        readonly List<(Section section, RectTransform row)> _navOrder =
            new List<(Section, RectTransform)>();

        /// <summary>The scrolling list's content, sized to whichever rows are showing.</summary>
        RectTransform _navList;

        /// <summary>
        /// The scrolling viewport the nav rows live in: from under the emblem
        /// down to the tool strip, which keeps its place at the foot of the rail.
        ///
        /// The stock scroll content stacks its children with a
        /// <see cref="VerticalLayoutGroup"/>; it is **disabled rather than
        /// destroyed**, because `Destroy` on a component is deferred to the end
        /// of the frame and a destroyed layout group would still lay out the
        /// rows added to it a few lines later.
        /// </summary>
        RectTransform BuildNavList(RectTransform panel)
        {
            // The bar hides itself when the mode's list fits, which is most of
            // the time in battle mode — three rows never scroll.
            var scroll = UIFactory.CreateScrollView(panel, out RectTransform nav,
                withScrollbar: true, autoHideScrollbar: true);
            _navList = nav;
            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, ToolStripHeight);
            rt.offsetMax = new Vector2(0, -NavTop);
            // The rail already has a fill; the scroll's own would darken a band
            // down the middle of it.
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var layout = nav.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = nav.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            return nav;
        }

        /// <summary>
        /// One nav row, added to the list in build order. Where it ends up
        /// vertically is <see cref="ApplyModeVisibility"/>'s business — rows
        /// used to carry hand-written offsets, which meant a row that was hidden
        /// left a hole and a row inserted in the middle renumbered every row
        /// under it.
        /// </summary>
        RectTransform AddNavRow(RectTransform panel, Section section, string label, Sprite glyph)
        {
            var row = UIFactory.CreatePanel(panel, "Nav_" + label, new Color(0, 0, 0, 0));
            UIFactory.Place(row, new Vector2(0f, 1f), Vector2.zero, new Vector2(NavRowWidth, 34));
            row.pivot = new Vector2(0f, 1f);
            _navOrder.Add((section, row));

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => OpenSection(section));

            // Left accent bar marks the active section, as in the design.
            var bar = UIFactory.CreatePanel(row, "ActiveBar", UiTheme.Accent);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
            bar.pivot = new Vector2(0, 0.5f);
            bar.sizeDelta = new Vector2(3, 0);
            bar.GetComponent<Image>().raycastTarget = false;

            var img = UIFactory.CreateImage(row, glyph, "Glyph");
            img.raycastTarget = false;
            UIFactory.Place((RectTransform)img.transform, new Vector2(0f, 0.5f), new Vector2(Pad, 0), new Vector2(16, 16));

            // "MINES AND OBSTACLES" is the longest label the rail has to carry,
            // so the row's text is fitted rather than clipped at a fixed width.
            var text = UIFactory.CreateText(row, label, UiTheme.FontBody, UiTheme.TextDim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(Pad + 26, 0),
                new Vector2(NavRowWidth - Pad - 34f, 20));
            UIFactory.Fit(text);

            _navRows.Add((section, label, row.GetComponent<Image>(), img, text, bar));
            return row;
        }

        // ----------------------------------------------------- general section

        /// <summary>
        /// Tactical graphics: derive each side's sector boundaries and FEBA from
        /// where its units currently stand.
        /// </summary>
        void BuildGeneralSection(RectTransform content)
        {
            SectionLabel(content, "TACTICAL GRAPHICS", -8);

            GeneralButton(content, "GENERATE SECTORS", -32, () => GenerateSectorsRequested?.Invoke());
            GeneralButton(content, "CLEAR GRAPHICS", -72, () => ClearSectorsRequested?.Invoke());
            _autoSectorBtn = GeneralButton(content, "AUTO-UPDATE: OFF", -112, () =>
            {
                _autoSectors = !_autoSectors;
                AutoSectorsChanged?.Invoke(_autoSectors);
                RefreshAutoSectorLabel();
            });

            var hint = UIFactory.CreateText(content,
                "Boundaries run rear-to-front between adjacent formations; the FEBA follows the forward units.",
                UiTheme.FontSmall, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -156), new Vector2(InnerWidth, 56));

            // --- intelligence ---
            SectionLabel(content, "INTELLIGENCE", -216);

            _losLamp = ToggleRow(content, "LINE OF SIGHT", -238, () =>
            {
                _lineOfSight = !_lineOfSight;
                LineOfSightChanged?.Invoke(_lineOfSight);
                RefreshGeneralSection();
            }, out _losLabel);

            // Directly under LINE OF SIGHT because the two are read together:
            // what a formation can see and what it can reach are the pair of
            // circles a planner is comparing, and having only one of them
            // switchable meant you could never look at either on its own.
            _weaponLamp = ToggleRow(content, "MAX WEAPON RANGE", -282, () =>
            {
                _weaponRange = !_weaponRange;
                WeaponRangeChanged?.Invoke(_weaponRange);
                RefreshGeneralSection();
            }, out _weaponLabel);

            _fogLamp = ToggleRow(content, "FOG OF WAR", -326, () =>
            {
                _fog = !_fog;
                FogOfWarChanged?.Invoke(_fog);
                RefreshGeneralSection();
            }, out _fogLabel);

            var intelHint = UIFactory.CreateText(content,
                "LINE OF SIGHT draws how far the selected formation can see, in red, with the distance in " +
                "metres on the ring.\n\n" +
                "MAX WEAPON RANGE draws how far it can shoot, in blue. Both are shown in the scenario " +
                "editor and in battle, and either can be turned off on its own — a mortar battery's two " +
                "circles are nothing alike, and overlaying them is only useful when you want both.\n\n" +
                "FOG OF WAR draws enemy formations only where something of yours can see them. Lose sight " +
                "of one and the map keeps the contact: last known position, the time it was seen, and a " +
                "ring that grows to cover where it could have got to since. Battle mode only — the editor " +
                "shows both sides so you can lay them out. Use the RECON orders to see past your own " +
                "units' eyes.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(intelHint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -370), new Vector2(InnerWidth, 230));

            RefreshGeneralSection();
        }

        /// <summary>
        /// Forces the GENERAL toggles back to given values without firing their
        /// events. RESET puts the systems back to their defaults directly, and
        /// the lamps here would otherwise keep reporting the state from before it.
        /// </summary>
        public void SyncGeneralToggles(bool autoSectors, bool fogOfWar, bool lineOfSight,
            bool weaponRange)
        {
            _autoSectors = autoSectors;
            _fog = fogOfWar;
            _lineOfSight = lineOfSight;
            _weaponRange = weaponRange;
            RefreshAutoSectorLabel();
            RefreshGeneralSection();
        }

        /// <summary>Repaints the GENERAL section's toggles from the state their systems own.</summary>
        void RefreshGeneralSection()
        {
            if (_losLamp != null)
            {
                _losLamp.GetComponent<Image>().color = _lineOfSight ? UiTheme.Success : UiTheme.TextFaint;
                _losLabel.text = _lineOfSight ? "SHOWN" : "HIDDEN";
            }
            if (_weaponLamp != null)
            {
                _weaponLamp.GetComponent<Image>().color = _weaponRange ? UiTheme.Success : UiTheme.TextFaint;
                _weaponLabel.text = _weaponRange ? "SHOWN" : "HIDDEN";
            }
            if (_fogLamp != null)
            {
                _fogLamp.GetComponent<Image>().color = _fog ? UiTheme.Success : UiTheme.TextFaint;
                _fogLabel.text = _fog ? "ON" : "OFF";
            }
        }

        Button GeneralButton(RectTransform content, string label, float y, UnityEngine.Events.UnityAction action)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Btn_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 34));

            var b = UIFactory.CreateButton(frame, label, action, new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)b.transform);
            return b;
        }

        void RefreshAutoSectorLabel()
        {
            if (_autoSectorBtn == null) return;
            _autoSectorBtn.GetComponentInChildren<Text>(true).text =
                _autoSectors ? "AUTO-UPDATE: ON" : "AUTO-UPDATE: OFF";
            _autoSectorBtn.GetComponentInChildren<Text>(true).color =
                _autoSectors ? UiTheme.Accent : UiTheme.Text;
        }

        // ------------------------------------------------------- units section

        void BuildUnitsSection(RectTransform content)
        {
            // --- side selector ---
            float half = (InnerWidth - 6f) / 2f;
            _blueTab = SideButton(content, "FRIENDLY", Pad, half, () => SetTeam(Team.User), out _blueFill);
            _redTab = SideButton(content, "ENEMY", Pad + half + 6f, half, () => SetTeam(Team.Enemy), out _redFill);

            // Neither an affiliation picker nor an echelon dropdown any more.
            //
            // Affiliation offered four values of which only two were ever right —
            // the side tabs above already say whose the unit is, and SetTeam
            // derives Friendly/Hostile from that — so it was a control whose only
            // real use was to contradict the tab beside it.
            //
            // Echelon went the same way: a dropdown listing every size from
            // section to army, sitting above a list of 37 unit types, made
            // deploying one unit a two-control operation and put the rarely-wanted
            // choice in front of the always-wanted one. Units now deploy at
            // <see cref="DefaultEchelon"/> and are re-sized after the fact from the
            // info panel, which is where the rest of a formation's details are
            // edited anyway. The ~90 px both controls took goes to the list.

            // --- search ---
            var searchFrame = UIFactory.CreateBorderedPanel(content, "SearchFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(searchFrame, new Vector2(0f, 1f), new Vector2(Pad, -50), new Vector2(InnerWidth, 34));

            var glass = UIFactory.CreateImage(searchFrame, UiIcons.Search, "SearchGlyph");
            glass.color = UiTheme.TextFaint;
            glass.raycastTarget = false;
            UIFactory.Place((RectTransform)glass.transform, new Vector2(0f, 0.5f), new Vector2(9, 0), new Vector2(14, 14));

            var input = UIFactory.CreateInputField(searchFrame, "Search unit or type...", UiTheme.FontSmall);
            var irt = (RectTransform)input.transform;
            UIFactory.Stretch(irt);
            irt.offsetMin = new Vector2(28, 0);
            input.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            input.onValueChanged.AddListener(v => { _search = v == null ? "" : v.Trim(); Populate(); });

            // --- list header: mode tabs + live count ---
            _availableTabBtn = ListModeButton(content, "AVAILABLE", Pad, () => SetListMode(ListMode.Available));
            _deployedTabBtn = ListModeButton(content, "DEPLOYED", Pad + 86f, () => SetListMode(ListMode.Deployed));

            var badge = UIFactory.CreatePanel(content, "CountBadge", UiTheme.AccentWash);
            UIFactory.Place(badge, new Vector2(0f, 1f), new Vector2(PanelWidth - Pad - 34, ListTop + 2f), new Vector2(34, 18));
            badge.GetComponent<Image>().raycastTarget = false;
            _listCount = UIFactory.CreateText(badge, "0", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_listCount.rectTransform);

            // --- the list itself ---
            // With a scrollbar: the AVAILABLE cards carry drag-to-deploy
            // handlers, which swallow the drag before the ScrollRect sees it, so
            // the list cannot be dragged to scroll and the wheel was the only
            // way to reach the units past the fold.
            var scroll = UIFactory.CreateScrollView(content, out _listContent, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 2);
            srt.offsetMax = new Vector2(0, ListTop - 26f);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset((int)Pad, (int)Pad, 4, 8);

            SetListMode(ListMode.Available);
        }

        Button SideButton(RectTransform content, string label, float x, float w,
            UnityEngine.Events.UnityAction action, out Image fill)
        {
            var b = UIFactory.CreateButton(content, label, action, UiTheme.Surface, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, -8), new Vector2(w, 32));
            fill = b.GetComponent<Image>();
            return b;
        }

        void StyleDropdown(Dropdown dd, float y)
        {
            var rt = (RectTransform)dd.transform;
            UIFactory.Place(rt, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 34));
            dd.GetComponent<Image>().color = UiTheme.Surface;
            if (dd.captionText != null)
            {
                dd.captionText.fontSize = UiTheme.FontSmall;
                dd.captionText.color = UiTheme.Text;
            }
        }

        Button ListModeButton(RectTransform content, string label, float x, UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(content, label, action, new Color(0, 0, 0, 0),
                UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, ListTop), new Vector2(82, 22));
            return b;
        }

        void SetListMode(ListMode mode)
        {
            _listMode = mode;
            TintListTab(_availableTabBtn, mode == ListMode.Available);
            TintListTab(_deployedTabBtn, mode == ListMode.Deployed);
            Populate();
        }

        static void TintListTab(Button b, bool active)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<Text>(true);
            if (t != null) t.color = active ? UiTheme.Accent : UiTheme.TextFaint;
            b.GetComponent<Image>().color = active ? UiTheme.AccentWash : new Color(0, 0, 0, 0);
        }

        void SetTeam(Team team)
        {
            _team = team;
            _affiliation = team == Team.User ? Affiliation.Friendly : Affiliation.Hostile;
            _blueFill.color = team == Team.User ? UiTheme.Friendly : UiTheme.Surface;
            _redFill.color = team == Team.Enemy ? UiTheme.Hostile : UiTheme.Surface;
            // The rear area follows the team tab rather than carrying a second
            // side control of its own — see BuildLogisticsSection.
            if (_logistics != null) _logistics.Team = team;
            // An airdrop lands supplies for the same side the panel is working
            // for — see BuildAirSupplySection.
            if (_airSupply != null) _airSupply.Team = team;
            if (_obstacles != null) _obstacles.Team = team;
            RefreshLogistics();
            RefreshSustainment();
            RefreshObstacles();
            PopulateReinforcements();
            Populate();
        }

        void OnUnitsChanged()
        {
            if (_listMode == ListMode.Deployed) Populate();
            // A group is a property of the units in it, so the group list has
            // nothing else to hear about a formation joining, leaving or dying.
            RefreshGroups();
            // Every burn rate on the sustainment page is read off the deployed
            // force, so the force changing is the only thing that moves them.
            if (_sectionContent.TryGetValue(Section.Sustainment, out var page) &&
                page.gameObject.activeSelf) RefreshSustainment();
        }

        // ------------------------------------------------------------ list

        void Populate()
        {
            if (_listContent == null) return;

            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                var c = _listContent.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            int count = _listMode == ListMode.Available ? PopulateAvailable() : PopulateDeployed();
            if (_listCount != null) _listCount.text = count.ToString();
        }

        /// <summary>
        /// The draggable catalogue, as an **accordion** of one section per
        /// <see cref="UnitBranch"/>: infantry, armour, artillery and the rest,
        /// each opening to the unit types inside it.
        ///
        /// Flat, this list is 117 cards deep and finding the mortar in it means
        /// scrolling past the ships. Even with headings it was a single column
        /// of cards several screens long. Collapsed sections turn it into a
        /// menu: the nine arms fit on one screen, and the one you open is the
        /// only one taking space. Walking the branches in declaration order
        /// puts manoeuvre first and the tail last, which is the order an order
        /// of battle is written in — and an empty branch prints no heading at
        /// all, so a search never leaves a bare label behind.
        /// </summary>
        int PopulateAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            // A search is already a statement of what you want; making the hits
            // wait behind a click would be the accordion fighting the query.
            bool searching = !string.IsNullOrEmpty(_search);
            int count = 0;

            foreach (var branch in UnitBranchInfo.All)
            {
                // Walked into a list first because the heading carries the
                // count, and the count is only known once the branch is walked.
                _branchMatches.Clear();
                foreach (var def in UnitDatabase.All)
                {
                    if (def.Branch != branch) continue;
                    if (!Matches(def.name, def.id, def.ammoType)) continue;
                    _branchMatches.Add(def);
                }
                if (_branchMatches.Count == 0) continue;

                count += _branchMatches.Count;

                bool open = searching || _openBranches.Contains(branch);
                BranchHeader(branch, _branchMatches.Count, open);
                if (!open) continue;

                foreach (var def in _branchMatches) CreateAvailableCard(def, folder);
            }

            if (count == 0) EmptyRow("No unit type matches that search.");
            return count;
        }

        /// <summary>Opens or closes one arm's section, and redraws the list around it.</summary>
        void ToggleBranch(UnitBranch branch)
        {
            if (!_openBranches.Remove(branch)) _openBranches.Add(branch);
            Populate();
        }

        /// <summary>
        /// One accordion header: a chevron saying which way it will go, the arm's
        /// name, and how many types are inside it.
        ///
        /// The count is on the header because it is the answer to the question a
        /// closed section raises: is there anything in there worth opening, and
        /// how much of the list am I about to unfold?
        /// </summary>
        void BranchHeader(UnitBranch branch, int count, bool open)
        {
            var row = UIFactory.CreateBorderedPanel(_listContent, "Branch_" + branch,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 30);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => ToggleBranch(branch));

            var chevron = UIFactory.CreateText(row, open ? "▾" : "▸", UiTheme.FontSmall,
                open ? UiTheme.Accent : UiTheme.TextDim, TextAnchor.MiddleCenter);
            chevron.raycastTarget = false;
            UIFactory.Place(chevron.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(16f, 16f));

            var text = UIFactory.CreateSectionHeader(row,
                UnitBranchInfo.DisplayName(branch).ToUpperInvariant(),
                open ? UiTheme.Accent : UiTheme.Text);
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(30f, 0f), new Vector2(InnerWidth - 90f, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(40f, 16f));
        }

        int PopulateDeployed()
        {
            int count = 0, index = 0;
            foreach (var actor in UnitRegistry.All)
            {
                if (actor == null || !actor.IsAlive) continue;
                // A formation the fog has taken off the map must not still be
                // listed here with its call sign and readiness — the list would
                // hand back exactly what the fog is withholding.
                if (actor.HiddenByFog) continue;
                index++;
                if (!Matches(actor.Def.name, actor.Def.id, actor.State.customName)) continue;
                CreateDeployedCard(actor, index);
                count++;
            }
            if (count == 0) EmptyRow(index == 0
                ? "Nothing deployed yet — drag a unit from AVAILABLE onto the map."
                : "No deployed unit matches that search.");
            return count;
        }

        bool Matches(params string[] fields)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            foreach (var f in fields)
                if (!string.IsNullOrEmpty(f) &&
                    f.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void EmptyRow(string message)
        {
            var t = UIFactory.CreateText(_listContent, message, UiTheme.FontSmall,
                UiTheme.TextFaint, TextAnchor.UpperLeft);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 52);
        }

        /// <summary>A draggable catalogue entry.</summary>
        void CreateAvailableCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreateBorderedPanel(_listContent, "Card_" + def.id, UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, AvailableCardHeight);

            var sprite = CardIcon(card, folder, def.id);

            UIFactory.CreateStackedLabels(card, def.name,
                $"ATK {def.attack:0}  ·  DEF {def.defence:0}  ·  {def.speedKmh:0} km/h",
                CardTextX, CardTextWidth, topInset: 12f, titleSize: UiTheme.FontBody);

            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(def, sprite));
            AddEvent(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
            AddEvent(trigger, EventTriggerType.EndDrag, e => EndDrag((PointerEventData)e));
        }

        /// <summary>A unit actually on the map: call sign, type and readiness.</summary>
        void CreateDeployedCard(UnitActor actor, int index)
        {
            var s = actor.State;
            var card = UIFactory.CreateBorderedPanel(_listContent, "Deployed_" + s.instanceId,
                UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, DeployedCardHeight);

            // Click selects the formation on the map; double-click also flies
            // the camera to it.
            //
            // A PointerClick trigger rather than a Button, because uGUI's
            // Button has no notion of a second click and
            // <see cref="PointerEventData.clickCount"/> already carries one —
            // timing clicks by hand here would be a worse copy of what the
            // event system has counted. The single-click path still runs on the
            // first click of a pair, which is right: flying to a formation you
            // have not selected would leave the map somewhere new with nothing
            // to show for it.
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.PointerClick, e =>
            {
                var pointer = (PointerEventData)e;
                if (pointer.clickCount >= 2) FocusUnitRequested?.Invoke(actor);
                else SelectUnitRequested?.Invoke(actor);
            });

            string folder = s.TeamEnum == Team.User ? "Friendly" : "Enemy";
            CardIcon(card, folder, actor.Def.id);

            string callSign = string.IsNullOrEmpty(s.customName)
                ? $"1-{index} {Abbreviate(actor.Def.name)}"
                : s.customName;

            // Side reads as a stripe down the card's left edge. It used to be a
            // dot in the text column, where it sat on top of the readiness line.
            var stripe = UIFactory.CreatePanel(card, "TeamStripe",
                s.TeamEnum == Team.User ? UiTheme.Friendly : UiTheme.Hostile);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = new Vector2(1, 0);
            stripe.sizeDelta = new Vector2(3, -2);
            stripe.GetComponent<Image>().raycastTarget = false;

            // Leaves room for the ⋮ button pinned to the card's right edge.
            float cardW = CardTextWidth - 26f;

            var title = UIFactory.CreateText(card, callSign, UiTheme.FontBody, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, CardTextX, 8f, cardW, 17f);
            UIFactory.Fit(title);

            var subtitle = UIFactory.CreateText(card, actor.Def.name, UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(subtitle.rectTransform, CardTextX, 25f, cardW, 15f);
            UIFactory.Fit(subtitle);

            // Third line is the design's metadata row. Real readiness data
            // rather than a decorative timestamp.
            var meta = UIFactory.CreateText(card,
                $"{s.echelon}  ·  STR {s.strength * 100f:0}%  ·  {s.status}",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(meta.rectTransform, CardTextX, 41f, cardW, 15f);
            UIFactory.Fit(meta);

            var kebab = UIFactory.CreateIconButton(card, UiIcons.Kebab,
                () => RemoveUnitRequested?.Invoke(actor), new Color(0, 0, 0, 0), UiTheme.TextFaint, 7f);
            UIFactory.Place((RectTransform)kebab.transform, new Vector2(1f, 0.5f), new Vector2(-6, 0), new Vector2(26, 26));
        }

        /// <summary>Framed unit icon, or a visible gap marker when the sprite is missing.</summary>
        Sprite CardIcon(RectTransform card, string folder, string unitId)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                icon.raycastTarget = false;
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(CardIconX, 0),
                    new Vector2(CardIconSize, CardIconSize));
                return sprite;
            }

            // Keep the layout intact and visibly flag the gap.
            var fallback = UIFactory.CreatePanel(card, "IconFallback", UiTheme.Panel);
            UIFactory.Place(fallback, new Vector2(0f, 0.5f), new Vector2(CardIconX, 0),
                new Vector2(CardIconSize, CardIconSize));
            var mark = UIFactory.CreateText(fallback, "?", 16, UiTheme.TextFaint, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(mark.rectTransform);
            return null;
        }

        /// <summary>"Mechanised infantry" → "MECH INF": a call-sign-length label.</summary>
        static string Abbreviate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "UNIT";
            var parts = name.Split(' ');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(p.Length <= 4 ? p.ToUpperInvariant()
                                        : p.Substring(0, 4).ToUpperInvariant());
                if (sb.Length >= 12) break;
            }
            return sb.ToString();
        }

        static void AddEvent(EventTrigger t, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(cb);
            t.triggers.Add(entry);
        }

        // ------------------------------------------------------- tool strip

        void BuildToolStrip(RectTransform panel)
        {
            var strip = UIFactory.CreatePanel(panel, "ToolStrip", UiTheme.Chrome);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.sizeDelta = new Vector2(0, ToolStripHeight);

            var rule = UIFactory.CreateDivider(strip, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = Vector2.zero;

            // Names the icon row. It used to sit under a full panel of labelled
            // controls that gave it context; alone at the foot of the rail it
            // reads as unexplained glyphs without this.
            var caption = UIFactory.CreateSectionHeader(strip, "TOOLS", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(caption.rectTransform, Pad, 8f, RailWidth - Pad * 2f, 14f);

            // Three tools, not five. The pencil and the square drew control
            // measures by hand; that whole feature is gone — see the class
            // remarks and docs/03-GAMEPLAY.md. Only the cursor latches now.
            AddTool(strip, 0, UiIcons.Cursor, () => SelectToolRequested?.Invoke());
            AddTool(strip, 1, UiIcons.Pin, () => GenerateSectorsRequested?.Invoke());
            AddTool(strip, 2, UiIcons.Chart, ToggleView);

            SetActiveTool(0);
        }

        void AddTool(RectTransform strip, int index, Sprite glyph, UnityEngine.Events.UnityAction action)
        {
            // Anchored to the strip's bottom, clear of the caption band above.
            var frame = UIFactory.CreateBorderedPanel(strip, "Tool" + index, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 0f), new Vector2(Pad + index * 42f, 10), new Vector2(36, 32));

            int captured = index;
            var btn = UIFactory.CreateIconButton(frame, glyph, () =>
            {
                // Sector generation and the view toggle are one-shot commands,
                // not modes — only the cursor latches.
                if (captured == 0) SetActiveTool(captured);
                action();
            }, new Color(0, 0, 0, 0), UiTheme.TextDim, 8f);
            UIFactory.Stretch((RectTransform)btn.transform);

            // Find the glyph by name: GetComponentInChildren searches the object
            // itself first and would hand back the button's own background.
            _tools.Add((frame.Find("Fill").GetComponent<Image>(),
                        btn.transform.Find("Glyph").GetComponent<Image>()));
        }

        void SetActiveTool(int index)
        {
            _activeTool = index;
            for (int i = 0; i < _tools.Count; i++)
            {
                bool on = i == index;
                _tools[i].fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                _tools[i].glyph.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        /// <summary>Puts the cursor tool back on top. Kept for callers that end a mode.</summary>
        public void ResetToolToSelect() => SetActiveTool(0);

        // ----------------------------------------------------- effects section

        /// <summary>
        /// Hand-placed effects: arm one, then click the terrain. Named EFFECTS
        /// rather than "Particles" because that is what they are to the player —
        /// how they are drawn is an implementation detail, and the same section
        /// would hold a decal or a mesh effect later.
        /// </summary>
        // ------------------------------------------------- sustainment section

        IronMeridian.Logistics.SustainmentSystem _sustainment;

        /// <summary>Fill the shown side's stocks from what it has deployed.</summary>
        public System.Action<Team, float> StockFromForceRequested;

        /// <summary>The period the consumption column is stated over.</summary>
        enum BurnPeriod { Day, Week, Month }
        BurnPeriod _burnPeriod = BurnPeriod.Day;

        static float BurnDays(BurnPeriod p) => p == BurnPeriod.Day ? 1f : p == BurnPeriod.Week ? 7f : 30f;
        static string BurnWord(BurnPeriod p) => p == BurnPeriod.Day ? "day" : p == BurnPeriod.Week ? "week" : "month";

        readonly List<(ResourceKind kind, InputField field, Text detail)> _resourceRows =
            new List<(ResourceKind, InputField, Text)>();
        readonly List<(BurnPeriod period, Image fill, Text label)> _burnTabs =
            new List<(BurnPeriod, Image, Text)>();
        Text _manpowerFigure, _manpowerDetail, _sustainSide, _sustainVerdict;
        /// <summary>Suppresses the write-back while the fields are being filled from the model.</summary>
        bool _sustainSyncing;

        /// <summary>Height of the SUSTAINMENT page inside its scroll view.</summary>
        const float SustainmentPageHeight = 236f + 9f * 58f + 120f;

        /// <summary>
        /// The force's stocks, its burn rate and how long it can go on.
        ///
        /// **Called SUSTAINMENT rather than RESOURCES.** Resources is what a
        /// strategy game calls the numbers in the corner of the screen;
        /// sustainment is what an army calls keeping a force in the field, and
        /// it is the right word for a panel that is about fuel, ammunition
        /// natures, replacements and rations. It also keeps the two logistic
        /// sections distinct at a glance: LOGISTICS is *where the supply is*,
        /// SUSTAINMENT is *how much of it there is*.
        ///
        /// **Stocks are typed; burn rates are not.** Every consumption figure on
        /// this page is arithmetic over the units on the map — see
        /// <see cref="IronMeridian.Logistics.SustainmentSystem"/>. Nobody can
        /// type a rate, so a scenario cannot state a burn that disagrees with
        /// its own order of battle.
        ///
        /// See docs/27-SUSTAINMENT.md.
        /// </summary>
        void BuildSustainmentSection(RectTransform section)
        {
            var content = ScrollableSection(section, SustainmentPageHeight);

            SectionLabel(content, "FORCE ON THE MAP", -8);

            _sustainSide = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_sustainSide.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad + InnerWidth - 110f, -8), new Vector2(110, 18));

            // The head count is the one figure on this page that is not a stock
            // at all, so it gets its own card rather than a row in the table.
            var head = UIFactory.CreateBorderedPanel(content, "Manpower", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(head, new Vector2(0f, 1f), new Vector2(Pad, -28), new Vector2(InnerWidth, 58));

            _manpowerFigure = UIFactory.CreateText(head, "—", 26, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_manpowerFigure.rectTransform, new Vector2(0f, 1f),
                new Vector2(12, -6), new Vector2(InnerWidth - 24f, 30));
            UIFactory.Fit(_manpowerFigure, 14);

            _manpowerDetail = UIFactory.CreateText(head, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_manpowerDetail.rectTransform, new Vector2(0f, 1f),
                new Vector2(12, -38), new Vector2(InnerWidth - 24f, 14));
            UIFactory.Fit(_manpowerDetail, 8);

            // Period tabs rather than three columns: the panel is 250 px wide,
            // and a day / week / month figure side by side would be three
            // unreadable numbers instead of one legible one.
            SectionLabel(content, "CONSUMPTION PER", -96);
            float third = (InnerWidth - 8f) / 3f;
            BurnTab(content, BurnPeriod.Day, "DAY", 0, third, -116);
            BurnTab(content, BurnPeriod.Week, "WEEK", 1, third, -116);
            BurnTab(content, BurnPeriod.Month, "MONTH", 2, third, -116);

            SectionLabel(content, "STOCKS", -160);

            float y = -180f;
            foreach (var def in ResourceCatalog.All)
            {
                ResourceRow(content, def, y);
                y -= 58f;
            }

            _sustainVerdict = UIFactory.CreateText(content, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.Place(_sustainVerdict.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 34));

            var fill = UIFactory.CreateBorderedPanel(content, "StockFromForce", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(fill, new Vector2(0f, 1f), new Vector2(Pad, y - 44f), new Vector2(InnerWidth, 32));
            var fillBtn = UIFactory.CreateButton(fill, "STOCK 7 DAYS FROM FORCE",
                () => StockFromForceRequested?.Invoke(_team, 7f),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)fillBtn.transform);
            UiTooltip.Attach(fillBtn.gameObject,
                "Fills every stock with a week of this side's current burn", UiTooltip.Side.Left);

            var hint = UIFactory.CreateText(content,
                "Stocks are yours to set and are saved with the map. Consumption is not typed — it is " +
                "worked out from the formations this side has deployed, at their echelon and current " +
                "strength, so it moves the moment the order of battle does.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 84f),
                new Vector2(InnerWidth, 76));

            RefreshSustainment();
        }

        void BurnTab(RectTransform content, BurnPeriod period, string label, int index,
            float width, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Burn_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(Pad + index * (width + 4f), y), new Vector2(width, 28));

            var captured = period;
            var btn = UIFactory.CreateButton(frame, label,
                () => { _burnPeriod = captured; RefreshSustainment(); },
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);

            _burnTabs.Add((period, frame.Find("Fill").GetComponent<Image>(),
                btn.GetComponentInChildren<Text>(true)));
        }

        /// <summary>
        /// One stock line: what it is, an editable figure, and what it costs.
        /// The stock field is on the right where every editable value in this
        /// interface is, and the derived numbers sit under the name as prose —
        /// a table of six columns at this width would be six columns of nothing.
        /// </summary>
        void ResourceRow(RectTransform content, ResourceDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Res_" + def.kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var pip = UIFactory.CreatePanel(frame, "Pip", def.tint);
            pip.anchorMin = new Vector2(0, 0); pip.anchorMax = new Vector2(0, 1);
            pip.pivot = new Vector2(0, 0.5f);
            pip.sizeDelta = new Vector2(3, -10);
            pip.GetComponent<Image>().raycastTarget = false;

            var title = UIFactory.CreateText(frame, def.name, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, 12f, 6f, InnerWidth - 122f, 16f);
            UIFactory.Fit(title, 8);

            var detail = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(detail.rectTransform, 12f, 24f, InnerWidth - 122f, 24f);
            UIFactory.Fit(detail, 7);

            var field = UIFactory.CreateInputField(frame, "0", 13);
            UIFactory.Place((RectTransform)field.transform, new Vector2(1f, 0.5f),
                new Vector2(-10, 0), new Vector2(96, 28));
            field.contentType = InputField.ContentType.DecimalNumber;

            var kind = def.kind;
            field.onEndEdit.AddListener(text =>
            {
                if (_sustainSyncing || _sustainment == null) return;
                // A malformed number leaves the stock alone rather than zeroing
                // it — half-typed input is not an instruction to empty a depot.
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    RefreshSustainment();
                    return;
                }
                _sustainment.SetStock(_team, kind, value);
            });

            _resourceRows.Add((kind, field, detail));
        }

        /// <summary>
        /// Repaints every figure on the page from the system and the team tab.
        /// Public because the controller's own actions — filling stocks, loading
        /// a map — change what it shows.
        /// </summary>
        public void RefreshSustainment()
        {
            if (_sustainment == null || _manpowerFigure == null) return;

            bool enemy = _team == Team.Enemy;
            if (_sustainSide != null)
            {
                _sustainSide.text = enemy ? "ENEMY" : "FRIENDLY";
                _sustainSide.color = enemy ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }

            int onField = _sustainment.ManpowerOnField(_team);
            int establishment = _sustainment.EstablishmentOnField(_team);
            int formations = _sustainment.FormationsOnField(_team);

            _manpowerFigure.text = $"{onField:n0} ON FIELD";
            _manpowerDetail.text = formations == 0
                ? "Nothing deployed for this side."
                : $"{formations} formation(s)  ·  {establishment:n0} at establishment  ·  " +
                  $"{(establishment > 0 ? onField * 100f / establishment : 0f):0}% strength";

            foreach (var (period, fill, label) in _burnTabs)
            {
                bool on = period == _burnPeriod;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            float days = BurnDays(_burnPeriod);
            string word = BurnWord(_burnPeriod);

            _sustainSyncing = true;
            foreach (var (kind, field, detail) in _resourceRows)
            {
                var def = ResourceCatalog.Get(kind);
                double stock = _sustainment.Stock(_team, kind);
                double burn = _sustainment.DailyUse(_team, kind) * days;
                double left = _sustainment.DaysOfSupply(_team, kind);

                field.text = stock.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                detail.text = $"{def.measure}  ·  {burn:n0} per {word}\n{DaysLeftText(left, burn)}";
            }
            _sustainSyncing = false;

            var (worst, worstDays) = _sustainment.BindingConstraint(_team);
            if (_sustainVerdict == null) return;

            if (worst == null || double.IsPositiveInfinity(worstDays))
            {
                _sustainVerdict.text = "Nothing deployed is consuming anything.";
                _sustainVerdict.color = UiTheme.TextFaint;
            }
            else
            {
                // The binding constraint is the whole point of the page: a force
                // is sustained for as long as its *shortest* stock lasts, and
                // nine figures without that sentence is nine figures.
                _sustainVerdict.text =
                    $"Sustained for {worstDays:0.#} day(s) — {ResourceCatalog.Get(worst.Value).name} runs out first.";
                _sustainVerdict.color = worstDays < 2.0 ? UiTheme.Danger
                    : worstDays < 7.0 ? UiTheme.Warning : UiTheme.Success;
            }
        }

        static string DaysLeftText(double days, double burn)
        {
            if (burn <= 0.0) return "not consumed by this force";
            if (double.IsPositiveInfinity(days)) return "not consumed by this force";
            if (days < 0.05) return "EXHAUSTED";
            return $"{days:0.#} day(s) of supply";
        }

        // ----------------------------------------------- reinforcements section

        ReinforcementSystem _reinforcements;
        RectTransform _reinforceList;
        Text _reinforceCount, _reinforceSide, _reinforceArrival;
        Button _reinforceAvailableTab, _reinforceScheduledTab;
        InputField _reinforceSearch;
        ListMode _reinforceMode = ListMode.Available;
        string _reinforceQuery = "";
        /// <summary>Arrival time the next scheduled formation is given, minutes after the battle starts.</summary>
        int _reinforceMinutes = 30;
        readonly HashSet<UnitBranch> _reinforceOpenBranches = new HashSet<UnitBranch>();

        /// <summary>
        /// **REINFORCEMENTS** — the same panel as UNITS, for formations that are
        /// not here yet.
        ///
        /// Deliberately the same UI, control for control: the blue/red tabs, the
        /// search box, the AVAILABLE / SCHEDULED pair of tabs, and the same
        /// branch accordion over the same 117 unit types. A designer choosing a
        /// counter-attack battalion is doing exactly what they do when they
        /// deploy one, and making them learn a second way to pick a unit would
        /// be inventing a difference that is not there.
        ///
        /// The one thing that *is* different is the verb. UNITS drags a
        /// formation onto the ground; this one gives it an **arrival time** —
        /// click a type and it joins the schedule at H+n, to arrive in its
        /// side's deployment zone (docs/22-MISSIONS.md §1c). That is the whole
        /// feature: the same force, entered at a different moment.
        ///
        /// See docs/30-REINFORCEMENTS.md.
        /// </summary>
        void BuildReinforcementSection(RectTransform content)
        {
            // Team tabs, exactly as UNITS has them.
            float half = (InnerWidth - 6f) / 2f;
            var blue = UIFactory.CreateBorderedPanel(content, "ReinBlue", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(blue, new Vector2(0f, 1f), new Vector2(Pad, -8), new Vector2(half, 30));
            var blueBtn = UIFactory.CreateButton(blue, "FRIENDLY", () => SetTeam(Team.User),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)blueBtn.transform);
            _reinforceBlueFill = blue.Find("Fill").GetComponent<Image>();

            var red = UIFactory.CreateBorderedPanel(content, "ReinRed", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(red, new Vector2(0f, 1f), new Vector2(Pad + half + 6f, -8), new Vector2(half, 30));
            var redBtn = UIFactory.CreateButton(red, "ENEMY", () => SetTeam(Team.Enemy),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)redBtn.transform);
            _reinforceRedFill = red.Find("Fill").GetComponent<Image>();

            _reinforceSearch = UIFactory.CreateInputField(content, "Search unit types…", UiTheme.FontSmall);
            UIFactory.Place((RectTransform)_reinforceSearch.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -46), new Vector2(InnerWidth, 30));
            _reinforceSearch.onValueChanged.AddListener(text =>
            {
                _reinforceQuery = text ?? "";
                PopulateReinforcements();
            });

            // Arrival time for the next pick. A stepper rather than a text
            // field: the figure is always a round number of minutes, and typing
            // one would be three keystrokes for a decision worth one.
            var timeFrame = UIFactory.CreateBorderedPanel(content, "ArrivalAt", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(timeFrame, new Vector2(0f, 1f), new Vector2(Pad, -84), new Vector2(InnerWidth, 34));

            var timeLabel = UIFactory.CreateText(timeFrame, "ARRIVES AT", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            timeLabel.raycastTarget = false;
            UIFactory.Place(timeLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(InnerWidth - 140f, 14));

            var minus = UIFactory.CreateButton(timeFrame, "−", () => StepArrival(-5),
                UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)minus.transform, new Vector2(1f, 0.5f),
                new Vector2(-88, 0), new Vector2(26, 24));

            _reinforceArrival = UIFactory.CreateText(timeFrame, "", UiTheme.FontSmall,
                UiTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _reinforceArrival.raycastTarget = false;
            UIFactory.Place(_reinforceArrival.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-46, 0), new Vector2(56, 16));

            var plus = UIFactory.CreateButton(timeFrame, "+", () => StepArrival(5),
                UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)plus.transform, new Vector2(1f, 0.5f),
                new Vector2(-10, 0), new Vector2(26, 24));

            // AVAILABLE / SCHEDULED, the same pair UNITS carries.
            _reinforceAvailableTab = ReinforceTab(content, "AVAILABLE", Pad,
                () => SetReinforceMode(ListMode.Available));
            _reinforceScheduledTab = ReinforceTab(content, "SCHEDULED", Pad + 88f,
                () => SetReinforceMode(ListMode.Deployed));

            _reinforceCount = UIFactory.CreateText(content, "0", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_reinforceCount.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -126), new Vector2(40, 18));

            _reinforceSide = UIFactory.CreateText(content, "", UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_reinforceSide.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad - 44f, -126), new Vector2(110, 18));

            var scroll = UIFactory.CreateScrollView(content, out _reinforceList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 42);
            srt.offsetMax = new Vector2(-Pad, -150);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _reinforceList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(2, 2, 2, 2); }

            var clear = UIFactory.CreateBorderedPanel(content, "ClearSchedule", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(clear, new Vector2(0f, 0f), new Vector2(Pad, 6), new Vector2(InnerWidth, 30));
            var clearBtn = UIFactory.CreateButton(clear, "CLEAR SCHEDULE",
                () => { if (_reinforcements != null) _reinforcements.Clear(); },
                new Color(0, 0, 0, 0), UiTheme.Danger, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)clearBtn.transform);

            SetReinforceMode(ListMode.Available);
        }

        Image _reinforceBlueFill, _reinforceRedFill;

        Button ReinforceTab(RectTransform content, string label, float x,
            UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(content, label, action, new Color(0, 0, 0, 0),
                UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f),
                new Vector2(x, -126), new Vector2(82, 22));
            return b;
        }

        void SetReinforceMode(ListMode mode)
        {
            _reinforceMode = mode;
            TintListTab(_reinforceAvailableTab, mode == ListMode.Available);
            TintListTab(_reinforceScheduledTab, mode == ListMode.Deployed);
            PopulateReinforcements();
        }

        void StepArrival(int minutes)
        {
            _reinforceMinutes = Mathf.Clamp(_reinforceMinutes + minutes, 0, 24 * 60);
            PopulateReinforcements();
        }

        /// <summary>
        /// Rebuilds whichever list is showing. Public because the schedule can
        /// change without the panel touching it — an arrival coming on during a
        /// battle takes itself off the pending list.
        /// </summary>
        public void RefreshReinforcements() => PopulateReinforcements();

        void PopulateReinforcements()
        {
            if (_reinforceList == null) return;

            if (_reinforceArrival != null)
                _reinforceArrival.text = _reinforceMinutes == 0 ? "H-HOUR" : $"H+{_reinforceMinutes}";
            if (_reinforceSide != null)
            {
                bool enemy = _team == Team.Enemy;
                _reinforceSide.text = enemy ? "ENEMY" : "FRIENDLY";
                _reinforceSide.color = enemy ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }
            if (_reinforceBlueFill != null)
                _reinforceBlueFill.color = _team == Team.User ? UiTheme.Friendly : UiTheme.Surface;
            if (_reinforceRedFill != null)
                _reinforceRedFill.color = _team == Team.Enemy ? UiTheme.Hostile : UiTheme.Surface;

            ClearChildren(_reinforceList);

            int count = _reinforceMode == ListMode.Available
                ? PopulateReinforceAvailable()
                : PopulateReinforceScheduled();

            if (_reinforceCount != null) _reinforceCount.text = count.ToString();
        }

        /// <summary>The catalogue, as the same branch accordion UNITS uses.</summary>
        int PopulateReinforceAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            bool searching = !string.IsNullOrEmpty(_reinforceQuery);
            int count = 0;

            foreach (var branch in UnitBranchInfo.All)
            {
                _branchMatches.Clear();
                foreach (var def in UnitDatabase.All)
                {
                    if (def.Branch != branch) continue;
                    if (!ReinforceMatches(def)) continue;
                    _branchMatches.Add(def);
                }
                if (_branchMatches.Count == 0) continue;

                count += _branchMatches.Count;

                bool open = searching || _reinforceOpenBranches.Contains(branch);
                ReinforceBranchHeader(branch, _branchMatches.Count, open);
                if (!open) continue;

                foreach (var def in _branchMatches) ReinforceCard(def, folder);
            }

            if (count == 0)
            {
                var empty = UIFactory.CreateText(_reinforceList,
                    "No unit type matches that search.", UiTheme.FontLabel,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 32);
            }
            return count;
        }

        bool ReinforceMatches(UnitDefinition def)
        {
            if (string.IsNullOrEmpty(_reinforceQuery)) return true;
            string q = _reinforceQuery.ToLowerInvariant();
            return (def.name != null && def.name.ToLowerInvariant().Contains(q)) ||
                   (def.id != null && def.id.ToLowerInvariant().Contains(q));
        }

        void ReinforceBranchHeader(UnitBranch branch, int count, bool open)
        {
            var row = UIFactory.CreateBorderedPanel(_reinforceList, "ReinBranch_" + branch,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 30);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() =>
            {
                if (!_reinforceOpenBranches.Remove(branch)) _reinforceOpenBranches.Add(branch);
                PopulateReinforcements();
            });

            var chevron = UIFactory.CreateText(row, open ? "▾" : "▸", UiTheme.FontSmall,
                open ? UiTheme.Accent : UiTheme.TextDim, TextAnchor.MiddleCenter);
            chevron.raycastTarget = false;
            UIFactory.Place(chevron.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(16f, 16f));

            var text = UIFactory.CreateSectionHeader(row,
                UnitBranchInfo.DisplayName(branch).ToUpperInvariant(),
                open ? UiTheme.Accent : UiTheme.Text);
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(30f, 0f),
                new Vector2(InnerWidth - 90f, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(40f, 16f));
        }

        /// <summary>
        /// One type, as a card. Clicking it schedules that type at the arrival
        /// time currently set — a click rather than a drag, because there is no
        /// ground to drag it to: where it lands is the deployment zone's
        /// business, not the cursor's.
        /// </summary>
        void ReinforceCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreateBorderedPanel(_reinforceList, "Rein_" + def.id,
                UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, 44);

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.onClick.AddListener(() =>
            {
                if (_reinforcements == null) return;
                _reinforcements.Add(def, _team, DefaultEchelon, _reinforceMinutes);
                SetReinforceMode(ListMode.Deployed);
            });

            var sprite = UIFactory.LoadIconSprite(folder, def.id);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                icon.raycastTarget = false;
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                    new Vector2(10, 0), new Vector2(34, 34));
            }

            UIFactory.CreateStackedLabels(card, def.name,
                $"{UnitBranchInfo.DisplayName(def.Branch)}   ·   {DefaultEchelon}",
                50f, InnerWidth - 110f, topInset: 5f);

            var at = UIFactory.CreateText(card,
                _reinforceMinutes == 0 ? "H-HOUR" : $"H+{_reinforceMinutes}",
                UiTheme.FontLabel, UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            at.raycastTarget = false;
            UIFactory.Place(at.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(52, 16));
        }

        /// <summary>The schedule itself, earliest first, with what each arrival is waiting on.</summary>
        int PopulateReinforceScheduled()
        {
            if (_reinforcements == null) return 0;

            int count = 0;
            double elapsed = _reinforcements.ElapsedMinutes;

            foreach (var entry in _reinforcements.Schedule)
            {
                if (entry.team != _team.ToString()) continue;
                var def = UnitDatabase.Get(entry.defId);
                if (def == null) continue;
                count++;

                var row = UIFactory.CreateBorderedPanel(_reinforceList, "Sched_" + count,
                    entry.arrived ? UiTheme.SurfaceSubtle : UiTheme.Surface, UiTheme.Border);
                row.sizeDelta = new Vector2(0, 44);

                var sprite = UIFactory.LoadIconSprite(_team == Team.User ? "Friendly" : "Enemy", def.id);
                if (sprite != null)
                {
                    var icon = UIFactory.CreateImage(row, sprite, "Icon");
                    icon.raycastTarget = false;
                    UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                        new Vector2(10, 0), new Vector2(30, 30));
                }

                // What it is waiting on, in the terms the designer set it in.
                string state = entry.arrived
                    ? "ARRIVED"
                    : elapsed > 0.0
                        ? $"in {Mathf.Max(0, Mathf.CeilToInt((float)(entry.arrivalMinutes - elapsed)))} min"
                        : $"{entry.echelon}";

                UIFactory.CreateStackedLabels(row, def.name, state, 46f, InnerWidth - 150f, topInset: 5f);

                var captured = entry;
                var earlier = UIFactory.CreateButton(row, "−", () => _reinforcements.Reschedule(captured, -5),
                    UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Place((RectTransform)earlier.transform, new Vector2(1f, 0.5f),
                    new Vector2(-84, 0), new Vector2(22, 22));

                var at = UIFactory.CreateText(row,
                    entry.arrivalMinutes == 0 ? "H" : $"H+{entry.arrivalMinutes}",
                    UiTheme.FontLabel, entry.arrived ? UiTheme.TextFaint : UiTheme.Accent,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                at.raycastTarget = false;
                UIFactory.Place(at.rectTransform, new Vector2(1f, 0.5f), new Vector2(-52, 0), new Vector2(44, 16));

                var later = UIFactory.CreateButton(row, "+", () => _reinforcements.Reschedule(captured, 5),
                    UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Place((RectTransform)later.transform, new Vector2(1f, 0.5f),
                    new Vector2(-30, 0), new Vector2(22, 22));

                var del = UIFactory.CreateButton(row, "✕", () => _reinforcements.Remove(captured),
                    new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f),
                    new Vector2(-6, 0), new Vector2(22, 22));
            }

            if (count == 0)
            {
                var empty = UIFactory.CreateText(_reinforceList,
                    "Nothing scheduled for this side. Pick a type in AVAILABLE and it joins the " +
                    "schedule at the arrival time above.",
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 48);
            }
            return count;
        }

        // ------------------------------------------------------ groups section

        /// <summary>Select every formation in a group.</summary>
        public System.Action<string> GroupSelectRequested;
        /// <summary>Select them and fly the camera so the whole group is framed.</summary>
        public System.Action<string> GroupFlyRequested;
        /// <summary>Put a group on the front line — see <c>GameController.ManTheFlot</c>.</summary>
        public System.Action<string> GroupFlotRequested;
        /// <summary>Release whichever group is holding the front line.</summary>
        public System.Action GroupFlotClearRequested;

        RectTransform _groupsList;
        Text _modeHeading;
        Text _groupsFlotState;
        string _flotHolder = "";

        /// <summary>
        /// The order of battle as the player has grouped it, and the one thing
        /// you can do to a group that is not an order: put it on the front line.
        ///
        /// **Why this is not the group panel.** The panel on the right describes
        /// *the current selection* — it appears when two things are selected and
        /// goes when they are not. This is the opposite question: what groups
        /// exist on this map, and where are they? A commander asks that without
        /// having selected anything, which is exactly when the right-hand panel
        /// is not there.
        ///
        /// See docs/03-GAMEPLAY.md § Groups.
        /// </summary>
        void BuildGroupsSection(RectTransform content)
        {
            SectionLabel(content, "FRONT LINE", -8);

            var flotFrame = UIFactory.CreateBorderedPanel(content, "FlotHolder", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(flotFrame, new Vector2(0f, 1f), new Vector2(Pad, -28), new Vector2(InnerWidth, 40));

            _groupsFlotState = UIFactory.CreateText(flotFrame, "", UiTheme.FontSmall, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_groupsFlotState.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(InnerWidth - 82f, 30));
            UIFactory.Fit(_groupsFlotState, 8);

            var release = UIFactory.CreateButton(flotFrame, "RELEASE",
                () => GroupFlotClearRequested?.Invoke(), UiTheme.SurfaceHover, UiTheme.TextDim, 10);
            UIFactory.Place((RectTransform)release.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(62, 24));

            SectionLabel(content, "GROUPS ON THIS MAP", -80);

            var scroll = UIFactory.CreateScrollView(content, out _groupsList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 76);
            srt.offsetMax = new Vector2(-Pad, -100);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _groupsList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(2, 2, 2, 2); }

            var hint = UIFactory.CreateText(content,
                "Click a group to select it, ◎ to fly to it. FLOT sends the whole group to the front " +
                "line: its formations are spread evenly along the line and each digs in on its own " +
                "stretch, facing the enemy. Groups are named on the right-hand panel with two or more " +
                "formations selected.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 66));

            RefreshGroups();
        }

        // -------------------------------------------------- reserved sections

        /// <summary>
        /// Four sections that are a nav row and an empty page, and nothing else
        /// yet: STATS, ZONES, OBJECTS and SUPPLIES.
        ///
        /// **Why they exist before their contents do.** They were asked for as
        /// places to build in, and a named empty page is the cheapest way to
        /// hold ground in the nav: the row, the section enum, the panel and the
        /// title are all wired, so filling one is writing its controls and
        /// nothing else. Each says on its face that it is empty — a page that
        /// merely rendered blank would read as a section that had broken.
        ///
        /// Each names its nearest built neighbours where there are any, so the
        /// next person to fill one is told what already exists rather than
        /// building a second way of doing it.
        /// </summary>
        void BuildStatsSection(RectTransform content) => BuildEmptySection(content, "STATS",
            "Nothing is built into this section yet — it is a row, a page and a place to put the " +
            "scenario's figures.\n\nWhat is counted today is elsewhere: casualties are on TAB " +
            "(the losses list) and each side's stocks are under SUSTAINMENT.");

        void BuildZonesSection(RectTransform content) => BuildEmptySection(content, "ZONES",
            "Nothing is built into this section yet — it is a row, a page and a place to put areas " +
            "drawn on the map.\n\nThe areas that already exist are elsewhere: a mission's boundary, " +
            "its headquarters zones and its deployment zones are all under MISSIONS.");

        void BuildObjectsSection(RectTransform content) => BuildEmptySection(content, "OBJECTS",
            "Nothing is built into this section yet — it is a row, a page and a place to put things " +
            "placed on the map that are not formations.\n\nThe ones that already exist are " +
            "elsewhere: barrier plans are under MINES AND OBSTACLES and rear-area installations " +
            "are under LOGISTICS.");

        void BuildSuppliesSection(RectTransform content) => BuildEmptySection(content, "SUPPLIES",
            "Nothing is built into this section yet — it is a row, a page and a place to put " +
            "supply.\n\nWhat exists today is elsewhere: a side's stocks and their daily use are " +
            "under SUSTAINMENT, depots and supply points under LOGISTICS, and air-dropped loads " +
            "on the AIR SUPPLY fire menu.");

        /// <summary>
        /// A reserved section's whole page: its heading and one card saying what
        /// it is not yet. Shared, so the four cannot drift apart into four
        /// slightly different ways of saying "empty".
        /// </summary>
        void BuildEmptySection(RectTransform content, string label, string note)
        {
            SectionLabel(content, label, -8);

            var frame = UIFactory.CreateBorderedPanel(content, "Empty", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -30), new Vector2(InnerWidth, 190));

            var caption = UIFactory.CreateSectionHeader(frame, "EMPTY", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(caption.rectTransform, 12f, 12f, InnerWidth - 24f, 14f);

            var text = UIFactory.CreateText(frame, note, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(text.rectTransform, 12f, 34f, InnerWidth - 24f, 144f);
        }

        /// <summary>Names the group currently holding the front line, "" for none.</summary>
        public void SetFlotHolder(string groupName)
        {
            _flotHolder = groupName ?? "";
            if (_groupsFlotState != null)
                _groupsFlotState.text = string.IsNullOrEmpty(_flotHolder)
                    ? "No group is holding the FLOT."
                    : $"FLOT held by {_flotHolder}.";
        }

        /// <summary>
        /// Rebuilds the group list from the registry. Called when the section is
        /// opened and whenever the order of battle changes — a group is a
        /// property of the units in it, so there is no group list to subscribe
        /// to.
        /// </summary>
        public void RefreshGroups()
        {
            if (_groupsList == null) return;
            SetFlotHolder(_flotHolder);

            // Cheap when shut: the registry changes on every spawn, move and
            // casualty, and rebuilding a list nobody is looking at would be a
            // few dozen uGUI objects churned per combat tick.
            if (!_sectionContent.TryGetValue(Section.Groups, out var page) ||
                !page.gameObject.activeSelf) return;

            ClearChildren(_groupsList);

            var order = new List<string>();
            var names = new Dictionary<string, string>();
            var counts = new Dictionary<string, int>();
            var sides = new Dictionary<string, Team>();

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || string.IsNullOrEmpty(u.State.groupId)) continue;
                string id = u.State.groupId;
                if (!counts.ContainsKey(id))
                {
                    order.Add(id);
                    names[id] = string.IsNullOrEmpty(u.State.groupName) ? "Unnamed group" : u.State.groupName;
                    counts[id] = 0;
                    sides[id] = u.State.TeamEnum;
                }
                counts[id]++;
            }

            if (order.Count == 0)
            {
                var empty = UIFactory.CreateText(_groupsList,
                    "No groups yet. Select two or more formations and name them on the right.",
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 44);
                return;
            }

            foreach (var id in order) GroupRow(id, names[id], counts[id], sides[id]);
        }

        void GroupRow(string id, string name, int count, Team side)
        {
            var row = UIFactory.CreateBorderedPanel(_groupsList, "Group_" + id, UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 52);

            var select = UIFactory.CreateButton(row, "", () => GroupSelectRequested?.Invoke(id),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)select.transform);
            var caption = select.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            // Side stripe: which army this is, readable before the name is.
            var stripe = UIFactory.CreatePanel(row, "Side",
                side == Team.Enemy ? GameConfig.RedTeam : GameConfig.BlueTeam);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(3, -8);
            stripe.GetComponent<Image>().raycastTarget = false;

            UIFactory.CreateStackedLabels(row, name,
                $"{count} formation(s)   ·   {(side == Team.Enemy ? "ENEMY" : "FRIENDLY")}",
                12f, InnerWidth - 108f, topInset: 8f);

            var fly = UIFactory.CreateButton(row, "◎", () => GroupFlyRequested?.Invoke(id),
                UiTheme.SurfaceHover, UiTheme.Text, 12);
            UIFactory.Place((RectTransform)fly.transform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(26, 20));

            var flot = UIFactory.CreateButton(row, "FLOT", () => GroupFlotRequested?.Invoke(id),
                UiTheme.AccentWash, UiTheme.Accent, 10);
            UIFactory.Place((RectTransform)flot.transform, new Vector2(1f, 0f),
                new Vector2(-8, 8), new Vector2(58, 20));
            UiTooltip.Attach(flot.gameObject,
                "Put this group on the front line — one stretch each, facing the enemy",
                UiTooltip.Side.Left);
        }

        /// <summary>
        /// Shows or hides the battle-only chrome on the rail. Right now that is
        /// the GROUPS row: a group is something you command rather than
        /// something you author, and a row that did nothing for the whole of a
        /// scenario's layout would be a row in the way.
        /// </summary>
        public void SetBattleMode(bool running)
        {
            if (_modeHeading != null)
            {
                _modeHeading.text = running ? "BATTLE MODE" : "SCENARIO MODE";
                _modeHeading.color = running ? UiTheme.Success : UiTheme.TextDim;
            }

            // The two modes carry two different rails — see ApplyModeVisibility,
            // ScenarioSections and BattleSections.
            ApplyModeVisibility(running);

            if (running) RefreshGroups();
        }

        // ------------------------------------------- mines and obstacles section

        IronMeridian.Lines.ObstacleSystem _obstacles;
        readonly List<(ObstacleKind kind, Image fill, Text label)> _obstacleButtons =
            new List<(ObstacleKind, Image, Text)>();
        RectTransform _obstacleList;
        Text _obstacleCount, _obstacleSide;

        /// <summary>Drop every mine and obstacle graphic on the map.</summary>
        public System.Action ObstaclesClearRequested;
        /// <summary>Fly the camera to one.</summary>
        public System.Action<IronMeridian.Lines.ObstacleMarker> ObstacleFocusRequested;
        /// <summary>Take one off the map.</summary>
        public System.Action<IronMeridian.Lines.ObstacleMarker> ObstacleRemoveRequested;

        /// <summary>Height of the MINES AND OBSTACLES page inside its scroll view.</summary>
        const float ObstaclePageHeight = 8 * 50f + 2 * 24f + 520f;

        /// <summary>
        /// The barrier plan: mines, and the obstacles they are tied into.
        ///
        /// **Driven entirely from <see cref="ObstacleCatalog"/>**, under two
        /// headings — mines, then constructed obstacles. The split is not
        /// decoration: they are laid by different people at different times, and
        /// a designer thinking about a minefield is not thinking about a
        /// roadblock.
        ///
        /// Each button lays that type's **doctrinal graphic** on the ground:
        /// flat, sized in metres, and aligned on the bearing the camera is
        /// looking along — see <c>ObstacleMarker</c> for why flat and why
        /// metres. The team tab decides whose barrier it is, exactly as it does
        /// for the rear area.
        ///
        /// See docs/31-OBSTACLES.md.
        /// </summary>
        void BuildObstacleSection(RectTransform section)
        {
            var content = ScrollableSection(section, ObstaclePageHeight);

            SectionLabel(content, "LAY ON MAP", -8);

            _obstacleSide = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_obstacleSide.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad + InnerWidth - 110f, -8), new Vector2(110, 18));

            float y = -34f;
            bool started = false;
            ObstacleFamily family = ObstacleFamily.Mines;

            foreach (var def in ObstacleCatalog.All)
            {
                if (!started || family != def.family)
                {
                    family = def.family;
                    started = true;
                    SectionLabel(content, family == ObstacleFamily.Mines ? "MINES" : "OBSTACLES", y);
                    y -= 24f;
                }

                ObstacleButton(content, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(content, "StopLaying", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 4f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STOP LAYING",
                () => { if (_obstacles != null) _obstacles.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            _obstacleCount = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_obstacleCount.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 44f), new Vector2(InnerWidth, 16));

            var scroll = UIFactory.CreateScrollView(content, out _obstacleList, withScrollbar: true);
            UIFactory.Place((RectTransform)scroll.transform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 64f), new Vector2(InnerWidth, 180f));
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _obstacleList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 3; layout.padding = new RectOffset(2, 2, 2, 2); }

            var clear = UIFactory.CreateBorderedPanel(content, "ClearObstacles", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(clear, new Vector2(0f, 1f), new Vector2(Pad, y - 252f), new Vector2(InnerWidth, 30));
            var clearBtn = UIFactory.CreateButton(clear, "REMOVE ALL",
                () => ObstaclesClearRequested?.Invoke(),
                new Color(0, 0, 0, 0), UiTheme.Danger, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)clearBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Pick a type, then click the ground. The graphic is laid along the bearing the camera " +
                "is looking, so face the way the belt runs before placing it. The tool stays armed, " +
                "because a barrier is several graphics rather than one. Right-click or Esc stops. " +
                "Nothing enforces these yet: they are the barrier plan, drawn and saved with the map.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 290f),
                new Vector2(InnerWidth, 120));

            RefreshObstacles();
        }

        void ObstacleButton(RectTransform content, ObstacleDef def, float y)
        {
            var kind = def.kind;
            var frame = UIFactory.CreateBorderedPanel(content, "Obs_" + kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_obstacles != null) _obstacles.Toggle(kind); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, UiIcons.GlyphFor(kind), "Glyph");
            icon.color = def.tint;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(26, 26));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.name, def.detail,
                48f, InnerWidth - 100f, topInset: 6f);

            // The ground it covers, on the button: it is the figure that decides
            // whether one graphic or three are wanted.
            var width = UIFactory.CreateText(frame, $"{def.widthMeters:0} m", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            width.raycastTarget = false;
            UIFactory.Place(width.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(48, 14));

            _obstacleButtons.Add((kind, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's own state — it owns what is armed and what is laid.</summary>
        public void RefreshObstacles()
        {
            if (_obstacles == null) return;

            foreach (var (kind, fill, label) in _obstacleButtons)
            {
                bool on = _obstacles.Armed.HasValue && _obstacles.Armed.Value == kind;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_obstacleSide != null)
            {
                bool enemySide = _team == Team.Enemy;
                _obstacleSide.text = enemySide ? "FOR ENEMY" : "FOR FRIENDLY";
                _obstacleSide.color = enemySide ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }

            if (_obstacleList == null) return;

            int blue = _obstacles.CountFor(Team.User), red = _obstacles.CountFor(Team.Enemy);
            if (_obstacleCount != null)
                _obstacleCount.text = $"LAID — {blue} FRIENDLY · {red} ENEMY";

            ClearChildren(_obstacleList);

            foreach (var marker in _obstacles.Markers)
            {
                if (marker == null) continue;
                var def = ObstacleCatalog.Get(marker.Kind);
                bool enemy = marker.Data.team == Team.Enemy.ToString();

                var row = UIFactory.CreatePanel(_obstacleList, "ObsRow_" + marker.Data.id, UiTheme.SurfaceSubtle);
                row.sizeDelta = new Vector2(0, 30);

                var pip = UIFactory.CreateImage(row, UiIcons.GlyphFor(marker.Kind), "Glyph");
                pip.color = def.tint;
                pip.raycastTarget = false;
                UIFactory.Place((RectTransform)pip.transform, new Vector2(0f, 0.5f),
                    new Vector2(8, 0), new Vector2(16, 16));

                var label = UIFactory.CreateText(row,
                    $"{def.name}   ·   {marker.Data.headingDeg:000}°",
                    UiTheme.FontLabel, enemy ? GameConfig.RedTeam : GameConfig.BlueTeam,
                    TextAnchor.MiddleLeft);
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(30, 0);
                lr.offsetMax = new Vector2(-58, 0);
                UIFactory.Fit(label, 8);

                var captured = marker;
                var focus = UIFactory.CreateButton(row, "◎", () => ObstacleFocusRequested?.Invoke(captured),
                    UiTheme.SurfaceHover, UiTheme.Text, 12);
                UIFactory.Place((RectTransform)focus.transform, new Vector2(1f, 0.5f),
                    new Vector2(-30, 0), new Vector2(24, 24));

                var del = UIFactory.CreateButton(row, "✕", () => ObstacleRemoveRequested?.Invoke(captured),
                    new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 12);
                UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f),
                    new Vector2(-4, 0), new Vector2(24, 24));
            }
        }

        // -------------------------------------------------- logistics section

        IronMeridian.Logistics.LogisticsSystem _logistics;
        readonly List<(LogisticsKind kind, Image fill, Text label)> _logisticsButtons =
            new List<(LogisticsKind, Image, Text)>();
        RectTransform _logisticsList;
        Text _logisticsCount, _logisticsSide;

        /// <summary>
        /// The rear area, laid out on the map.
        ///
        /// **Driven entirely from <see cref="LogisticsCatalog"/>.** Six buttons
        /// are read off the catalogue's rows, so a seventh kind of installation
        /// appears here, on the map and in the save file without this method
        /// being touched — the same arrangement the artillery natures and the
        /// movement tasks use.
        ///
        /// **The team tab decides the side.** A scenario has two rear areas and
        /// the designer lays out both; rather than a second team control here,
        /// the panel follows the one already in UNITS, so whichever side you
        /// are deploying formations for is the side you are deploying its
        /// supply for.
        ///
        /// See docs/26-LOGISTICS.md.
        /// </summary>
        void BuildLogisticsSection(RectTransform content)
        {
            SectionLabel(content, "DEPLOY ON MAP", -8);

            // Which army the next site belongs to. The panel takes the side
            // from the UNITS tab rather than carrying its own control, so it
            // has to *say* which one that is — a deploy button whose side is
            // decided on another page is a button you press to find out.
            _logisticsSide = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_logisticsSide.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad + InnerWidth - 110f, -8), new Vector2(110, 18));

            float y = -28f;
            foreach (var def in LogisticsCatalog.All)
            {
                LogisticsButton(content, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(content, "StopDeploying", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 4f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STOP DEPLOYING",
                () => { if (_logistics != null) _logistics.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            float listTop = y - 44f;
            _logisticsCount = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_logisticsCount.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, listTop), new Vector2(InnerWidth, 16));

            // The deployed list takes whatever height is left rather than a
            // fixed block: it is the only part of this panel that grows.
            var scroll = UIFactory.CreateScrollView(content, out _logisticsList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 42);
            srt.offsetMax = new Vector2(-Pad, listTop - 18f);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _logisticsList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 3; layout.padding = new RectOffset(2, 2, 2, 2); }

            var clear = UIFactory.CreateBorderedPanel(content, "ClearLogistics", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(clear, new Vector2(0f, 0f), new Vector2(Pad, 6), new Vector2(InnerWidth, 30));
            var clearBtn = UIFactory.CreateButton(clear, "REMOVE ALL SITES",
                () => LogisticsClearRequested?.Invoke(),
                new Color(0, 0, 0, 0), UiTheme.Danger, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)clearBtn.transform);

            RefreshLogistics();
        }

        /// <summary>Drop every logistic installation on the map.</summary>
        public System.Action LogisticsClearRequested;

        void LogisticsButton(RectTransform content, LogisticsDef def, float y)
        {
            var kind = def.kind;
            var frame = UIFactory.CreateBorderedPanel(content, "Log_" + kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_logistics != null) _logistics.Toggle(kind); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, UiIcons.GlyphFor(kind), "Glyph");
            icon.color = def.tint;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.name, def.detail,
                46f, InnerWidth - 96f, topInset: 6f);

            // The ground it serves, stated on the button: it is the number that
            // decides where the site goes, so it belongs where the choice is made.
            var reach = UIFactory.CreateText(frame, $"{def.serviceRadiusKm:0.#} km",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Place(reach.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(44, 16));

            _logisticsButtons.Add((kind, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>
        /// Repaints from the system's own state — it owns what is armed and
        /// what is on the map, not the panel.
        /// </summary>
        public void RefreshLogistics()
        {
            if (_logistics == null) return;

            foreach (var (kind, fill, label) in _logisticsButtons)
            {
                bool on = _logistics.Armed.HasValue && _logistics.Armed.Value == kind;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_logisticsList == null) return;

            if (_logisticsSide != null)
            {
                bool enemySide = _team == Team.Enemy;
                _logisticsSide.text = enemySide ? "FOR ENEMY" : "FOR FRIENDLY";
                _logisticsSide.color = enemySide ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }

            int blue = _logistics.CountFor(Team.User), red = _logistics.CountFor(Team.Enemy);
            if (_logisticsCount != null)
                _logisticsCount.text = $"DEPLOYED — {blue} FRIENDLY · {red} ENEMY";

            ClearChildren(_logisticsList);

            foreach (var site in _logistics.Sites)
            {
                if (site == null) continue;
                var def = LogisticsCatalog.Get(site.Kind);
                bool enemy = site.Data.team == Team.Enemy.ToString();

                var row = UIFactory.CreatePanel(_logisticsList, "Site_" + site.Data.id, UiTheme.SurfaceSubtle);
                row.sizeDelta = new Vector2(0, 30);

                var pip = UIFactory.CreateImage(row, UiIcons.GlyphFor(site.Kind), "Glyph");
                pip.color = def.tint;
                pip.raycastTarget = false;
                UIFactory.Place((RectTransform)pip.transform, new Vector2(0f, 0.5f),
                    new Vector2(8, 0), new Vector2(16, 16));

                var label = UIFactory.CreateText(row,
                    $"{def.name}   ·   {site.Data.latitude:0.###}, {site.Data.longitude:0.###}",
                    UiTheme.FontLabel, enemy ? GameConfig.RedTeam : GameConfig.BlueTeam,
                    TextAnchor.MiddleLeft);
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(30, 0);
                lr.offsetMax = new Vector2(-58, 0);
                UIFactory.Fit(label, 8);

                var captured = site;
                var focus = UIFactory.CreateButton(row, "◎", () => LogisticsFocusRequested?.Invoke(captured),
                    UiTheme.SurfaceHover, UiTheme.Text, 12);
                UIFactory.Place((RectTransform)focus.transform, new Vector2(1f, 0.5f),
                    new Vector2(-30, 0), new Vector2(24, 24));

                var del = UIFactory.CreateButton(row, "✕", () => LogisticsRemoveRequested?.Invoke(captured),
                    new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 12);
                UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f),
                    new Vector2(-4, 0), new Vector2(24, 24));
            }
        }

        /// <summary>Fly the camera to a deployed site.</summary>
        public System.Action<IronMeridian.Logistics.LogisticsSite> LogisticsFocusRequested;
        /// <summary>Take one site off the map.</summary>
        public System.Action<IronMeridian.Logistics.LogisticsSite> LogisticsRemoveRequested;

        static void ClearChildren(RectTransform content)
        {
            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        void BuildEffectsSection(RectTransform content)
        {
            SectionLabel(content, "PLACE ON MAP", -8);

            EffectButton(content, VfxId.FireMedium, "FIRE", "Burning ground — loops until removed",
                UiIcons.Flame, new Color(1.00f, 0.55f, 0.15f), -30);
            EffectButton(content, VfxId.Explosion, "EXPLOSION", "Detonation, then a burning wreck",
                UiIcons.Burst, new Color(1.00f, 0.72f, 0.30f), -88);
            EffectButton(content, VfxId.SmokePlume, "SMOKE", "Rising column — loops until removed",
                UiIcons.SmokeStack, new Color(0.70f, 0.74f, 0.80f), -146);

            var stop = UIFactory.CreateBorderedPanel(content, "StopPlacing", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, -212), new Vector2(InnerWidth, 32));
            var stopBtn = UIFactory.CreateButton(stop, "STOP PLACING",
                () => { if (_effects != null) _effects.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Arm an effect, then click the terrain. A reticle tracks the real ground point, so what you " +
                "see is where it lands. Ground that has not streamed in yet is refused. " +
                "Right-click or Esc stops placing. Each effect carries its own positional sound.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -254), new Vector2(InnerWidth, 90));

            RefreshEffects();
        }

        void EffectButton(RectTransform content, VfxId id, string label, string detail,
            Sprite glyph, Color glyphColour, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Effect_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "", () => { if (_effects != null) _effects.Toggle(id); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.color = glyphColour;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, label, detail,
                46f, InnerWidth - 78f, topInset: 9f);

            // Speaker pip: every one of these carries audio.
            var pip = UIFactory.CreateText(frame, "♪", UiTheme.FontSmall, UiTheme.Accent, TextAnchor.MiddleRight);
            UIFactory.Place(pip.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(16, 16));

            _effectButtons.Add((id, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the tool's state — it owns what is armed, not the panel.</summary>
        void RefreshEffects()
        {
            if (_effects == null) return;
            foreach (var (id, fill, label) in _effectButtons)
            {
                bool on = _effects.Armed.HasValue && _effects.Armed.Value == id;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- artillery section

        ArtilleryStrikeSystem _artillery;
        readonly List<(ArtilleryCaliber caliber, Image fill, Text label)> _artilleryButtons =
            new List<(ArtilleryCaliber, Image, Text)>();
        readonly Dictionary<ArtilleryOrigin, RectTransform> _artilleryPages =
            new Dictionary<ArtilleryOrigin, RectTransform>();
        readonly List<(ArtilleryOrigin origin, Image fill, Text label)> _originTabs =
            new List<(ArtilleryOrigin, Image, Text)>();
        ArtilleryOrigin _artilleryOrigin = ArtilleryOrigin.Nato;

        /// <summary>
        /// Button glyph per nature. The catalogue owns the numbers; the UI owns
        /// the pictures. Chosen by kind and weight rather than by exact calibre,
        /// so a new nature gets a sensible icon without touching this.
        /// </summary>
        static Sprite CaliberGlyph(ArtilleryDef def)
        {
            if (def.kind == ArtilleryKind.Mortar) return UiIcons.MortarBomb;
            if (def.calibreMm <= 105) return UiIcons.ShellLight;
            if (def.calibreMm >= 152) return UiIcons.ShellHeavy;
            return UiIcons.ShellMedium;
        }

        /// <summary>
        /// The fire-support menu, driven entirely from <see cref="ArtilleryCatalog"/>.
        ///
        /// Fourteen natures will not fit in one column, and stacking them into a
        /// scroll would bury the choice that actually matters. They are split by
        /// **inventory** instead — NATO or Enemy — because that is the first
        /// decision a player makes and it halves the list. Within a page they run
        /// mortars then guns, ascending by calibre, so the beaten zone grows
        /// monotonically down the page and the trade-off between natures is
        /// legible without reading a word.
        /// </summary>
        void BuildArtillerySection(RectTransform content)
        {
            SectionLabel(content, "CALL FOR FIRE", -8);
            StrikeBudgetRow(content, -28f);

            BuildOriginTabs(content, -64f);

            // One page per inventory, both laid out at the same origin; only the
            // selected one is active.
            foreach (ArtilleryOrigin origin in System.Enum.GetValues(typeof(ArtilleryOrigin)))
            {
                var page = UIFactory.CreateGroup(content, "ArtyPage_" + origin);
                page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(1, 1);
                page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
                _artilleryPages[origin] = page;
                BuildOriginPage(page, origin);
            }

            ShowArtilleryOrigin(_artilleryOrigin);
            RefreshArtillery();
        }

        void BuildOriginTabs(RectTransform content, float y)
        {
            var origins = new[] { ArtilleryOrigin.Nato, ArtilleryOrigin.Enemy };
            var names = new[] { "NATO", "ENEMY" };
            float w = (InnerWidth - 6f) / 2f;

            for (int i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                var frame = UIFactory.CreateBorderedPanel(content, "Origin_" + names[i],
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 6f), y),
                    new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => ShowArtilleryOrigin(origin),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
                UIFactory.Stretch((RectTransform)btn.transform);

                _originTabs.Add((origin, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        void BuildOriginPage(RectTransform page, ArtilleryOrigin origin)
        {
            // Clear of the section label, the allowance readout and the tabs.
            float y = -102f;
            ArtilleryKind? lastKind = null;

            foreach (var def in ArtilleryCatalog.OfOrigin(origin))
            {
                // A heading each time the class changes: a mortar and a gun of
                // the same calibre are different weapons, and the list should say so.
                if (lastKind != def.kind)
                {
                    SectionLabel(page, def.kind == ArtilleryKind.Mortar ? "MORTARS" : "GUNS & HOWITZERS", y);
                    y -= 22f;
                    lastKind = def.kind;
                }

                ArtilleryButton(page, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(page, "StandDown", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STAND DOWN",
                () => { if (_artillery != null) _artillery.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(page,
                "Pick a nature, then click the map to place the target area. A ten second countdown runs in the " +
                "HUD, then " + ArtilleryCatalog.ShellsPerMission + " rounds land inside the circle. The number on " +
                "each button is that nature's beaten zone. A mission cannot be recalled once away — STAND DOWN " +
                "only clears the tube. Several can be in the air at once, so fire can be walked across a position.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 44f),
                new Vector2(InnerWidth, 130));
        }

        void ShowArtilleryOrigin(ArtilleryOrigin origin)
        {
            _artilleryOrigin = origin;
            foreach (var kv in _artilleryPages) kv.Value.gameObject.SetActive(kv.Key == origin);

            foreach (var (o, fill, label) in _originTabs)
            {
                bool on = o == origin;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        void ArtilleryButton(RectTransform content, ArtilleryDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Arty_" + def.caliber, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_artillery != null) _artillery.Toggle(def.caliber); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, CaliberGlyph(def), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(22, 22));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                40f, InnerWidth - 88f, topInset: 6f);

            // Beaten zone on the right. It is the number that decides which
            // nature to call for, so it belongs on the button rather than only
            // in the hint text.
            var radius = UIFactory.CreateText(frame, def.radiusMeters.ToString("0") + " m", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 6), new Vector2(52, 14));

            AllowanceLabel(frame, ArtilleryCatalog.BudgetKey(def.caliber), def.missions);

            _artilleryButtons.Add((def.caliber, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshArtillery()
        {
            if (_artillery == null) return;
            foreach (var (caliber, fill, label) in _artilleryButtons)
            {
                bool on = _artillery.Armed.HasValue && _artillery.Armed.Value == caliber;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- air strike section

        AirStrikeSystem _airStrike;
        readonly List<(StrikeAircraft aircraft, Image fill, Text label)> _airStrikeButtons =
            new List<(StrikeAircraft, Image, Text)>();

        /// <summary>
        /// Button glyph per airframe. The flying wing is the bomber's own
        /// silhouette; the other two borrow shapes that read at 24 px — a rotor
        /// disc for the helicopter, a swept dart for the fighter.
        /// </summary>
        static Sprite AirframeGlyph(StrikeAircraft aircraft) => aircraft switch
        {
            StrikeAircraft.AttackHelicopter => UiIcons.Helicopter,
            StrikeAircraft.StrikeFighter => UiIcons.Jet,
            _ => UiIcons.FlyingWing
        };

        /// <summary>
        /// The air-tasking menu. Same shape as the artillery panel because it is
        /// the same decision — pick a delivery means, then commit a piece of
        /// ground — and driven entirely from <see cref="AirStrikeCatalog"/>.
        /// </summary>
        void BuildAirStrikeSection(RectTransform content)
        {
            SectionLabel(content, "TASK AN AIRFRAME", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in AirStrikeCatalog.All)
            {
                AirStrikeButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "Abort", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_airStrike != null) _airStrike.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                $"Pick an airframe, then click the map to place the target area. A " +
                $"{AirStrikeCatalog.CountdownSeconds:0} second countdown runs in the HUD, then the aircraft " +
                $"runs in and releases {AirStrikeCatalog.BombsPerStrike} weapons in one pass. The stick walks " +
                "along its track, so the blasts follow the aeroplane rather than landing in a heap. The attack " +
                "heading is different every time. A tasked strike cannot be recalled — abort only clears the " +
                "airframe before it is sent.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 160));

            RefreshAirStrike();
        }

        void AirStrikeButton(RectTransform content, AircraftDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Air_" + def.label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_airStrike != null) _airStrike.Toggle(def.aircraft); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, AirframeGlyph(def.aircraft), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            var radius = UIFactory.CreateText(frame, $"{def.radiusMeters:0} m", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(52, 14));

            AllowanceLabel(frame, AirStrikeCatalog.BudgetKey(def.aircraft), def.missions);

            _airStrikeButtons.Add((def.aircraft, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshAirStrike()
        {
            if (_airStrike == null) return;
            foreach (var (aircraft, fill, label) in _airStrikeButtons)
            {
                bool on = _airStrike.Armed.HasValue && _airStrike.Armed.Value == aircraft;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- air supply section

        AirSupplySystem _airSupply;
        readonly List<(SupplyKind kind, Image fill, Text label)> _airSupplyButtons =
            new List<(SupplyKind, Image, Text)>();

        /// <summary>The load's own glyph — the same three the LOGISTICS panel uses.</summary>
        static Sprite SupplyGlyph(SupplyKind kind) => kind switch
        {
            SupplyKind.Ammo => UiIcons.Rounds,
            SupplyKind.Oil => UiIcons.FuelDrop,
            _ => UiIcons.MedicalCross
        };

        /// <summary>
        /// The airdrop menu, driven entirely from <see cref="AirSupplyCatalog"/>.
        ///
        /// **The one page in this dock that gives something.** It sits beside
        /// AIR STRIKE because the two are flown by the same kind of thing and
        /// tasked in exactly the same way — pick, place, wait, watch — and the
        /// pairing is the clearest way of saying that an aircraft overhead is
        /// not always bad news.
        ///
        /// The three loads carry the **same glyphs as the LOGISTICS panel's**
        /// ammunition, fuel and medical points, because that is precisely what a
        /// drop leaves on the ground: not an effect, a supply point that was not
        /// there before. See docs/29-AIR-SUPPLY.md.
        /// </summary>
        void BuildAirSupplySection(RectTransform content)
        {
            SectionLabel(content, "DROP SUPPLIES", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in AirSupplyCatalog.All)
            {
                AirSupplyButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "AbortSupply", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_airSupply != null) _airSupply.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                $"Pick a load, then click the map to place the drop zone. A " +
                $"{AirSupplyCatalog.CountdownSeconds:0} second countdown runs in the HUD, then a transport " +
                "runs in low and pushes its bundles out over the zone. Each canopy that lands leaves a " +
                "supply point on the map — the same object the LOGISTICS panel places by hand, with the " +
                "same icon, and removable the same way. The run-in heading is different every time.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 170));

            RefreshAirSupply();
        }

        void AirSupplyButton(RectTransform content, SupplyDropDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Supply_" + def.kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_airSupply != null) _airSupply.Toggle(def.kind); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, SupplyGlyph(def.kind), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            // Bundles, not a beaten zone: the figure that matters here is how
            // many supply points the mission leaves behind.
            var bundles = UIFactory.CreateText(frame, $"{def.bundles} bundles", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            bundles.raycastTarget = false;
            UIFactory.Place(bundles.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(60, 14));

            AllowanceLabel(frame, AirSupplyCatalog.BudgetKey(def.kind), def.missions);

            _airSupplyButtons.Add((def.kind, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshAirSupply()
        {
            if (_airSupply == null) return;
            foreach (var (kind, fill, label) in _airSupplyButtons)
            {
                bool on = _airSupply.Armed.HasValue && _airSupply.Armed.Value == kind;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- uav strike section

        UavStrikeSystem _uavStrike;
        readonly List<(UavType uav, Image fill, Text label)> _uavButtons =
            new List<(UavType, Image, Text)>();

        /// <summary>
        /// The unmanned menu. Kept separate from AIR STRIKE rather than folded
        /// into it, because what is being tasked is a different kind of thing: an
        /// airframe comes back and a loitering munition does not, so the two ask
        /// different questions of the player and are answered from different
        /// stocks. Driven entirely from <see cref="UavCatalog"/>.
        /// </summary>
        void BuildUavStrikeSection(RectTransform content)
        {
            SectionLabel(content, "TASK A UAV", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in UavCatalog.All)
            {
                UavButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "AbortUav", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_uavStrike != null) _uavStrike.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Pick a type, then click the map to place the objective. A ten second countdown runs in the HUD, " +
                "then the drone launches and flies in.\n\n" +
                "The attack types are expended on the target — one aircraft, one warhead, and nothing comes back. " +
                "Their blast is deliberately the smallest of any strike here: a loitering munition carries a few " +
                "kilograms, not a shell.\n\n" +
                "The RECONNAISSANCE DRONE carries no warhead. The ring under the cursor is the 10 km it will " +
                "uncover; it holds an orbit over the point for five operational minutes, lifts the fog off " +
                "everything inside that circle, and flies home. What it saw stays on the map as last-known " +
                "contacts. Turn FOG OF WAR on in GENERAL and start the battle, or there is nothing for it to " +
                "uncover.\n\n" +
                "Each type has its own allowance — the second figure on its button. Every sortie, " +
                "armed or not, spends one of them, and running one type out does not touch the others.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 300));

            RefreshUavStrike();
        }

        void UavButton(RectTransform content, UavDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Uav_" + def.uav, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_uavStrike != null) _uavStrike.Toggle(def.uav); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            // The recon type gets its own glyph. It is the one row in this menu
            // that does not end in an explosion, and a quadcopter icon shared
            // with the loitering munitions would be the menu saying otherwise.
            var icon = UIFactory.CreateImage(frame, def.isRecon ? UiIcons.ReconEye : UiIcons.Quadcopter, "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            // Metres for a warhead's beaten zone; kilometres for a search area.
            // Ten thousand metres is a number nobody reads as ten kilometres.
            string figure = def.isRecon
                ? $"{def.reconRadiusKm:0} km"
                : def.radiusMeters.ToString("0") + " m";
            var radius = UIFactory.CreateText(frame, figure, UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(52, 14));

            AllowanceLabel(frame, UavCatalog.BudgetKey(def.uav), def.missions);

            _uavButtons.Add((def.uav, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshUavStrike()
        {
            if (_uavStrike == null) return;
            foreach (var (uav, fill, label) in _uavButtons)
            {
                bool on = _uavStrike.Armed.HasValue && _uavStrike.Armed.Value == uav;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // -------------------------------------------------- navy strike section

        NavalStrikeSystem _naval;
        readonly List<(NavalGun gun, Image fill, Text label)> _navalButtons =
            new List<(NavalGun, Image, Text)>();
        readonly Dictionary<NavalOrigin, RectTransform> _navalPages =
            new Dictionary<NavalOrigin, RectTransform>();
        readonly List<(NavalOrigin origin, Image fill, Text label)> _navalTabs =
            new List<(NavalOrigin, Image, Text)>();
        NavalOrigin _navalOrigin = NavalOrigin.Nato;

        /// <summary>
        /// Glyph per gun. Chosen by weight, exactly as the artillery menu does:
        /// nine bespoke pictograms would be nine pictograms nobody could tell
        /// apart at 22 px, and what the player is choosing between is how heavy
        /// the shell is.
        /// </summary>
        static Sprite NavalGlyph(NavalGunDef def)
        {
            if (def.calibreMm <= 76) return UiIcons.ShellLight;
            if (def.calibreMm >= 127) return UiIcons.ShellHeavy;
            return UiIcons.ShellMedium;
        }

        /// <summary>
        /// Naval gunfire support, driven entirely from <see cref="NavalCatalog"/>.
        ///
        /// Same shape as the artillery menu — inventory tabs over a list of
        /// calibres — because it is the same decision made about a different
        /// kind of gun, and two fire menus that behaved differently would be two
        /// things to learn instead of one. The **fleets** split the list the way
        /// the artillery menu's inventories do: it is the first choice a player
        /// makes and it halves what they have to read.
        /// </summary>
        void BuildNavalStrikeSection(RectTransform content)
        {
            SectionLabel(content, "CALL FOR NAVAL GUNFIRE", -8);
            StrikeBudgetRow(content, -28f);

            BuildNavalTabs(content, -64f);

            foreach (NavalOrigin origin in System.Enum.GetValues(typeof(NavalOrigin)))
            {
                var page = UIFactory.CreateGroup(content, "NavyPage_" + origin);
                page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(1, 1);
                page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
                _navalPages[origin] = page;
                BuildNavalPage(page, origin);
            }

            ShowNavalOrigin(_navalOrigin);
            RefreshNavalStrike();
        }

        void BuildNavalTabs(RectTransform content, float y)
        {
            var origins = new[] { NavalOrigin.Nato, NavalOrigin.Enemy };
            var names = new[] { "NATO NAVY", "ENEMY NAVY" };
            float w = (InnerWidth - 6f) / 2f;

            for (int i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                var frame = UIFactory.CreateBorderedPanel(content, "NavyOrigin_" + names[i],
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 6f), y),
                    new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => ShowNavalOrigin(origin),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);

                _navalTabs.Add((origin, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        void BuildNavalPage(RectTransform page, NavalOrigin origin)
        {
            float y = -102f;

            foreach (var def in NavalCatalog.OfOrigin(origin))
            {
                NavalButton(page, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(page, "CheckFire", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "CHECK FIRE",
                () => { if (_naval != null) _naval.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(page,
                "Pick a gun, then click the map to place the target area. The ring under the cursor is that " +
                "gun's beaten zone — it is wider than a land gun's of the same calibre, because the rounds " +
                "come from a moving ship at extreme range. A ten second countdown runs in the HUD, then the " +
                "mission lands: every round is resolved where it actually falls, and each leaves its own " +
                "burst, smoke and report.\n\n" +
                "Naval mountings are automatic, so a mission is more rounds, faster, than a battery's five. " +
                "The number on each button is the beaten zone; the round count is on the line beneath it.\n\n" +
                "A mission cannot be recalled once away — CHECK FIRE only stands the gun down. Each mounting " +
                "has its own allowance, shown as the second figure on its button.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 44f),
                new Vector2(InnerWidth, 250));
        }

        void ShowNavalOrigin(NavalOrigin origin)
        {
            _navalOrigin = origin;
            foreach (var kv in _navalPages) kv.Value.gameObject.SetActive(kv.Key == origin);

            foreach (var (o, fill, label) in _navalTabs)
            {
                bool on = o == origin;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        void NavalButton(RectTransform content, NavalGunDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Navy_" + def.gun,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_naval != null) _naval.Toggle(def.gun); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, NavalGlyph(def), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(22, 22));

            // The round count moves into the detail line. It is a fixed property
            // of the mounting — it never changes while you play — so it belongs
            // with the prose that describes the gun, and it frees the right-hand
            // column for the two figures that do change the decision: the beaten
            // zone and how many missions are left.
            var (name, _) = UIFactory.CreateStackedLabels(frame,
                def.label, $"{def.detail}  ·  {def.roundsPerMission} rds",
                40f, InnerWidth - 88f, topInset: 6f);

            var radius = UIFactory.CreateText(frame, $"{def.radiusMeters:0} m",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 6),
                new Vector2(52, 14));

            AllowanceLabel(frame, NavalCatalog.BudgetKey(def.gun), def.missions);

            _navalButtons.Add((def.gun, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshNavalStrike()
        {
            if (_naval == null) return;
            foreach (var (gun, fill, label) in _navalButtons)
            {
                bool on = _naval.Armed.HasValue && _naval.Armed.Value == gun;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // ----------------------------------------------------- players section

        PlayerPanel _players;

        /// <summary>
        /// Who is fighting this scenario. Built by <see cref="PlayerPanel"/>
        /// rather than inline, for the same reason the commanders section is:
        /// it is a small application of its own and this file is long enough.
        /// See docs/25-PLAYERS.md.
        /// </summary>
        void BuildPlayersSection(RectTransform content)
        {
            _players = new PlayerPanel(content);
            _players.Flash = m => DropRejected?.Invoke(m);
            _players.Build();
        }

        // -------------------------------------------------- commanders section

        /// <summary>Put the map's current selection under this officer.</summary>
        public System.Action<CommanderState> CommanderAssignRequested;
        /// <summary>Select this officer's formations on the map.</summary>
        public System.Action<CommanderState> CommanderSelectUnitsRequested;

        CommanderPanel _commanders;

        /// <summary>
        /// The order of battle above the units. Built by <see cref="CommanderPanel"/>
        /// rather than here: it is the first section that is a small application
        /// of its own, and this file is long enough that a fourteenth inline
        /// builder would be the one nobody could find.
        /// </summary>
        void BuildCommandersSection(RectTransform content)
        {
            _commanders = new CommanderPanel(content);
            _commanders.AssignSelectionRequested = c => CommanderAssignRequested?.Invoke(c);
            _commanders.SelectUnitsRequested = c => CommanderSelectUnitsRequested?.Invoke(c);
            _commanders.Flash = m => DropRejected?.Invoke(m);
            _commanders.Build();
        }

        /// <summary>Repaints the commanders section — the controller calls it after an assignment.</summary>
        public void RefreshCommanders() => _commanders?.Rebuild();

        // ---------------------------------------------------- missions section

        /// <summary>Open this mission in the editor: load its map and settings.</summary>
        public System.Action<MissionDefinition> MissionOpenRequested;
        /// <summary>Write the mission record **and** the current map to its file.</summary>
        public System.Action<MissionDefinition> MissionSaveRequested;
        /// <summary>Create a mission here, in this campaign, with this name.</summary>
        public System.Action<Campaign, string> MissionCreateRequested;
        /// <summary>Remove the mission from the campaign list.</summary>
        public System.Action<MissionDefinition> MissionDeleteRequested;

        Dropdown _campaignDropdown, _missionDropdown;
        InputField _missionName, _missionLocation, _missionBriefing;
        InputField _missionLat, _missionLon, _missionAltitude;
        RectTransform _missionFogLamp;
        Text _missionFogLabel, _missionStatus;
        Campaign _missionCampaign = Campaign.WestEurope;
        MissionDefinition _mission;
        List<MissionDefinition> _missionsShown = new List<MissionDefinition>();
        /// <summary>True while the panel is writing its own controls, so their events are not edits.</summary>
        bool _missionSyncing;

        /// <summary>
        /// The single-player mission editor.
        ///
        /// **Why the campaign browser lives in the map editor at all.** A mission
        /// is a piece of ground with an order of battle on it, and the editor is
        /// the only place that ground can be laid out. Putting the mission's own
        /// fields anywhere else would mean editing the scenario in one screen and
        /// its name and start point in another, with a step in between to keep
        /// them together — and that step is exactly what goes wrong. Here, SAVE
        /// writes both files the game reads, so there is nothing to keep in sync.
        ///
        /// **Two dropdowns rather than one long list.** Campaign first, then its
        /// missions, because the campaign is what the player's own screens are
        /// organised by: a flat list of every mission in the game would let you
        /// pick one without noticing which board it will appear on.
        ///
        /// See docs/22-MISSIONS.md.
        /// </summary>
        void BuildMissionsSection(RectTransform section)
        {
            // The only section that outgrew the panel. Its controls are placed
            // at absolute offsets like every other section's, so rather than
            // reflowing them into a layout group the whole page is put inside a
            // scroll view of a fixed height — the offsets stay meaningful and
            // the content stops running off the bottom of a 1080 window.
            var content = ScrollableSection(section, MissionsPageHeight);

            SectionLabel(content, "CAMPAIGN", -8);

            _campaignDropdown = UIFactory.CreateDropdown(content, CampaignNames(), 0, OnCampaignPicked);
            StyleDropdown(_campaignDropdown, -28);

            SectionLabel(content, "MISSION", -74);

            _missionDropdown = UIFactory.CreateDropdown(content, new List<string> { "—" }, 0, OnMissionPicked);
            StyleDropdown(_missionDropdown, -94);

            // OPEN is separate from picking one in the dropdown on purpose:
            // choosing a mission to edit its fields is cheap, and loading its
            // map throws away whatever is on the editor's map right now.
            var open = UIFactory.CreateBorderedPanel(content, "OpenMission", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(open, new Vector2(0f, 1f), new Vector2(Pad, -134), new Vector2(InnerWidth, 32));
            var openBtn = UIFactory.CreateButton(open, "OPEN IN EDITOR",
                () => { if (_mission != null) MissionOpenRequested?.Invoke(_mission); },
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)openBtn.transform);

            // --- fields ---
            SectionLabel(content, "MISSION NAME", -178);
            _missionName = MissionField(content, "e.g. Berlin", -198);

            SectionLabel(content, "LOCATION", -238);
            _missionLocation = MissionField(content, "e.g. Berlin, Germany", -258);

            SectionLabel(content, "BRIEFING", -298);
            _missionBriefing = MissionField(content, "One line on what this is about", -318);

            SectionLabel(content, "START POINT", -358);
            float half = (InnerWidth - 6f) / 2f;
            _missionLat = MissionField(content, "latitude", -378, Pad, half);
            _missionLon = MissionField(content, "longitude", -378, Pad + half + 6f, half);

            SectionLabel(content, "START ALTITUDE (M)", -418);
            _missionAltitude = MissionField(content, "12000", -438);

            _missionFogLamp = ToggleRow(content, "FOG OF WAR", -482, () =>
            {
                if (_mission == null) return;
                _mission.fogOfWar = !_mission.fogOfWar;
                RefreshMissionFields();
            }, out _missionFogLabel);

            BuildMissionAreaBlock(content);
            BuildHqZoneBlock(content);
            BuildDeploymentBlock(content);

            // --- actions ---
            var save = UIFactory.CreateBorderedPanel(content, "SaveMission", UiTheme.Success, UiTheme.Success);
            UIFactory.Place(save, new Vector2(0f, 1f), new Vector2(Pad, -HqBlockBottom - 12f), new Vector2(InnerWidth, 36));
            var saveBtn = UIFactory.CreateButton(save, "SAVE MISSION + MAP", CommitMission,
                new Color(0, 0, 0, 0), Color.white, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)saveBtn.transform);

            MissionActionButton(content, "NEW MISSION HERE", -HqBlockBottom - 56f, UiTheme.Surface, UiTheme.Text, () =>
            {
                string name = _missionName != null && !string.IsNullOrWhiteSpace(_missionName.text)
                    ? _missionName.text.Trim()
                    : "New mission";
                MissionCreateRequested?.Invoke(_missionCampaign, name);
            });

            MissionActionButton(content, "DELETE MISSION", -HqBlockBottom - 96f, UiTheme.Danger, Color.white, () =>
            {
                if (_mission != null) MissionDeleteRequested?.Invoke(_mission);
            });

            _missionStatus = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.Place(_missionStatus.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -HqBlockBottom - 138f), new Vector2(InnerWidth, 34));

            var hint = UIFactory.CreateText(content,
                "A mission is this record plus its map file, and SAVE writes both — so whatever is on the " +
                "editor's map right now (units, control measures, weather, H-hour, view) becomes what the " +
                "player gets from SINGLE PLAYER. There is no separate publish step.\n\n" +
                "NEW MISSION HERE starts one at the point the camera is looking at, in the campaign chosen " +
                "above. DELETE removes it from the campaign board but leaves its map file on disk — a " +
                "scenario takes an evening to lay out and this button is one mis-click.\n\n" +
                "Missions are saved to your own copy of the list, which shadows the shipped one. Delete " +
                "missions.json from the save folder to go back to the missions the game ships with.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -HqBlockBottom - 176f),
                new Vector2(InnerWidth, 250));

            RefreshMissionList();
        }

        /// <summary>
        /// Height of the MISSIONS page inside its scroll view. Grew with the HQ
        /// ZONES block — everything below it is placed relative to
        /// <see cref="HqBlockBottom"/> so the page and its contents can never
        /// drift apart.
        /// </summary>
        const float MissionsPageHeight = HqBlockBottom + 440f;

        /// <summary>
        /// Wraps a section's content in a scroll view of a fixed page height,
        /// returning the page to place controls on.
        ///
        /// The stock scroll content stacks its children with a
        /// <see cref="VerticalLayoutGroup"/>, which would fight the absolute
        /// offsets every section builder uses. Both that and the size fitter are
        /// **disabled** rather than destroyed: <c>Destroy</c> on a component is
        /// deferred to end of frame, so a destroyed layout group would still lay
        /// out the children added to it a few lines later.
        /// </summary>
        static RectTransform ScrollableSection(RectTransform section, float pageHeight)
        {
            var scroll = UIFactory.CreateScrollView(section, out RectTransform page, withScrollbar: true);
            UIFactory.Stretch((RectTransform)scroll.transform);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var layout = page.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = page.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            page.sizeDelta = new Vector2(0, pageHeight);
            return page;
        }

        // ------------------------------------------------------- mission area

        /// <summary>Arm the click-to-draw area tool.</summary>
        public System.Action MissionAreaDrawRequested;
        /// <summary>Replace the area with a box of the given half-size, in km, around the view.</summary>
        public System.Action<float> MissionAreaRectangleRequested;
        /// <summary>Drop the area — the mission becomes unbounded again.</summary>
        public System.Action MissionAreaClearRequested;

        Text _missionAreaState, _missionAreaFigures;
        Button _missionAreaDrawBtn;

        /// <summary>
        /// The mission's boundary controls.
        ///
        /// **Why a mission has a boundary at all.** A scenario is a piece of
        /// ground. Without one the player can pan to the next country, the fog
        /// of war has to guess how much map to cover, and there is nothing to
        /// say where the battle is supposed to be. With one, the camera stops at
        /// the edge, everything outside goes dark in battle, and a formation
        /// that wanders off it is off the battlefield.
        ///
        /// Two ways to set it, because there are two cases: most missions want a
        /// box of about this size around here, which is one click; some want the
        /// shape of a valley or a coastline, which is worth drawing.
        ///
        /// See docs/22-MISSIONS.md and docs/16-FOG-OF-WAR.md.
        /// </summary>
        void BuildMissionAreaBlock(RectTransform content)
        {
            SectionLabel(content, "MISSION AREA", -528);

            var frame = UIFactory.CreateBorderedPanel(content, "MissionAreaState", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -548), new Vector2(InnerWidth, 42));

            var (state, figures) = UIFactory.CreateStackedLabels(frame,
                "UNBOUNDED", "The whole world is in play", 12f, InnerWidth - 24f, topInset: 5f);
            _missionAreaState = state;
            _missionAreaFigures = figures;

            var drawFrame = UIFactory.CreateBorderedPanel(content, "DrawArea", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(drawFrame, new Vector2(0f, 1f), new Vector2(Pad, -598), new Vector2(InnerWidth, 32));
            _missionAreaDrawBtn = UIFactory.CreateButton(drawFrame, "DRAW AREA ON MAP",
                () => MissionAreaDrawRequested?.Invoke(),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)_missionAreaDrawBtn.transform);

            // Three sizes rather than a number field: these are the scales a
            // scenario is actually laid out at — a town, a corps sector, a
            // theatre — and typing "37" would be a decision nobody has a reason
            // to make.
            float third = (InnerWidth - 8f) / 3f;
            RectangleButton(content, "20 KM", 10f, 0, third, -638);
            RectangleButton(content, "50 KM", 25f, 1, third, -638);
            RectangleButton(content, "120 KM", 60f, 2, third, -638);

            MissionActionButton(content, "CLEAR AREA", -678, UiTheme.Surface, UiTheme.TextDim,
                () => MissionAreaClearRequested?.Invoke());
        }

        // ---------------------------------------------------------- HQ zones

        /// <summary>Arm a map pick for one side's headquarters.</summary>
        public System.Action<Team> MissionHqSetRequested;
        /// <summary>Take one side's headquarters off the map.</summary>
        public System.Action<Team> MissionHqClearRequested;
        /// <summary>Resize both zones, km.</summary>
        public System.Action<float> MissionHqRadiusRequested;

        Text _friendlyHqState, _friendlyHqFigures, _enemyHqState, _enemyHqFigures;
        readonly List<(float km, Image fill, Text label)> _hqRadiusButtons =
            new List<(float, Image, Text)>();

        /// <summary>Top of the HQ ZONES block, and the bottom it hands back to the page.</summary>
        const float HqBlockTop = 718f;
        const float HqBlockEnd = HqBlockTop + 176f;
        /// <summary>The DEPLOYMENT ZONES block below it, and the bottom the whole page continues from.</summary>
        const float DeployBlockTop = HqBlockEnd + 8f;
        const float HqBlockBottom = DeployBlockTop + 176f;

        /// <summary>
        /// Where the two headquarters are.
        ///
        /// **Why a mission names them.** A scenario is not only a piece of
        /// ground and two orders of battle — it is a *purpose*, and at
        /// operational level the purpose is almost always expressed against a
        /// headquarters: seize theirs, protect ours, get within artillery range
        /// of one, keep the other out of range. Without somewhere on the map
        /// that means "this is the enemy's command post" every mission is a
        /// meeting engagement, because the only thing either side can be told
        /// to do is find the other one.
        ///
        /// Two zones, one radius, both belonging to the **mission record**
        /// rather than to the map file — the same split the mission area uses,
        /// and for the same reason: they are what the scenario is *about*, not
        /// what happens to be deployed on it.
        ///
        /// See docs/22-MISSIONS.md.
        /// </summary>
        void BuildHqZoneBlock(RectTransform content)
        {
            SectionLabel(content, "HQ ZONES", -HqBlockTop);

            HqRow(content, Team.User, "FRIENDLY HQ", GameConfig.BlueTeam, -HqBlockTop - 20f,
                out _friendlyHqState, out _friendlyHqFigures);
            HqRow(content, Team.Enemy, "ENEMY HQ", GameConfig.RedTeam, -HqBlockTop - 70f,
                out _enemyHqState, out _enemyHqFigures);

            SectionLabel(content, "ZONE SIZE", -HqBlockTop - 118f);

            // The three echelons a headquarters is actually drawn at. Typing a
            // number would be a decision nobody has a reason to make — the same
            // argument the mission area's three box sizes make.
            float third = (InnerWidth - 8f) / 3f;
            HqRadiusButton(content, "1 KM", 1f, 0, third, -HqBlockTop - 138f);
            HqRadiusButton(content, "3 KM", 3f, 1, third, -HqBlockTop - 138f);
            HqRadiusButton(content, "8 KM", 8f, 2, third, -HqBlockTop - 138f);
        }

        void HqRow(RectTransform content, Team team, string label, Color tint, float y,
            out Text state, out Text figures)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Hq_" + team, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 44));

            // Side stripe rather than a coloured caption: the row's own text
            // has to stay readable, and which army this is should be legible
            // before a word of it is read.
            var stripe = UIFactory.CreatePanel(frame, "Side", tint);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(3, -8);
            stripe.GetComponent<Image>().raycastTarget = false;

            var (title, detail) = UIFactory.CreateStackedLabels(frame, label, "Not placed",
                12f, InnerWidth - 104f, topInset: 5f);
            state = title;
            figures = detail;

            var captured = team;
            var set = UIFactory.CreateButton(frame, "SET",
                () => MissionHqSetRequested?.Invoke(captured), UiTheme.SurfaceHover, UiTheme.Text, 11);
            UIFactory.Place((RectTransform)set.transform, new Vector2(1f, 0.5f),
                new Vector2(-38, 0), new Vector2(48, 26));
            UiTooltip.Attach(set.gameObject, "Click the map to place this headquarters",
                UiTooltip.Side.Left);

            var clear = UIFactory.CreateButton(frame, "✕",
                () => MissionHqClearRequested?.Invoke(captured), UiTheme.Surface, UiTheme.TextDim, 12);
            UIFactory.Place((RectTransform)clear.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(24, 24));
        }

        void HqRadiusButton(RectTransform content, string label, float km, int index,
            float width, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "HqR_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(Pad + index * (width + 4f), y), new Vector2(width, 30));

            var btn = UIFactory.CreateButton(frame, label, () => MissionHqRadiusRequested?.Invoke(km),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);

            _hqRadiusButtons.Add((km, frame.Find("Fill").GetComponent<Image>(),
                btn.GetComponentInChildren<Text>(true)));
        }

        /// <summary>
        /// Repaints the HQ block from the mission being edited. Public because
        /// the controller owns the map pick that places a zone, and the panel
        /// has to be told when one lands.
        /// </summary>
        public void RefreshHqZones()
        {
            if (_friendlyHqState == null) return;

            HqRowState(_friendlyHqState, _friendlyHqFigures, "FRIENDLY HQ", _mission?.friendlyHq);
            HqRowState(_enemyHqState, _enemyHqFigures, "ENEMY HQ", _mission?.enemyHq);

            float radius = _mission?.hqRadiusKm ?? 3f;
            foreach (var (km, fill, label) in _hqRadiusButtons)
            {
                bool on = _mission != null && Mathf.Approximately(km, radius);
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        static void HqRowState(Text state, Text figures, string label, MissionZone zone)
        {
            state.text = label;
            figures.text = zone == null || !zone.placed
                ? "Not placed"
                : $"{zone.latitude:0.####}, {zone.longitude:0.####}";
        }

        // -------------------------------------------------- deployment zones

        /// <summary>Arm a map pick for one side's deployment zone.</summary>
        public System.Action<Team> MissionDeploymentSetRequested;
        /// <summary>Take one side's deployment zone off the map.</summary>
        public System.Action<Team> MissionDeploymentClearRequested;
        /// <summary>Resize both zones, km.</summary>
        public System.Action<float> MissionDeploymentRadiusRequested;

        Text _friendlyDeployState, _friendlyDeployFigures, _enemyDeployState, _enemyDeployFigures;
        readonly List<(float km, Image fill, Text label)> _deployRadiusButtons =
            new List<(float, Image, Text)>();

        /// <summary>
        /// Where each side's reinforcements arrive.
        ///
        /// **Why a scenario has to name this.** A reinforcement that appeared
        /// wherever the schedule felt like putting it would be a spawn, not a
        /// reinforcement — the whole meaning of a reserve arriving is that it
        /// comes from *somewhere*, and that somewhere is a decision the designer
        /// makes: a road entry, a rear assembly area, the far side of a river.
        /// Without one, arrivals fall back to their own side's rear, which is
        /// the honest default but not a choice anybody made.
        ///
        /// Same shape as the HQ block above, and deliberately so: they are the
        /// same kind of statement about the same ground, and a designer who has
        /// learned one has learned both. See docs/30-REINFORCEMENTS.md.
        /// </summary>
        void BuildDeploymentBlock(RectTransform content)
        {
            SectionLabel(content, "DEPLOYMENT ZONES", -DeployBlockTop);

            DeployRow(content, Team.User, "FRIENDLY DEPLOYMENT", GameConfig.BlueTeam,
                -DeployBlockTop - 20f, out _friendlyDeployState, out _friendlyDeployFigures);
            DeployRow(content, Team.Enemy, "ENEMY DEPLOYMENT", GameConfig.RedTeam,
                -DeployBlockTop - 70f, out _enemyDeployState, out _enemyDeployFigures);

            SectionLabel(content, "ZONE SIZE", -DeployBlockTop - 118f);

            float third = (InnerWidth - 8f) / 3f;
            DeployRadiusButton(content, "2 KM", 2f, 0, third, -DeployBlockTop - 138f);
            DeployRadiusButton(content, "5 KM", 5f, 1, third, -DeployBlockTop - 138f);
            DeployRadiusButton(content, "12 KM", 12f, 2, third, -DeployBlockTop - 138f);
        }

        void DeployRow(RectTransform content, Team team, string label, Color tint, float y,
            out Text state, out Text figures)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Deploy_" + team, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 44));

            var stripe = UIFactory.CreatePanel(frame, "Side", tint);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(3, -8);
            stripe.GetComponent<Image>().raycastTarget = false;

            var (title, detail) = UIFactory.CreateStackedLabels(frame, label, "Not placed",
                12f, InnerWidth - 104f, topInset: 5f);
            state = title;
            figures = detail;

            var captured = team;
            var set = UIFactory.CreateButton(frame, "SET",
                () => MissionDeploymentSetRequested?.Invoke(captured), UiTheme.SurfaceHover, UiTheme.Text, 11);
            UIFactory.Place((RectTransform)set.transform, new Vector2(1f, 0.5f),
                new Vector2(-38, 0), new Vector2(48, 26));
            UiTooltip.Attach(set.gameObject, "Click the map to place this deployment zone",
                UiTooltip.Side.Left);

            var clear = UIFactory.CreateButton(frame, "✕",
                () => MissionDeploymentClearRequested?.Invoke(captured), UiTheme.Surface, UiTheme.TextDim, 12);
            UIFactory.Place((RectTransform)clear.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(24, 24));
        }

        void DeployRadiusButton(RectTransform content, string label, float km, int index,
            float width, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "DepR_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(Pad + index * (width + 4f), y), new Vector2(width, 30));

            var btn = UIFactory.CreateButton(frame, label, () => MissionDeploymentRadiusRequested?.Invoke(km),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);

            _deployRadiusButtons.Add((km, frame.Find("Fill").GetComponent<Image>(),
                btn.GetComponentInChildren<Text>(true)));
        }

        /// <summary>Repaints the deployment block from the mission being edited.</summary>
        public void RefreshDeploymentZones()
        {
            if (_friendlyDeployState == null) return;

            HqRowState(_friendlyDeployState, _friendlyDeployFigures, "FRIENDLY DEPLOYMENT",
                _mission?.friendlyDeployment);
            HqRowState(_enemyDeployState, _enemyDeployFigures, "ENEMY DEPLOYMENT",
                _mission?.enemyDeployment);

            float radius = _mission?.deploymentRadiusKm ?? 5f;
            foreach (var (km, fill, label) in _deployRadiusButtons)
            {
                bool on = _mission != null && Mathf.Approximately(km, radius);
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        void RectangleButton(RectTransform content, string label, float halfKm, int index,
            float width, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Rect_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(Pad + index * (width + 4f), y), new Vector2(width, 32));

            var btn = UIFactory.CreateButton(frame, label,
                () => MissionAreaRectangleRequested?.Invoke(halfKm),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);
            UIFactory.Fit(btn.GetComponentInChildren<Text>(), 9);
        }

        /// <summary>Tells the panel whether the area tool is armed, so its button can say so.</summary>
        public void SetMissionAreaDrawing(bool drawing)
        {
            if (_missionAreaDrawBtn == null) return;

            var caption = _missionAreaDrawBtn.GetComponentInChildren<Text>();
            if (caption != null)
            {
                caption.text = drawing ? "DRAWING — RIGHT-CLICK TO CLOSE" : "DRAW AREA ON MAP";
                caption.color = drawing ? UiTheme.Accent : UiTheme.Text;
            }
        }

        /// <summary>Repaints the area readout from the mission's own record.</summary>
        public void RefreshMissionArea()
        {
            if (_missionAreaState == null) return;

            var area = _mission?.area;
            if (area == null || !area.HasArea)
            {
                _missionAreaState.text = "UNBOUNDED";
                _missionAreaState.color = UiTheme.TextDim;
                _missionAreaFigures.text = _mission == null
                    ? "No mission selected"
                    : "The whole world is in play";
                return;
            }

            _missionAreaState.text = "BOUNDED";
            _missionAreaState.color = UiTheme.Accent;
            _missionAreaFigures.text =
                $"{area.VertexCount} corners · {area.AreaKm2():n0} km² · {area.RadiusKm():0.#} km radius";
        }

        InputField MissionField(RectTransform content, string placeholder, float y,
            float x = Pad, float width = InnerWidth)
        {
            var field = UIFactory.CreateInputField(content, placeholder, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)field.transform, new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, 32));
            field.GetComponent<Image>().color = UiTheme.Surface;
            field.onEndEdit.AddListener(_ => ReadMissionFields());
            return field;
        }

        void MissionActionButton(RectTransform content, string label, float y,
            Color fill, Color text, UnityEngine.Events.UnityAction action)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Mission_" + label, fill, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 32));
            var btn = UIFactory.CreateButton(frame, label, action, new Color(0, 0, 0, 0), text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)btn.transform);
        }

        static List<string> CampaignNames()
        {
            var names = new List<string>(CampaignInfo.All.Length);
            foreach (var c in CampaignInfo.All) names.Add(CampaignInfo.DisplayName(c));
            return names;
        }

        void OnCampaignPicked(int index)
        {
            if (_missionSyncing) return;
            _missionCampaign = CampaignInfo.All[Mathf.Clamp(index, 0, CampaignInfo.All.Length - 1)];
            RefreshMissionList();
        }

        void OnMissionPicked(int index)
        {
            if (_missionSyncing) return;
            _mission = index >= 0 && index < _missionsShown.Count ? _missionsShown[index] : null;
            RefreshMissionFields();
        }

        /// <summary>
        /// Repopulates the mission dropdown for the chosen campaign, keeping the
        /// current selection if it survived. Public because the controller calls
        /// it after creating or deleting one — the library is the source of
        /// truth and the panel is a view of it.
        /// </summary>
        public void RefreshMissionList()
        {
            if (_missionDropdown == null) return;

            _missionsShown = MissionLibrary.OfCampaign(_missionCampaign, includeHidden: true);

            var names = new List<string>(_missionsShown.Count);
            foreach (var m in _missionsShown)
                names.Add(m.available ? m.name : m.name + "  (hidden)");
            if (names.Count == 0) names.Add("— no missions —");

            int index = _mission == null ? 0 : Mathf.Max(0, _missionsShown.IndexOf(_mission));
            _mission = _missionsShown.Count > 0
                ? _missionsShown[Mathf.Clamp(index, 0, _missionsShown.Count - 1)]
                : null;

            _missionSyncing = true;
            _missionDropdown.ClearOptions();
            _missionDropdown.AddOptions(names);
            _missionDropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, names.Count - 1));
            _missionDropdown.RefreshShownValue();
            _missionSyncing = false;

            RefreshMissionFields();
        }

        /// <summary>Selects a mission in the panel — used when the editor opens one.</summary>
        public void ShowMission(MissionDefinition mission)
        {
            if (mission == null) return;
            _mission = mission;
            _missionCampaign = mission.CampaignEnum;

            if (_campaignDropdown != null)
            {
                _missionSyncing = true;
                _campaignDropdown.SetValueWithoutNotify(
                    Mathf.Max(0, System.Array.IndexOf(CampaignInfo.All, _missionCampaign)));
                _campaignDropdown.RefreshShownValue();
                _missionSyncing = false;
            }
            RefreshMissionList();
        }

        /// <summary>Writes the panel's controls from the selected mission.</summary>
        void RefreshMissionFields()
        {
            if (_missionName == null) return;

            _missionSyncing = true;
            var m = _mission;

            _missionName.text = m?.name ?? "";
            _missionLocation.text = m?.location ?? "";
            _missionBriefing.text = m?.briefing ?? "";
            _missionLat.text = m == null ? "" : m.latitude.ToString("0.#####",
                System.Globalization.CultureInfo.InvariantCulture);
            _missionLon.text = m == null ? "" : m.longitude.ToString("0.#####",
                System.Globalization.CultureInfo.InvariantCulture);
            _missionAltitude.text = m == null ? "" : m.startAltitudeMeters.ToString("0");

            _missionSyncing = false;

            bool fog = m != null && m.fogOfWar;
            if (_missionFogLamp != null)
            {
                _missionFogLamp.GetComponent<Image>().color = fog ? UiTheme.Success : UiTheme.TextFaint;
                _missionFogLabel.text = m == null ? "—" : fog ? "ON" : "OFF";
            }

            if (_missionStatus != null)
                _missionStatus.text = m == null
                    ? "No mission selected."
                    : $"{m.id}  ·  map: {m.ResolvedMapFile}";

            RefreshMissionArea();
            RefreshHqZones();
            RefreshDeploymentZones();
        }

        /// <summary>The mission the panel is editing, so the controller can read its area back.</summary>
        public MissionDefinition CurrentMission => _mission;

        /// <summary>
        /// Reads the panel's controls back into the selected mission.
        ///
        /// Run on every field's end-edit rather than only on save, so the record
        /// in memory always matches what is on screen — otherwise typing a new
        /// latitude and then pressing OPEN would fly to the old one.
        /// **Nothing is written to disk here**; that is SAVE's job.
        /// </summary>
        void ReadMissionFields()
        {
            if (_missionSyncing || _mission == null) return;

            var invariant = System.Globalization.CultureInfo.InvariantCulture;

            if (!string.IsNullOrWhiteSpace(_missionName.text)) _mission.name = _missionName.text.Trim();
            _mission.location = _missionLocation.text.Trim();
            _mission.briefing = _missionBriefing.text.Trim();

            // A malformed number leaves the value alone rather than zeroing it —
            // half-typed input is not an instruction to move the mission to the
            // Gulf of Guinea.
            if (double.TryParse(_missionLat.text, System.Globalization.NumberStyles.Float,
                    invariant, out double lat) && lat >= -90.0 && lat <= 90.0)
                _mission.latitude = lat;

            if (double.TryParse(_missionLon.text, System.Globalization.NumberStyles.Float,
                    invariant, out double lon) && lon >= -180.0 && lon <= 180.0)
                _mission.longitude = lon;

            if (double.TryParse(_missionAltitude.text, System.Globalization.NumberStyles.Float,
                    invariant, out double alt))
                _mission.startAltitudeMeters = Mathf.Clamp((float)alt, 300f, 120000f);

            RefreshMissionFields();
        }

        void CommitMission()
        {
            if (_mission == null)
            {
                _missionStatus.text = "Nothing selected — create a mission first.";
                return;
            }
            ReadMissionFields();
            MissionSaveRequested?.Invoke(_mission);
            RefreshMissionList();
        }

        /// <summary>Shows the result of a save/create/delete in the panel's own status line.</summary>
        public void SetMissionStatus(string message)
        {
            if (_missionStatus != null) _missionStatus.text = message;
        }

        // ----------------------------------------------------- weather section

        /// <summary>
        /// Two independent axes, because they genuinely are: SKY is the time of
        /// day, CONDITIONS is what is falling out of it. Folding them into one
        /// list would make a night storm unexpressible — and would leave the
        /// automatic day/night toggle fighting whatever weather was picked.
        /// </summary>
        /// <summary>Height the scenario-start block occupies inside the merged page.</summary>
        const float StartBlockHeight = 380f;
        /// <summary>And the weather block below it.</summary>
        const float WeatherBlockHeight = 480f;

        /// <summary>
        /// **ENVIRONMENT** — when the scenario is fought and what the weather is
        /// doing, on one page.
        ///
        /// These were two rail rows, WEATHER CONDITIONS and DATE AND TIME, and
        /// they were always one decision. A designer setting a night attack is
        /// choosing the hour *and* the sky in the same breath; the auto
        /// day/night switch reads the clock the other section owned; and a
        /// player asking "what will this look like" has to open both to find
        /// out. One row that answers the whole question is worth two that each
        /// answer half of it — and it gives the rail a row back.
        ///
        /// The two builders are unchanged and are laid into sub-pages of a
        /// scroll view. Every section builder here places its controls at
        /// absolute offsets from the top of what it is given, so handing each
        /// one its own container is what lets them be stacked without either of
        /// them being reflowed.
        /// </summary>
        void BuildEnvironmentSection(RectTransform section)
        {
            var page = ScrollableSection(section, StartBlockHeight + WeatherBlockHeight);

            BuildDateTimeSection(SubPage(page, "StartBlock", 0f, StartBlockHeight));
            BuildWeatherSection(SubPage(page, "WeatherBlock", StartBlockHeight, WeatherBlockHeight));
        }

        /// <summary>A full-width slice of a page, so a section builder's own offsets stay meaningful.</summary>
        static RectTransform SubPage(RectTransform page, string name, float top, float height)
        {
            var rt = UIFactory.CreateGroup(page, name);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top);
            rt.sizeDelta = new Vector2(0f, height);
            return rt;
        }

        void BuildWeatherSection(RectTransform content)
        {
            SectionLabel(content, "SKY", -8);

            var skies = WeatherCatalog.AllSkies;
            float skyW = (InnerWidth - 8f) / 3f;
            for (int i = 0; i < skies.Count; i++)
            {
                var sky = skies[i];
                var b = UIFactory.CreateButton(content, sky.name, () => ApplyPhase(sky.phase),
                    UiTheme.Surface, UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f),
                    new Vector2(Pad + i * (skyW + 4f), -28), new Vector2(skyW, 30));
                _skyButtons.Add((sky.phase, b));
            }

            // --- automatic day/night ---
            var autoFrame = UIFactory.CreateBorderedPanel(content, "AutoDayNight", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(autoFrame, new Vector2(0f, 1f), new Vector2(Pad, -66), new Vector2(InnerWidth, 46));

            _autoDayNightBtn = UIFactory.CreateButton(autoFrame, "", ToggleAutoDayNight,
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)_autoDayNightBtn.transform);
            var autoCaption = _autoDayNightBtn.GetComponentInChildren<Text>(true);
            if (autoCaption != null) autoCaption.gameObject.SetActive(false);

            _autoDayNightLamp = UIFactory.CreatePanel(autoFrame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(_autoDayNightLamp, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(8, 8));
            _autoDayNightLamp.GetComponent<Image>().raycastTarget = false;

            var (_, autoState) = UIFactory.CreateStackedLabels(autoFrame,
                "AUTO DAY / NIGHT", "", 28f, InnerWidth - 40f, topInset: 7f);
            _autoDayNightLabel = autoState;

            // --- conditions ---
            SectionLabel(content, "CONDITIONS", -124);

            var conditions = WeatherCatalog.AllConditions;
            for (int i = 0; i < conditions.Count; i++)
            {
                var def = conditions[i];
                float y = -146f - i * 44f;

                var frame = UIFactory.CreateBorderedPanel(content, "Weather_" + def.name,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 40));

                var b = UIFactory.CreateButton(frame, "", () => ApplyCondition(def.condition),
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)b.transform);
                var caption = b.GetComponentInChildren<Text>(true);
                if (caption != null) caption.gameObject.SetActive(false);

                var (name, _) = UIFactory.CreateStackedLabels(frame, def.name, def.detail,
                    12f, InnerWidth - 44f, topInset: 4f);

                // A speaker pip marks the conditions that bring an audio bed.
                if (def.ambience != IronMeridian.Audio.AmbienceTrack.None)
                {
                    var pip = UIFactory.CreateText(frame, "♪", UiTheme.FontSmall, UiTheme.Accent,
                        TextAnchor.MiddleRight);
                    UIFactory.Place(pip.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(16, 16));
                }

                _conditionFrames.Add((def.condition, frame.Find("Fill").GetComponent<Image>(), name));
            }

            var hint = UIFactory.CreateText(content,
                "Sky and fog preview here in the editor. Weather audio plays in battle mode only.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -420), new Vector2(InnerWidth, 40));

            RefreshWeather();
        }

        void ApplyPhase(SkyPhase phase)
        {
            if (_weather == null) return;
            _weather.SetPhase(phase);
        }

        void ApplyCondition(WeatherCondition condition)
        {
            if (_weather == null) return;
            _weather.SetCondition(condition);
        }

        void ToggleAutoDayNight()
        {
            if (_weather == null) return;
            _weather.SetAutoDayNight(!_weather.AutoDayNight);
        }

        /// <summary>Repaints the whole section from the system's state — it is the source of truth.</summary>
        void RefreshWeather()
        {
            if (_weather == null) return;

            foreach (var (phase, btn) in _skyButtons)
            {
                bool on = !_weather.AutoDayNight && phase == _weather.Phase;
                btn.GetComponent<Image>().color = on ? UiTheme.Accent : UiTheme.Surface;
                var t = btn.GetComponentInChildren<Text>(true);
                if (t != null) t.color = on ? Color.white : UiTheme.TextDim;
            }

            foreach (var (condition, fill, label) in _conditionFrames)
            {
                bool on = condition == _weather.Condition;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_autoDayNightLamp != null)
                _autoDayNightLamp.GetComponent<Image>().color =
                    _weather.AutoDayNight ? UiTheme.Success : UiTheme.TextFaint;

            if (_autoDayNightLabel != null)
                _autoDayNightLabel.text = _weather.AutoDayNight
                    ? $"ON — clock drives the sky (now {_weather.Phase})"
                    : "OFF — sky is set by hand above";
        }

        // -------------------------------------------------- date & time section

        /// <summary>
        /// H-hour for the scenario: the current start (click to edit) plus three
        /// ready-made times of day. Whatever is set here is the clock the top
        /// bar shows once the battle starts, and it is saved with the map.
        /// </summary>
        void BuildDateTimeSection(RectTransform content)
        {
            SectionLabel(content, "SCENARIO START", -8);

            var frame = UIFactory.CreateBorderedPanel(content, "StartButton", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -30), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "", OpenStartEditor, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var glyph = UIFactory.CreateImage(frame, UiIcons.Clock, "Glyph");
            glyph.color = UiTheme.Accent;
            glyph.raycastTarget = false;
            UIFactory.Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(18, 18));

            var (startValue, _) = UIFactory.CreateStackedLabels(frame, "", "Click to change",
                40f, InnerWidth - 52f, topInset: 9f, titleSize: UiTheme.FontBody);
            _startValueLabel = startValue;

            SectionLabel(content, "PRESETS", -96);

            for (int i = 0; i < StartPresets.Length; i++)
            {
                var preset = StartPresets[i];
                float y = -118f - i * 58f;

                var pf = UIFactory.CreateBorderedPanel(content, "Preset" + i, UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(pf, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

                var pb = UIFactory.CreateButton(pf, "", () => ApplyStart(preset.when),
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)pb.transform);
                var pc = pb.GetComponentInChildren<Text>(true);
                if (pc != null) pc.gameObject.SetActive(false);

                float presetW = InnerWidth - 24f;
                var name = UIFactory.CreateText(pf, preset.name, UiTheme.FontSmall, UiTheme.Text,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.PlaceTopLeft(name.rectTransform, 12f, 4f, presetW, 15f);
                UIFactory.Fit(name);

                var when = UIFactory.CreateText(pf, preset.when.ToString("HH:mm  ·  dd.MM.yyyy"),
                    UiTheme.FontLabel, UiTheme.Accent, TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(when.rectTransform, 12f, 19f, presetW, 14f);
                UIFactory.Fit(when);

                var detail = UIFactory.CreateText(pf, preset.detail, UiTheme.FontLabel, UiTheme.TextFaint,
                    TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(detail.rectTransform, 12f, 34f, presetW, 14f);
                UIFactory.Fit(detail);
            }

            var hint = UIFactory.CreateText(content,
                "The clock runs only while a battle is in progress — the editor is timeless. " +
                "This start time is saved with the map.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -302), new Vector2(InnerWidth, 56));

            RefreshStartLabel();
        }

        void OpenStartEditor()
        {
            if (_clock == null || _canvas == null) return;
            DateTimeDialog.Open(_canvas, _clock.StartDateTime, ApplyStart);
        }

        void ApplyStart(System.DateTime when)
        {
            if (_clock == null) return;
            _clock.SetStart(when);
            RefreshStartLabel();
        }

        void RefreshStartLabel()
        {
            if (_startValueLabel == null || _clock == null) return;
            _startValueLabel.text = _clock.StartText;
        }

        // -------------------------------------------------------- map section

        void BuildMapSection(RectTransform content)
        {
            SectionLabel(content, "TILE STYLE", -8);

            _styleDropdown = UIFactory.CreateDropdown(content, StyleNames(),
                System.Array.IndexOf(Styles, _map.Style), OnStyleSelected);
            StyleDropdown(_styleDropdown, -30);

            SectionLabel(content, "PROJECTION", -76);

            var frame = UIFactory.CreateBorderedPanel(content, "ViewToggle", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -98), new Vector2(InnerWidth, 32));

            var viewBtn = UIFactory.CreateButton(frame,
                _map.ViewMode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D",
                ToggleView, new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)viewBtn.transform);
            _viewBtnLabel = viewBtn.GetComponentInChildren<Text>(true);

            var parity = UIFactory.CreateText(content,
                "2D and 3D show the same world — units, effects, weather, lines and buildings all behave identically.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(parity.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -134), new Vector2(InnerWidth, 32));

            SectionLabel(content, "LAYERS", -172);

            _buildingsLamp = ToggleRow(content, "3D BUILDINGS", -194,
                () => { _map.SetBuildingsVisible(!_map.BuildingsVisible); }, out _buildingsLabel);

            _mapControlsLamp = ToggleRow(content, "ON-MAP CONTROLS", -238,
                () => { if (_mapControls != null) _mapControls.SetControlsVisible(!_mapControls.ControlsVisible); RefreshMapSection(); },
                out _mapControlsLabel);

            _compassLamp = ToggleRow(content, "COMPASS", -282,
                () => { if (_mapControls != null) _mapControls.SetCompassVisible(!_mapControls.CompassVisible); RefreshMapSection(); },
                out _compassLabel);

            SectionLabel(content, "UNIT LABELS", -330);

            _labelSizeValue = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_labelSizeValue.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -330), new Vector2(80, 18));

            var slider = UIFactory.CreateSlider(content, LabelScaleTo01(UnitActor.LabelScale), v =>
            {
                UnitActor.SetLabelScale(LabelScaleFrom01(v));
                RefreshMapSection();
            });
            UIFactory.Place((RectTransform)slider.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -352), new Vector2(InnerWidth, 30));

            // Control measures used to be set up from here. They have their own
            // section in the rail now, with the options docked on the right:
            // choosing what to draw has nothing to do with which imagery is
            // under it, and the two had no business sharing a panel.

            RefreshMapSection();
        }

        /// <summary>A lamp + label row that reads as an on/off switch.</summary>
        RectTransform ToggleRow(RectTransform content, string label, float y,
            UnityEngine.Events.UnityAction action, out Text stateLabel)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Toggle_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 38));

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var lamp = UIFactory.CreatePanel(frame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(8, 8));
            lamp.GetComponent<Image>().raycastTarget = false;

            // Title and state share one line, so the two rects must not overlap:
            // the title stops where the state column begins.
            var title = UIFactory.CreateText(frame, label, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(28, 0),
                new Vector2(InnerWidth - 28f - 74f, 16));
            UIFactory.Fit(title);

            stateLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Place(stateLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(62, 16));

            return lamp;
        }

        List<string> StyleNames()
        {
            var names = new List<string>(Styles.Length);
            foreach (var style in Styles) names.Add(StyleLabel(style));
            return names;
        }

        static string StyleLabel(MapStyle style) => style switch
        {
            MapStyle.Satellite => "SATELLITE",
            MapStyle.SatelliteLabels => "SATELLITE + LABELS",
            MapStyle.Roads => "ROADS",
            MapStyle.Terrain => "TERRAIN (NO IMAGERY)",
            MapStyle.Sentinel2 => "SENTINEL-2",
            MapStyle.OpenStreetMap => "OPENSTREETMAP",
            _ => style.ToString().ToUpperInvariant()
        };

        // The slider is linear 0..1 but the useful label range is 0.5x..2.5x,
        // so map between them rather than exposing raw multipliers.
        static float LabelScaleTo01(float scale) => Mathf.InverseLerp(0.5f, 2.5f, scale);
        static float LabelScaleFrom01(float v) => Mathf.Lerp(0.5f, 2.5f, v);

        /// <summary>
        /// Latches the tool strip's boundary button, so an armed draw tool reads
        /// the same in the rail as it does in the options panel that armed it.
        /// </summary>
        /// <summary>Repaints every toggle and readout from the systems that own the state.</summary>
        void RefreshMapSection()
        {
            if (_buildingsLamp != null)
            {
                bool on = _map != null && _map.BuildingsVisible;
                _buildingsLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _buildingsLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_mapControlsLamp != null)
            {
                bool on = _mapControls != null && _mapControls.ControlsVisible;
                _mapControlsLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _mapControlsLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_compassLamp != null)
            {
                bool on = _mapControls != null && _mapControls.CompassVisible;
                _compassLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _compassLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_labelSizeValue != null)
                _labelSizeValue.text = string.Format("{0:0.00}x", UnitActor.LabelScale);
        }

        void SectionLabel(RectTransform content, string label, float y)
        {
            var t = UIFactory.CreateSectionHeader(content, label);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 18));
        }

        // --------------------------------------------------- strike allowance

        /// <summary>Every "STRIKES REMAINING" readout on the rail, repainted together.</summary>
        /// <summary>
        /// Every per-system "missions left" readout on a fire button, with the
        /// budget key and limit it reports. Repainted together whenever a
        /// mission is spent — see <see cref="RefreshStrikeBudget"/>.
        /// </summary>
        readonly List<(Text label, string key, int limit)> _budgetLabels =
            new List<(Text, string, int)>();

        /// <summary>
        /// The shared strike allowance, shown at the head of each fire menu.
        ///
        /// It is on **all three** of them, and on the missile board, because the
        /// pool is shared: a player who spends it on artillery has spent it on
        /// air strikes too, and a counter that appeared only in the menu being
        /// used would let them find that out the hard way. See
        /// <see cref="StrikeBudget"/>.
        /// </summary>
        /// <summary>
        /// Names the right-hand column of the fire buttons below it.
        ///
        /// It used to be the allowance itself — one shared count of ninety-nine
        /// for every strike in the game. The count is now attached to each
        /// system (see <see cref="StrikeBudget"/>), so what this row does is
        /// say what the second figure on every button beneath it means. A
        /// column of bare "4 / 6"s with nothing to read them against is the
        /// kind of number a player learns to ignore.
        /// </summary>
        void StrikeBudgetRow(RectTransform content, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "AllowanceLegend",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 28));

            var name = UIFactory.CreateText(frame, "MISSIONS AVAILABLE", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(InnerWidth - 110f, 14));

            var note = UIFactory.CreateText(frame, "PER SYSTEM", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            note.raycastTarget = false;
            UIFactory.Place(note.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(94, 16));
        }

        /// <summary>
        /// The "missions left" figure on a fire button, under its radius. Every
        /// fire menu builds its right-hand column the same way, so a player
        /// reads the same two numbers in the same place whichever one is open.
        /// </summary>
        Text AllowanceLabel(RectTransform frame, string key, int limit)
        {
            var label = UIFactory.CreateText(frame, "", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            label.raycastTarget = false;
            UIFactory.Place(label.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, -9),
                new Vector2(56, 14));

            _budgetLabels.Add((label, key, limit));
            return label;
        }

        /// <summary>Repaints every allowance readout. Driven by the budget's own event.</summary>
        void RefreshStrikeBudget()
        {
            foreach (var (label, key, limit) in _budgetLabels)
            {
                if (label == null) continue;
                label.text = StrikeBudget.RemainingText(key, limit);
                label.color = StrikeBudget.RemainingColour(key, limit,
                    UiTheme.Accent, UiTheme.Warning, UiTheme.Hostile);
            }
        }

        void ToggleView()
        {
            _map.ToggleViewMode();
            _rig.SetMode(_map.ViewMode);
        }

        void OnViewModeChanged(ViewMode mode)
        {
            if (_viewBtnLabel == null) return;
            _viewBtnLabel.text = mode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D";
        }

        void OnStyleSelected(int index) => _map.SetMapStyle(Styles[index]);

        void OnStyleChanged(MapStyle style)
        {
            if (_styleDropdown == null) return;
            int idx = System.Array.IndexOf(Styles, style);
            _styleDropdown.SetValueWithoutNotify(idx);
            _styleDropdown.RefreshShownValue();
        }

        // ---------------------------------------------------- drag to deploy

        void BeginDrag(UnitDefinition def, Sprite sprite)
        {
            _dragging = def;
            _dragGhost.sprite = sprite;
            _dragGhost.gameObject.SetActive(sprite != null);
            _lastDropValid = false;
        }

        void Drag(PointerEventData e)
        {
            if (_dragging == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, e.position, _canvas.worldCamera, out Vector2 local);
            ((RectTransform)_dragGhost.transform).anchoredPosition = local;

            // Live WYSIWYG ground marker: over UI or off the loaded terrain,
            // there's nowhere valid to drop, so hide it instead of guessing.
            bool overUI = e.pointerCurrentRaycast.gameObject != null;
            Vector3 world = default;
            _lastDropValid = !overUI && _map.RaycastGround(_worldCam, e.position, out world);
            if (_lastDropValid)
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);

                // Two separate questions, and both must answer yes. The raycast
                // above says the cursor is over *something*; this says the
                // ground under that point can actually be measured. They come
                // apart at a tile seam, where the ray clips the edge of a tile
                // that is streaming out — and a unit deployed there is left at
                // the fallback height, floating over a valley or buried in a
                // ridge. Refusing costs one more click; the alternative is a
                // formation nobody can find.
                if (!GeoUtils.TrySampleTerrainHeight(_map.Georeference, lat, lon, out double ground))
                {
                    _lastDropValid = false;
                    _placementMarker.SetVisible(false);
                    return;
                }

                // Remember exactly where the ring is sitting: the deploy uses
                // this point rather than re-raycasting on release, so the unit
                // cannot land somewhere the preview never showed.
                _dropLat = lat; _dropLon = lon;
                _placementMarker.MoveTo(lat, lon);
                _placementMarker.SetVisible(true);
            }
            else
            {
                _placementMarker.SetVisible(false);
            }
        }

        void EndDrag(PointerEventData e)
        {
            _dragGhost.gameObject.SetActive(false);
            _placementMarker.SetVisible(false);
            if (_dragging == null) return;

            // Released back over the palette, HUD bar or info panel — not a valid
            // deploy point, so don't silently place the unit on whatever terrain
            // happens to be behind that UI.
            if (e.pointerCurrentRaycast.gameObject != null)
            {
                DropRejected?.Invoke("Drop the unit onto the map, not the UI.");
                _dragging = null;
                return;
            }

            if (!_lastDropValid)
            {
                DropRejected?.Invoke("No solid ground there yet — the terrain is still streaming in.");
                _dragging = null;
                return;
            }

            DropRequested?.Invoke(_dragging, _team, _affiliation, _echelon, _dropLat, _dropLon);
            _dragging = null;
        }

        /// <summary>
        /// The drop preview: the same 3D volume a strike target area uses,
        /// scaled down to a formation's footprint.
        ///
        /// It was a flat spinning reticle, which had the failing every decal on
        /// this map has — at the shallow camera pitch the editor is usually
        /// worked at, a circle on sloping ground foreshortens into a line, and
        /// behind a fold of terrain it disappears entirely. You could not see
        /// where you were about to put a battalion. A volume standing on the
        /// ground reads from any angle, and reusing TargetAreaMarker means the
        /// preview and the strike markers stay visually consistent for free —
        /// motes included.
        /// </summary>
        void BuildGroundMarker()
        {
            _placementMarker = TargetAreaMarker.Create(_map.Georeference,
                PlacementRadiusMeters, UiTheme.Accent);
            _placementMarker.SetAlarm(0f);
            _placementMarker.SetVisible(false);
        }

        /// <summary>
        /// Footprint of the drop preview, metres. About the ground a deployed
        /// battalion's icon covers, so what you see is the space it will take.
        /// </summary>
        const float PlacementRadiusMeters = 260f;

        void OnDestroy()
        {
            // Build() subscribes to the map and registry; without this the
            // callbacks fire into a destroyed component on scene reload.
            UnitRegistry.Changed -= OnUnitsChanged;
            StrikeBudget.Changed -= RefreshStrikeBudget;
            // The commanders and players panels subscribe to registries of their own.
            _commanders?.Dispose();
            _players?.Dispose();
            if (_clock != null) _clock.StartChanged -= RefreshStartLabel;
            if (_weather != null) _weather.Changed -= RefreshWeather;
            if (_effects != null) _effects.ArmedChanged -= RefreshEffects;
            if (_artillery != null) _artillery.ArmedChanged -= RefreshArtillery;
            if (_airStrike != null) _airStrike.ArmedChanged -= RefreshAirStrike;
            if (_uavStrike != null) _uavStrike.ArmedChanged -= RefreshUavStrike;
            if (_naval != null) _naval.ArmedChanged -= RefreshNavalStrike;
            if (_map == null) return;
            _map.ViewModeChanged -= OnViewModeChanged;
            _map.StyleChanged -= OnStyleChanged;
        }
    }
}
