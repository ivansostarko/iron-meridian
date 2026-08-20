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

        /// <summary>
        /// **REINFORCEMENTS** — the arrivals this scenario laid on, for the side
        /// the tabs are set to.
        ///
        /// **It shows the schedule, not a catalogue.** This page used to be the
        /// whole 117-type list with a DEPLOY on every card, which made a battle
        /// a shop: anything either side owned could be conjured into the
        /// deployment zone at any moment, and the schedule the designer had
        /// written was a separate thing nobody could see. Showing only what the
        /// scenario laid on is what makes a reinforcement a plan rather than a
        /// resource.
        ///
        /// **NOW is a decision inside that plan.** A commander who can see a
        /// reserve due at H+40 and wants it at H+12 is making a choice the
        /// scenario left them; spending an arrival early is not the same as
        /// inventing one, and the row goes to ARRIVED either way.
        ///
        /// Arrivals are written in **scenario mode** — UNITS, right-click a type,
        /// ADD TO REINFORCEMENT — and saved with the map. See
        /// docs/30-REINFORCEMENTS.md.
        /// </summary>
        void BuildReinforcementSection(RectTransform content)
        {
            SideSelector(content, -8f);

            var caption = UIFactory.CreateText(content, "", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -44), new Vector2(InnerWidth - 60f, 18));
            _reinforceSide = caption;

            _reinforceCount = UIFactory.CreateText(content, "0", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(_reinforceCount.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -44), new Vector2(52, 18));

            var scroll = UIFactory.CreateScrollView(content, out _reinforceList, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(Pad, 52);
            srt.offsetMax = new Vector2(-Pad, -68);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var layout = _reinforceList.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(2, 2, 2, 2); }

            var hint = UIFactory.CreateText(content,
                "Arrivals are laid on in scenario mode — UNITS, right-click a type, ADD TO "
                + "REINFORCEMENT. They land in this side’s deployment zone; set one under ZONES, or "
                + "they fall in behind the force they are joining.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 6),
                new Vector2(InnerWidth, 44));

            PopulateReinforcements();
        }

        /// <summary>
        /// Rebuilds the list. Public because the schedule can change without the
        /// panel touching it — an arrival coming on during a battle takes its
        /// own row out of the pending list.
        /// </summary>
        public void RefreshReinforcements()
        {
            PopulateReinforcements();
            // The UNITS page shows the same schedule under REINFORCEMENT, and its tab
            // carries the count whichever list is open.
            if (_listMode == ListMode.Reinforcement) Populate();
            else RefreshListTabCounts();
        }

        void PopulateReinforcements()
        {
            if (_reinforceList == null) return;

            bool enemy = _team == Team.Enemy;
            int rows = _reinforcements != null ? _reinforcements.CountFor(_team) : 0;
            int formations = _reinforcements != null ? _reinforcements.FormationsFor(_team) : 0;

            if (_reinforceSide != null)
            {
                _reinforceSide.text = enemy ? "ENEMY ARRIVALS" : "FRIENDLY ARRIVALS";
                _reinforceSide.color = enemy ? GameConfig.RedTeam : GameConfig.BlueTeam;
            }
            if (_reinforceCount != null)
                _reinforceCount.text = rows == 0 ? "—" : $"{formations}";

            ClearChildren(_reinforceList);

            if (_reinforcements == null || rows == 0)
            {
                var empty = UIFactory.CreateText(_reinforceList,
                    "No arrivals are scheduled for this side.", UiTheme.FontLabel,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 40);
                return;
            }

            string folder = _team == Team.User ? "Friendly" : "Enemy";
            double elapsed = _reinforcements.ElapsedMinutes;
            foreach (var entry in _reinforcements.For(_team))
                ReinforcementRow(entry, folder, elapsed);
        }

        /// <summary>Height of one scheduled-arrival row in the battle panel.</summary>
        const float ReinforceRowHeight = 54f;
        /// <summary>Width such a row actually gets, once the scrollbar and list padding are off.</summary>
        const float ReinforceRowWidth = InnerWidth - UIFactory.ScrollbarWidth - 4f;

        /// <summary>
        /// One scheduled arrival, as the commander sees it: what is coming, when
        /// it is due, and — while it is still pending — a way to have it now.
        ///
        /// **The list is the scenario’s, not a catalogue.** This panel used to
        /// be the whole 117-type catalogue with a DEPLOY on every card, which
        /// made a battle a shop: anything either side owned could be conjured
        /// into the deployment zone at any moment, and the schedule the designer
        /// wrote was a separate thing nobody could see. Showing only what the
        /// scenario laid on is what makes a reinforcement a plan rather than a
        /// resource — and NOW is a decision *within* that plan, spending an
        /// arrival early rather than inventing one.
        ///
        /// A row that has already come on stays, greyed, with the minute it
        /// arrived. It is the record of what the fight has been given so far,
        /// which is exactly what a commander counting their reserves is asking.
        /// </summary>
        void ReinforcementRow(ReinforcementEntry entry, string folder, double elapsedMinutes)
        {
            var def = UnitDatabase.Get(entry.defId);
            string name = def != null ? def.name : entry.defId;
            bool arrived = entry.arrived;

            var card = UIFactory.CreateBorderedPanel(_reinforceList, "Arrival_" + entry.defId,
                arrived ? UiTheme.Panel : UiTheme.Surface,
                arrived ? UiTheme.Border : UiTheme.BorderStrong);
            card.sizeDelta = new Vector2(0, ReinforceRowHeight);

            if (def != null)
            {
                var sprite = UIFactory.LoadIconSprite(folder, def.id);
                if (sprite != null)
                {
                    var icon = UIFactory.CreateImage(card, sprite, "Icon");
                    icon.raycastTarget = false;
                    icon.color = arrived ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
                    UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                        new Vector2(8, 0), new Vector2(30, 30));
                }
            }

            int many = Mathf.Max(1, entry.count);
            UIFactory.CreateStackedLabels(card,
                many > 1 ? $"{many} × {name}" : name,
                $"{entry.echelon}   ·   H+{entry.arrivalMinutes}",
                // Stops clear of the state word, which shares the top line.
                44f, ReinforceRowWidth - 136f, topInset: 8f);

            // The state, in the corner the eye goes to last: what this row is
            // waiting for, or that it is no longer waiting.
            // ElapsedMinutes is 0 outside a battle, so "pending" and "due in
            // its full time" are the same reading there — which is the honest
            // one: before H-hour every arrival is still to come.
            double due = entry.arrivalMinutes - elapsedMinutes;
            string state = arrived ? "ARRIVED"
                         : elapsedMinutes <= 0.0 ? $"H+{entry.arrivalMinutes}"
                         : due <= 0 ? "DUE"
                         : $"IN {Mathf.CeilToInt((float)due)} MIN";

            var status = UIFactory.CreateText(card, state, UiTheme.FontLabel,
                arrived ? UiTheme.TextFaint : due <= 5 ? UiTheme.Warning : UiTheme.TextDim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            status.raycastTarget = false;
            UIFactory.Place(status.rectTransform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(74, 14));

            if (arrived) return;

            var now = UIFactory.CreateButton(card, "NOW", () =>
            {
                if (_reinforcements.BringForward(entry))
                    DropRejected?.Invoke($"{name} called forward.");
            }, UiTheme.AccentWash, UiTheme.Accent, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)now.transform, new Vector2(1f, 0f),
                new Vector2(-8, 8), new Vector2(64, 22));
            UiTooltip.Attach(now.gameObject,
                "Bring this arrival on at once instead of waiting for its minute.",
                UiTooltip.Side.Left);
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
