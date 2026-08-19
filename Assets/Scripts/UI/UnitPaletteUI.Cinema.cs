using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Map;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — the CINEMA MODE section: a camera path over
    /// the battle, recorded a shot at a time and played back as a flight.
    ///
    /// One part of a class split across files purely for size; the fields and
    /// lifecycle live in UnitPaletteUI.cs.
    ///
    /// **Battle mode only.** Every other row on this rail authors something. This
    /// one authors nothing — it is a way of watching a fight that is already
    /// running, which is a thing you want during the battle and not while laying
    /// one out.
    ///
    /// **A waypoint is recorded, not typed.** There is no editing of a shot's
    /// figures: the player frames the view they want with the ordinary camera
    /// controls and presses ADD WAYPOINT, and what is stored is exactly what was
    /// on screen. Numbers in boxes would be a worse way of saying "this shot" for
    /// a thing whose whole subject is what it looks like.
    ///
    /// All the work is <see cref="CinemaSystem"/>'s; this is the panel that asks
    /// for it. See docs/03-GAMEPLAY.md § Cinema mode.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ----------------------------------------------------- cinema section

        CinemaSystem _cinema;

        RectTransform _cinemaList;
        Button _cinemaPlayButton;
        Text _cinemaPlayLabel, _cinemaLegValue, _cinemaStatus;
        Image _cinemaPlayGlyph;

        /// <summary>Height of one waypoint row, and the gap between rows.</summary>
        const float CinemaRowHeight = 46f;
        const float CinemaListPad = 2f;

        /// <summary>Width a waypoint row actually gets, once the scrollbar has taken its own.</summary>
        const float CinemaRowWidth = InnerWidth - UIFactory.ScrollbarWidth - CinemaListPad * 2f;

        void BuildCinemaSection(RectTransform content)
        {
            SectionLabel(content, "CAMERA PATH", -8);

            var add = UIFactory.CreateButton(content, "ADD WAYPOINT", AddCinemaWaypoint,
                UiTheme.Surface, UiTheme.Text, UiTheme.FontBody);
            UIFactory.Place((RectTransform)add.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -30), new Vector2(InnerWidth, 42));

            var addIcon = UIFactory.CreateImage(add.transform, UiIcons.Plus, "Icon");
            addIcon.color = UiTheme.Accent;
            addIcon.raycastTarget = false;
            UIFactory.Place(addIcon.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(16, 16));

            var addHint = UIFactory.CreateText(content,
                "Frame the shot you want with the ordinary camera controls, then add it. "
                + "Position, altitude, heading and tilt are all recorded.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(addHint.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -78), new Vector2(InnerWidth, 34));

            SectionLabel(content, "WAYPOINTS", -118);

            // The list is the only thing here that grows, so it takes the slack
            // between the fixed block above it and the fixed block below.
            var scroll = UIFactory.CreateScrollView(content, out _cinemaList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 146);
            srt.offsetMax = new Vector2(-Pad, -138);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var layout = _cinemaList.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 4;
                layout.padding = new RectOffset((int)CinemaListPad, (int)CinemaListPad, 2, 2);
            }

            // --- the fixed block at the foot: timing, then playback.
            var legFrame = UIFactory.CreateBorderedPanel(content, "CinemaLeg",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(legFrame, new Vector2(0f, 0f), new Vector2(Pad, 108),
                new Vector2(InnerWidth, 32));

            var legLabel = UIFactory.CreateText(legFrame, "SECONDS PER LEG", UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(legLabel.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(10, 0), new Vector2(InnerWidth - 108f, 20));
            UIFactory.Fit(legLabel, 8);

            var slower = UIFactory.CreateButton(legFrame, "◄",
                () => _cinema?.StepLegSeconds(-CinemaSystem.LegStepSeconds),
                UiTheme.SurfaceHover, UiTheme.TextDim, 11);
            UIFactory.Place((RectTransform)slower.transform, new Vector2(1f, 0.5f),
                new Vector2(-72, 0), new Vector2(22, 22));

            _cinemaLegValue = UIFactory.CreateText(legFrame, "", UiTheme.FontSmall,
                UiTheme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_cinemaLegValue.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-34, 0), new Vector2(38, 20));

            var faster = UIFactory.CreateButton(legFrame, "►",
                () => _cinema?.StepLegSeconds(CinemaSystem.LegStepSeconds),
                UiTheme.SurfaceHover, UiTheme.TextDim, 11);
            UIFactory.Place((RectTransform)faster.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(22, 22));

            _cinemaPlayButton = UIFactory.CreateButton(content, "PLAY", ToggleCinemaPlayback,
                UiTheme.AccentWash, UiTheme.Accent, UiTheme.FontBody);
            UIFactory.Place((RectTransform)_cinemaPlayButton.transform, new Vector2(0f, 0f),
                new Vector2(Pad, 60), new Vector2(InnerWidth, 42));
            _cinemaPlayLabel = _cinemaPlayButton.GetComponentInChildren<Text>(true);

            _cinemaPlayGlyph = UIFactory.CreateImage(_cinemaPlayButton.transform, UiIcons.Play, "Glyph");
            _cinemaPlayGlyph.color = UiTheme.Accent;
            _cinemaPlayGlyph.raycastTarget = false;
            UIFactory.Place(_cinemaPlayGlyph.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(15, 15));

            _cinemaStatus = UIFactory.CreateText(content, "", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(_cinemaStatus.rectTransform, new Vector2(0f, 0f),
                new Vector2(Pad, 34), new Vector2(InnerWidth - 76f, 20));

            var clear = UIFactory.CreateButton(content, "CLEAR", ClearCinemaPath,
                UiTheme.SurfaceHover, UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)clear.transform, new Vector2(1f, 0f),
                new Vector2(-Pad, 32), new Vector2(64, 24));

            RefreshCinema();
        }

        /// <summary>
        /// Hands the panel its system. Called after <see cref="Build"/> because
        /// the system needs the rig, and set here rather than passed in because
        /// the panel only ever asks the system for things — every control above
        /// reads the field at click time, so a null one is a dead button rather
        /// than a broken build.
        /// </summary>
        public void SetCinema(CinemaSystem cinema)
        {
            if (_cinema != null) _cinema.Changed -= RefreshCinema;
            _cinema = cinema;
            if (_cinema != null)
            {
                _cinema.Changed += RefreshCinema;
                // Same channel the PLAYERS and COMMANDERS pages report on.
                _cinema.Flash = m => DropRejected?.Invoke(m);
            }
            RefreshCinema();
        }

        void AddCinemaWaypoint()
        {
            if (_cinema == null) return;
            int index = _cinema.Add();
            if (index < 0) { DropRejected?.Invoke("The map is not ready yet."); return; }
            DropRejected?.Invoke($"Waypoint {index + 1} recorded from the current view.");
        }

        void ClearCinemaPath()
        {
            if (_cinema == null || _cinema.Shots.Count == 0) return;
            _cinema.Clear();
            DropRejected?.Invoke("Camera path cleared.");
        }

        void ToggleCinemaPlayback()
        {
            if (_cinema == null) return;
            if (_cinema.IsPlaying) _cinema.Stop();
            else _cinema.Play();
        }

        /// <summary>
        /// Rebuilds the list and repaints the playback controls.
        ///
        /// Driven by <see cref="CinemaSystem.Changed"/> rather than polled — and
        /// the system raises it on every leg, so the row the camera is flying
        /// towards is lit while it is flying towards it.
        /// </summary>
        void RefreshCinema()
        {
            if (_cinemaList == null) return;

            bool playing = _cinema != null && _cinema.IsPlaying;
            int count = _cinema?.Shots.Count ?? 0;

            if (_cinemaLegValue != null)
                _cinemaLegValue.text = _cinema != null ? $"{_cinema.LegSeconds:0}s" : "—";

            if (_cinemaPlayLabel != null) _cinemaPlayLabel.text = playing ? "STOP" : "PLAY";
            if (_cinemaPlayGlyph != null)
                _cinemaPlayGlyph.sprite = playing ? UiIcons.Square : UiIcons.Play;
            if (_cinemaPlayButton != null)
            {
                var img = _cinemaPlayButton.GetComponent<Image>();
                if (img != null) img.color = playing ? UiTheme.Danger : UiTheme.AccentWash;
                if (_cinemaPlayLabel != null)
                    _cinemaPlayLabel.color = playing ? UiTheme.Text : UiTheme.Accent;
                if (_cinemaPlayGlyph != null)
                    _cinemaPlayGlyph.color = playing ? UiTheme.Text : UiTheme.Accent;
                // Two shots is a path; one is a place. Say so with the button
                // rather than by refusing the press after the fact.
                _cinemaPlayButton.interactable = count >= 2;
            }

            if (_cinemaStatus != null)
            {
                _cinemaStatus.text =
                    playing ? $"Flying to waypoint {_cinema.CurrentShot + 1} of {count}"
                    : count == 0 ? "No waypoints yet."
                    : count == 1 ? "One more waypoint and the path can be flown."
                    : $"{count} waypoints   ·   about {count * (_cinema?.LegSeconds ?? 0f):0}s";
                _cinemaStatus.color = playing ? UiTheme.Accent : UiTheme.TextFaint;
            }

            // Cheap when shut: the tour raises Changed on every leg, and
            // rebuilding a list nobody is looking at would churn a few dozen
            // uGUI objects a second for the whole of a playback.
            if (!_sectionContent.TryGetValue(Section.Cinema, out var page) ||
                !page.gameObject.activeSelf) return;

            ClearChildren(_cinemaList);

            if (count == 0)
            {
                var empty = UIFactory.CreateText(_cinemaList,
                    "No waypoints yet. Move the camera to the first shot and press ADD WAYPOINT.",
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 44);
                return;
            }

            for (int i = 0; i < count; i++) CinemaRow(i, _cinema.Shots[i]);
        }

        void CinemaRow(int index, CinemaSystem.Shot shot)
        {
            bool current = _cinema != null && _cinema.IsPlaying && _cinema.CurrentShot == index;

            var row = UIFactory.CreateBorderedPanel(_cinemaList, "Waypoint_" + index,
                current ? UiTheme.AccentWash : UiTheme.Surface,
                current ? UiTheme.Accent : UiTheme.Border);
            row.sizeDelta = new Vector2(0, CinemaRowHeight);

            // Altitude and heading, because those are what make one shot of the
            // same ground different from another — a name alone would not.
            string altitude = shot.distanceMeters >= 1000f
                ? $"{shot.distanceMeters / 1000f:0.#} km"
                : $"{shot.distanceMeters:0} m";

            UIFactory.CreateStackedLabels(row, $"Waypoint {index + 1}",
                $"{altitude}   ·   {shot.yawDeg:000}°   ·   {shot.pitchDeg:0}° tilt",
                12f, CinemaRowWidth - 76f, topInset: 6f);

            var go = UIFactory.CreateButton(row, "◎", () => _cinema?.Preview(index),
                UiTheme.SurfaceHover, UiTheme.Text, 12);
            UIFactory.Place((RectTransform)go.transform, new Vector2(1f, 0.5f),
                new Vector2(-38, 0), new Vector2(26, 22));
            UiTooltip.Attach(go.gameObject, "Fly to this shot", UiTooltip.Side.Left);

            var remove = UIFactory.CreateButton(row, "✕", () => _cinema?.RemoveAt(index),
                UiTheme.SurfaceHover, UiTheme.TextDim, 12);
            UIFactory.Place((RectTransform)remove.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(24, 22));
            UiTooltip.Attach(remove.gameObject, "Remove this waypoint", UiTooltip.Side.Left);
        }
    }
}
