using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// Esc/P pause overlay for the Game scene: resume, save, load, exit to
    /// main menu, exit to Windows. Freezes gameplay via Time.timeScale while
    /// open. Runs before other gameplay scripts (see DefaultExecutionOrder)
    /// so BlockOpen reads this frame's pre-Escape state, not the post-Escape
    /// state already consumed by SelectionManager/MissionAreaTool.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PauseMenuUI : MonoBehaviour
    {
        /// <summary>Return true when another system should consume this Escape press instead (drawing a line, unit selected).</summary>
        public System.Func<bool> BlockOpen;
        public System.Action SaveRequested;
        public System.Action LoadRequested;
        /// <summary>
        /// Time scale to restore on close. The game clock owns the player's
        /// chosen speed, so resuming must ask for it rather than assume 1x.
        /// </summary>
        public System.Func<float> ResumeTimeScale;

        /// <summary>
        /// Where EXIT TO MAIN MENU goes. The main menu from the map editor; the campaign
        /// browser from a mission, so leaving one drops the player back at the
        /// board they picked it from rather than making them walk the whole menu
        /// again to retry it.
        /// </summary>
        public string ExitScene = GameConfig.SceneMainMenu;

        GameObject _root;
        GameObject _quitModal;
        Text _status;

        public bool IsOpen => _root != null && _root.activeSelf;

        public void Build(Canvas canvas)
        {
            var overlay = UIFactory.CreatePanel(canvas.transform, "PauseMenu", new Color(0, 0, 0, 0.72f));
            UIFactory.Stretch(overlay);
            _root = overlay.gameObject;

            var box = UIFactory.CreatePanel(overlay, "Box", GameConfig.UiPanel);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 580));

            var title = UIFactory.CreateText(box, "PAUSED", 40, GameConfig.UiAccent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(400, 60));

            MenuButton(box, "RESUME GAME", 0, Resume);
            MenuButton(box, "SAVE", 1, DoSave);
            MenuButton(box, "LOAD", 2, DoLoad);
            // Both exits say where they go. "EXIT" beside "EXIT TO WINDOWS"
            // asked the player to infer the difference from the absence of a
            // destination, which is exactly the reading you do not want to get
            // wrong on a menu that can close the game.
            MenuButton(box, "EXIT TO MAIN MENU", 3, ExitToMainMenu);
            MenuButton(box, "EXIT TO WINDOWS", 4, ShowQuitModal);

            // Sits just below the last button (index 4 ends at -130-4*76-62 = -516).
            _status = UIFactory.CreateText(box, "", 16, GameConfig.UiTextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -534), new Vector2(420, 30));

            BuildQuitModal(overlay);
            _root.SetActive(false);
        }

        void MenuButton(Transform parent, string label, int index, UnityEngine.Events.UnityAction action)
        {
            var btn = UIFactory.CreateButton(parent, label, action, GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)btn.transform, new Vector2(0.5f, 1f),
                new Vector2(0, -130 - index * 76), new Vector2(400, 62));
            // "EXIT TO MAIN MENU" is the longest caption here and only just
            // clears 400 px at 24 pt; best-fit shrinks rather than overruns.
            UIFactory.Fit(btn.GetComponentInChildren<Text>(), 16);
        }

        void BuildQuitModal(Transform parent)
        {
            var overlay = UIFactory.CreatePanel(parent, "QuitConfirm", new Color(0, 0, 0, 0.6f));
            UIFactory.Stretch(overlay);
            overlay.gameObject.AddComponent<Button>().onClick.AddListener(HideQuitModal);

            var box = UIFactory.CreatePanel(overlay, "Box", GameConfig.UiPanel);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 240));

            var txt = UIFactory.CreateText(box, "Exit to Windows?", 30, null,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(500, 50));

            var sub = UIFactory.CreateText(box, "Any unsaved map changes will be lost.", 20, GameConfig.UiTextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(500, 36));

            var yes = UIFactory.CreateButton(box, "EXIT", ExitToWindows,
                new Color(0.62f, 0.16f, 0.16f), GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)yes.transform, new Vector2(0.5f, 0f), new Vector2(-110, 30), new Vector2(200, 60));

            var no = UIFactory.CreateButton(box, "CANCEL", HideQuitModal,
                GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)no.transform, new Vector2(0.5f, 0f), new Vector2(110, 30), new Vector2(200, 60));

            _quitModal = overlay.gameObject;
            _quitModal.SetActive(false);
        }

        void ShowQuitModal() => _quitModal.SetActive(true);
        void HideQuitModal() => _quitModal.SetActive(false);

        public void Open()
        {
            _root.SetActive(true);
            _status.text = "";
            Time.timeScale = 0f;
        }

        public void Close()
        {
            _root.SetActive(false);
            _quitModal.SetActive(false);
            Time.timeScale = ResumeTimeScale != null ? ResumeTimeScale() : 1f;
        }

        void Resume() => Close();

        void DoSave()
        {
            SaveRequested?.Invoke();
            _status.text = "Game saved.";
        }

        void DoLoad()
        {
            LoadRequested?.Invoke();
            _status.text = "Game loaded.";
        }

        void ExitToMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(ExitScene);
        }

        void ExitToWindows()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            // Don't let the pause hotkeys fire while the player is typing
            // (e.g. naming a unit group) — Escape just drops focus instead.
            if (IsTypingInInputField())
            {
                if (Input.GetKeyDown(KeyCode.Escape)) EventSystem.current.SetSelectedGameObject(null);
                return;
            }

            bool escape = Input.GetKeyDown(KeyCode.Escape);
            bool pKey = Input.GetKeyDown(KeyCode.P);

            if (IsOpen)
            {
                if (escape || pKey) Close();
                return;
            }

            if (pKey) { Open(); return; }   // P always toggles pause — no other system uses that key.

            if (escape)
            {
                if (BlockOpen != null && BlockOpen()) return;
                Open();
            }
        }

        static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var input = es.currentSelectedGameObject.GetComponent<InputField>();
            return input != null && input.isFocused;
        }
    }
}
