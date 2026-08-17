using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Vfx;

namespace IronMeridian.UI
{
    /// <summary>
    /// AUDIO — every sound the game can make, with its name, where it is loaded
    /// from and a transport to play it.
    ///
    /// **One register, four channels.** The game plays audio through four
    /// separate paths — the music bed (<see cref="MusicManager"/>), the weather
    /// ambience (<see cref="AmbienceManager"/>), positional effect sounds
    /// (<see cref="EffectAudio"/>) and the UI click — and each knows only about
    /// itself. This screen is the only place they are listed together, which is
    /// what makes "is that file actually installed?" a question with an answer.
    ///
    /// **It reports the truth, not the catalogue.** A row says whether the clip
    /// it plays came from a file, from the synthesised stand-in, or from the
    /// fallback track a screen borrows until it is scored — because the
    /// catalogue naming a path is not the same as the path resolving, and that
    /// gap is the whole reason to look.
    ///
    /// The menu bed is stopped on entry: this screen is for listening to one
    /// sound at a time, and a bed underneath it would be the loudest thing in
    /// the mix.
    ///
    /// Reached from DEVELOPMENT. See docs/10-AUDIO.md.
    /// </summary>
    public class AudioListUI : MonoBehaviour
    {
        const float ScreenMargin = 60f, ColumnGap = 24f;
        const float PanelW = 600f, PanelY = -170f, BottomMargin = 44f;
        const float ToolbarY = -170f, HintY = -228f, TableY = -252f;
        const float PanelInset = 42f;
        const float RowPad = 8f;

        enum Channel { All, Music, Ambience, Effects, Interface }

        /// <summary>Where a row's clip actually came from — the thing worth reading.</summary>
        enum Source { File, Synthesised, Fallback, Missing }

        class SoundEntry
        {
            public string key;
            public string name;
            public string detail;
            public Channel channel;
            /// <summary>Catalogued Resources path, or "" for a sound with no file.</summary>
            public string path;
            /// <summary>Path the clip was actually loaded from — differs when a fallback was taken.</summary>
            public string resolvedPath;
            public float volume = 1f;
            public bool loop;
            public Source source;
            public AudioClip clip;
        }

        class Column
        {
            public readonly string Label;
            public readonly float Weight;
            public readonly System.Func<SoundEntry, string> Cell;
            public float Start, End;
            public Column(string label, float weight, System.Func<SoundEntry, string> cell)
            { Label = label; Weight = weight; Cell = cell; }
        }

        static readonly Column[] Columns =
        {
            new Column("SOUND",   2.60f, e => e.name),
            new Column("CHANNEL", 1.20f, e => e.channel.ToString().ToUpperInvariant()),
            new Column("SOURCE",  1.40f, e => SourceText(e.source)),
            new Column("PATH",    3.80f, e => string.IsNullOrEmpty(e.path) ? "— no file" : e.path),
            new Column("LENGTH",  1.00f, e => e.clip == null ? "—" : Duration(e.clip.length)),
        };

        static void NormaliseColumns()
        {
            float total = 0f;
            foreach (var c in Columns) total += c.Weight;
            if (total <= 0f) return;

            float cursor = 0f;
            foreach (var c in Columns)
            {
                c.Start = cursor / total;
                cursor += c.Weight;
                c.End = cursor / total;
            }
        }

        // ------------------------------------------------------------- state
        readonly List<SoundEntry> _entries = new List<SoundEntry>();
        Channel _channel = Channel.All;
        string _search = "";
        SoundEntry _selected;

        RectTransform _header, _rowsContent, _detailBody;
        Text _resultCount, _hint, _detailName, _detailSub, _timeLabel, _playLabel;
        InputField _searchField;
        Slider _scrub, _volume;
        Button _playButton, _loopButton;
        AudioSource _source;
        bool _scrubbing;
        bool _forceLoop;
        readonly Dictionary<string, Image> _rowImages = new Dictionary<string, Image>();
        readonly List<System.Action> _repaints = new List<System.Action>();

        void Start()
        {
            NormaliseColumns();
            AudioManager.Apply();
            MusicManager.Stop();

            // 2D: this is a preview channel of its own, not something in the
            // world, and the listener is on a menu camera that never moves.
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.priority = 90;

            var canvas = UIFactory.CreateCanvas("AudioListCanvas");
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default,
                BackgroundCatalog.DenseScreenScrim);

            LoadEntries();

            BuildHeaderBar(canvas.transform);
            BuildToolbar(canvas.transform);
            BuildTable(canvas.transform);
            BuildDetailPanel(canvas.transform);

            Rebuild();
        }

        // ------------------------------------------------------------ loading

        /// <summary>
        /// Walks all four channels and resolves each clip the same way the
        /// system that owns it would, so a row reports what would really play.
        /// </summary>
        void LoadEntries()
        {
            _entries.Clear();

            foreach (var def in AudioCatalog.AllMusic)
            {
                var entry = new SoundEntry
                {
                    key = "music/" + def.track,
                    name = Pretty(def.track.ToString()),
                    detail = def.description,
                    channel = Channel.Music,
                    path = def.resourcePath,
                    volume = def.volume,
                    loop = def.loop
                };

                entry.clip = Resources.Load<AudioClip>(def.resourcePath);
                if (entry.clip != null)
                {
                    entry.source = Source.File;
                    entry.resolvedPath = def.resourcePath;
                }
                else
                {
                    // Follow the same bounded fallback chain MusicManager walks,
                    // so a screen that borrows the shared bed says so rather
                    // than reading as broken.
                    var hop = def;
                    for (int i = 0; i < 4 && hop != null && hop.fallback != MusicTrack.None; i++)
                    {
                        hop = AudioCatalog.Get(hop.fallback);
                        if (hop == null) break;
                        entry.clip = Resources.Load<AudioClip>(hop.resourcePath);
                        if (entry.clip == null) continue;
                        entry.source = Source.Fallback;
                        entry.resolvedPath = hop.resourcePath;
                        break;
                    }
                    if (entry.clip == null) entry.source = Source.Missing;
                }

                _entries.Add(entry);
            }

            foreach (var def in AudioCatalog.AllAmbience)
            {
                var clip = Resources.Load<AudioClip>(def.resourcePath);
                _entries.Add(new SoundEntry
                {
                    key = "ambience/" + def.track,
                    name = Pretty(def.track.ToString()),
                    detail = def.description,
                    channel = Channel.Ambience,
                    path = def.resourcePath,
                    resolvedPath = clip != null ? def.resourcePath : "",
                    volume = def.volume,
                    loop = true,
                    clip = clip,
                    source = clip != null ? Source.File : Source.Missing
                });
            }

            foreach (EffectSound sound in System.Enum.GetValues(typeof(EffectSound)))
            {
                if (sound == EffectSound.None) continue;

                bool installed = EffectAudio.HasInstalledFile(sound);
                string path = EffectAudio.ResourcePath(sound);
                _entries.Add(new SoundEntry
                {
                    key = "effect/" + sound,
                    name = Pretty(sound.ToString()),
                    detail = EffectUsage(sound),
                    channel = Channel.Effects,
                    path = path,
                    resolvedPath = installed ? path : "",
                    volume = 0.55f,
                    loop = EffectAudio.IsLooping(sound),
                    clip = EffectAudio.Clip(sound),
                    source = installed ? Source.File : Source.Synthesised
                });
            }

            // The interface's own sounds, off the same catalogue every button
            // plays them from — so this register cannot disagree with what is
            // actually heard. The click falls back to the synthesised one when
            // its file is missing; the hover simply goes quiet.
            foreach (var def in AudioCatalog.AllUi)
            {
                var file = Resources.Load<AudioClip>(def.resourcePath);
                bool synth = file == null && def.sound == UiSound.Click;

                _entries.Add(new SoundEntry
                {
                    key = "ui/" + def.sound,
                    name = "UI " + def.sound.ToString().ToLowerInvariant(),
                    detail = def.description,
                    channel = Channel.Interface,
                    path = def.resourcePath,
                    resolvedPath = file != null ? def.resourcePath : "",
                    volume = def.volume,
                    loop = false,
                    clip = file != null ? file : synth ? AudioManager.ClickClip : null,
                    source = file != null ? Source.File : synth ? Source.Synthesised : Source.Missing
                });
            }
        }

        /// <summary>
        /// Which effects carry a given sound, read off the effect catalogue so
        /// the two registers cannot disagree. Sounds no effect carries — the
        /// aircraft, drone and missile signatures, which their own run systems
        /// play — say where they come from instead.
        /// </summary>
        static string EffectUsage(EffectSound sound)
        {
            var users = new List<string>();
            foreach (var def in VfxCatalog.All)
                if (def.sound == sound) users.Add(def.id.ToString());

            if (users.Count > 0)
                return "Played by " + string.Join(", ", users) + ".";

            return sound switch
            {
                EffectSound.JetPass => "Played by the bomber run as an aircraft passes overhead.",
                EffectSound.DroneBuzz => "Played by a UAV run, travelling with the drone.",
                EffectSound.ShahedEngine => "Played by a one-way drone run — a two-stroke rasp, not a quadcopter.",
                EffectSound.MissileMotor => "Played by a missile in flight, travelling with it.",
                EffectSound.MissileIncoming => "Played on a missile's terminal descent.",
                _ => "Carried by no catalogue row — played directly by a strike system."
            };
        }

        // ------------------------------------------------------- header bar

        void BuildHeaderBar(Transform parent)
        {
            var title = UIFactory.CreateText(parent, "AUDIO", 46,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -66), new Vector2(500, 70));

            _resultCount = UIFactory.CreateText(parent, "", 19, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_resultCount.rectTransform, new Vector2(0f, 1f), new Vector2(80, -116), new Vector2(1000, 28));

            UIFactory.CreateBackButton(parent, "BACK TO DEVELOPMENT", Leave);
        }

        void Leave()
        {
            _source.Stop();
            SceneManager.LoadScene(GameConfig.SceneTesting);
        }

        // ---------------------------------------------------------- toolbar

        void BuildToolbar(Transform parent)
        {
            var bar = UIFactory.CreateGroup(parent, "Toolbar");
            StretchToTableWidth(bar, ToolbarY, 46f);

            _searchField = UIFactory.CreateInputField(bar, "Search name or path...", 18);
            UIFactory.Place((RectTransform)_searchField.transform, new Vector2(0f, 1f),
                new Vector2(0, 0), new Vector2(300, 44));
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v == null ? "" : v.Trim();
                Rebuild();
            });

            var channels = (Channel[])System.Enum.GetValues(typeof(Channel));
            var buttons = new List<(Button button, Channel channel)>();
            const float segW = 120f;

            for (int i = 0; i < channels.Length; i++)
            {
                var c = channels[i];
                var btn = UIFactory.CreateButton(bar, c.ToString().ToUpperInvariant(),
                    () => { _channel = c; Rebuild(); },
                    GameConfig.UiPanelLight, GameConfig.UiText, 15);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(318f + i * (segW + 3f), 0), new Vector2(segW, 44));
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 10);
                buttons.Add((btn, c));
            }

            _repaints.Add(() =>
            {
                foreach (var (button, channel) in buttons)
                {
                    bool on = channel == _channel;
                    button.GetComponent<Image>().color = on ? GameConfig.UiAccent : GameConfig.UiPanelLight;
                    var txt = button.GetComponentInChildren<Text>();
                    if (txt == null) continue;
                    txt.color = on ? GameConfig.UiBackground : GameConfig.UiText;
                    txt.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            });

            _hint = UIFactory.CreateText(parent,
                "The background music is stopped while this screen is open. Effect sounds with no " +
                "installed file are synthesised at runtime — the game is audible with no audio assets at all.",
                15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            StretchToTableWidth(_hint.rectTransform, HintY, 22f);
        }

        // ------------------------------------------------------------ table

        void BuildTable(Transform parent)
        {
            var table = UIFactory.CreateGroup(parent, "Table");
            table.anchorMin = new Vector2(0, 0); table.anchorMax = new Vector2(1, 1);
            table.pivot = new Vector2(0.5f, 1f);
            table.offsetMin = new Vector2(ScreenMargin, BottomMargin);
            table.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), TableY);

            _header = UIFactory.CreatePanel(table, "Header", GameConfig.UiPanel);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1);
            _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -40); _header.offsetMax = Vector2.zero;

            foreach (var col in Columns)
            {
                var t = UIFactory.CreateText(_header, col.Label, 14, GameConfig.UiAccent,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                SpanColumn(t.rectTransform, col, RowPad, 32f);
                UIFactory.Fit(t, 10);
            }

            var scroll = UIFactory.CreateScrollView(table, out _rowsContent, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -44);
        }

        static void StretchToTableWidth(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ScreenMargin, top - height);
            rt.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), top);
        }

        static void SpanColumn(RectTransform rt, Column col, float inset, float height)
        {
            rt.anchorMin = new Vector2(col.Start, 0.5f);
            rt.anchorMax = new Vector2(col.End, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, -height * 0.5f);
            rt.offsetMax = new Vector2(-4f, height * 0.5f);
        }

        static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        // ---------------------------------------------------------- rebuild

        List<SoundEntry> Visible()
        {
            var list = new List<SoundEntry>();
            foreach (var e in _entries)
            {
                if (_channel != Channel.All && e.channel != _channel) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    Miss(e.name) && Miss(e.path) && Miss(e.detail)) continue;
                list.Add(e);
            }
            return list;

            bool Miss(string s) =>
                string.IsNullOrEmpty(s) ||
                s.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        void Rebuild()
        {
            foreach (var repaint in _repaints) repaint();

            ClearChildren(_rowsContent);
            _rowImages.Clear();

            var rows = Visible();
            foreach (var e in rows) CreateRow(e);

            int missing = 0, synthesised = 0;
            foreach (var e in _entries)
            {
                if (e.source == Source.Missing) missing++;
                else if (e.source == Source.Synthesised) synthesised++;
            }

            _resultCount.text = rows.Count == _entries.Count
                ? $"{rows.Count} sounds · {synthesised} synthesised, {missing} with no clip at all · docs/10-AUDIO.md"
                : $"{rows.Count} of {_entries.Count} sounds · docs/10-AUDIO.md";

            if (rows.Count == 0)
            {
                var empty = UIFactory.CreateText(_rowsContent, "No sounds match these filters.",
                    18, GameConfig.UiTextDim);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 60);
                Select(null);
                return;
            }

            if (_selected != null && rows.Contains(_selected)) Highlight();
            else Select(rows[0]);
        }

        void CreateRow(SoundEntry entry)
        {
            var row = UIFactory.CreatePanel(_rowsContent, "Row_" + entry.key, RowColour(false));
            row.sizeDelta = new Vector2(0, 44);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => Select(entry));
            _rowImages[entry.key] = row.GetComponent<Image>();

            for (int i = 0; i < Columns.Length; i++)
            {
                var col = Columns[i];
                var t = UIFactory.CreateText(row, col.Cell(entry), i == 0 ? 16 : 14,
                    i == 0 ? GameConfig.UiText
                           : (i == 2 ? SourceColour(entry.source) : GameConfig.UiTextDim),
                    TextAnchor.MiddleLeft);
                SpanColumn(t.rectTransform, col, 4f, 34f);
                UIFactory.Fit(t, 10);
            }
        }

        static Color RowColour(bool selected) =>
            selected
                ? new Color(GameConfig.UiAccent.r, GameConfig.UiAccent.g, GameConfig.UiAccent.b, 0.22f)
                : new Color(1f, 1f, 1f, 0.03f);

        static string SourceText(Source s) => s switch
        {
            Source.File => "File",
            Source.Synthesised => "Synthesised",
            Source.Fallback => "Fallback",
            _ => "No clip"
        };

        static Color SourceColour(Source s) => s switch
        {
            Source.File => UiTheme.Success,
            Source.Synthesised => GameConfig.UiAccent,
            Source.Fallback => UiTheme.Warning,
            _ => UiTheme.Hostile
        };

        // ----------------------------------------------------- detail panel

        void BuildDetailPanel(Transform parent)
        {
            var panel = UIFactory.CreatePanel(parent, "DetailPanel", GameConfig.UiPanel);
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-(ScreenMargin + PanelW), BottomMargin);
            panel.offsetMax = new Vector2(-ScreenMargin, PanelY);

            float inner = PanelW - PanelInset * 2f;

            _detailName = UIFactory.CreateText(panel, "", 26, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -16), new Vector2(inner, 34));

            _detailSub = UIFactory.CreateText(panel, "", 15, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            UIFactory.Place(_detailSub.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -54), new Vector2(inner, 52));

            // --- transport ---
            var scrubTrack = UIFactory.CreateGroup(panel, "Scrub");
            UIFactory.Place(scrubTrack, new Vector2(0f, 1f), new Vector2(PanelInset, -122), new Vector2(inner, 34));
            _scrub = UIFactory.CreateSlider(scrubTrack, 0f, OnScrub);
            UIFactory.Stretch((RectTransform)_scrub.transform);

            _timeLabel = UIFactory.CreateText(panel, "0:00 / 0:00", 14,
                GameConfig.UiTextDim, TextAnchor.MiddleRight);
            UIFactory.Place(_timeLabel.rectTransform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -158), new Vector2(inner, 20));

            float third = (inner - 12f) / 3f;

            _playButton = UIFactory.CreateButton(panel, "PLAY", TogglePlay,
                UiTheme.Success, Color.white, 16);
            UIFactory.Place((RectTransform)_playButton.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -184), new Vector2(third, 42));
            _playLabel = _playButton.GetComponentInChildren<Text>();

            var stop = UIFactory.CreateButton(panel, "STOP", StopPlayback,
                GameConfig.UiPanelLight, GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)stop.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset + third + 6f, -184), new Vector2(third, 42));

            _loopButton = UIFactory.CreateButton(panel, "LOOP", ToggleLoop,
                GameConfig.UiPanelLight, GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)_loopButton.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset + (third + 6f) * 2f, -184), new Vector2(third, 42));

            var volLabel = UIFactory.CreateText(panel, "PREVIEW LEVEL", 13,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(volLabel.rectTransform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -238), new Vector2(inner, 18));

            var volTrack = UIFactory.CreateGroup(panel, "Volume");
            UIFactory.Place(volTrack, new Vector2(0f, 1f), new Vector2(PanelInset, -260), new Vector2(inner, 34));
            _volume = UIFactory.CreateSlider(volTrack, 1f, v => { if (_source != null) _source.volume = v; });
            UIFactory.Stretch((RectTransform)_volume.transform);

            var scroll = UIFactory.CreateScrollView(panel, out _detailBody, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(PanelInset, 16);
            srt.offsetMax = new Vector2(-PanelInset, -308);
        }

        void Select(SoundEntry entry)
        {
            if (_selected != null && _rowImages.TryGetValue(_selected.key, out var previous) && previous != null)
                previous.color = RowColour(false);

            StopPlayback();

            _selected = entry;
            // The row's own catalogued level is the sensible starting point:
            // the point of a preview is what it sounds like in the game, and
            // music sits at 0.45 there for a reason.
            _forceLoop = entry != null && entry.loop;
            if (_volume != null) _volume.SetValueWithoutNotify(entry?.volume ?? 1f);
            if (_source != null) _source.volume = entry?.volume ?? 1f;

            Highlight();
            RefreshDetail();
        }

        void Highlight()
        {
            if (_selected == null) return;
            if (_rowImages.TryGetValue(_selected.key, out var image) && image != null)
                image.color = RowColour(true);
        }

        void RefreshDetail()
        {
            var e = _selected;

            _detailName.text = e == null ? "—" : e.name.ToUpperInvariant();
            _detailSub.text = e == null ? "No sound selected" : e.detail;

            RefreshTransport();
            BuildFacts(e);
        }

        void BuildFacts(SoundEntry e)
        {
            ClearChildren(_detailBody);
            if (e == null) return;

            Section("REGISTER");
            Fact("Channel", e.channel.ToString());
            Fact("Catalogue path", string.IsNullOrEmpty(e.path) ? "— no file, synthesised" : "Resources/" + e.path);
            Fact("Source", SourceText(e.source));
            if (e.source == Source.Fallback)
                Fact("Loaded from", "Resources/" + e.resolvedPath);
            Fact("Catalogue level", $"{e.volume:0.00}");
            Fact("Loops in game", e.loop ? "Yes" : "No");

            Section("CLIP");
            if (e.clip == null)
            {
                Fact("Status", "No clip resolves — this sound is silent in game.");
            }
            else
            {
                Fact("Length", Duration(e.clip.length));
                Fact("Channels", e.clip.channels.ToString());
                Fact("Sample rate", $"{e.clip.frequency:n0} Hz");
                Fact("Samples", $"{e.clip.samples:n0}");
            }

            Section("NOTES");
            var note = UIFactory.CreateText(_detailBody, NoteFor(e), 14,
                GameConfig.UiTextDim, TextAnchor.UpperLeft);
            note.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ((RectTransform)note.transform).sizeDelta = new Vector2(0, 60);
        }

        static string NoteFor(SoundEntry e) => e.source switch
        {
            Source.Missing =>
                "Nothing loads at this path. Audio files must live under Assets/Resources — nothing " +
                "else is loadable at runtime. See docs/10-AUDIO.md.",
            Source.Fallback =>
                "This screen has no track of its own yet and borrows the shared bed. Drop a file at " +
                "the catalogue path above and it is used automatically — no code change.",
            Source.Synthesised =>
                "Built at runtime by ProceduralAudio. Drop a real file at the catalogue path and it " +
                "takes over; the synthesised version exists so the game is audible with no audio assets.",
            _ =>
                "Loaded from the file above. Levels live in AudioCatalog, not at call sites — see " +
                "golden rule 9 in CLAUDE.md."
        };

        void Section(string label)
        {
            var t = UIFactory.CreateText(_detailBody, label, 13, GameConfig.UiAccent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 28);
        }

        const float FactSplit = 0.42f;

        void Fact(string label, string value)
        {
            var row = UIFactory.CreateGroup(_detailBody, "Fact_" + label);
            row.sizeDelta = new Vector2(0, 24);

            var l = UIFactory.CreateText(row, label, 14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(FactSplit, 1);
            l.rectTransform.offsetMin = Vector2.zero; l.rectTransform.offsetMax = new Vector2(-6, 0);
            UIFactory.Fit(l, 10);

            var v = UIFactory.CreateText(row, value, 14, GameConfig.UiText, TextAnchor.MiddleRight);
            v.rectTransform.anchorMin = new Vector2(FactSplit, 0); v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(-4, 0);
            UIFactory.Fit(v, 10);
        }

        // -------------------------------------------------------- transport

        void TogglePlay()
        {
            if (_selected?.clip == null) return;

            if (_source.isPlaying) { _source.Pause(); RefreshTransport(); return; }

            if (_source.clip != _selected.clip)
            {
                _source.clip = _selected.clip;
                _source.time = 0f;
            }
            _source.loop = _forceLoop;
            _source.Play();
            RefreshTransport();
        }

        void StopPlayback()
        {
            if (_source == null) return;
            _source.Stop();
            _source.time = 0f;
            if (_scrub != null) _scrub.SetValueWithoutNotify(0f);
            RefreshTransport();
        }

        void ToggleLoop()
        {
            _forceLoop = !_forceLoop;
            if (_source != null) _source.loop = _forceLoop;
            RefreshTransport();
        }

        /// <summary>
        /// Seeking. Guarded by <see cref="_scrubbing"/> because
        /// <see cref="Update"/> writes the same slider every frame — without the
        /// guard the two would fight and the handle would spring back under the
        /// cursor.
        /// </summary>
        void OnScrub(float value01)
        {
            if (_scrubbing || _source == null || _source.clip == null) return;
            _scrubbing = true;
            // A sample exactly at clip.length is out of range and throws.
            _source.time = Mathf.Clamp(value01 * _source.clip.length, 0f, _source.clip.length - 0.01f);
            _scrubbing = false;
        }

        void RefreshTransport()
        {
            bool playable = _selected?.clip != null;

            if (_playLabel != null)
                _playLabel.text = !playable ? "NO CLIP" : _source.isPlaying ? "PAUSE" : "PLAY";
            if (_playButton != null)
                _playButton.GetComponent<Image>().color = playable
                    ? (_source.isPlaying ? UiTheme.Warning : UiTheme.Success)
                    : GameConfig.UiPanelLight;

            if (_loopButton != null)
            {
                var caption = _loopButton.GetComponentInChildren<Text>();
                if (caption != null)
                {
                    caption.text = _forceLoop ? "LOOP: ON" : "LOOP: OFF";
                    caption.color = _forceLoop ? GameConfig.UiBackground : GameConfig.UiText;
                }
                _loopButton.GetComponent<Image>().color =
                    _forceLoop ? GameConfig.UiAccent : GameConfig.UiPanelLight;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                var focused = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
                if (focused == null || focused.GetComponent<InputField>() == null) { Leave(); return; }
            }

            if (_selected?.clip == null || _scrub == null) return;

            float length = Mathf.Max(0.01f, _selected.clip.length);
            float t = _source.clip == _selected.clip ? _source.time : 0f;

            _scrubbing = true;
            _scrub.SetValueWithoutNotify(Mathf.Clamp01(t / length));
            _scrubbing = false;

            _timeLabel.text = $"{Duration(t)} / {Duration(length)}";

            // A non-looping clip that has run out leaves the button saying PAUSE
            // for the rest of the session unless the transport is re-read.
            if (!_source.isPlaying && _playLabel != null && _playLabel.text == "PAUSE")
                RefreshTransport();
        }

        void OnDestroy()
        {
            if (_source != null) _source.Stop();
        }

        // ------------------------------------------------------------ naming

        static string Duration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>"ArtilleryMedium" -> "Artillery medium".</summary>
        static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
