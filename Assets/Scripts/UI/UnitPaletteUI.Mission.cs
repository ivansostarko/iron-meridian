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
    /// <see cref="UnitPaletteUI"/> — scenario authoring: players, commanders, missions, the mission area, HQ zones and deployment zones.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: players section, commanders section, missions section, fields ---, actions ---, mission area, HQ zones, deployment zones.
    /// </summary>
    public partial class UnitPaletteUI
    {
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
        Campaign _missionCampaign = Campaign.Europe;
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
            // HQ ZONES and DEPLOYMENT ZONES are on the ZONES panel — see
            // BuildZonesSection. They are ground a mission names rather than
            // fields of its record, and they were the bottom half of a page
            // whose top half is text boxes.

            // --- actions ---
            var save = UIFactory.CreateBorderedPanel(content, "SaveMission", UiTheme.Success, UiTheme.Success);
            UIFactory.Place(save, new Vector2(0f, 1f), new Vector2(Pad, -MissionActionsTop - 12f), new Vector2(InnerWidth, 36));
            var saveBtn = UIFactory.CreateButton(save, "SAVE MISSION + MAP", CommitMission,
                new Color(0, 0, 0, 0), Color.white, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)saveBtn.transform);

            MissionActionButton(content, "NEW MISSION HERE", -MissionActionsTop - 56f, UiTheme.Surface, UiTheme.Text, () =>
            {
                string name = _missionName != null && !string.IsNullOrWhiteSpace(_missionName.text)
                    ? _missionName.text.Trim()
                    : "New mission";
                MissionCreateRequested?.Invoke(_missionCampaign, name);
            });

            MissionActionButton(content, "DELETE MISSION", -MissionActionsTop - 96f, UiTheme.Danger, Color.white, () =>
            {
                if (_mission != null) MissionDeleteRequested?.Invoke(_mission);
            });

            _missionStatus = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.Place(_missionStatus.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -MissionActionsTop - 138f), new Vector2(InnerWidth, 34));

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
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -MissionActionsTop - 176f),
                new Vector2(InnerWidth, 250));

            RefreshMissionList();
        }

        /// <summary>
        /// Height of the MISSIONS page inside its scroll view. Grew with the HQ
        /// ZONES block — everything below it is placed relative to
        /// <see cref="HqBlockBottom"/> so the page and its contents can never
        /// drift apart.
        /// </summary>
        const float MissionsPageHeight = MissionActionsTop + 440f;

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

        /// <summary>
        /// Top of the HQ ZONES block on the **ZONES** page, and the bottom it
        /// hands back. It used to be 718 px down the MISSIONS page; on a page of
        /// its own it starts at the top.
        /// </summary>
        const float HqBlockTop = 8f;
        const float HqBlockEnd = HqBlockTop + 176f;
        /// <summary>The DEPLOYMENT ZONES block below it, and the bottom the page continues from.</summary>
        const float DeployBlockTop = HqBlockEnd + 8f;
        const float HqBlockBottom = DeployBlockTop + 176f;

        /// <summary>Where the MISSIONS page resumes after the mission-area block.</summary>
        const float MissionActionsTop = 718f;

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
    }
}
