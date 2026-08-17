using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// VIDEOS — every film the game plays, with where it is loaded from and a
    /// transport to watch it.
    ///
    /// The same lab as AUDIO, for the same reason: the catalogue naming a path
    /// is not the same as the path resolving, and a screen that plays the file
    /// is the only way to be sure. A row says whether the clip was found, how
    /// long it is and what plays it.
    ///
    /// The menu bed is stopped on the way in — a film has its own sound, and a
    /// music bed under it would be the loudest thing in the mix.
    ///
    /// Reached from DEVELOPMENT. See docs/32-VIDEO.md.
    /// </summary>
    public class VideoListUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        const float ScreenMargin = 60f;
        const float ListWidth = 520f;
        const float ContentTop = 190f, ContentBottom = 60f;
        const float RowHeight = 82f, RowGap = 6f;
        /// <summary>Height of the transport strip under the picture.</summary>
        const float TransportHeight = 96f;

        class Entry
        {
            public VideoDef Def;
            public VideoClip Clip;
            public bool Found => Clip != null;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<VideoId, Image> _rowFills = new Dictionary<VideoId, Image>();
        Entry _selected;

        Canvas _canvas;
        RectTransform _listContent;
        RawImage _picture;
        AspectRatioFitter _fitter;
        RenderTexture _target;
        VideoPlayer _video;
        AudioSource _audio;
        Text _detailName, _detailPath, _detailUse, _timeLabel, _playLabel, _placeholder;
        Slider _scrub;
        bool _scrubbing;

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Stop();

            _canvas = UIFactory.CreateCanvas("VideoListCanvas");
            UIFactory.CreateScreenBackground(_canvas.transform, BackgroundId.Interior,
                BackgroundCatalog.DenseScreenScrim);

            LoadEntries();
            BuildHeader();
            BuildList();
            BuildPlayer();

            if (_entries.Count > 0) Select(_entries[0]);
            else ShowPlaceholder("No videos are registered. See docs/32-VIDEO.md.");
        }

        void LoadEntries()
        {
            foreach (var def in VideoCatalog.All)
                _entries.Add(new Entry { Def = def, Clip = Resources.Load<VideoClip>(def.resourcePath) });
        }

        // ------------------------------------------------------------- frame

        void BuildHeader()
        {
            var title = UIFactory.CreateText(_canvas.transform, "VIDEOS", 52, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f),
                new Vector2(ScreenMargin, -70), new Vector2(700, 60));

            int found = 0;
            foreach (var e in _entries) if (e.Found) found++;

            var sub = UIFactory.CreateText(_canvas.transform,
                $"{_entries.Count} registered · {found} installed. Every film the game plays, " +
                "with the file behind it and a transport to watch it.",
                18, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f),
                new Vector2(ScreenMargin, -122), new Vector2(1200, 26));

            UIFactory.CreateBackButton(_canvas.transform, "BACK TO DEVELOPMENT", Leave);
        }

        void BuildList()
        {
            var scroll = UIFactory.CreateScrollView(_canvas.transform, out _listContent,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(ScreenMargin, ContentBottom);
            rt.offsetMax = new Vector2(ScreenMargin + ListWidth, -ContentTop);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = RowGap;
            layout.padding = new RectOffset(0, 0, 0, 12);

            foreach (var entry in _entries) AddRow(entry);
        }

        void AddRow(Entry entry)
        {
            var e = entry;

            var frame = UIFactory.CreateBorderedPanel(_listContent, "Row_" + e.Def.id,
                UiTheme.Surface, UiTheme.Border);
            frame.sizeDelta = new Vector2(0, RowHeight);

            var btn = UIFactory.CreateButton(frame, "", () => Select(e),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var glyph = UIFactory.CreateImage(frame, UiIcons.Play, "Glyph");
            glyph.color = e.Found ? UiTheme.Accent : UiTheme.TextFaint;
            glyph.raycastTarget = false;
            UIFactory.Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(22, 22));

            var name = UIFactory.CreateText(frame, e.Def.name.ToUpperInvariant(), 20,
                e.Found ? UiTheme.Text : UiTheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(name.rectTransform, 62f, 14f, ListWidth - 200f, 24f);
            UIFactory.Fit(name, 13);

            var path = UIFactory.CreateText(frame, e.Def.resourcePath, 13,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(path.rectTransform, 62f, 42f, ListWidth - 200f, 20f);
            UIFactory.Fit(path, 9);

            // States, not decoration: a missing file is the thing this screen
            // exists to make visible.
            var state = UIFactory.CreateText(frame, e.Found ? "INSTALLED" : "MISSING",
                UiTheme.FontLabel, e.Found ? UiTheme.Success : UiTheme.Danger,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(state.rectTransform, new Vector2(1f, 0.5f), new Vector2(-18, 0),
                new Vector2(120, 18));

            _rowFills[e.Def.id] = frame.Find("Fill").GetComponent<Image>();
        }

        // ------------------------------------------------------------ player

        void BuildPlayer()
        {
            var frame = UIFactory.CreateBorderedPanel(_canvas.transform, "Player",
                UiTheme.Panel, UiTheme.Border);
            frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 1);
            frame.offsetMin = new Vector2(ScreenMargin + ListWidth + 28f, ContentBottom);
            frame.offsetMax = new Vector2(-ScreenMargin, -ContentTop);

            // The picture: black behind it, because a film is letterboxed and
            // whatever shows in the bars must not be the panel's own fill.
            var stage = UIFactory.CreatePanel(frame, "Stage", Color.black);
            stage.anchorMin = new Vector2(0, 0); stage.anchorMax = new Vector2(1, 1);
            stage.offsetMin = new Vector2(12, TransportHeight);
            stage.offsetMax = new Vector2(-12, -12);

            _picture = UIFactory.CreateRawImage(stage, "Picture");
            _picture.raycastTarget = false;
            var prt = _picture.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            UIFactory.Stretch(prt);

            // Fit, never envelope: a frame is composed and must not be cropped.
            _fitter = _picture.gameObject.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _fitter.aspectRatio = 1.777f;

            _placeholder = UIFactory.CreateText(stage, "", 18, UiTheme.TextDim);
            UIFactory.Stretch(_placeholder.rectTransform);

            _detailName = UIFactory.CreateText(frame, "", 22, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, TransportHeight - 26f), new Vector2(700, 24));
            UIFactory.Fit(_detailName, 13);

            _detailUse = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleRight);
            UIFactory.Place(_detailUse.rectTransform, new Vector2(1f, 0f),
                new Vector2(-16, TransportHeight - 26f), new Vector2(560, 20));
            UIFactory.Fit(_detailUse, 9);

            _detailPath = UIFactory.CreateText(frame, "", 13, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_detailPath.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, TransportHeight - 48f), new Vector2(900, 18));
            UIFactory.Fit(_detailPath, 9);

            // --- transport ---
            var play = UIFactory.CreateButton(frame, "PLAY", TogglePlay,
                UiTheme.Accent, GameConfig.UiBackground, 15);
            UIFactory.Place((RectTransform)play.transform, new Vector2(0f, 0f),
                new Vector2(16, 16), new Vector2(120, 36));
            _playLabel = play.GetComponentInChildren<Text>();

            var restart = UIFactory.CreateButton(frame, "RESTART", Restart,
                UiTheme.Surface, UiTheme.TextDim, 15);
            UIFactory.Place((RectTransform)restart.transform, new Vector2(0f, 0f),
                new Vector2(144, 16), new Vector2(120, 36));

            _timeLabel = UIFactory.CreateText(frame, "0:00 / 0:00", 14, UiTheme.TextDim,
                TextAnchor.MiddleRight);
            UIFactory.Place(_timeLabel.rectTransform, new Vector2(1f, 0f),
                new Vector2(-16, 34), new Vector2(140, 20));

            _scrub = UIFactory.CreateSlider(frame, 0f, OnScrub);
            var srt = (RectTransform)_scrub.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0);
            srt.pivot = new Vector2(0.5f, 0);
            srt.offsetMin = new Vector2(276, 20);
            srt.offsetMax = new Vector2(-170, 52);

            _video = gameObject.AddComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.isLooping = false;
            _video.skipOnDrop = true;
            _video.renderMode = VideoRenderMode.RenderTexture;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _video.SetTargetAudioSource(0, _audio);

            _video.errorReceived += (_, message) =>
            {
                Debug.LogWarning($"[Videos] {message}");
                ShowPlaceholder(message);
            };
        }

        // ---------------------------------------------------------- selection

        void Select(Entry entry)
        {
            _selected = entry;
            PaintRows();

            _detailName.text = entry.Def.name.ToUpperInvariant();
            _detailPath.text = "Resources/" + entry.Def.resourcePath;
            _detailUse.text = entry.Def.usedBy.ToUpperInvariant();

            Stop();

            if (!entry.Found)
            {
                ShowPlaceholder($"Missing: Resources/{entry.Def.resourcePath}\n" +
                                "Video files must live under an Assets/Resources folder — see docs/32-VIDEO.md.");
                return;
            }

            var clip = entry.Clip;
            ReleaseTarget();
            _target = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "VideoPreview" };
            _picture.texture = _target;
            _fitter.aspectRatio = clip.height > 0 ? clip.width / (float)clip.height : 1.777f;

            _video.clip = clip;
            _video.targetTexture = _target;
            _video.frame = 0;
            _video.Prepare();

            _placeholder.text = "";
            _picture.gameObject.SetActive(true);
            SetPlayLabel();
        }

        void PaintRows()
        {
            foreach (var pair in _rowFills)
                pair.Value.color = _selected != null && pair.Key == _selected.Def.id
                    ? UiTheme.AccentWash : UiTheme.Surface;
        }

        void ShowPlaceholder(string message)
        {
            if (_placeholder != null) _placeholder.text = message;
            if (_picture != null) _picture.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------- transport

        void TogglePlay()
        {
            if (_selected == null || !_selected.Found) return;
            if (_video.isPlaying) _video.Pause();
            else _video.Play();
            SetPlayLabel();
        }

        void Restart()
        {
            if (_selected == null || !_selected.Found) return;
            _video.frame = 0;
            _video.Play();
            SetPlayLabel();
        }

        void Stop()
        {
            if (_video != null) _video.Stop();
            if (_scrub != null) _scrub.value = 0f;
            SetPlayLabel();
        }

        void SetPlayLabel()
        {
            if (_playLabel != null)
                _playLabel.text = _video != null && _video.isPlaying ? "PAUSE" : "PLAY";
        }

        void OnScrub(float value)
        {
            if (_selected == null || !_selected.Found || !_video.canSetTime) return;
            if (!_scrubbing) return;
            _video.time = value * _selected.Clip.length;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Leave(); return; }

            if (_selected == null || !_selected.Found || _video == null) return;

            // The slider is driven by the film except while it is being dragged,
            // which is the one time the film has to follow it instead.
            _scrubbing = _scrub != null &&
                         UnityEngine.EventSystems.EventSystem.current != null &&
                         UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
                             == _scrub.gameObject;

            double length = _selected.Clip.length;
            if (!_scrubbing && length > 0.001)
                _scrub.SetValueWithoutNotify((float)(_video.time / length));

            _timeLabel.text = $"{Clock(_video.time)} / {Clock(length)}";
            SetPlayLabel();
        }

        static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = Mathf.FloorToInt((float)seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        // ------------------------------------------------------------- leave

        void Leave()
        {
            Stop();
            SceneManager.LoadScene(GameConfig.SceneTesting);
        }

        void ReleaseTarget()
        {
            if (_target == null) return;
            _target.Release();
            Destroy(_target);
            _target = null;
        }

        void OnDestroy() => ReleaseTarget();
    }
}
