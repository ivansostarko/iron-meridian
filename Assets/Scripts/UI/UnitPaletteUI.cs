using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// Left "Order of Battle" panel: a vertical section nav (General, Units,
    /// Map), the section's controls, and a tool strip along the bottom.
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

        // Tool strip.
        public System.Action SelectToolRequested;
        public System.Action BoundaryToolRequested;
        public System.Action DefensiveLineToolRequested;

        // Deployed list.
        public System.Action<UnitActor> SelectUnitRequested;
        public System.Action<UnitActor> RemoveUnitRequested;

        enum Section { General, Units, Effects, Weather, Map, DateTime }
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
        const float PanelWidth = UiTheme.LeftPanelWidth;
        const float Pad = UiTheme.PanelPadding;
        const float InnerWidth = PanelWidth - Pad * 2f;
        /// <summary>Text width inside a list card: card width less the icon column and right padding.</summary>
        const float CardTextWidth = InnerWidth - 58f;
        /// <summary>Title block plus the six nav rows.</summary>
        const float HeaderHeight = 266f;
        const float ToolStripHeight = 56f;

        Team _team = Team.User;
        Affiliation _affiliation = Affiliation.Friendly;
        Echelon _echelon = Echelon.Battalion;
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

        readonly List<(Section section, RectTransform row, Image fill, Image glyph, Text label, RectTransform bar)> _navRows =
            new List<(Section, RectTransform, Image, Image, Text, RectTransform)>();
        readonly Dictionary<Section, RectTransform> _sectionContent = new Dictionary<Section, RectTransform>();
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
        GameObject _groundMarker;
        CesiumGlobeAnchor _groundMarkerAnchor;
        PlacementMarker _markerAnim;
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
            MapControlsUI mapControls, LineDrawTool drawTool)
        {
            _canvas = canvas;
            _map = map;
            _worldCam = worldCam;
            _rig = rig;
            _clock = clock;
            _weather = weather;
            _effects = effects;
            _mapControls = mapControls;
            _drawTool = drawTool;

            var panel = UIFactory.CreatePanel(canvas.transform, "UnitPalette", UiTheme.Panel);
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(0, 0);
            panel.offsetMax = new Vector2(PanelWidth, -UiTheme.TopBarHeight);

            // Hairline down the panel's right edge, separating it from the map.
            var edge = UIFactory.CreatePanel(panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            BuildHeader(panel);

            var body = UIFactory.CreateGroup(panel, "Body");
            body.anchorMin = new Vector2(0, 0); body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(0, ToolStripHeight);
            body.offsetMax = new Vector2(0, -HeaderHeight);

            _sectionContent[Section.General] = MakeSectionContent(body, "General");
            _sectionContent[Section.Units] = MakeSectionContent(body, "Units");
            _sectionContent[Section.Effects] = MakeSectionContent(body, "Effects");
            _sectionContent[Section.Weather] = MakeSectionContent(body, "Weather");
            _sectionContent[Section.Map] = MakeSectionContent(body, "Map");
            _sectionContent[Section.DateTime] = MakeSectionContent(body, "DateTime");

            BuildGeneralSection(_sectionContent[Section.General]);
            BuildUnitsSection(_sectionContent[Section.Units]);
            BuildEffectsSection(_sectionContent[Section.Effects]);
            BuildWeatherSection(_sectionContent[Section.Weather]);
            BuildMapSection(_sectionContent[Section.Map]);
            BuildDateTimeSection(_sectionContent[Section.DateTime]);

            BuildToolStrip(panel);
            SetSection(Section.Units);

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
        }

        static RectTransform MakeSectionContent(RectTransform body, string name)
        {
            var rt = UIFactory.CreateGroup(body, "Section_" + name);
            UIFactory.Stretch(rt);
            return rt;
        }

        // ------------------------------------------------------------ header

        void BuildHeader(RectTransform panel)
        {
            var emblem = UIFactory.CreateImage(panel, UiIcons.Shield, "Emblem");
            emblem.color = UiTheme.Accent;
            emblem.raycastTarget = false;
            UIFactory.Place((RectTransform)emblem.transform, new Vector2(0f, 1f), new Vector2(Pad, -14), new Vector2(19, 19));

            var title = UIFactory.CreateText(panel, "ORDER OF BATTLE", UiTheme.FontHeading,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad + 27, -13), new Vector2(210, 22));

            AddNavRow(panel, Section.General, "GENERAL", UiIcons.Flag, -44);
            AddNavRow(panel, Section.Units, "UNITS", UiIcons.Person, -80);
            AddNavRow(panel, Section.Effects, "EFFECTS", UiIcons.Flame, -116);
            AddNavRow(panel, Section.Weather, "WEATHER CONDITIONS", UiIcons.Cloud, -152);
            AddNavRow(panel, Section.Map, "MAP", UiIcons.Layers, -188);
            AddNavRow(panel, Section.DateTime, "DATE AND TIME", UiIcons.Clock, -224);

            var rule = UIFactory.CreateDivider(panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -HeaderHeight + 6);
        }

        void AddNavRow(RectTransform panel, Section section, string label, Sprite glyph, float y)
        {
            var row = UIFactory.CreatePanel(panel, "Nav_" + label, new Color(0, 0, 0, 0));
            UIFactory.Place(row, new Vector2(0f, 1f), new Vector2(0, y), new Vector2(PanelWidth, 34));
            row.pivot = new Vector2(0f, 1f);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => SetSection(section));

            // Left accent bar marks the active section, as in the design.
            var bar = UIFactory.CreatePanel(row, "ActiveBar", UiTheme.Accent);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
            bar.pivot = new Vector2(0, 0.5f);
            bar.sizeDelta = new Vector2(3, 0);
            bar.GetComponent<Image>().raycastTarget = false;

            var img = UIFactory.CreateImage(row, glyph, "Glyph");
            img.raycastTarget = false;
            UIFactory.Place((RectTransform)img.transform, new Vector2(0f, 0.5f), new Vector2(Pad, 0), new Vector2(16, 16));

            var text = UIFactory.CreateText(row, label, UiTheme.FontBody, UiTheme.TextDim,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(Pad + 26, 0), new Vector2(190, 20));

            _navRows.Add((section, row, row.GetComponent<Image>(), img, text, bar));
        }

        void SetSection(Section section)
        {
            _section = section;
            foreach (var (s, _, fill, glyph, label, bar) in _navRows)
            {
                bool on = s == section;
                fill.color = on ? UiTheme.AccentWash : new Color(0, 0, 0, 0);
                glyph.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                label.color = on ? UiTheme.Text : UiTheme.TextDim;
                bar.gameObject.SetActive(on);
            }
            foreach (var kv in _sectionContent)
                kv.Value.gameObject.SetActive(kv.Key == section);
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

            // --- affiliation + echelon ---
            var affDd = UIFactory.CreateDropdown(content,
                new List<string> { "Friendly", "Hostile", "Neutral", "Unknown" }, 0,
                i => _affiliation = (Affiliation)i);
            StyleDropdown(affDd, -50);

            var echNames = new List<string>(System.Enum.GetNames(typeof(Echelon)));
            var echDd = UIFactory.CreateDropdown(content, echNames, (int)Echelon.Battalion,
                i => _echelon = (Echelon)i);
            StyleDropdown(echDd, -90);

            // --- search ---
            var searchFrame = UIFactory.CreateBorderedPanel(content, "SearchFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(searchFrame, new Vector2(0f, 1f), new Vector2(Pad, -130), new Vector2(InnerWidth, 34));

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
            UIFactory.Place(badge, new Vector2(0f, 1f), new Vector2(PanelWidth - Pad - 34, -172), new Vector2(34, 18));
            badge.GetComponent<Image>().raycastTarget = false;
            _listCount = UIFactory.CreateText(badge, "0", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_listCount.rectTransform);

            // --- the list itself ---
            var scroll = UIFactory.CreateScrollView(content, out _listContent);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 2);
            srt.offsetMax = new Vector2(0, -196);

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
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, -170), new Vector2(82, 22));
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

        int PopulateAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            int count = 0;
            foreach (var def in UnitDatabase.All)
            {
                if (!Matches(def.name, def.id, def.ammoType)) continue;
                CreateAvailableCard(def, folder);
                count++;
            }
            if (count == 0) EmptyRow("No unit type matches that search.");
            return count;
        }

        int PopulateDeployed()
        {
            int count = 0, index = 0;
            foreach (var actor in UnitRegistry.All)
            {
                if (actor == null || !actor.IsAlive) continue;
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
            card.sizeDelta = new Vector2(0, 50);

            var sprite = CardIcon(card, folder, def.id);

            UIFactory.CreateStackedLabels(card, def.name,
                $"ATK {def.attack:0}  ·  DEF {def.defence:0}  ·  {def.speedKmh:0} km/h",
                50f, CardTextWidth, topInset: 8f, titleSize: UiTheme.FontBody);

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
            card.sizeDelta = new Vector2(0, 58);

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
            UIFactory.PlaceTopLeft(title.rectTransform, 50f, 5f, cardW, 17f);
            UIFactory.Fit(title);

            var subtitle = UIFactory.CreateText(card, actor.Def.name, UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(subtitle.rectTransform, 50f, 22f, cardW, 15f);
            UIFactory.Fit(subtitle);

            // Third line is the design's metadata row. Real readiness data
            // rather than a decorative timestamp.
            var meta = UIFactory.CreateText(card,
                $"{s.echelon}  ·  STR {s.strength * 100f:0}%  ·  {s.status}",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(meta.rectTransform, 50f, 38f, cardW, 15f);
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
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(36, 36));
                return sprite;
            }

            // Keep the layout intact and visibly flag the gap.
            var fallback = UIFactory.CreatePanel(card, "IconFallback", UiTheme.Panel);
            UIFactory.Place(fallback, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(36, 36));
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

            AddTool(strip, 0, UiIcons.Cursor, () => SelectToolRequested?.Invoke());
            AddTool(strip, 1, UiIcons.Pencil, () => DefensiveLineToolRequested?.Invoke());
            AddTool(strip, 2, UiIcons.Square, () => BoundaryToolRequested?.Invoke());
            AddTool(strip, 3, UiIcons.Pin, () => GenerateSectorsRequested?.Invoke());
            AddTool(strip, 4, UiIcons.Chart, ToggleView);

            SetActiveTool(0);
        }

        void AddTool(RectTransform strip, int index, Sprite glyph, UnityEngine.Events.UnityAction action)
        {
            var frame = UIFactory.CreateBorderedPanel(strip, "Tool" + index, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 0.5f), new Vector2(Pad + index * 42f, 0), new Vector2(36, 32));

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

            SectionLabel(content, "CONTROL MEASURES", -394);

            var boundaryFrame = UIFactory.CreateBorderedPanel(content, "BoundaryOptions", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(boundaryFrame, new Vector2(0f, 1f), new Vector2(Pad, -416), new Vector2(InnerWidth, 46));

            var bBtn = UIFactory.CreateButton(boundaryFrame, "", OpenBoundaryOptions,
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)bBtn.transform);
            var bCaption = bBtn.GetComponentInChildren<Text>(true);
            if (bCaption != null) bCaption.gameObject.SetActive(false);

            var bIcon = UIFactory.CreateImage(boundaryFrame, UiIcons.Square, "Glyph");
            bIcon.color = UiTheme.Accent;
            bIcon.raycastTarget = false;
            UIFactory.Place((RectTransform)bIcon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(18, 18));

            UIFactory.CreateStackedLabels(boundaryFrame, "BOUNDARY OPTIONS",
                "Type, side, colour, width, caption", 40f, InnerWidth - 52f, topInset: 6f);

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

        void OpenBoundaryOptions()
        {
            if (_drawTool == null || _canvas == null) return;
            // Latch the tool strip's boundary button so the armed state is
            // visible in both places once drawing starts.
            BoundaryOptionsDialog.Open(_canvas, _drawTool, () => SetActiveTool(2));
        }

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
                // Remember exactly where the ring is sitting: the deploy uses
                // this point rather than re-raycasting on release, so the unit
                // cannot land somewhere the preview never showed.
                _dropLat = lat; _dropLon = lon;
                double h = GeoUtils.SampleTerrainHeight(_map.Georeference, lat, lon, 250) + 3.0;
                _groundMarkerAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(lon, lat, h);
                _groundMarker.SetActive(true);
            }
            else
            {
                _groundMarker.SetActive(false);
            }
        }

        void EndDrag(PointerEventData e)
        {
            _dragGhost.gameObject.SetActive(false);
            _groundMarker.SetActive(false);
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
                DropRejected?.Invoke("Terrain not loaded here yet — try again in a moment.");
                _dragging = null;
                return;
            }

            DropRequested?.Invoke(_dragging, _team, _affiliation, _echelon, _dropLat, _dropLon);
            _dragging = null;
        }

        void BuildGroundMarker()
        {
            _groundMarker = new GameObject("PlacementPreview");
            _groundMarker.transform.SetParent(_map.Georeference.transform, false);
            _groundMarkerAnchor = _groundMarker.AddComponent<CesiumGlobeAnchor>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_groundMarker.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = Vector3.one * 320f;
            var mat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Reticle(UiTheme.Accent));
            quad.GetComponent<MeshRenderer>().material = mat;

            _markerAnim = _groundMarker.AddComponent<PlacementMarker>();
            _markerAnim.Init(quad.transform, mat);

            _groundMarker.SetActive(false);
        }

        /// <summary>
        /// Idle animation for the drop reticle: a slow spin plus a breathing
        /// pulse, so it reads as a live cursor rather than a decal stamped on
        /// the imagery. Scale is driven in world metres, so it stays a constant
        /// ground footprint regardless of zoom.
        /// </summary>
        class PlacementMarker : MonoBehaviour
        {
            const float BaseSize = 320f;

            Transform _quad;
            Material _mat;
            float _t;

            public void Init(Transform quad, Material mat)
            {
                _quad = quad; _mat = mat;
            }

            // Re-shown each time the pointer re-enters valid terrain, which
            // replays the pop-in.
            void OnEnable() => _t = 0f;

            void Update()
            {
                if (_quad == null) return;
                _t += Time.unscaledDeltaTime;

                float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.18f));
                float breathe = 1f + Mathf.Sin(_t * 3.4f) * 0.05f;
                _quad.localScale = Vector3.one * (BaseSize * pop * breathe);

                _quad.localRotation = Quaternion.Euler(90f, 0f, _t * 26f);

                var c = _mat.color;
                c.a = Mathf.Lerp(0.35f, 0.95f, (Mathf.Sin(_t * 3.4f) + 1f) * 0.5f) * pop;
                _mat.color = c;
            }
        }

        void OnDestroy()
        {
            // Build() subscribes to the map and registry; without this the
            // callbacks fire into a destroyed component on scene reload.
            UnitRegistry.Changed -= OnUnitsChanged;
            if (_clock != null) _clock.StartChanged -= RefreshStartLabel;
            if (_weather != null) _weather.Changed -= RefreshWeather;
            if (_effects != null) _effects.ArmedChanged -= RefreshEffects;
            if (_map == null) return;
            _map.ViewModeChanged -= OnViewModeChanged;
            _map.StyleChanged -= OnStyleChanged;
        }
    }
}
