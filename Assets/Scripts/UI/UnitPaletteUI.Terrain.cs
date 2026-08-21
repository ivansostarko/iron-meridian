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
    /// <see cref="UnitPaletteUI"/> — what is drawn on the ground: map objects, air-dropped supplies, mines and obstacles, and logistic installations.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: map objects, supplies section, mines and obstacles section, logistics section.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ------------------------------------------------------- map objects

        IronMeridian.Lines.MapObjectSystem _mapObjects;
        readonly List<(MapObjectKind kind, Image fill, Text label)> _objectButtons =
            new List<(MapObjectKind, Image, Text)>();
        RectTransform _objectList;
        Text _objectCount;

        /// <summary>Fly the camera to a drawn object — the list rows' action.</summary>
        public System.Action<MapObjectData> MapObjectFocusRequested;

        public void BindMapObjects(IronMeridian.Lines.MapObjectSystem system)
        {
            _mapObjects = system;
            if (_mapObjects != null) _mapObjects.Team = _team;
            RefreshMapObjects();
        }

        const float ObjectsPageHeight = 10 * 50f + 560f;

        /// <summary>
        /// OBJECTS — the infrastructure a scenario is fought over, drawn on the
        /// ground rather than dropped on it.
        ///
        /// **Why these are polygons.** A depot is a place and LOGISTICS marks it
        /// with a point. A bridge, an airfield or a quarter of a city is an
        /// *extent*: what matters is how much ground it covers and where its
        /// ends are, which a marker cannot say. Four corners minimum — see
        /// <see cref="MapObjectCatalog.MinCorners"/>.
        ///
        /// The side comes from the tabs at the head of the page, as it does on
        /// LOGISTICS and MINES AND OBSTACLES: a bridge in friendly hands and the
        /// same bridge in the enemy's are different problems.
        /// </summary>
        void BuildObjectsSection(RectTransform section)
        {
            var content = SidedPage(ScrollableSection(section, ObjectsPageHeight + SideBlock));

            float y = -30f;
            foreach (var def in MapObjectCatalog.All)
            {
                ObjectButton(content, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(content, "StopDrawing", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 4f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STOP DRAWING",
                () => { if (_mapObjects != null) _mapObjects.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Pick a kind, then click at least four corners on the map. Backspace undoes a corner, " +
                "right-click or Enter closes the outline, Esc abandons it. The kind stays armed so a " +
                "row of bridges is a row of outlines, not ten trips back to this panel.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 42f),
                new Vector2(InnerWidth, 76));

            SectionLabel(content, "ON THIS MAP", y - 124f);

            _objectCount = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_objectCount.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 146f), new Vector2(InnerWidth, 16));

            var scroll = UIFactory.CreateScrollView(content, out _objectList,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0, y - 168f);
            srt.sizeDelta = new Vector2(-Pad * 2f, 240f);

            var layout = _objectList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(2, 2, 2, 2); }

            RefreshMapObjects();
        }

        void ObjectButton(RectTransform content, MapObjectDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Object_" + def.kind,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var captured = def.kind;
            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_mapObjects != null) _mapObjects.Arm(captured); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            // A swatch in the object's own colour: the panel and the ground use
            // one palette, so a row here is findable on the map.
            var swatch = UIFactory.CreatePanel(frame, "Swatch", ParseHex(def.colorHex));
            UIFactory.Place(swatch, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(6, 26));
            swatch.GetComponent<Image>().raycastTarget = false;

            var label = UIFactory.CreateText(frame, def.name, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(label.rectTransform, 26f, 6f, InnerWidth - 40f, 16f);
            UIFactory.Fit(label, 9);
            label.raycastTarget = false;

            var note = UIFactory.CreateText(frame, def.description, UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(note.rectTransform, 26f, 22f, InnerWidth - 40f, 20f);
            UIFactory.Fit(note, 8);
            note.raycastTarget = false;

            _objectButtons.Add((def.kind, frame.Find("Fill").GetComponent<Image>(), label));
        }

        /// <summary>"#RRGGBB" to a colour, falling back to the accent rather than to black.</summary>
        static Color ParseHex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : UiTheme.Accent;

        /// <summary>Repaints the armed kind and the list of what is on the map.</summary>
        public void RefreshMapObjects()
        {
            if (_mapObjects == null) return;

            foreach (var (kind, fill, label) in _objectButtons)
            {
                bool on = _mapObjects.Armed.HasValue && _mapObjects.Armed.Value == kind;
                if (fill != null) fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_objectList == null) return;

            int blue = _mapObjects.CountFor(Team.User), red = _mapObjects.CountFor(Team.Enemy);
            if (_objectCount != null)
                _objectCount.text = $"DRAWN — {blue} FRIENDLY · {red} ENEMY";

            ClearChildren(_objectList);

            foreach (var data in _mapObjects.Objects)
            {
                var captured = data;
                var def = MapObjectCatalog.Get(data.KindEnum);
                bool friendly = data.TeamEnum == Team.User;

                var row = UIFactory.CreateBorderedPanel(_objectList, "Obj_" + data.id,
                    UiTheme.Surface, UiTheme.Border);
                row.sizeDelta = new Vector2(0, 34);

                var focus = UIFactory.CreateButton(row, "",
                    () => MapObjectFocusRequested?.Invoke(captured),
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)focus.transform);
                var made = focus.GetComponentInChildren<Text>(true);
                if (made != null) made.gameObject.SetActive(false);

                var side = UIFactory.CreatePanel(row, "Side",
                    friendly ? UiTheme.Friendly : UiTheme.Hostile);
                UIFactory.Place(side, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(4, 20));
                side.GetComponent<Image>().raycastTarget = false;

                var text = UIFactory.CreateText(row, $"{def.name}  ·  {data.points.Count} corners",
                    UiTheme.FontLabel, UiTheme.Text, TextAnchor.MiddleLeft);
                UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(20, 0),
                    new Vector2(InnerWidth - 70f, 16));
                UIFactory.Fit(text, 8);
                text.raycastTarget = false;

                var remove = UIFactory.CreateIconButton(row, UiIcons.Trash,
                    () => { _mapObjects.Remove(captured); },
                    new Color(0, 0, 0, 0), UiTheme.TextFaint, 7f);
                UIFactory.Place((RectTransform)remove.transform, new Vector2(1f, 0.5f),
                    new Vector2(-6, 0), new Vector2(24, 24));
            }
        }

        // --------------------------------------------------- supplies section

        RectTransform _suppliesList;
        Text _suppliesHeadline;

        /// <summary>
        /// **SUPPLIES** — what every friendly formation is actually carrying:
        /// ammunition, fuel and rations, formation by formation.
        ///
        /// **Why this and not SUSTAINMENT.** SUSTAINMENT is the theatre's
        /// stocks — one set of figures for the whole side, and the rate the force
        /// burns them. It answers "how long can this army fight". This answers a
        /// different question, the one a commander asks before ordering an
        /// attack: *which battalion is out of ammunition*. A total cannot say
        /// that, because a side with three days of fuel in depot still has a
        /// company that cannot move.
        ///
        /// **Friendly only.** What the enemy is carrying is not something a
        /// commander knows, and a page that told them would undo fog of war more
        /// completely than any reconnaissance could — see docs/16-FOG-OF-WAR.md.
        ///
        /// Read live off <see cref="UnitState"/> and <see cref="UnitDefinition"/>
        /// on every rebuild: this holds no figures of its own, so it cannot
        /// disagree with the formation's own info panel.
        /// </summary>
        void BuildSuppliesSection(RectTransform content)
        {
            _suppliesHeadline = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(_suppliesHeadline.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -28),
                new Vector2(InnerWidth, 30));

            var scroll = UIFactory.CreateScrollView(content, out _suppliesList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 46);
            srt.offsetMax = new Vector2(-Pad, -62);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _suppliesList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 3; layout.padding = new RectOffset(2, 2, 2, 2); }

            var hint = UIFactory.CreateText(content,
                "AMM is rounds carried against the type's scale, FUEL litres, RAT days of rations. " +
                "A formation runs amber below a third and red when it is out. Click a row to select it.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 36));

            RefreshSupplies();
        }

        /// <summary>
        /// Rebuilds the supply table. Driven from <see cref="OnUnitsChanged"/>
        /// and from opening the section, and — like every other live page on the
        /// rail — silent while it is shut.
        /// </summary>
        public void RefreshSupplies()
        {
            if (_suppliesList == null) return;
            if (!_sectionContent.TryGetValue(Section.Supplies, out var page) ||
                !page.gameObject.activeSelf) return;

            ClearChildren(_suppliesList);

            int formations = 0, dry = 0, low = 0;
            long ammo = 0, ammoScale = 0;

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.Def == null) continue;
                if (u.State.TeamEnum != Team.User) continue;

                formations++;
                ammo += u.State.ammo;
                ammoScale += u.Def.ammoStock;

                float share = u.Def.ammoStock > 0 ? u.State.ammo / (float)u.Def.ammoStock : 1f;
                if (u.State.ammo <= 0 && u.Def.ammoStock > 0) dry++;
                else if (share < LowSupplyShare) low++;

                SupplyRow(u);
            }

            if (_suppliesHeadline != null)
                _suppliesHeadline.text = formations == 0
                    ? "No friendly formations on the map."
                    : $"{formations} formation(s)  ·  {(ammoScale > 0 ? Mathf.RoundToInt(100f * ammo / ammoScale) : 100)}% of ammunition scale\n" +
                      $"{dry} out of ammunition  ·  {low} below a third";

            if (formations == 0)
            {
                var empty = UIFactory.CreateText(_suppliesList,
                    "Nothing deployed for this side yet.", UiTheme.FontLabel,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 32);
            }
        }

        /// <summary>Share of scale below which a stock reads as low rather than held.</summary>
        const float LowSupplyShare = 1f / 3f;

        /// <summary>
        /// One formation's line: its icon and name over the three stocks it
        /// carries, each coloured by how much of its own scale is left. Absolute
        /// figures rather than bars — "180 / 600" is a number a commander can
        /// weigh against a fire plan, and a bar is not.
        /// </summary>
        void SupplyRow(UnitActor unit)
        {
            var def = unit.Def;
            var state = unit.State;

            var row = UIFactory.CreateBorderedPanel(_suppliesList, "Sup_" + state.instanceId,
                UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 46);

            var captured = unit;
            var select = UIFactory.CreateButton(row, "", () => SelectUnitRequested?.Invoke(captured),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)select.transform);
            var made = select.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var sprite = UIFactory.LoadIconSprite("Friendly", def.id);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(row, sprite, "Icon");
                icon.raycastTarget = false;
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                    new Vector2(8, 0), new Vector2(28, 28));
            }

            string name = string.IsNullOrEmpty(state.customName) ? def.name : state.customName;
            var title = UIFactory.CreateText(row, name, UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            title.raycastTarget = false;
            UIFactory.PlaceTopLeft(title.rectTransform, 42f, 5f, InnerWidth - 70f, 15f);
            UIFactory.Fit(title, 8);

            // The three stocks, laid out as thirds so the columns line up down
            // the table however long the names above them are.
            float cell = (InnerWidth - UIFactory.ScrollbarWidth - 52f) / 3f;
            SupplyCell(row, 42f, cell, "AMM", $"{state.ammo:n0}/{def.ammoStock:n0}",
                def.ammoStock > 0 ? state.ammo / (float)def.ammoStock : 1f);
            SupplyCell(row, 42f + cell, cell, "FUEL",
                def.fuelStock > 0 ? $"{state.fuel:n0} L" : "—",
                def.fuelStock > 0 ? state.fuel / def.fuelStock : 1f);
            SupplyCell(row, 42f + cell * 2f, cell, "RAT", $"{state.foodDays} d",
                def.foodDays > 0 ? state.foodDays / (float)def.foodDays : 1f);
        }

        void SupplyCell(RectTransform row, float x, float width, string label, string value, float share)
        {
            var caption = UIFactory.CreateText(row, label, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            caption.raycastTarget = false;
            UIFactory.PlaceTopLeft(caption.rectTransform, x, 24f, 26f, 14f);

            var text = UIFactory.CreateText(row, value, UiTheme.FontLabel, SupplyColour(share),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            text.raycastTarget = false;
            UIFactory.PlaceTopLeft(text.rectTransform, x + 26f, 24f, Mathf.Max(20f, width - 28f), 14f);
            UIFactory.Fit(text, 8);
        }

        static Color SupplyColour(float share) =>
            share <= 0f ? UiTheme.Danger
            : share < LowSupplyShare ? UiTheme.Warning
            : UiTheme.Text;

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
                12f, GroupsRowWidth - 94f, topInset: 8f);

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
        /// Which mode the rail is in, or null before the first call — which is
        /// how "the scene arrived in this mode" is told from "the player just
        /// switched to it".
        /// </summary>
        bool? _battleMode;

        /// <summary>
        /// Raised once on a real change of mode, after the rail has put its own
        /// panel away.
        ///
        /// The rail only owns the section panel; the right-hand dock, the unit
        /// inspector, the type card and the fire menus belong to the controller.
        /// Rather than reach across for them, it says "the mode changed" and the
        /// controller resets what it owns — see GameController, which hangs the
        /// same tidy-up off this that RESET uses.
        /// </summary>
        public System.Action ModeChanged;

        /// <summary>
        /// Swaps the rail between its two lists — see ApplyModeVisibility,
        /// ScenarioSections and BattleSections — and opens the section the new
        /// mode starts on.
        ///
        /// **A change of mode closes the panel.** Whatever was open belonged to
        /// the job that has just ended: a section left up across the switch is a
        /// page about laying a scenario out sitting over a battle, or the other
        /// way round. Closing hands the screen back to the map, which is the one
        /// thing both jobs are about, and the rail is one click from any of it.
        /// It matches what the editor does on load, where nothing is open either.
        /// </summary>
        public void SetBattleMode(bool running)
        {
            if (_modeHeading != null)
            {
                _modeHeading.text = running ? "BATTLE MODE" : "SCENARIO MODE";
                _modeHeading.color = running ? UiTheme.Success : UiTheme.TextDim;
            }

            ApplyModeVisibility(running);

            // Only on a real change of mode. This is also called once during
            // Build to put the rail into whatever mode the scene arrived in, and
            // there is nothing to reset on the way in.
            //
            // ApplyModeVisibility above already closes a section the new mode
            // does not have; this closes the ones it does, which is every other
            // case.
            if (_battleMode.HasValue && _battleMode.Value != running)
            {
                ClosePanel();
                ModeChanged?.Invoke();
            }
            _battleMode = running;

            if (running) RefreshGroups();
        }

        // ------------------------------------------- mines and obstacles section

        IronMeridian.Lines.ObstacleSystem _obstacles;
        readonly List<(ObstacleKind kind, Image fill, Text label)> _obstacleButtons =
            new List<(ObstacleKind, Image, Text)>();
        RectTransform _obstacleList;
        Text _obstacleCount;

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
            var content = SidedPage(ScrollableSection(section, ObstaclePageHeight + SideBlock));

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
                "because a barrier is several graphics rather than one. Right-click or Esc stops.\n\n" +
                "MINEFIELD is drawn as an area instead: click round the belt — three corners or more — " +
                "then right-click or Enter to close it, Backspace to undo a corner, Esc to abandon. " +
                "A minefield is ground rather than a place, and it is the one barrier that bites: " +
                "formations that drive through an enemy belt while a battle is running set off mines.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 290f),
                new Vector2(InnerWidth, 170));

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

            if (_obstacleList == null) return;

            int blue = _obstacles.CountFor(Team.User), red = _obstacles.CountFor(Team.Enemy);
            if (_obstacleCount != null)
            {
                // While an outline is open the line counts corners instead. It
                // is the one number the designer cannot read off the map, and
                // the minimum is what they are working towards.
                _obstacleCount.text = _obstacles.Drawing
                    ? $"OUTLINING — {_obstacles.DraftCorners} CORNER(S), " +
                      $"{ObstacleCatalog.MinAreaCorners} NEEDED"
                    : $"LAID — {blue} FRIENDLY · {red} ENEMY";
            }

            ClearChildren(_obstacleList);

            foreach (var marker in _obstacles.Markers)
            {
                if (marker == null) continue;
                var def = ObstacleCatalog.Get(marker.Kind);
                bool enemy = marker.Data.team == nameof(Team.Enemy);

                var row = UIFactory.CreatePanel(_obstacleList, "ObsRow_" + marker.Data.id, UiTheme.SurfaceSubtle);
                row.sizeDelta = new Vector2(0, 30);

                var pip = UIFactory.CreateImage(row, UiIcons.GlyphFor(marker.Kind), "Glyph");
                pip.color = def.tint;
                pip.raycastTarget = false;
                UIFactory.Place((RectTransform)pip.transform, new Vector2(0f, 0.5f),
                    new Vector2(8, 0), new Vector2(16, 16));

                // An area reports the ground it covers; a stamp reports the
                // bearing it was laid on. Each is the figure that says whether
                // the graphic is where it was meant to go, and neither means
                // anything for the other sort.
                string measure = marker.IsArea
                    ? $"{IronMeridian.Lines.ObstacleSystem.AreaKm2(marker.Data.points):0.0} km²"
                    : $"{marker.Data.headingDeg:000}°";

                var label = UIFactory.CreateText(row,
                    $"{def.name}   ·   {measure}",
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
        Text _logisticsCount;
        RectTransform _serviceRingLamp;
        Text _serviceRingLabel;

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
            content = SidedPage(content);

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

            y -= 40f;

            // SHOW SERVICE RINGS. The one control on this panel that is about
            // *judging* a laydown rather than making one: with it on, every
            // installation draws the ground it serves, draped on the terrain, so
            // the question the radii exist for — does this rear area actually
            // cover the force — can be answered by looking rather than by
            // arithmetic. Off by default because each ring is a terrain-sampled
            // band and a rear area is a dozen sites. See docs/26-LOGISTICS.md §4a.
            var rings = UIFactory.CreateBorderedPanel(content, "ServiceRings", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(rings, new Vector2(0f, 1f), new Vector2(Pad, y - 4f), new Vector2(InnerWidth, 34));

            var ringsBtn = UIFactory.CreateButton(rings, "",
                () =>
                {
                    if (_logistics == null) return;
                    _logistics.SetServiceRingsVisible(!_logistics.ServiceRingsVisible);
                },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)ringsBtn.transform);
            var ringsCaption = ringsBtn.GetComponentInChildren<Text>(true);
            if (ringsCaption != null) ringsCaption.gameObject.SetActive(false);

            _serviceRingLabel = UIFactory.CreateText(rings, "SHOW SERVICE RINGS",
                UiTheme.FontSmall, UiTheme.Text, TextAnchor.MiddleLeft);
            _serviceRingLabel.raycastTarget = false;
            UIFactory.Place(_serviceRingLabel.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(InnerWidth - 60f, 16));

            _serviceRingLamp = UIFactory.CreatePanel(rings, "Lamp", UiTheme.Border);
            UIFactory.Place(_serviceRingLamp, new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(12, 12));
            _serviceRingLamp.GetComponent<Image>().raycastTarget = false;

            UiTooltip.Attach(rings.gameObject,
                "Draw the ground every installation serves, on the terrain. " +
                "The one view that says whether a rear area covers the force it is behind.",
                UiTooltip.Side.Right);

            float listTop = y - 48f;
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

        /// <summary>
        /// One kind's button — **drag it onto the map, or click it and then
        /// click the map.**
        ///
        /// Two gestures on one control, exactly as a unit card carries them,
        /// and for the same reasons. A drag is the direct statement — you are
        /// carrying the thing and you put it down — and it is the one that makes
        /// a rear area quick to lay out. A click-arm is the gesture a drag
        /// cannot make: onto ground you have to pan to first, and from a session
        /// driven by a pad rather than a mouse.
        ///
        /// **A drag released back over the button arms the kind**, and that is
        /// deliberate rather than accidental. uGUI suppresses the click after a
        /// drag only when the pressed object and the dragged object differ; here
        /// one EventTrigger is both, so releasing over the button still raises
        /// PointerClick — and it raises it *before* EndDrag
        /// (`StandaloneInputModule.ProcessMousePress`). `Toggle` stands the drag
        /// down as it arms, so the EndDrag that follows finds nothing to place
        /// and says nothing. A gesture that never reached the map therefore
        /// leaves the tool armed, which is the useful reading of it, and never
        /// deploys a site and arms the tool off one press.
        /// </summary>
        void LogisticsButton(RectTransform content, LogisticsDef def, float y)
        {
            var kind = def.kind;
            var frame = UIFactory.CreateBorderedPanel(content, "Log_" + kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e =>
            {
                if (_logistics == null) return;
                _logistics.BeginDrag(kind);
                _dragGhost.sprite = UiIcons.GlyphFor(kind);
                _dragGhost.color = def.tint;
                _dragGhost.gameObject.SetActive(true);
            });
            AddEvent(trigger, EventTriggerType.Drag, e =>
            {
                if (_logistics == null) return;
                var pointer = (PointerEventData)e;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)_canvas.transform, pointer.position, _canvas.worldCamera,
                    out Vector2 local);
                ((RectTransform)_dragGhost.transform).anchoredPosition = local;
                _logistics.DragTo(pointer.position, pointer.pointerCurrentRaycast.gameObject != null);
            });
            AddEvent(trigger, EventTriggerType.EndDrag, e =>
            {
                _dragGhost.gameObject.SetActive(false);
                // The palette's ghost is shared with the unit drag, which tints
                // it per side; put the tint back so the next unit dragged is not
                // wearing this installation's colour.
                _dragGhost.color = Color.white;
                if (_logistics == null) return;
                var pointer = (PointerEventData)e;
                _logistics.EndDrag(pointer.position, pointer.pointerCurrentRaycast.gameObject != null);
            });
            AddEvent(trigger, EventTriggerType.PointerClick, e =>
            {
                if (_logistics != null) _logistics.Toggle(kind);
            });

            UiTooltip.Attach(frame.gameObject,
                $"{def.name} — drag onto the map to deploy, or click to arm and then click the ground. " +
                $"Serves {def.serviceRadiusKm:0.#} km; holds {def.defaultStock:0.#} issues.",
                UiTooltip.Side.Right);

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

            if (_serviceRingLamp != null)
            {
                bool on = _logistics.ServiceRingsVisible;
                _serviceRingLamp.GetComponent<Image>().color = on ? UiTheme.Accent : UiTheme.Border;
                if (_serviceRingLabel != null)
                    _serviceRingLabel.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_logisticsList == null) return;

            int blue = _logistics.CountFor(Team.User), red = _logistics.CountFor(Team.Enemy);
            if (_logisticsCount != null)
                _logisticsCount.text = $"DEPLOYED — {blue} FRIENDLY · {red} ENEMY";

            ClearChildren(_logisticsList);

            foreach (var site in _logistics.Sites)
            {
                if (site == null) continue;
                var def = LogisticsCatalog.Get(site.Kind);
                bool enemy = site.Data.team == nameof(Team.Enemy);

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
    }
}
