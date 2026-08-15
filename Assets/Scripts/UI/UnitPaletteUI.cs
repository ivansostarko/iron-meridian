using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;
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
    /// (the draggable catalogue) and DEPLOYED (what is actually on the map,
    /// click to select).
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
        public System.Action BoundaryToolRequested;
        public System.Action DefensiveLineToolRequested;

        // Deployed list.
        public System.Action<UnitActor> SelectUnitRequested;
        public System.Action<UnitActor> RemoveUnitRequested;

        enum Section
        {
            General, Units, Commanders, Boundaries, Effects, Artillery, AirStrike, UavStrike,
            /// <summary>
            /// The odd one out: it has no section panel of its own and opens a
            /// board docked in the section panel's place. See
            /// <see cref="MissileSystemsRequested"/>.
            /// </summary>
            Missiles,
            NavalStrike,
            Missions,
            Weather, Map, DateTime
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
        /// <summary>Emblem block plus the fourteen nav rows, measured from the rail's top.</summary>
        const float HeaderHeight = 554f;
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
        LineDrawTool _drawTool;
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
            ArtilleryStrikeSystem artillery, AirStrikeSystem airStrike,
            UavStrikeSystem uavStrike, NavalStrikeSystem naval,
            MapControlsUI mapControls, LineDrawTool drawTool)
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
            _uavStrike = uavStrike;
            _naval = naval;
            _mapControls = mapControls;
            _drawTool = drawTool;

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
            _sectionContent[Section.Commanders] = MakeSectionContent(body, "Commanders");
            _sectionContent[Section.Boundaries] = MakeSectionContent(body, "Boundaries");
            _sectionContent[Section.Effects] = MakeSectionContent(body, "Effects");
            _sectionContent[Section.Artillery] = MakeSectionContent(body, "Artillery");
            _sectionContent[Section.AirStrike] = MakeSectionContent(body, "AirStrike");
            _sectionContent[Section.UavStrike] = MakeSectionContent(body, "UavStrike");
            _sectionContent[Section.NavalStrike] = MakeSectionContent(body, "NavalStrike");
            _sectionContent[Section.Missions] = MakeSectionContent(body, "Missions");
            _sectionContent[Section.Weather] = MakeSectionContent(body, "Weather");
            _sectionContent[Section.Map] = MakeSectionContent(body, "Map");
            _sectionContent[Section.DateTime] = MakeSectionContent(body, "DateTime");

            BuildGeneralSection(_sectionContent[Section.General]);
            BuildUnitsSection(_sectionContent[Section.Units]);
            BuildCommandersSection(_sectionContent[Section.Commanders]);
            BuildBoundariesSection(_sectionContent[Section.Boundaries]);
            BuildEffectsSection(_sectionContent[Section.Effects]);
            BuildArtillerySection(_sectionContent[Section.Artillery]);
            BuildAirStrikeSection(_sectionContent[Section.AirStrike]);
            BuildUavStrikeSection(_sectionContent[Section.UavStrike]);
            BuildNavalStrikeSection(_sectionContent[Section.NavalStrike]);
            BuildMissionsSection(_sectionContent[Section.Missions]);
            BuildWeatherSection(_sectionContent[Section.Weather]);
            BuildMapSection(_sectionContent[Section.Map]);
            BuildDateTimeSection(_sectionContent[Section.DateTime]);

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

        /// <summary>Raised by the MISSILE SYSTEMS nav row; the controller opens the right-hand board.</summary>
        public System.Action MissileSystemsRequested;

        /// <summary>Opens a section, or closes the panel if that section is already showing.</summary>
        void OpenSection(Section section)
        {
            // MISSILE SYSTEMS has no section of its own. It closes the sliding
            // panel and hands over to the right-hand board, which is honest
            // about where the controls actually are — leaving the left panel
            // open on whatever was last shown would look like the click missed.
            if (section == Section.Missiles)
            {
                ClosePanel();
                MissileSystemsRequested?.Invoke();
                return;
            }

            if (_panelOpen && _section == section) { ClosePanel(); return; }

            _section = section;
            _panelOpen = true;
            _sectionPanel.gameObject.SetActive(true);

            foreach (var kv in _sectionContent)
                kv.Value.gameObject.SetActive(kv.Key == section);

            // The commanders page skips rebuilds while it is shut — loading a
            // map would otherwise rebuild it once per formation spawned.
            if (section == Section.Commanders) _commanders?.OnShown();

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

        /// <summary>True while the right-hand missile board is up; drives its nav row's highlight.</summary>
        bool _missilesOpen;

        /// <summary>
        /// Tells the rail whether the missile board is showing. Its row cannot
        /// use the section panel's own open state — it has no section — and a
        /// nav row that never lights up reads as a button that did nothing.
        /// </summary>
        public void SetMissilePanelOpen(bool open)
        {
            _missilesOpen = open;
            PaintNav();
        }

        /// <summary>Which nav row reads as active — none at all while the panel is closed.</summary>
        void PaintNav()
        {
            foreach (var (s, _, fill, glyph, label, bar) in _navRows)
            {
                bool on = s == Section.Missiles ? _missilesOpen : (_panelOpen && s == _section);
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
            // buried underneath it.
            LeftChromeEdge = x + PanelWidth;
            if (_mapControls != null) _mapControls.SetLeftInset(LeftChromeEdge);

            if (_slide <= 0f) _sectionPanel.gameObject.SetActive(false);
        }

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

            AddNavRow(panel, Section.General, "GENERAL", UiIcons.Flag, -44);
            AddNavRow(panel, Section.Units, "UNITS", UiIcons.Person, -80);
            AddNavRow(panel, Section.Boundaries, "CONTROL MEASURES", UiIcons.Square, -116);
            AddNavRow(panel, Section.Effects, "EFFECTS", UiIcons.Flame, -152);
            AddNavRow(panel, Section.Artillery, "ARTILLERY STRIKE", UiIcons.Artillery, -188);
            AddNavRow(panel, Section.AirStrike, "AIR STRIKE", UiIcons.FlyingWing, -224);
            AddNavRow(panel, Section.UavStrike, "UAV STRIKES", UiIcons.Quadcopter, -260);
            // Opens a panel on the *right* rather than a section in the sliding
            // panel — see MissilePanelUI for why that one needs the width.
            AddNavRow(panel, Section.Missiles, "MISSILE SYSTEMS", UiIcons.Interceptor, -296);
            // Last of the five fire menus, so all the ways of putting explosives
            // on a piece of ground sit together in the rail.
            AddNavRow(panel, Section.NavalStrike, "NAVY STRIKE", UiIcons.Warship, -332);
            // The single-player campaign's missions, edited here and played from
            // the main menu — see docs/22-MISSIONS.md.
            AddNavRow(panel, Section.Commanders, "COMMANDERS", UiIcons.Orders, -368);
            AddNavRow(panel, Section.Missions, "MISSIONS", UiIcons.Pin, -404);
            AddNavRow(panel, Section.Weather, "WEATHER CONDITIONS", UiIcons.Cloud, -440);
            AddNavRow(panel, Section.Map, "MAP", UiIcons.Layers, -476);
            AddNavRow(panel, Section.DateTime, "DATE AND TIME", UiIcons.Clock, -512);

            var rule = UIFactory.CreateDivider(panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -HeaderHeight + 6);
        }

        void AddNavRow(RectTransform panel, Section section, string label, Sprite glyph, float y)
        {
            var row = UIFactory.CreatePanel(panel, "Nav_" + label, new Color(0, 0, 0, 0));
            UIFactory.Place(row, new Vector2(0f, 1f), new Vector2(0, y), new Vector2(RailWidth, 34));
            row.pivot = new Vector2(0f, 1f);

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

            // "WEATHER CONDITIONS" is the longest label the rail has to carry, so
            // the row's text is fitted rather than clipped at a fixed width.
            var text = UIFactory.CreateText(row, label, UiTheme.FontBody, UiTheme.TextDim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(Pad + 26, 0),
                new Vector2(RailWidth - Pad - 34f, 20));
            UIFactory.Fit(text);

            _navRows.Add((section, label, row.GetComponent<Image>(), img, text, bar));
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
            Populate();
        }

        void OnUnitsChanged()
        {
            if (_listMode == ListMode.Deployed) Populate();
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
        /// The draggable catalogue, grouped under a heading per
        /// <see cref="UnitBranch"/>.
        ///
        /// Flat, this list is 117 cards deep and finding the mortar in it means
        /// scrolling past the ships. Walking the branches in declaration order
        /// puts manoeuvre first and the tail last, which is the order an order
        /// of battle is written in — and an empty branch prints no heading, so a
        /// search never leaves a bare label behind.
        /// </summary>
        int PopulateAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            int count = 0;

            foreach (var branch in UnitBranchInfo.All)
            {
                bool headed = false;
                foreach (var def in UnitDatabase.All)
                {
                    if (def.Branch != branch) continue;
                    if (!Matches(def.name, def.id, def.ammoType)) continue;

                    if (!headed)
                    {
                        BranchHeader(UnitBranchInfo.DisplayName(branch));
                        headed = true;
                    }
                    CreateAvailableCard(def, folder);
                    count++;
                }
            }

            if (count == 0) EmptyRow("No unit type matches that search.");
            return count;
        }

        /// <summary>Divider row naming the arm the cards under it belong to.</summary>
        void BranchHeader(string label)
        {
            var row = UIFactory.CreateGroup(_listContent, "Branch_" + label);
            row.sizeDelta = new Vector2(0, 24);

            var text = UIFactory.CreateSectionHeader(row, label.ToUpperInvariant(), UiTheme.Accent);
            UIFactory.PlaceTopLeft(text.rectTransform, CardIconX, 8f, CardTextWidth + CardIconSize, 14f);
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

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.onClick.AddListener(() => SelectUnitRequested?.Invoke(actor));

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
            // reads as five unexplained glyphs without this.
            var caption = UIFactory.CreateSectionHeader(strip, "TOOLS", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(caption.rectTransform, Pad, 8f, RailWidth - Pad * 2f, 14f);

            AddTool(strip, 0, UiIcons.Cursor, () => SelectToolRequested?.Invoke());
            AddTool(strip, 1, UiIcons.Pencil, () => DefensiveLineToolRequested?.Invoke());
            AddTool(strip, 2, UiIcons.Square, () => BoundaryToolRequested?.Invoke());
            AddTool(strip, 3, UiIcons.Pin, () => GenerateSectorsRequested?.Invoke());
            AddTool(strip, 4, UiIcons.Chart, ToggleView);

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
                // not modes — only the first three latch.
                if (captured <= 2) SetActiveTool(captured);
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

        /// <summary>Called by the controller when the draw tool exits on its own.</summary>
        public void ResetToolToSelect() => SetActiveTool(0);

        // -------------------------------------------------- boundaries section

        /// <summary>Raised with the kind to draw; the controller opens the options panel.</summary>
        public System.Action<LineKind> ControlMeasureRequested;

        /// <summary>
        /// The kinds of control measure that can be drawn by hand.
        ///
        /// Picking a kind is a separate decision from styling it, which is why
        /// this is a section in the rail and the options are a panel on the
        /// right: kind changes how the line should be laid on the ground — a
        /// rear boundary runs parallel to the front, a lateral one runs into it
        /// — so it is chosen before anything else and then left alone, while
        /// colour and width are fiddled with until they look right.
        /// </summary>
        void BuildBoundariesSection(RectTransform content)
        {
            SectionLabel(content, "DRAW A CONTROL MEASURE", -8);

            float y = -30f;
            foreach (var (kind, name, detail) in BoundaryPanelUI.Kinds)
            {
                ControlMeasureButton(content, kind, name, detail, y);
                y -= 58f;
            }

            var stop = UIFactory.CreateBorderedPanel(content, "StopDrawing", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var stopBtn = UIFactory.CreateButton(stop, "STOP DRAWING",
                () => { SelectToolRequested?.Invoke(); ResetToolToSelect(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Pick a kind, set it up in the panel on the right, then click the map to place each vertex. " +
                "Enter or double-click finishes the line; Esc abandons it. The style carries over, so a run of " +
                "phase lines only needs setting up once.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 110));
        }

        void ControlMeasureButton(RectTransform content, LineKind kind, string name, string detail, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Kind_" + name, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => ControlMeasureRequested?.Invoke(kind),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, UiIcons.Square, "Glyph");
            icon.color = UiTheme.Accent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(20, 20));

            UIFactory.CreateStackedLabels(frame, name, detail, 42f, InnerWidth - 54f, topInset: 9f);
        }

        // ----------------------------------------------------- effects section

        /// <summary>
        /// Hand-placed effects: arm one, then click the terrain. Named EFFECTS
        /// rather than "Particles" because that is what they are to the player —
        /// how they are drawn is an implementation detail, and the same section
        /// would hold a decal or a mesh effect later.
        /// </summary>
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
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(52, 16));

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
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(52, 16));

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
                "Every sortie, armed or not, costs one of the scenario's 99 strikes.",
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
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(52, 16));

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
                "A mission cannot be recalled once away — CHECK FIRE only stands the gun down. Every mission " +
                "spends one of the scenario's 99 strikes.",
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

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                40f, InnerWidth - 88f, topInset: 6f);

            // Beaten zone over round count: the two numbers that decide which
            // gun to call for, and the pair that says what "naval" means here.
            var figures = UIFactory.CreateText(frame,
                $"{def.radiusMeters:0} m\n{def.roundsPerMission} rds",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            figures.raycastTarget = false;
            UIFactory.Place(figures.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(52, 30));

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

            // --- actions ---
            var save = UIFactory.CreateBorderedPanel(content, "SaveMission", UiTheme.Success, UiTheme.Success);
            UIFactory.Place(save, new Vector2(0f, 1f), new Vector2(Pad, -724), new Vector2(InnerWidth, 36));
            var saveBtn = UIFactory.CreateButton(save, "SAVE MISSION + MAP", CommitMission,
                new Color(0, 0, 0, 0), Color.white, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)saveBtn.transform);

            MissionActionButton(content, "NEW MISSION HERE", -768, UiTheme.Surface, UiTheme.Text, () =>
            {
                string name = _missionName != null && !string.IsNullOrWhiteSpace(_missionName.text)
                    ? _missionName.text.Trim()
                    : "New mission";
                MissionCreateRequested?.Invoke(_missionCampaign, name);
            });

            MissionActionButton(content, "DELETE MISSION", -808, UiTheme.Danger, Color.white, () =>
            {
                if (_mission != null) MissionDeleteRequested?.Invoke(_mission);
            });

            _missionStatus = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.Place(_missionStatus.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -850), new Vector2(InnerWidth, 34));

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
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -888),
                new Vector2(InnerWidth, 250));

            RefreshMissionList();
        }

        /// <summary>Height of the MISSIONS page inside its scroll view.</summary>
        const float MissionsPageHeight = 1160f;

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
        public void MarkBoundaryToolActive() => SetActiveTool(2);

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
        readonly List<Text> _budgetLabels = new List<Text>();

        /// <summary>
        /// The shared strike allowance, shown at the head of each fire menu.
        ///
        /// It is on **all three** of them, and on the missile board, because the
        /// pool is shared: a player who spends it on artillery has spent it on
        /// air strikes too, and a counter that appeared only in the menu being
        /// used would let them find that out the hard way. See
        /// <see cref="StrikeBudget"/>.
        /// </summary>
        void StrikeBudgetRow(RectTransform content, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "StrikeBudget",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 28));

            var name = UIFactory.CreateText(frame, "STRIKES REMAINING", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(InnerWidth - 96f, 14));

            var value = UIFactory.CreateText(frame, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            value.raycastTarget = false;
            UIFactory.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(80, 16));

            _budgetLabels.Add(value);
        }

        /// <summary>Repaints every allowance readout. Driven by the budget's own event.</summary>
        void RefreshStrikeBudget()
        {
            foreach (var label in _budgetLabels)
            {
                if (label == null) continue;
                label.text = StrikeBudget.RemainingText;
                label.color = StrikeBudget.RemainingColour(UiTheme.Accent, UiTheme.Warning, UiTheme.Hostile);
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
            // The commanders panel subscribes to two registries of its own.
            _commanders?.Dispose();
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
