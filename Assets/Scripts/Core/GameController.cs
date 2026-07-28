using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.UI;
using IronMeridian.Units;

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
        CombatSystem _combat;
        GameHUD _hud;
        UnitPaletteUI _palette;
        UnitInfoPanel _infoPanel;
        MapSaveData _save;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
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

            _combat = gameObject.AddComponent<CombatSystem>();

            _selection = gameObject.AddComponent<SelectionManager>();
            _selection.Init(_map, _rig.Cam);
            _selection.InputBlocked = () => _drawTool.Current != LineDrawTool.Mode.None;

            // --- UI ---
            var canvas = UIFactory.CreateCanvas("GameCanvas");

            _hud = gameObject.AddComponent<GameHUD>();
            _hud.Build(canvas, _map, _rig, _drawTool, _lines, _combat, SaveMap, LoadMap);

            _palette = gameObject.AddComponent<UnitPaletteUI>();
            _palette.Build(canvas);
            _palette.DropRequested = OnPaletteDrop;

            _infoPanel = gameObject.AddComponent<UnitInfoPanel>();
            _infoPanel.Build(canvas);
            _selection.SelectionChanged = u => _infoPanel.Show(u);

            // --- content ---
            ApplySave(_save);
            _map.ViewModeChanged += mode => _rig.SetMode(mode);
            _map.SetViewMode(_save.viewMode == "Mode2D" ? ViewMode.Mode2D : ViewMode.Mode3D);
        }

        // ------------------------------------------------------- spawning
        void OnPaletteDrop(UnitDefinition def, Team team, Affiliation aff,
            Echelon echelon, Vector2 screenPos)
        {
            if (!_map.RaycastGround(_rig.Cam, screenPos, out Vector3 world))
            {
                _hud.Flash("Terrain not loaded here yet — try again in a moment.");
                return;
            }
            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);

            var state = new UnitState
            {
                instanceId = System.Guid.NewGuid().ToString("N").Substring(0, 10),
                defId = def.id,
                team = team.ToString(),
                affiliation = aff.ToString(),
                echelon = echelon.ToString(),
                customName = "",
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
            UnitActor.Spawn(_map.Georeference, state);
            _hud.Flash($"Deployed {echelon} {def.name} ({team})");
        }

        // ------------------------------------------------------- save/load
        void SaveMap()
        {
            _save.units.Clear();
            foreach (var a in UnitRegistry.All)
                if (a != null && a.IsAlive) _save.units.Add(a.Snapshot());
            _save.lines = _lines.Serialize();
            _save.viewMode = _map.ViewMode.ToString();
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
        }
    }
}
