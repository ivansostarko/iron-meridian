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
        public string mapFileName = "lyon_dev.json";

        MapManager _map;
        CameraRig _rig;
        SelectionManager _selection;
        LineManager _lines;
        MarkerManager _markers;
        LineDrawTool _drawTool;
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

        // Latest countdown reported by each strike system. A null title means
        // that system has nothing in the air; see RefreshStrikeBanner.
        (string title, float remaining, float total, Color colour) _artilleryBanner;
        (string title, float remaining, float total, Color colour) _airStrikeBanner;
        (string title, float remaining, float total, Color colour) _uavStrikeBanner;

        /// <summary>
        /// Shows whichever strike is closest to landing. Ties are impossible in
        /// practice and harmless if they happen — either is the right answer.
        /// </summary>
        void RefreshStrikeBanner()
        {
            var pick = _artilleryBanner;

            foreach (var other in new[] { _airStrikeBanner, _uavStrikeBanner })
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
        UnitHoverTooltip _hoverTooltip;
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

        readonly System.Collections.Generic.List<UnitState> _clipboard =
            new System.Collections.Generic.List<UnitState>();
        double _clipboardCentreLat, _clipboardCentreLon;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            UnitRegistry.Clear();

            // Up first, on its own high-sorting canvas, so it covers the map and
            // the HUD built below while Cesium streams the terrain in.
            _loading = LoadingScreenUI.Show(GameConfig.GameName, "Preparing the operational map");

            _save = SaveSystem.LoadMap(mapFileName) ?? new MapSaveData
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

            EditHistory.Clear();

            _selection = gameObject.AddComponent<SelectionManager>();
            _selection.InputBlocked = () => Loading || DateTimeDialog.IsOpen ||
                                            ConfirmDialog.IsOpen ||
                                            _effects.IsArmed ||
                                            _artillery.IsArmed ||
                                            _airStrike.IsArmed ||
                                            _uavStrike.IsArmed ||
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

            // Identifying a counter should not cost a click. Built after the HUD
            // so it draws over it, and shown in both modes — the information is
            // as useful when laying a scenario out as when fighting it.
            _hoverTooltip = UnitHoverTooltip.Create(canvas);
            _selection.HoverChanged = u => _hoverTooltip.Show(u);
            _selection.Flash = _hud.Flash;
            _effects.Flash = _hud.Flash;
            _artillery.Flash = _hud.Flash;
            _airStrike.Flash = _hud.Flash;
            _uavStrike.Flash = _hud.Flash;

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
                _boundaryPanel.Opened = () => _selection.Select(null);
            });

            BuildStep("unit palette", () =>
            {
                _palette = gameObject.AddComponent<UnitPaletteUI>();
                _palette.Build(canvas, _map, _rig.Cam, _rig, _clock, _weather, _effects,
                    _artillery, _airStrike, _uavStrike, _mapControls, _drawTool);
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
            });

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

        // ------------------------------------------------------- save/load
        void SaveMap()
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
            _selection.Select(null);

            // Editor settings, and the panel lamps that report them.
            _fog.SetEnabled(false);
            _showLineOfSight = true;
            _sectors.AutoUpdate = false;
            _sectors.ClearAll();
            if (_palette != null) _palette.SyncGeneralToggles(false, false, true);
            UnitActor.SetLabelScale(1f);
            _clock.SetSpeed(GameClock.NormalSpeed);
            _mapControls.SetControlsVisible(true);
            _mapControls.SetCompassVisible(false);

            var shipped = SaveSystem.LoadShippedMap(mapFileName);
            if (shipped == null)
            {
                _hud.Flash("Reset failed — the shipped scenario could not be read.");
                return;
            }

            _save = shipped;
            foreach (var a in new System.Collections.Generic.List<UnitActor>(UnitRegistry.All))
                if (a != null) Destroy(a.gameObject);
            UnitRegistry.Clear();
            if (_vfx != null) _vfx.StopAll();
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

            _weaponRing.Show(u.State.latitude, u.State.longitude, u.Def.weaponRangeKm);
            _rangeRingLat = u.State.latitude;
            _rangeRingLon = u.State.longitude;
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
    }
}
