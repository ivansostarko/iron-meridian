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

        /// <summary>
        /// Horizontal inset for everything inside the stat table — the section
        /// headings (GENERAL, POSITION, WEAPONS…), the label/value rows and the
        /// hairline under each row.
        ///
        /// Much wider than <see cref="UiTheme.PanelPadding"/> on purpose. The
        /// table lives inside a scroll viewport, and a narrow inset put the
        /// first character of every label and the last digit of every value
        /// hard against the viewport's clipping edge — legible on paper, shaved
        /// in practice. A generous gutter is also what makes the block read as a
        /// page of data rather than as text pinned to the edge of the screen;
        /// every value is best-fitted (<see cref="UIFactory.Fit"/>), so the
        /// width this costs is paid in type size rather than in truncation.
        ///
        /// This is the *base* gutter, before <see cref="ContentShift"/> slides
        /// the block sideways. <see cref="TableLeftInset"/> and
        /// <see cref="TableRightInset"/> are what the rows actually use.
        /// </summary>
        const float TableInset = 50f;

        /// <summary>
        /// How far the whole content block — the tab strip, the section
        /// headings and every label/value row — is shifted **left** inside the
        /// panel.
        ///
        /// It is a shift rather than extra padding on both sides: the panel is
        /// narrow and the row is already split into two columns, so taking
        /// 25 px off each edge would cost the label column a third of its width
        /// and best-fit would pay for it in type size. Moving the block instead
        /// leaves every column exactly as wide as it was, gives the values a
        /// deeper margin against the screen edge, and closes the gap on the map
        /// side where the panel meets the terrain.
        /// </summary>
        const float ContentShift = 25f;

        /// <summary>
        /// Extra left padding on the content block — the section headings and
        /// every label/value row under GENERAL, POSITION and COMBAT POWER.
        ///
        /// The panel widened to 330 px (see <see cref="UiTheme.RightPanelWidth"/>)
        /// and the table kept its old gutter, which left the labels sitting
        /// closer to the panel's edge than to anything else on it. The value
        /// column is unaffected: it is anchored to the right, so the padding is
        /// paid out of the gutter between the two columns rather than out of the
        /// numbers.
        /// </summary>
        const float ContentPadLeft = 30f;

        /// <summary>
        /// A further 15 px of left gutter on the content block, on top of
        /// <see cref="ContentPadLeft"/>.
        ///
        /// Kept as its own figure rather than folded into the one above: that
        /// one is the padding the 330 px panel was widened to deserve, this one
        /// is a deliberate nudge away from the panel's edge, and separating them
        /// means either can be retuned without re-deriving the other. Only the
        /// label column pays for it — the value column is anchored right, so the
        /// numbers do not move.
        /// </summary>
        const float ContentNudgeLeft = 15f;

        /// <summary>Inset from the panel's left edge to the content — the table's own gutter, shifted.</summary>
        const float TableLeftInset = TableInset - ContentShift + ContentPadLeft + ContentNudgeLeft;
        /// <summary>Inset from the panel's right edge. The 25 px the left gave up ends up here.</summary>
        const float TableRightInset = TableInset + ContentShift;

        /// <summary>
        /// Clear space between the label column and the value column. Without
        /// it the two cells merely stop touching, and a long label sits flush
        /// against its own number with nothing to separate them.
        /// </summary>
        const float ColumnGutter = 12f;
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

        /// <summary>The canvas the panel lives on — where <see cref="ConfirmDialog"/> is put up.</summary>
        Canvas _canvas;

        public void Build(Canvas canvas)
        {
            _canvas = canvas;
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitInfoPanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            // Below the strike dock's icon strip, not under the top bar: those
            // icons must stay reachable with a formation selected, so nothing
            // on this edge is allowed to cover them. See StrikeDockUI.
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

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

            // Unit names run from "EOD" to "Surface-to-air missile", so the
            // title has to shrink rather than overrun the close button.
            _title = UIFactory.CreateText(_panel, "", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(_title.rectTransform, 76f, 18f, PanelWidth - 96f, 26f);
            UIFactory.Fit(_title, 12);

            _affiliation = UIFactory.CreateText(_panel, "", UiTheme.FontSmall, UiTheme.Accent, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_affiliation.rectTransform, 76f, 46f, PanelWidth - 96f, 18f);
        }

        // -------------------------------------------------------------- tabs

        void BuildTabs()
        {
            // The strip keeps its left edge and gives up ContentShift on the
            // right, so all four tabs sit that much further left — the same
            // move the table below makes. It is not offset to a negative x:
            // the panel has no mask, and a strip hanging past its left edge
            // would draw its fill and its underline over the map.
            float stripWidth = PanelWidth - ContentShift;

            var strip = UIFactory.CreateGroup(_panel, "Tabs");
            UIFactory.Place(strip, new Vector2(0f, 1f), new Vector2(0, -84), new Vector2(stripWidth, 52));

            var rule = UIFactory.CreateDivider(strip, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.anchoredPosition = Vector2.zero;

            float w = stripWidth / 4f;
            // Captions are abbreviated where the full word does not fit a
            // quarter of the panel. "EQUIPMENT" at 75 px was being best-fitted
            // down to an unreadable 7 pt to squeeze in; "EQUIP" is shorter than
            // the space it has and stays at full size, which is the better
            // trade — the icon above it already carries the meaning.
            AddTab(strip, Tab.Info, "INFO", UiIcons.Info, 0 * w, w);
            AddTab(strip, Tab.Equipment, "EQUIP", UiIcons.Equipment, 1 * w, w);
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

            var text = UIFactory.CreateText(holder, label, 10, UiTheme.TextDim, TextAnchor.UpperCenter, FontStyle.Bold);
            // Inset from the tab's own edges so adjacent captions have clear air
            // between them rather than meeting in the middle of the seam.
            UIFactory.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(w - 10, 14));
            UIFactory.Fit(text, 8);

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

            // Prev/next step through the selected unit's own side. The row
            // above it names what its arrows do ("Heading 0°"); a bare pair of
            // chevrons underneath was the odd one out, readable only by pressing
            // it. The caption sits between them, in the gap they already left.
            var cycleLabel = UIFactory.CreateText(_panel, "Next Unit", UiTheme.FontSmall,
                UiTheme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(cycleLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(PanelWidth - 120, 32));
            UIFactory.Fit(cycleLabel, 9);

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

            // No glyph: a red, full-width, single-purpose button is already
            // unmistakable, and the bin icon only shifted the caption off the
            // button's centre line to make room for a second way of saying the
            // same thing. The words now sit where the button is.
            var remove = UIFactory.CreateButton(_panel, "REMOVE UNIT", RequestRemove,
                UiTheme.Danger, UiTheme.Text, UiTheme.FontHeading);
            var rrt = (RectTransform)remove.transform;
            UIFactory.Place(rrt, new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(PanelWidth - 24, 38));
            var caption = remove.GetComponentInChildren<Text>(true);
            if (caption != null) caption.fontStyle = FontStyle.Bold;
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

        /// <summary>
        /// Asks before taking a formation off the map.
        ///
        /// Ctrl+Z does put it back, but the button is full-width, red and sits
        /// directly under the arrows that step from one unit to the next — the
        /// mis-click it invites is one the player would have to notice before
        /// they could undo it. The modal names the unit, so the answer to "am I
        /// deleting the right one" is on screen rather than behind the dialog.
        /// </summary>
        void RequestRemove()
        {
            if (_current == null) return;

            var unit = _current;
            string name = string.IsNullOrEmpty(unit.State.customName)
                ? unit.Def.name : unit.State.customName;

            ConfirmDialog.Open(_canvas, "REMOVE UNIT",
                $"Take {name} off the map? The formation and its orders go with it. " +
                "Ctrl+Z puts it back.",
                "REMOVE UNIT",
                () =>
                {
                    // The selection can change while the modal is up, so act on
                    // the formation the player was looking at when they pressed
                    // the button, not on whatever is current when they confirm.
                    if (unit == null || !unit.IsAlive) return;
                    RemoveRequested?.Invoke(unit);
                });
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
            Row("Branch", UnitBranchInfo.DisplayName(d.Branch));
            Row("Category", UnitCategoryInfo.DisplayName(d.Category));
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

            // Only while there is a march to report. A MARCH block that always
            // showed "—  ·  —  ·  —" would be three rows of nothing between the
            // position and the combat power in the common case.
            var mover = _current.Mover;
            if (mover != null && mover.IsMoving)
            {
                Section("MARCH");
                Row("March speed", $"{d.speedKmh:0} km/h");
                Row("Distance to go", $"{mover.RemainingKm:0.#} km");
                Row("ETA", UnitMover.FormatDuration(mover.EtaGameSeconds), UiTheme.Accent);
                int legs = mover.WaypointsRemaining;
                if (legs > 1) Row("Legs remaining", legs.ToString());
            }

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
            Row("Current Order", s.status == nameof(UnitStatus.Moving) ? "Move" : "Defend");
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
            status == nameof(UnitStatus.Destroyed) ? UiTheme.Hostile
            : status == nameof(UnitStatus.Routed) ? UiTheme.Warning
            : status == nameof(UnitStatus.Engaging) ? UiTheme.Warning
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
                new Vector2(TableLeftInset, 4),
                new Vector2(PanelWidth - TableLeftInset - TableRightInset, 18));
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
            rule.offsetMin = new Vector2(TableLeftInset, 0);
            rule.offsetMax = new Vector2(-TableRightInset, 1);

            // The row is split into a label column and a value column with a
            // gutter between them, all measured from the *actual* row width
            // rather than from PanelWidth.
            //
            // Measuring from PanelWidth was the bug: the row is laid out by a
            // VerticalLayoutGroup inside a viewport, so its real width is the
            // viewport's, and any inset the viewport adds made every cell that
            // much too wide — pushing the label off the left edge and the value
            // off the right. Stretch anchors make both columns follow whatever
            // width the row actually gets.
            const float LabelShare = 0.50f;

            var lbl = UIFactory.CreateText(row, label, UiTheme.FontLabel, UiTheme.TextDim, TextAnchor.MiddleLeft);
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(LabelShare, 1f);
            lr.offsetMin = new Vector2(TableLeftInset, 0);
            lr.offsetMax = new Vector2(-ColumnGutter * 0.5f, 0);
            UIFactory.Fit(lbl, 8);

            var val = UIFactory.CreateText(row, value, UiTheme.FontLabel,
                valueColour ?? UiTheme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(LabelShare, 0f);
            vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = new Vector2(ColumnGutter * 0.5f, 0);
            vr.offsetMax = new Vector2(-TableRightInset, 0);
            UIFactory.Fit(val, 8);

            _values[label] = val;
        }

        /// <summary>
        /// Moves the panel's top edge, so it can clear whatever is docked above
        /// it on this edge — the fire-menu cluster always, and the minimap too
        /// once a battle starts. One caller decides for all of them; see
        /// <c>GameController.RefreshRightDockTop</c>.
        /// </summary>
        public void SetTopInset(float pixels)
        {
            if (_panel != null) _panel.offsetMax = new Vector2(0, -pixels);
        }
    }
}
