using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Top command bar of the game screen: navigation, 2D/3D switch, line
    /// drawing tools, save/load and battle control.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        MapManager _map;
        CameraRig _rig;
        LineDrawTool _drawTool;
        LineManager _lines;
        CombatSystem _combat;
        System.Action _saveAction, _loadAction;

        Button _viewBtn, _battleBtn, _boundaryBtn, _defLineBtn, _line3DBtn;
        Text _status;
        bool _lines3D = true;

        public void Build(Canvas canvas, MapManager map, CameraRig rig, LineDrawTool drawTool,
            LineManager lines, CombatSystem combat, System.Action save, System.Action load)
        {
            _map = map; _rig = rig; _drawTool = drawTool; _lines = lines; _combat = combat;
            _saveAction = save; _loadAction = load;

            var bar = UIFactory.CreatePanel(canvas.transform, "TopBar", GameConfig.UiPanel);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(0, -70);
            bar.offsetMax = Vector2.zero;

            float x = 10;
            Btn(bar, ref x, 130, "< MENU", () => SceneManager.LoadScene(GameConfig.SceneMainMenu));

            var title = UIFactory.CreateText(bar, "DEV — LYON", 24, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(x + 10, 0), new Vector2(220, 50));
            x += 240;

            _viewBtn = Btn(bar, ref x, 110, "VIEW: 3D", ToggleView);
            _boundaryBtn = Btn(bar, ref x, 190, "DRAW BOUNDARY", () => ToggleDraw(LineDrawTool.Mode.Boundary));
            _defLineBtn = Btn(bar, ref x, 210, "DRAW DEFENSIVE LINE", () => ToggleDraw(LineDrawTool.Mode.DefensiveLine));
            _line3DBtn = Btn(bar, ref x, 130, "LINES: 3D", ToggleLines3D);
            Btn(bar, ref x, 110, "SAVE", () => _saveAction?.Invoke());
            Btn(bar, ref x, 110, "LOAD", () => _loadAction?.Invoke());

            _battleBtn = UIFactory.CreateButton(bar, "▶ START BATTLE", () => _combat.Toggle(),
                new Color(0.16f, 0.45f, 0.2f), GameConfig.UiText, 22);
            UIFactory.Place((RectTransform)_battleBtn.transform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(210, 50));

            _status = UIFactory.CreateText(canvas.transform, "", 20, GameConfig.UiAccent);
            UIFactory.Place(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 34));

            _combat.RunningChanged += OnBattleChanged;
            _drawTool.ModeChanged += OnDrawModeChanged;
            _map.ViewModeChanged += OnViewModeChanged;

            var help = UIFactory.CreateText(canvas.transform,
                "LMB select unit / add line point   •   RMB move order / finish line   •   WASD pan   •   Wheel zoom   •   Q/E rotate   •   MMB orbit",
                17, GameConfig.UiTextDim);
            UIFactory.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(1400, 30));
        }

        Button Btn(RectTransform bar, ref float x, float w, string label,
            UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(bar, label, action, GameConfig.UiPanelLight, null, 20);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(w, 50));
            ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
            x += w + 8;
            return b;
        }

        void ToggleView()
        {
            _map.ToggleViewMode();
            _rig.SetMode(_map.ViewMode);
        }

        void OnViewModeChanged(ViewMode mode)
        {
            _viewBtn.GetComponentInChildren<Text>().text =
                mode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D";
        }

        void ToggleDraw(LineDrawTool.Mode mode)
        {
            if (_drawTool.Current == mode) _drawTool.CancelDrawing();
            else _drawTool.StartDrawing(mode);
        }

        void OnDrawModeChanged(LineDrawTool.Mode mode)
        {
            _boundaryBtn.image.color = mode == LineDrawTool.Mode.Boundary
                ? GameConfig.UiAccent : GameConfig.UiPanelLight;
            _defLineBtn.image.color = mode == LineDrawTool.Mode.DefensiveLine
                ? GameConfig.UiAccent : GameConfig.UiPanelLight;
            _status.text = mode switch
            {
                LineDrawTool.Mode.Boundary => "Drawing BOUNDARY — LMB add point, RMB/Enter finish, Esc cancel",
                LineDrawTool.Mode.DefensiveLine => "Drawing DEFENSIVE LINE — LMB add point, RMB/Enter finish, Esc cancel",
                _ => ""
            };
        }

        void ToggleLines3D()
        {
            _lines3D = !_lines3D;
            _drawTool.Draw3D = _lines3D;
            _lines.SetAll3D(_lines3D);
            _line3DBtn.GetComponentInChildren<Text>().text = _lines3D ? "LINES: 3D" : "LINES: 2D";
        }

        void OnBattleChanged(bool running)
        {
            _battleBtn.GetComponentInChildren<Text>().text = running ? "■ PAUSE BATTLE" : "▶ START BATTLE";
            _battleBtn.image.color = running
                ? new Color(0.55f, 0.32f, 0.12f) : new Color(0.16f, 0.45f, 0.2f);
        }

        public void Flash(string message) => _status.text = message;
    }
}
