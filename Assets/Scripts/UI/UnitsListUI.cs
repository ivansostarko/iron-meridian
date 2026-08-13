using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Models;

namespace IronMeridian.UI
{
    /// <summary>
    /// Reference catalogue: every unit definition in a filterable, sortable
    /// table, with a detail panel on the right showing the selected type's
    /// animated 3D model and full stat block. Reached from Testing.
    /// </summary>
    public class UnitsListUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        //
        // Everything here is anchor-driven rather than laid out in absolute
        // pixels. The canvas scaler matches width and height equally, so the
        // reference width is only 1920 at exactly 16:9 — at any other aspect it
        // shrinks, and a table pinned at a fixed 1185 px ran under a detail
        // panel pinned to the right edge. Stretching the table between the two
        // insets, and giving its columns proportional widths, means the whole
        // row is visible at every window shape.
        /// <summary>Inset from the left edge to the table, and from the right edge to the detail panel.</summary>
        const float ScreenMargin = 60f;
        /// <summary>Gap between the table and the detail panel.</summary>
        const float ColumnGap = 24f;
        /// <summary>Detail panel width. The one fixed dimension — its stat rows need a stable column split.</summary>
        const float PanelW = 560f;
        /// <summary>Top of the table and of the detail panel, measured from the top of the screen.</summary>
        const float TableY = -256f, PanelY = -170f;
        /// <summary>Bottom margin under both, so neither runs off a short window.</summary>
        const float BottomMargin = 44f;
        const float RowPad = 8f;      // CreateScrollView's content padding

        enum SortKey { Name, Category, Attack, Defence, Armour, Range, Speed, Manpower }
        enum TeamView { Both, Friendly, Enemy }
        enum CategoryView { All, Ground, Drone }

        /// <summary>
        /// A table column, sized as a **share of the table width** rather than in
        /// pixels. <see cref="Start"/> and <see cref="End"/> are the normalised
        /// bounds every header cell and row cell anchors to, so the table reflows
        /// with the window and no column can be cut off the right-hand edge.
        /// </summary>
        // Not a readonly struct: Start/End are computed once at startup, and an
        // array element is addressable so they can be written in place.
        struct Column
        {
            public readonly string Label;
            public readonly float Weight;
            public readonly SortKey? Sort;
            public float Start, End;      // filled in by NormaliseColumns

            public Column(string label, float weight, SortKey? sort = null)
            { Label = label; Weight = weight; Sort = sort; Start = 0f; End = 0f; }
        }

        static readonly Column[] Columns =
        {
            // ICON carries two 44 px icons side by side, so it needs the widest
            // share that is not text.
            new Column("ICON",     1.20f),
            new Column("NAME",     3.10f, SortKey.Name),
            new Column("CATEGORY", 1.25f, SortKey.Category),
            new Column("ATK",      0.70f, SortKey.Attack),
            new Column("DEF",      0.70f, SortKey.Defence),
            new Column("ARM",      0.70f, SortKey.Armour),
            new Column("RANGE",    1.00f, SortKey.Range),
            new Column("SPEED",    1.10f, SortKey.Speed),
            new Column("MANPOWER", 1.15f, SortKey.Manpower),
        };

        /// <summary>
        /// Turns the weights into cumulative 0..1 bounds. Run once at startup so
        /// every cell can anchor straight to them.
        /// </summary>
        static void NormaliseColumns()
        {
            float total = 0f;
            foreach (var c in Columns) total += c.Weight;
            if (total <= 0f) return;

            float cursor = 0f;
            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i].Start = cursor / total;
                cursor += Columns[i].Weight;
                Columns[i].End = cursor / total;
            }
        }

        // ------------------------------------------------------------- state
        SortKey _sortKey = SortKey.Name;
        bool _sortAscending = true;
        TeamView _team = TeamView.Both;
        CategoryView _category = CategoryView.All;
        string _search = "";
        UnitDefinition _selected;

        RectTransform _header, _rowsContent;
        Text _resultCount;
        InputField _searchField;
        readonly Dictionary<string, Image> _rowImages = new Dictionary<string, Image>();
        /// <summary>Toolbar controls that repaint themselves from filter state on every rebuild.</summary>
        readonly List<System.Action> _repaints = new List<System.Action>();

        // detail panel
        ModelPreview _preview;
        Text _detailName, _detailSub;
        RectTransform _detailIcons, _detailStats;

        void Start()
        {
            NormaliseColumns();
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("UnitsListCanvas");

            // Dense data table over artwork: lean on the scrim so every row stays legible.
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
            var title = UIFactory.CreateText(parent, "UNITS LIST", 48,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(600, 70));

            _resultCount = UIFactory.CreateText(parent, "", 20, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_resultCount.rectTransform, new Vector2(0f, 1f), new Vector2(80, -122), new Vector2(700, 30));

            var back = UIFactory.CreateButton(parent, "< BACK",
                () => SceneManager.LoadScene(GameConfig.SceneTesting),
                GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80, -70), new Vector2(180, 60));
        }

        // ---------------------------------------------------------- toolbar

        void BuildToolbar(Transform parent)
        {
            var bar = UIFactory.CreateGroup(parent, "Toolbar");
            StretchToTableWidth(bar, -170f, 52f);

            _searchField = UIFactory.CreateInputField(bar, "Search name, id or ammo...", 18);
            UIFactory.Place((RectTransform)_searchField.transform, new Vector2(0f, 1f),
                new Vector2(0, 0), new Vector2(320, 46));
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v == null ? "" : v.Trim();
                Rebuild();
            });

            Segmented(bar, 340f, new[] { "BOTH", "FRIENDLY", "ENEMY" }, () => (int)_team,
                i => { _team = (TeamView)i; Rebuild(); });

            Segmented(bar, 660f, new[] { "ALL", "GROUND", "DRONE" }, () => (int)_category,
                i => { _category = (CategoryView)i; Rebuild(); });

            var reset = UIFactory.CreateButton(bar, "RESET", () =>
            {
                _team = TeamView.Both; _category = CategoryView.All; _search = "";
                _sortKey = SortKey.Name; _sortAscending = true;
                // Clearing the field fires onValueChanged, which rebuilds — but
                // only if the text was non-empty, so rebuild unconditionally after.
                if (_searchField != null) _searchField.text = "";
                Rebuild();
            }, GameConfig.UiPanelLight, GameConfig.UiTextDim, 16);
            UIFactory.Place((RectTransform)reset.transform, new Vector2(0f, 1f), new Vector2(984, 0), new Vector2(110, 46));

            // Both sides field the same catalogue, so say what the filter does
            // rather than letting it look like it is dropping unit types.
            var hint = UIFactory.CreateText(parent,
                "Both teams field the same catalogue — the affiliation filter switches which icon set is shown. " +
                "Click a column heading to sort; click a row for its 3D model.",
                15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            StretchToTableWidth(hint.rectTransform, -228f, 22f);
        }

        /// <summary>
        /// A row of mutually exclusive buttons. The active one is repainted on
        /// every rebuild, so the control always reflects the real filter state.
        /// </summary>
        void Segmented(Transform parent, float x, string[] labels,
            System.Func<int> current, System.Action<int> onPick)
        {
            const float segW = 106f;
            var buttons = new Button[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var btn = UIFactory.CreateButton(parent, labels[i], () => onPick(index),
                    GameConfig.UiPanelLight, GameConfig.UiText, 16);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(x + i * (segW + 2f), 0), new Vector2(segW, 46));
                buttons[i] = btn;
            }

            _repaints.Add(() =>
            {
                int active = current();
                for (int i = 0; i < buttons.Length; i++)
                {
                    bool on = i == active;
                    buttons[i].GetComponent<Image>().color = on ? GameConfig.UiAccent : GameConfig.UiPanelLight;
                    var txt = buttons[i].GetComponentInChildren<Text>();
                    if (txt != null)
                    {
                        txt.color = on ? GameConfig.UiBackground : GameConfig.UiText;
                        txt.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                    }
                }
            });
        }

        // ------------------------------------------------------------ table

        void BuildTable(Transform parent)
        {
            var table = UIFactory.CreateGroup(parent, "Table");
            // Full height between the header block and the bottom margin, so the
            // list grows with the window instead of stopping at a fixed 780 px.
            table.anchorMin = new Vector2(0, 0); table.anchorMax = new Vector2(1, 1);
            table.pivot = new Vector2(0.5f, 1f);
            table.offsetMin = new Vector2(ScreenMargin, BottomMargin);
            table.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), TableY);

            _header = UIFactory.CreatePanel(table, "Header", GameConfig.UiPanel);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1);
            _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -42); _header.offsetMax = Vector2.zero;

            var scroll = UIFactory.CreateScrollView(table, out _rowsContent, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -46);
        }

        /// <summary>
        /// Spans a rect across the table's column of the screen: from the left
        /// margin to where the detail panel begins. Used by the toolbar and the
        /// hint line so they track the table rather than drifting over the panel.
        /// </summary>
        static void StretchToTableWidth(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ScreenMargin, top - height);
            rt.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), top);
        }

        /// <summary>
        /// Empties a container now rather than at end of frame. `Destroy` is
        /// deferred, so a layout group would spend this frame measuring the old
        /// children alongside the new ones — the table visibly jumps on every
        /// filter change. Detaching first takes them out of the layout at once.
        /// </summary>
        static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        void BuildHeaderCells()
        {
            ClearChildren(_header);

            foreach (var col in Columns)
            {
                bool active = col.Sort.HasValue && col.Sort.Value == _sortKey;
                string label = col.Label + (active ? (_sortAscending ? "  ▲" : "  ▼") : "");

                if (!col.Sort.HasValue)
                {
                    var t = UIFactory.CreateText(_header, label, 15, GameConfig.UiAccent,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    SpanColumn(t.rectTransform, col, RowPad + 4f, 34f);
                    UIFactory.Fit(t, 10);
                    continue;
                }

                var key = col.Sort.Value;
                var btn = UIFactory.CreateButton(_header, label, () => ToggleSort(key),
                    new Color(1f, 1f, 1f, active ? 0.08f : 0.02f),
                    active ? GameConfig.UiAccent : GameConfig.UiText, 15);
                SpanColumn((RectTransform)btn.transform, col, RowPad, 34f);

                var txt = btn.GetComponentInChildren<Text>();
                txt.alignment = TextAnchor.MiddleLeft;
                txt.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                txt.rectTransform.offsetMin = new Vector2(4, 0);
                UIFactory.Fit(txt, 10);
            }
        }

        void ToggleSort(SortKey key)
        {
            // Re-picking the active column flips direction; a new column starts
            // ascending, which is what a name/number list should default to.
            if (_sortKey == key) _sortAscending = !_sortAscending;
            else { _sortKey = key; _sortAscending = true; }
            Rebuild();
        }

        // ------------------------------------------------------- filter/sort

        List<UnitDefinition> VisibleUnits()
        {
            var list = new List<UnitDefinition>();
            foreach (var def in UnitDatabase.All)
            {
                if (_category == CategoryView.Ground && def.Category != UnitCategory.CoreGround) continue;
                if (_category == CategoryView.Drone && def.Category != UnitCategory.Drone) continue;
                if (!MatchesSearch(def)) continue;
                list.Add(def);
            }

            list.Sort((a, b) =>
            {
                int c = Compare(a, b);
                // Ties fall back to name so the order is stable between rebuilds.
                if (c == 0) c = string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                return _sortAscending ? c : -c;
            });
            return list;
        }

        bool MatchesSearch(UnitDefinition def)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return Contains(def.name) || Contains(def.id) || Contains(def.ammoType);

            bool Contains(string s) =>
                !string.IsNullOrEmpty(s) &&
                s.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        int Compare(UnitDefinition a, UnitDefinition b) => _sortKey switch
        {
            SortKey.Name => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase),
            SortKey.Category => string.Compare(a.category, b.category, System.StringComparison.OrdinalIgnoreCase),
            SortKey.Attack => a.attack.CompareTo(b.attack),
            SortKey.Defence => a.defence.CompareTo(b.defence),
            SortKey.Armour => a.armour.CompareTo(b.armour),
            SortKey.Range => a.weaponRangeKm.CompareTo(b.weaponRangeKm),
            SortKey.Speed => a.speedKmh.CompareTo(b.speedKmh),
            SortKey.Manpower => a.manpower.CompareTo(b.manpower),
            _ => 0
        };

        // ---------------------------------------------------------- rebuild

        void Rebuild()
        {
            BuildHeaderCells();
            foreach (var repaint in _repaints) repaint();

            ClearChildren(_rowsContent);
            _rowImages.Clear();

            var units = VisibleUnits();
            foreach (var def in units) CreateRow(_rowsContent, def);

            _resultCount.text = units.Count == UnitDatabase.All.Count
                ? $"{units.Count} unit types"
                : $"{units.Count} of {UnitDatabase.All.Count} unit types";

            if (units.Count == 0)
            {
                var empty = UIFactory.CreateText(_rowsContent, "No unit types match these filters.",
                    18, GameConfig.UiTextDim);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 60);
                Select(null);
                return;
            }

            // Keep the current selection if it survived the filter, otherwise
            // fall to the first row so the detail panel is never blank. A
            // surviving selection only needs its new row re-highlighted —
            // re-running Select would reload the model and restart its
            // animation on every keystroke in the search box.
            if (_selected != null && units.Contains(_selected)) HighlightSelectedRow();
            else Select(units[0]);
        }

        void CreateRow(Transform parent, UnitDefinition def)
        {
            var row = UIFactory.CreatePanel(parent, "Row_" + def.id, RowColour(def, false));
            row.sizeDelta = new Vector2(0, 56);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => Select(def));
            _rowImages[def.id] = row.GetComponent<Image>();

            // Icon column: one icon per affiliation shown, side by side when both.
            // Anchored inside the column rather than at an absolute x, so they
            // stay in their cell when the table reflows.
            float iconX = 6f;
            if (_team != TeamView.Enemy) { PlaceIcon(row, "Friendly", def.id, iconX, Columns[0]); iconX += 50f; }
            if (_team != TeamView.Friendly) PlaceIcon(row, "Enemy", def.id, iconX, Columns[0]);

            Cell(row, def.name, Columns[1], 17, GameConfig.UiText);
            Cell(row, def.Category == UnitCategory.Drone ? "Drone" : "Core Ground",
                Columns[2], 14, GameConfig.UiTextDim);
            Cell(row, $"{def.attack:0}", Columns[3], 15, GameConfig.UiText);
            Cell(row, $"{def.defence:0}", Columns[4], 15, GameConfig.UiText);
            Cell(row, $"{def.armour:0}", Columns[5], 15, GameConfig.UiText);
            Cell(row, $"{def.weaponRangeKm:0.#} km", Columns[6], 15, GameConfig.UiText);
            Cell(row, $"{def.speedKmh:0} km/h", Columns[7], 15, GameConfig.UiText);
            Cell(row, $"{def.manpower:n0}", Columns[8], 15, GameConfig.UiText);
        }

        Color RowColour(UnitDefinition def, bool selected)
        {
            if (selected) return new Color(GameConfig.UiAccent.r, GameConfig.UiAccent.g, GameConfig.UiAccent.b, 0.22f);
            // Tint by the affiliation being viewed — a cheap way to keep the
            // filter state visible while scanning the table.
            return _team switch
            {
                TeamView.Friendly => new Color(GameConfig.BlueTeam.r, GameConfig.BlueTeam.g, GameConfig.BlueTeam.b, 0.06f),
                TeamView.Enemy => new Color(GameConfig.RedTeam.r, GameConfig.RedTeam.g, GameConfig.RedTeam.b, 0.06f),
                _ => new Color(1f, 1f, 1f, 0.03f)
            };
        }

        /// <summary>
        /// A unit icon at <paramref name="x"/> pixels from the left of its
        /// container. <paramref name="col"/> anchors it inside a table column;
        /// pass null (the detail panel) to anchor to the container itself.
        /// </summary>
        void PlaceIcon(Transform parent, string folder, string unitId, float x, Column? col = null)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite == null) return;
            var img = UIFactory.CreateImage(parent, sprite, folder + "Icon");
            var rt = (RectTransform)img.transform;

            float left = col.HasValue ? col.Value.Start : 0f;
            rt.anchorMin = rt.anchorMax = new Vector2(left, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(44, 44);

            img.raycastTarget = false;    // clicks belong to the row
        }

        void Cell(Transform parent, string text, Column col, int fontSize, Color? color = null)
        {
            var t = UIFactory.CreateText(parent, text, fontSize, color, TextAnchor.MiddleLeft);
            SpanColumn(t.rectTransform, col, 4f, 40f);
            // Long names shrink to fit their column instead of running into the
            // next one. Legacy Text has no ellipsis, so best-fit is the closest
            // thing to a responsive cell.
            UIFactory.Fit(t, 11);
        }

        /// <summary>
        /// Anchors a rect to a column's share of the table width, vertically
        /// centred in its row. Horizontal anchors do the work, so the cell tracks
        /// the table at any window size without anything recomputing pixels.
        /// </summary>
        static void SpanColumn(RectTransform rt, Column col, float inset, float height)
        {
            rt.anchorMin = new Vector2(col.Start, 0.5f);
            rt.anchorMax = new Vector2(col.End, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, -height * 0.5f);
            rt.offsetMax = new Vector2(-4f, height * 0.5f);
        }

        // ----------------------------------------------------- detail panel

        void BuildDetailPanel(Transform parent)
        {
            var panel = UIFactory.CreatePanel(parent, "DetailPanel", GameConfig.UiPanel);
            // Pinned to the right edge and stretched to the bottom margin, so the
            // stat list gets whatever height the window has rather than a fixed
            // 866 px that ran off the bottom of anything shorter than 1080.
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-(ScreenMargin + PanelW), BottomMargin);
            panel.offsetMax = new Vector2(-ScreenMargin, PanelY);

            _detailName = UIFactory.CreateText(panel, "", 26, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 1f), new Vector2(20, -16), new Vector2(PanelW - 40, 34));

            _detailSub = UIFactory.CreateText(panel, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_detailSub.rectTransform, new Vector2(0f, 1f), new Vector2(20, -52), new Vector2(PanelW - 40, 22));

            _detailIcons = UIFactory.CreateGroup(panel, "Icons");
            UIFactory.Place(_detailIcons, new Vector2(0f, 1f), new Vector2(20, -80), new Vector2(PanelW - 40, 46));

            var previewSize = new Vector2(PanelW - 40, 300);
            _preview = ModelPreview.Create(panel, previewSize);
            UIFactory.Place((RectTransform)_preview.transform, new Vector2(0f, 1f),
                new Vector2(20, -134), previewSize);

            var hint = UIFactory.CreateText(panel, "Drag to orbit · scroll to zoom", 13,
                GameConfig.UiTextDim, TextAnchor.MiddleRight);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(20, -438), new Vector2(PanelW - 40, 18));

            var scroll = UIFactory.CreateScrollView(panel, out _detailStats, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(16, 16);
            srt.offsetMax = new Vector2(-16, -462);
        }

        void Select(UnitDefinition def)
        {
            // Repaint the previous row before the reference moves on.
            if (_selected != null && _rowImages.TryGetValue(_selected.id, out var previous) && previous != null)
                previous.color = RowColour(_selected, false);

            _selected = def;
            HighlightSelectedRow();
            RefreshDetail();
        }

        void HighlightSelectedRow()
        {
            if (_selected == null) return;
            if (_rowImages.TryGetValue(_selected.id, out var image) && image != null)
                image.color = RowColour(_selected, true);
        }

        void RefreshDetail()
        {
            var def = _selected;

            _detailName.text = def == null ? "—" : def.name.ToUpperInvariant();
            _detailSub.text = def == null
                ? "No unit selected"
                : $"{(def.Category == UnitCategory.Drone ? "Drone" : "Core Ground")}  ·  id: {def.id}";

            ClearChildren(_detailIcons);
            if (def != null)
            {
                PlaceIcon(_detailIcons, "Friendly", def.id, 0f);
                PlaceIcon(_detailIcons, "Enemy", def.id, 52f);
            }

            if (_preview != null) _preview.Show(def);

            BuildStats(def);
        }

        void BuildStats(UnitDefinition def)
        {
            ClearChildren(_detailStats);
            if (def == null) return;

            if (!string.IsNullOrEmpty(def.description))
            {
                var d = UIFactory.CreateText(_detailStats, def.description, 15,
                    GameConfig.UiTextDim, TextAnchor.UpperLeft);
                // Height from the text rather than a fixed 54 px, which cut the
                // longer descriptions off mid-sentence.
                d.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
                ((RectTransform)d.transform).sizeDelta = new Vector2(0, 54);
            }

            Section("COMBAT");
            Stat("Attack", $"{def.attack:0}");
            Stat("Hard attack", $"{def.hardAttack:0}");
            Stat("Defence", $"{def.defence:0}");
            Stat("Armour", $"{def.armour:0}");
            Stat("Anti-air", $"{def.antiAir:0}");
            Stat("Weapon range", $"{def.weaponRangeKm:0.#} km");
            Stat("View range", $"{def.viewRangeKm:0.#} km");

            Section("FORMATION");
            Stat("Manpower (company)", $"{def.manpower:n0}");
            Stat("Training", $"{def.training:0}");
            Stat("Morale", $"{def.morale:0}");
            Stat("Organisation", $"{def.organisation:0}");
            Stat("Speed", $"{def.speedKmh:0} km/h");

            Section("LOGISTICS");
            Stat("Ammo type", string.IsNullOrEmpty(def.ammoType) ? "—" : def.ammoType);
            Stat("Ammo carried", $"{def.ammoStock:n0}");
            Stat("Fuel carried", def.fuelStock > 0 ? $"{def.fuelStock:n0} L" : "—");
            Stat("Fuel per km", def.fuelUsePerKm > 0 ? $"{def.fuelUsePerKm:0.#} L" : "—");
            Stat("Rations", $"{def.foodDays} days");

            var flags = new List<string>();
            if (def.canIndirectFire) flags.Add("Indirect fire");
            if (def.canCounterUas) flags.Add("Counter-UAS");
            if (def.isSupport) flags.Add("Support");
            Section("CAPABILITIES");
            Stat("Flags", flags.Count > 0 ? string.Join(", ", flags) : "—");

            Section("3D MODEL");
            var model = UnitModelLibrary.Resolve(def);
            Stat("Source", model?.sourceAsset ?? "none yet");
            Stat("Idle animation", model?.idleClip ?? "—");
        }

        void Section(string label)
        {
            var t = UIFactory.CreateText(_detailStats, label, 14, GameConfig.UiAccent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 30);
        }

        /// <summary>
        /// One label/value row. The two rects meet at <see cref="StatSplit"/>
        /// rather than overlapping, and both shrink to fit — the longest values
        /// here are asset names and ammunition designations, which used to run
        /// straight off the panel.
        /// </summary>
        const float StatSplit = 0.46f;

        void Stat(string label, string value)
        {
            var row = UIFactory.CreateGroup(_detailStats, "Stat_" + label);
            row.sizeDelta = new Vector2(0, 26);

            var l = UIFactory.CreateText(row, label, 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(StatSplit, 1);
            l.rectTransform.offsetMin = Vector2.zero; l.rectTransform.offsetMax = new Vector2(-6, 0);
            UIFactory.Fit(l, 11);

            var v = UIFactory.CreateText(row, value, 15, GameConfig.UiText, TextAnchor.MiddleRight);
            v.rectTransform.anchorMin = new Vector2(StatSplit, 0); v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(-4, 0);
            UIFactory.Fit(v, 11);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
