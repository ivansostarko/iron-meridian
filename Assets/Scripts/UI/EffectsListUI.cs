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
    /// PARTICLES — every row of <see cref="VfxCatalog"/>, played in 3D
    /// with the sound it carries.
    ///
    /// **Why the lab exists.** The catalogue is the register of what the game
    /// can draw, and until now the only way to see an entry was to make the
    /// event that triggers it happen on the map — which for half the rows means
    /// calling a fire mission and watching a 300 m burst from 20 km up. Here
    /// each effect is shown at its **authored** size, close up, on a ground
    /// plane, looping, with the metre figure it is actually blown up to written
    /// beside it.
    ///
    /// **It shows what the game shows.** Prefab resolution goes through
    /// <see cref="VfxSystem.LoadPrefab"/> and audio through
    /// <see cref="EffectAudio"/>, so a row that falls back to a procedural
    /// stand-in here is one that falls back on the map too — which is the
    /// single most useful thing this screen reports, given the shipped VFX pack
    /// is URP-only and this project runs the built-in pipeline.
    ///
    /// Reached from DEVELOPMENT. See docs/08-PARTICLE-SYSTEMS.md.
    /// </summary>
    public class EffectsListUI : MonoBehaviour
    {
        const float ScreenMargin = 60f, ColumnGap = 24f;
        const float PanelW = 620f, PanelY = -170f, BottomMargin = 44f;
        const float ToolbarY = -170f, HintY = -228f, TableY = -252f;
        const float PanelInset = 42f;
        const float RowPad = 8f;

        /// <summary>Families, in the order the catalogue declares them.</summary>
        enum Family { All, Fire, Smoke, Explosion, Artillery, Air, Uav, Missile, Other }

        class Column
        {
            public readonly string Label;
            public readonly float Weight;
            public readonly System.Func<VfxDef, string> Cell;
            public float Start, End;
            public Column(string label, float weight, System.Func<VfxDef, string> cell)
            { Label = label; Weight = weight; Cell = cell; }
        }

        static readonly Column[] Columns =
        {
            new Column("EFFECT",  3.00f, d => Pretty(d.id.ToString())),
            new Column("FAMILY",  1.40f, d => FamilyOf(d).ToString().ToUpperInvariant()),
            new Column("SOURCE",  1.60f, d => VfxSystem.LoadPrefab(d) != null ? "Prefab" : "Procedural"),
            new Column("SCALE",   1.10f, d => $"{d.scaleMeters:0} m"),
            new Column("LIFE",    1.10f, d => d.Loops ? "loops" : $"{d.lifeSeconds:0.#} s"),
            new Column("SOUND",   1.60f, d => d.sound == EffectSound.None ? "—" : Pretty(d.sound.ToString())),
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
        Family _family = Family.All;
        string _search = "";
        VfxDef _selected;

        RectTransform _header, _rowsContent, _detailBody;
        Text _resultCount, _hint, _detailName, _detailSub, _sourceLine;
        InputField _searchField;
        VfxPreview _preview;
        Button _loopButton;
        readonly Dictionary<VfxId, Image> _rowImages = new Dictionary<VfxId, Image>();
        readonly List<System.Action> _repaints = new List<System.Action>();

        void Start()
        {
            NormaliseColumns();
            AudioManager.Apply();
            // The lab is for listening to effects. A music bed under them would
            // be the loudest thing in the mix and the whole point of the screen
            // is what the effect sounds like on its own.
            MusicManager.Stop();

            var canvas = UIFactory.CreateCanvas("EffectsListCanvas");
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default,
                BackgroundCatalog.DenseScreenScrim);

            BuildHeaderBar(canvas.transform);
            BuildToolbar(canvas.transform);
            BuildTable(canvas.transform);
            BuildDetailPanel(canvas.transform);

            Rebuild();
        }

        // ------------------------------------------------------- header bar

        void BuildHeaderBar(Transform parent)
        {
            var title = UIFactory.CreateText(parent, "PARTICLES", 46,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -66), new Vector2(760, 70));

            _resultCount = UIFactory.CreateText(parent, "", 19, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_resultCount.rectTransform, new Vector2(0f, 1f), new Vector2(80, -116), new Vector2(900, 28));

            UIFactory.CreateBackButton(parent, "BACK TO DEVELOPMENT",
                () => SceneManager.LoadScene(GameConfig.SceneTesting));
        }

        // ---------------------------------------------------------- toolbar

        void BuildToolbar(Transform parent)
        {
            var bar = UIFactory.CreateGroup(parent, "Toolbar");
            StretchToTableWidth(bar, ToolbarY, 46f);

            _searchField = UIFactory.CreateInputField(bar, "Search effect or sound...", 18);
            UIFactory.Place((RectTransform)_searchField.transform, new Vector2(0f, 1f),
                new Vector2(0, 0), new Vector2(300, 44));
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v == null ? "" : v.Trim();
                Rebuild();
            });

            var families = (Family[])System.Enum.GetValues(typeof(Family));
            var buttons = new List<(Button button, Family family)>();
            const float segW = 96f;

            for (int i = 0; i < families.Length; i++)
            {
                var f = families[i];
                var btn = UIFactory.CreateButton(bar, f.ToString().ToUpperInvariant(),
                    () => { _family = f; Rebuild(); },
                    GameConfig.UiPanelLight, GameConfig.UiText, 14);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(318f + i * (segW + 2f), 0), new Vector2(segW, 44));
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 10);
                buttons.Add((btn, f));
            }

            _repaints.Add(() =>
            {
                foreach (var (button, family) in buttons)
                {
                    bool on = family == _family;
                    button.GetComponent<Image>().color = on ? GameConfig.UiAccent : GameConfig.UiPanelLight;
                    var txt = button.GetComponentInChildren<Text>();
                    if (txt == null) continue;
                    txt.color = on ? GameConfig.UiBackground : GameConfig.UiText;
                    txt.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            });

            _hint = UIFactory.CreateText(parent,
                "Effects are shown at their authored size — about one metre — not at the map scale " +
                "listed under SCALE. Click a row to play it; drag the preview to orbit, scroll to zoom.",
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

        List<VfxDef> Visible()
        {
            var list = new List<VfxDef>();
            foreach (var d in VfxCatalog.All)
            {
                if (_family != Family.All && FamilyOf(d) != _family) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    d.id.ToString().IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    d.sound.ToString().IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                list.Add(d);
            }
            return list;
        }

        void Rebuild()
        {
            foreach (var repaint in _repaints) repaint();

            ClearChildren(_rowsContent);
            _rowImages.Clear();

            var rows = Visible();
            foreach (var d in rows) CreateRow(d);

            int authored = 0;
            foreach (var d in VfxCatalog.All) if (VfxSystem.LoadPrefab(d) != null) authored++;

            _resultCount.text = rows.Count == VfxCatalog.All.Count
                ? $"{rows.Count} effects · {authored} from authored prefabs, " +
                  $"{VfxCatalog.All.Count - authored} procedural · docs/08-PARTICLE-SYSTEMS.md"
                : $"{rows.Count} of {VfxCatalog.All.Count} effects · docs/08-PARTICLE-SYSTEMS.md";

            if (rows.Count == 0)
            {
                var empty = UIFactory.CreateText(_rowsContent, "No effects match these filters.",
                    18, GameConfig.UiTextDim);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 60);
                Select(null);
                return;
            }

            if (_selected != null && rows.Contains(_selected)) Highlight();
            else Select(rows[0]);
        }

        void CreateRow(VfxDef def)
        {
            var row = UIFactory.CreatePanel(_rowsContent, "Row_" + def.id, RowColour(def, false));
            row.sizeDelta = new Vector2(0, 44);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => Select(def));
            _rowImages[def.id] = row.GetComponent<Image>();

            for (int i = 0; i < Columns.Length; i++)
            {
                var col = Columns[i];
                var t = UIFactory.CreateText(row, col.Cell(def), i == 0 ? 16 : 14,
                    i == 0 ? GameConfig.UiText : GameConfig.UiTextDim, TextAnchor.MiddleLeft);
                SpanColumn(t.rectTransform, col, 4f, 34f);
                UIFactory.Fit(t, 10);
            }

            // The tint is the effect's own, which turns the table into a legend:
            // the fire rows are orange, the smoke rows grey, and the eye finds
            // the family before it reads the column.
            var swatch = UIFactory.CreatePanel(row, "Swatch", def.tint);
            swatch.anchorMin = new Vector2(0, 0); swatch.anchorMax = new Vector2(0, 1);
            swatch.pivot = new Vector2(0, 0.5f);
            swatch.sizeDelta = new Vector2(3, 0);
            swatch.GetComponent<Image>().raycastTarget = false;
        }

        static Color RowColour(VfxDef def, bool selected) =>
            selected
                ? new Color(GameConfig.UiAccent.r, GameConfig.UiAccent.g, GameConfig.UiAccent.b, 0.22f)
                : new Color(1f, 1f, 1f, 0.03f);

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

            _detailSub = UIFactory.CreateText(panel, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_detailSub.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -52), new Vector2(inner, 22));

            var previewSize = new Vector2(inner, 340);
            _preview = VfxPreview.Create(panel, previewSize);
            UIFactory.Place((RectTransform)_preview.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -84), previewSize);

            // --- transport ---
            float y = -436f;
            float third = (inner - 12f) / 3f;

            var replay = UIFactory.CreateButton(panel, "REPLAY", () => _preview.Replay(),
                UiTheme.Success, Color.white, 16);
            UIFactory.Place((RectTransform)replay.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset, y), new Vector2(third, 40));

            var stop = UIFactory.CreateButton(panel, "STOP", () => _preview.Stop(),
                GameConfig.UiPanelLight, GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)stop.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset + third + 6f, y), new Vector2(third, 40));

            _loopButton = UIFactory.CreateButton(panel, "LOOP: ON", ToggleLoop,
                GameConfig.UiAccent, GameConfig.UiBackground, 16);
            UIFactory.Place((RectTransform)_loopButton.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset + (third + 6f) * 2f, y), new Vector2(third, 40));

            _sourceLine = UIFactory.CreateText(panel, "", 14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_sourceLine.rectTransform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -484), new Vector2(inner, 20));

            var scroll = UIFactory.CreateScrollView(panel, out _detailBody, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(PanelInset, 16);
            srt.offsetMax = new Vector2(-PanelInset, -512);
        }

        void ToggleLoop()
        {
            _preview.Looping = !_preview.Looping;
            var caption = _loopButton.GetComponentInChildren<Text>();
            if (caption != null)
            {
                caption.text = _preview.Looping ? "LOOP: ON" : "LOOP: OFF";
                caption.color = _preview.Looping ? GameConfig.UiBackground : GameConfig.UiText;
            }
            _loopButton.GetComponent<Image>().color =
                _preview.Looping ? GameConfig.UiAccent : GameConfig.UiPanelLight;
        }

        void Select(VfxDef def)
        {
            if (_selected != null && _rowImages.TryGetValue(_selected.id, out var previous) && previous != null)
                previous.color = RowColour(_selected, false);

            _selected = def;
            Highlight();
            RefreshDetail();
        }

        void Highlight()
        {
            if (_selected == null) return;
            if (_rowImages.TryGetValue(_selected.id, out var image) && image != null)
                image.color = RowColour(_selected, true);
        }

        void RefreshDetail()
        {
            var def = _selected;

            _detailName.text = def == null ? "—" : Pretty(def.id.ToString()).ToUpperInvariant();
            _detailSub.text = def == null
                ? "No effect selected"
                : $"{FamilyOf(def)}  ·  {(def.Loops ? "looping" : $"{def.lifeSeconds:0.#} s one-shot")}" +
                  $"  ·  {def.scaleMeters:0} m on the map";

            _preview.Show(def);

            _sourceLine.text = def == null ? ""
                : _preview.UsingAuthoredPrefab
                    ? $"Drawn from Resources/{def.prefabPath}"
                    : string.IsNullOrEmpty(def.prefabPath)
                        ? $"Procedural — ProceduralVfx.{def.fallback}. No prefab is catalogued for this row."
                        : $"Procedural — ProceduralVfx.{def.fallback}. " +
                          $"Resources/{def.prefabPath} is missing, or its shaders do not run on this pipeline.";

            BuildFacts(def);
        }

        void BuildFacts(VfxDef def)
        {
            ClearChildren(_detailBody);
            if (def == null) return;

            Section("CATALOGUE");
            Fact("Id", def.id.ToString());
            Fact("Family", FamilyOf(def).ToString());
            Fact("Map scale", $"{def.scaleMeters:0} m across");
            Fact("Lifetime", def.Loops ? "Loops until stopped" : $"{def.lifeSeconds:0.##} s");
            Fact("Priority", def.priority.ToString());
            Fact("Tint", "#" + ColorUtility.ToHtmlStringRGB(def.tint));

            Section("SOURCE");
            Fact("Authored prefab", string.IsNullOrEmpty(def.prefabPath) ? "— none catalogued" : def.prefabPath);
            Fact("Procedural fallback", def.fallback.ToString());
            Fact("Playing", _preview.UsingAuthoredPrefab ? "The authored prefab" : "The procedural fallback");

            Section("SOUND");
            if (def.sound == EffectSound.None)
            {
                Fact("Effect sound", "— silent");
            }
            else
            {
                Fact("Effect sound", Pretty(def.sound.ToString()));
                Fact("Loops", EffectAudio.IsLooping(def.sound) ? "Yes" : "No, one-shot");
                string path = EffectAudio.ResourcePath(def.sound);
                Fact("File", string.IsNullOrEmpty(path) ? "— synthesised only" : "Resources/" + path);
                Fact("Playing", EffectAudio.HasInstalledFile(def.sound)
                    ? "The installed file" : "The synthesised stand-in");
            }

            Section("NOTES");
            var note = UIFactory.CreateText(_detailBody,
                "Priority decides what survives when the concurrent-effect budget " +
                $"({GameConfig.VfxMaxConcurrent}) forces an eviction — higher wins. " +
                "Everything here is authored at roughly one world unit and scaled by " +
                "VfxSystem to the map figure above.",
                14, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            note.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ((RectTransform)note.transform).sizeDelta = new Vector2(0, 60);
        }

        void Section(string label)
        {
            var t = UIFactory.CreateText(_detailBody, label, 13, GameConfig.UiAccent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 28);
        }

        const float FactSplit = 0.44f;

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

        // ------------------------------------------------------------ naming

        /// <summary>
        /// Which family an effect belongs to, worked out from its id. Derived
        /// rather than stored: the catalogue already groups these rows by name
        /// and by the comment blocks around them, and a second field to keep in
        /// step would be a second thing to get wrong.
        /// </summary>
        static Family FamilyOf(VfxDef def)
        {
            string id = def.id.ToString();

            if (id.StartsWith("Artillery")) return Family.Artillery;
            if (id.StartsWith("Aerial")) return Family.Air;
            if (id.StartsWith("Uav") || id.StartsWith("Shahed") || id == "ReconMarker") return Family.Uav;
            if (id.StartsWith("Missile")) return Family.Missile;
            if (id.Contains("Smoke")) return Family.Smoke;
            if (id.Contains("Fire")) return Family.Fire;
            if (id == "Explosion" || id == "ImpactBurst" || id == "WeaponFire") return Family.Explosion;
            return Family.Other;
        }

        /// <summary>"ArtilleryMediumBurst" -> "Artillery medium burst".</summary>
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

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            var focused = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (focused != null && focused.GetComponent<InputField>() != null) return;

            SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
