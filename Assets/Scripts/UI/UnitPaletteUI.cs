using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Left-side "Order of Battle" panel, organised as three accordion tabs:
    /// General (reserved for future use), Units (team/affiliation/echelon
    /// pickers and the draggable unit list) and Map (tile style + 2D/3D).
    /// Pick team (Friendly/Enemy), affiliation and echelon, then DRAG a unit
    /// card onto the terrain to deploy it. A live ground marker tracks the
    /// actual 3D drop point during the drag, so what you see is exactly where
    /// the unit lands — not just a floating 2D ghost.
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

        Button _autoSectorBtn;
        bool _autoSectors;

        Team _team = Team.User;
        Affiliation _affiliation = Affiliation.Friendly;
        Echelon _echelon = Echelon.Battalion;

        RectTransform _listContent;
        Button _blueTab, _redTab;
        Image _dragGhost;
        Canvas _canvas;
        UnitDefinition _dragging;

        MapManager _map;
        CameraRig _rig;
        Camera _worldCam;
        GameObject _groundMarker;
        CesiumGlobeAnchor _groundMarkerAnchor;
        PlacementMarker _markerAnim;
        bool _lastDropValid;
        double _dropLat, _dropLon;

        static readonly MapStyle[] Styles = { MapStyle.Satellite, MapStyle.Terrain, MapStyle.Roads };
        Dropdown _styleDropdown;
        // Cached rather than fetched via GetComponentInChildren: the MAP
        // section starts collapsed, and that call skips inactive children —
        // it returned null and threw on the first ViewModeChanged event.
        Text _viewBtnLabel;

        const float HeaderHeight = 32f;
        const float Gap = 4f;

        class AccordionSection
        {
            public RectTransform header;
            public RectTransform content;
            public Text arrow;
            public bool expanded;
            public float contentHeight;   // 0 => flexible, fills leftover space
        }

        RectTransform _accordionRoot;
        readonly List<AccordionSection> _sections = new List<AccordionSection>();

        public void Build(Canvas canvas, MapManager map, Camera worldCam, CameraRig rig)
        {
            _canvas = canvas;
            _map = map;
            _worldCam = worldCam;
            _rig = rig;
            var panel = UIFactory.CreatePanel(canvas.transform, "UnitPalette", GameConfig.UiPanel);
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(0, 0);
            panel.offsetMax = new Vector2(270, -50);

            var title = UIFactory.CreateText(panel, "ORDER OF BATTLE", 18,
                GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(260, 30));

            _accordionRoot = UIFactory.CreateGroup(panel, "Accordion");
            _accordionRoot.anchorMin = new Vector2(0, 0);
            _accordionRoot.anchorMax = new Vector2(1, 1);
            _accordionRoot.offsetMin = new Vector2(6, 6);
            _accordionRoot.offsetMax = new Vector2(-6, -40);

            // contentHeight 0 marks the section that soaks up the leftover
            // space; the others keep their fixed height.
            var generalContent = AddSection("GENERAL", false, 186);
            var unitsContent = AddSection("UNITS", true, 0);
            var mapContent = AddSection("MAP", false, 110);

            BuildGeneralTab(generalContent);
            BuildUnitsTab(unitsContent);
            BuildMapTab(mapContent);
            Relayout();

            // Drag ghost (top-most)
            var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            ghostGo.transform.SetParent(canvas.transform, false);
            _dragGhost = ghostGo.GetComponent<Image>();
            _dragGhost.raycastTarget = false;
            _dragGhost.preserveAspect = true;
            ((RectTransform)ghostGo.transform).sizeDelta = new Vector2(70, 70);
            ghostGo.SetActive(false);

            BuildGroundMarker();
            Populate();

            _map.ViewModeChanged += OnViewModeChanged;
            _map.StyleChanged += OnStyleChanged;
        }

        // ------------------------------------------------------- accordion

        RectTransform AddSection(string label, bool startExpanded, float contentHeight)
        {
            var header = UIFactory.CreatePanel(_accordionRoot, "Header_" + label, GameConfig.UiPanelLight);
            var btn = header.gameObject.AddComponent<Button>();
            btn.targetGraphic = header.GetComponent<Image>();

            var arrow = UIFactory.CreateText(header, startExpanded ? "▾" : "▸", 13, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(arrow.rectTransform, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(18, 26));

            var text = UIFactory.CreateText(header, label, 14, GameConfig.UiText,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(26, 0), new Vector2(200, 26));

            var content = UIFactory.CreateGroup(_accordionRoot, "Content_" + label);
            content.gameObject.SetActive(startExpanded);

            var section = new AccordionSection
            {
                header = header,
                content = content,
                arrow = arrow,
                expanded = startExpanded,
                contentHeight = contentHeight
            };
            _sections.Add(section);

            btn.onClick.AddListener(() => ToggleSection(section));
            return content;
        }

        void ToggleSection(AccordionSection s)
        {
            s.expanded = !s.expanded;
            s.content.gameObject.SetActive(s.expanded);
            s.arrow.text = s.expanded ? "▾" : "▸";
            Relayout();
        }

        /// <summary>
        /// Positions headers/contents by hand rather than via a layout group:
        /// the flexible section is stretched between what sits above it and
        /// what sits below it, so collapsed sections stay pinned and visible
        /// instead of being pushed off the bottom of the panel.
        /// </summary>
        void Relayout()
        {
            int flex = -1;
            for (int i = 0; i < _sections.Count; i++)
                if (_sections[i].contentHeight <= 0f && _sections[i].expanded) { flex = i; break; }

            float top = 0f;
            int lastTop = flex >= 0 ? flex : _sections.Count - 1;
            for (int i = 0; i <= lastTop; i++)
            {
                var s = _sections[i];
                PlaceFromTop(s.header, ref top, HeaderHeight);
                if (i == flex) break;                       // its content stretches, handled below
                if (s.expanded) PlaceFromTop(s.content, ref top, s.contentHeight);
            }

            if (flex < 0) return;

            float bottom = 0f;
            for (int i = _sections.Count - 1; i > flex; i--)
            {
                var s = _sections[i];
                if (s.expanded) PlaceFromBottom(s.content, ref bottom, s.contentHeight);
                PlaceFromBottom(s.header, ref bottom, HeaderHeight);
            }

            var fc = _sections[flex].content;
            fc.anchorMin = new Vector2(0, 0);
            fc.anchorMax = new Vector2(1, 1);
            fc.pivot = new Vector2(0.5f, 0.5f);
            fc.offsetMax = new Vector2(0, -top);
            fc.offsetMin = new Vector2(0, bottom);
        }

        static void PlaceFromTop(RectTransform rt, ref float y, float h)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, h);
            rt.anchoredPosition = new Vector2(0, -y);
            y += h + Gap;
        }

        static void PlaceFromBottom(RectTransform rt, ref float y, float h)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0, h);
            rt.anchoredPosition = new Vector2(0, y);
            y += h + Gap;
        }

        // ------------------------------------------------------- general tab

        /// <summary>
        /// Tactical-graphics controls: derive each side's sector boundaries and
        /// FEBA from where its units currently stand.
        /// </summary>
        void BuildGeneralTab(RectTransform content)
        {
            var heading = UIFactory.CreateText(content, "TACTICAL GRAPHICS", 12,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(4, -4), new Vector2(240, 18));

            GeneralButton(content, "GENERATE SECTORS", -26, GameConfig.UiPanelLight,
                () => GenerateSectorsRequested?.Invoke());

            GeneralButton(content, "CLEAR GRAPHICS", -64, GameConfig.UiPanelLight,
                () => ClearSectorsRequested?.Invoke());

            _autoSectorBtn = GeneralButton(content, "AUTO-UPDATE: OFF", -102, GameConfig.UiPanelLight,
                () =>
                {
                    _autoSectors = !_autoSectors;
                    AutoSectorsChanged?.Invoke(_autoSectors);
                    RefreshAutoSectorLabel();
                });

            var hint = UIFactory.CreateText(content,
                "Boundaries run rear-to-front between\nadjacent formations; FEBA follows the\nforward units.",
                10, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(4, -140), new Vector2(248, 40));
        }

        Button GeneralButton(RectTransform content, string label, float y, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(content, label, action, bg, GameConfig.UiText, 13);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-8, 32);
            rt.anchoredPosition = new Vector2(0, y);
            return b;
        }

        void RefreshAutoSectorLabel()
        {
            if (_autoSectorBtn == null) return;
            _autoSectorBtn.GetComponentInChildren<Text>(true).text =
                _autoSectors ? "AUTO-UPDATE: ON" : "AUTO-UPDATE: OFF";
            _autoSectorBtn.image.color = _autoSectors ? GameConfig.UiAccent : GameConfig.UiPanelLight;
        }

        // ------------------------------------------------------- units tab

        void BuildUnitsTab(RectTransform content)
        {
            _blueTab = UIFactory.CreateButton(content, "FRIENDLY", () => SetTeam(Team.User),
                GameConfig.BlueTeam, Color.white, 15);
            UIFactory.Place((RectTransform)_blueTab.transform, new Vector2(0f, 1f), new Vector2(2, -6), new Vector2(122, 36));

            _redTab = UIFactory.CreateButton(content, "ENEMY", () => SetTeam(Team.Enemy),
                GameConfig.UiPanelLight, Color.white, 15);
            UIFactory.Place((RectTransform)_redTab.transform, new Vector2(0f, 1f), new Vector2(132, -6), new Vector2(122, 36));

            var affLabel = UIFactory.CreateText(content, "Affiliation", 14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(affLabel.rectTransform, new Vector2(0f, 1f), new Vector2(4, -52), new Vector2(96, 24));
            var affDd = UIFactory.CreateDropdown(content,
                new List<string> { "Friendly", "Hostile", "Neutral", "Unknown" }, 0,
                i => _affiliation = (Affiliation)i);
            UIFactory.Place((RectTransform)affDd.transform, new Vector2(0f, 1f), new Vector2(90, -48), new Vector2(164, 34));

            var echLabel = UIFactory.CreateText(content, "Echelon", 14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(echLabel.rectTransform, new Vector2(0f, 1f), new Vector2(4, -90), new Vector2(96, 24));
            var echNames = new List<string>(System.Enum.GetNames(typeof(Echelon)));
            var echDd = UIFactory.CreateDropdown(content, echNames, (int)Echelon.Battalion,
                i => _echelon = (Echelon)i);
            UIFactory.Place((RectTransform)echDd.transform, new Vector2(0f, 1f), new Vector2(90, -86), new Vector2(164, 34));

            var hint = UIFactory.CreateText(content, "Drag a unit onto the map to deploy",
                13, GameConfig.UiTextDim);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(260, 22));

            // Scrollable unit list
            var scroll = UIFactory.CreateScrollView(content, out _listContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 2);
            srt.offsetMax = new Vector2(0, -146);
        }

        void SetTeam(Team team)
        {
            _team = team;
            _affiliation = team == Team.User ? Affiliation.Friendly : Affiliation.Hostile;
            _blueTab.image.color = team == Team.User ? GameConfig.BlueTeam : GameConfig.UiPanelLight;
            _redTab.image.color = team == Team.Enemy ? GameConfig.RedTeam : GameConfig.UiPanelLight;
            Populate();
        }

        void Populate()
        {
            foreach (Transform child in _listContent) Destroy(child.gameObject);
            string folder = _team == Team.User ? "Friendly" : "Enemy";

            string lastCategory = null;
            foreach (var def in UnitDatabase.All)
            {
                if (def.category != lastCategory)
                {
                    lastCategory = def.category;
                    var header = UIFactory.CreateText(_listContent,
                        def.Category == UnitCategory.Drone ? "— DRONE UNITS —" : "— CORE GROUND UNITS —",
                        13, GameConfig.UiAccent);
                    var hrt = header.rectTransform;
                    hrt.sizeDelta = new Vector2(0, 22);
                }
                CreateCard(def, folder);
            }
        }

        void CreateCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreatePanel(_listContent, "Card_" + def.id, GameConfig.UiPanelLight);
            card.sizeDelta = new Vector2(0, 48);

            var sprite = UIFactory.LoadIconSprite(folder, def.id);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(6, 0), new Vector2(42, 42));
                ((RectTransform)icon.transform).pivot = new Vector2(0, 0.5f);
            }
            else
            {
                // Keep the layout intact and visibly flag the gap instead of leaving empty space.
                var fallback = UIFactory.CreatePanel(card, "IconFallback", GameConfig.UiPanel);
                UIFactory.Place(fallback, new Vector2(0f, 0.5f), new Vector2(6, 0), new Vector2(42, 42));
                fallback.pivot = new Vector2(0, 0.5f);
                var mark = UIFactory.CreateText(fallback, "?", 20, GameConfig.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(mark.rectTransform);
            }

            var name = UIFactory.CreateText(card, def.name, 14, null, TextAnchor.MiddleLeft);
            UIFactory.Stretch(name.rectTransform);
            name.rectTransform.offsetMin = new Vector2(56, 15);

            var stats = UIFactory.CreateText(card,
                $"ATK {def.attack:0}  DEF {def.defence:0}  SPD {def.speedKmh:0} km/h",
                11, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Stretch(stats.rectTransform);
            stats.rectTransform.offsetMin = new Vector2(56, 0);
            stats.rectTransform.offsetMax = new Vector2(0, -21);

            // Drag handling
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(def, sprite));
            AddEvent(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
            AddEvent(trigger, EventTriggerType.EndDrag, e => EndDrag((PointerEventData)e));
        }

        static void AddEvent(EventTrigger t, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(cb);
            t.triggers.Add(entry);
        }

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
            var mat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Reticle(GameConfig.UiAccent));
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

        // ------------------------------------------------------- map tab

        void BuildMapTab(RectTransform content)
        {
            var styleLabel = UIFactory.CreateText(content, "Tile style", 14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(styleLabel.rectTransform, new Vector2(0f, 1f), new Vector2(4, -4), new Vector2(120, 22));

            _styleDropdown = UIFactory.CreateDropdown(content,
                new List<string> { "SATELLITE", "TERRAIN", "ROADS" },
                System.Array.IndexOf(Styles, _map.Style), OnStyleSelected);
            var ddRt = (RectTransform)_styleDropdown.transform;
            ddRt.anchorMin = new Vector2(0, 1); ddRt.anchorMax = new Vector2(1, 1);
            ddRt.pivot = new Vector2(0.5f, 1f);
            ddRt.offsetMin = new Vector2(4, -60);
            ddRt.offsetMax = new Vector2(-4, -26);

            var viewBtn = UIFactory.CreateButton(content, _map.ViewMode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D",
                ToggleView, GameConfig.UiPanelLight, null, 15);
            _viewBtnLabel = viewBtn.GetComponentInChildren<Text>(true);
            var vbRt = (RectTransform)viewBtn.transform;
            vbRt.anchorMin = new Vector2(0, 1); vbRt.anchorMax = new Vector2(1, 1);
            vbRt.pivot = new Vector2(0.5f, 1f);
            vbRt.offsetMin = new Vector2(4, -100);
            vbRt.offsetMax = new Vector2(-4, -66);
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

        void OnDestroy()
        {
            // Build() subscribes to the map; without this the callbacks fire
            // into a destroyed component on scene reload.
            if (_map == null) return;
            _map.ViewModeChanged -= OnViewModeChanged;
            _map.StyleChanged -= OnStyleChanged;
        }
    }
}
