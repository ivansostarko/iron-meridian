using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Logistics;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// What one supply point is holding, and who can reach it — opened by
    /// **clicking the installation on the map**.
    ///
    /// Clicking the thing you want to know about is the only discoverable way
    /// in, and it is the question the player actually has: standing over a rear
    /// area, *which of these is nearly out, and is anything close enough to use
    /// it?* The LOGISTICS panel lists what exists; this says what it is worth.
    ///
    /// **Three readings, in the order they are asked.**
    ///
    /// • **How much is left**, as a bar and as issues — the number that decides
    ///   whether the position can be held.
    /// • **What it reaches**, in kilometres, because the whole geometry of a
    ///   rear area is whether the service radius covers the formations that need
    ///   it.
    /// • **Who is in it right now**, one row per formation, with what that
    ///   formation is actually short of. A unit already full is listed and
    ///   greyed rather than hidden: the usual answer to "why is this cache not
    ///   going down" is that everything near it is full, and a list that
    ///   silently omitted them could not say so.
    ///
    /// It shares the right-hand edge with the unit inspector, the group panel
    /// and the front-line options, so opening it stands them down — see
    /// <see cref="Opened"/>.
    ///
    /// See docs/26-LOGISTICS.md and docs/29-AIR-SUPPLY.md.
    /// </summary>
    public class SupplyPanelUI : MonoBehaviour
    {
        /// <summary>True while the panel is showing.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when the panel opens, so competing right-hand panels can stand down.</summary>
        public System.Action Opened;
        /// <summary>Fly the camera to a formation in the list.</summary>
        public System.Action<UnitActor> FocusUnitRequested;
        /// <summary>Take this installation off the map.</summary>
        public System.Action<LogisticsSite> RemoveRequested;

        /// <summary>
        /// Draw or drop one site's **service ring** on the terrain.
        ///
        /// Raised as the panel opens and closes rather than owned here, because
        /// the ring belongs to the map: <c>LogisticsSystem</c> knows whether the
        /// LOGISTICS panel is already showing every ring, in which case a panel
        /// closing must not pull one down. The panel's job is to say which site
        /// is being looked at.
        ///
        /// It is a **ring rather than a number** for the reason the reach line
        /// gives: the whole geometry of a rear area is whether the radius covers
        /// the formations that need it, and that is a question about ground.
        /// </summary>
        public System.Action<LogisticsSite, bool> RingRequested;

        const float Pad = UiTheme.PanelPadding;
        const float RowHeight = 46f;
        /// <summary>Seconds between repaints while the panel is up.</summary>
        const float RefreshSeconds = 0.5f;

        RectTransform _panel, _listContent, _stockFill, _capacityRow, _capacityControls;
        Text _title, _side, _stockText, _reachText, _serviceText, _inRangeText, _emptyNote;
        Text _capacityText;
        Image _stockTrack;

        LogisticsSite _site;
        ResupplySystem _resupply;
        float _timer;

        public LogisticsSite Site => _site;

        public void Build(Canvas canvas, ResupplySystem resupply)
        {
            _resupply = resupply;

            _panel = UIFactory.CreatePanel(canvas.transform, "SupplyPanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0);
            _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-UiTheme.RightPanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1);
            edge.pivot = new Vector2(0, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            BuildHeader();
            BuildStockBlock();
            BuildList();

            _panel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ header

        void BuildHeader()
        {
            _title = UIFactory.CreateText(_panel, "", UiTheme.FontTitle, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(_title.rectTransform, Pad, 14f,
                UiTheme.RightPanelWidth - Pad * 2f - 30f, 24f);
            UIFactory.Fit(_title, 12);

            var close = UIFactory.CreateIconButton(_panel, UiIcons.Close, Hide,
                new Color(0, 0, 0, 0), UiTheme.TextDim);
            UIFactory.PlaceTopLeft((RectTransform)close.transform,
                UiTheme.RightPanelWidth - Pad - 22f, 14f, 22f, 22f);
            UiTooltip.Attach(close.gameObject, "Close", UiTooltip.Side.Left);

            _side = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_side.rectTransform, Pad, 40f,
                UiTheme.RightPanelWidth - Pad * 2f, 16f);

            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(Pad, 0); rule.offsetMax = new Vector2(-Pad, 0);
            rule.anchoredPosition = new Vector2(0, -64f);
        }

        // ------------------------------------------------------------- stock

        /// <summary>
        /// The stock bar.
        ///
        /// A bar as well as a figure because the two answer different questions:
        /// "four issues" is what you plan with, and a bar a third full is what
        /// you see without reading. It is the same device the unit counters use
        /// for strength, which is deliberate — a supply point running out and a
        /// formation being ground down are the same shape of problem.
        /// </summary>
        void BuildStockBlock()
        {
            var header = UIFactory.CreateSectionHeader(_panel, "STOCK", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(header.rectTransform, Pad, 76f,
                UiTheme.RightPanelWidth - Pad * 2f, 14f);

            var track = UIFactory.CreatePanel(_panel, "StockTrack", new Color(0f, 0f, 0f, 0.45f));
            UIFactory.PlaceTopLeft(track, Pad, 96f, UiTheme.RightPanelWidth - Pad * 2f, 14f);
            _stockTrack = track.GetComponent<Image>();
            _stockTrack.raycastTarget = false;

            _stockFill = UIFactory.CreatePanel(track, "StockFill", UiTheme.Success);
            _stockFill.anchorMin = Vector2.zero;
            _stockFill.anchorMax = new Vector2(0f, 1f);
            _stockFill.pivot = new Vector2(0f, 0.5f);
            _stockFill.offsetMin = Vector2.zero;
            _stockFill.offsetMax = Vector2.zero;
            _stockFill.GetComponent<Image>().raycastTarget = false;

            _stockText = UIFactory.CreateText(_panel, "", UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_stockText.rectTransform, Pad, 116f,
                UiTheme.RightPanelWidth - Pad * 2f, 18f);

            BuildCapacityRow();

            // Sized for the longest string each of these can hold, because
            // UIFactory.CreateText leaves verticalOverflow at Overflow: a box
            // that is too short does not truncate, it spills over whatever is
            // beneath it. At FontLabel in the ~306 px of content width here a
            // line runs to about 57 characters, so this is three lines for the
            // service sentence and two for the reach line. Anything longer added
            // to either string needs the height raised to match.
            _serviceText = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_serviceText.rectTransform, Pad, 172f,
                UiTheme.RightPanelWidth - Pad * 2f, 46f);

            _reachText = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_reachText.rectTransform, Pad, 222f,
                UiTheme.RightPanelWidth - Pad * 2f, 30f);

            var remove = UIFactory.CreateBorderedPanel(_panel, "RemoveSite",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.PlaceTopLeft(remove, Pad, 258f, UiTheme.RightPanelWidth - Pad * 2f, 28f);
            var removeBtn = UIFactory.CreateButton(remove, "REMOVE INSTALLATION",
                () => { if (_site != null) RemoveRequested?.Invoke(_site); Hide(); },
                new Color(0, 0, 0, 0), UiTheme.Danger, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)removeBtn.transform);
        }

        /// <summary>
        /// How much this **particular** installation holds — set here, on the
        /// site, rather than taken from the catalogue and left there.
        ///
        /// The catalogue figure is a sensible default, not a rule: a scenario in
        /// which every depot holds exactly forty issues is a scenario where the
        /// rear area has no shape. A forward dump that is meant to run out
        /// halfway through the battle is a *design decision*, and it needs
        /// somewhere to be made. `LogisticsSiteData.capacity` has always been in
        /// the save file; until now nothing could write it.
        ///
        /// **Editor only.** The row is hidden once the battle is running.
        /// Topping a depot up mid-fight is not a design decision, it is a cheat,
        /// and a control that is only sometimes legitimate is better absent than
        /// present-and-disapproved-of.
        /// </summary>
        void BuildCapacityRow()
        {
            _capacityRow = UIFactory.CreateGroup(_panel, "CapacityRow");
            UIFactory.PlaceTopLeft(_capacityRow, Pad, 138f,
                UiTheme.RightPanelWidth - Pad * 2f, 28f);

            float width = UiTheme.RightPanelWidth - Pad * 2f;

            var label = UIFactory.CreateText(_capacityRow, "HOLDS", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            UIFactory.Place(label.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(52f, 16f));

            _capacityText = UIFactory.CreateText(_capacityRow, "", UiTheme.FontSmall,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _capacityText.raycastTarget = false;
            UIFactory.Place(_capacityText.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(54f, 0f), new Vector2(110f, 18f));

            // The controls sit in a group of their own so the battle can take
            // *them* away without taking the figure with them — see RefreshStock.
            _capacityControls = UIFactory.CreateGroup(_capacityRow, "CapacityControls");
            UIFactory.Stretch(_capacityControls);

            Stepper("Less", "−", -CapacityStep, width - 96f);
            Stepper("More", "+", CapacityStep, width - 68f);

            var fill = UIFactory.CreateButton(_capacityControls, "FILL", () => SetStock(Capacity()),
                UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)fill.transform, new Vector2(0f, 0.5f),
                new Vector2(width - 38f, 0f), new Vector2(38f, 24f));
            UiTooltip.Attach(fill.gameObject, "Restock this installation to its limit",
                UiTooltip.Side.Left);
        }

        /// <summary>Issues one press of − or + moves the limit by.</summary>
        const double CapacityStep = 5.0;
        /// <summary>A site that holds nothing is a site that does nothing; one issue is the floor.</summary>
        const double MinCapacity = 1.0;

        void Stepper(string name, string caption, double delta, float x)
        {
            var button = UIFactory.CreateButton(_capacityControls, caption, () => Step(delta),
                UiTheme.SurfaceHover, UiTheme.Text, UiTheme.FontSmall);
            button.name = name;
            UIFactory.Place((RectTransform)button.transform, new Vector2(0f, 0.5f),
                new Vector2(x, 0f), new Vector2(24f, 24f));
        }

        double Capacity() => _site == null ? 0.0 : _site.Data.capacity;

        void Step(double delta)
        {
            if (_site == null) return;

            double capacity = System.Math.Max(MinCapacity, _site.Data.capacity + delta);
            _site.Data.capacity = capacity;
            // Stock follows the ceiling down. A depot recorded as holding more
            // than it can is a save that contradicts itself, and the bar would
            // draw past its own track.
            _site.Data.stock = System.Math.Min(_site.Data.stock, capacity);
            AfterStockEdit();
        }

        void SetStock(double stock)
        {
            if (_site == null) return;
            _site.Data.stock = System.Math.Max(0.0, System.Math.Min(stock, _site.Data.capacity));
            AfterStockEdit();
        }

        void AfterStockEdit()
        {
            // The marker carries the figure and the bar as well as this panel,
            // so the map has to be told rather than left to notice.
            _site.RefreshStock();
            Refresh();
        }

        // -------------------------------------------------------------- list

        void BuildList()
        {
            _inRangeText = UIFactory.CreateSectionHeader(_panel, "IN RANGE", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(_inRangeText.rectTransform, Pad, 296f,
                UiTheme.RightPanelWidth - Pad * 2f, 14f);

            var scroll = UIFactory.CreateScrollView(_panel, out _listContent, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(Pad, Pad);
            rt.offsetMax = new Vector2(-Pad, -316f);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 3; layout.padding = new RectOffset(0, 0, 2, 6); }

            _emptyNote = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(_emptyNote.rectTransform, Pad, 320f,
                UiTheme.RightPanelWidth - Pad * 2f, 60f);
        }

        // ------------------------------------------------------------- show

        public void Show(LogisticsSite site)
        {
            if (site == null) { Hide(); return; }

            // Clicking straight from one installation to the next: the ring
            // follows the panel, so the site being left drops its own.
            if (_site != null && _site != site) RingRequested?.Invoke(_site, false);

            _site = site;
            _panel.gameObject.SetActive(true);
            IsOpen = true;
            Opened?.Invoke();
            RingRequested?.Invoke(site, true);
            Refresh();
        }

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
            if (_site != null) RingRequested?.Invoke(_site, false);
            _site = null;
            IsOpen = false;
        }

        /// <summary>Closes if it is showing this site — it has just been destroyed or removed.</summary>
        public void HideIfShowing(LogisticsSite site)
        {
            if (_site != null && _site == site) Hide();
        }

        void Update()
        {
            if (!IsOpen) return;

            // The site can be destroyed under the panel — a strike on it, or a
            // cache issuing its last load. Closing is the honest answer; leaving
            // a panel describing something that is no longer on the map is not.
            if (_site == null) { Hide(); return; }

            // Unscaled: the panel keeps reading while the battle is paused,
            // which is exactly when a player is studying a rear area.
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshSeconds;
            Refresh();
        }

        // ----------------------------------------------------------- repaint

        public void Refresh()
        {
            if (!IsOpen || _site == null) return;

            var def = LogisticsCatalog.Get(_site.Kind);
            bool enemy = _site.Data.team == nameof(Team.Enemy);

            _title.text = string.IsNullOrEmpty(_site.Data.label)
                ? def.name : _site.Data.label.ToUpperInvariant();
            _side.text = $"{(enemy ? "ENEMY" : "FRIENDLY")}  ·  {def.detail}" +
                         (_site.Data.airdropped ? "  ·  AIRDROPPED" : "");
            _side.color = enemy ? GameConfig.RedTeam : GameConfig.BlueTeam;

            RefreshStock(def);
            RefreshService(def);
            RefreshList(def);
        }

        void RefreshStock(LogisticsDef def)
        {
            bool tracked = _site.Data.TracksStock;
            float fraction = tracked
                ? Mathf.Clamp01((float)(_site.Data.stock / _site.Data.capacity))
                : 1f;

            _stockFill.anchorMax = new Vector2(fraction, 1f);
            // Green down to a third, amber below it, red on the last issue —
            // the same three-stage reading the strength bars use, so a rear
            // area in trouble looks like a formation in trouble.
            _stockFill.GetComponent<Image>().color =
                fraction > 0.5f ? UiTheme.Success
                : fraction > 0.2f ? UiTheme.Warning
                : UiTheme.Danger;

            _stockText.text = tracked
                ? $"{_site.Data.stock:0.#} of {_site.Data.capacity:0.#} issues left"
                : "Not tracked — this installation does not run out";

            if (_capacityRow != null)
            {
                // The **figure** stays all the time — during a battle "what is
                // this depot's ceiling" is still worth knowing, and blanking the
                // row would leave a hole in the middle of the panel. Only the
                // controls go, because only they are the cheat.
                _capacityRow.gameObject.SetActive(tracked);
                if (_capacityText != null)
                    _capacityText.text = $"{_site.Data.capacity:0.#} ISSUES";
                if (_capacityControls != null)
                    _capacityControls.gameObject.SetActive(!Units.CombatSystem.BattleRunning);
            }
        }

        void RefreshService(LogisticsDef def)
        {
            _serviceText.text = def.service switch
            {
                SupplyService.Ammunition => "Rearms friendly formations standing in its area.",
                SupplyService.Fuel => "Refuels friendly formations standing in its area.",
                SupplyService.Medical =>
                    "Returns lightly wounded to duty — recovers strength slowly, up to 75 %.",
                SupplyService.Repair =>
                    "Returns deadlined vehicles to the road — recovers serviceability, " +
                    "up to fully fit. Formations that walk take nothing from it.",
                SupplyService.General => "Rearms, refuels, repairs and treats, at a reduced rate.",
                _ => "Drawn on the map. This kind does not issue anything yet."
            };

            _reachText.text = $"REACHES {def.serviceRadiusKm:0.#} KM  ·  ring drawn on the " +
                              "ground  ·  formations inside it draw automatically, in battle";
        }

        void RefreshList(LogisticsDef def)
        {
            ClearList();

            if (def.service == SupplyService.None)
            {
                _emptyNote.text = "";
                _inRangeText.text = "IN RANGE";
                return;
            }

            var units = _resupply != null
                ? _resupply.UnitsInRange(_site)
                : new List<UnitActor>();

            _inRangeText.text = $"IN RANGE — {units.Count} FORMATION(S)";

            if (units.Count == 0)
            {
                _emptyNote.text = "Nothing of this side is inside its area. Move a formation " +
                                  "into the circle and it will draw from this automatically once " +
                                  "the battle is running.";
                return;
            }

            _emptyNote.text = "";
            foreach (var unit in units) Row(unit, def);
        }

        /// <summary>
        /// One formation in the area: what it is, and what it is short of.
        ///
        /// The shortfall rather than the absolute figures. "1 200 rounds" means
        /// nothing without the establishment beside it; "AMMO 38 %" is the thing
        /// the player is deciding on, and three of them across a row is a
        /// readable answer to "is this cache doing anything".
        /// </summary>
        void Row(UnitActor unit, LogisticsDef def)
        {
            bool wants = Wants(unit, def);

            var row = UIFactory.CreatePanel(_listContent, "Row_" + unit.State.instanceId,
                wants ? UiTheme.Surface : UiTheme.SurfaceSubtle);
            row.sizeDelta = new Vector2(0, RowHeight);

            string name = string.IsNullOrEmpty(unit.State.customName)
                ? unit.Def.name : unit.State.customName;

            var title = UIFactory.CreateText(row, name, UiTheme.FontSmall,
                wants ? UiTheme.Text : UiTheme.TextFaint, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.raycastTarget = false;
            UIFactory.PlaceTopLeft(title.rectTransform, 8f, 6f,
                UiTheme.RightPanelWidth - 100f, 16f);
            UIFactory.Fit(title, 8);

            var detail = UIFactory.CreateText(row, Shortfall(unit), UiTheme.FontLabel,
                wants ? UiTheme.TextDim : UiTheme.TextFaint, TextAnchor.MiddleLeft);
            detail.raycastTarget = false;
            UIFactory.PlaceTopLeft(detail.rectTransform, 8f, 24f,
                UiTheme.RightPanelWidth - 100f, 16f);
            UIFactory.Fit(detail, 8);

            var captured = unit;
            var focus = UIFactory.CreateButton(row, "◎", () => FocusUnitRequested?.Invoke(captured),
                UiTheme.SurfaceHover, UiTheme.Text, 12);
            UIFactory.Place((RectTransform)focus.transform, new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(26, 26));
            UiTooltip.Attach(focus.gameObject, "Fly to this formation", UiTooltip.Side.Left);
        }

        /// <summary>Whether this formation would actually take anything from this kind of site.</summary>
        static bool Wants(UnitActor unit, LogisticsDef def)
        {
            bool ammo = unit.Def.ammoStock > 0 && unit.State.ammo < unit.Def.ammoStock;
            bool fuel = unit.Def.fuelStock > 0.01f && unit.State.fuel < unit.Def.fuelStock;
            bool hurt = unit.State.strength < 0.75f;
            // Only a formation with equipment can be short of serviceability;
            // UnitActor.Serviceability reports 1 for one that walks, so this is
            // false for it without a second test.
            bool lame = unit.Serviceability < 0.999f;

            return def.service switch
            {
                SupplyService.Ammunition => ammo,
                SupplyService.Fuel => fuel,
                SupplyService.Medical => hurt,
                SupplyService.Repair => lame,
                SupplyService.General => ammo || fuel || hurt || lame,
                _ => false
            };
        }

        static string Shortfall(UnitActor unit)
        {
            string ammo = unit.Def.ammoStock > 0
                ? $"AMMO {100f * unit.State.ammo / unit.Def.ammoStock:0} %"
                : "AMMO —";
            string fuel = unit.Def.fuelStock > 0.01f
                ? $"FUEL {100f * unit.State.fuel / unit.Def.fuelStock:0} %"
                : "FUEL —";
            // A formation that walks gets a dash rather than a permanent 100 %:
            // an unbroken column of full serviceability across a rifle brigade
            // reads as a figure being tracked when it is a figure that does not
            // apply. See UnitActor.HasEquipment.
            string svc = unit.HasEquipment
                ? $"SVC {unit.Serviceability * 100f:0} %"
                : "SVC —";
            return $"{ammo}  ·  {fuel}  ·  STR {unit.State.strength * 100f:0} %  ·  {svc}";
        }

        void ClearList()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                var child = _listContent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        public void SetTopInset(float pixels)
        {
            if (_panel != null) _panel.offsetMax = new Vector2(0, -pixels);
        }

        void OnDestroy()
        {
            if (_site != null) IsOpen = false;
        }
    }
}
