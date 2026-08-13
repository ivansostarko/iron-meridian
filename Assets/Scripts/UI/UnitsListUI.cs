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
        const float TableX = 60f, TableW = 1185f, TableY = -256f, TableH = 780f;
        const float PanelW = 595f, PanelH = 866f, PanelY = -170f;
        const float RowPad = 8f;      // CreateScrollView's content padding

        enum SortKey { Name, Category, Attack, Defence, Armour, Range, Speed, Manpower }
        enum TeamView { Both, Friendly, Enemy }
        enum CategoryView { All, Ground, Drone }

        readonly struct Column
        {
            public readonly string Label;
            public readonly float X, W;
            public readonly SortKey? Sort;
            public Column(string label, float x, float w, SortKey? sort = null)
            { Label = label; X = x; W = w; Sort = sort; }
        }

        static readonly Column[] Columns =
        {
            new Column("ICON",     0,    108),
            new Column("NAME",     108,  340, SortKey.Name),
            new Column("CATEGORY", 448,  130, SortKey.Category),
            new Column("ATK",      578,  78,  SortKey.Attack),
            new Column("DEF",      656,  78,  SortKey.Defence),
            new Column("ARM",      734,  78,  SortKey.Armour),
            new Column("RANGE",    812,  100, SortKey.Range),
            new Column("SPEED",    912,  110, SortKey.Speed),
            new Column("MANPOWER", 1022, 120, SortKey.Manpower),
        };

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
            IronMeridian.Audio.AudioManager.Apply();
            var canvas = UIFactory.CreateCanvas("UnitsListCanvas");

            var bg = UIFactory.CreatePanel(canvas.transform, "Background", GameConfig.UiBackground);
            UIFactory.Stretch(bg);

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
            UIFactory.Place(bar, new Vector2(0f, 1f), new Vector2(TableX, -170), new Vector2(TableW, 52));

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
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(TableX, -228), new Vector2(TableW, 22));
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
            UIFactory.Place(table, new Vector2(0f, 1f), new Vector2(TableX, TableY), new Vector2(TableW, TableH));

            _header = UIFactory.CreatePanel(table, "Header", GameConfig.UiPanel);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1);
            _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -42); _header.offsetMax = Vector2.zero;

            var scroll = UIFactory.CreateScrollView(table, out _rowsContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -46);
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
                    UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f),
                        new Vector2(col.X + RowPad + 4, 0), new Vector2(col.W - 8, 34));
                    continue;
                }

                var key = col.Sort.Value;
                var btn = UIFactory.CreateButton(_header, label, () => ToggleSort(key),
                    new Color(1f, 1f, 1f, active ? 0.08f : 0.02f),
                    active ? GameConfig.UiAccent : GameConfig.UiText, 15);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 0.5f),
                    new Vector2(col.X + RowPad, 0), new Vector2(col.W - 4, 34));

                var txt = btn.GetComponentInChildren<Text>();
                txt.alignment = TextAnchor.MiddleLeft;
                txt.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                txt.rectTransform.offsetMin = new Vector2(4, 0);
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
            float iconX = Columns[0].X + 6;
            if (_team != TeamView.Enemy) { PlaceIcon(row, "Friendly", def.id, iconX); iconX += 50; }
            if (_team != TeamView.Friendly) PlaceIcon(row, "Enemy", def.id, iconX);

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

        void PlaceIcon(Transform parent, string folder, string unitId, float x)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite == null) return;
            var img = UIFactory.CreateImage(parent, sprite, folder + "Icon");
            UIFactory.Place((RectTransform)img.transform, new Vector2(0f, 0.5f),
                new Vector2(x, 0), new Vector2(44, 44));
            img.raycastTarget = false;    // clicks belong to the row
        }

        void Cell(Transform parent, string text, Column col, int fontSize, Color? color = null)
        {
            var t = UIFactory.CreateText(parent, text, fontSize, color, TextAnchor.MiddleLeft);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(col.X + 4, 0), new Vector2(col.W - 8, 40));
        }

        // ----------------------------------------------------- detail panel

        void BuildDetailPanel(Transform parent)
        {
            var panel = UIFactory.CreatePanel(parent, "DetailPanel", GameConfig.UiPanel);
            UIFactory.Place(panel, new Vector2(1f, 1f), new Vector2(-60, PanelY), new Vector2(PanelW, PanelH));

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

            var scroll = UIFactory.CreateScrollView(panel, out _detailStats);
            UIFactory.Place((RectTransform)scroll.transform, new Vector2(0f, 1f),
                new Vector2(16, -462), new Vector2(PanelW - 32, PanelH - 478));
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
                PlaceIcon(_detailIcons, "Friendly", def.id, 0);
                PlaceIcon(_detailIcons, "Enemy", def.id, 52);
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

        void Stat(string label, string value)
        {
            var row = UIFactory.CreateGroup(_detailStats, "Stat_" + label);
            row.sizeDelta = new Vector2(0, 24);

            var l = UIFactory.CreateText(row, label, 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(0.55f, 1);
            l.rectTransform.offsetMin = Vector2.zero; l.rectTransform.offsetMax = Vector2.zero;

            var v = UIFactory.CreateText(row, value, 15, GameConfig.UiText, TextAnchor.MiddleRight);
            v.rectTransform.anchorMin = new Vector2(0.55f, 0); v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(-4, 0);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
