using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.UI;
using IronMeridian.Units;
using IronMeridian.Vfx;

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
        LineDrawTool _drawTool;
        FrontlineSystem _frontline;
        SectorSystem _sectors;
        CombatSystem _combat;
        VfxSystem _vfx;
        GameClock _clock;
        GameHUD _hud;
        UnitPaletteUI _palette;
        UnitInfoPanel _infoPanel;
        GroupPanelUI _groupPanel;
        UnitActionBarUI _actionBar;
        PauseMenuUI _pauseMenu;
        MapSaveData _save;

        RangeRing _viewRing, _weaponRing;
        UnitActor _rangeRingUnit;
        double _rangeRingLat, _rangeRingLon;

        readonly System.Collections.Generic.List<UnitState> _clipboard =
            new System.Collections.Generic.List<UnitState>();
        double _clipboardCentreLat, _clipboardCentreLon;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            UnitRegistry.Clear();

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

            _drawTool = gameObject.AddComponent<LineDrawTool>();
            _drawTool.Init(_map, _rig.Cam, _lines);

            _frontline = gameObject.AddComponent<FrontlineSystem>();
            _frontline.Init(_lines);

            _sectors = gameObject.AddComponent<SectorSystem>();
            _sectors.Init(_lines, _map.Georeference);

            // Effects must exist before any unit spawns — a unit restored below
            // strength starts burning the moment it is built.
            _vfx = gameObject.AddComponent<VfxSystem>();
            _vfx.Init(_map.Georeference);

            _combat = gameObject.AddComponent<CombatSystem>();

            // Clock runs only in game mode; the editor is timeless.
            _clock = gameObject.AddComponent<GameClock>();
            _combat.RunningChanged += _clock.SetRunning;

            EditHistory.Clear();

            _selection = gameObject.AddComponent<SelectionManager>();
            _selection.InputBlocked = () => _drawTool.Current != LineDrawTool.Mode.None;
            _selection.BattleRunning = () => _combat.Running;

            // --- UI ---
            var canvas = UIFactory.CreateCanvas("GameCanvas");
            _selection.Init(_map, _rig.Cam, canvas);

            _hud = gameObject.AddComponent<GameHUD>();
            _hud.Build(canvas, _combat, _clock);
            _map.LoadError += _hud.Flash;
            _selection.Flash = _hud.Flash;

            // Each panel is built in isolation: the whole UI is constructed at
            // runtime, so an exception in one builder used to abort the rest of
            // Start() — silently leaving the info panel, range rings, selection
            // wiring and pause menu unbuilt with no clue as to why.
            BuildStep("unit palette", () =>
            {
                _palette = gameObject.AddComponent<UnitPaletteUI>();
                _palette.Build(canvas, _map, _rig.Cam, _rig);
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
                _viewRing = RangeRing.Create(_map.Georeference, _map.Georeference.transform,
                    GameConfig.ViewRangeColor, 12f, "Max view");
                _weaponRing = RangeRing.Create(_map.Georeference, _map.Georeference.transform,
                    GameConfig.WeaponRangeColor, 12f, "Max weapon range");
            });

            BuildStep("unit action bar", () =>
            {
                _actionBar = gameObject.AddComponent<UnitActionBarUI>();
                _actionBar.Build(canvas);
                _actionBar.Flash = _hud.Flash;
                _actionBar.MoveRequested = () => _selection.ArmMoveOrder();
                _selection.MoveOrderResolved = () => _actionBar.ClearMoveArmed();
                // The order bar belongs to game mode; leaving battle puts the
                // editor back in charge.
                _combat.RunningChanged += _ => RefreshActionBar();
            });

            _selection.SelectionChanged = sel =>
            {
                if (_infoPanel != null) _infoPanel.Show(sel.Count == 1 ? sel[0] : null);
                if (_groupPanel != null) _groupPanel.SetSelection(sel);
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
                _rig.InputBlocked = () => _pauseMenu.IsOpen;
            });

            // --- content ---
            BuildStep("map content", () =>
            {
                ApplySave(_save);
                _map.ViewModeChanged += mode => _rig.SetMode(mode);
                _map.SetViewMode(_save.viewMode == "Mode2D" ? ViewMode.Mode2D : ViewMode.Mode3D);
                _map.SetMapStyle(System.Enum.TryParse(_save.mapStyle, out MapStyle style) ? style : MapStyle.Satellite);
            });
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
            _save.viewMode = _map.ViewMode.ToString();
            _save.mapStyle = _map.Style.ToString();
            string path = SaveSystem.SaveMap(_save, mapFileName);
            _hud.Flash($"Saved -> {path}");
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
            foreach (var u in data.units)
                UnitActor.Spawn(_map.Georeference, u);
            _lines.LoadFrom(data.lines);
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
            if (_rangeRingUnit != null && _viewRing != null && _weaponRing != null)
            {
                if (!_rangeRingUnit.IsAlive)
                {
                    _viewRing.Hide(); _weaponRing.Hide(); _rangeRingUnit = null;
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
            if (_viewRing == null || _weaponRing == null) return;
            if (sel.Count != 1 || sel[0] == null || !sel[0].IsAlive)
            {
                _viewRing.Hide();
                _weaponRing.Hide();
                _rangeRingUnit = null;
                return;
            }
            _rangeRingUnit = sel[0];
            ShowRangeRingsForCurrentUnit();
        }

        void ShowRangeRingsForCurrentUnit()
        {
            if (_viewRing == null || _weaponRing == null) return;
            var u = _rangeRingUnit;
            _viewRing.Show(u.State.latitude, u.State.longitude, u.Def.viewRangeKm);
            _weaponRing.Show(u.State.latitude, u.State.longitude, u.Def.weaponRangeKm);
            _rangeRingLat = u.State.latitude;
            _rangeRingLon = u.State.longitude;
        }
    }
}
