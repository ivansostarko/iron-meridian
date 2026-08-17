using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.UI;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.Core
{
    /// <summary>
    /// Entry point of the Game (Dev) scene. Builds the Cesium map centred on
    /// Lyon, spawns the saved order of battle, and wires every system: camera,
    /// selection, palette drag-drop, line tools, auto front line, combat and
    /// the HUD. Attach to an empty GameObject; everything else is created at
    /// runtime.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        /// <summary>
        /// The scenario this scene opens. Overridden by the mission the player
        /// picked, if there is one — see <see cref="_mission"/>.
        /// </summary>
        public string mapFileName = "lyon_dev.json";

        /// <summary>
        /// The mission being played or edited, or null in the plain map editor.
        ///
        /// The Game scene is one scene doing two jobs: it is the map editor
        /// reached from DEVELOPMENT, and it is what a single-player mission is
        /// played in. That is deliberate — a mission *is* a map with an order of
        /// battle on it, and a second scene would be the same systems wired the
        /// same way with a different name. What the mission changes is where the
        /// map opens, what it is called, and where BACK goes.
        /// </summary>
        MissionDefinition _mission;

        MapManager _map;
        CameraRig _rig;
        SelectionManager _selection;
        LineManager _lines;
        MarkerManager _markers;
        /// <summary>Draws and shows the open mission's boundary — see docs/22-MISSIONS.md.</summary>
        MissionAreaTool _areaTool;
        FrontlineSystem _frontline;
        SectorSystem _sectors;
        DefenceOrderSystem _defence;
        /// <summary>Ring / line / quadrant graphics for every placed task — docs/15-COMBAT-ORDERS.md.</summary>
        TaskAreaSystem _taskAreas;
        /// <summary>The five movement tasks and the standing commands under them.</summary>
        ManoeuvreOrderSystem _manoeuvre;
        /// <summary>Drawn intentions. Nothing it puts on the map executes.</summary>
        PlannerSystem _planner;
        CombatSystem _combat;
        AttackOrderSystem _attacks;
        ReconOrderSystem _recon;
        FogOfWarSystem _fog;
        VfxSystem _vfx;
        WeatherSystem _weather;
        EffectPlacementTool _effects;
        ArtilleryStrikeSystem _artillery;
        AirStrikeSystem _airStrike;
        AirSupplySystem _airSupply;
        UavStrikeSystem _uavStrike;
        MissileStrikeSystem _missiles;
        NavalStrikeSystem _naval;
        AirDefenceSystem _airDefence;
        StrikeAftermath _aftermath;

        // Latest countdown reported by each strike system. A null title means
        // that system has nothing in the air; see RefreshStrikeBanner.
        (string title, float remaining, float total, Color colour) _artilleryBanner;
        (string title, float remaining, float total, Color colour) _airStrikeBanner;
        (string title, float remaining, float total, Color colour) _airSupplyBanner;
        (string title, float remaining, float total, Color colour) _uavStrikeBanner;
        (string title, float remaining, float total, Color colour) _missileBanner;
        (string title, float remaining, float total, Color colour) _navalBanner;

        /// <summary>
        /// Shows whichever strike is closest to landing. Ties are impossible in
        /// practice and harmless if they happen — either is the right answer.
        /// </summary>
        void RefreshStrikeBanner()
        {
            var pick = _artilleryBanner;

            foreach (var other in new[] { _airStrikeBanner, _airSupplyBanner, _uavStrikeBanner,
                                          _missileBanner, _navalBanner })
            {
                bool sooner = other.title != null &&
                              (pick.title == null || other.remaining < pick.remaining);
                if (sooner) pick = other;
            }

            _hud.SetFireMission(pick.title, pick.remaining, pick.total,
                pick.title == null ? UiTheme.Accent : pick.colour);
        }
        MapControlsUI _mapControls;
        GameClock _clock;
        GameHUD _hud;
        UnitPaletteUI _palette;
        StrikeDockUI _strikeDock;
        FrontlinePanelUI _frontlinePanel;
        UnitHoverTooltip _hoverTooltip;
        UnitClusterLayer _clusters;
        ConnectivityWatcher _connectivity;
        UnitInfoPanel _infoPanel;
        UnitTypePanel _typePanel;
        IronMeridian.Lines.MapObjectSystem _mapObjects;
        IronMeridian.Logistics.LogisticsSystem _logistics;
        IronMeridian.Logistics.SustainmentSystem _sustainment;
        ReinforcementSystem _reinforcements;
        IronMeridian.Lines.ObstacleSystem _obstacles;
        MiniMapUI _minimap;
        GroupPanelUI _groupPanel;
        UnitActionBarUI _actionBar;
        PauseMenuUI _pauseMenu;
        LoadingScreenUI _loading;
        UnityEngine.Canvas _canvas;
        MapSaveData _save;

        /// <summary>True while the loading overlay is still covering the map.</summary>
        bool Loading => _loading != null;

        RangeRing _losRing, _weaponRing;
        UnitActor _rangeRingUnit;
        double _rangeRingLat, _rangeRingLon;
        /// <summary>Line-of-sight ring on selection. On by default; toggled from the GENERAL panel.</summary>
        bool _showLineOfSight = true;
        /// <summary>Max-weapon-range ring on selection. Same deal — GENERAL owns the switch.</summary>
        bool _showWeaponRange = true;

        readonly System.Collections.Generic.List<UnitState> _clipboard =
            new System.Collections.Generic.List<UnitState>();
        double _clipboardCentreLat, _clipboardCentreLon;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            // No hover sound on this screen — a policy of the map editor's, not
            // a change to the player's setting, and lifted again in OnDestroy.
            // See AudioManager.HoverSuppressed.
            IronMeridian.Audio.AudioManager.HoverSuppressed = true;
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            UnitRegistry.Clear();
            // Both static, so they survive a scene load: a fresh scenario
            // opening with half its strikes already spent, or with the last
            // one's casualty list still in it, would be inexplicable.
            StrikeBudget.Reset();
            LossLedger.Clear();

            // Up first, on its own high-sorting canvas, so it covers the map and
            // the HUD built below while Cesium streams the terrain in.
            _loading = LoadingScreenUI.Show(GameConfig.GameName, "Preparing the operational map");

            // A mission picked from SINGLE PLAYER decides the scenario; without
            // one this is the map editor on its dev map.
            _mission = MissionLibrary.Selected;
            if (_mission != null)
            {
                mapFileName = _mission.ResolvedMapFile;
                _loading.SetStatus($"Preparing {_mission.name}");
            }

            _save = (_mission != null ? MissionLibrary.LoadMap(_mission) : SaveSystem.LoadMap(mapFileName))
                ?? new MapSaveData
                {
                    mapName = "Lyon Dev",
                    centerLatitude = GameConfig.LyonLatitude,
                    centerLongitude = GameConfig.LyonLongitude
                };

            // --- world ---
            _map = gameObject.AddComponent<MapManager>();
            _map.Build(_save.centerLatitude, _save.centerLongitude);

            // --- camera ---
            _rig = gameObject.AddComponent<CameraRig>();
            Vector3 focus = GeoUtils.GeoToUnity(_map.Georeference,
                _save.centerLatitude, _save.centerLongitude, 300);
            _rig.Init(focus, (float)_save.cameraHeightMeters);
            _rig.Cam.tag = "MainCamera";

            // --- systems ---
            _lines = gameObject.AddComponent<LineManager>();
            _lines.Init(_map.Georeference);

            _markers = gameObject.AddComponent<MarkerManager>();
            _markers.Init(_map.Georeference);

            // The mission's own boundary. Deliberately not a LineManager line:
            // it belongs to the mission record, not to the map file underneath
            // it — see MissionAreaTool.
            _areaTool = gameObject.AddComponent<MissionAreaTool>();
            _areaTool.Init(_map, _rig.Cam);

            _frontline = gameObject.AddComponent<FrontlineSystem>();
            _frontline.Init(_lines, _map, _rig.Cam);

            _sectors = gameObject.AddComponent<SectorSystem>();
            _sectors.Init(_lines, _map.Georeference);

            // Defend / Hold / Guard. Its graphics are ordinary lines and
            // markers, so they save and load with the rest of the map.
            // Every placed task draws through one system, so a defence, a
            // recon objective and a rally point are read the same way.
            _taskAreas = gameObject.AddComponent<TaskAreaSystem>();
            _taskAreas.Init(_lines, _markers, _map.Georeference);

            _defence = gameObject.AddComponent<DefenceOrderSystem>();
            _defence.Init(_lines, _markers, _taskAreas);

            _manoeuvre = gameObject.AddComponent<ManoeuvreOrderSystem>();
            _manoeuvre.Init(_taskAreas);

            _planner = gameObject.AddComponent<PlannerSystem>();
            _planner.Init(_lines);

            // Effects must exist before any unit spawns — a unit restored below
            // strength starts burning the moment it is built.
            _vfx = gameObject.AddComponent<VfxSystem>();
            _vfx.Init(_map.Georeference);

            // What a strike leaves on the ground once the burst is over: half an
            // hour of fire, then two hours of smoke, both on the operational
            // clock — see docs/08-PARTICLE-SYSTEMS.md.
            _aftermath = gameObject.AddComponent<StrikeAftermath>();

            _combat = gameObject.AddComponent<CombatSystem>();

            // Offensive tasks. Ordered attacks take precedence over the
            // automatic exchange, so this has to exist before the first tick.
            _attacks = gameObject.AddComponent<AttackOrderSystem>();
            _attacks.Init(_combat, _map.Georeference);

            // Clock runs only in game mode; the editor is timeless.
            _clock = gameObject.AddComponent<GameClock>();
            // The FLOT's history snapshots run on the scenario clock.
            _frontline.Clock = _clock;
            _combat.RunningChanged += _clock.SetRunning;

            // Limited intelligence, and the recon tasks that are the only way to
            // see past a unit's own eyes once it is on. Fog needs the clock —
            // a contact is stamped with the scenario time it was made.
            _fog = gameObject.AddComponent<FogOfWarSystem>();
            _fog.Init(_map.Georeference, _clock);

            _recon = gameObject.AddComponent<ReconOrderSystem>();
            _recon.Init(_combat, _fog, _map.Georeference);

            // Sky, fog and precipitation. Ambience is battle-mode only, so the
            // system needs to know when a battle starts.
            _weather = gameObject.AddComponent<WeatherSystem>();
            _weather.Init(_map.Sun, _rig.Cam, _clock);
            _combat.RunningChanged += _weather.SetBattleRunning;

            // Hand-placed fire / explosion / smoke.
            _effects = gameObject.AddComponent<EffectPlacementTool>();
            _effects.Init(_map, _rig.Cam);

            // Called fire missions — see docs/17-ARTILLERY.md.
            _artillery = gameObject.AddComponent<ArtilleryStrikeSystem>();
            _artillery.Init(_map, _rig.Cam);

            // Tasked air strikes — see docs/18-AIR-STRIKES.md.
            _airStrike = gameObject.AddComponent<AirStrikeSystem>();
            _airStrike.Init(_map, _rig.Cam);

            // Air supply drops — the one called mission that leaves something
            // standing on the ground. See docs/29-AIR-SUPPLY.md.
            _airSupply = gameObject.AddComponent<AirSupplySystem>();
            _airSupply.Init(_map, _rig.Cam);

            // Tasked UAV strikes — see docs/19-UAV-STRIKES.md.
            _uavStrike = gameObject.AddComponent<UavStrikeSystem>();
            _uavStrike.Init(_map, _rig.Cam);

            // Ground-based air defence. It answers the UAV sorties above, so it
            // has to exist before one can be flown — see docs/24-AIR-DEFENCE.md.
            _airDefence = gameObject.AddComponent<AirDefenceSystem>();
            _airDefence.Init(_map.Georeference);

            // Missile systems — see docs/20-MISSILE-SYSTEMS.md.
            _missiles = gameObject.AddComponent<MissileStrikeSystem>();
            _missiles.Init(_map, _rig.Cam);

            // Naval gunfire support — see docs/21-NAVAL-GUNFIRE.md.
            _naval = gameObject.AddComponent<NavalStrikeSystem>();
            _naval.Init(_map, _rig.Cam);

            // The scenario's rear area: depots and supply, fuel, ammunition,
            // repair and medical points — see docs/26-LOGISTICS.md.
            _logistics = gameObject.AddComponent<IronMeridian.Logistics.LogisticsSystem>();
            _logistics.Init(_map, _rig.Cam);

            // Where a landed bundle registers itself. Assigned here rather than
            // where the supply system is built, because that is above this line:
            // a drop is a logistics event, and the logistics system has to exist
            // before anything can hand it one.
            _airSupply.Logistics = _logistics;

            // Formations that arrive after the battle starts — docs/30.
            _reinforcements = gameObject.AddComponent<ReinforcementSystem>();

            // Mines and obstacles: the barrier plan, as control measures on the
            // ground — see docs/31-OBSTACLES.md.
            _obstacles = gameObject.AddComponent<IronMeridian.Lines.ObstacleSystem>();
            _obstacles.Init(_map, _rig.Cam);

            // What the force fights on: stocks typed by the designer, burn rates
            // derived from the order of battle — see docs/27-SUSTAINMENT.md.
            _sustainment = gameObject.AddComponent<IronMeridian.Logistics.SustainmentSystem>();

            EditHistory.Clear();

            _selection = gameObject.AddComponent<SelectionManager>();
            _selection.InputBlocked = () => Loading || DateTimeDialog.IsOpen ||
                                            ConfirmDialog.IsOpen ||
                                            LossesDialog.IsOpen ||
                                            ContextMenuUI.IsOpen ||
                                            _effects.IsArmed ||
                                            _artillery.IsArmed ||
                                            _airStrike.IsArmed ||
                                            _airSupply.IsArmed ||
                                            _uavStrike.IsArmed ||
                                            _missiles.IsArmed ||
                                            _naval.IsArmed ||
                                            _logistics.IsArmed ||
                                            _obstacles.IsArmed ||
                                            _frontline.Drawing ||
                                            _areaTool.Drawing;
            _selection.BattleRunning = () => _combat.Running;
            // Right-click on a formation or a logistic site opens its own menu
            // instead of ordering a move — see OpenMapContextMenu.
            _selection.ContextMenuRequested = OpenMapContextMenu;

            // --- UI ---
            var canvas = UIFactory.CreateCanvas("GameCanvas");
            _canvas = canvas;
            _selection.Init(_map, _rig.Cam, canvas);

            BuildStep("map controls", () =>
            {
                _mapControls = gameObject.AddComponent<MapControlsUI>();
                _mapControls.Build(canvas, _map, _rig);
            });

            _hud = gameObject.AddComponent<GameHUD>();
            _hud.Build(canvas, _combat, _clock);
            // The bar says which of its two jobs this scene is doing, and its
            // home button leaves the way the player came in.
            if (_mission != null)
            {
                _hud.SetTitle(_mission.name.ToUpperInvariant());
                _hud.HomeScene = GameConfig.SceneSinglePlayer;
            }

            // Identifying a counter should not cost a click. Built after the HUD
            // so it draws over it, and shown in both modes — the information is
            // as useful when laying a scenario out as when fighting it.
            // The world camera is what puts the card beside the counter rather
            // than beside the cursor — see UnitHoverTooltip.
            _hoverTooltip = UnitHoverTooltip.Create(canvas, _rig.Cam);
            _selection.HoverChanged = u => _hoverTooltip.Show(u);
            _selection.Flash = _hud.Flash;
            _effects.Flash = _hud.Flash;
            _artillery.Flash = _hud.Flash;
            _airStrike.Flash = _hud.Flash;
            _airSupply.Flash = _hud.Flash;
            // Dropped supplies belong to whichever side the palette is working
            // for, the same rule the LOGISTICS panel's own placements follow.
            _airSupply.Team = Data.Team.User;
            _uavStrike.Flash = _hud.Flash;
            _missiles.Flash = _hud.Flash;
            _naval.Flash = _hud.Flash;
            _airDefence.Flash = _hud.Flash;

            // Both strike systems report their countdown every frame, and there
            // is one banner. Left to themselves they would fight over it — the
            // idle one would blank it a frame after the busy one filled it — so
            // each writes to its own slot and the banner shows whichever is
            // nearest to landing.
            _artillery.CountdownChanged = (title, remaining, total, colour) =>
            {
                _artilleryBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };
            _airStrike.CountdownChanged = (title, remaining, total, colour) =>
            {
                _airStrikeBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };
            _airSupply.CountdownChanged = (title, remaining, total, colour) =>
            {
                _airSupplyBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };
            _uavStrike.CountdownChanged = (title, remaining, total, colour) =>
            {
                _uavStrikeBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };
            _missiles.CountdownChanged = (title, remaining, total, colour) =>
            {
                _missileBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };
            _naval.CountdownChanged = (title, remaining, total, colour) =>
            {
                _navalBanner = (title, remaining, total, colour);
                RefreshStrikeBanner();
            };

            _defence.Flash = _hud.Flash;
            _taskAreas.Flash = _hud.Flash;
            _manoeuvre.Flash = _hud.Flash;
            _planner.Flash = _hud.Flash;
            _attacks.Flash = _hud.Flash;
            _recon.Flash = _hud.Flash;
            _hud.ResetRequested = ConfirmReset;
            // A formation the fog has just taken off the map cannot stay
            // selected — the info panel would keep reporting it.
            _fog.UnitHidden = u => { if (_selection.Selected == u) _selection.Select(null); };
            _map.LoadError += _hud.Flash;

            // A tileset request that actually fails is the other half of the
            // connectivity story — see ConnectivityWatcher for why neither
            // signal is sufficient alone.
            _map.LoadError += _ => _hud.ShowAlert(
                "Map data failed to load — check your connection.", 5f);

            // Losing the network does not stop the game, it stops the *map*
            // filling in, which without a message looks like a hang.
            _connectivity = gameObject.AddComponent<ConnectivityWatcher>();
            _connectivity.ReachabilityChanged = reachable => _hud.ShowAlert(
                reachable
                    ? "Connection restored — map tiles will resume loading."
                    : "No internet connection — new map tiles and imagery will not load.",
                5f, warning: !reachable);
            // A tileset failure means the terrain will never finish: drop the
            // overlay at once so the player sees the HUD's error rather than a
            // bar that sits there until the timeout.
            _map.LoadError += _ => { if (_loading != null) _loading.Dismiss("Map failed to load."); };

            // Each panel is built in isolation: the whole UI is constructed at
            // runtime, so an exception in one builder used to abort the rest of
            // Start() — silently leaving the info panel, range rings, selection
            // Settings for the automatic front line. Reached by clicking the
            // line itself — see FrontlinePanelUI for why it is not a rail
            // section like everything else.
            BuildStep("front line options", () =>
            {
                _frontlinePanel = FrontlinePanelUI.Create(canvas, _frontline);
                _frontline.Flash = _hud.Flash;

                // FLOT_BREACH: an enemy force with real combat power is
                // established behind the line. The alert is the event's first
                // consumer; reserves, counterattacks and victory conditions
                // are the others when they exist.
                _frontline.Breach += (victim, lat, lon, depth) =>
                {
                    bool ours = victim == Team.User;
                    _hud.ShowAlert(ours
                        ? $"FLOT BREACHED — enemy force {depth:0.#} km behind the line."
                        : $"Breakthrough — friendly force {depth:0.#} km behind the enemy FLOT.",
                        6f, warning: ours);
                };
                _frontlinePanel.Opened = () =>
                {
                    if (_strikeDock != null) _strikeDock.Hide();
                };
                _selection.LineClicked = line =>
                {
                    if (line == null || !FrontlineSystem.IsFlotLine(line.Data.id)) return;
                    _frontlinePanel.Show();
                    _hud.Flash("Front line — derived from where the formations stand.");
                };
            });

            // The five fire menus, as an icon cluster at the top right. Built
            // before the palette: the palette lays its four strike sections
            // into the dock's pages, so the dock has to exist first.
            BuildStep("strike dock", () =>
            {
                _strikeDock = gameObject.AddComponent<StrikeDockUI>();
                _strikeDock.Build(canvas);

                // Two panels cannot share the right-hand edge, so opening a fire
                // menu drops the selection — which is honest besides: you are
                // choosing a weapon now, not inspecting a formation.
                _strikeDock.Opened = () =>
                {
                    _selection.Select(null);
                    if (_frontlinePanel != null) _frontlinePanel.Hide();
                };

                // Closing a menu stands its own system down; leaving one armed
                // behind a panel that is off screen would turn the next click on
                // the map into a strike nobody asked for.
                _strikeDock.Closed = menu =>
                {
                    switch (menu)
                    {
                        case StrikeDockUI.Menu.Artillery: _artillery.Cancel(); break;
                        case StrikeDockUI.Menu.AirStrike: _airStrike.Cancel(); break;
                        case StrikeDockUI.Menu.AirSupply: _airSupply.Cancel(); break;
                        case StrikeDockUI.Menu.UavStrike: _uavStrike.Cancel(); break;
                        case StrikeDockUI.Menu.Missiles: _missiles.Cancel(); break;
                        case StrikeDockUI.Menu.NavalStrike: _naval.Cancel(); break;
                    }
                };

                _strikeDock.RightInsetChanged = _ => RefreshRightInset();

                // Fire menus are a battle control, not an authoring one — see
                // StrikeDockUI. Leaving the battle takes them off the screen and
                // stands any armed weapon down with them.
                _combat.RunningChanged += running => _strikeDock.SetBattleMode(running);
                _strikeDock.SetBattleMode(_combat.Running);

                // And stood down here as well, on the way out of battle mode.
                // The dock disarms whatever menu it was showing; this covers
                // every system whether or not its menu was the open one, so
                // "no fire missions in scenario mode" holds however a launcher
                // came to be armed.
                _combat.RunningChanged += running =>
                {
                    if (running) return;
                    _artillery.Cancel();
                    _airStrike.Cancel();
                    _airSupply.Cancel();
                    _uavStrike.Cancel();
                    _missiles.Cancel();
                    _naval.Cancel();
                };
            });

            BuildStep("missile systems", () =>
            {
                // Not held: the board fills a page the dock owns and is driven
                // entirely by the missile system's own events from then on.
                MissilePanelUI.Create(
                    _strikeDock.PageFor(StrikeDockUI.Menu.Missiles), _missiles);
            });

            BuildStep("unit palette", () =>
            {
                _palette = gameObject.AddComponent<UnitPaletteUI>();
                _palette.Build(canvas, _map, _rig.Cam, _rig, _clock, _weather, _effects,
                    _artillery, _airStrike, _airSupply, _uavStrike, _naval, _mapControls, _strikeDock,
                    _logistics, _sustainment, _reinforcements, _obstacles);
                _palette.DropRequested = OnPaletteDrop;
                _palette.DropRejected = _hud.Flash;
                _palette.GenerateSectorsRequested = GenerateSectors;
                _palette.ClearSectorsRequested = () =>
                {
                    _sectors.ClearAll();
                    _hud.Flash("Tactical graphics cleared.");
                };
                _palette.AutoSectorsChanged = on =>
                {
                    _sectors.AutoUpdate = on;
                    if (on) GenerateSectors();
                };
                _palette.LineOfSightChanged = SetLineOfSightVisible;
                _palette.WeaponRangeChanged = SetWeaponRangeVisible;
                _palette.FogOfWarChanged = on =>
                {
                    _fog.SetEnabled(on);
                    _hud.Flash(on
                        ? "Fog of war armed — enemy formations show only where you can see them, in battle."
                        : "Fog of war off — the whole order of battle is visible.");
                };

                // Bottom tool strip. The cursor is the only latching tool left —
                // the pencil and the square drew control measures by hand, and
                // that feature is gone.
                _palette.SelectToolRequested = () =>
                {
                    _areaTool.CancelDrawing();
                    _effects.Cancel();
                };

                // COMMANDERS section — the order of battle above the units.
                _palette.CommanderAssignRequested = AssignSelectionToCommander;
                _palette.CommanderSelectUnitsRequested = SelectCommandersUnits;

                // MISSIONS section — the single-player campaign, edited here.
                _palette.MissionOpenRequested = OpenMission;
                _palette.MissionSaveRequested = SaveMission;
                _palette.MissionCreateRequested = CreateMissionHere;
                _palette.MissionDeleteRequested = DeleteMission;

                // The mission's boundary. The tool owns the drawing and the
                // overlay; the controller only decides which mission it is
                // pointed at and what happens when the area changes.
                _palette.MissionAreaDrawRequested = () =>
                {
                    if (!PointAreaToolAtPanelMission()) return;
                    _areaTool.StartDrawing();
                };
                _palette.MissionAreaRectangleRequested = MakeMissionRectangle;
                _palette.MissionAreaClearRequested = () =>
                {
                    if (!PointAreaToolAtPanelMission()) return;
                    _areaTool.ClearArea();
                };

                _areaTool.Flash = _hud.Flash;
                _areaTool.DrawingChanged = _palette.SetMissionAreaDrawing;
                _areaTool.AreaChanged = _ =>
                {
                    _palette.RefreshMissionArea();
                    ApplyMissionArea();
                };

                if (_mission != null)
                {
                    _palette.ShowMission(_mission);
                    _areaTool.Show(_mission.area);
                    ApplyMissionArea();
                    RefreshHqZones();
                    RefreshDeploymentZones();
                }

                // LOGISTICS section — the scenario's rear area.
                _logistics.Flash = _hud.Flash;
                _logistics.Changed += _palette.RefreshLogistics;
                _palette.LogisticsClearRequested = () =>
                {
                    int n = _logistics.Sites.Count;
                    _logistics.Clear();
                    _hud.Flash(n == 0
                        ? "There are no logistic sites on the map."
                        : $"{n} logistic site(s) removed.");
                };
                _palette.LogisticsRemoveRequested = site =>
                {
                    if (site == null) return;
                    string name = LogisticsCatalog.Get(site.Kind).name;
                    _logistics.Remove(site);
                    _hud.Flash($"{name} removed.");
                };
                _palette.LogisticsFocusRequested = site =>
                {
                    if (site == null) return;
                    var focus = GeoUtils.GeoToUnity(_map.Georeference,
                        site.Data.latitude, site.Data.longitude, 300);
                    _rig.FlyTo(focus, Mathf.Min(_rig.Distance, UnitFocusDistanceMeters));
                };

                // SUSTAINMENT section — the stocks behind the force.
                _sustainment.Changed += _palette.RefreshSustainment;
                _palette.StockFromForceRequested = (team, days) =>
                {
                    _sustainment.StockFromForce(team, days);
                    _hud.Flash($"{(team == Team.Enemy ? "Enemy" : "Friendly")} stocks filled with " +
                               $"{days:0} day(s) of its current consumption.");
                };

                // MINES AND OBSTACLES section — the barrier plan.
                _obstacles.Flash = _hud.Flash;
                _obstacles.Changed += _palette.RefreshObstacles;
                _palette.ObstaclesClearRequested = () =>
                {
                    int n = _obstacles.Markers.Count;
                    _obstacles.Clear();
                    _hud.Flash(n == 0
                        ? "There are no obstacle graphics on the map."
                        : $"{n} mine and obstacle graphic(s) removed.");
                };
                _palette.ObstacleRemoveRequested = marker =>
                {
                    if (marker == null) return;
                    string name = ObstacleCatalog.Get(marker.Kind).name;
                    _obstacles.Remove(marker);
                    _hud.Flash($"{name} removed.");
                };
                _palette.ObstacleFocusRequested = marker =>
                {
                    if (marker == null) return;
                    var focus = GeoUtils.GeoToUnity(_map.Georeference,
                        marker.Data.latitude, marker.Data.longitude, 300);
                    _rig.FlyTo(focus, Mathf.Min(_rig.Distance, UnitFocusDistanceMeters));
                };

                // GROUPS section — battle-mode only, see UnitPaletteUI.
                _palette.GroupSelectRequested = id => _selection.SetSelection(GroupMembers(id));
                _palette.GroupFlyRequested = id => FlyToGroup(GroupMembers(id));
                _palette.GroupFlotRequested = ManTheFlot;
                _palette.GroupFlotClearRequested = () =>
                {
                    if (string.IsNullOrEmpty(_frontline.HoldingGroupId))
                    {
                        _hud.Flash("No group is holding the front line.");
                        return;
                    }
                    string name = _frontline.HoldingGroupName;
                    _frontline.SetHoldingGroup("", "");
                    _palette.SetFlotHolder("");
                    _hud.Flash($"{name} released from the front line. Its formations keep their positions.");
                };

                // MISSIONS → DEPLOYMENT ZONES.
                _palette.MissionDeploymentSetRequested = SetMissionDeployment;
                _palette.MissionDeploymentClearRequested = ClearMissionDeployment;
                _palette.MissionDeploymentRadiusRequested = SetMissionDeploymentRadius;

                // REINFORCEMENTS — the schedule and where it arrives.
                _reinforcements.Init(_clock, _combat);
                _reinforcements.Flash = _hud.Flash;
                _reinforcements.Changed += _palette.RefreshReinforcements;
                _reinforcements.Spawn = (def, team, echelon, lat, lon) =>
                    OnPaletteDrop(def, team,
                        team == Team.User ? Affiliation.Friendly : Affiliation.Hostile,
                        echelon, lat, lon);
                _reinforcements.ZoneFor = DeploymentZoneFor;

                // MISSIONS → HQ ZONES.
                _palette.MissionHqSetRequested = SetMissionHq;
                _palette.MissionHqClearRequested = ClearMissionHq;
                _palette.MissionHqRadiusRequested = SetMissionHqRadius;

                // AVAILABLE list: a click on a catalogue card opens what that
                // type is, on the right-hand edge the other panels share.
                _palette.InspectTypeRequested = (def, team) =>
                {
                    if (_typePanel == null) return;
                    _selection.Select(null);          // the two panels share one strip
                    _typePanel.Show(def, team);
                };

                // DEPLOYED list.
                _palette.SelectUnitRequested = u => _selection.Select(u);
                _palette.FocusUnitRequested = FlyToUnit;
                _palette.RemoveUnitRequested = RemoveUnitFromMap;

                // The minimap shares the left edge with the rail now, so it
                // rides the section panel's slide exactly as the zoom cluster
                // does.
                _palette.LeftInsetChanged = edge =>
                {
                    if (_minimap != null) _minimap.SetLeftInset(edge);
                };
                if (_minimap != null) _minimap.SetLeftInset(_palette.LeftChromeEdge);

                // The rail's battle-only chrome — the GROUPS row.
                _combat.RunningChanged += running => _palette.SetBattleMode(running);
                _palette.SetBattleMode(_combat.Running);
            });

            // The battle minimap, docked under the fire-menu cluster. Built
            // after the strike dock so the two agree on where the block of
            // top-right chrome ends.
            BuildStep("minimap", () =>
            {
                _minimap = gameObject.AddComponent<MiniMapUI>();
                _minimap.Build(canvas, _map, _rig, _frontline,
                    _save.centerLatitude, _save.centerLongitude);
                _minimap.AreaSource = () => _mission?.area;
                _minimap.FlyRequested = (lat, lon) =>
                {
                    var focus = GeoUtils.GeoToUnity(_map.Georeference, lat, lon, 300);
                    _rig.FlyTo(focus);
                };

                // Same rule as the fire menus above it: an operational picture
                // is something a battle has. See MiniMapUI.
                _combat.RunningChanged += running => _minimap.SetVisible(running);
                _minimap.SetVisible(_combat.Running);
            });

            BuildStep("map objects", () =>
            {
                _mapObjects = gameObject.AddComponent<MapObjectSystem>();
                _mapObjects.Init(_map, _rig.Cam);
                _mapObjects.Flash = _hud.Flash;
                _mapObjects.Changed += _palette.RefreshMapObjects;
                _palette.BindMapObjects(_mapObjects);
                _palette.MapObjectFocusRequested = obj =>
                {
                    if (obj == null || obj.points == null || obj.points.Count == 0) return;
                    // The first corner, not a centroid: a bridge's polygon can be
                    // long and thin, and its centre is as likely to be water.
                    var p0 = obj.points[0];
                    FlyTo(p0.latitude, p0.longitude, 4000f);
                };
            });

            BuildStep("unit type panel", () =>
            {
                _typePanel = gameObject.AddComponent<UnitTypePanel>();
                _typePanel.Build(canvas);
            });

            BuildStep("unit info panel", () =>
            {
                _infoPanel = gameObject.AddComponent<UnitInfoPanel>();
                _infoPanel.Build(canvas);
                _infoPanel.RemoveRequested = RemoveUnitFromMap;
                _infoPanel.CycleRequested = CycleSelection;
            });

            // Battle-mode only, and only once the camera is far enough out that
            // individual counters have stopped being readable — see
            // UnitClusterLayer for why the editor is deliberately excluded.
            BuildStep("unit clusters", () =>
            {
                _clusters = gameObject.AddComponent<UnitClusterLayer>();
                _clusters.Build(canvas, _rig.Cam, _rig, _combat);
                _clusters.SelectRequested = members =>
                {
                    _selection.SetSelection(members);
                    _hud.Flash(members.Count == 1
                        ? "1 formation selected from the cluster."
                        : $"{members.Count} formations selected from the cluster.");
                };
            });

            BuildStep("group panel", () =>
            {
                _groupPanel = gameObject.AddComponent<GroupPanelUI>();
                _groupPanel.Build(canvas);
                _groupPanel.Flash = _hud.Flash;
                _groupPanel.SelectGroupRequested = members => _selection.SetSelection(members);
                _groupPanel.FlyToGroupRequested = FlyToGroup;
                // The order bar is captioned with the group's name and lives at
                // the foot of the map, so a rename here has to reach it.
                _groupPanel.GroupsChanged = RefreshActionBar;
                _groupPanel.RemoveUnitRequested = u =>
                {
                    RecordRemoval(u);
                    // Drop it from the selection first so the panel rebuilds
                    // from a list that no longer contains the dead unit.
                    var remaining = new System.Collections.Generic.List<UnitActor>();
                    foreach (var s in _selection.Selection)
                        if (s != null && s != u) remaining.Add(s);
                    _selection.SetSelection(remaining);
                    u.RemoveFromMap();
                };
            });

            BuildStep("range rings", () =>
            {
                // How far this formation can see. Captioned in metres on the ring
                // itself: a line of sight is a distance you are judging against
                // the ground, and "4 500 m" is the number that is being judged —
                // kilometres to one decimal reads as an approximation.
                _losRing = RangeRing.Create(_map.Georeference, _map.Georeference.transform,
                    GameConfig.ViewRangeColor, "LINE OF SIGHT");
                _weaponRing = RangeRing.Create(_map.Georeference, _map.Georeference.transform,
                    GameConfig.WeaponRangeColor, "Max weapon range");
            });

            BuildStep("unit action bar", () =>
            {
                _actionBar = gameObject.AddComponent<UnitActionBarUI>();
                _actionBar.Build(canvas);
                _actionBar.Flash = _hud.Flash;
                // Every task on the bar is the same shape: pick it, then click
                // the ground. One arming mechanism carries all of them —
                // SelectionManager.ArmGroundPick — and each order just supplies
                // what to do with the point.
                _actionBar.MoveRequested = task => _selection.ArmGroundPick(
                    (lat, lon) => OrderMove(task, lat, lon),
                    "Move order cancelled.");

                _actionBar.DefenceRequested = task => _selection.ArmGroundPick(
                    (lat, lon) => OrderDefence(task, lat, lon),
                    "Defensive task cancelled.");

                _actionBar.PlanRequested = kind => _selection.ArmGroundPick(
                    (lat, lon) => _planner.Draw(_selection.Selected, kind, lat, lon),
                    "Plan cancelled.");

                _selection.GroundPickResolved = () => _actionBar.ClearArmed();

                // The standing commands act at once — no ground to pick.
                _actionBar.StopRequested = () => ForSelection(u => _manoeuvre.Stop(u));
                _actionBar.ToggleFreeMovementRequested = () =>
                {
                    var lead = _selection.Selected;
                    if (lead == null) return;
                    // Flipped from the lead formation's state so a mixed
                    // selection ends up all one way rather than inverted
                    // unit by unit into whatever it already was.
                    bool on = !lead.State.freeMovement;
                    ForSelection(u => _manoeuvre.SetFreeMovement(u, on));
                };
                _actionBar.ToggleAutomaticAttackRequested = () =>
                {
                    var lead = _selection.Selected;
                    if (lead == null) return;
                    bool on = !lead.State.automaticAttack;
                    ForSelection(u => _manoeuvre.SetAutomaticAttack(u, on));
                };

                // Attack: the bar picks the task, and the next map click is
                // either an enemy formation or the ground to attack.
                _actionBar.AttackRequested = task => _selection.ArmAttackOrder(task);
                // A named target is the one thing a group does *not* spread
                // across a frontage: everything selected attacks the formation
                // that was clicked.
                _selection.AttackTargetPicked = (target, task) =>
                    ForSelection(u => _attacks.Order(u, target, task));
                _selection.AttackGroundPicked = (lat, lon, task) =>
                    OrderAreaAttack(task, lat, lon);
                _selection.AttackOrderResolved = () => _actionBar.ClearArmed();

                // Recon: same shape, but the map click is a point on the ground
                // rather than an enemy formation.
                _actionBar.ReconRequested = task => _selection.ArmReconOrder(task);
                _selection.ReconPointPicked = (lat, lon, task) => OrderRecon(task, lat, lon);
                _selection.ReconOrderResolved = () => _actionBar.ClearArmed();
                // The order bar belongs to game mode; leaving battle puts the
                // editor back in charge.
                _combat.RunningChanged += _ => RefreshActionBar();
            });

            _selection.SelectionChanged = sel =>
            {
                _selectionPanelOpen = sel.Count >= 1 && sel[0] != null;
                bool infoPanelOpen = sel.Count == 1 && sel[0] != null;

                // Everything that docks on the right shares one strip of screen:
                // the unit inspector, the group panel, the front line options
                // and the fire menus. Selecting a formation is a clear statement
                // about which of them you now want.
                if (_selectionPanelOpen)
                {
                    if (_frontlinePanel != null) _frontlinePanel.Hide();
                    if (_strikeDock != null) _strikeDock.Hide();
                    if (_typePanel != null) _typePanel.Hide();
                }

                if (_infoPanel != null) _infoPanel.Show(infoPanelOpen ? sel[0] : null);
                if (_groupPanel != null) _groupPanel.SetSelection(sel);
                if (_minimap != null) _minimap.SetSelection(sel);
                // The whole map can be carrying orders at once; this is what
                // makes the selected formation's own area stand out of them.
                if (_taskAreas != null) _taskAreas.SetSelection(sel);

                RefreshRightInset();
                UpdateRangeRings(sel);
                RefreshActionBar();
            };

            BuildStep("pause menu", () =>
            {
                _pauseMenu = gameObject.AddComponent<PauseMenuUI>();
                _pauseMenu.Build(canvas);
                _pauseMenu.BlockOpen = () => _areaTool.Drawing || _selection.Selected != null;
                _pauseMenu.SaveRequested = SaveMap;
                _pauseMenu.LoadRequested = LoadMap;
                // EXIT goes back where the player came in from. Dropping a
                // mission player at the main menu would make them walk the
                // campaign browser again to retry the mission they just left.
                if (_mission != null) _pauseMenu.ExitScene = GameConfig.SceneSinglePlayer;
                _pauseMenu.ResumeTimeScale = () => _clock.DesiredTimeScale;
                _rig.InputBlocked = () => Loading || DateTimeDialog.IsOpen ||
                                          ConfirmDialog.IsOpen ||
                                          LossesDialog.IsOpen ||
                                          ContextMenuUI.IsOpen ||
                                          _pauseMenu.IsOpen;
            });

            // --- content ---
            BuildStep("map content", () =>
            {
                ApplySave(_save);
                _map.ViewModeChanged += mode =>
                {
                    _rig.SetMode(mode);
                    // Control measures are drawn either clamped to terrain or on
                    // a flat band. Re-clamping on the switch is what keeps the
                    // two projections showing the same graphics rather than one
                    // of them hiding lines inside the ground.
                    _lines.SetAll3D(mode == ViewMode.Mode3D);
                };
                _map.SetViewMode(_save.viewMode == "Mode2D" ? ViewMode.Mode2D : ViewMode.Mode3D);
                _map.SetMapStyle(System.Enum.TryParse(_save.mapStyle, out MapStyle style) ? style : MapStyle.Satellite);
                _map.SetBuildingsVisible(_save.showBuildings);

                // A mission is a fight rather than a layout exercise, so it gets
                // to arm the fog for the player. The editor never does.
                if (_mission != null)
                {
                    _fog.SetEnabled(_mission.fogOfWar);
                    if (_palette != null)
                        _palette.SyncGeneralToggles(false, _mission.fogOfWar, _showLineOfSight, _showWeaponRange);
                }

                // The camera is only bounded while a battle is running — the
                // editor has to be able to fly outside an area to draw it.
                _combat.RunningChanged += _ => ApplyMissionArea();
                ApplyMissionArea();
            });

            BuildStep("mission mode", ApplyMissionMode);
            // Last: it reads the minimap's dock position, which mission mode
            // has just had its say on.
            BuildStep("right dock layout", RefreshRightDockTop);

            // Everything is built; what remains is Cesium streaming tiles for
            // the opening view. Hand the overlay that as its progress source.
            if (_loading != null)
            {
                _loading.SetStatus($"Streaming terrain — {_save.mapName}");
                _loading.Track(
                    () => _map.TerrainLoadProgress01,
                    () => _map.TerrainLoadProgress01 >= 0.999f);
            }
        }

        /// <summary>
        /// The order bar belongs to game mode — in editor mode right-click
        /// repositions instead — and now to a group as much as to a single
        /// formation.
        ///
        /// **Why a group gets the same bar.** The group panel used to carry
        /// three buttons of its own, in a different place, with a different
        /// vocabulary, that did nothing. A group is not a different kind of
        /// thing from a formation as far as orders go: it moves, attacks,
        /// reconnoitres and defends, and it does so with the same six verbs. So
        /// it uses the same bar in the same place, captioned with whose orders
        /// they are — see <see cref="UnitActionBarUI.Show"/>.
        /// </summary>
        void RefreshActionBar()
        {
            if (_actionBar == null) return;
            if (!_combat.Running) { _actionBar.Hide(); return; }

            var sel = _selection.Selection;
            UnitActor lead = null;
            int alive = 0;
            foreach (var u in sel)
            {
                if (u == null || !u.IsAlive) continue;
                if (lead == null) lead = u;
                alive++;
            }

            if (lead == null) { _actionBar.Hide(); return; }
            _actionBar.Show(lead, alive == 1 ? null : $"GROUP ORDERS — {SelectionScopeName(sel, alive)}");
        }

        /// <summary>
        /// What the order bar calls a multi-unit selection: the group's name
        /// when they all share one, and a count when they do not. Naming a
        /// group that only half the selection belongs to would be the bar
        /// lying about what it is going to act on.
        /// </summary>
        static string SelectionScopeName(
            System.Collections.Generic.IReadOnlyList<UnitActor> selection, int alive)
        {
            string id = null, name = null;
            bool first = true;

            foreach (var u in selection)
            {
                if (u == null || !u.IsAlive) continue;
                if (string.IsNullOrEmpty(u.State.groupId)) { first = false; id = null; break; }
                if (first) { id = u.State.groupId; name = u.State.groupName; first = false; }
                else if (u.State.groupId != id) { id = null; break; }
            }

            return string.IsNullOrEmpty(id)
                ? $"{alive} FORMATIONS"
                : $"{(string.IsNullOrEmpty(name) ? "UNNAMED GROUP" : name)}  ·  {alive}";
        }

        /// <summary>Runs one setup step, reporting failure instead of aborting the remaining wiring.</summary>
        void BuildStep(string what, System.Action step)
        {
            try { step(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameController] Failed to build {what} — the rest of the scene will still load.\n{e}");
            }
        }

        // ------------------------------------------------------- spawning
        void OnPaletteDrop(UnitDefinition def, Team team, Affiliation aff,
            Echelon echelon, double lat, double lon)
        {
            var state = new UnitState
            {
                instanceId = System.Guid.NewGuid().ToString("N").Substring(0, 10),
                defId = def.id,
                team = team.ToString(),
                affiliation = aff.ToString(),
                echelon = echelon.ToString(),
                customName = "",
                groupId = "",
                groupName = "",
                latitude = lat,
                longitude = lon,
                strength = 1f,
                organisation = def.organisation,
                morale = def.morale,
                status = UnitStatus.Idle.ToString(),
                ammo = def.ammoStock,
                fuel = def.fuelStock,
                foodDays = def.foodDays
            };
            var actor = UnitActor.Spawn(_map.Georeference, state);
            RecordSpawn($"deploy {def.name}", actor);
            DeployEffect.Play(_map.Georeference, lat, lon,
                team == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);
            _hud.Flash($"Deployed {echelon} {def.name} ({team})");
        }

        /// <summary>
        /// Steps the selection to the previous/next living unit on the same
        /// side — the info panel's ◄ ► footer. Wraps, so holding either arrow
        /// walks the whole order of battle.
        /// </summary>
        void CycleSelection(int step)
        {
            var current = _selection.Selected;
            if (current == null) return;

            var side = new System.Collections.Generic.List<UnitActor>();
            foreach (var u in UnitRegistry.OfTeam(current.State.TeamEnum))
                if (u != null && u.IsAlive) side.Add(u);

            int index = side.IndexOf(current);
            if (side.Count == 0 || index < 0) return;

            int next = ((index + step) % side.Count + side.Count) % side.Count;
            _selection.Select(side[next]);
        }

        void GenerateSectors()
        {
            int blue = _sectors.Generate(Team.User);
            int red = _sectors.Generate(Team.Enemy);
            if (blue + red == 0)
            {
                _hud.Flash("Need units on the map (and an opposing force) to derive sectors.");
                return;
            }
            _hud.Flash($"Sectors generated — {blue} blue / {red} red control measures.");
        }

        // ------------------------------------------------------- clipboard / undo

        void CopySelection()
        {
            _clipboard.Clear();
            foreach (var u in _selection.Selection)
                if (u != null && u.IsAlive) _clipboard.Add(u.State.Clone());

            if (_clipboard.Count == 0) { _hud.Flash("Nothing selected to copy."); return; }

            // Centre of the copied group — paste positions everything relative
            // to this so the formation survives the round trip.
            double lat = 0, lon = 0;
            foreach (var s in _clipboard) { lat += s.latitude; lon += s.longitude; }
            _clipboardCentreLat = lat / _clipboard.Count;
            _clipboardCentreLon = lon / _clipboard.Count;

            _hud.Flash($"Copied {_clipboard.Count} unit(s) — Ctrl+V to paste.");
        }

        void PasteClipboard()
        {
            if (_clipboard.Count == 0) { _hud.Flash("Clipboard is empty."); return; }

            // Paste under the cursor when it is over loaded terrain, otherwise
            // just offset from the originals so the paste is never silently
            // dropped somewhere off-screen.
            double targetLat, targetLon;
            if (_map.RaycastGround(_rig.Cam, Input.mousePosition, out Vector3 world))
                GeoUtils.UnityToGeo(_map.Georeference, world, out targetLat, out targetLon, out _);
            else
            {
                targetLat = _clipboardCentreLat + 0.004;
                targetLon = _clipboardCentreLon + 0.004;
            }

            // Preserve the copied formation: every unit keeps its offset from
            // the group's centre rather than stacking on the cursor.
            var pasted = new System.Collections.Generic.List<UnitActor>();
            foreach (var src in _clipboard)
            {
                var state = src.Clone();
                state.instanceId = System.Guid.NewGuid().ToString("N").Substring(0, 10);
                state.groupId = "";          // a copy is not a member of the original's group
                state.groupName = "";
                state.latitude = targetLat + (src.latitude - _clipboardCentreLat);
                state.longitude = targetLon + (src.longitude - _clipboardCentreLon);

                var actor = UnitActor.Spawn(_map.Georeference, state);
                if (actor != null) pasted.Add(actor);
            }

            if (pasted.Count == 0) { _hud.Flash("Paste failed — unknown unit type."); return; }

            DeployEffect.Play(_map.Georeference, targetLat, targetLon,
                pasted[0].State.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);

            EditHistory.Push($"paste {pasted.Count} unit(s)", () =>
            {
                foreach (var a in pasted) if (a != null) a.RemoveFromMap();
            });

            _selection.SetSelection(pasted);
            _hud.Flash($"Pasted {pasted.Count} unit(s).");
        }

        void UndoLastEdit()
        {
            if (!EditHistory.Undo(out string label))
            {
                _hud.Flash(label == null ? "Nothing to undo." : $"Could not undo {label}.");
                return;
            }
            _selection.Select(null);
            _hud.Flash($"Undid {label}.");
        }

        /// <summary>Records a spawn so Ctrl+Z can take it back off the map.</summary>
        void RecordSpawn(string label, UnitActor actor)
        {
            if (actor == null) return;
            EditHistory.Push(label, () => { if (actor != null) actor.RemoveFromMap(); });
        }

        /// <summary>Records a removal so Ctrl+Z can put the unit back as it was.</summary>
        void RecordRemoval(UnitActor actor)
        {
            if (actor == null) return;

            // A formation's orders go with it. Without this its task area, its
            // plan and its pending contingency would outlive it — graphics on
            // the map belonging to a counter that is no longer there.
            if (_taskAreas != null) _taskAreas.ClearFor(actor);
            if (_planner != null) _planner.ClearFor(actor);
            if (_manoeuvre != null) _manoeuvre.Forget(actor.State.instanceId);

            var snapshot = actor.State.Clone();
            string name = string.IsNullOrEmpty(snapshot.customName)
                ? UnitDatabase.Get(snapshot.defId)?.name ?? snapshot.defId
                : snapshot.customName;
            EditHistory.Push($"remove {name}", () => UnitActor.Spawn(_map.Georeference, snapshot));
        }

        // ------------------------------------------------------- missions

        /// <summary>
        /// Opens a mission in the editor: its map, its settings, and its start
        /// point. Everything currently on the map is replaced — this is the same
        /// operation as loading a scenario, which is what a mission is.
        /// </summary>
        void OpenMission(MissionDefinition mission)
        {
            if (mission == null) return;

            var data = MissionLibrary.LoadMap(mission);
            if (data == null)
            {
                _hud.Flash($"Could not open '{mission.name}'.");
                return;
            }

            _mission = mission;
            MissionLibrary.Select(mission);
            mapFileName = mission.ResolvedMapFile;
            _save = data;

            ClearMapContents();
            ApplySave(_save);

            _map.SetViewMode(_save.viewMode == "Mode2D" ? ViewMode.Mode2D : ViewMode.Mode3D);
            _map.SetMapStyle(System.Enum.TryParse(_save.mapStyle, out MapStyle style) ? style : MapStyle.Satellite);
            _map.SetBuildingsVisible(_save.showBuildings);
            _fog.SetEnabled(mission.fogOfWar);
            if (_palette != null) _palette.SyncGeneralToggles(false, mission.fogOfWar,
                _showLineOfSight, _showWeaponRange);

            if (mission.area == null) mission.area = new Data.MissionArea();
            _areaTool.Show(mission.area);
            ApplyMissionArea();
            if (_palette != null) _palette.RefreshMissionArea();

            // Old missions carry no HQ record at all; JsonUtility leaves the
            // fields null rather than at their initialisers when the JSON has
            // no member for them, so they are filled in here rather than
            // guarded at every reader.
            if (mission.friendlyHq == null) mission.friendlyHq = new Data.MissionZone();
            if (mission.enemyHq == null) mission.enemyHq = new Data.MissionZone();
            if (mission.friendlyDeployment == null) mission.friendlyDeployment = new Data.MissionZone();
            if (mission.enemyDeployment == null) mission.enemyDeployment = new Data.MissionZone();
            RefreshHqZones();
            RefreshDeploymentZones();

            FlyTo(mission.latitude, mission.longitude, (float)mission.startAltitudeMeters);

            _hud.SetTitle(mission.name.ToUpperInvariant());
            _hud.Flash($"Opened mission '{mission.name}' — {mission.location}");
            if (_palette != null) _palette.SetMissionStatus($"Opened {mission.id}.");
        }

        /// <summary>
        /// Writes the mission: the record **and** the map under it.
        ///
        /// Both, always. A mission is its record plus its scenario, and saving
        /// only one of them is the failure mode this whole feature exists to
        /// avoid — a designer who moves a battalion, presses save, and finds the
        /// player still fighting the old one.
        /// </summary>
        void SaveMission(MissionDefinition mission)
        {
            if (mission == null) return;

            // The live editor state is the truth for everything the map owns.
            CollectSave();
            // …and for the view and weather settings the mission carries a copy
            // of, so a designer does not have to restate them in the panel.
            MissionLibrary.ReadBackFrom(mission, _save);
            // The mission's own fields then win, so the start point stays the
            // one that was typed rather than wherever the camera drifted to.
            MissionLibrary.ApplyTo(mission, _save);

            string mapPath = SaveSystem.SaveMap(_save, mission.ResolvedMapFile);
            MissionLibrary.SaveBook();

            mapFileName = mission.ResolvedMapFile;
            _mission = mission;
            _hud.SetTitle(mission.name.ToUpperInvariant());

            // The area travels with the record, so it is already written — this
            // is about the editor catching up with the mission it just adopted.
            _areaTool.Show(mission.area);
            ApplyMissionArea();

            bool bounded = mission.area != null && mission.area.HasArea;
            _hud.Flash($"Saved mission '{mission.name}' -> {mapPath}" +
                       (bounded ? "" : "  ·  no mission area set — the battle is unbounded"));
            if (_palette != null)
            {
                _palette.SetMissionStatus($"Saved {mission.id} · {_save.units.Count} unit(s)" +
                    (bounded ? $" · {mission.area.AreaKm2():n0} km² area." : " · unbounded."));
                _palette.RefreshMissionArea();
            }
        }

        /// <summary>
        /// Starts a mission at the point the camera is looking at, in the
        /// campaign the panel has chosen. "Here" rather than at a default,
        /// because the designer has already flown to the ground they want.
        /// </summary>
        void CreateMissionHere(Data.Campaign campaign, string name)
        {
            GeoUtils.UnityToGeo(_map.Georeference, _rig.Focus, out double lat, out double lon, out _);

            var mission = MissionLibrary.Create(campaign, name, lat, lon);
            mission.startAltitudeMeters = Mathf.Clamp(_rig.Distance, 300f, 120000f);
            MissionLibrary.ReadBackFrom(mission, _save);
            MissionLibrary.SaveBook();

            _mission = mission;
            MissionLibrary.Select(mission);
            mapFileName = mission.ResolvedMapFile;

            _hud.SetTitle(mission.name.ToUpperInvariant());
            _hud.Flash($"Created mission '{mission.name}' in {Data.CampaignInfo.DisplayName(campaign)}. " +
                       "Lay it out, set its area, then SAVE MISSION + MAP.");

            _areaTool.Show(mission.area);
            ApplyMissionArea();

            if (_palette != null)
            {
                _palette.ShowMission(mission);
                _palette.SetMissionStatus($"Created {mission.id}. Nothing saved to its map yet.");
            }
            RefreshHqZones();
            RefreshDeploymentZones();
        }

        void DeleteMission(MissionDefinition mission)
        {
            if (mission == null) return;

            ConfirmDialog.Open(_canvas, "DELETE MISSION",
                $"Remove '{mission.name}' from the {Data.CampaignInfo.DisplayName(mission.CampaignEnum)} " +
                "campaign?\n\nIts map file is left on disk, so the scenario itself is not lost — but the " +
                "mission stops appearing in SINGLE PLAYER.",
                "DELETE MISSION", () =>
                {
                    string name = mission.name;
                    if (!MissionLibrary.Delete(mission))
                    {
                        _hud.Flash($"Could not delete '{name}'.");
                        return;
                    }
                    if (_mission == mission)
                    {
                        _mission = null;
                        _hud.SetTitle("MAP EDITOR");
                        _areaTool.Hide();
                        ApplyMissionArea();
                    }
                    _hud.Flash($"Deleted mission '{name}'.");
                    if (_palette != null)
                    {
                        _palette.RefreshMissionList();
                        _palette.SetMissionStatus($"Deleted {name}. Its map file was kept.");
                    }
                });
        }

        // ----------------------------------------------------- commanders

        /// <summary>
        /// Puts every selected formation under one officer.
        ///
        /// The selection is the gesture, deliberately: an order of battle is
        /// built by picking formations off the map and handing them to somebody,
        /// which is a drag-select and a button rather than twenty rows of
        /// dropdowns. Formations from the other side are skipped rather than
        /// refused — a box drag across a front line catches both, and failing the
        /// whole assignment because of that would be the panel being pedantic
        /// about something it can simply do correctly.
        /// </summary>
        void AssignSelectionToCommander(Data.CommanderState commander)
        {
            if (commander == null) return;

            var picked = _selection.Selection;
            if (picked == null || picked.Count == 0)
            {
                _hud.Flash("Select formations on the map first, then press ASSIGN SELECTED.");
                return;
            }

            int assigned = 0, skipped = 0;
            foreach (var u in picked)
            {
                if (u == null || !u.IsAlive) continue;
                if (u.State.TeamEnum != commander.TeamEnum) { skipped++; continue; }
                CommanderRegistry.Assign(u, commander);
                assigned++;
            }

            CommanderRegistry.RaiseChanged();

            string who = $"{Data.RankCatalog.Get(commander.TeamEnum, commander.rank).abbrev} {commander.name}";
            _hud.Flash(assigned == 0
                ? $"Nothing on {who}'s side was selected."
                : $"{who} takes command of {assigned} formation(s)." +
                  (skipped > 0 ? $" {skipped} on the other side were left alone." : ""));
        }

        /// <summary>Selects everything an officer holds, so it can be moved or re-tasked as one.</summary>
        void SelectCommandersUnits(Data.CommanderState commander)
        {
            var units = CommanderRegistry.UnitsOf(commander);
            if (units.Count == 0)
            {
                _hud.Flash("That officer holds no formations.");
                return;
            }

            _selection.SetSelection(units);
            _hud.Flash($"Selected {units.Count} formation(s).");
        }

        // --------------------------------------------------- mission mode

        /// <summary>
        /// True once the mission's battle has been started for us. A mission is
        /// entered to fight it, and there is no START BATTLE control on the
        /// screen to press — see <see cref="ApplyMissionMode"/>.
        /// </summary>
        bool _missionBattleStarted;

        /// <summary>
        /// Turns the Game scene from a map editor into a mission: **the map and
        /// the timer, and nothing else.**
        ///
        /// The scene does both jobs, and almost all of its chrome belongs to the
        /// authoring one. The left rail's thirteen sections deploy units and
        /// draw control measures; RESET reloads the scenario; START BATTLE is
        /// the editor deciding when to fight. None of that is a player's
        /// business in a mission that somebody else laid out, and RESET is
        /// actively dangerous there.
        ///
        /// **The on-map zoom cluster and compass stay.** They are map controls,
        /// not editor tools — the same argument that removes the rail keeps
        /// them. So do the unit info panel and the order bar, which appear only
        /// while something is selected and are the only way to give an order.
        ///
        /// No-op in the editor, so the two jobs never have to agree on anything
        /// beyond this one call.
        /// </summary>
        void ApplyMissionMode()
        {
            if (_mission == null) return;

            if (_hud != null) _hud.SetMissionMode(true);
            if (_palette != null) _palette.SetChromeVisible(false);
            // The fire menus go with it. Belt and braces — a dock left up
            // with nothing to close it would sit over the map for the rest
            // of the mission.
            if (_strikeDock != null) _strikeDock.SetChromeVisible(false);

            // The minimap stays — it is the operational picture, which is
            // gameplay rather than authoring. With the rail gone it slides back
            // to the screen's own left margin.
            if (_minimap != null) _minimap.SetLeftInset(0f);
        }

        /// <summary>
        /// Starts a mission's battle once the terrain has streamed in.
        ///
        /// It has to start by itself: the control that would start it is gone,
        /// and the clock — the one piece of chrome a mission keeps — only reads
        /// out while a battle is running. Deferred until the loading overlay has
        /// gone so the first combat tick does not land on units that are still
        /// being clamped to terrain Cesium has not delivered.
        /// </summary>
        void TickMissionAutoStart()
        {
            if (_mission == null || _missionBattleStarted || Loading) return;
            _missionBattleStarted = true;
            _combat.SetRunning(true);
        }

        // --------------------------------------------------- mission area

        /// <summary>
        /// Checks there is a mission to bound and that it is the one the editor
        /// actually has open, then aims the area tool at it.
        ///
        /// The panel's selection is not always the open mission — a designer can
        /// pick one in the dropdown to correct its briefing without loading its
        /// map. Drawing an area in that state would write a boundary into one
        /// mission while the overlay drew it over another's ground, which is
        /// worse than refusing: an area is drawn *against the terrain*, so it
        /// only means anything on the map it belongs to.
        /// </summary>
        bool PointAreaToolAtPanelMission()
        {
            var picked = _palette != null ? _palette.CurrentMission : _mission;
            if (picked == null)
            {
                // Wording kept general: this guard now stands in front of the
                // HQ zones as well as the area, and a message about "its area"
                // in answer to a click on SET HQ is a message about the wrong
                // thing.
                _hud.Flash("Select or create a mission first — this belongs to a mission record.");
                return false;
            }
            if (_mission != picked)
            {
                _hud.Flash($"Open '{picked.name}' in the editor first — this is drawn on its own ground.");
                return false;
            }

            if (picked.area == null) picked.area = new Data.MissionArea();
            if (_areaTool.Area != picked.area) _areaTool.Show(picked.area);
            return true;
        }

        // ----------------------------------------------------- HQ zones

        /// <summary>
        /// The two headquarters drawn on the map, when the open mission names
        /// them. Range rings rather than a graphic of their own: a HQ zone is a
        /// place and a radius, which is exactly what a range ring states, and
        /// re-using it means the two read as the same kind of statement about
        /// ground as a formation's own reach does. See docs/22-MISSIONS.md.
        /// </summary>
        RangeRing _friendlyHqRing, _enemyHqRing;

        /// <summary>
        /// Arms a map click for one side's headquarters. Refuses unless the
        /// mission the panel is pointed at is the one actually open — an HQ is
        /// drawn *against the terrain*, so it only means anything on the map it
        /// belongs to. Same rule as the mission area.
        /// </summary>
        void SetMissionHq(Team team)
        {
            if (!PointAreaToolAtPanelMission()) return;

            string side = team == Team.User ? "friendly" : "enemy";
            _hud.Flash($"Click the ground for the {side} headquarters (Esc or RMB cancels).");

            // An authoring pick, not an order: it acts on the map rather than on
            // a formation, and it is done in the editor with the clock stopped.
            // Both of the order guards would otherwise swallow it silently —
            // see SelectionManager.ArmGroundPick.
            _selection.ArmGroundPick((lat, lon) =>
            {
                var zone = HqFor(team);
                zone.placed = true;
                zone.latitude = lat;
                zone.longitude = lon;

                RefreshHqZones();
                _hud.Flash($"{char.ToUpperInvariant(side[0]) + side.Substring(1)} HQ set — " +
                           $"{_mission.hqRadiusKm:0.#} km zone. SAVE MISSION + MAP to keep it.");
            }, "HQ placement cancelled.", requireSelection: false, battleOnly: false);
        }

        /// <summary>
        /// One side's HQ record, created if the mission has none.
        /// <c>JsonUtility</c> leaves a missing object member null rather than at
        /// its initialiser, so every mission written before HQ zones existed
        /// comes back with two nulls here.
        /// </summary>
        Data.MissionZone HqFor(Team team)
        {
            if (team == Team.User)
                return _mission.friendlyHq ??= new Data.MissionZone();
            return _mission.enemyHq ??= new Data.MissionZone();
        }

        void ClearMissionHq(Team team)
        {
            if (!PointAreaToolAtPanelMission()) return;

            var zone = HqFor(team);
            if (!zone.placed)
            {
                _hud.Flash("That headquarters has not been placed.");
                return;
            }

            zone.placed = false;
            RefreshHqZones();
            _hud.Flash($"{(team == Team.User ? "Friendly" : "Enemy")} HQ cleared.");
        }

        // ------------------------------------------------ deployment zones

        /// <summary>The two deployment zones drawn on the map, when the mission names them.</summary>
        RangeRing _friendlyDeployRing, _enemyDeployRing;

        /// <summary>
        /// Where a side's reinforcements arrive: the mission's zone, or nothing
        /// — in which case <see cref="ReinforcementSystem"/> falls back to that
        /// side's own rear, which is the honest answer for a map that is not a
        /// mission at all.
        /// </summary>
        (double lat, double lon, float radiusKm)? DeploymentZoneFor(Team team)
        {
            if (_mission == null) return null;
            var zone = team == Team.User ? _mission.friendlyDeployment : _mission.enemyDeployment;
            if (zone == null || !zone.placed) return null;
            return (zone.latitude, zone.longitude, Mathf.Max(0.3f, _mission.deploymentRadiusKm));
        }

        Data.MissionZone DeploymentFor(Team team)
        {
            if (team == Team.User)
                return _mission.friendlyDeployment ??= new Data.MissionZone();
            return _mission.enemyDeployment ??= new Data.MissionZone();
        }

        void SetMissionDeployment(Team team)
        {
            if (!PointAreaToolAtPanelMission()) return;

            string side = team == Team.User ? "friendly" : "enemy";
            _hud.Flash($"Click the ground for the {side} deployment zone (Esc or RMB cancels).");

            // An authoring pick: no selection behind it, and made with the clock
            // stopped — see SelectionManager.ArmGroundPick.
            _selection.ArmGroundPick((lat, lon) =>
            {
                var zone = DeploymentFor(team);
                zone.placed = true;
                zone.latitude = lat;
                zone.longitude = lon;

                RefreshDeploymentZones();
                _hud.Flash($"{char.ToUpperInvariant(side[0]) + side.Substring(1)} deployment zone set — " +
                           $"{_mission.deploymentRadiusKm:0.#} km. Reinforcements arrive here.");
            }, "Deployment zone placement cancelled.", requireSelection: false, battleOnly: false);
        }

        void ClearMissionDeployment(Team team)
        {
            if (!PointAreaToolAtPanelMission()) return;

            var zone = DeploymentFor(team);
            if (!zone.placed)
            {
                _hud.Flash("That deployment zone has not been placed.");
                return;
            }

            zone.placed = false;
            RefreshDeploymentZones();
            _hud.Flash($"{(team == Team.User ? "Friendly" : "Enemy")} deployment zone cleared — " +
                       "its reinforcements will arrive behind their own force.");
        }

        void SetMissionDeploymentRadius(float km)
        {
            if (!PointAreaToolAtPanelMission()) return;

            _mission.deploymentRadiusKm = km;
            RefreshDeploymentZones();
            _hud.Flash($"Deployment zones are now {km:0.#} km across the radius.");
        }

        /// <summary>Redraws both deployment rings from the open mission and repaints the panel.</summary>
        void RefreshDeploymentZones()
        {
            if (_palette != null) _palette.RefreshDeploymentZones();

            ShowDeployRing(ref _friendlyDeployRing, _mission?.friendlyDeployment,
                GameConfig.BlueTeam, "FRIENDLY DEPLOYMENT");
            ShowDeployRing(ref _enemyDeployRing, _mission?.enemyDeployment,
                GameConfig.RedTeam, "ENEMY DEPLOYMENT");
        }

        void ShowDeployRing(ref RangeRing ring, Data.MissionZone zone, Color colour, string title)
        {
            if (zone == null || !zone.placed || _mission == null)
            {
                if (ring != null) ring.Hide();
                return;
            }

            if (ring == null)
                ring = RangeRing.Create(_map.Georeference, _map.Georeference.transform, colour, title);

            ring.Show(zone.latitude, zone.longitude, Mathf.Max(0.2f, _mission.deploymentRadiusKm),
                $"{title}  {_mission.deploymentRadiusKm:0.#} km");
        }

        void SetMissionHqRadius(float km)
        {
            if (!PointAreaToolAtPanelMission()) return;

            _mission.hqRadiusKm = km;
            RefreshHqZones();
            _hud.Flash($"HQ zones are now {km:0.#} km across the radius.");
        }

        /// <summary>
        /// Redraws both HQ rings from the open mission and repaints the panel.
        /// One method, called from every path that can change them, so the map
        /// and the panel cannot disagree about where a headquarters is.
        /// </summary>
        void RefreshHqZones()
        {
            if (_palette != null) _palette.RefreshHqZones();

            ShowHqRing(ref _friendlyHqRing, _mission?.friendlyHq, GameConfig.BlueTeam, "FRIENDLY HQ");
            ShowHqRing(ref _enemyHqRing, _mission?.enemyHq, GameConfig.RedTeam, "ENEMY HQ");
        }

        void ShowHqRing(ref RangeRing ring, Data.MissionZone zone, Color colour, string title)
        {
            if (zone == null || !zone.placed || _mission == null)
            {
                if (ring != null) ring.Hide();
                return;
            }

            // Built on first use rather than at startup: most maps in the editor
            // are not a mission, and two rings nobody asked for would be two
            // more objects re-sampling terrain every time the georeference moves.
            if (ring == null)
                ring = RangeRing.Create(_map.Georeference, _map.Georeference.transform, colour, title);

            ring.Show(zone.latitude, zone.longitude, Mathf.Max(0.2f, _mission.hqRadiusKm),
                $"{title}  {_mission.hqRadiusKm:0.#} km");
        }

        /// <summary>
        /// Boxes the mission's area around the point the camera is looking at.
        /// "Here" rather than around the mission's start point, because the
        /// designer has already flown to the ground they mean.
        /// </summary>
        void MakeMissionRectangle(float halfKm)
        {
            if (!PointAreaToolAtPanelMission()) return;

            GeoUtils.UnityToGeo(_map.Georeference, _rig.Focus, out double lat, out double lon, out _);
            _areaTool.SetArea(Data.MissionArea.Rectangle(lat, lon, halfKm, halfKm));
            _hud.Flash($"Mission area set to a {halfKm * 2f:0} km box. SAVE MISSION + MAP to keep it.");
        }

        /// <summary>
        /// Pushes the open mission's boundary into the systems that enforce it:
        /// the fog (which blacks out everything beyond it in battle) and the
        /// camera (which will not be walked past it).
        ///
        /// **The camera is bounded in battle only.** The editor has to be able
        /// to fly outside the area to draw it, and to see the ground it might
        /// grow into. It is the battle that is fought on one piece of ground.
        /// </summary>
        void ApplyMissionArea()
        {
            var area = _mission?.area;
            bool bounded = area != null && area.HasArea;

            _fog.SetArea(bounded ? area : null);

            if (!bounded || !_combat.Running)
            {
                _rig.ClampFocus = null;
                _rig.SetMaxDistance(float.MaxValue);   // clamped to the rig's own ceiling
                return;
            }

            _rig.ClampFocus = world =>
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
                double clampedLat = lat, clampedLon = lon;
                area.Clamp(ref clampedLat, ref clampedLon);
                if (clampedLat == lat && clampedLon == lon) return world;
                return GeoUtils.GeoToUnity(_map.Georeference, clampedLat, clampedLon, 300);
            };

            // Enough standoff to see the whole area and a margin of the dark
            // around it, and no more — zooming out to a continent when the
            // battle is a valley is the same problem as panning to one.
            _rig.SetMaxDistance(Mathf.Max(2000f, area.RadiusKm() * 2.4f * 1000f));
        }

        /// <summary>Puts the camera over a geodetic point at a given standoff.</summary>
        void FlyTo(double lat, double lon, float altitudeMeters)
        {
            _rig.ResetNorth();
            _rig.ResetTilt();
            _rig.JumpTo(GeoUtils.GeoToUnity(_map.Georeference, lat, lon, 300));
            _rig.SetDistance(altitudeMeters);
        }

        /// <summary>
        /// Double-click on a DEPLOYED row: select the formation and travel to it.
        ///
        /// Selecting first is what makes the arrival mean something — the
        /// camera stops over a counter that is ringed, outlined and open in the
        /// info panel, rather than over a patch of terrain the player then has
        /// to find the unit on. The standoff is close enough to read the
        /// counter's neighbours but not so close that the formation fills the
        /// screen; a unit already being looked at from closer in keeps that
        /// view rather than being pulled back out to a standard one.
        /// </summary>
        void FlyToUnit(UnitActor unit)
        {
            if (unit == null || !unit.IsAlive) return;

            _selection.Select(unit);

            var focus = GeoUtils.GeoToUnity(_map.Georeference,
                unit.State.latitude, unit.State.longitude, 300);
            _rig.FlyTo(focus, Mathf.Min(_rig.Distance, UnitFocusDistanceMeters));

            _hud.Flash($"{(string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName)}" +
                       " — flying to its position.");
        }

        /// <summary>Standoff a double-clicked formation is shown at, metres.</summary>
        const float UnitFocusDistanceMeters = 4500f;

        // ------------------------------------------------- map context menus

        /// <summary>
        /// Answers a right-click on the map: opens the menu for whatever is
        /// under the cursor, or lets the click through as a move order.
        ///
        /// **What gets a menu, and why only these.** A friendly formation and a
        /// logistic site are the two things on this map that a player owns and
        /// may want gone. An enemy formation is not yours to remove, and bare
        /// ground already means something on this button — so both fall through
        /// to the move order, which is what right-click has always done.
        ///
        /// Formations win over sites when the two overlap: a counter is the
        /// thing you are looking at, and a depot underneath it is scenery by
        /// comparison. Same precedence the left-click pick uses for units over
        /// control measures.
        ///
        /// Both modes. The menu is about what is *on* the map rather than about
        /// what the map is for, and a right-click that produced a menu while a
        /// battle ran and silently moved a formation while it did not would be a
        /// trap rather than a mode.
        /// </summary>
        bool OpenMapContextMenu(Vector2 screenPos)
        {
            if (_canvas == null) return false;

            var unit = _selection.UnitAt(screenPos);
            if (unit != null && unit.IsAlive && unit.State.TeamEnum == Team.User)
            {
                string name = string.IsNullOrEmpty(unit.State.customName)
                    ? unit.Def.name : unit.State.customName;

                ContextMenuUI.Open(_canvas, screenPos, $"{name}  ·  {unit.State.echelon}",
                    new System.Collections.Generic.List<ContextMenuUI.Item>
                    {
                        new ContextMenuUI.Item("REMOVE UNIT", () => RemoveUnitFromMap(unit), destructive: true)
                    });
                return true;
            }

            var obstacle = _obstacles != null ? _obstacles.PickAt(_rig.Cam, screenPos) : null;
            if (obstacle != null)
            {
                var obsDef = ObstacleCatalog.Get(obstacle.Kind);
                bool hostile = obstacle.Data.team == Team.Enemy.ToString();

                ContextMenuUI.Open(_canvas, screenPos,
                    $"{obsDef.name}  ·  {(hostile ? "ENEMY" : "FRIENDLY")}",
                    new System.Collections.Generic.List<ContextMenuUI.Item>
                    {
                        new ContextMenuUI.Item("REMOVE GRAPHIC", () =>
                        {
                            _obstacles.Remove(obstacle);
                            _hud.Flash($"{obsDef.name} removed.");
                        }, destructive: true)
                    });
                return true;
            }

            var site = _logistics != null
                ? _logistics.PickAt(_rig.Cam, screenPos)
                : null;
            if (site != null)
            {
                var def = LogisticsCatalog.Get(site.Kind);
                bool enemy = site.Data.team == Team.Enemy.ToString();

                ContextMenuUI.Open(_canvas, screenPos,
                    $"{def.name}  ·  {(enemy ? "ENEMY" : "FRIENDLY")}",
                    new System.Collections.Generic.List<ContextMenuUI.Item>
                    {
                        new ContextMenuUI.Item("REMOVE SITE", () =>
                        {
                            _logistics.Remove(site);
                            _hud.Flash($"{def.name} removed.");
                        }, destructive: true)
                    });
                return true;
            }

            return false;
        }

        /// <summary>
        /// Takes a formation off the map, from wherever the request came — the
        /// rail's DEPLOYED list, the group panel, or the map's own right-click
        /// menu. One path, so a removal is recorded for undo and drops the
        /// selection in every case.
        /// </summary>
        void RemoveUnitFromMap(UnitActor unit)
        {
            if (unit == null || !unit.IsAlive) return;

            string name = string.IsNullOrEmpty(unit.State.customName)
                ? unit.Def.name : unit.State.customName;

            RecordRemoval(unit);

            // Drop it from the selection first, so nothing is left pointing at a
            // formation that is about to leave the map — the info panel would
            // keep reporting it and the order bar would keep offering it orders.
            bool wasSelected = false;
            foreach (var s in _selection.Selection) if (s == unit) { wasSelected = true; break; }
            if (wasSelected) _selection.Select(null);

            unit.RemoveFromMap();
            _hud.Flash($"{name} removed. Ctrl+Z puts it back.");
        }

        /// <summary>Every living formation in a group, in registry order.</summary>
        System.Collections.Generic.List<UnitActor> GroupMembers(string groupId)
        {
            var members = new System.Collections.Generic.List<UnitActor>();
            if (string.IsNullOrEmpty(groupId)) return members;
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.groupId == groupId) members.Add(u);
            return members;
        }

        /// <summary>
        /// Puts a group on the front line: its formations are spread evenly
        /// along the FLOT and each digs in on its own stretch of it.
        ///
        /// **Why this is worth a button.** The front line is *derived* — it is
        /// where the fighting is, not a control measure anybody drew — and up
        /// to now nothing could be done with it except look at it. But "hold
        /// the line" is the commonest order at this level, and giving it by
        /// hand meant clicking DEFENCE once per battalion and eyeballing the
        /// spacing along a curve. One click instead: the line is sampled at
        /// equal arc lengths, one point per formation, and each is ordered to
        /// defend its point.
        ///
        /// **They are set back from the line, not on it.** The FLOT runs
        /// between the two sides, so a formation placed exactly on it would be
        /// standing in the contact itself. Each objective is offset toward the
        /// group's own rear by <see cref="FlotStandoffKm"/>, which is what
        /// makes this a defence of the line rather than an advance across it.
        /// </summary>
        void ManTheFlot(string groupId)
        {
            var members = GroupMembers(groupId);
            if (members.Count == 0)
            {
                _hud.Flash("That group has no formations left.");
                return;
            }

            // The group mans its own side's forward edge — not the enemy's,
            // and not a pocket ring.
            var pts = _frontline.PointsForManning(members[0].State.TeamEnum);
            if (pts == null || pts.Count < 2)
            {
                _hud.Flash("There is no front line to hold — both sides need formations in contact.");
                return;
            }

            // Which way is "back". Taken from the two sides' centres of mass
            // rather than from the line's own geometry: the line bends, and a
            // per-point normal would send the flank formations off in
            // directions that have nothing to do with where their army is.
            var side = members[0].State.TeamEnum;
            if (!SideCentres(side, out double ownLat, out double ownLon,
                             out double enemyLat, out double enemyLon))
            {
                _hud.Flash("The enemy has nothing on the map — there is no front to face.");
                return;
            }

            GeoUtils.ToLocalKm(enemyLat, enemyLon, ownLat, ownLon, out double backEast, out double backNorth);
            double backLength = System.Math.Sqrt(backEast * backEast + backNorth * backNorth);
            if (backLength < 0.001)
            {
                _hud.Flash("The two sides are on top of each other — no front to hold.");
                return;
            }
            backEast /= backLength; backNorth /= backLength;

            // Arc length along the drawn line, so the formations are spaced by
            // *ground* rather than by vertex index — the line is smoothed, and
            // its vertices bunch up wherever it bends.
            var cumulative = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++)
                cumulative[i] = cumulative[i - 1] + GeoUtils.DistanceKm(
                    pts[i - 1].latitude, pts[i - 1].longitude, pts[i].latitude, pts[i].longitude);

            double total = cumulative[pts.Count - 1];
            if (total < 0.1)
            {
                _hud.Flash("The front line is too short to distribute a group along.");
                return;
            }

            int placed = 0;
            for (int i = 0; i < members.Count; i++)
            {
                // Centres of equal shares rather than the ends, so the outermost
                // formations sit inside the line instead of on its two tips.
                double target = total * (i + 0.5) / members.Count;
                PointAt(pts, cumulative, target, out double lat, out double lon);

                GeoUtils.FromLocalKm(lat, lon, backEast * FlotStandoffKm, backNorth * FlotStandoffKm,
                    out double standLat, out double standLon);

                if (_defence.Defend(members[i], standLat, standLon)) placed++;
            }

            // One name for all three places it appears — the flash, the line's
            // own caption and the panel's readout — so an unnamed group does
            // not read as "no group" on one of them and as a holder on another.
            string name = string.IsNullOrEmpty(members[0].State.groupName)
                ? "Unnamed group" : members[0].State.groupName;
            _frontline.SetHoldingGroup(groupId, name);
            if (_palette != null) _palette.SetFlotHolder(name);

            _hud.Flash($"{name} takes the front line — {placed} formation(s) along {total:0.#} km, " +
                       $"{FlotStandoffKm:0.#} km back from it.");
        }

        /// <summary>How far behind the FLOT a formation manning it digs in, km.</summary>
        const double FlotStandoffKm = 1.2;

        /// <summary>
        /// Power-weighted centres of the given side and of its opponent.
        /// Returns false when either side has nothing on the map.
        /// </summary>
        static bool SideCentres(Team side, out double ownLat, out double ownLon,
            out double enemyLat, out double enemyLon)
        {
            ownLat = ownLon = enemyLat = enemyLon = 0;
            int own = 0, enemy = 0;

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                if (u.State.TeamEnum == side)
                {
                    ownLat += u.State.latitude; ownLon += u.State.longitude; own++;
                }
                else
                {
                    enemyLat += u.State.latitude; enemyLon += u.State.longitude; enemy++;
                }
            }

            if (own == 0 || enemy == 0) return false;
            ownLat /= own; ownLon /= own;
            enemyLat /= enemy; enemyLon /= enemy;
            return true;
        }

        /// <summary>The point a given distance along a polyline, interpolated within its segment.</summary>
        static void PointAt(System.Collections.Generic.List<GeoPoint> pts, double[] cumulative,
            double distanceKm, out double lat, out double lon)
        {
            int i = 1;
            while (i < pts.Count - 1 && cumulative[i] < distanceKm) i++;

            double span = cumulative[i] - cumulative[i - 1];
            double t = span > 1e-6 ? (distanceKm - cumulative[i - 1]) / span : 0.0;
            t = System.Math.Max(0.0, System.Math.Min(1.0, t));

            lat = pts[i - 1].latitude + (pts[i].latitude - pts[i - 1].latitude) * t;
            lon = pts[i - 1].longitude + (pts[i].longitude - pts[i - 1].longitude) * t;
        }

        /// <summary>
        /// Double-click on a group row: select the whole group and travel to it.
        ///
        /// Unlike a single formation, a group has an **extent**, so the standoff
        /// is derived from it rather than fixed — flying to a brigade holding a
        /// thirty-kilometre frontage at the same altitude as a single battalion
        /// would put two of its units on screen and leave the player to guess
        /// where the rest went. The camera pulls back to whatever frames the
        /// whole group, with a floor so a tightly-stacked group is not shoved
        /// into the ground.
        /// </summary>
        void FlyToGroup(System.Collections.Generic.List<UnitActor> members)
        {
            if (members == null || members.Count == 0) return;

            _selection.SetSelection(members);

            double lat = 0, lon = 0;
            int count = 0;
            foreach (var u in members)
            {
                if (u == null || !u.IsAlive) continue;
                lat += u.State.latitude; lon += u.State.longitude;
                count++;
            }
            if (count == 0) return;

            lat /= count; lon /= count;

            // Radius of the group about its own centre, in metres.
            double spreadM = 0;
            foreach (var u in members)
            {
                if (u == null || !u.IsAlive) continue;
                spreadM = System.Math.Max(spreadM,
                    GeoUtils.DistanceKm(lat, lon, u.State.latitude, u.State.longitude) * 1000.0);
            }

            float distance = Mathf.Max(UnitFocusDistanceMeters, (float)spreadM * GroupFocusMargin);
            _rig.FlyTo(GeoUtils.GeoToUnity(_map.Georeference, lat, lon, 300), distance);

            string name = members[0] != null && !string.IsNullOrEmpty(members[0].State.groupName)
                ? members[0].State.groupName : "Group";
            _hud.Flash($"{name} — {count} formation(s) selected, flying to them.");
        }

        /// <summary>
        /// How much standoff a group's own radius is worth. Above 2 so the
        /// outermost formations sit inside the frame rather than on its edge.
        /// </summary>
        const float GroupFocusMargin = 2.6f;

        /// <summary>Takes every unit, effect and undo step off the map, ready for a fresh load.</summary>
        void ClearMapContents()
        {
            _combat.SetRunning(false);
            _attacks.CancelAll();
            _recon.CancelAll();
            if (_airDefence != null) _airDefence.CancelAll();
            _selection.Select(null);

            foreach (var a in new System.Collections.Generic.List<UnitActor>(UnitRegistry.All))
                if (a != null) Destroy(a.gameObject);
            UnitRegistry.Clear();

            // A casualty list carried over from the scenario being replaced
            // would be worse than none — it would be a wrong one.
            LossLedger.Clear();

            if (_vfx != null) _vfx.StopAll();
            if (_aftermath != null) _aftermath.ClearAll();
            _sectors.ClearAll();
            EditHistory.Clear();
        }

        // ------------------------------------------------------- save/load

        /// <summary>
        /// Copies the live editor state into <see cref="_save"/>. Shared by the
        /// map save and the mission save, so the two can never disagree about
        /// what "the current state of the map" means.
        /// </summary>
        void CollectSave()
        {
            _save.units.Clear();
            foreach (var a in UnitRegistry.All)
                if (a != null && a.IsAlive) _save.units.Add(a.Snapshot());
            _save.lines = _lines.Serialize();
            _save.markers = _markers.Serialize();
            _save.flotMode = _frontline.Mode.ToString();
            _save.logistics = _logistics.Serialize();
            _save.mapObjects = _mapObjects.Serialize();
            _save.obstacles = _obstacles.Serialize();
            _save.resources = _sustainment.Serialize();
            _save.reinforcements = _reinforcements.Serialize();
            _save.commanders = CommanderRegistry.Serialize();
            _save.teams = PlayerRegistry.SaveTeams();
            _save.players = PlayerRegistry.SavePlayers();
            _save.viewMode = _map.ViewMode.ToString();
            _save.mapStyle = _map.Style.ToString();
            _save.showBuildings = _map.BuildingsVisible;
            _save.startDateTime = _clock.StartToSaveString();
            _save.skyPhase = _weather.ManualPhase.ToString();
            _save.weatherCondition = _weather.Condition.ToString();
            _save.autoDayNight = _weather.AutoDayNight;
        }

        void SaveMap()
        {
            CollectSave();

            // A mission's map save is also a mission save: the record carries a
            // copy of the view and weather, and leaving it stale would mean the
            // player entering on settings the designer changed and saved.
            if (_mission != null)
            {
                MissionLibrary.ReadBackFrom(_mission, _save);
                MissionLibrary.SaveBook();
            }

            string path = SaveSystem.SaveMap(_save, mapFileName);
            _hud.Flash($"Saved -> {path}");
        }

        // ------------------------------------------------------- reset

        /// <summary>
        /// RESET in the top bar. Asks first: this throws away every unit, line,
        /// marker and order the player has placed, and Ctrl+Z tracks individual
        /// edits rather than wholesale operations, so there is no way back.
        /// </summary>
        void ConfirmReset()
        {
            ConfirmDialog.Open(_canvas, "RESET MAP EDITOR",
                "Reloads the scenario from disk and puts every editor setting back to its default.\n\n" +
                "Units you have deployed, lines you have drawn, defensive positions and any orders in " +
                "progress are discarded. This cannot be undone.",
                "RESET EVERYTHING", ResetEditor);
        }

        /// <summary>
        /// Back to the state the editor opens in: the scenario as it is on disk,
        /// and every setting the player can change from the panels at its
        /// default. Deliberately reloads the *shipped* map rather than the last
        /// save, because "reset" that restores your own last save is not a reset.
        /// </summary>
        void ResetEditor()
        {
            // Stop the world first: a running battle, orders in flight and a
            // half-finished line would all otherwise act on units being deleted.
            _combat.SetRunning(false);
            _attacks.CancelAll();
            _recon.CancelAll();
            if (_manoeuvre != null) _manoeuvre.CancelAll();
            if (_taskAreas != null) _taskAreas.ClearAll();
            if (_planner != null) _planner.ClearAll();
            _effects.Cancel();
            _logistics.Cancel();
            _obstacles.Cancel();
            if (_mapObjects != null) _mapObjects.Cancel();
            _airSupply.Cancel();
            _missiles.Cancel();
            _naval.Cancel();
            _artillery.Cancel();
            _airStrike.Cancel();
            _uavStrike.Cancel();
            _selection.Select(null);
            if (_frontlinePanel != null) _frontlinePanel.Hide();
            if (_strikeDock != null) _strikeDock.Hide();

            // Editor settings, and the panel lamps that report them.
            _fog.SetEnabled(false);
            _showLineOfSight = true;
            _showWeaponRange = true;
            _sectors.AutoUpdate = false;
            _sectors.ClearAll();
            if (_palette != null) _palette.SetFlotHolder("");
            _frontline.ResetToDefaults();
            if (_palette != null) _palette.SyncGeneralToggles(false, false, true, true);
            UnitActor.SetLabelScale(1f);
            _clock.SetSpeed(GameClock.NormalSpeed);
            _mapControls.SetControlsVisible(true);
            _mapControls.SetCompassVisible(false);

            // In a mission, "reset" means the mission as it is on disk — its own
            // saved scenario. Falling through to LoadShippedMap would look for a
            // StreamingAssets file a player-authored mission has never had.
            var shipped = _mission != null
                ? MissionLibrary.LoadMap(_mission)
                : SaveSystem.LoadShippedMap(mapFileName);

            if (shipped == null)
            {
                _hud.Flash("Reset failed — the scenario could not be read.");
                return;
            }

            _save = shipped;
            foreach (var a in new System.Collections.Generic.List<UnitActor>(UnitRegistry.All))
                if (a != null) Destroy(a.gameObject);
            UnitRegistry.Clear();
            LossLedger.Clear();
            if (_vfx != null) _vfx.StopAll();
            // StopAll kills the effects; this is what stops the bookkeeping
            // outliving them and trying to swap a dead fire for smoke.
            if (_aftermath != null) _aftermath.ClearAll();
            StrikeBudget.Reset();
            EditHistory.Clear();

            ApplySave(_save);
            _map.SetViewMode(_save.viewMode == "Mode2D" ? ViewMode.Mode2D : ViewMode.Mode3D);
            _map.SetMapStyle(System.Enum.TryParse(_save.mapStyle, out MapStyle style) ? style : MapStyle.Satellite);
            _map.SetBuildingsVisible(_save.showBuildings);
            _rig.ResetNorth();
            _rig.ResetTilt();
            _rig.JumpTo(GeoUtils.GeoToUnity(_map.Georeference,
                _save.centerLatitude, _save.centerLongitude, 300));

            _hud.Flash($"Editor reset — '{_save.mapName}' reloaded from the shipped scenario.");
        }

        void LoadMap()
        {
            var data = SaveSystem.LoadMap(mapFileName);
            if (data == null) { _hud.Flash("No save found."); return; }
            _save = data;
            foreach (var a in new System.Collections.Generic.List<UnitActor>(UnitRegistry.All))
                if (a != null) Destroy(a.gameObject);
            UnitRegistry.Clear();
            LossLedger.Clear();
            // World-anchored wreck fires and smoke outlive their units by design,
            // so they have to be cleared explicitly on a reload.
            if (_vfx != null) _vfx.StopAll();
            if (_aftermath != null) _aftermath.ClearAll();
            // Undo closures captured actors that no longer exist.
            EditHistory.Clear();
            ApplySave(_save);
            _hud.Flash($"Loaded '{_save.mapName}'.");
        }

        void ApplySave(MapSaveData data)
        {
            _clock.SetStartFromSaveString(data.startDateTime);
            _weather.LoadFrom(data.skyPhase, data.weatherCondition, data.autoDayNight);
            foreach (var u in data.units)
                UnitActor.Spawn(_map.Georeference, u);
            _lines.LoadFrom(data.lines);
            // Before the first solve, so a manual-mode map comes back manual
            // and adopts its drawn line rather than overwriting it.
            _frontline.SetMode(System.Enum.TryParse(data.flotMode, out FlotMode fm) ? fm : FlotMode.Automatic);
            // After the units: a task marker whose owning unit is not on the map
            // is swept away, and during a load that is briefly all of them.
            _markers.LoadFrom(data.markers);
            // Independent of the units: an installation belongs to the scenario
            // and outlives every formation that draws on it.
            _logistics.LoadFrom(data.logistics);
            _mapObjects.LoadFrom(data.mapObjects);
            _obstacles.LoadFrom(data.obstacles);
            _sustainment.LoadFrom(data.resources);
            _reinforcements.LoadFrom(data.reinforcements);
            // Commanders last. They are referenced by id from the units that are
            // already down, so loading them earlier would have the roster point
            // at formations that did not exist yet.
            CommanderRegistry.LoadFrom(data.commanders);
            EnsureCommanders();
            // Who is playing which side. Fills in the two-team, two-player
            // default when the map carries none — see docs/25-PLAYERS.md.
            PlayerRegistry.LoadFrom(data.teams, data.players);
        }

        /// <summary>
        /// Gives a side a chain of command if the map brought none.
        ///
        /// **Why this is automatic now.** It used to be a SEED button in the
        /// COMMANDERS panel, which meant every scenario started with two empty
        /// rosters and every formation reading as unassigned until somebody
        /// found the button and pressed it twice. A chain of command is not an
        /// optional extra: an army has one, and a map that does not is a map
        /// missing something rather than a map exercising a choice.
        ///
        /// **Only when empty.** A saved scenario's own roster is the designer's
        /// work and is never overwritten — including a deliberately emptied one
        /// for the side that was cleared, which is why CLEAR ALL still means
        /// something. The seed runs per side, so a map that names a friendly
        /// chain and no enemy one gets the enemy filled in and its own left
        /// alone.
        /// </summary>
        void EnsureCommanders()
        {
            foreach (var team in new[] { Team.User, Team.Enemy })
            {
                if (CommanderRegistry.CountOfTeam(team) > 0) continue;
                CommanderRegistry.Seed(team);
            }
        }

        /// <summary>
        /// Hands the interface's hover sound back on the way out. The
        /// suppression is this screen's, and Unity destroys the old scene
        /// before the next one's <c>Start</c> runs, so the menu the player
        /// lands on has it again.
        /// </summary>
        void OnDestroy() => IronMeridian.Audio.AudioManager.HoverSuppressed = false;

        void Update()
        {
            TickMissionAutoStart();
            RefreshRightInset();
            // Polled, not pushed: it depends on the window's height as well as
            // on whether the minimap is up. Both guards make the poll free when
            // nothing has changed.
            RefreshRightDockTop();

            if (Input.GetKeyDown(KeyCode.F5)) SaveMap();
            if (Input.GetKeyDown(KeyCode.F9)) LoadMap();

            // TAB — the casualty list. Toggled from here alone: the dialog also
            // watches Escape, but two behaviours reading TAB in an undefined
            // order would close and reopen it in the same frame.
            //
            // Battle mode only, or once a battle has been fought: in a fresh
            // editor nothing has been lost, and a key that opened an empty page
            // would read as broken rather than as informative.
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (LossesDialog.IsOpen) LossesDialog.Close();
                else if (_combat.Running || LossLedger.Any) LossesDialog.Open(_canvas);
                else _hud.Flash("No battle has been fought yet — start one to keep a casualty list.");
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (ctrl)
            {
                if (Input.GetKeyDown(KeyCode.C)) CopySelection();
                if (Input.GetKeyDown(KeyCode.V)) PasteClipboard();
                if (Input.GetKeyDown(KeyCode.Z)) UndoLastEdit();
            }

            // Keep the range rings glued to a moving selected unit without
            // re-sampling terrain every frame when it's standing still.
            if (_rangeRingUnit != null && _losRing != null && _weaponRing != null)
            {
                if (!_rangeRingUnit.IsAlive)
                {
                    _losRing.Hide(); _weaponRing.Hide(); _rangeRingUnit = null;
                }
                else if (_rangeRingUnit.State.latitude != _rangeRingLat ||
                         _rangeRingUnit.State.longitude != _rangeRingLon)
                {
                    ShowRangeRingsForCurrentUnit();
                }
            }
        }

        // ------------------------------------------------------- range rings
        void UpdateRangeRings(System.Collections.Generic.IReadOnlyList<UnitActor> sel)
        {
            if (_losRing == null || _weaponRing == null) return;
            if (sel.Count != 1 || sel[0] == null || !sel[0].IsAlive)
            {
                _losRing.Hide();
                _weaponRing.Hide();
                _rangeRingUnit = null;
                return;
            }
            _rangeRingUnit = sel[0];
            ShowRangeRingsForCurrentUnit();
        }

        void ShowRangeRingsForCurrentUnit()
        {
            if (_losRing == null || _weaponRing == null) return;
            var u = _rangeRingUnit;

            if (_showLineOfSight)
            {
                float km = u.Def.viewRangeKm;
                _losRing.Show(u.State.latitude, u.State.longitude, km,
                    $"LINE OF SIGHT  {km * 1000f:n0} m");
            }
            else _losRing.Hide();

            if (_showWeaponRange)
            {
                float km = u.Def.weaponRangeKm;
                _weaponRing.Show(u.State.latitude, u.State.longitude, km,
                    $"MAX WEAPON RANGE  {km * 1000f:n0} m");
            }
            else _weaponRing.Hide();

            _rangeRingLat = u.State.latitude;
            _rangeRingLon = u.State.longitude;
        }

        // ------------------------------------------------- order bar helpers

        /// <summary>
        /// Applies an order to every formation in the selection. The bar acts on
        /// the lead unit's *state* but on the whole selection's *behaviour* — a
        /// player who has box-selected six battalions and pressed STOP means all
        /// six, and asking them to do it once per counter would be the interface
        /// forgetting what a selection is for.
        /// </summary>
        void ForSelection(System.Action<UnitActor> order)
        {
            foreach (var u in new System.Collections.Generic.List<UnitActor>(_selection.Selection))
                if (u != null && u.IsAlive) order(u);
        }

        /// <summary>
        /// Gives a ground order to the whole selection, **spread across a
        /// frontage** rather than stacked on the point that was clicked.
        ///
        /// One click is one objective, but six battalions cannot occupy one
        /// grid square: sending them all to the same coordinate piles six
        /// counters, six objective rings and six defensive lines on top of each
        /// other, and the player has ordered something no formation could
        /// carry out. So the click sets the *centre* of a frontage, and the
        /// formations are laid out across it perpendicular to the axis of
        /// advance — which is what a frontage is.
        ///
        /// Two details make it read as a deliberate laydown rather than an
        /// arbitrary scatter. The spacing comes from the formations' own reach,
        /// so a group of mortar companies is packed tighter than a group of
        /// rocket battalions and each can still cover its neighbour. And they
        /// are ordered by where they already stand across that axis, so the
        /// left-hand formation gets the left-hand slot and nobody is sent
        /// across the front of anybody else.
        /// </summary>
        void ForSelectionOnGround(double lat, double lon,
            System.Action<UnitActor, double, double> order)
        {
            var units = new System.Collections.Generic.List<UnitActor>();
            foreach (var u in _selection.Selection)
                if (u != null && u.IsAlive) units.Add(u);

            if (units.Count == 0) return;
            if (units.Count == 1) { order(units[0], lat, lon); return; }

            // Axis of advance: from where the group is now to where it is being
            // sent. The frontage lies across it.
            double centreLat = 0, centreLon = 0;
            double reachKm = 0;
            foreach (var u in units)
            {
                centreLat += u.State.latitude;
                centreLon += u.State.longitude;
                reachKm += u.Def.weaponRangeKm;
            }
            centreLat /= units.Count; centreLon /= units.Count;
            reachKm /= units.Count;

            float axis = GeoUtils.BearingDeg(centreLat, centreLon, lat, lon);
            double across = (axis + 90f) * System.Math.PI / 180.0;
            double eastAcross = System.Math.Sin(across), northAcross = System.Math.Cos(across);

            double spacingKm = System.Math.Min(4.0, System.Math.Max(0.6, reachKm * 0.35));

            // Sorted by their present position along the frontage, so the order
            // of march is the order they are already in.
            double Lateral(UnitActor u)
            {
                GeoUtils.ToLocalKm(centreLat, centreLon, u.State.latitude, u.State.longitude,
                    out double east, out double north);
                return east * eastAcross + north * northAcross;
            }
            units.Sort((a, b) => Lateral(a).CompareTo(Lateral(b)));

            double half = (units.Count - 1) * spacingKm * 0.5;
            for (int i = 0; i < units.Count; i++)
            {
                double offset = i * spacingKm - half;
                GeoUtils.FromLocalKm(lat, lon, eastAcross * offset, northAcross * offset,
                    out double unitLat, out double unitLon);
                order(units[i], unitLat, unitLon);
            }
        }

        /// <summary>
        /// MOVE, FAST MOVE, TACTICAL MOVE, WITHDRAW or RETREAT onto the picked
        /// ground. Given to the whole selection; each formation gets its own
        /// objective ring, because six rings on one point would be one ring.
        /// </summary>
        void OrderMove(MoveTask task, double lat, double lon)
        {
            ForSelectionOnGround(lat, lon, (u, ulat, ulon) => _manoeuvre.Order(u, task, ulat, ulon));
        }

        /// <summary>
        /// RECON AREA on the picked ground: four sectors about the point, and
        /// the formation moves there and searches it.
        ///
        /// The area is drawn from the formation's own sensor reach under the
        /// task, so the quadrants are the ground it will *actually* see rather
        /// than a fixed circle that flatters a scout car and shortchanges a
        /// surveillance radar.
        /// </summary>
        void OrderRecon(ReconTask task, double lat, double lon)
        {
            var def = ReconTaskCatalog.Get(task);

            ForSelectionOnGround(lat, lon, (unit, ulat, ulon) =>
            {
                double radiusKm = System.Math.Min(20.0,
                    System.Math.Max(1.0, unit.Def.viewRangeKm * def.sensorRangeFactor));
                float axis = GeoUtils.BearingDeg(unit.State.latitude, unit.State.longitude, ulat, ulon);

                _taskAreas.Show(unit, TaskAreaShape.Quadrants, MarkerKind.Recon, "RECON",
                    ulat, ulon, radiusKm, axis, def.arrowTint, VfxId.TaskAreaRecon);

                _recon.Order(unit, ulat, ulon, task);
            });
        }

        /// <summary>
        /// DEFEND, HOLD or GUARD on the picked ground — across a frontage when
        /// several formations are selected, which is what a defence given to a
        /// group means: each holds its own stretch of the line, side by side,
        /// rather than all of them stacking on one hill.
        /// </summary>
        void OrderDefence(UnitActionBarUI.DefenceTask task, double lat, double lon)
        {
            ForSelectionOnGround(lat, lon, (unit, ulat, ulon) =>
            {
                switch (task)
                {
                    case UnitActionBarUI.DefenceTask.Defend: _defence.Defend(unit, ulat, ulon); break;
                    case UnitActionBarUI.DefenceTask.Hold: _defence.Hold(unit, ulat, ulon); break;
                    default: _defence.Guard(unit, ulat, ulon); break;
                }
            });
        }

        /// <summary>
        /// An attack onto ground rather than onto a formation. The objective
        /// ring is drawn first so the player can see what was committed to, and
        /// the order system takes whatever is inside it.
        /// </summary>
        void OrderAreaAttack(AttackTask task, double lat, double lon)
        {
            ForSelectionOnGround(lat, lon, (unit, ulat, ulon) =>
            {
                double radiusKm = AttackObjectiveRadiusKm(unit);
                float axis = GeoUtils.BearingDeg(unit.State.latitude, unit.State.longitude, ulat, ulon);

                _taskAreas.Show(unit, TaskAreaShape.Ring, MarkerKind.Attack, "ATTACK",
                    ulat, ulon, radiusKm, axis, AttackTaskCatalog.Get(task).arrowTint,
                    VfxId.TaskAreaAttack);

                _attacks.OrderArea(unit, ulat, ulon, radiusKm, task);
            });
        }

        /// <summary>
        /// Radius of an attack objective. Half the formation's own weapon range,
        /// floored and capped: a mortar company attacking "that ground" means a
        /// much smaller piece of it than a rocket battalion does, and the ring
        /// has to be something the formation can actually cover.
        /// </summary>
        static double AttackObjectiveRadiusKm(UnitActor unit) =>
            System.Math.Min(8.0, System.Math.Max(0.6, unit.Def.weaponRangeKm * 0.5));

        /// <summary>True while a selection has a panel up on the right-hand edge.</summary>
        bool _selectionPanelOpen;

        /// <summary>
        /// Keeps the on-map compass clear of whatever is docked on the right.
        ///
        /// Four things can be there and only one at a time — the unit
        /// inspector, the group panel, the front line options and the strike
        /// dock — but they are not the same width, and each used to set the
        /// inset itself. One place that asks all of them is what stops the
        /// compass being left parked inboard of a panel that has gone.
        /// </summary>
        void RefreshRightInset()
        {
            if (_mapControls == null) return;

            float inset = 0f;
            if (_selectionPanelOpen) inset = UiTheme.RightPanelWidth;
            else if (_typePanel != null && _typePanel.Visible) inset = UiTheme.RightPanelWidth;
            else if (StrikeDockUI.IsOpen) inset = StrikeDockUI.PanelWidth;
            else if (FrontlinePanelUI.IsOpen) inset = UiTheme.RightPanelWidth;

            // Polled from Update as well as pushed by the panels, because two of
            // them close without raising anything. The guard is what makes the
            // poll free: SetRightInset moves rects, and doing that sixty times a
            // second for a value that has not changed would be a layout rebuild
            // per frame.
            if (Mathf.Approximately(inset, _lastRightInset)) return;
            _lastRightInset = inset;
            _mapControls.SetRightInset(inset);
        }

        float _lastRightInset = -1f;

        /// <summary>
        /// Where the right-hand panels' top edge sits: below the fire-menu
        /// cluster, always.
        ///
        /// This used to also have to clear the minimap, which hung under that
        /// cluster and left a panel on a short screen with less room than its
        /// own header needed. The minimap has moved to the opposite corner —
        /// see MiniMapUI — so the right edge is back to one rule with no
        /// adaptive clamp behind it.
        /// </summary>
        void RefreshRightDockTop()
        {
            float top = UiTheme.TopBarHeight + UiTheme.StrikeDockHeight;

            if (Mathf.Approximately(top, _lastRightDockTop)) return;
            _lastRightDockTop = top;

            if (_infoPanel != null) _infoPanel.SetTopInset(top);
            if (_typePanel != null) _typePanel.SetTopInset(top);
            if (_groupPanel != null) _groupPanel.SetTopInset(top);
            if (_frontlinePanel != null) _frontlinePanel.SetTopInset(top);
            if (_strikeDock != null) _strikeDock.SetTopInset(top);
        }

        float _lastRightDockTop = -1f;

        /// <summary>GENERAL → LINE OF SIGHT. Repaints the current selection immediately.</summary>
        void SetLineOfSightVisible(bool on)
        {
            _showLineOfSight = on;
            if (_rangeRingUnit != null && _rangeRingUnit.IsAlive) ShowRangeRingsForCurrentUnit();
            else if (_losRing != null) _losRing.Hide();

            _hud.Flash(on
                ? "Line of sight shown on the selected unit, with its range in metres."
                : "Line of sight hidden.");
        }

        /// <summary>GENERAL → MAX WEAPON RANGE. The other half of the same pair.</summary>
        void SetWeaponRangeVisible(bool on)
        {
            _showWeaponRange = on;
            if (_rangeRingUnit != null && _rangeRingUnit.IsAlive) ShowRangeRingsForCurrentUnit();
            else if (_weaponRing != null) _weaponRing.Hide();

            _hud.Flash(on
                ? "Maximum weapon range shown on the selected unit, with its reach in metres."
                : "Maximum weapon range hidden.");
        }
    }
}
