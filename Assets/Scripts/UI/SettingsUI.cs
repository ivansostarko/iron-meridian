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
    /// The settings screen: a **vertical tab rail** down the left, one page at a
    /// time on the right.
    ///
    /// **Why the tabs moved off the top.** Two 600 px slabs across the width of
    /// the screen spent a whole band of it saying "VIDEO" and "AUDIO", and left
    /// nowhere for a third to go — a horizontal bar is a shape that fits the
    /// number of tabs it was drawn for. A rail grows downward for nothing, puts
    /// the tab labels in a column the eye reads at a glance, and is the same
    /// device the map editor's own nav uses, so the two screens read as one
    /// interface.
    ///
    /// **Every control here does something.** A settings page whose switches
    /// write a preference and change nothing teaches the player that the screen
    /// is broken; each row below maps to a Unity setting that visibly bites, or
    /// to a mix level something actually reads — see
    /// <see cref="Core.DisplaySettings"/> and <see cref="AudioManager"/>.
    ///
    /// Video changes that cost a display mode switch — resolution and window
    /// mode — wait for **APPLY**. Everything else takes effect as it is moved,
    /// because the whole point of a shadow or volume control is hearing and
    /// seeing what you are choosing.
    ///
    /// Three pages: **VIDEO**, **AUDIO** and **CONTROLS**. The last is a
    /// reference rather than a rebinder — see <see cref="BuildControlsPage"/>.
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout

        /// <summary>Left inset of the tab rail, and the width of the rail itself.</summary>
        const float RailX = 80f, RailWidth = 300f;
        /// <summary>Top of the rail and of the page beside it, from the screen's top.</summary>
        const float ContentTop = 190f;
        /// <summary>Clear space under both, so neither runs into the bottom of the screen.</summary>
        const float ContentBottom = 60f;
        const float TabHeight = 64f, TabGap = 6f;
        /// <summary>Gap between the rail and the page.</summary>
        const float PageGap = 28f;
        /// <summary>Right inset of the page.</summary>
        const float PageRight = 80f;

        const float RowHeight = 72f, RowGap = 8f;
        /// <summary>Width of the control column at the right-hand end of a row.</summary>
        const float ControlWidth = 420f;

        /// <summary>One page and the tab that opens it.</summary>
        class Page
        {
            public string Title;
            public RectTransform Body;
            public Image Fill;
            public RectTransform Strip;
            public Text Label, Detail;
        }

        readonly List<Page> _pages = new List<Page>();

        Resolution[] _resolutions;
        int _selResolution;
        int _selWindowMode;

        static readonly string[] WindowModes = { "Fullscreen (borderless)", "Exclusive fullscreen", "Windowed" };
        static readonly string[] ShadowNames = { "Off", "Hard only", "Hard and soft" };
        static readonly string[] TextureNames = { "Full", "Half", "Quarter" };

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("SettingsCanvas");

            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Interior,
                BackgroundCatalog.DenseScreenScrim);

            var title = UIFactory.CreateText(canvas.transform, "SETTINGS", 56,
                UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(RailX, -70), new Vector2(600, 80));

            var sub = UIFactory.CreateText(canvas.transform,
                "Display, audio and the controls. Changes apply as you make them, except resolution and window mode.",
                20, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(RailX, -126), new Vector2(1100, 28));

            UIFactory.CreateBackButton(canvas.transform, "BACK TO MAIN MENU",
                () => SceneManager.LoadScene(GameConfig.SceneMainMenu),
                new Vector2(1f, 1f), new Vector2(-80, -62), new Vector2(300, 62));

            BuildPage(canvas.transform, "VIDEO", "Resolution, quality and the frame rate", BuildVideoPage);
            BuildPage(canvas.transform, "AUDIO", "Master volume and the four channels under it", BuildAudioPage);
            BuildPage(canvas.transform, "CONTROLS", "Every key and mouse button, in one place", BuildControlsPage);

            SelectPage(0);
        }

        // ------------------------------------------------------- tabs + page

        /// <summary>
        /// One tab on the rail and the page it opens, built together so the two
        /// cannot get out of step — the rail's length is however many of these
        /// have been made.
        /// </summary>
        void BuildPage(Transform parent, string title, string detail, System.Action<RectTransform> build)
        {
            int index = _pages.Count;

            var frame = UIFactory.CreateBorderedPanel(parent, "Tab_" + title, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(RailX, -(ContentTop + index * (TabHeight + TabGap))),
                new Vector2(RailWidth, TabHeight));

            var btn = UIFactory.CreateButton(frame, "", () => SelectPage(index),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            // Accent strip down the leading edge, as on the main menu's rows and
            // the editor's nav — one device for "this is the active one".
            var strip = UIFactory.CreatePanel(frame, "Strip", UiTheme.Accent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.sizeDelta = new Vector2(4f, 0);
            strip.GetComponent<Image>().raycastTarget = false;

            var label = UIFactory.CreateText(frame, title, 22, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(label.rectTransform, 24f, 12f, RailWidth - 40f, 24f);
            UIFactory.Fit(label, 14);

            var note = UIFactory.CreateText(frame, detail, 13, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(note.rectTransform, 24f, 36f, RailWidth - 40f, 18f);
            UIFactory.Fit(note, 10);

            // The page itself: a scroll view, because the video list is longer
            // than a short window and a settings row that cannot be reached is
            // a setting that cannot be changed.
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform body,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(RailX + RailWidth + PageGap, ContentBottom);
            srt.offsetMax = new Vector2(-PageRight, -ContentTop);

            var layout = body.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = body.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            _rowY = 0f;
            build(body);
            body.sizeDelta = new Vector2(0, _rowY + 24f);

            _pages.Add(new Page
            {
                Title = title,
                Body = body,
                Fill = frame.Find("Fill").GetComponent<Image>(),
                Strip = strip,
                Label = label,
                Detail = note
            });
        }

        void SelectPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                var p = _pages[i];
                bool on = i == index;
                // The whole page's scroll view is the body's grandparent — the
                // viewport and the scrollbar have to go with it, or an inactive
                // page leaves its bar on the screen.
                p.Body.parent.parent.gameObject.SetActive(on);
                p.Fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                p.Strip.sizeDelta = new Vector2(on ? 8f : 4f, 0);
                p.Label.color = on ? Color.white : UiTheme.TextDim;
                p.Detail.color = on ? UiTheme.Text : UiTheme.TextFaint;
            }
        }

        // ------------------------------------------------------------- VIDEO

        void BuildVideoPage(RectTransform page)
        {
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

            Heading(page, "DISPLAY");

            Row(page, "Resolution", "Takes effect on APPLY", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder,
                    _resolutions.Select(r => $"{r.width} × {r.height}").ToList(),
                    _selResolution, i => _selResolution = i);
                Fill(dd.transform);
            });

            Row(page, "Window mode", "Takes effect on APPLY", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder, WindowModes.ToList(),
                    _selWindowMode, i => _selWindowMode = i);
                Fill(dd.transform);
            });

            var apply = UIFactory.CreateButton(page, "APPLY DISPLAY MODE", ApplyVideo,
                UiTheme.Accent, GameConfig.UiBackground, 20);
            UIFactory.Place((RectTransform)apply.transform, new Vector2(0f, 1f),
                new Vector2(0, -_rowY), new Vector2(320, 56));
            _rowY += 56f + RowGap * 2f;

            Heading(page, "QUALITY");

            var levels = QualitySettings.names != null && QualitySettings.names.Length > 0
                ? QualitySettings.names.ToList()
                : new List<string> { "Default" };

            Row(page, "Quality preset", "Sets shadows, anti-aliasing and the rest in one move", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder, levels,
                    Mathf.Clamp(DisplaySettings.QualityLevel, 0, levels.Count - 1), i =>
                    {
                        DisplaySettings.QualityLevel = i;
                        // A preset overwrites every individual setting below it,
                        // so the page has to be told what it now says — Apply
                        // pushes the stored values back over the preset.
                        DisplaySettings.Apply();
                    });
                Fill(dd.transform);
            });

            Row(page, "Anti-aliasing", "Smooths the edges of terrain and models", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder,
                    DisplaySettings.AntiAliasSamples.Select(s => s == 0 ? "Off" : $"{s}×").ToList(),
                    Mathf.Clamp(DisplaySettings.AntiAliasIndex, 0, DisplaySettings.AntiAliasSamples.Length - 1),
                    i => { DisplaySettings.AntiAliasIndex = i; DisplaySettings.Apply(); });
                Fill(dd.transform);
            });

            Row(page, "Shadows", "The most expensive setting on a wide view", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder, ShadowNames.ToList(),
                    Mathf.Clamp(DisplaySettings.ShadowLevel, 0, ShadowNames.Length - 1),
                    i => { DisplaySettings.ShadowLevel = i; DisplaySettings.Apply(); });
                Fill(dd.transform);
            });

            Row(page, "Texture quality", "Lower this first if the map is short of video memory", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder, TextureNames.ToList(),
                    Mathf.Clamp(DisplaySettings.TextureQuality, 0, TextureNames.Length - 1),
                    i => { DisplaySettings.TextureQuality = i; DisplaySettings.Apply(); });
                Fill(dd.transform);
            });

            Row(page, "Anisotropic filtering", "Keeps ground textures sharp at a shallow angle", holder =>
            {
                var tg = UIFactory.CreateToggle(holder, "", DisplaySettings.Anisotropic,
                    on => { DisplaySettings.Anisotropic = on; DisplaySettings.Apply(); });
                Box(tg.transform);
            });

            Heading(page, "FRAME RATE");

            Row(page, "V-Sync", "Matches the display's refresh rate and rules out tearing", holder =>
            {
                var tg = UIFactory.CreateToggle(holder, "", DisplaySettings.VSync,
                    on => { DisplaySettings.VSync = on; DisplaySettings.Apply(); });
                Box(tg.transform);
            });

            Row(page, "Frame-rate cap", "Ignored while V-Sync is on", holder =>
            {
                var dd = UIFactory.CreateDropdown(holder,
                    DisplaySettings.FrameCaps.Select(f => f == 0 ? "Uncapped" : $"{f} fps").ToList(),
                    Mathf.Clamp(DisplaySettings.FrameCapIndex, 0, DisplaySettings.FrameCaps.Length - 1),
                    i => { DisplaySettings.FrameCapIndex = i; DisplaySettings.Apply(); });
                Fill(dd.transform);
            });

            Note(page, "Terrain detail is streamed by Cesium and is not a quality setting — " +
                       "how much of the map is loaded depends on the camera's altitude, not on this page.");
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

        // ------------------------------------------------------------- AUDIO

        void BuildAudioPage(RectTransform page)
        {
            Heading(page, "MASTER");

            VolumeRow(page, "Master volume", "Everything the game plays",
                () => AudioManager.MasterVolume, v => AudioManager.MasterVolume = v);

            Heading(page, "CHANNELS");

            VolumeRow(page, "Music", "The menu and map bed",
                () => AudioManager.MusicVolume, v => AudioManager.MusicVolume = v);

            VolumeRow(page, "Weather ambience", "Rain, storm and snow beds",
                () => AudioManager.AmbienceVolume, v => AudioManager.AmbienceVolume = v);

            VolumeRow(page, "Effects", "Gunfire, explosions, aircraft and drones",
                () => AudioManager.EffectsVolume, v => AudioManager.EffectsVolume = v);

            VolumeRow(page, "Interface", "Button clicks and hovers",
                () => AudioManager.InterfaceVolume, v => AudioManager.InterfaceVolume = v);

            Heading(page, "INTERFACE");

            Row(page, "Hover sounds", "The sound a button makes under the cursor, on the menu screens", holder =>
            {
                var tg = UIFactory.CreateToggle(holder, "", AudioManager.HoverSounds,
                    on => AudioManager.HoverSounds = on);
                Box(tg.transform);
            });

            Note(page, "Each channel is a level under the master volume, so turning the music down does " +
                       "not turn the battle down with it. Hover sounds are always off in the map editor, " +
                       "where a hundred controls sit under a cursor on its way to the map.");
        }

        // ---------------------------------------------------------- CONTROLS

        /// <summary>
        /// Every control in the game, in one readable place.
        ///
        /// **It is a reference, not a rebinder.** The game reads
        /// <c>UnityEngine.Input</c> directly against fixed <c>KeyCode</c>s
        /// (golden rule 5), so there is nothing here to rebind yet — and a page
        /// of dropdowns that wrote a preference nothing reads would be worse
        /// than a page that is honest about what it is. What a player actually
        /// needs today is to find out that C sets a formation's facing, which
        /// nothing on the map screen says.
        ///
        /// **Grouped by what you are doing, not by device.** A list sorted by
        /// key is a list you can only use if you already know the key. The
        /// groups run in the order a session does: move the camera, pick
        /// something, order it, then the editor's own tools.
        ///
        /// Keys are drawn as **chips** — bordered caps, laid out from the right
        /// so every row's keys end on the same edge whatever their number and
        /// length. Alternatives inside one row are separated by a faint "or",
        /// so "W A S D" reads as one chord and "R / F" as two choices.
        /// </summary>
        void BuildControlsPage(RectTransform page)
        {
            Heading(page, "CAMERA");
            Control(page, "Pan the map", "Hold to keep moving", "W", "A", "S", "D");
            Control(page, "Pan the map", "The arrow keys do the same", "\u2191", "\u2193", "\u2190", "\u2192");
            Control(page, "Zoom in and out", "Wheel, or hold either key", "Wheel", "or", "R", "F");
            Control(page, "Rotate the view", "3D mode only \u2014 2D is locked north-up", "Q", "E");
            Control(page, "Orbit and tilt", "3D mode only", "Middle-drag");

            Heading(page, "SELECTION");
            Control(page, "Select a formation", "Clears anything already selected", "LMB");
            Control(page, "Box-select", "Every friendly formation inside the box", "LMB drag");
            Control(page, "Add or remove one", "Toggles that formation in the selection", "Shift", "LMB");
            Control(page, "Add a boxed group", "Keeps what is already selected", "Shift", "drag");
            Control(page, "Clear the selection", "", "Esc");

            Heading(page, "ORDERS");
            Control(page, "Move or march", "On bare ground. In battle it is a march order", "RMB");
            Control(page, "Add a waypoint", "Extends the march instead of replacing it", "Shift", "RMB");
            Control(page, "Open a formation's menu", "Right-click the counter rather than the ground", "RMB");
            Control(page, "Set facing", "Then aim with the mouse", "C");
            Control(page, "Confirm the facing", "", "LMB", "or", "Enter");
            Control(page, "Cancel an armed order", "Any order waiting for a click on the map", "Esc", "or", "RMB");

            Heading(page, "MAP EDITOR");
            Control(page, "Copy the selection", "", "Ctrl", "C");
            Control(page, "Paste", "Lands on the cursor, keeping the group's shape", "Ctrl", "V");
            Control(page, "Undo the last edit", "Placements, moves and facings", "Ctrl", "Z");
            Control(page, "Save the map", "", "F5");
            Control(page, "Load the map", "", "F9");
            Control(page, "Undo the last point", "While drawing a line or an area", "Backspace");
            Control(page, "Finish a drawing", "", "Enter", "or", "RMB");

            Heading(page, "SCREENS");
            Control(page, "Pause menu", "Either key opens and closes it", "Esc", "or", "P");
            Control(page, "Casualty list", "Battle mode, or once a battle has been fought", "Tab");
            Control(page, "Back out of a screen", "Closes a dialog first, if one is open", "Esc");

            Note(page, "Controls are fixed for now \u2014 the game reads the keyboard directly rather than through a " +
                       "binding table, so there is nothing here to rebind yet. The map screen carries the same " +
                       "shortcuts in a single line under the map.");
        }

        /// <summary>
        /// One control: what it does, an optional line saying when, and its keys
        /// as chips ending flush with the right-hand edge.
        ///
        /// The word "or" may be passed among the keys and is drawn as text
        /// rather than as a chip \u2014 it is the one thing in the row that is not
        /// something you press.
        /// </summary>
        void Control(RectTransform page, string action, string detail, params string[] keys)
        {
            Row(page, action, detail, holder =>
            {
                // Right to left: the chips end on the control column's edge, so
                // the whole page has one vertical line of keys however many each
                // row carries.
                float x = 0f;
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    if (keys[i] == "or")
                    {
                        var word = UIFactory.CreateText(holder, "or", 14, UiTheme.TextFaint,
                            TextAnchor.MiddleCenter);
                        UIFactory.Place(word.rectTransform, new Vector2(1f, 0.5f),
                            new Vector2(-x - OrWidth * 0.5f, 0), new Vector2(OrWidth, 20));
                        x += OrWidth + ChipGap;
                        continue;
                    }

                    x += Chip(holder, keys[i], x) + ChipGap;
                }
            });
        }

        const float ChipHeight = 32f, ChipGap = 6f, ChipPadX = 14f, ChipMinWidth = 38f;
        const float OrWidth = 26f;

        /// <summary>
        /// One key cap, placed <paramref name="fromRight"/> px in from the
        /// control column's right edge. Returns its width so the caller can step
        /// the next one along.
        ///
        /// Width is measured from the label rather than fixed: "Middle-drag" and
        /// "W" are both keys, and a column of equal-width caps would either clip
        /// the long one or leave the short one floating in a slab.
        /// </summary>
        float Chip(RectTransform holder, string key, float fromRight)
        {
            var frame = UIFactory.CreateBorderedPanel(holder, "Key_" + key,
                new Color(1f, 1f, 1f, 0.05f), UiTheme.BorderStrong);

            var label = UIFactory.CreateText(frame, key, 16, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(label.rectTransform);
            label.raycastTarget = false;

            float width = Mathf.Max(ChipMinWidth, label.preferredWidth + ChipPadX * 2f);
            UIFactory.Place(frame, new Vector2(1f, 0.5f),
                new Vector2(-fromRight, 0), new Vector2(width, ChipHeight));
            // Place puts the pivot at the anchor, so the frame hangs leftwards
            // from the column's right edge, which is what the right-to-left walk
            // in Control expects.
            return width;
        }

        // ------------------------------------------------------------ pieces

        /// <summary>Running Y as a page is filled, so the page can size itself at the end.</summary>
        float _rowY;

        void Heading(RectTransform page, string text)
        {
            var t = UIFactory.CreateText(page, UiTheme.Spaced(text), 15, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(4, -_rowY - 8f),
                new Vector2(700, 20));
            _rowY += 36f;
        }

        /// <summary>
        /// A labelled row with its control at the right-hand end. The label and
        /// its explanation are stacked on the left; the control column has a
        /// fixed width, so every control on the page lines up whatever the
        /// label beside it says.
        /// </summary>
        void Row(RectTransform page, string label, string detail, System.Action<RectTransform> build)
        {
            // Width comes from the page: the row is anchored to both its edges
            // and positioned by its offsets, so it follows the window instead
            // of being cut to a guess at how wide the page is.
            var frame = UIFactory.CreateBorderedPanel(page, "Row_" + label, UiTheme.Surface, UiTheme.Border);
            frame.anchorMin = new Vector2(0, 1); frame.anchorMax = new Vector2(1, 1);
            frame.pivot = new Vector2(0.5f, 1f);
            frame.offsetMin = new Vector2(0, -(_rowY + RowHeight));
            frame.offsetMax = new Vector2(0, -_rowY);

            var title = UIFactory.CreateText(frame, label, 22, UiTheme.Text, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(title.rectTransform, 24f, 14f, 520f, 24f);
            UIFactory.Fit(title, 14);

            if (!string.IsNullOrEmpty(detail))
            {
                var note = UIFactory.CreateText(frame, detail, 14, UiTheme.TextFaint, TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(note.rectTransform, 24f, 40f, 520f, 18f);
                UIFactory.Fit(note, 10);
            }

            var holder = UIFactory.CreateGroup(frame, "Control");
            holder.anchorMin = new Vector2(1, 0.5f); holder.anchorMax = new Vector2(1, 0.5f);
            holder.pivot = new Vector2(1, 0.5f);
            holder.anchoredPosition = new Vector2(-24, 0);
            holder.sizeDelta = new Vector2(ControlWidth, RowHeight - 16f);
            build(holder);

            _rowY += RowHeight + RowGap;
        }

        /// <summary>A slider and its percentage, the shape every level control on this page takes.</summary>
        void VolumeRow(RectTransform page, string label, string detail,
            System.Func<float> read, System.Action<float> write)
        {
            Row(page, label, detail, holder =>
            {
                Text pct = null;

                var slider = UIFactory.CreateSlider(holder, read(), v =>
                {
                    write(v);
                    if (pct != null) pct.text = $"{Mathf.RoundToInt(v * 100)}%";
                });
                UIFactory.Place((RectTransform)slider.transform, new Vector2(0f, 0.5f),
                    new Vector2(0, 0), new Vector2(ControlWidth - 80f, 44));

                pct = UIFactory.CreateText(holder, $"{Mathf.RoundToInt(read() * 100)}%",
                    20, UiTheme.Accent, TextAnchor.MiddleRight);
                UIFactory.Place(pct.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(70, 44));
            });
        }

        void Note(RectTransform page, string text)
        {
            var t = UIFactory.CreateText(page, text, 15, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(4, -_rowY - 10f),
                new Vector2(900, 70));
            _rowY += 84f;
        }

        /// <summary>Stretches a control across its holder — the shape most rows want.</summary>
        static void Fill(Transform control) =>
            UIFactory.Place((RectTransform)control, new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(ControlWidth, 52));

        /// <summary>
        /// A checkbox at the right-hand end of the row.
        ///
        /// Not <see cref="Fill"/>: a toggle's box is anchored to the left of its
        /// own rect, so a full-width one leaves the box marooned in the middle
        /// of the row, a control column away from the dropdowns it lines up with.
        /// </summary>
        static void Box(Transform control) =>
            UIFactory.Place((RectTransform)control, new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(34, 34));

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneMainMenu);
        }
    }
}
