using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — sections describing the force itself: effects, sustainment, reinforcements, groups and stats.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: effects section, sustainment section, reinforcements section, groups section, stats section.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ----------------------------------------------------- effects section

        /// <summary>
        /// Hand-placed effects: arm one, then click the terrain. Named EFFECTS
        /// rather than "Particles" because that is what they are to the player —
        /// how they are drawn is an implementation detail, and the same section
        /// would hold a decal or a mesh effect later.
        /// </summary>
        // ------------------------------------------------- sustainment section

        IronMeridian.Logistics.SustainmentSystem _sustainment;

        /// <summary>Fill the shown side's stocks from what it has deployed.</summary>
        public System.Action<Team, float> StockFromForceRequested;

        /// <summary>The period the consumption column is stated over.</summary>
        enum BurnPeriod { Day, Week, Month }
        BurnPeriod _burnPeriod = BurnPeriod.Day;

        static float BurnDays(BurnPeriod p) => p == BurnPeriod.Day ? 1f : p == BurnPeriod.Week ? 7f : 30f;
        static string BurnWord(BurnPeriod p) => p == BurnPeriod.Day ? "day" : p == BurnPeriod.Week ? "week" : "month";

        readonly List<(ResourceKind kind, InputField field, Text detail)> _resourceRows =
            new List<(ResourceKind, InputField, Text)>();
        readonly List<(BurnPeriod period, Image fill, Text label)> _burnTabs =
            new List<(BurnPeriod, Image, Text)>();
        Text _manpowerFigure, _manpowerDetail, _sustainVerdict;
        /// <summary>Suppresses the write-back while the fields are being filled from the model.</summary>
        bool _sustainSyncing;

        /// <summary>Height of the SUSTAINMENT page inside its scroll view.</summary>
        const float SustainmentPageHeight = 236f + 9f * 58f + 120f;

        /// <summary>
        /// The force's stocks, its burn rate and how long it can go on.
        ///
        /// **Called SUSTAINMENT rather than RESOURCES.** Resources is what a
        /// strategy game calls the numbers in the corner of the screen;
        /// sustainment is what an army calls keeping a force in the field, and
        /// it is the right word for a panel that is about fuel, ammunition
        /// natures, replacements and rations. It also keeps the two logistic
        /// sections distinct at a glance: LOGISTICS is *where the supply is*,
        /// SUSTAINMENT is *how much of it there is*.
        ///
        /// **Stocks are typed; burn rates are not.** Every consumption figure on
        /// this page is arithmetic over the units on the map — see
        /// <see cref="IronMeridian.Logistics.SustainmentSystem"/>. Nobody can
        /// type a rate, so a scenario cannot state a burn that disagrees with
        /// its own order of battle.
        ///
        /// See docs/27-SUSTAINMENT.md.
        /// </summary>
        void BuildSustainmentSection(RectTransform section)
        {
            var content = SidedPage(ScrollableSection(section, SustainmentPageHeight + SideBlock));

            SectionLabel(content, "FORCE ON THE MAP", -8);

            // The head count is the one figure on this page that is not a stock
            // at all, so it gets its own card rather than a row in the table.
            var head = UIFactory.CreateBorderedPanel(content, "Manpower", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(head, new Vector2(0f, 1f), new Vector2(Pad, -28), new Vector2(InnerWidth, 58));

            _manpowerFigure = UIFactory.CreateText(head, "—", 26, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_manpowerFigure.rectTransform, new Vector2(0f, 1f),
                new Vector2(12, -6), new Vector2(InnerWidth - 24f, 30));
            UIFactory.Fit(_manpowerFigure, 14);

            _manpowerDetail = UIFactory.CreateText(head, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_manpowerDetail.rectTransform, new Vector2(0f, 1f),
                new Vector2(12, -38), new Vector2(InnerWidth - 24f, 14));
            UIFactory.Fit(_manpowerDetail, 8);

            // Period tabs rather than three columns: the panel is 250 px wide,
            // and a day / week / month figure side by side would be three
            // unreadable numbers instead of one legible one.
            SectionLabel(content, "CONSUMPTION PER", -96);
            float third = (InnerWidth - 8f) / 3f;
            BurnTab(content, BurnPeriod.Day, "DAY", 0, third, -116);
            BurnTab(content, BurnPeriod.Week, "WEEK", 1, third, -116);
            BurnTab(content, BurnPeriod.Month, "MONTH", 2, third, -116);

            SectionLabel(content, "STOCKS", -160);

            float y = -180f;
            foreach (var def in ResourceCatalog.All)
            {
                ResourceRow(content, def, y);
                y -= 58f;
            }

            _sustainVerdict = UIFactory.CreateText(content, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.UpperLeft);
            UIFactory.Place(_sustainVerdict.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 34));

            var fill = UIFactory.CreateBorderedPanel(content, "StockFromForce", UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(fill, new Vector2(0f, 1f), new Vector2(Pad, y - 44f), new Vector2(InnerWidth, 32));
            var fillBtn = UIFactory.CreateButton(fill, "STOCK 7 DAYS FROM FORCE",
                () => StockFromForceRequested?.Invoke(_team, 7f),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)fillBtn.transform);
            UiTooltip.Attach(fillBtn.gameObject,
                "Fills every stock with a week of this side's current burn", UiTooltip.Side.Left);

            var hint = UIFactory.CreateText(content,
                "Stocks are yours to set and are saved with the map. Consumption is not typed — it is " +
                "worked out from the formations this side has deployed, at their echelon and current " +
                "strength, so it moves the moment the order of battle does.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 84f),
                new Vector2(InnerWidth, 76));

            RefreshSustainment();
        }

        void BurnTab(RectTransform content, BurnPeriod period, string label, int index,
            float width, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Burn_" + label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f),
                new Vector2(Pad + index * (width + 4f), y), new Vector2(width, 28));

            var captured = period;
            var btn = UIFactory.CreateButton(frame, label,
                () => { _burnPeriod = captured; RefreshSustainment(); },
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);

            _burnTabs.Add((period, frame.Find("Fill").GetComponent<Image>(),
                btn.GetComponentInChildren<Text>(true)));
        }

        /// <summary>
        /// One stock line: what it is, an editable figure, and what it costs.
        /// The stock field is on the right where every editable value in this
        /// interface is, and the derived numbers sit under the name as prose —
        /// a table of six columns at this width would be six columns of nothing.
        /// </summary>
        void ResourceRow(RectTransform content, ResourceDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Res_" + def.kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var pip = UIFactory.CreatePanel(frame, "Pip", def.tint);
            pip.anchorMin = new Vector2(0, 0); pip.anchorMax = new Vector2(0, 1);
            pip.pivot = new Vector2(0, 0.5f);
            pip.sizeDelta = new Vector2(3, -10);
            pip.GetComponent<Image>().raycastTarget = false;

            var title = UIFactory.CreateText(frame, def.name, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, 12f, 6f, InnerWidth - 122f, 16f);
            UIFactory.Fit(title, 8);

            var detail = UIFactory.CreateText(frame, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(detail.rectTransform, 12f, 24f, InnerWidth - 122f, 24f);
            UIFactory.Fit(detail, 7);

            var field = UIFactory.CreateInputField(frame, "0", 13);
            UIFactory.Place((RectTransform)field.transform, new Vector2(1f, 0.5f),
                new Vector2(-10, 0), new Vector2(96, 28));
            field.contentType = InputField.ContentType.DecimalNumber;

            var kind = def.kind;
            field.onEndEdit.AddListener(text =>
            {
                if (_sustainSyncing || _sustainment == null) return;
                // A malformed number leaves the stock alone rather than zeroing
                // it — half-typed input is not an instruction to empty a depot.
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    RefreshSustainment();
                    return;
                }
                _sustainment.SetStock(_team, kind, value);
            });

            _resourceRows.Add((kind, field, detail));
        }

        /// <summary>
        /// Repaints every figure on the page from the system and the team tab.
        /// Public because the controller's own actions — filling stocks, loading
        /// a map — change what it shows.
        /// </summary>
        public void RefreshSustainment()
        {
            if (_sustainment == null || _manpowerFigure == null) return;

            int onField = _sustainment.ManpowerOnField(_team);
            int establishment = _sustainment.EstablishmentOnField(_team);
            int formations = _sustainment.FormationsOnField(_team);

            _manpowerFigure.text = $"{onField:n0} ON FIELD";
            _manpowerDetail.text = formations == 0
                ? "Nothing deployed for this side."
                : $"{formations} formation(s)  ·  {establishment:n0} at establishment  ·  " +
                  $"{(establishment > 0 ? onField * 100f / establishment : 0f):0}% strength";

            foreach (var (period, fill, label) in _burnTabs)
            {
                bool on = period == _burnPeriod;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }

            float days = BurnDays(_burnPeriod);
            string word = BurnWord(_burnPeriod);

            _sustainSyncing = true;
            foreach (var (kind, field, detail) in _resourceRows)
            {
                var def = ResourceCatalog.Get(kind);
                double stock = _sustainment.Stock(_team, kind);
                double burn = _sustainment.DailyUse(_team, kind) * days;
                double left = _sustainment.DaysOfSupply(_team, kind);

                field.text = stock.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                detail.text = $"{def.measure}  ·  {burn:n0} per {word}\n{DaysLeftText(left, burn)}";
            }
            _sustainSyncing = false;

            var (worst, worstDays) = _sustainment.BindingConstraint(_team);
            if (_sustainVerdict == null) return;

            if (worst == null || double.IsPositiveInfinity(worstDays))
            {
                _sustainVerdict.text = "Nothing deployed is consuming anything.";
                _sustainVerdict.color = UiTheme.TextFaint;
            }
            else
            {
                // The binding constraint is the whole point of the page: a force
                // is sustained for as long as its *shortest* stock lasts, and
                // nine figures without that sentence is nine figures.
                _sustainVerdict.text =
                    $"Sustained for {worstDays:0.#} day(s) — {ResourceCatalog.Get(worst.Value).name} runs out first.";
                _sustainVerdict.color = worstDays < 2.0 ? UiTheme.Danger
                    : worstDays < 7.0 ? UiTheme.Warning : UiTheme.Success;
            }
        }

        static string DaysLeftText(double days, double burn)
        {
            if (burn <= 0.0) return "not consumed by this force";
            if (double.IsPositiveInfinity(days)) return "not consumed by this force";
            if (days < 0.05) return "EXHAUSTED";
            return $"{days:0.#} day(s) of supply";
        }

        // ----------------------------------------------- reinforcements section

        ReinforcementSystem _reinforcements;
        RectTransform _reinforceList;
        Text _reinforceCount, _reinforceSide;
        InputField _reinforceSearch;
        string _reinforceQuery = "";
        readonly HashSet<UnitBranch> _reinforceOpenBranches = new HashSet<UnitBranch>();

        /// <summary>
        /// Left inset of an accordion header's contents in this list — INFANTRY,
        /// ARMOUR and the rest. The rows keep the list's full width so the count
        /// badge still sits against the scrollbar; it is the chevron and the
        /// heading that move in, which is what makes the arm's name read as a
        /// heading over its cards rather than as another card.
        /// </summary>
        const float ReinforceHeaderIndent = 25f;

        /// <summary>
        /// **REINFORCEMENTS** — the same panel as UNITS, for formations brought
        /// on after the map was laid out.
        ///
        /// Deliberately the same UI, control for control: the blue/red tabs, the
        /// search box, and the same branch accordion over the same 117 unit
        /// types. A commander calling a battalion forward is doing exactly what
        /// a designer does when they deploy one, and making them learn a second
        /// way to pick a unit would be inventing a difference that is not there.
        ///
        /// **Click and it is there.** This panel used to schedule: a stepper set
        /// an arrival time, a SCHEDULED tab held the queue, and the formation
        /// appeared at H+n. That is an authoring tool, and this is the rail's
        /// *battle* mode — a commander asking for a reserve wants it committed,
        /// not diarised. So a card places the formation immediately, in its
        /// side's deployment zone (docs/22-MISSIONS.md §1c), scattered off
        /// whatever arrived before it.
        ///
        /// The schedule itself has not gone: <see cref="ReinforcementSystem"/>
        /// still carries one loaded from the map file and still brings it on
        /// during a battle. What went is the panel for typing one in.
        ///
        /// See docs/30-REINFORCEMENTS.md.
        /// </summary>
        void BuildReinforcementSection(RectTransform content)
        {
            // Team tabs, exactly as UNITS has them.
            float half = (InnerWidth - 6f) / 2f;
            var blue = UIFactory.CreateBorderedPanel(content, "ReinBlue", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(blue, new Vector2(0f, 1f), new Vector2(Pad, -8), new Vector2(half, 30));
            var blueBtn = UIFactory.CreateButton(blue, "FRIENDLY", () => SetTeam(Team.User),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)blueBtn.transform);
            _reinforceBlueFill = blue.Find("Fill").GetComponent<Image>();

            var red = UIFactory.CreateBorderedPanel(content, "ReinRed", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(red, new Vector2(0f, 1f), new Vector2(Pad + half + 6f, -8), new Vector2(half, 30));
            var redBtn = UIFactory.CreateButton(red, "ENEMY", () => SetTeam(Team.Enemy),
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)redBtn.transform);
            _reinforceRedFill = red.Find("Fill").GetComponent<Image>();

            _reinforceSearch = UIFactory.CreateInputField(content, "Search unit types…", UiTheme.FontSmall);
            UIFactory.Place((RectTransform)_reinforceSearch.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -46), new Vector2(InnerWidth, 30));
            _reinforceSearch.onValueChanged.AddListener(text =>
            {
                _reinforceQuery = text ?? "";
                PopulateReinforcements();
            });

            var caption = UIFactory.CreateText(content, "BRING ON NOW", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -86), new Vector2(InnerWidth - 100f, 18));

            _reinforceCount = UIFactory.CreateText(content, "0", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_reinforceCount.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -86), new Vector2(40, 18));

            _reinforceSide = UIFactory.CreateText(content, "", UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_reinforceSide.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad - 44f, -86), new Vector2(110, 18));

            var scroll = UIFactory.CreateScrollView(content, out _reinforceList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 46);
            srt.offsetMax = new Vector2(-Pad, -110);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _reinforceList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(2, 2, 2, 2); }

            var hint = UIFactory.CreateText(content,
                "A card places that formation at once, in this side's deployment zone — set one under " +
                "ZONES, or it falls in behind the force it is joining.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 36));

            PopulateReinforcements();
        }

        Image _reinforceBlueFill, _reinforceRedFill;

        /// <summary>
        /// Rebuilds the list. Public because the force can change without the
        /// panel touching it — a scheduled arrival coming on during a battle
        /// changes what is deployed while this page is open.
        /// </summary>
        public void RefreshReinforcements() => PopulateReinforcements();

        void PopulateReinforcements()
        {
            if (_reinforceList == null) return;

            if (_reinforceSide != null)
            {
                bool enemy = _team == Team.Enemy;
                _reinforceSide.text = enemy ? "ENEMY" : "FRIENDLY";
                _reinforceSide.color = enemy ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }
            if (_reinforceBlueFill != null)
                _reinforceBlueFill.color = _team == Team.User ? UiTheme.Friendly : UiTheme.Surface;
            if (_reinforceRedFill != null)
                _reinforceRedFill.color = _team == Team.Enemy ? UiTheme.Hostile : UiTheme.Surface;

            ClearChildren(_reinforceList);

            int count = PopulateReinforceAvailable();
            if (_reinforceCount != null) _reinforceCount.text = count.ToString();
        }

        /// <summary>The catalogue, as the same branch accordion UNITS uses.</summary>
        int PopulateReinforceAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            bool searching = !string.IsNullOrEmpty(_reinforceQuery);
            int count = 0;

            foreach (var branch in UnitBranchInfo.All)
            {
                _branchMatches.Clear();
                foreach (var def in UnitDatabase.All)
                {
                    if (def.Branch != branch) continue;
                    if (!ReinforceMatches(def)) continue;
                    _branchMatches.Add(def);
                }
                if (_branchMatches.Count == 0) continue;

                count += _branchMatches.Count;

                bool open = searching || _reinforceOpenBranches.Contains(branch);
                ReinforceBranchHeader(branch, _branchMatches.Count, open);
                if (!open) continue;

                foreach (var def in _branchMatches) ReinforceCard(def, folder);
            }

            if (count == 0)
            {
                var empty = UIFactory.CreateText(_reinforceList,
                    "No unit type matches that search.", UiTheme.FontLabel,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 32);
            }
            return count;
        }

        bool ReinforceMatches(UnitDefinition def)
        {
            if (string.IsNullOrEmpty(_reinforceQuery)) return true;
            string q = _reinforceQuery.ToLowerInvariant();
            return (def.name != null && def.name.ToLowerInvariant().Contains(q)) ||
                   (def.id != null && def.id.ToLowerInvariant().Contains(q));
        }

        void ReinforceBranchHeader(UnitBranch branch, int count, bool open)
        {
            var row = UIFactory.CreateBorderedPanel(_reinforceList, "ReinBranch_" + branch,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 30);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() =>
            {
                if (!_reinforceOpenBranches.Remove(branch)) _reinforceOpenBranches.Add(branch);
                PopulateReinforcements();
            });

            var chevron = UIFactory.CreateText(row, open ? "▾" : "▸", UiTheme.FontSmall,
                open ? UiTheme.Accent : UiTheme.TextDim, TextAnchor.MiddleCenter);
            chevron.raycastTarget = false;
            UIFactory.Place(chevron.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(ReinforceHeaderIndent + 14f, 0f), new Vector2(16f, 16f));

            var text = UIFactory.CreateSectionHeader(row,
                UnitBranchInfo.DisplayName(branch).ToUpperInvariant(),
                open ? UiTheme.Accent : UiTheme.Text);
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(ReinforceHeaderIndent + 30f, 0f),
                new Vector2(InnerWidth - 90f - ReinforceHeaderIndent, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(40f, 16f));
        }

        /// <summary>
        /// One type, as a card. Clicking it puts that formation on the map
        /// immediately, in this side's deployment zone — a click rather than a
        /// drag, because there is no ground to drag it to: where it lands is the
        /// zone's business, not the cursor's.
        /// </summary>
        void ReinforceCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreateBorderedPanel(_reinforceList, "Rein_" + def.id,
                UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, 44);

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.onClick.AddListener(() =>
            {
                if (_reinforcements == null) return;
                _reinforcements.DeployNow(def, _team, DefaultEchelon);
            });

            var sprite = UIFactory.LoadIconSprite(folder, def.id);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                icon.raycastTarget = false;
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                    new Vector2(10, 0), new Vector2(34, 34));
            }

            UIFactory.CreateStackedLabels(card, def.name,
                $"{UnitBranchInfo.DisplayName(def.Branch)}   ·   {DefaultEchelon}",
                50f, InnerWidth - 110f, topInset: 5f);

            var at = UIFactory.CreateText(card, "DEPLOY",
                UiTheme.FontLabel, UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            at.raycastTarget = false;
            UIFactory.Place(at.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(52, 16));
        }

        // ------------------------------------------------------ groups section

        /// <summary>Select every formation in a group.</summary>
        public System.Action<string> GroupSelectRequested;
        /// <summary>Select them and fly the camera so the whole group is framed.</summary>
        public System.Action<string> GroupFlyRequested;
        /// <summary>Put a group on the front line — see <c>GameController.ManTheFlot</c>.</summary>
        public System.Action<string> GroupFlotRequested;
        /// <summary>Release whichever group is holding the front line.</summary>
        public System.Action GroupFlotClearRequested;

        RectTransform _groupsList;
        Text _modeHeading;
        Text _groupsFlotState;
        string _flotHolder = "";

        /// <summary>
        /// Insets of a row in GROUPS ON THIS MAP, applied as the list's own
        /// layout padding so the rows are genuinely narrower. Asymmetric because
        /// the list carries a scrollbar down its right-hand side and the rows
        /// should clear it by the same *visible* margin they keep on the left.
        /// </summary>
        const float GroupsListPadLeft = 35f, GroupsListPadRight = 30f;

        /// <summary>
        /// Width a GROUPS row actually gets. Everything placed against a row's
        /// right-hand end measures from this — the FLOT and ◎ buttons are
        /// right-anchored and the caption is not, so a row that shrank while the
        /// caption did not would run the two into each other.
        /// </summary>
        const float GroupsRowWidth =
            InnerWidth - UIFactory.ScrollbarWidth - GroupsListPadLeft - GroupsListPadRight;

        /// <summary>
        /// The order of battle as the player has grouped it, and the one thing
        /// you can do to a group that is not an order: put it on the front line.
        ///
        /// **Why this is not the group panel.** The panel on the right describes
        /// *the current selection* — it appears when two things are selected and
        /// goes when they are not. This is the opposite question: what groups
        /// exist on this map, and where are they? A commander asks that without
        /// having selected anything, which is exactly when the right-hand panel
        /// is not there.
        ///
        /// See docs/03-GAMEPLAY.md § Groups.
        /// </summary>
        void BuildGroupsSection(RectTransform content)
        {
            SectionLabel(content, "FRONT LINE", -8);

            var flotFrame = UIFactory.CreateBorderedPanel(content, "FlotHolder", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(flotFrame, new Vector2(0f, 1f), new Vector2(Pad, -28), new Vector2(InnerWidth, 40));

            _groupsFlotState = UIFactory.CreateText(flotFrame, "", UiTheme.FontSmall, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_groupsFlotState.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(InnerWidth - 82f, 30));
            UIFactory.Fit(_groupsFlotState, 8);

            var release = UIFactory.CreateButton(flotFrame, "RELEASE",
                () => GroupFlotClearRequested?.Invoke(), UiTheme.SurfaceHover, UiTheme.TextDim, 10);
            UIFactory.Place((RectTransform)release.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(62, 24));

            SectionLabel(content, "GROUPS ON THIS MAP", -80);

            var scroll = UIFactory.CreateScrollView(content, out _groupsList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 76);
            srt.offsetMax = new Vector2(-Pad, -100);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _groupsList.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 4;
                layout.padding = new RectOffset((int)GroupsListPadLeft, (int)GroupsListPadRight, 2, 2);
            }

            var hint = UIFactory.CreateText(content,
                "Click a group to select it, ◎ to fly to it. FLOT sends the whole group to the front " +
                "line: its formations are spread evenly along the line and each digs in on its own " +
                "stretch, facing the enemy. Groups are named on the right-hand panel with two or more " +
                "formations selected.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 66));

            RefreshGroups();
        }

        // ------------------------------------------------------ stats section

        RectTransform _statsList;
        Text _statsHeadline;

        /// <summary>
        /// **STATS** — what the battle has cost, both sides, in the rail.
        ///
        /// The same ledger the TAB page reads (<see cref="LossesDialog"/>, and
        /// <see cref="LossLedger"/> under it), in the shape the rail can hold. It
        /// is not a second accounting: both read <c>LossLedger</c> and neither
        /// keeps a figure of its own, so the two cannot drift.
        ///
        /// **Why both, when TAB already exists.** TAB is a page you stop and
        /// read — two columns side by side, the whole comparison at once. This is
        /// a column you keep open while you fight, in the rail your hand is
        /// already on, at the cost of reading the two sides one under the other.
        /// Different postures, same numbers.
        ///
        /// **FORM** is formations destroyed outright; **MEN** is the manpower
        /// behind every point of strength lost, in surviving formations as well
        /// as dead ones — see <see cref="LossLedger"/> for how they are booked.
        /// </summary>
        void BuildStatsSection(RectTransform content)
        {
            SectionLabel(content, "BATTLE LOSSES", -8);

            _statsHeadline = UIFactory.CreateText(content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(_statsHeadline.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -28),
                new Vector2(InnerWidth, 30));

            var scroll = UIFactory.CreateScrollView(content, out _statsList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 46);
            srt.offsetMax = new Vector2(-Pad, -62);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _statsList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 2; layout.padding = new RectOffset(2, 2, 2, 2); }

            var hint = UIFactory.CreateText(content,
                "FORM is formations destroyed outright. MEN is the manpower behind every point of " +
                "strength lost. TAB shows the same figures as a full page.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 36));

            // A tick of combat can add a row, so the page follows the ledger
            // rather than a timer — a total that disagreed with the rows above
            // it would be worse than no total.
            LossLedger.Changed += RefreshStats;
            RefreshStats();
        }

        /// <summary>
        /// Rebuilds the loss tables. Cheap when shut, for the same reason
        /// <see cref="RefreshGroups"/> is: the ledger books a row on every
        /// exchange, and churning uGUI objects for a page nobody is looking at
        /// would cost a few dozen of them per combat tick.
        /// </summary>
        public void RefreshStats()
        {
            if (_statsList == null) return;
            if (!_sectionContent.TryGetValue(Section.Stats, out var page) ||
                !page.gameObject.activeSelf) return;

            var (blueForm, bluePeople) = LossLedger.Total(Team.User);
            var (redForm, redPeople) = LossLedger.Total(Team.Enemy);
            if (_statsHeadline != null)
                _statsHeadline.text =
                    $"FRIENDLY  {blueForm} formation(s)  ·  {Mathf.RoundToInt(bluePeople):n0} men\n" +
                    $"ENEMY  {redForm} formation(s)  ·  {Mathf.RoundToInt(redPeople):n0} men";

            ClearChildren(_statsList);
            StatsSide(Team.User, "FRIENDLY LOSSES", UiTheme.Friendly);
            StatsSide(Team.Enemy, "HOSTILE LOSSES", UiTheme.Hostile);
        }

        /// <summary>One side's block: heading, standing/lost summary, then its table.</summary>
        void StatsSide(Team team, string title, Color accent)
        {
            var head = UIFactory.CreateText(_statsList, title, UiTheme.FontSmall, accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)head.transform).sizeDelta = new Vector2(0, 20);

            var (formations, personnel) = LossLedger.Total(team);
            var summary = UIFactory.CreateText(_statsList,
                $"{formations:n0} destroyed  ·  {LossLedger.Surviving(team):n0} still on the map  ·  " +
                $"{Mathf.RoundToInt(personnel):n0} men",
                UiTheme.FontLabel, formations > 0 ? UiTheme.Text : UiTheme.TextDim, TextAnchor.UpperLeft);
            ((RectTransform)summary.transform).sizeDelta = new Vector2(0, 26);

            var rows = LossLedger.For(team);
            if (rows.Count == 0)
            {
                var none = UIFactory.CreateText(_statsList, "No losses recorded.", UiTheme.FontLabel,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)none.transform).sizeDelta = new Vector2(0, 24);
                return;
            }

            foreach (var data in rows) StatsRow(data);
        }

        /// <summary>
        /// One formation type's line. Three columns, the two numeric ones
        /// measured in from the right so they stay in line down the table and
        /// the name takes whatever is left — the same rule the TAB page uses.
        /// </summary>
        void StatsRow(LossLedger.Row data)
        {
            const float FormWidth = 42f, MenWidth = 56f;
            float width = InnerWidth - UIFactory.ScrollbarWidth - 4f;

            var row = UIFactory.CreatePanel(_statsList, "Loss_" + data.defId, UiTheme.SurfaceSubtle);
            row.sizeDelta = new Vector2(0, 22);

            var name = UIFactory.CreateText(row, data.name, UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(6, 0),
                new Vector2(width - FormWidth - MenWidth - 14f, 16));
            UIFactory.Fit(name, 8);

            var form = UIFactory.CreateText(row, data.formations.ToString(), UiTheme.FontLabel,
                data.formations > 0 ? UiTheme.Warning : UiTheme.TextFaint,
                TextAnchor.MiddleRight, FontStyle.Bold);
            form.raycastTarget = false;
            UIFactory.Place(form.rectTransform, new Vector2(1f, 0.5f), new Vector2(-(MenWidth + 4f), 0),
                new Vector2(FormWidth, 16));

            var men = UIFactory.CreateText(row, $"{Mathf.RoundToInt(data.personnel):n0}",
                UiTheme.FontLabel, UiTheme.TextDim, TextAnchor.MiddleRight);
            men.raycastTarget = false;
            UIFactory.Place(men.rectTransform, new Vector2(1f, 0.5f), new Vector2(-4, 0),
                new Vector2(MenWidth, 16));
        }

        /// <summary>
        /// ZONES — the ground a mission *names*: where each side's headquarters
        /// is, and where its reinforcements arrive.
        ///
        /// They were the bottom half of the MISSIONS page, seven hundred pixels
        /// below its first field, under a form of text boxes. They are not
        /// fields of a record: they are places on the map, put there by clicking
        /// it, and they belong with the other things you draw rather than with
        /// the mission's name and briefing. MISSIONS keeps the record and the
        /// mission's own boundary, which is what SAVE MISSION + MAP writes.
        ///
        /// Both blocks still edit the **mission**, so they read as unavailable
        /// until one is open — the same fields the MISSIONS panel refreshes.
        /// </summary>
        void BuildZonesSection(RectTransform section)
        {
            var content = ScrollableSection(section, ZonesPageHeight);

            BuildHqZoneBlock(content);
            BuildDeploymentBlock(content);

            var hint = UIFactory.CreateText(content,
                "Both belong to the mission rather than to the map, so they need one open: pick it under " +
                "MISSIONS first. SET drops the zone where the camera is looking; the size buttons apply to " +
                "both sides at once, because two headquarters in one scenario are at the same echelon.\n\n" +
                "A mission's own boundary — the ground the battle is fought inside — is still under " +
                "MISSIONS, with the record it is part of. See docs/22-MISSIONS.md.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -HqBlockBottom - 12f), new Vector2(InnerWidth, 150));
        }

        const float ZonesPageHeight = HqBlockBottom + 180f;
    }
}
