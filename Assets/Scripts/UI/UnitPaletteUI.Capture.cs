using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — the CAPTURE section: a still of the map, or
    /// a video of it.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// The section is in **both** mode lists. A screenshot of a scenario being
    /// laid out and a recording of the battle that follows are the same job,
    /// and hiding the controls the moment the fight starts would remove them
    /// exactly when there is something worth recording.
    ///
    /// All the work is <see cref="CaptureSystem"/>'s; this is the panel that
    /// asks for it. See docs/39-CAPTURE.md.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ---------------------------------------------------- capture section

        Text _captureRecordLabel, _captureStatus, _capturePath;
        Image _captureRecordDot;
        Button _captureRecordButton;

        void BuildCaptureSection(RectTransform content)
        {
            SectionLabel(content, "STILL", -8);

            // --- screenshot
            var shot = UIFactory.CreateButton(content, "SCREENSHOT",
                () => { CaptureSystem.TakeScreenshot(); RefreshCapture(); },
                UiTheme.Surface, UiTheme.Text, UiTheme.FontBody);
            UIFactory.Place((RectTransform)shot.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -30), new Vector2(InnerWidth, 44));

            var shotIcon = UIFactory.CreateImage(shot.transform, UiIcons.Camera, "Icon");
            shotIcon.color = UiTheme.TextDim;
            UIFactory.Place(shotIcon.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(20, 20));

            var shotHint = UIFactory.CreateText(content,
                "A PNG of the screen exactly as it looks, interface included.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(shotHint.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -80), new Vector2(InnerWidth, 30));

            SectionLabel(content, "RECORDING", -118);

            // --- record / stop
            _captureRecordButton = UIFactory.CreateButton(content, "RECORD",
                () => { CaptureSystem.ToggleRecording(); RefreshCapture(); },
                UiTheme.Surface, UiTheme.Text, UiTheme.FontBody);
            UIFactory.Place((RectTransform)_captureRecordButton.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -140), new Vector2(InnerWidth, 44));

            _captureRecordDot = UIFactory.CreateImage(_captureRecordButton.transform, UiIcons.Disc, "Dot");
            _captureRecordDot.color = UiTheme.Danger;
            UIFactory.Place(_captureRecordDot.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(14, 14));

            _captureRecordLabel = _captureRecordButton.GetComponentInChildren<Text>();

            _captureStatus = UIFactory.CreateText(content, "", UiTheme.FontSmall, UiTheme.TextDim,
                TextAnchor.UpperLeft);
            UIFactory.Place(_captureStatus.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -190), new Vector2(InnerWidth, 20));

            var recordHint = UIFactory.CreateText(content,
                $"An H.264 .mp4 at {CaptureSystem.RecordFps} fps, encoded by ffmpeg as it goes. The "
                + "game runs in slow motion while recording so that no frame is missed; the video "
                + "itself plays back at full speed.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(recordHint.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -214), new Vector2(InnerWidth, 66));

            SectionLabel(content, "SAVED TO", -292);

            _capturePath = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.UpperLeft);
            UIFactory.Place(_capturePath.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -314), new Vector2(InnerWidth, 48));

            var open = UIFactory.CreateButton(content, "OPEN FOLDER",
                CaptureSystem.OpenFolder, UiTheme.SurfaceHover, UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)open.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -366), new Vector2(InnerWidth, 30));

            RefreshCapture();
        }

        /// <summary>
        /// Repaints the record button and the two readouts.
        ///
        /// Driven by <see cref="CaptureSystem.Changed"/> rather than polled, so
        /// the panel is only touched when something actually happened — the
        /// system raises it on start, on stop, and once a second while a take
        /// is running.
        /// </summary>
        void RefreshCapture()
        {
            if (_captureRecordButton == null) return;

            bool recording = CaptureSystem.Recording;

            if (_captureRecordLabel != null)
                _captureRecordLabel.text = recording ? "STOP" : "RECORD";
            if (_captureRecordDot != null)
                _captureRecordDot.sprite = recording ? UiIcons.Square : UiIcons.Disc;

            var img = _captureRecordButton.GetComponent<Image>();
            if (img != null) img.color = recording ? UiTheme.Danger : UiTheme.Surface;

            // No encoder, no recording — and say so where the button is,
            // rather than letting it look broken when pressed.
            bool canRecord = CaptureSystem.CanRecord;
            _captureRecordButton.interactable = canRecord || recording;
            if (_captureRecordDot != null)
                _captureRecordDot.color = canRecord ? UiTheme.Danger : UiTheme.TextFaint;

            if (_captureStatus != null)
            {
                _captureStatus.text =
                    !string.IsNullOrEmpty(CaptureSystem.LastError) ? CaptureSystem.LastError
                    : recording
                        ? $"● {CaptureSystem.RecordedSeconds:0}s — {CaptureSystem.FrameCount} frames"
                    : !canRecord
                        ? "ffmpeg not found — install it to record. See docs/39-CAPTURE.md."
                    : CaptureSystem.FrameCount > 0
                        ? $"Last take: {CaptureSystem.RecordedSeconds:0.#}s "
                          + $"({CaptureSystem.FrameCount} frames)"
                        : "";

                _captureStatus.color = string.IsNullOrEmpty(CaptureSystem.LastError) && canRecord
                    ? UiTheme.TextDim : UiTheme.Warning;
            }

            if (_capturePath != null)
            {
                _capturePath.text = string.IsNullOrEmpty(CaptureSystem.LastOutput)
                    ? CaptureSystem.OutputRoot
                    : CaptureSystem.LastOutput;
            }
        }
    }
}
