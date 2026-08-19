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
    public partial class UnitPaletteUI : MonoBehaviour
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
        /// <summary>
        /// A catalogue card was clicked: show what this *type* is. Dragging it
        /// still deploys one — a click is the question, a drag is the answer.
        /// </summary>
        public System.Action<UnitDefinition, Team> InspectTypeRequested;

        enum Section
        {
            General, Units, Players, Commanders, Logistics, Sustainment, Reinforcements,
            Obstacles, Effects, Missions, Environment, Map,
            /// <summary>Battle mode: the derived control measures — see <see cref="BuildSectorsSection"/>.</summary>
            Sectors,
            /// <summary>Reserved — see <see cref="BuildStatsSection"/>.</summary>
            Stats,
            /// <summary>The mission's HQ and deployment zones — see <see cref="BuildZonesSection"/>.</summary>
            Zones,
            /// <summary>Reserved — see <see cref="BuildObjectsSection"/>.</summary>
            Objects,
            /// <summary>Reserved — see <see cref="BuildSuppliesSection"/>.</summary>
            Supplies,
            /// <summary>Battle-mode only — the nav row is hidden in the editor.</summary>
            Groups,
            /// <summary>Stills and recording — see <see cref="BuildCaptureSection"/>.</summary>
            Capture
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
        const float CardIconX = SectionIndent + 13f;

        /// <summary>
        /// How far an arm's contents are inset from the panel, so the cards read
        /// as nested under the header that opened them rather than as a list the
        /// header happens to sit in.
        ///
        /// The header keeps the panel's own edge: it is the thing you click to
        /// find the section, and a heading indented as far as its contents stops
        /// looking like a heading.
        /// </summary>
        const float SectionIndent = 30f;
        /// <summary>Where a card's text column starts: icon inset + icon + gutter.</summary>
        const float CardTextX = CardIconX + CardIconSize + 6f;
        /// <summary>
        /// Text width inside a list card. The card is the content width less the
        /// layout padding, and the content is the viewport less the scrollbar,
        /// so all three come off the panel width here.
        /// </summary>
        const float CardTextWidth = InnerWidth - CardTextX - UIFactory.ScrollbarWidth - 8f;
        const float AvailableCardHeight = 66f;
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
            _sectionContent[Section.Sectors] = MakeSectionContent(body, "Sectors");
            _sectionContent[Section.Stats] = MakeSectionContent(body, "Stats");
            _sectionContent[Section.Zones] = MakeSectionContent(body, "Zones");
            _sectionContent[Section.Objects] = MakeSectionContent(body, "Objects");
            _sectionContent[Section.Supplies] = MakeSectionContent(body, "Supplies");
            _sectionContent[Section.Groups] = MakeSectionContent(body, "Groups");
            _sectionContent[Section.Capture] = MakeSectionContent(body, "Capture");

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
            BuildSectorsSection(_sectionContent[Section.Sectors]);
            BuildStatsSection(_sectionContent[Section.Stats]);
            BuildZonesSection(_sectionContent[Section.Zones]);
            BuildObjectsSection(_sectionContent[Section.Objects]);
            BuildSuppliesSection(_sectionContent[Section.Supplies]);
            BuildGroupsSection(_sectionContent[Section.Groups]);
            BuildCaptureSection(_sectionContent[Section.Capture]);

            // The capture panel shows a running frame count, so it is driven by
            // the system rather than polled — and unsubscribed in OnDestroy,
            // because CaptureSystem outlives this per-scene component.
            CaptureSystem.Changed += RefreshCapture;

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
            if (section == Section.Objects) RefreshMapObjects();
            if (section == Section.Stats) RefreshStats();
            if (section == Section.Supplies) RefreshSupplies();
            if (section == Section.Capture) RefreshCapture();

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
            // Raised here as well as from the slide: taking the rail away is a
            // change to where the chrome ends, and everything measuring from it
            // — the minimap, the map camera's own viewport — has to hear about
            // the largest such change there is.
            LeftInsetChanged?.Invoke(LeftChromeEdge);
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

        /// <summary>Seconds between supply-table rebuilds while SUPPLIES is open.</summary>
        const float SupplyTickSeconds = 1f;
        float _supplyTimer;

        void Update()
        {
            if (_chromeHidden) return;

            // Ammunition and fuel are spent by combat rather than by anything
            // that raises an event, so the supply table is the one page here that
            // has to be pulled. Once a second: fast enough that a battery running
            // dry is visible while it happens, slow enough that rebuilding thirty
            // rows is not a per-frame cost.
            if (_panelOpen && _section == Section.Supplies)
            {
                _supplyTimer += Time.unscaledDeltaTime;
                if (_supplyTimer >= SupplyTickSeconds) { _supplyTimer = 0f; RefreshSupplies(); }
            }

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
            AddNavRow(nav, Section.Sectors, "SECTORS", UiIcons.Square);
            AddNavRow(nav, Section.Stats, "STATS", UiIcons.Chart);
            AddNavRow(nav, Section.Zones, "ZONES", UiIcons.Square);
            AddNavRow(nav, Section.Objects, "OBJECTS", UiIcons.Equipment);
            AddNavRow(nav, Section.Supplies, "SUPPLIES", UiIcons.Crates);
            AddNavRow(nav, Section.Groups, "GROUPS", UiIcons.Group);
            AddNavRow(nav, Section.Capture, "CAPTURE", UiIcons.Camera);

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
            Section.Missions, Section.Environment, Section.Map, Section.Zones, Section.Objects,
            Section.Capture
        };

        /// <summary>
        /// The rail in **battle mode**: what a fight in progress is managed
        /// with. Deliberately short — during a battle the rail is not where the
        /// player's attention should be.
        /// </summary>
        static readonly Section[] BattleSections =
        {
            Section.Reinforcements, Section.Sectors, Section.Groups,
            Section.Stats, Section.Supplies, Section.Capture
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
        /// <summary>
        /// SECTORS — the control measures the game *derives*, rather than the
        /// ones a designer draws.
        ///
        /// These three used to head the GENERAL panel, above the intelligence
        /// toggles, which put an authoring page and a battle page in one
        /// section. They are a battle control: a boundary runs between the
        /// formations as they stand, and the whole point of AUTO-UPDATE is that
        /// it keeps redrawing them while the fighting moves. GENERAL keeps what
        /// it was always about — what the player is allowed to see.
        /// </summary>
        void BuildSectorsSection(RectTransform content)
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
            RefreshAutoSectorLabel();

            var hint = UIFactory.CreateText(content,
                "Boundaries run rear-to-front between adjacent formations; the FEBA follows the " +
                "forward units. AUTO-UPDATE redraws them as the battle moves — see docs/28-FLOT.md.",
                UiTheme.FontSmall, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -156),
                new Vector2(InnerWidth, 76));
        }

        void BuildGeneralSection(RectTransform content)
        {
            // --- intelligence ---
            SectionLabel(content, "INTELLIGENCE", -8);

            _losLamp = ToggleRow(content, "LINE OF SIGHT", -30, () =>
            {
                _lineOfSight = !_lineOfSight;
                LineOfSightChanged?.Invoke(_lineOfSight);
                RefreshGeneralSection();
            }, out _losLabel);

            // Directly under LINE OF SIGHT because the two are read together:
            // what a formation can see and what it can reach are the pair of
            // circles a planner is comparing, and having only one of them
            // switchable meant you could never look at either on its own.
            _weaponLamp = ToggleRow(content, "MAX WEAPON RANGE", -74, () =>
            {
                _weaponRange = !_weaponRange;
                WeaponRangeChanged?.Invoke(_weaponRange);
                RefreshGeneralSection();
            }, out _weaponLabel);

            _fogLamp = ToggleRow(content, "FOG OF WAR", -118, () =>
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
            UIFactory.Place(intelHint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -166),
                new Vector2(InnerWidth, 240));

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
    }
}
