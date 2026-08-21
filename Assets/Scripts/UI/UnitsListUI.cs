using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Models;
using IronMeridian.Save;
using IronMeridian.Vfx;

namespace IronMeridian.UI
{
    /// <summary>
    /// UNITS LIST — the reference catalogue for every data table the game
    /// is built from, and the one place they can be tuned.
    ///
    /// **Seven tables, one screen.** Unit types were only ever half the answer
    /// to "what can this thing do": artillery natures, strike airframes, UAVs,
    /// missile systems, naval guns and the six logistic installations are all
    /// catalogues of the same shape and were previously readable only in source.
    /// They are tabs here, driven from <see cref="GameCatalogs"/> — adding a
    /// weapon family, or any other data table, adds a tab.
    ///
    /// **Editing.** EDIT turns the detail panel's value column into fields;
    /// every write lands in the live catalogue at once, so the effect is visible
    /// the next time the map editor is opened without a restart. SAVE writes
    /// them to your own <c>tuning.json</c>, which is a sparse patch over the
    /// shipped data rather than a copy of it — see <see cref="TuningStore"/> for
    /// why that matters. REVERT puts the selected record back; RESET ALL puts
    /// every record back and deletes the file.
    ///
    /// Reached from DEVELOPMENT.
    /// </summary>
    public class UnitsListUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        //
        // Everything is anchor-driven rather than laid out in absolute pixels.
        // The canvas scaler matches width and height equally, so the reference
        // width is only 1920 at exactly 16:9 — at any other aspect it shrinks,
        // and a table pinned at a fixed x ran under a detail panel pinned to the
        // right edge. Stretching the table between the two insets, and giving
        // its columns proportional widths, means the whole row is visible at
        // every window shape.
        const float ScreenMargin = 60f;
        const float ColumnGap = 24f;
        /// <summary>Detail panel width. The one fixed dimension — its rows need a stable column split.</summary>
        const float PanelW = 560f;
        const float PanelY = -170f;
        const float BottomMargin = 44f;
        const float RowPad = 8f;      // CreateScrollView's content padding

        /// <summary>Rows the header block occupies, measured from the top of the screen.</summary>
        const float TabsY = -164f, ToolbarY = -222f, BranchY = -278f, HintY = -326f;
        /// <summary>Top of the table, with and without the branch-filter row.</summary>
        const float TableYWithBranches = -352f, TableYPlain = -300f;

        /// <summary>
        /// Left inset of the **first** column's content, inside its own cell,
        /// in every catalogue.
        ///
        /// The lead column stands against the scroll viewport's clipping edge,
        /// which shaves the left stroke of an APP-6 frame and the first letter
        /// of a name. 35 px is a gutter wide enough that the column reads as
        /// indented rather than as pressed against the frame, and it is applied
        /// to all six tables so a tab change does not shift the table sideways.
        /// Every other column keeps the tighter <see cref="RowPad"/>.
        /// </summary>
        const float FirstColumnInset = 35f;
        const float PanelInset = 50f;

        enum TeamView { Both, Friendly, Enemy }

        /// <summary>
        /// A table column, sized as a **share of the table width** rather than in
        /// pixels, so the table reflows with the window and no column can be cut
        /// off the right-hand edge.
        /// </summary>
        class Column
        {
            public readonly string Label;
            public readonly float Weight;
            /// <summary>Sort key, or null for a column that cannot be sorted on.</summary>
            public readonly string Sort;
            /// <summary>Cell text for one row; null for the icon column.</summary>
            public readonly System.Func<CatalogEntry, string> Cell;
            public float Start, End;

            public Column(string label, float weight, string sort,
                System.Func<CatalogEntry, string> cell)
            { Label = label; Weight = weight; Sort = sort; Cell = cell; }
        }

        /// <summary>Turns the weights into cumulative 0..1 bounds every cell anchors to.</summary>
        static void NormaliseColumns(List<Column> columns)
        {
            float total = 0f;
            foreach (var c in columns) total += c.Weight;
            if (total <= 0f) return;

            float cursor = 0f;
            foreach (var c in columns)
            {
                c.Start = cursor / total;
                cursor += c.Weight;
                c.End = cursor / total;
            }
        }

        // ------------------------------------------------------------- state
        CatalogGroup _catalog;
        List<CatalogEntry> _entries = new List<CatalogEntry>();
        List<Column> _columns = new List<Column>();

        string _sortKey = "name";
        bool _sortAscending = true;
        TeamView _team = TeamView.Both;
        /// <summary>Arm of service filter; null shows every branch. Units tab only.</summary>
        UnitBranch? _branch;
        string _search = "";
        CatalogEntry _selected;
        bool _editing;

        RectTransform _header, _rowsContent, _table, _branchRow;
        Text _resultCount, _hint, _statusLine;
        InputField _searchField;
        Button _editButton;
        readonly Dictionary<string, Image> _rowImages = new Dictionary<string, Image>();
        /// <summary>Controls that repaint themselves from filter state on every rebuild.</summary>
        readonly List<System.Action> _repaints = new List<System.Action>();

        // detail panel
        ModelPreview _preview;
        Text _detailName, _detailSub, _previewHint;
        RectTransform _detailIcons, _detailStats;
        StatEditorPanel _stats;

        bool IsUnits => _catalog != null && _catalog.key == GameCatalogs.Units;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
            var canvas = UIFactory.CreateCanvas("UnitsListCanvas");

            // Dense data table over artwork: lean on the scrim so every row stays legible.
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Default,
                BackgroundCatalog.DenseScreenScrim);

            _catalog = GameCatalogs.All[0];

            BuildHeaderBar(canvas.transform);
            BuildCatalogTabs(canvas.transform);
            BuildToolbar(canvas.transform);
            BuildTable(canvas.transform);
            BuildDetailPanel(canvas.transform);

            SelectCatalog(_catalog);
        }

        // ------------------------------------------------------- header bar

        void BuildHeaderBar(Transform parent)
        {
            var title = UIFactory.CreateText(parent, "UNITS LIST", 46,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -66), new Vector2(760, 70));

            _resultCount = UIFactory.CreateText(parent, "", 19, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_resultCount.rectTransform, new Vector2(0f, 1f), new Vector2(80, -116), new Vector2(760, 28));

            _statusLine = UIFactory.CreateText(parent, "", 15, GameConfig.UiAccent, TextAnchor.MiddleRight);
            UIFactory.Place(_statusLine.rectTransform, new Vector2(1f, 1f), new Vector2(-80, -136), new Vector2(760, 24));

            UIFactory.CreateBackButton(parent, "BACK TO DEVELOPMENT",
                () => SceneManager.LoadScene(GameConfig.SceneTesting));

            // The tuning controls. Grouped at the right of the header rather
            // than in the detail panel: SAVE and RESET ALL act on the whole
            // file, not on the record that happens to be selected.
            _editButton = UIFactory.CreateButton(parent, "EDIT", ToggleEditing,
                GameConfig.UiPanelLight, GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)_editButton.transform, new Vector2(1f, 1f),
                new Vector2(-396, -66), new Vector2(120, 42));

            var save = UIFactory.CreateButton(parent, "SAVE", SaveTuning,
                UiTheme.Success, Color.white, 16);
            UIFactory.Place((RectTransform)save.transform, new Vector2(1f, 1f),
                new Vector2(-518, -66), new Vector2(120, 42));

            var revert = UIFactory.CreateButton(parent, "REVERT", RevertSelected,
                GameConfig.UiPanelLight, GameConfig.UiTextDim, 15);
            UIFactory.Place((RectTransform)revert.transform, new Vector2(1f, 1f),
                new Vector2(-640, -66), new Vector2(120, 42));

            var reset = UIFactory.CreateButton(parent, "RESET ALL", ResetAll,
                UiTheme.Danger, Color.white, 15);
            UIFactory.Place((RectTransform)reset.transform, new Vector2(1f, 1f),
                new Vector2(-762, -66), new Vector2(130, 42));

            RefreshTuningControls();
        }

        // ------------------------------------------------------ catalogue tabs

        void BuildCatalogTabs(Transform parent)
        {
            var row = UIFactory.CreateGroup(parent, "CatalogTabs");
            StretchToTableWidth(row, TabsY, 44f);

            var buttons = new List<(Button button, CatalogGroup group)>();
            const float segW = 168f;

            for (int i = 0; i < GameCatalogs.All.Length; i++)
            {
                var group = GameCatalogs.All[i];
                var btn = UIFactory.CreateButton(row, group.title, () => SelectCatalog(group),
                    GameConfig.UiPanelLight, GameConfig.UiText, 16);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(i * (segW + 3f), 0), new Vector2(segW, 42));
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 11);
                buttons.Add((btn, group));
            }

            _repaints.Add(() =>
            {
                foreach (var (button, group) in buttons)
                {
                    bool on = group == _catalog;
                    button.GetComponent<Image>().color = on ? GameConfig.UiAccent : GameConfig.UiPanelLight;
                    var txt = button.GetComponentInChildren<Text>();
                    if (txt == null) continue;
                    txt.color = on ? GameConfig.UiBackground : GameConfig.UiText;
                    txt.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            });
        }

        // ---------------------------------------------------------- toolbar

        void BuildToolbar(Transform parent)
        {
            var toolbar = UIFactory.CreateGroup(parent, "Toolbar");
            StretchToTableWidth(toolbar, ToolbarY, 48f);

            _searchField = UIFactory.CreateInputField(toolbar, "Search name, id or detail...", 18);
            UIFactory.Place((RectTransform)_searchField.transform, new Vector2(0f, 1f),
                new Vector2(0, 0), new Vector2(320, 46));
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v == null ? "" : v.Trim();
                Rebuild();
            });

            Segmented(toolbar, 340f, 106f, new[] { "BOTH", "FRIENDLY", "ENEMY" }, () => (int)_team,
                i => { _team = (TeamView)i; Rebuild(); }, unitsOnly: true);

            var reset = UIFactory.CreateButton(toolbar, "RESET FILTERS", () =>
            {
                _team = TeamView.Both; _branch = null; _search = "";
                _sortKey = "name"; _sortAscending = true;
                // Clearing the field fires onValueChanged, which rebuilds — but
                // only if the text was non-empty, so rebuild unconditionally after.
                if (_searchField != null) _searchField.text = "";
                Rebuild();
            }, GameConfig.UiPanelLight, GameConfig.UiTextDim, 14);
            UIFactory.Place((RectTransform)reset.transform, new Vector2(0f, 1f),
                new Vector2(672, 0), new Vector2(150, 46));

            BuildBranchFilter(parent);

            _hint = UIFactory.CreateText(parent, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            StretchToTableWidth(_hint.rectTransform, HintY, 22f);
        }

        const string AllBranchesHint =
            "Both teams field the same catalogue — the affiliation filter switches which icon set is shown. " +
            "Click a column heading to sort; click a row for its 3D model and full values.";

        /// <summary>
        /// The arm-of-service filter: ALL plus one button per
        /// <see cref="UnitBranch"/>, each carrying how many types it holds. The
        /// counts are of the whole catalogue, not of the current search — a
        /// number that moved while typing would be reporting on the search box.
        /// Units tab only; hidden for the weapon catalogues, which have no arms.
        /// </summary>
        void BuildBranchFilter(Transform parent)
        {
            _branchRow = UIFactory.CreateGroup(parent, "BranchFilter");
            StretchToTableWidth(_branchRow, BranchY, 46f);

            var counts = new Dictionary<UnitBranch, int>();
            foreach (var def in UnitDatabase.All)
            {
                counts.TryGetValue(def.Branch, out int n);
                counts[def.Branch] = n + 1;
            }

            const float segW = 96f;
            var buttons = new List<(Button button, UnitBranch? branch)>();

            void Add(string label, UnitBranch? branch, int index)
            {
                var btn = UIFactory.CreateButton(_branchRow, label, () => { _branch = branch; Rebuild(); },
                    GameConfig.UiPanelLight, GameConfig.UiText, 14);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(index * (segW + 2f), 0), new Vector2(segW, 42));
                // Long labels ("ARTILLERY 13") shrink rather than overflow their button.
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 10);
                buttons.Add((btn, branch));
            }

            Add($"ALL {UnitDatabase.All.Count}", null, 0);
            for (int i = 0; i < UnitBranchInfo.All.Length; i++)
            {
                var b = UnitBranchInfo.All[i];
                counts.TryGetValue(b, out int n);
                Add($"{UnitBranchInfo.ShortName(b)} {n}", b, i + 1);
            }

            _repaints.Add(() =>
            {
                foreach (var (button, branch) in buttons)
                {
                    bool on = branch == _branch;
                    button.GetComponent<Image>().color = on ? GameConfig.UiAccent : GameConfig.UiPanelLight;
                    var txt = button.GetComponentInChildren<Text>();
                    if (txt == null) continue;
                    txt.color = on ? GameConfig.UiBackground : GameConfig.UiText;
                    txt.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            });
        }

        /// <summary>
        /// A row of mutually exclusive buttons. The active one is repainted on
        /// every rebuild, so the control always reflects the real filter state.
        /// </summary>
        void Segmented(Transform parent, float x, float segW, string[] labels,
            System.Func<int> current, System.Action<int> onPick, bool unitsOnly = false)
        {
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
                    if (unitsOnly) buttons[i].gameObject.SetActive(IsUnits);
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
            _table = UIFactory.CreateGroup(parent, "Table");
            // Full height between the header block and the bottom margin, so the
            // list grows with the window instead of stopping at a fixed height.
            _table.anchorMin = new Vector2(0, 0); _table.anchorMax = new Vector2(1, 1);
            _table.pivot = new Vector2(0.5f, 1f);
            _table.offsetMin = new Vector2(ScreenMargin, BottomMargin);
            _table.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), TableYWithBranches);

            _header = UIFactory.CreatePanel(_table, "Header", GameConfig.UiPanel);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1);
            _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -42); _header.offsetMax = Vector2.zero;

            var scroll = UIFactory.CreateScrollView(_table, out _rowsContent, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -46);
        }

        /// <summary>
        /// Spans a rect across the table's column of the screen: from the left
        /// margin to where the detail panel begins. Used by the toolbar rows so
        /// they track the table rather than drifting over the panel.
        /// </summary>
        static void StretchToTableWidth(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ScreenMargin, top - height);
            rt.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), top);
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

        // ------------------------------------------------------- catalogues

        void SelectCatalog(CatalogGroup group)
        {
            if (group == null) return;
            _catalog = group;
            _entries = group.Load();
            _columns = BuildColumns(group);
            NormaliseColumns(_columns);

            _selected = null;
            _branch = null;
            _sortKey = "name";
            _sortAscending = true;

            // The branch filter belongs to the unit catalogue alone; the table
            // takes the row back when it is not shown rather than leaving a gap
            // where a control used to be.
            if (_branchRow != null) _branchRow.gameObject.SetActive(IsUnits);
            if (_table != null)
                _table.offsetMax = new Vector2(_table.offsetMax.x,
                    IsUnits ? TableYWithBranches : TableYPlain);

            Rebuild();
        }

        /// <summary>
        /// The columns for a catalogue. Units get the stat table they always
        /// had; every other table is a name, what it belongs to and what it is
        /// for, because those records have no shared numbers worth a column —
        /// an airframe's approach time and a naval gun's calibre are not the
        /// same kind of figure and would make a nonsense of one heading.
        /// </summary>
        List<Column> BuildColumns(CatalogGroup group)
        {
            if (group.key == GameCatalogs.Units)
                return new List<Column>
                {
                    // ICON carries two 44 px icons side by side *plus* the
                    // column's left indent, so it needs the widest share that is
                    // not text.
                    new Column("ICON",     1.50f, null,       null),
                    new Column("NAME",     3.10f, "name",     e => e.name),
                    new Column("BRANCH",   1.25f, "group",    e => e.group),
                    new Column("ATK",      0.70f, "attack",   e => $"{Unit(e).attack:0}"),
                    new Column("DEF",      0.70f, "defence",  e => $"{Unit(e).defence:0}"),
                    new Column("ARM",      0.70f, "armour",   e => $"{Unit(e).armour:0}"),
                    new Column("RANGE",    1.00f, "range",    e => $"{Unit(e).weaponRangeKm:0.#} km"),
                    new Column("SPEED",    1.10f, "speed",    e => $"{Unit(e).speedKmh:0} km/h"),
                    new Column("MANPOWER", 1.15f, "manpower", e => $"{Unit(e).manpower:n0}"),
                };

            // The rear area *does* have shared numbers worth a column, unlike
            // the weapon tables: every installation reaches a distance and holds
            // a quantity, in the same units, and those two figures are the whole
            // of what distinguishes a depot from a supply point. Leaving them in
            // a DETAIL string would hide the one comparison this table is for.
            if (group.key == GameCatalogs.Logistics)
                return new List<Column>
                {
                    new Column("NAME",    2.40f, "name",   e => e.name),
                    new Column("SERVES",  1.50f, "group",  e => e.group),
                    new Column("REACH",   1.10f, "reach",  e => $"{Site(e)?.serviceRadiusKm ?? 0f:0.#} km"),
                    new Column("ISSUES",  1.10f, "issues", e => $"{Site(e)?.defaultStock ?? 0.0:0.#}"),
                    new Column("DETAIL",  3.20f, null,     e => e.detail),
                    new Column("ID",      1.60f, "id",     e => e.id),
                };

            return new List<Column>
            {
                new Column("NAME",   2.60f, "name",  e => e.name),
                new Column("GROUP",  1.60f, "group", e => e.group),
                new Column("DETAIL", 4.20f, null,    e => e.detail),
                new Column("ID",     1.60f, "id",    e => e.id),
            };
        }

        /// <summary>The installation behind an entry, or null on every other tab.</summary>
        static LogisticsDef Site(CatalogEntry e) => e?.record as LogisticsDef;

        /// <summary>The unit behind an entry, or null — the weapon catalogues have none, and neither does an empty table.</summary>
        static UnitDefinition Unit(CatalogEntry e) => e?.record as UnitDefinition;

        void BuildHeaderCells()
        {
            ClearChildren(_header);

            for (int i = 0; i < _columns.Count; i++)
            {
                var col = _columns[i];
                bool active = col.Sort != null && col.Sort == _sortKey;
                string label = col.Label + (active ? (_sortAscending ? "  ▲" : "  ▼") : "");
                float inset = i == 0 ? FirstColumnInset : RowPad;

                if (col.Sort == null)
                {
                    var t = UIFactory.CreateText(_header, label, 15, GameConfig.UiAccent,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    SpanColumn(t.rectTransform, col, inset, 34f);
                    UIFactory.Fit(t, 10);
                    continue;
                }

                string key = col.Sort;
                var btn = UIFactory.CreateButton(_header, label, () => ToggleSort(key),
                    new Color(1f, 1f, 1f, active ? 0.08f : 0.02f),
                    active ? GameConfig.UiAccent : GameConfig.UiText, 15);
                // The button's own fill spans the whole cell — a sort control
                // that started 35 px in would leave a dead strip beside it that
                // looks clickable and is not. The caption inside it takes the
                // indent instead.
                SpanColumn((RectTransform)btn.transform, col, RowPad, 34f);

                var txt = btn.GetComponentInChildren<Text>();
                txt.alignment = TextAnchor.MiddleLeft;
                txt.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                txt.rectTransform.offsetMin = new Vector2(i == 0 ? inset - RowPad + 4f : 4f, 0);
                UIFactory.Fit(txt, 10);
            }
        }

        void ToggleSort(string key)
        {
            // Re-picking the active column flips direction; a new column starts
            // ascending, which is what a name/number list should default to.
            if (_sortKey == key) _sortAscending = !_sortAscending;
            else { _sortKey = key; _sortAscending = true; }
            Rebuild();
        }

        // ------------------------------------------------------- filter/sort

        List<CatalogEntry> VisibleEntries()
        {
            var list = new List<CatalogEntry>();
            foreach (var e in _entries)
            {
                if (IsUnits && _branch.HasValue)
                {
                    var unit = Unit(e);
                    if (unit == null || unit.Branch != _branch.Value) continue;
                }
                if (!MatchesSearch(e)) continue;
                list.Add(e);
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

        bool MatchesSearch(CatalogEntry e)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            if (Contains(e.name) || Contains(e.id) || Contains(e.detail) || Contains(e.group)) return true;
            // Ammunition designations are the other thing anyone searches a unit
            // catalogue for, and they are not on the row.
            return Unit(e) != null && Contains(Unit(e).ammoType);

            bool Contains(string s) =>
                !string.IsNullOrEmpty(s) &&
                s.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        int Compare(CatalogEntry a, CatalogEntry b)
        {
            switch (_sortKey)
            {
                case "name": return string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                case "id": return string.Compare(a.id, b.id, System.StringComparison.OrdinalIgnoreCase);
                case "group":
                    // Units sort by the branch's declaration order rather than
                    // alphabetically, so the table groups the way the Units doc
                    // reads: manoeuvre, then fires, then air, then the tail.
                    if (IsUnits && Unit(a) != null && Unit(b) != null)
                        return ((int)Unit(a).Branch).CompareTo((int)Unit(b).Branch);
                    return string.Compare(a.group, b.group, System.StringComparison.OrdinalIgnoreCase);
            }

            // The rear area's two numeric columns. Checked before the unit
            // stats below, because an installation is not a UnitDefinition and
            // would fall straight through to "no order at all".
            var sa = Site(a); var sb = Site(b);
            if (sa != null && sb != null)
                return _sortKey switch
                {
                    "reach" => sa.serviceRadiusKm.CompareTo(sb.serviceRadiusKm),
                    "issues" => sa.defaultStock.CompareTo(sb.defaultStock),
                    _ => 0
                };

            var ua = Unit(a); var ub = Unit(b);
            if (ua == null || ub == null) return 0;
            return _sortKey switch
            {
                "attack" => ua.attack.CompareTo(ub.attack),
                "defence" => ua.defence.CompareTo(ub.defence),
                "armour" => ua.armour.CompareTo(ub.armour),
                "range" => ua.weaponRangeKm.CompareTo(ub.weaponRangeKm),
                "speed" => ua.speedKmh.CompareTo(ub.speedKmh),
                "manpower" => ua.manpower.CompareTo(ub.manpower),
                _ => 0
            };
        }

        // ---------------------------------------------------------- rebuild

        void Rebuild()
        {
            BuildHeaderCells();
            foreach (var repaint in _repaints) repaint();

            ClearChildren(_rowsContent);
            _rowImages.Clear();

            var rows = VisibleEntries();
            foreach (var e in rows) CreateRow(_rowsContent, e);

            _resultCount.text = rows.Count == _entries.Count
                ? $"{rows.Count} {Noun(_catalog)} · {_catalog.doc}"
                : $"{rows.Count} of {_entries.Count} {Noun(_catalog)} · {_catalog.doc}";

            if (_hint != null)
                _hint.text = IsUnits
                    ? (_branch.HasValue
                        ? $"{UnitBranchInfo.DisplayName(_branch.Value).ToUpperInvariant()} — {UnitBranchInfo.Blurb(_branch.Value)}"
                        : AllBranchesHint)
                    : _catalog.blurb;

            if (rows.Count == 0)
            {
                var empty = UIFactory.CreateText(_rowsContent, "Nothing matches these filters.",
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
            if (_selected != null && rows.Contains(_selected)) HighlightSelectedRow();
            else Select(rows[0]);
        }

        static string Noun(CatalogGroup g) =>
            g.key == GameCatalogs.Units ? "unit types" : g.title.ToLowerInvariant();

        void CreateRow(Transform parent, CatalogEntry entry)
        {
            var row = UIFactory.CreatePanel(parent, "Row_" + entry.id, RowColour(false));
            row.sizeDelta = new Vector2(0, IsUnits ? 56 : 48);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => Select(entry));
            _rowImages[entry.id] = row.GetComponent<Image>();

            FillRow(row, entry);
        }

        /// <summary>
        /// Lays one row's cells out. Shared by the initial build and the
        /// after-an-edit refresh, which were two copies of the same loop and
        /// drifted apart the moment either column indent changed.
        /// </summary>
        void FillRow(RectTransform row, CatalogEntry entry)
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                var col = _columns[i];
                float inset = i == 0 ? FirstColumnInset : 4f;

                if (col.Cell == null)
                {
                    // The icon column: one icon per affiliation shown, side by
                    // side when both. Anchored inside the column rather than at
                    // an absolute x, so they stay in their cell when the table
                    // reflows.
                    float iconX = inset;
                    if (_team != TeamView.Enemy) { PlaceIcon(row, "Friendly", entry.id, iconX, col); iconX += 50f; }
                    if (_team != TeamView.Friendly) PlaceIcon(row, "Enemy", entry.id, iconX, col);
                    continue;
                }

                bool lead = col.Label == "NAME";
                Cell(row, col.Cell(entry) ?? "", col, lead ? 17 : 15,
                    lead ? GameConfig.UiText : GameConfig.UiTextDim, inset);
            }
        }

        Color RowColour(bool selected)
        {
            if (selected) return new Color(GameConfig.UiAccent.r, GameConfig.UiAccent.g, GameConfig.UiAccent.b, 0.22f);
            if (!IsUnits) return new Color(1f, 1f, 1f, 0.03f);
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
        void PlaceIcon(Transform parent, string folder, string unitId, float x, Column col = null)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite == null) return;
            var img = UIFactory.CreateImage(parent, sprite, folder + "Icon");
            var rt = (RectTransform)img.transform;

            float left = col != null ? col.Start : 0f;
            rt.anchorMin = rt.anchorMax = new Vector2(left, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(44, 44);

            img.raycastTarget = false;    // clicks belong to the row
        }

        void Cell(Transform parent, string text, Column col, int fontSize, Color? color = null,
            float inset = 4f)
        {
            var t = UIFactory.CreateText(parent, text, fontSize, color, TextAnchor.MiddleLeft);
            SpanColumn(t.rectTransform, col, inset, 40f);
            // Long names shrink to fit their column instead of running into the
            // next one. Legacy Text has no ellipsis, so best-fit is the closest
            // thing to a responsive cell.
            UIFactory.Fit(t, 11);
        }

        /// <summary>
        /// Anchors a rect to a column's share of the table width, vertically
        /// centred in its row, so the cell tracks the table at any window size
        /// without anything recomputing pixels.
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
            // value list gets whatever height the window has.
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-(ScreenMargin + PanelW), BottomMargin);
            panel.offsetMax = new Vector2(-ScreenMargin, PanelY);

            // Everything inside is inset on both sides. The panel is a column of
            // dense text standing against the edge of the screen, and text that
            // starts at the frame reads as spilling out of it.
            float inner = PanelW - PanelInset * 2f;

            _detailName = UIFactory.CreateText(panel, "", 26, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -16), new Vector2(inner, 34));

            _detailSub = UIFactory.CreateText(panel, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_detailSub.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -52), new Vector2(inner, 22));

            _detailIcons = UIFactory.CreateGroup(panel, "Icons");
            UIFactory.Place(_detailIcons, new Vector2(0f, 1f), new Vector2(PanelInset, -80), new Vector2(inner, 46));

            var previewSize = new Vector2(inner, 300);
            _preview = ModelPreview.Create(panel, previewSize);
            UIFactory.Place((RectTransform)_preview.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -134), previewSize);

            _previewHint = UIFactory.CreateText(panel, "Drag to orbit · scroll to zoom", 13,
                GameConfig.UiTextDim, TextAnchor.MiddleRight);
            UIFactory.Place(_previewHint.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -438), new Vector2(inner, 18));

            var scroll = UIFactory.CreateScrollView(panel, out _detailStats, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(PanelInset, 16);
            srt.offsetMax = new Vector2(-PanelInset, -462);

            _stats = new StatEditorPanel(_detailStats);
            // Editing a name or a stat has to show up in the table too, or the
            // row and the panel disagree about the record they both point at.
            _stats.FieldChanged = _ => { RefreshRowText(); RefreshTuningControls(); };
        }

        void Select(CatalogEntry entry)
        {
            // Repaint the previous row before the reference moves on.
            if (_selected != null && _rowImages.TryGetValue(_selected.id, out var previous) && previous != null)
                previous.color = RowColour(false);

            _selected = entry;
            HighlightSelectedRow();
            RefreshDetail();
        }

        void HighlightSelectedRow()
        {
            if (_selected == null) return;
            if (_rowImages.TryGetValue(_selected.id, out var image) && image != null)
                image.color = RowColour(true);
        }

        void RefreshDetail()
        {
            var entry = _selected;

            _detailName.text = entry == null ? "—" : entry.name.ToUpperInvariant();
            _detailSub.text = entry == null
                ? "Nothing selected"
                : $"{entry.group}  ·  id: {entry.id}";

            ClearChildren(_detailIcons);
            // Icons exist for unit types only — a naval gun has no APP-6 symbol.
            if (entry != null && IsUnits)
            {
                PlaceIcon(_detailIcons, "Friendly", entry.id, 0f);
                PlaceIcon(_detailIcons, "Enemy", entry.id, 52f);
            }

            RefreshPreview(entry);

            _stats.Show(_catalog?.key, entry?.id, entry?.record, _catalog?.readOnlyFields);
            RefreshTuningControls();
        }

        /// <summary>
        /// Shows the record's 3D model where it has one. Units resolve through
        /// their definition; airframes and UAVs name a model id directly. The
        /// rest — artillery natures, missile systems, naval guns — are fired
        /// from off the map and have nothing to show, so the preview is hidden
        /// rather than left displaying the last thing that did.
        /// </summary>
        void RefreshPreview(CatalogEntry entry)
        {
            if (_preview == null) return;

            string modelId = entry?.record switch
            {
                AircraftDef air => air.modelId,
                UavDef uav => uav.modelId,
                // An installation is a *place*, and the one in this table that
                // has a building to show — see docs/26-LOGISTICS.md §4b.
                LogisticsDef site => site.modelId,
                _ => null
            };

            bool shows = IsUnits || modelId != null;
            _preview.gameObject.SetActive(shows);
            if (_previewHint != null) _previewHint.gameObject.SetActive(shows);
            if (!shows) return;

            if (IsUnits) _preview.Show(Unit(entry));
            else _preview.ShowModel(modelId, entry?.name ?? "This system");
        }

        /// <summary>Re-reads the selected row's cells after an edit, without rebuilding the whole table.</summary>
        void RefreshRowText()
        {
            if (_selected == null) return;
            if (!_rowImages.TryGetValue(_selected.id, out var image) || image == null) return;

            // The entry's own display strings are snapshots taken at load, so a
            // renamed record would keep its old caption until the tab was
            // re-entered. Refresh them from the record before redrawing.
            if (Unit(_selected) is UnitDefinition unit)
            {
                _selected.name = unit.name;
                _selected.detail = unit.description;
                _selected.group = UnitBranchInfo.DisplayName(unit.Branch);
            }

            var row = (RectTransform)image.transform;
            ClearChildren(row);
            FillRow(row, _selected);

            _detailName.text = _selected.name.ToUpperInvariant();
        }

        // ------------------------------------------------------------ tuning

        void ToggleEditing()
        {
            _editing = !_editing;
            _stats.SetEditing(_editing);
            RefreshTuningControls();
        }

        void RefreshTuningControls()
        {
            if (_editButton != null)
            {
                var caption = _editButton.GetComponentInChildren<Text>();
                if (caption != null)
                {
                    caption.text = _editing ? "DONE" : "EDIT";
                    caption.color = _editing ? GameConfig.UiBackground : GameConfig.UiText;
                }
                _editButton.GetComponent<Image>().color =
                    _editing ? GameConfig.UiAccent : GameConfig.UiPanelLight;
            }

            if (_statusLine == null) return;

            int changed = TuningStore.OverriddenRecordCount;
            _statusLine.text = changed == 0
                ? (TuningStore.HasOverrides ? "No changes — tuning file is empty." : "Shipped values.")
                : $"{changed} record(s) changed — SAVE to keep them.";
            _statusLine.color = changed == 0 ? GameConfig.UiTextDim : GameConfig.UiAccent;
        }

        void SaveTuning()
        {
            string path = TuningStore.Save();
            _statusLine.text = $"Saved {TuningStore.OverriddenRecordCount} record(s) -> {path}";
            _statusLine.color = UiTheme.Success;
        }

        void RevertSelected()
        {
            if (_selected == null) return;

            if (!TuningStore.Revert(_catalog.key, _selected.id, _selected.record))
            {
                _statusLine.text = $"{_selected.name} is already at its shipped values.";
                _statusLine.color = GameConfig.UiTextDim;
                return;
            }

            if (_selected.record is UnitDefinition unit) unit.RefreshDerived();
            TuningStore.Save();

            RefreshRowText();
            _stats.Rebuild();
            _statusLine.text = $"Reverted {_selected.name}.";
            _statusLine.color = UiTheme.Success;
        }

        void ResetAll()
        {
            int reverted = GameCatalogs.ResetAll();

            // The entries hold display snapshots and the columns hold closures
            // over the records, so the whole tab is reloaded rather than patched.
            SelectCatalog(_catalog);

            _statusLine.text = reverted == 0
                ? "Nothing was overridden — everything is already shipped values."
                : $"Reset {reverted} record(s). The tuning file is gone.";
            _statusLine.color = UiTheme.Success;
        }

        void Update()
        {
            // Escape leaves — unless a field has focus, where it is how you get
            // out of the field.
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            var focused = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (focused != null && focused.GetComponent<InputField>() != null) return;

            SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
