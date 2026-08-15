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
        LineDrawTool _drawTool;
        /// <summary>Draws and shows the open mission's boundary — see docs/22-MISSIONS.md.</summary>
        MissionAreaTool _areaTool;
        FrontlineSystem _frontline;
        SectorSystem _sectors;
        DefenceOrderSystem _defence;
        CombatSystem _combat;
        AttackOrderSystem _attacks;
        ReconOrderSystem _recon;
        FogOfWarSystem _fog;
        VfxSystem _vfx;
        WeatherSystem _weather;
        EffectPlacementTool _effects;
        ArtilleryStrikeSystem _artillery;
        AirStrikeSystem _airStrike;
        UavStrikeSystem _uavStrike;
        MissileStrikeSystem _missiles;
        NavalStrikeSystem _naval;
        StrikeAftermath _aftermath;

        // Latest countdown reported by each strike system. A null title means
        // that system has nothing in the air; see RefreshStrikeBanner.
        (string title, float remaining, float total, Color colour) _artilleryBanner;
        (string title, float remaining, float total, Color colour) _airStrikeBanner;
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

            foreach (var other in new[] { _airStrikeBanner, _uavStrikeBanner, _missileBanner, _navalBanner })
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
        BoundaryPanelUI _boundaryPanel;
        FrontlinePanelUI _frontlinePanel;
        MissilePanelUI _missilePanel;
        UnitHoverTooltip _hoverTooltip;
        UnitClusterLayer _clusters;
        ConnectivityWatcher _connectivity;
        UnitInfoPanel _infoPanel;
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
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            UnitRegistry.Clear();
            // Static, so it survives a scene load: a fresh scenario opening with
            // half its strikes already spent would be inexplicable.
            StrikeBudget.Reset();

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

            _drawTool = gameObject.AddComponent<LineDrawTool>();
            _drawTool.Init(_map, _rig.Cam, _lines);

            // The mission's own boundary. Deliberately not a LineManager line:
            // it belongs to the mission record, not to the map file underneath
            // it — see MissionAreaTool.
            _areaTool = gameObject.AddComponent<MissionAreaTool>();
            _areaTool.Init(_map, _rig.Cam);

            _frontline = gameObject.AddComponent<FrontlineSystem>();
            _frontline.Init(_lines);

            _sectors = gameObject.AddComponent<SectorSystem>();
            _sectors.Init(_lines, _map.Georeference);

            // Defend / Hold / Guard. Its graphics are ordinary lines and
            // markers, so they save and load with the rest of the map.
            _defence = gameObject.AddComponent<DefenceOrderSystem>();
            _defence.Init(_lines, _markers);

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

            // Tasked UAV strikes — see docs/19-UAV-STRIKES.md.
            _uavStrike = gameObject.AddComponent<UavStrikeSystem>();
            _uavStrike.Init(_map, _rig.Cam);

            // Missile systems — see docs/20-MISSILE-SYSTEMS.md.
            _missiles = gameObject.AddComponent<MissileStrikeSystem>();
            _missiles.Init(_map, _rig.Cam);

            // Naval gunfire support — see docs/21-NAVAL-GUNFIRE.md.
            _naval = gameObject.AddComponent<NavalStrikeSystem>();
            _naval.Init(_map, _rig.Cam);

            EditHistory.Clear();

            _selection = gameObject.AddComponent<SelectionManager>();
            _selection.InputBlocked = () => Loading || DateTimeDialog.IsOpen ||
                                            ConfirmDialog.IsOpen ||
                                            _effects.IsArmed ||
                                            _artillery.IsArmed ||
                                            _airStrike.IsArmed ||
                                            _uavStrike.IsArmed ||
                                            _missiles.IsArmed ||
                                            _naval.IsArmed ||
                                            _areaTool.Drawing ||
                                            _drawTool.Current != LineDrawTool.Mode.None;
            _selection.BattleRunning = () => _combat.Running;

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
            _uavStrike.Flash = _hud.Flash;
            _missiles.Flash = _hud.Flash;
            _naval.Flash = _hud.Flash;

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
            // wiring and pause menu unbuilt with no clue as to why.
            // Built before the palette: the palette's CONTROL MEASURES section
            // opens it, and wiring that up needs it to already exist.
            BuildStep("control measure options", () =>
            {
                _boundaryPanel = BoundaryPanelUI.Create(canvas, _drawTool, () =>
                {
                    if (_palette != null) _palette.MarkBoundaryToolActive();
                });
                // Two panels cannot share the right-hand edge. Opening this one
                // drops the unit selection, which is what takes the info panel
                // down — and is honest besides: you are drawing now, not
                // inspecting a formation.
                _boundaryPanel.Opened = () =>
                {
                    _selection.Select(null);
                    if (_frontlinePanel != null) _frontlinePanel.Hide();
                    CloseMissilePanel();
                };
            });

            // Settings for the automatic front line. Reached by clicking the
            // line itself — see FrontlinePanelUI for why it is not a rail
            // section like everything else.
            BuildStep("front line options", () =>
            {
                _frontlinePanel = FrontlinePanelUI.Create(canvas, _frontline);
                _frontlinePanel.Opened = () =>
                {
                    if (_boundaryPanel != null) _boundaryPanel.Hide();
                    CloseMissilePanel();
                };
                _selection.LineClicked = line =>
                {
                    if (line == null || line.Data.id != FrontlineSystem.LineId) return;
                    _frontlinePanel.Show();
                    _hud.Flash("Front line — derived from where the formations stand.");
                };
            });

            BuildStep("missile systems", () =>
            {
                _missilePanel = MissilePanelUI.Create(canvas, _missiles);
                _missilePanel.Opened = () =>
                {
                    // The board docks where the rail's section panel docks, so
                    // opening it closes that panel; nothing on the right-hand
                    // edge is disturbed any more, which means a formation can
                    // stay selected while a launcher is being chosen.
                    if (_palette != null)
                    {
                        _palette.ClosePanel();
                        _palette.SetMissilePanelOpen(true);
                    }
                };
                // The on-map zoom cluster rides whichever left-hand board is up.
                _missilePanel.LeftInsetChanged = inset =>
                {
                    if (_mapControls == null) return;
                    if (inset > 0f) _mapControls.SetLeftInset(inset);
                    else if (_palette != null) _palette.ReassertMapInset();
                };
            });

            BuildStep("unit palette", () =>
            {
                _palette = gameObject.AddComponent<UnitPaletteUI>();
                _palette.Build(canvas, _map, _rig.Cam, _rig, _clock, _weather, _effects,
                    _artillery, _airStrike, _uavStrike, _naval, _mapControls, _drawTool);
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
                _palette.MissileSystemsRequested = () =>
                {
                    if (_missilePanel == null) return;
                    _missilePanel.Toggle();
                    _palette.SetMissilePanelOpen(MissilePanelUI.IsOpen);
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

                // Bottom tool strip.
                // CONTROL MEASURES section → the docked options panel on the right.
                _palette.ControlMeasureRequested = kind =>
                {
                    if (_boundaryPanel == null) return;
                    _drawTool.PendingKind = kind;
                    _boundaryPanel.Show(kind);
                };

                _palette.SelectToolRequested = () => _drawTool.CancelDrawing();
                _palette.BoundaryToolRequested = () => _drawTool.StartDrawing(LineDrawTool.Mode.Boundary);
                _palette.DefensiveLineToolRequested = () => _drawTool.StartDrawing(LineDrawTool.Mode.DefensiveLine);
                // The draw tool also exits on its own (Esc, or finishing a line);
                // keep the strip's latched button honest when it does.
                _drawTool.ModeChanged += mode =>
                {
                    if (mode == LineDrawTool.Mode.None) _palette.ResetToolToSelect();
                };

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
                    _drawTool.CancelDrawing();
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
                }

                // DEPLOYED list.
                _palette.SelectUnitRequested = u => _selection.Select(u);
                _palette.RemoveUnitRequested = u =>
                {
                    RecordRemoval(u);
                    _selection.Select(null);
                    u.RemoveFromMap();
                };
            });

            BuildStep("unit info panel", () =>
            {
                _infoPanel = gameObject.AddComponent<UnitInfoPanel>();
                _infoPanel.Build(canvas);
                _infoPanel.RemoveRequested = u =>
                {
                    RecordRemoval(u);
                    _selection.Select(null);
                    u.RemoveFromMap();
                };
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
                _actionBar.MoveRequested = () => _selection.ArmMoveOrder();
                _actionBar.DefendRequested = () => _defence.Defend(_selection.Selected);
                _actionBar.HoldRequested = () => _defence.Hold(_selection.Selected);
                _actionBar.GuardRequested = () => _defence.Guard(_selection.Selected);
                _selection.MoveOrderResolved = () => _actionBar.ClearMoveArmed();

                // Attack: the bar picks the task, the next map click picks the
                // target, and the order system does the rest.
                _actionBar.AttackRequested = task => _selection.ArmAttackOrder(task);
                _selection.AttackTargetPicked = (target, task) =>
                    _attacks.Order(_selection.Selected, target, task);
                _selection.AttackOrderResolved = () => _actionBar.ClearAttackArmed();

                // Recon: same shape, but the map click is a point on the ground
                // rather than an enemy formation.
                _actionBar.ReconRequested = task => _selection.ArmReconOrder(task);
                _selection.ReconPointPicked = (lat, lon, task) =>
                    _recon.Order(_selection.Selected, lat, lon, task);
                _selection.ReconOrderResolved = () => _actionBar.ClearReconArmed();
                // The order bar belongs to game mode; leaving battle puts the
                // editor back in charge.
                _combat.RunningChanged += _ => RefreshActionBar();
            });

            _selection.SelectionChanged = sel =>
            {
                bool infoPanelOpen = sel.Count == 1 && sel[0] != null;
                // The info panel and the front line panel share the right-hand
                // edge; selecting a formation is a clear statement about which
                // of the two you now want. The missile board is *not* taken
                // down with them — it docks on the left, so a launcher can stay
                // chosen while a formation is inspected on the right.
                if (infoPanelOpen && _frontlinePanel != null) _frontlinePanel.Hide();
                if (_infoPanel != null) _infoPanel.Show(infoPanelOpen ? sel[0] : null);
                if (_groupPanel != null) _groupPanel.SetSelection(sel);
                // The compass lives in the bottom-right corner and steps aside
                // for the info panel rather than being parked inboard of a panel
                // that is usually not there.
                if (_mapControls != null)
                    _mapControls.SetRightInset(infoPanelOpen ? UiTheme.RightPanelWidth : 0f);
                UpdateRangeRings(sel);
                RefreshActionBar();
            };

            BuildStep("pause menu", () =>
            {
                _pauseMenu = gameObject.AddComponent<PauseMenuUI>();
                _pauseMenu.Build(canvas);
                _pauseMenu.BlockOpen = () => _drawTool.Current != LineDrawTool.Mode.None || _selection.Selected != null;
                _pauseMenu.SaveRequested = SaveMap;
                _pauseMenu.LoadRequested = LoadMap;
                // EXIT goes back where the player came in from. Dropping a
                // mission player at the main menu would make them walk the
                // campaign browser again to retry the mission they just left.
                if (_mission != null) _pauseMenu.ExitScene = GameConfig.SceneSinglePlayer;
                _pauseMenu.ResumeTimeScale = () => _clock.DesiredTimeScale;
                _rig.InputBlocked = () => Loading || DateTimeDialog.IsOpen ||
                                          ConfirmDialog.IsOpen ||
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
        /// The order bar is only meaningful in game mode with a single unit
        /// selected — in editor mode right-click repositions instead.
        /// </summary>
        void RefreshActionBar()
        {
            if (_actionBar == null) return;
            var sel = _selection.Selection;
            if (_combat.Running && sel.Count == 1 && sel[0] != null && sel[0].IsAlive)
                _actionBar.Show(sel[0]);
            else
                _actionBar.Hide();
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
            // Both are opened from the rail, which has just gone. Closing them
            // is belt and braces — a panel left up with nothing to close it
            // would sit over the map for the rest of the mission.
            if (_boundaryPanel != null) _boundaryPanel.Hide();
            if (_missilePanel != null) _missilePanel.Hide();
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
                _hud.Flash("Select or create a mission before setting its area.");
                return false;
            }
            if (_mission != picked)
            {
                _hud.Flash($"Open '{picked.name}' in the editor first — an area is drawn on its own ground.");
                return false;
            }

            if (picked.area == null) picked.area = new Data.MissionArea();
            if (_areaTool.Area != picked.area) _areaTool.Show(picked.area);
            return true;
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

        /// <summary>Takes every unit, effect and undo step off the map, ready for a fresh load.</summary>
        void ClearMapContents()
        {
            _combat.SetRunning(false);
            _attacks.CancelAll();
            _recon.CancelAll();
            _selection.Select(null);

            foreach (var a in new System.Collections.Generic.List<UnitActor>(UnitRegistry.All))
                if (a != null) Destroy(a.gameObject);
            UnitRegistry.Clear();

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
            _drawTool.CancelDrawing();
            _effects.Cancel();
            _missiles.Cancel();
            _naval.Cancel();
            _artillery.Cancel();
            _airStrike.Cancel();
            _uavStrike.Cancel();
            _selection.Select(null);
            if (_frontlinePanel != null) _frontlinePanel.Hide();
            CloseMissilePanel();

            // Editor settings, and the panel lamps that report them.
            _fog.SetEnabled(false);
            _showLineOfSight = true;
            _showWeaponRange = true;
            _sectors.AutoUpdate = false;
            _sectors.ClearAll();
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
            // After the units: a task marker whose owning unit is not on the map
            // is swept away, and during a load that is briefly all of them.
            _markers.LoadFrom(data.markers);
        }

        void Update()
        {
            TickMissionAutoStart();

            if (Input.GetKeyDown(KeyCode.F5)) SaveMap();
            if (Input.GetKeyDown(KeyCode.F9)) LoadMap();

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

        /// <summary>Takes the missile board down and un-lights its nav row with it.</summary>
        void CloseMissilePanel()
        {
            if (_missilePanel != null) _missilePanel.Hide();
            if (_palette != null) _palette.SetMissilePanelOpen(false);
        }

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
