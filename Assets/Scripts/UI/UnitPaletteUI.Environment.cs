using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — the world around the battle: weather conditions, the operational clock and map/tileset settings.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: weather section, automatic day/night ---, conditions ---, date & time section, map section.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ----------------------------------------------------- weather section

        /// <summary>
        /// Two independent axes, because they genuinely are: SKY is the time of
        /// day, CONDITIONS is what is falling out of it. Folding them into one
        /// list would make a night storm unexpressible — and would leave the
        /// automatic day/night toggle fighting whatever weather was picked.
        /// </summary>
        /// <summary>Height the scenario-start block occupies inside the merged page.</summary>
        const float StartBlockHeight = 380f;
        /// <summary>And the weather block below it.</summary>
        const float WeatherBlockHeight = 480f;

        /// <summary>
        /// **ENVIRONMENT** — when the scenario is fought and what the weather is
        /// doing, on one page.
        ///
        /// These were two rail rows, WEATHER CONDITIONS and DATE AND TIME, and
        /// they were always one decision. A designer setting a night attack is
        /// choosing the hour *and* the sky in the same breath; the auto
        /// day/night switch reads the clock the other section owned; and a
        /// player asking "what will this look like" has to open both to find
        /// out. One row that answers the whole question is worth two that each
        /// answer half of it — and it gives the rail a row back.
        ///
        /// The two builders are unchanged and are laid into sub-pages of a
        /// scroll view. Every section builder here places its controls at
        /// absolute offsets from the top of what it is given, so handing each
        /// one its own container is what lets them be stacked without either of
        /// them being reflowed.
        /// </summary>
        void BuildEnvironmentSection(RectTransform section)
        {
            var page = ScrollableSection(section, StartBlockHeight + WeatherBlockHeight);

            BuildDateTimeSection(SubPage(page, "StartBlock", 0f, StartBlockHeight));
            BuildWeatherSection(SubPage(page, "WeatherBlock", StartBlockHeight, WeatherBlockHeight));
        }

        /// <summary>A full-width slice of a page, so a section builder's own offsets stay meaningful.</summary>
        static RectTransform SubPage(RectTransform page, string name, float top, float height)
        {
            var rt = UIFactory.CreateGroup(page, name);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top);
            rt.sizeDelta = new Vector2(0f, height);
            return rt;
        }

        void BuildWeatherSection(RectTransform content)
        {
            SectionLabel(content, "SKY", -8);

            var skies = WeatherCatalog.AllSkies;
            float skyW = (InnerWidth - 8f) / 3f;
            for (int i = 0; i < skies.Count; i++)
            {
                var sky = skies[i];
                var b = UIFactory.CreateButton(content, sky.name, () => ApplyPhase(sky.phase),
                    UiTheme.Surface, UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f),
                    new Vector2(Pad + i * (skyW + 4f), -28), new Vector2(skyW, 30));
                _skyButtons.Add((sky.phase, b));
            }

            // --- automatic day/night ---
            var autoFrame = UIFactory.CreateBorderedPanel(content, "AutoDayNight", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(autoFrame, new Vector2(0f, 1f), new Vector2(Pad, -66), new Vector2(InnerWidth, 46));

            _autoDayNightBtn = UIFactory.CreateButton(autoFrame, "", ToggleAutoDayNight,
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)_autoDayNightBtn.transform);
            var autoCaption = _autoDayNightBtn.GetComponentInChildren<Text>(true);
            if (autoCaption != null) autoCaption.gameObject.SetActive(false);

            _autoDayNightLamp = UIFactory.CreatePanel(autoFrame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(_autoDayNightLamp, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(8, 8));
            _autoDayNightLamp.GetComponent<Image>().raycastTarget = false;

            var (_, autoState) = UIFactory.CreateStackedLabels(autoFrame,
                "AUTO DAY / NIGHT", "", 28f, InnerWidth - 40f, topInset: 7f);
            _autoDayNightLabel = autoState;

            // --- conditions ---
            SectionLabel(content, "CONDITIONS", -124);

            var conditions = WeatherCatalog.AllConditions;
            for (int i = 0; i < conditions.Count; i++)
            {
                var def = conditions[i];
                float y = -146f - i * 44f;

                var frame = UIFactory.CreateBorderedPanel(content, "Weather_" + def.name,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 40));

                var b = UIFactory.CreateButton(frame, "", () => ApplyCondition(def.condition),
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)b.transform);
                var caption = b.GetComponentInChildren<Text>(true);
                if (caption != null) caption.gameObject.SetActive(false);

                var (name, _) = UIFactory.CreateStackedLabels(frame, def.name, def.detail,
                    12f, InnerWidth - 44f, topInset: 4f);

                // A speaker pip marks the conditions that bring an audio bed.
                if (def.ambience != IronMeridian.Audio.AmbienceTrack.None)
                {
                    var pip = UIFactory.CreateText(frame, "♪", UiTheme.FontSmall, UiTheme.Accent,
                        TextAnchor.MiddleRight);
                    UIFactory.Place(pip.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(16, 16));
                }

                _conditionFrames.Add((def.condition, frame.Find("Fill").GetComponent<Image>(), name));
            }

            var hint = UIFactory.CreateText(content,
                "Sky and fog preview here in the editor. Weather audio plays in battle mode only.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -420), new Vector2(InnerWidth, 40));

            RefreshWeather();
        }

        void ApplyPhase(SkyPhase phase)
        {
            if (_weather == null) return;
            _weather.SetPhase(phase);
        }

        void ApplyCondition(WeatherCondition condition)
        {
            if (_weather == null) return;
            _weather.SetCondition(condition);
        }

        void ToggleAutoDayNight()
        {
            if (_weather == null) return;
            _weather.SetAutoDayNight(!_weather.AutoDayNight);
        }

        /// <summary>Repaints the whole section from the system's state — it is the source of truth.</summary>
        void RefreshWeather()
        {
            if (_weather == null) return;

            foreach (var (phase, btn) in _skyButtons)
            {
                bool on = !_weather.AutoDayNight && phase == _weather.Phase;
                btn.GetComponent<Image>().color = on ? UiTheme.Accent : UiTheme.Surface;
                var t = btn.GetComponentInChildren<Text>(true);
                if (t != null) t.color = on ? Color.white : UiTheme.TextDim;
            }

            foreach (var (condition, fill, label) in _conditionFrames)
            {
                bool on = condition == _weather.Condition;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            if (_autoDayNightLamp != null)
                _autoDayNightLamp.GetComponent<Image>().color =
                    _weather.AutoDayNight ? UiTheme.Success : UiTheme.TextFaint;

            if (_autoDayNightLabel != null)
                _autoDayNightLabel.text = _weather.AutoDayNight
                    ? $"ON — clock drives the sky (now {_weather.Phase})"
                    : "OFF — sky is set by hand above";
        }

        // -------------------------------------------------- date & time section

        /// <summary>
        /// H-hour for the scenario: the current start (click to edit) plus three
        /// ready-made times of day. Whatever is set here is the clock the top
        /// bar shows once the battle starts, and it is saved with the map.
        /// </summary>
        void BuildDateTimeSection(RectTransform content)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "StartButton", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -30), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "", OpenStartEditor, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var glyph = UIFactory.CreateImage(frame, UiIcons.Clock, "Glyph");
            glyph.color = UiTheme.Accent;
            glyph.raycastTarget = false;
            UIFactory.Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(18, 18));

            var (startValue, _) = UIFactory.CreateStackedLabels(frame, "", "Click to change",
                40f, InnerWidth - 52f, topInset: 9f, titleSize: UiTheme.FontBody);
            _startValueLabel = startValue;

            SectionLabel(content, "PRESETS", -96);

            for (int i = 0; i < StartPresets.Length; i++)
            {
                var preset = StartPresets[i];
                float y = -118f - i * 58f;

                var pf = UIFactory.CreateBorderedPanel(content, "Preset" + i, UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(pf, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

                var pb = UIFactory.CreateButton(pf, "", () => ApplyStart(preset.when),
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)pb.transform);
                var pc = pb.GetComponentInChildren<Text>(true);
                if (pc != null) pc.gameObject.SetActive(false);

                float presetW = InnerWidth - 24f;
                var name = UIFactory.CreateText(pf, preset.name, UiTheme.FontSmall, UiTheme.Text,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.PlaceTopLeft(name.rectTransform, 12f, 4f, presetW, 15f);
                UIFactory.Fit(name);

                var when = UIFactory.CreateText(pf, preset.when.ToString("HH:mm  ·  dd.MM.yyyy"),
                    UiTheme.FontLabel, UiTheme.Accent, TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(when.rectTransform, 12f, 19f, presetW, 14f);
                UIFactory.Fit(when);

                var detail = UIFactory.CreateText(pf, preset.detail, UiTheme.FontLabel, UiTheme.TextFaint,
                    TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(detail.rectTransform, 12f, 34f, presetW, 14f);
                UIFactory.Fit(detail);
            }

            var hint = UIFactory.CreateText(content,
                "The clock runs only while a battle is in progress — the editor is timeless. " +
                "This start time is saved with the map.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -302), new Vector2(InnerWidth, 56));

            RefreshStartLabel();
        }

        void OpenStartEditor()
        {
            if (_clock == null || _canvas == null) return;
            DateTimeDialog.Open(_canvas, _clock.StartDateTime, ApplyStart);
        }

        void ApplyStart(System.DateTime when)
        {
            if (_clock == null) return;
            _clock.SetStart(when);
            RefreshStartLabel();
        }

        void RefreshStartLabel()
        {
            if (_startValueLabel == null || _clock == null) return;
            _startValueLabel.text = _clock.StartText;
        }

        // -------------------------------------------------------- map section

        void BuildMapSection(RectTransform content)
        {
            _styleDropdown = UIFactory.CreateDropdown(content, StyleNames(),
                System.Array.IndexOf(Styles, _map.Style), OnStyleSelected);
            StyleDropdown(_styleDropdown, -30);

            SectionLabel(content, "PROJECTION", -76);

            var frame = UIFactory.CreateBorderedPanel(content, "ViewToggle", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -98), new Vector2(InnerWidth, 32));

            var viewBtn = UIFactory.CreateButton(frame,
                _map.ViewMode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D",
                ToggleView, new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)viewBtn.transform);
            _viewBtnLabel = viewBtn.GetComponentInChildren<Text>(true);

            var parity = UIFactory.CreateText(content,
                "2D and 3D show the same world — units, effects, weather, lines and buildings all behave identically.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(parity.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -134), new Vector2(InnerWidth, 32));

            SectionLabel(content, "LAYERS", -172);

            _buildingsLamp = ToggleRow(content, "3D BUILDINGS", -194,
                () => { _map.SetBuildingsVisible(!_map.BuildingsVisible); }, out _buildingsLabel);

            _mapControlsLamp = ToggleRow(content, "ON-MAP CONTROLS", -238,
                () => { if (_mapControls != null) _mapControls.SetControlsVisible(!_mapControls.ControlsVisible); RefreshMapSection(); },
                out _mapControlsLabel);

            _compassLamp = ToggleRow(content, "COMPASS", -282,
                () => { if (_mapControls != null) _mapControls.SetCompassVisible(!_mapControls.CompassVisible); RefreshMapSection(); },
                out _compassLabel);

            SectionLabel(content, "UNIT LABELS", -330);

            _labelSizeValue = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_labelSizeValue.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -330), new Vector2(80, 18));

            var slider = UIFactory.CreateSlider(content, LabelScaleTo01(UnitActor.LabelScale), v =>
            {
                UnitActor.SetLabelScale(LabelScaleFrom01(v));
                RefreshMapSection();
            });
            UIFactory.Place((RectTransform)slider.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -352), new Vector2(InnerWidth, 30));

            // Control measures used to be set up from here. They have their own
            // section in the rail now, with the options docked on the right:
            // choosing what to draw has nothing to do with which imagery is
            // under it, and the two had no business sharing a panel.

            RefreshMapSection();
        }

        /// <summary>
        /// A lamp + label row that reads as an on/off switch.
        ///
        /// <paramref name="hint"/> becomes the row's hover caption. That is
        /// where the long explanations belong: a switch that needs a paragraph
        /// to explain it does not need that paragraph on the page, taking the
        /// vertical space the switches themselves wanted and being read once and
        /// never again. On hover it is there for the player who has not met the
        /// control before and invisible to the one who has.
        /// </summary>
        RectTransform ToggleRow(RectTransform content, string label, float y,
            UnityEngine.Events.UnityAction action, out Text stateLabel, string hint = null)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Toggle_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 38));

            var btn = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var lamp = UIFactory.CreatePanel(frame, "Lamp", UiTheme.TextFaint);
            UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(8, 8));
            lamp.GetComponent<Image>().raycastTarget = false;

            // Title and state share one line, so the two rects must not overlap:
            // the title stops where the state column begins.
            var title = UIFactory.CreateText(frame, label, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(28, 0),
                new Vector2(InnerWidth - 28f - 74f, 16));
            UIFactory.Fit(title);

            stateLabel = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Place(stateLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(62, 16));

            // On the button rather than the frame: the button is stretched over
            // the whole row and is what the pointer actually lands on.
            UiTooltip.Attach(btn.gameObject, hint);

            return lamp;
        }

        List<string> StyleNames()
        {
            var names = new List<string>(Styles.Length);
            foreach (var style in Styles) names.Add(StyleLabel(style));
            return names;
        }

        static string StyleLabel(MapStyle style) => style switch
        {
            MapStyle.Satellite => "SATELLITE",
            MapStyle.SatelliteLabels => "SATELLITE + LABELS",
            MapStyle.Roads => "ROADS",
            MapStyle.Terrain => "TERRAIN (NO IMAGERY)",
            MapStyle.Sentinel2 => "SENTINEL-2",
            MapStyle.OpenStreetMap => "OPENSTREETMAP",
            _ => style.ToString().ToUpperInvariant()
        };

        // The slider is linear 0..1 but the useful label range is 0.5x..2.5x,
        // so map between them rather than exposing raw multipliers.
        static float LabelScaleTo01(float scale) => Mathf.InverseLerp(0.5f, 2.5f, scale);
        static float LabelScaleFrom01(float v) => Mathf.Lerp(0.5f, 2.5f, v);

        /// <summary>
        /// Latches the tool strip's boundary button, so an armed draw tool reads
        /// the same in the rail as it does in the options panel that armed it.
        /// </summary>
        /// <summary>Repaints every toggle and readout from the systems that own the state.</summary>
        void RefreshMapSection()
        {
            if (_buildingsLamp != null)
            {
                bool on = _map != null && _map.BuildingsVisible;
                _buildingsLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _buildingsLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_mapControlsLamp != null)
            {
                bool on = _mapControls != null && _mapControls.ControlsVisible;
                _mapControlsLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _mapControlsLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_compassLamp != null)
            {
                bool on = _mapControls != null && _mapControls.CompassVisible;
                _compassLamp.GetComponent<Image>().color = on ? UiTheme.Success : UiTheme.TextFaint;
                _compassLabel.text = on ? "SHOWN" : "HIDDEN";
            }

            if (_labelSizeValue != null)
                _labelSizeValue.text = string.Format("{0:0.00}x", UnitActor.LabelScale);
        }

        void SectionLabel(RectTransform content, string label, float y)
        {
            var t = UIFactory.CreateSectionHeader(content, label);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 18));
        }
    }
}
