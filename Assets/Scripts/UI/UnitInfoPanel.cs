using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Right-side unit inspector: identity header, four tabs (Info, Equipment,
    /// Orders, Status), and a sectioned label/value table, over a footer with
    /// prev/next unit and the destructive remove action.
    ///
    /// Splitting the stat block across tabs is what keeps the panel readable —
    /// the previous single scroll ran to thirty rows and buried the fields that
    /// matter while giving an order.
    /// </summary>
    public class UnitInfoPanel : MonoBehaviour
    {
        public System.Action<UnitActor> RemoveRequested;
        /// <summary>Step to the previous/next unit of the same team (-1 / +1).</summary>
        public System.Action<int> CycleRequested;

        const float PanelWidth = UiTheme.RightPanelWidth;
        /// <summary>Header (icon, name, affiliation) plus the tab strip.</summary>
        const float TopBlockHeight = 156f;
        /// <summary>Heading rotator, cycle row and the remove button.</summary>
        const float BottomBlockHeight = 132f;

        enum Tab { Info, Equipment, Orders, Status }

        RectTransform _panel;
        Image _icon;
        Text _title, _affiliation, _headingLabel;
        RectTransform _tableContent;
        UnitActor _current;
        Tab _tab = Tab.Info;

        readonly List<(Tab tab, Image fill, Image glyph, Text label, RectTransform underline)> _tabs =
            new List<(Tab, Image, Image, Text, RectTransform)>();

        // Value labels by row key, so the periodic refresh rewrites values
        // without tearing the table down. _builtFor/_builtTab track what the
        // current rows belong to.
        readonly Dictionary<string, Text> _values = new Dictionary<string, Text>();
        UnitActor _builtFor;
        Tab _builtTab;
        bool _rebuilding;

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitInfoPanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -UiTheme.TopBarHeight);

            // Hairline down the panel's left edge, separating it from the map.
            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            BuildHeader();
            BuildTabs();
            BuildTable();
            BuildFooter();

            Hide();
        }

        // ------------------------------------------------------------ header

        void BuildHeader()
        {
            var close = UIFactory.CreateButton(_panel, "✕", Hide, new Color(0, 0, 0, 0), UiTheme.TextDim, 16);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-8, -8), new Vector2(30, 30));

            // The unit's own APP-6 icon, framed — the design's identity block.
            var frame = UIFactory.CreateBorderedPanel(_panel, "IconFrame", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(UiTheme.PanelPadding, -16), new Vector2(52, 52));

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(frame, false);
            _icon = iconGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            var irt = (RectTransform)iconGo.transform;
            UIFactory.Stretch(irt);
            irt.offsetMin = new Vector2(6, 6);
            irt.offsetMax = new Vector2(-6, -6);

            _title = UIFactory.CreateText(_panel, "", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(76, -22), new Vector2(PanelWidth - 96, 26));

            _affiliation = UIFactory.CreateText(_panel, "", UiTheme.FontSmall, UiTheme.Accent, TextAnchor.UpperLeft);
            UIFactory.Place(_affiliation.rectTransform, new Vector2(0f, 1f), new Vector2(76, -48), new Vector2(PanelWidth - 96, 20));
        }

        // -------------------------------------------------------------- tabs

        void BuildTabs()
        {
            var strip = UIFactory.CreateGroup(_panel, "Tabs");
            UIFactory.Place(strip, new Vector2(0f, 1f), new Vector2(0, -84), new Vector2(PanelWidth, 52));

            var rule = UIFactory.CreateDivider(strip, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.anchoredPosition = Vector2.zero;

            float w = PanelWidth / 4f;
            AddTab(strip, Tab.Info, "INFO", UiIcons.Info, 0 * w, w);
            AddTab(strip, Tab.Equipment, "EQUIPMENT", UiIcons.Equipment, 1 * w, w);
            AddTab(strip, Tab.Orders, "ORDERS", UiIcons.Orders, 2 * w, w);
            AddTab(strip, Tab.Status, "STATUS", UiIcons.Pulse, 3 * w, w);

            RefreshTabs();
        }

        void AddTab(RectTransform strip, Tab tab, string label, Sprite glyph, float x, float w)
        {
            var holder = UIFactory.CreatePanel(strip, "Tab_" + label, new Color(0, 0, 0, 0));
            UIFactory.Place(holder, new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(w, 52));
            holder.pivot = new Vector2(0f, 0.5f);

            var btn = holder.gameObject.AddComponent<Button>();
            btn.targetGraphic = holder.GetComponent<Image>();
            btn.onClick.AddListener(() => SetTab(tab));

            var img = UIFactory.CreateImage(holder, glyph, "Glyph");
            img.raycastTarget = false;
            UIFactory.Place((RectTransform)img.transform, new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(17, 17));

            var text = UIFactory.CreateText(holder, label, 9, UiTheme.TextDim, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(w, 14));

            // Active-tab indicator, sitting on the strip's bottom rule.
            var underline = UIFactory.CreatePanel(holder, "Underline", UiTheme.Accent);
            underline.anchorMin = new Vector2(0, 0); underline.anchorMax = new Vector2(1, 0);
            underline.pivot = new Vector2(0.5f, 0);
            underline.sizeDelta = new Vector2(0, 2);
            underline.anchoredPosition = Vector2.zero;
            underline.GetComponent<Image>().raycastTarget = false;

            _tabs.Add((tab, holder.GetComponent<Image>(), img, text, underline));
        }

        void SetTab(Tab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            RefreshTabs();
            Refresh();
        }

        void RefreshTabs()
        {
            foreach (var (tab, fill, glyph, label, underline) in _tabs)
            {
                bool on = tab == _tab;
                fill.color = on ? UiTheme.AccentWash : new Color(0, 0, 0, 0);
                glyph.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                label.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                underline.gameObject.SetActive(on);
            }
        }

        // ------------------------------------------------------------- table

        void BuildTable()
        {
            var scroll = UIFactory.CreateScrollView(_panel, out _tableContent);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, BottomBlockHeight);
            srt.offsetMax = new Vector2(0, -TopBlockHeight);

            // The shared scroll defaults are tuned for the palette's big cards;
            // a dense stat table needs far tighter spacing.
            var layout = _tableContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 0;
            layout.padding = new RectOffset(0, 0, 6, 8);
        }

        // ------------------------------------------------------------ footer

        void BuildFooter()
        {
            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.anchoredPosition = new Vector2(0, BottomBlockHeight);

            // Facing: units are drawn with a heading, so it needs a control.
            var rotLeft = UIFactory.CreateButton(_panel, "◄", () => Rotate(-15f),
                UiTheme.Surface, UiTheme.TextDim, 15);
            UIFactory.Place((RectTransform)rotLeft.transform, new Vector2(0f, 0f),
                new Vector2(UiTheme.PanelPadding, 96), new Vector2(40, 28));

            _headingLabel = UIFactory.CreateText(_panel, "Heading 0°", UiTheme.FontSmall,
                UiTheme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_headingLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 96),
                new Vector2(PanelWidth - 120, 28));

            var rotRight = UIFactory.CreateButton(_panel, "►", () => Rotate(15f),
                UiTheme.Surface, UiTheme.TextDim, 15);
            UIFactory.Place((RectTransform)rotRight.transform, new Vector2(1f, 0f),
                new Vector2(-UiTheme.PanelPadding, 96), new Vector2(40, 28));

            // Prev/next step through the selected unit's own side.
            var prev = UIFactory.CreateBorderedPanel(_panel, "PrevUnit", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(prev, new Vector2(0f, 0f), new Vector2(UiTheme.PanelPadding, 56), new Vector2(46, 32));
            var prevBtn = UIFactory.CreateButton(prev, "◄", () => CycleRequested?.Invoke(-1),
                new Color(0, 0, 0, 0), UiTheme.TextDim, 14);
            UIFactory.Stretch((RectTransform)prevBtn.transform);

            var next = UIFactory.CreateBorderedPanel(_panel, "NextUnit", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(next, new Vector2(1f, 0f), new Vector2(-UiTheme.PanelPadding, 56), new Vector2(46, 32));
            var nextBtn = UIFactory.CreateButton(next, "►", () => CycleRequested?.Invoke(1),
                new Color(0, 0, 0, 0), UiTheme.TextDim, 14);
            UIFactory.Stretch((RectTransform)nextBtn.transform);

            var remove = UIFactory.CreateButton(_panel, "", RequestRemove, UiTheme.Danger, UiTheme.Text, 1);
            var rrt = (RectTransform)remove.transform;
            UIFactory.Place(rrt, new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(PanelWidth - 24, 38));
            var caption = remove.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var bin = UIFactory.CreateImage(rrt, UiIcons.Trash, "TrashGlyph");
            bin.color = UiTheme.Text;
            bin.raycastTarget = false;
            UIFactory.Place((RectTransform)bin.transform, new Vector2(0.5f, 0.5f), new Vector2(-58, 0), new Vector2(15, 15));

            var removeLabel = UIFactory.CreateText(rrt, "REMOVE UNIT", UiTheme.FontHeading,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(removeLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-40, 0), new Vector2(160, 22));
        }

        // ----------------------------------------------------------- lifecycle

        public void Show(UnitActor unit)
        {
            if (_panel == null) return;          // build failed; don't take the scene down with it
            _current = unit;
            if (unit == null) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();           // above GroupPanel, which shares this rect
            string folder = unit.State.TeamEnum == Team.User ? "Friendly" : "Enemy";
            _icon.sprite = UIFactory.LoadIconSprite(folder, unit.Def.id);
            Refresh();
        }

        public void Hide()
        {
            _current = null;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        void Update()
        {
            // Stop refreshing a unit that died or was removed while shown —
            // Refresh() would dereference a destroyed actor.
            if (_current != null && !_current.IsAlive) { Hide(); return; }
            if (_current != null && Time.frameCount % 30 == 0) Refresh();
        }

        void Rotate(float delta)
        {
            if (_current == null) return;
            _current.SetHeading(_current.State.headingDeg + delta);
            _headingLabel.text = $"Heading {_current.State.headingDeg:0}°";
        }

        void RequestRemove()
        {
            if (_current == null) return;
            RemoveRequested?.Invoke(_current);
        }

        // --------------------------------------------------------- table body

        void Refresh()
        {
            if (_current == null || _tableContent == null) return;

            // Rows are rebuilt when the unit or the tab changes; the periodic
            // refresh only rewrites value labels in place.
            _rebuilding = _builtFor != _current || _builtTab != _tab;
            if (_rebuilding)
            {
                // Unparent before Destroy: destruction is deferred to end of
                // frame, so old rows would otherwise sit in the layout beside
                // the new ones. Walk backwards — reparenting mutates the list.
                for (int i = _tableContent.childCount - 1; i >= 0; i--)
                {
                    var c = _tableContent.GetChild(i);
                    c.SetParent(null, false);
                    Destroy(c.gameObject);
                }
                _values.Clear();
                _builtFor = _current;
                _builtTab = _tab;
            }

            var s = _current.State;
            var d = _current.Def;

            _title.text = string.IsNullOrEmpty(s.customName) ? d.name : s.customName;
            bool friendly = s.TeamEnum == Team.User;
            _affiliation.text = friendly ? "Friendly Unit" : "Hostile Unit";
            _affiliation.color = friendly ? UiTheme.Accent : UiTheme.Hostile;
            _headingLabel.text = $"Heading {s.headingDeg:0}°";

            switch (_tab)
            {
                case Tab.Info: BuildInfoTab(s, d); break;
                case Tab.Equipment: BuildEquipmentTab(s, d); break;
                case Tab.Orders: BuildOrdersTab(s, d); break;
                case Tab.Status: BuildStatusTab(s, d); break;
            }
        }

        void BuildInfoTab(UnitState s, UnitDefinition d)
        {
            Section("GENERAL");
            Row("Unit Type", d.name);
            Row("Affiliation", s.affiliation);
            Row("Category", d.Category == UnitCategory.Drone ? "Drone" : "Core Ground");
            Row("Size", s.echelon);
            int manpower = Mathf.RoundToInt(d.manpower *
                EchelonInfo.ManpowerMultiplier(s.EchelonEnum) * Mathf.Clamp01(s.strength));
            int full = Mathf.RoundToInt(d.manpower * EchelonInfo.ManpowerMultiplier(s.EchelonEnum));
            Row("Strength", $"{manpower:n0} / {full:n0}");
            Row("Status", s.status, StatusColour(s.status));

            Section("POSITION");
            Row("Location", $"{Mathf.Abs((float)s.latitude):0.0000}°{(s.latitude >= 0 ? "N" : "S")}, " +
                            $"{Mathf.Abs((float)s.longitude):0.0000}°{(s.longitude >= 0 ? "E" : "W")}");
            Row("Elevation", $"{s.heightMeters:n0} m");
            Row("Team", s.TeamEnum == Team.User ? "User (Blue)" : "Enemy (Red)");
            Row("Heading", $"{s.headingDeg:0}°");

            Section("COMBAT POWER");
            Row("Combat power", $"{_current.CurrentPower():n0}");
            Row("Training", $"{d.training:0}/100");
            Row("Morale", $"{s.morale:0}/100");
            Row("Organisation", $"{s.organisation:0}/100");
        }

        void BuildEquipmentTab(UnitState s, UnitDefinition d)
        {
            Section("WEAPONS");
            Row("Attack", $"{d.attack:0}");
            Row("Hard attack", $"{d.hardAttack:0}");
            Row("Defence", $"{d.defence:0}");
            Row("Armour", $"{d.armour:0}");
            Row("Anti-air", $"{d.antiAir:0}");

            Section("RANGES");
            Row("Weapon range", $"{d.weaponRangeKm:0.#} km");
            Row("View range", $"{d.viewRangeKm:0.#} km");
            Row("Speed", $"{d.speedKmh:0} km/h");

            Section("SUSTAINMENT");
            Row("Ammo type", string.IsNullOrEmpty(d.ammoType) ? "—" : d.ammoType);
            Row("Ammo", $"{s.ammo:n0} / {d.ammoStock:n0}");
            Row("Fuel", d.fuelStock > 0 ? $"{s.fuel:n0} / {d.fuelStock:n0} L" : "—");
            Row("Rations", $"{s.foodDays} days");
        }

        void BuildOrdersTab(UnitState s, UnitDefinition d)
        {
            Section("ORDERS");
            Row("Current Order", s.status == UnitStatus.Moving.ToString() ? "Move" : "Defend");
            Row("Objective", _current.Mover != null && _current.Mover.IsMoving ? "Move to point" : "Hold position");
            Row("Endurance", s.organisation > 60f ? "High" : s.organisation > 30f ? "Medium" : "Low");
            Row("Rules of Engagement", "ROE 1");

            Section("CAPABILITIES");
            Row("Indirect fire", d.canIndirectFire ? "Yes" : "No");
            Row("Counter-UAS", d.canCounterUas ? "Yes" : "No");
            Row("Support unit", d.isSupport ? "Yes" : "No");

            Section("MOVEMENT");
            Row("Moving", _current.Mover != null && _current.Mover.IsMoving ? "Yes" : "No");
            Row("Fuel per km", d.fuelUsePerKm > 0 ? $"{d.fuelUsePerKm:0.#} L" : "—");
        }

        void BuildStatusTab(UnitState s, UnitDefinition d)
        {
            Section("READINESS");
            Row("Status", s.status, StatusColour(s.status));
            Row("Strength", $"{s.strength * 100f:0}%", StrengthColour(s.strength));
            Row("Morale", $"{s.morale:0}/100");
            Row("Organisation", $"{s.organisation:0}/100");

            Section("SUPPLY");
            Row("Ammo", $"{s.ammo:n0} / {d.ammoStock:n0}", s.ammo <= 0 ? UiTheme.Hostile : (Color?)null);
            Row("Fuel", d.fuelStock > 0 ? $"{s.fuel:n0} L" : "—");
            Row("Rations", $"{s.foodDays} days");

            Section("IDENTIFICATION");
            Row("Call sign", string.IsNullOrEmpty(s.customName) ? d.name : s.customName);
            Row("Instance", s.instanceId);
            Row("Group", string.IsNullOrEmpty(s.groupName) ? "—" : s.groupName);
        }

        static Color StatusColour(string status) =>
            status == UnitStatus.Destroyed.ToString() ? UiTheme.Hostile
            : status == UnitStatus.Routed.ToString() ? UiTheme.Warning
            : status == UnitStatus.Engaging.ToString() ? UiTheme.Warning
            : UiTheme.Success;

        static Color StrengthColour(float strength01) =>
            strength01 < 0.3f ? UiTheme.Hostile
            : strength01 < 0.6f ? UiTheme.Warning
            : UiTheme.Text;

        void Section(string label)
        {
            if (!_rebuilding) return;
            var holder = UIFactory.CreateGroup(_tableContent, "Section_" + label);
            holder.sizeDelta = new Vector2(0, 32);

            var h = UIFactory.CreateSectionHeader(holder, label);
            UIFactory.Place(h.rectTransform, new Vector2(0f, 0f),
                new Vector2(UiTheme.PanelPadding, 4), new Vector2(PanelWidth - 24, 18));
        }

        void Row(string label, string value, Color? valueColour = null)
        {
            if (!_rebuilding)
            {
                if (_values.TryGetValue(label, out var existing) && existing != null)
                {
                    existing.text = value;
                    if (valueColour.HasValue) existing.color = valueColour.Value;
                }
                return;
            }

            var row = UIFactory.CreatePanel(_tableContent, "Row_" + label, new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, UiTheme.RowHeight);

            // Hairline under each row: what makes the block read as a data table.
            var rule = UIFactory.CreateDivider(row, new Color(1f, 1f, 1f, 0.045f));
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.offsetMin = new Vector2(UiTheme.PanelPadding, 0);
            rule.offsetMax = new Vector2(-UiTheme.PanelPadding, 1);

            // Both cells stretch with the row and are inset in pixels, so they
            // track whatever width the layout hands the row. Fractional anchors
            // plus overflow let text render outside the row entirely — labels
            // used to spill off the panel's left edge and values off its right.
            var lbl = UIFactory.CreateText(row, label, UiTheme.FontSmall, UiTheme.TextDim, TextAnchor.MiddleLeft);
            var lr = lbl.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(UiTheme.PanelPadding, 0);
            lr.offsetMax = new Vector2(-(PanelWidth * 0.46f), 0);

            var val = UIFactory.CreateText(row, value, UiTheme.FontSmall,
                valueColour ?? UiTheme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(1f, 0f); vr.anchorMax = new Vector2(1f, 1f);
            vr.pivot = new Vector2(1f, 0.5f);
            vr.sizeDelta = new Vector2(PanelWidth * 0.5f, 0);
            vr.anchoredPosition = new Vector2(-UiTheme.PanelPadding, 0);

            _values[label] = val;
        }
    }
}
