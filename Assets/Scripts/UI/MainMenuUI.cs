using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// Main menu: Testing / Settings / Quit (with confirmation modal).
    /// Attach to an empty GameObject in the MainMenu scene — the whole UI is
    /// built at runtime.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        GameObject _quitModal;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("MainMenuCanvas");

            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default);

            // Decorative header band
            var band = UIFactory.CreatePanel(canvas.transform, "Band", GameConfig.UiPanel);
            UIFactory.Place(band, new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(1920, 200));

            var title = UIFactory.CreateText(canvas.transform, GameConfig.GameName.ToUpperInvariant(),
                92, GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(1400, 120));

            var subtitle = UIFactory.CreateText(canvas.transform,
                "REAL-TERRAIN OPERATIONAL WARGAME", 26, GameConfig.UiTextDim);
            UIFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -250), new Vector2(1000, 40));

            // Six entries now rather than three, so the buttons are shorter and
            // the block is anchored from its top rather than centred — a centred
            // group grows in both directions and would have walked up into the
            // title as entries were added.
            var menu = UIFactory.CreateGroup(canvas.transform, "Menu");
            UIFactory.Place(menu, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(460, 560));

            // Play modes first, tools second: what most people came to do goes
            // at the top, and QUIT stays last where it can't be hit by accident.
            MakeMenuButton(menu, "SINGLE PLAYER", 0, () => SceneManager.LoadScene(GameConfig.SceneSinglePlayer));
            MakeMenuButton(menu, "MULTIPLAYER", 1, () => SceneManager.LoadScene(GameConfig.SceneMultiplayer));
            MakeMenuButton(menu, "EXTRAS", 2, () => SceneManager.LoadScene(GameConfig.SceneExtras));
            MakeMenuButton(menu, "TESTING", 3, () => SceneManager.LoadScene(GameConfig.SceneTesting));
            MakeMenuButton(menu, "SETTINGS", 4, () => SceneManager.LoadScene(GameConfig.SceneSettings));
            MakeMenuButton(menu, "QUIT", 5, ShowQuitModal);

            var version = UIFactory.CreateText(canvas.transform,
                $"{GameConfig.GameName} {GameConfig.Version}", 18, GameConfig.UiTextDim);
            UIFactory.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-30, 24), new Vector2(400, 30));

            BuildQuitModal(canvas.transform);
        }

        void MakeMenuButton(Transform parent, string label, int index, UnityEngine.Events.UnityAction action)
        {
            var btn = UIFactory.CreateButton(parent, label, action, GameConfig.UiPanel, GameConfig.UiText, 28);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 1f), new Vector2(0, -index * 88), new Vector2(460, 74));

            // Accent strip on the left edge
            var strip = UIFactory.CreatePanel(rt, "Strip", GameConfig.UiAccent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = new Vector2(6, 0);
        }

        void BuildQuitModal(Transform canvas)
        {
            var overlay = UIFactory.CreatePanel(canvas, "QuitModal", new Color(0, 0, 0, 0.72f));
            UIFactory.Stretch(overlay);
            overlay.gameObject.AddComponent<Button>()  // click outside = cancel
                .onClick.AddListener(HideQuitModal);

            var box = UIFactory.CreatePanel(overlay, "Box", GameConfig.UiPanel);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 280));

            var txt = UIFactory.CreateText(box, "Quit Iron Meridian?", 36, null,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(560, 60));

            var sub = UIFactory.CreateText(box, "Any unsaved map changes will be lost.",
                22, GameConfig.UiTextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -115), new Vector2(560, 40));

            var yes = UIFactory.CreateButton(box, "QUIT", QuitGame,
                new Color(0.62f, 0.16f, 0.16f), GameConfig.UiText, 26);
            UIFactory.Place((RectTransform)yes.transform, new Vector2(0.5f, 0f), new Vector2(-125, 40), new Vector2(220, 70));

            var no = UIFactory.CreateButton(box, "CANCEL", HideQuitModal,
                GameConfig.UiPanelLight, GameConfig.UiText, 26);
            UIFactory.Place((RectTransform)no.transform, new Vector2(0.5f, 0f), new Vector2(125, 40), new Vector2(220, 70));

            _quitModal = overlay.gameObject;
            _quitModal.SetActive(false);
        }

        void ShowQuitModal() => _quitModal.SetActive(true);
        void HideQuitModal() => _quitModal.SetActive(false);

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_quitModal != null && _quitModal.activeSelf) HideQuitModal();
                else ShowQuitModal();
            }
        }
    }
}
