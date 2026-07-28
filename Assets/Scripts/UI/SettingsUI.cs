using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Audio;

namespace IronMeridian.UI
{
    /// <summary>
    /// Settings screen with two tabs:
    ///   VIDEO — resolution, window mode, vsync
    ///   AUDIO — master volume slider for the whole game
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        RectTransform _videoTab, _audioTab;
        Button _videoTabBtn, _audioTabBtn;

        Resolution[] _resolutions;
        int _selResolution;
        int _selWindowMode;

        static readonly string[] WindowModes = { "Fullscreen (Borderless)", "Exclusive Fullscreen", "Windowed" };

        void Start()
        {
            AudioManager.Apply();
            var canvas = UIFactory.CreateCanvas("SettingsCanvas");

            var bg = UIFactory.CreatePanel(canvas.transform, "Background", GameConfig.UiBackground);
            UIFactory.Stretch(bg);

            var title = UIFactory.CreateText(canvas.transform, "SETTINGS", 56,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(600, 80));

            var back = UIFactory.CreateButton(canvas.transform, "< BACK",
                () => SceneManager.LoadScene(GameConfig.SceneMainMenu),
                GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80, -70), new Vector2(180, 60));

            // ---- Tab bar ----
            var tabBar = UIFactory.CreateGroup(canvas.transform, "TabBar");
            UIFactory.Place(tabBar, new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(1200, 64));

            _videoTabBtn = UIFactory.CreateButton(tabBar, "VIDEO SETTINGS", () => SelectTab(0), GameConfig.UiPanel, null, 26);
            UIFactory.Place((RectTransform)_videoTabBtn.transform, new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(596, 64));

            _audioTabBtn = UIFactory.CreateButton(tabBar, "AUDIO SETTINGS", () => SelectTab(1), GameConfig.UiPanel, null, 26);
            UIFactory.Place((RectTransform)_audioTabBtn.transform, new Vector2(0f, 0.5f), new Vector2(604, 0), new Vector2(596, 64));

            // ---- Tab pages ----
            var page = UIFactory.CreatePanel(canvas.transform, "Page", GameConfig.UiPanel);
            UIFactory.Place(page, new Vector2(0.5f, 1f), new Vector2(0, -252), new Vector2(1200, 620));

            _videoTab = BuildVideoTab(page);
            _audioTab = BuildAudioTab(page);
            SelectTab(0);
        }

        // ------------------------------------------------ VIDEO
        RectTransform BuildVideoTab(Transform parent)
        {
            var tab = UIFactory.CreateGroup(parent, "VideoTab");
            UIFactory.Stretch(tab);

            _resolutions = Screen.resolutions
                .GroupBy(r => (r.width, r.height))
                .Select(g => g.Last())
                .OrderByDescending(r => r.width * r.height)
                .ToArray();
            if (_resolutions.Length == 0) _resolutions = new[] { Screen.currentResolution };

            _selResolution = System.Array.FindIndex(_resolutions,
                r => r.width == Screen.width && r.height == Screen.height);
            if (_selResolution < 0) _selResolution = 0;

            _selWindowMode = Screen.fullScreenMode switch
            {
                FullScreenMode.ExclusiveFullScreen => 1,
                FullScreenMode.Windowed => 2,
                _ => 0
            };

            Row(tab, 0, "Resolution", row =>
            {
                var dd = UIFactory.CreateDropdown(row,
                    _resolutions.Select(r => $"{r.width} x {r.height}").ToList(),
                    _selResolution, i => _selResolution = i);
                UIFactory.Place((RectTransform)dd.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(420, 56));
            });

            Row(tab, 1, "Window mode", row =>
            {
                var dd = UIFactory.CreateDropdown(row, WindowModes.ToList(),
                    _selWindowMode, i => _selWindowMode = i);
                UIFactory.Place((RectTransform)dd.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(420, 56));
            });

            Row(tab, 2, "V-Sync", row =>
            {
                var tg = UIFactory.CreateToggle(row, "", QualitySettings.vSyncCount > 0,
                    on => QualitySettings.vSyncCount = on ? 1 : 0);
                UIFactory.Place((RectTransform)tg.transform, new Vector2(1f, 0.5f), new Vector2(-386, 0), new Vector2(420, 56));
            });

            var apply = UIFactory.CreateButton(tab, "APPLY", ApplyVideo, GameConfig.UiAccent,
                new Color(0.1f, 0.1f, 0.1f), 26);
            UIFactory.Place((RectTransform)apply.transform, new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(260, 70));
            return tab;
        }

        void ApplyVideo()
        {
            var res = _resolutions[Mathf.Clamp(_selResolution, 0, _resolutions.Length - 1)];
            var mode = _selWindowMode switch
            {
                1 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow
            };
            Screen.SetResolution(res.width, res.height, mode);
            PlayerPrefs.SetInt("im.res.w", res.width);
            PlayerPrefs.SetInt("im.res.h", res.height);
            PlayerPrefs.SetInt("im.res.mode", (int)mode);
            Debug.Log($"[Settings] Applied {res.width}x{res.height} {mode}");
        }

        // ------------------------------------------------ AUDIO
        RectTransform BuildAudioTab(Transform parent)
        {
            var tab = UIFactory.CreateGroup(parent, "AudioTab");
            UIFactory.Stretch(tab);

            Text pctLabel = null;
            Row(tab, 0, "Master volume", row =>
            {
                var slider = UIFactory.CreateSlider(row, AudioManager.MasterVolume, v =>
                {
                    AudioManager.MasterVolume = v;
                    if (pctLabel != null) pctLabel.text = $"{Mathf.RoundToInt(v * 100)}%";
                });
                UIFactory.Place((RectTransform)slider.transform, new Vector2(1f, 0.5f), new Vector2(-90, 0), new Vector2(360, 44));

                pctLabel = UIFactory.CreateText(row, $"{Mathf.RoundToInt(AudioManager.MasterVolume * 100)}%",
                    26, GameConfig.UiAccent, TextAnchor.MiddleRight);
                UIFactory.Place(pctLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-4, 0), new Vector2(80, 44));
            });

            var hint = UIFactory.CreateText(tab,
                "Controls the volume of the whole game (UI, effects and future music).",
                22, GameConfig.UiTextDim);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -220), new Vector2(1000, 60));
            return tab;
        }

        // ------------------------------------------------ shared
        void Row(Transform tab, int index, string label, System.Action<RectTransform> build)
        {
            var row = UIFactory.CreatePanel(tab, "Row_" + label, new Color(1, 1, 1, 0.03f));
            UIFactory.Place(row, new Vector2(0.5f, 1f), new Vector2(0, -40 - index * 100), new Vector2(1080, 80));

            var txt = UIFactory.CreateText(row, label, 26, null, TextAnchor.MiddleLeft);
            UIFactory.Stretch(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(30, 0);

            var holder = UIFactory.CreateGroup(row, "Control");
            holder.anchorMin = new Vector2(1, 0.5f); holder.anchorMax = new Vector2(1, 0.5f);
            holder.pivot = new Vector2(1, 0.5f);
            holder.anchoredPosition = new Vector2(-30, 0);
            holder.sizeDelta = new Vector2(460, 80);
            build(holder);
        }

        void SelectTab(int i)
        {
            _videoTab.gameObject.SetActive(i == 0);
            _audioTab.gameObject.SetActive(i == 1);
            _videoTabBtn.image.color = i == 0 ? GameConfig.UiPanelLight : GameConfig.UiPanel;
            _audioTabBtn.image.color = i == 1 ? GameConfig.UiPanelLight : GameConfig.UiPanel;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneMainMenu);
        }
    }
}
