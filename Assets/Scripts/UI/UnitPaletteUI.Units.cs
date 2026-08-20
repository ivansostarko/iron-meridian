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
    /// <see cref="UnitPaletteUI"/> — the UNITS section: side and echelon selectors, search, the available/deployed list, and the tool strip.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: units section, side selector ---, search ---, list header: mode tabs + live count ---, the list itself ---, list, tool strip.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // ------------------------------------------------------- units section

        void BuildUnitsSection(RectTransform content)
        {
            // --- side selector ---
            // The shared one, the same control LOGISTICS, SUSTAINMENT and MINES
            // AND OBSTACLES carry. This page used to build a pair of its own
            // from bare buttons: no border, a different height, and painted by
            // its own two lines in SetTeam rather than by PaintSideTabs with
            // everything else. Two implementations of one control is how they
            // drift, and this was the one that had.
            SideSelector(content, -8f);

            // Neither an affiliation picker nor an echelon dropdown any more.
            //
            // Affiliation offered four values of which only two were ever right —
            // the side tabs above already say whose the unit is, and SetTeam
            // derives Friendly/Hostile from that — so it was a control whose only
            // real use was to contradict the tab beside it.
            //
            // Echelon went the same way: a dropdown listing every size from
            // section to army, sitting above a list of 37 unit types, made
            // deploying one unit a two-control operation and put the rarely-wanted
            // choice in front of the always-wanted one. Units now deploy at
            // <see cref="DefaultEchelon"/> and are re-sized after the fact from the
            // info panel, which is where the rest of a formation's details are
            // edited anyway. The ~90 px both controls took goes to the list.

            // --- search ---
            var searchFrame = UIFactory.CreateBorderedPanel(content, "SearchFrame", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(searchFrame, new Vector2(0f, 1f), new Vector2(Pad, -50), new Vector2(InnerWidth, 34));

            var glass = UIFactory.CreateImage(searchFrame, UiIcons.Search, "SearchGlyph");
            glass.color = UiTheme.TextFaint;
            glass.raycastTarget = false;
            UIFactory.Place((RectTransform)glass.transform, new Vector2(0f, 0.5f), new Vector2(9, 0), new Vector2(14, 14));

            var input = UIFactory.CreateInputField(searchFrame, "Search unit or type...", UiTheme.FontSmall);
            var irt = (RectTransform)input.transform;
            UIFactory.Stretch(irt);
            irt.offsetMin = new Vector2(28, 0);
            input.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            input.onValueChanged.AddListener(v => { _search = v == null ? "" : v.Trim(); Populate(); });

            // --- list header: mode tabs + live count ---
            // A segmented control across the full width, not two 82 px buttons
            // floating at the left of a 306 px panel with a count badge stranded
            // at the other end. The two are one choice, they are the same width
            // because neither is the lesser, and the count sits inside the tab
            // it counts — a badge in the corner was a number with nothing saying
            // what it was of.
            float tabWidth = (InnerWidth - 8f) / 3f;
            ListModeButton(content, "AVAILABLE", UiIcons.Folder, Pad, tabWidth,
                "AVAILABLE — every formation type this side can field. Drag one onto the map to "
                + "deploy it, or right-click it for the same thing without the drag.",
                () => SetListMode(ListMode.Available), out _availableUnderline, out _availableCount);
            ListModeButton(content, "DEPLOYED", UiIcons.Pin, Pad + tabWidth + 4f, tabWidth,
                "DEPLOYED — what is already on the map, both sides, with the ones you can still "
                + "move listed under your own.",
                () => SetListMode(ListMode.Deployed), out _deployedUnderline, out _deployedCount);
            // The schedule, written from the catalogue next door — see
            // OpenCardMenu. Third because it is the last of the three questions
            // in order: what is there, what have I put down, what is still to
            // come.
            //
            // **REINFORCEMENT, not ARRIVING.** The old caption named the moment
            // rather than the thing: everything on a scenario board arrives at
            // some point, and the word that tells a player what this list *is*
            // is the one the rest of the game already uses for it — the panel it
            // is written from says ADD TO REINFORCEMENT, and the doc is
            // docs/30-REINFORCEMENTS.md.
            ListModeButton(content, "REINFORCEMENT", UiIcons.Clock, Pad + (tabWidth + 4f) * 2f, tabWidth,
                "REINFORCEMENT — formations due to arrive after H-hour, earliest first. Right-click "
                + "a type under AVAILABLE and choose ADD TO REINFORCEMENT to put one on the schedule.",
                () => SetListMode(ListMode.Reinforcement), out _reinforceUnderline, out _reinforceTabCount);

            // The strip the two tabs sit on. Each carries its own accent
            // underline when open; this dim rule runs the full width under both,
            // so the closed half reads as the other end of one control rather
            // than as a caption floating beside a lit one.
            var tabRule = UIFactory.CreateDivider(content, UiTheme.Border);
            tabRule.anchorMin = new Vector2(0, 1); tabRule.anchorMax = new Vector2(0, 1);
            tabRule.pivot = new Vector2(0, 1);
            tabRule.anchoredPosition = new Vector2(Pad, ListTop - ListTabHeight);
            tabRule.sizeDelta = new Vector2(InnerWidth, 1);
            tabRule.GetComponent<Image>().raycastTarget = false;

            // --- the list itself ---
            // With a scrollbar: the AVAILABLE cards carry drag-to-deploy
            // handlers, which swallow the drag before the ScrollRect sees it, so
            // the list cannot be dragged to scroll and the wheel was the only
            // way to reach the units past the fold.
            var scroll = UIFactory.CreateScrollView(content, out _listContent, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 2);
            srt.offsetMax = new Vector2(0, ListTop - ListTabHeight);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset((int)Pad, (int)Pad, 4, 8);

            SetListMode(ListMode.Available);
        }

        void StyleDropdown(Dropdown dd, float y)
        {
            var rt = (RectTransform)dd.transform;
            UIFactory.Place(rt, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 34));
            dd.GetComponent<Image>().color = UiTheme.Surface;
            if (dd.captionText != null)
            {
                dd.captionText.fontSize = UiTheme.FontSmall;
                dd.captionText.color = UiTheme.Text;
            }
        }

        /// <summary>
        /// Height of one tab in the segment.
        ///
        /// Two rows rather than one, which is what pays for both the icon and
        /// the rename. At a third of a 250 px panel a tab is about 80 px wide,
        /// and a glyph, a caption and a count could not share one line of that —
        /// "REINFORCEMENT" alone needs most of it. Splitting the tab into a mark
        /// line (glyph and count) over a caption line gives the caption the full
        /// width and the glyph a place that is not competing with it.
        /// </summary>
        const float ListTabHeight = 36f;

        /// <summary>
        /// One third of the AVAILABLE / DEPLOYED / REINFORCEMENT segment: a
        /// glyph, the count of what is in it, its name, and an underline that
        /// marks the open one.
        ///
        /// The count is per tab rather than shared. "37" beside a row of tabs
        /// answers none of "how many types are there", "how many have I put
        /// down" and "how many are still to come" without first checking which
        /// tab is lit; one number in each answers all three at once, which is
        /// most of what the segment is asked.
        ///
        /// **The glyph is the thing you find it by.** Three words in the same
        /// weight at the same size are three words to read; a folder, a pin and
        /// a clock are told apart without reading, and after the first session
        /// that is how the tab is actually found. They are not a replacement for
        /// the words — an icon nobody has seen before means nothing — which is
        /// why both are on the tab and the full sentence is on the tooltip.
        /// </summary>
        void ListModeButton(RectTransform content, string label, Sprite glyph, float x, float w,
            string tooltip, UnityEngine.Events.UnityAction action,
            out RectTransform underline, out Text count)
        {
            var frame = UIFactory.CreatePanel(content, "ListTab_" + label, new Color(0, 0, 0, 0));
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(x, ListTop),
                new Vector2(w, ListTabHeight));

            var b = UIFactory.CreateButton(frame, "", action, new Color(0, 0, 0, 0), UiTheme.TextDim, 1);
            UIFactory.Stretch((RectTransform)b.transform);
            // The factory centres a caption in every button; this one draws its
            // own two rows instead.
            var made = b.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            // Below, not beside: the panel is on the left edge of the screen, so
            // a caption to the left would be clamped back over the tab it is
            // describing.
            UiTooltip.Attach(frame.gameObject, tooltip, UiTooltip.Side.Right);

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.raycastTarget = false;
            UIFactory.PlaceTopLeft((RectTransform)icon.transform, 7f, 4f, 13f, 13f);

            count = UIFactory.CreateText(frame, "0", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleRight, FontStyle.Bold);
            count.raycastTarget = false;
            UIFactory.PlaceTopLeft(count.rectTransform, w - 36f, 3f, 30f, 15f);

            // The name across the full width of the tab. "REINFORCEMENT" is
            // thirteen characters in eighty pixels, which is exactly why it has
            // a line to itself and why Fit is allowed down to 7 px here — the
            // word is being recognised, not read letter by letter, and the
            // tooltip carries it in full either way.
            var caption = UIFactory.CreateText(frame, label, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            caption.raycastTarget = false;
            UIFactory.PlaceTopLeft(caption.rectTransform, 7f, 19f, w - 12f, 13f);
            UIFactory.Fit(caption, 7);

            underline = UIFactory.CreatePanel(frame, "Underline", UiTheme.Accent);
            underline.anchorMin = new Vector2(0, 0); underline.anchorMax = new Vector2(1, 0);
            underline.pivot = new Vector2(0.5f, 0);
            underline.sizeDelta = new Vector2(0, 2);
            underline.anchoredPosition = Vector2.zero;
            underline.GetComponent<Image>().raycastTarget = false;

            _listTabCaptions[label] = caption;
            _listTabGlyphs[label] = icon;
        }

        readonly Dictionary<string, Text> _listTabCaptions = new Dictionary<string, Text>();
        readonly Dictionary<string, Image> _listTabGlyphs = new Dictionary<string, Image>();

        void SetListMode(ListMode mode)
        {
            _listMode = mode;
            PaintListTabs();
            Populate();
        }

        /// <summary>Lights whichever third of the segment is open.</summary>
        void PaintListTabs()
        {
            Paint("AVAILABLE", _availableUnderline, _availableCount, _listMode == ListMode.Available);
            Paint("DEPLOYED", _deployedUnderline, _deployedCount, _listMode == ListMode.Deployed);
            Paint("REINFORCEMENT", _reinforceUnderline, _reinforceTabCount,
                _listMode == ListMode.Reinforcement);

            void Paint(string label, RectTransform underline, Text count, bool on)
            {
                if (underline != null) underline.gameObject.SetActive(on);
                if (count != null) count.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                if (_listTabCaptions.TryGetValue(label, out var caption) && caption != null)
                    caption.color = on ? UiTheme.Accent : UiTheme.TextFaint;
                if (_listTabGlyphs.TryGetValue(label, out var glyph) && glyph != null)
                    glyph.color = on ? UiTheme.Accent : UiTheme.TextFaint;
            }
        }

        /// <summary>
        /// FRIENDLY / ENEMY at the head of a panel that puts something on the
        /// map for one side.
        ///
        /// **Why the panels grew their own.** The side was the UNITS tab's, and
        /// LOGISTICS, SUSTAINMENT and MINES AND OBSTACLES only *reported* it —
        /// "FOR ENEMY" in the corner, decided on a different page. Laying an
        /// enemy minefield therefore meant opening UNITS, switching side, coming
        /// back, and remembering to switch it again afterwards. The control now
        /// sits where the work is.
        ///
        /// It is the **same** `_team`, not a second one: one side is selected in
        /// the editor at a time, every one of these panels reads it, and the
        /// tabs everywhere repaint together. Two side pickers that could
        /// disagree would be a bug with a UI.
        /// </summary>
        void SideSelector(RectTransform content, float y)
        {
            float half = (InnerWidth - 4f) / 2f;
            Tab(Team.User, "FRIENDLY", 0);
            Tab(Team.Enemy, "ENEMY", 1);

            void Tab(Team team, string label, int index)
            {
                var frame = UIFactory.CreateBorderedPanel(content, "Side_" + label,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f),
                    new Vector2(Pad + index * (half + 4f), y), new Vector2(half, 28));

                var captured = team;
                var btn = UIFactory.CreateButton(frame, label, () => SetTeam(captured),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
                UIFactory.Stretch((RectTransform)btn.transform);

                var text = btn.GetComponentInChildren<Text>(true);
                UIFactory.Fit(text, 9);

                _sideTabs.Add((team, frame.Find("Fill").GetComponent<Image>(), text));
            }

            PaintSideTabs();
        }

        /// <summary>Every side tab on every panel, repainted together — see <see cref="SideSelector"/>.</summary>
        readonly List<(Team team, Image fill, Text label)> _sideTabs =
            new List<(Team, Image, Text)>();

        /// <summary>Height a <see cref="SideSelector"/> takes, so a page can leave room for it.</summary>
        const float SideBlock = 34f;

        void PaintSideTabs()
        {
            foreach (var (team, fill, label) in _sideTabs)
            {
                if (fill == null || label == null) continue;
                bool on = team == _team;
                fill.color = on ? (team == Team.User ? UiTheme.Friendly : UiTheme.Hostile)
                                : UiTheme.Surface;
                label.color = on ? Color.white : UiTheme.TextDim;
            }
        }

        /// <summary>
        /// A page that lays something down for one side: the selector, then the
        /// page's own content shifted below it.
        ///
        /// The content is re-parented into a group rather than every offset in
        /// the builder being moved down — the builders place by absolute offsets
        /// from the top, and shifting a page by hand is a page of arithmetic
        /// that has to be redone every time a row is added to it.
        /// </summary>
        RectTransform SidedPage(RectTransform content)
        {
            SideSelector(content, -8f);

            var body = UIFactory.CreateGroup(content, "Body");
            body.anchorMin = new Vector2(0, 0); body.anchorMax = new Vector2(1, 1);
            body.offsetMin = Vector2.zero;
            body.offsetMax = new Vector2(0, -SideBlock);
            return body;
        }

        void SetTeam(Team team)
        {
            _team = team;
            _affiliation = team == Team.User ? Affiliation.Friendly : Affiliation.Hostile;
            // The rear area follows the team tab rather than carrying a second
            // side control of its own — see BuildLogisticsSection.
            if (_logistics != null) _logistics.Team = team;
            // An airdrop lands supplies for the same side the panel is working
            // for — see BuildAirSupplySection.
            if (_airSupply != null) _airSupply.Team = team;
            if (_obstacles != null) _obstacles.Team = team;
            if (_mapObjects != null) _mapObjects.Team = team;
            PaintSideTabs();
            RefreshLogistics();
            RefreshSustainment();
            RefreshObstacles();
            PopulateReinforcements();
            Populate();
        }

        void OnUnitsChanged()
        {
            if (_listMode == ListMode.Deployed) Populate();
            // A group is a property of the units in it, so the group list has
            // nothing else to hear about a formation joining, leaving or dying.
            RefreshGroups();
            // Every burn rate on the sustainment page is read off the deployed
            // force, so the force changing is the only thing that moves them.
            if (_sectionContent.TryGetValue(Section.Sustainment, out var page) &&
                page.gameObject.activeSelf) RefreshSustainment();
            // A formation joining or leaving is a row appearing or disappearing
            // from the supply table. What it *carries* moves without the registry
            // hearing about it, which is what the tick in Update is for.
            RefreshSupplies();
        }

        // ------------------------------------------------------------ list

        void Populate()
        {
            if (_listContent == null) return;

            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                var c = _listContent.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            switch (_listMode)
            {
                case ListMode.Deployed: PopulateDeployed(); break;
                case ListMode.Reinforcement: PopulateArriving(); break;
                default: PopulateAvailable(); break;
            }

            // Every tab carries its own figure, whichever is open. They are all
            // cheap — a count of a loaded catalogue, a walk of the registry, a
            // walk of a schedule a dozen rows long — and a number that only
            // updated while you were looking at it would be a number nobody
            // could trust from the tab beside it.
            RefreshListTabCounts();
        }

        void RefreshListTabCounts()
        {
            if (_availableCount != null)
            {
                int types = 0;
                foreach (var def in UnitDatabase.All)
                    if (Matches(def.name, def.id, def.ammoType)) types++;
                _availableCount.text = types.ToString();
            }

            if (_deployedCount != null)
            {
                int deployed = 0;
                foreach (var u in UnitRegistry.All) if (u != null && u.IsAlive) deployed++;
                _deployedCount.text = deployed.ToString();
            }

            if (_reinforceTabCount != null)
                _reinforceTabCount.text = _reinforcements != null
                    ? _reinforcements.FormationsFor(_team).ToString() : "0";
        }

        /// <summary>
        /// The draggable catalogue, as an **accordion** of one section per
        /// <see cref="UnitBranch"/>: infantry, armour, artillery and the rest,
        /// each opening to the unit types inside it.
        ///
        /// Flat, this list is 117 cards deep and finding the mortar in it means
        /// scrolling past the ships. Even with headings it was a single column
        /// of cards several screens long. Collapsed sections turn it into a
        /// menu: the nine arms fit on one screen, and the one you open is the
        /// only one taking space. Walking the branches in declaration order
        /// puts manoeuvre first and the tail last, which is the order an order
        /// of battle is written in — and an empty branch prints no heading at
        /// all, so a search never leaves a bare label behind.
        /// </summary>
        int PopulateAvailable()
        {
            string folder = _team == Team.User ? "Friendly" : "Enemy";
            // A search is already a statement of what you want; making the hits
            // wait behind a click would be the accordion fighting the query.
            bool searching = !string.IsNullOrEmpty(_search);
            int count = 0;

            foreach (var branch in UnitBranchInfo.All)
            {
                // Walked into a list first because the heading carries the
                // count, and the count is only known once the branch is walked.
                _branchMatches.Clear();
                foreach (var def in UnitDatabase.All)
                {
                    if (def.Branch != branch) continue;
                    if (!Matches(def.name, def.id, def.ammoType)) continue;
                    _branchMatches.Add(def);
                }
                if (_branchMatches.Count == 0) continue;

                count += _branchMatches.Count;

                bool open = searching || _openBranches.Contains(branch);
                BranchHeader(branch, _branchMatches.Count, open);
                if (!open) continue;

                foreach (var def in _branchMatches) CreateAvailableCard(def, folder);
            }

            if (count == 0) EmptyRow("No unit type matches that search.");
            return count;
        }

        /// <summary>Opens or closes one arm's section, and redraws the list around it.</summary>
        void ToggleBranch(UnitBranch branch)
        {
            if (!_openBranches.Remove(branch)) _openBranches.Add(branch);
            Populate();
        }

        /// <summary>
        /// One accordion header: a chevron saying which way it will go, the arm's
        /// name, and how many types are inside it.
        ///
        /// The count is on the header because it is the answer to the question a
        /// closed section raises: is there anything in there worth opening, and
        /// how much of the list am I about to unfold?
        /// </summary>
        /// <summary>
        /// Where an arm of service is printed on its header row, from the left
        /// of the row.
        ///
        /// 30 px clear of the chevron rather than butting up against it. The arm
        /// is the thing being read on this row — the chevron only says which way
        /// it will go — and a name starting immediately after a glyph reads as a
        /// caption on the glyph instead of as the heading it is.
        /// </summary>
        const float BranchTextLeft = 60f;

        void BranchHeader(UnitBranch branch, int count, bool open)
        {
            var row = UIFactory.CreateBorderedPanel(_listContent, "Branch_" + branch,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 34);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => ToggleBranch(branch));

            // An accent bar down the open one, so the arm the list is unfolded
            // under is readable without going back to the chevron.
            var stripe = UIFactory.CreatePanel(row, "Stripe",
                open ? UiTheme.Accent : new Color(0, 0, 0, 0));
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(3, -8);
            stripe.GetComponent<Image>().raycastTarget = false;

            var chevron = UIFactory.CreateText(row, open ? "▾" : "▸", UiTheme.FontSmall,
                open ? UiTheme.Accent : UiTheme.TextDim, TextAnchor.MiddleCenter);
            chevron.raycastTarget = false;
            UIFactory.Place(chevron.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(16f, 16f));

            var text = UIFactory.CreateSectionHeader(row,
                UnitBranchInfo.DisplayName(branch).ToUpperInvariant(),
                open ? UiTheme.Accent : UiTheme.Text);
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(BranchTextLeft, 0f), new Vector2(InnerWidth - BranchTextLeft - 60f, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(40f, 16f));
        }

        /// <summary>
        /// The REINFORCEMENT list: this side's schedule, one row per type, earliest
        /// first.
        ///
        /// **A row is an order, not a record.** Everything on it can be changed
        /// from the row — how many arrive, when, and whether the row is there at
        /// all — because a schedule that could only be added to would make the
        /// first mistake permanent. What it deliberately cannot do is place the
        /// formation: that is what the map is for, and a reinforcement that
        /// could be conjured onto the ground from the authoring page would be
        /// the DEPLOY button in the wrong mode.
        ///
        /// Written from the catalogue next door — right-click a card, ADD TO
        /// REINFORCEMENT. See docs/30-REINFORCEMENTS.md.
        /// </summary>
        int PopulateArriving()
        {
            if (_reinforcements == null)
            {
                EmptyRow("No schedule.", EmptyListSidePad);
                return 0;
            }

            var rows = _reinforcements.For(_team);
            if (rows.Count == 0)
            {
                EmptyRow("Nothing is due to arrive for this side. Right-click a type under "
                         + "AVAILABLE and choose ADD TO REINFORCEMENT.", EmptyListSidePad);
                return 0;
            }

            string folder = _team == Team.User ? "Friendly" : "Enemy";
            int formations = 0;
            foreach (var row in rows)
            {
                ArrivingRow(row, folder);
                formations += Mathf.Max(1, row.count);
            }
            return formations;
        }

        /// <summary>
        /// One scheduled arrival. Two lines: what it is, then when and how many.
        ///
        /// The steppers are on the row rather than behind a selection because
        /// there are only ever a handful of rows and every one of them is being
        /// tuned against the others — "this one earlier, that one bigger" is the
        /// whole of laying a schedule out, and a panel that made each change a
        /// select-then-edit would put a click between every pair of them.
        /// </summary>
        void ArrivingRow(ReinforcementEntry entry, string folder)
        {
            var def = UnitDatabase.Get(entry.defId);
            string name = def != null ? def.name : entry.defId;

            var card = UIFactory.CreateBorderedPanel(_listContent, "Arriving_" + entry.defId,
                UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, ArrivingRowHeight);

            if (def != null)
            {
                var sprite = UIFactory.LoadIconSprite(folder, def.id);
                if (sprite != null)
                {
                    var icon = UIFactory.CreateImage(card, sprite, "Icon");
                    icon.raycastTarget = false;
                    UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 1f),
                        new Vector2(8, -6), new Vector2(28, 28));
                }
            }

            var title = UIFactory.CreateText(card, name, UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            title.raycastTarget = false;
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f),
                new Vector2(42, -8), new Vector2(ArrivingRowWidth - 78f, 18f));
            UIFactory.Fit(title, 8);

            var sub = UIFactory.CreateText(card, entry.echelon, UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            sub.raycastTarget = false;
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f),
                new Vector2(42, -24), new Vector2(ArrivingRowWidth - 78f, 14f));

            var drop = UIFactory.CreateButton(card, "✕", () =>
            {
                _reinforcements.Remove(entry);
                DropRejected?.Invoke($"{name} taken off the schedule.");
            }, UiTheme.SurfaceHover, UiTheme.TextDim, 12);
            UIFactory.Place((RectTransform)drop.transform, new Vector2(1f, 1f),
                new Vector2(-6, -6), new Vector2(22, 22));
            UiTooltip.Attach(drop.gameObject, "Remove from the schedule", UiTooltip.Side.Left);

            // --- the second line: when, and how many.
            Stepper(card, 8f, "ARRIVES", $"H+{entry.arrivalMinutes}",
                () => _reinforcements.Reschedule(entry, -ArrivalStepMinutes),
                () => _reinforcements.Reschedule(entry, ArrivalStepMinutes),
                "Earlier", "Later");

            Stepper(card, ArrivingRowWidth * 0.5f + 4f, "HOW MANY", entry.count.ToString(),
                () => _reinforcements.StepCount(entry, -1),
                () => _reinforcements.StepCount(entry, 1),
                "One fewer", "One more");
        }

        /// <summary>Height of one REINFORCEMENT row: two lines plus its steppers.</summary>
        const float ArrivingRowHeight = 68f;
        /// <summary>Width a REINFORCEMENT row actually gets, once the scrollbar and list padding are off.</summary>
        const float ArrivingRowWidth = InnerWidth - UIFactory.ScrollbarWidth - Pad * 2f;
        /// <summary>What the ARRIVES ± moves by. Five minutes is a bound in a battle, not a rounding.</summary>
        const int ArrivalStepMinutes = 5;

        /// <summary>
        /// A captioned ◄ value ► on the bottom line of a REINFORCEMENT row.
        ///
        /// The caption is above the value rather than beside it: two of these
        /// sit side by side on a 280 px row, and a horizontal caption would take
        /// the space the arrows need to stay big enough to hit.
        /// </summary>
        void Stepper(RectTransform card, float x, string caption, string value,
            UnityEngine.Events.UnityAction less, UnityEngine.Events.UnityAction more,
            string lessHint, string moreHint)
        {
            float width = ArrivingRowWidth * 0.5f - 12f;

            var label = UIFactory.CreateText(card, caption, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            UIFactory.Place(label.rectTransform, new Vector2(0f, 0f), new Vector2(x, 26f),
                new Vector2(width, 12f));
            UIFactory.Fit(label, 7);

            var back = UIFactory.CreateButton(card, "◄", less, UiTheme.SurfaceHover, UiTheme.TextDim, 10);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0f, 0f),
                new Vector2(x, 6f), new Vector2(20, 18));
            UiTooltip.Attach(back.gameObject, lessHint);

            var readout = UIFactory.CreateText(card, value, UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            readout.raycastTarget = false;
            UIFactory.Place(readout.rectTransform, new Vector2(0f, 0f),
                new Vector2(x + 22f, 6f), new Vector2(width - 46f, 18f));
            UIFactory.Fit(readout, 8);

            var fwd = UIFactory.CreateButton(card, "►", more, UiTheme.SurfaceHover, UiTheme.TextDim, 10);
            UIFactory.Place((RectTransform)fwd.transform, new Vector2(0f, 0f),
                new Vector2(x + width - 20f, 6f), new Vector2(20, 18));
            UiTooltip.Attach(fwd.gameObject, moreHint);
        }

        /// <summary>
        /// The DEPLOYED list, **split by side**.
        ///
        /// It used to be one list in registry order, which is the order things
        /// happened to be spawned in — so a scenario with both sides laid out
        /// interleaved them, and the only thing telling a blue card from a red
        /// one was a 3 px stripe down its edge. Two headed blocks answer the
        /// question the list is actually opened with: what has each side got.
        ///
        /// Both blocks are always drawn, even when empty, because "nothing
        /// deployed for the enemy" is information a designer wants rather than a
        /// row to be hidden.
        /// </summary>
        int PopulateDeployed()
        {
            int shown = 0, onMap = 0;

            foreach (var team in new[] { Team.User, Team.Enemy })
            {
                var matching = new List<UnitActor>();
                int held = 0;

                foreach (var actor in UnitRegistry.All)
                {
                    if (actor == null || !actor.IsAlive) continue;
                    // A formation the fog has taken off the map must not still
                    // be listed here with its call sign and readiness — the list
                    // would hand back exactly what the fog is withholding.
                    if (actor.HiddenByFog) continue;
                    if (actor.State.TeamEnum != team) continue;

                    held++;
                    if (!Matches(actor.Def.name, actor.Def.id, actor.State.customName)) continue;
                    matching.Add(actor);
                }

                onMap += held;
                SideHeader(team, matching.Count);

                if (matching.Count == 0)
                {
                    EmptyRow(held == 0
                        ? "Nothing deployed for this side."
                        : "No formation on this side matches that search.");
                    continue;
                }

                int index = 0;
                foreach (var actor in matching) CreateDeployedCard(actor, ++index);
                shown += matching.Count;
            }

            if (onMap == 0 && string.IsNullOrEmpty(_search))
                EmptyRow("Drag a unit from AVAILABLE onto the map to deploy it.");

            return shown;
        }

        /// <summary>
        /// A side's heading in the DEPLOYED list: a colour bar, the side's name
        /// and how many of it are listed. Coloured, because the side is the one
        /// thing the block is separating on.
        /// </summary>
        void SideHeader(Team team, int count)
        {
            bool friendly = team == Team.User;
            var colour = friendly ? UiTheme.Friendly : UiTheme.Hostile;

            var row = UIFactory.CreatePanel(_listContent, "Side_" + team, new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, 26);

            var bar = UIFactory.CreatePanel(row, "Bar", colour);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
            bar.pivot = new Vector2(0, 0.5f);
            bar.offsetMin = new Vector2(0, 5f);
            bar.offsetMax = new Vector2(3f, -5f);
            bar.GetComponent<Image>().raycastTarget = false;

            var text = UIFactory.CreateSectionHeader(row,
                friendly ? "FRIENDLY" : "ENEMY", colour);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f),
                new Vector2(InnerWidth - 70f, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f),
                new Vector2(40f, 16f));
        }

        bool Matches(params string[] fields)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            foreach (var f in fields)
                if (!string.IsNullOrEmpty(f) &&
                    f.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// The line a list shows when it has nothing in it.
        ///
        /// <paramref name="sidePad"/> insets the words from both edges of the
        /// panel. An empty list is the one place a panel is *only* a paragraph,
        /// and a paragraph running edge to edge in a 250 px column reads as
        /// something that has overflowed rather than something that was set —
        /// the cards it stands in for all have a border holding them off the
        /// sides, and the text has to be given that margin explicitly because it
        /// has no border of its own to provide it.
        /// </summary>
        void EmptyRow(string message, float sidePad = 0f)
        {
            if (sidePad <= 0f)
            {
                var plain = UIFactory.CreateText(_listContent, message, UiTheme.FontSmall,
                    UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)plain.transform).sizeDelta = new Vector2(0, 52);
                return;
            }

            // A transparent row the layout group can size, with the words inset
            // inside it — the group drives its children's width, so the margin
            // cannot be put on the label itself.
            var row = UIFactory.CreatePanel(_listContent, "Empty", new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, 76);
            row.GetComponent<Image>().raycastTarget = false;

            var t = UIFactory.CreateText(row, message, UiTheme.FontSmall,
                UiTheme.TextFaint, TextAnchor.UpperLeft);
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(sidePad, 0f);
            rt.offsetMax = new Vector2(-sidePad, -4f);
        }

        /// <summary>
        /// Margin either side of an empty REINFORCEMENT list's explanation.
        /// Wider than the panel's own padding: this is a sentence telling the
        /// player how to fill the list, and it is the only thing on the page.
        /// </summary>
        const float EmptyListSidePad = 20f;

        /// <summary>A draggable catalogue entry.</summary>
        void CreateAvailableCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreateBorderedPanel(_listContent, "Card_" + def.id, UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, AvailableCardHeight);

            var sprite = CardIcon(card, folder, def.id);

            var name = UIFactory.CreateText(card, def.name, UiTheme.FontBody, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(name.rectTransform, CardTextX, 10f, CardTextWidth, 18f);
            UIFactory.Fit(name, 9);

            // The three numbers, as marks and values rather than as a sentence.
            // "ATK 12 · DEF 8 · 45 km/h" was one line of prose read as data:
            // the words are three quarters of it, they repeat on every card,
            // and at 11 px the eye has to parse the dots to find the figures.
            StatChips(card, def);

            // The card carries three verbs and shows none of them. Drag is
            // discoverable by accident and click is the obvious one, but the
            // right-click menu — the only way to put a type on the arrival
            // schedule, and the only way onto ground you have to pan to first —
            // is a gesture nobody tries on a list. One line says so once.
            UiTooltip.Attach(card.gameObject,
                $"{def.name} — drag onto the map to deploy, click for its data, "
                + "right-click for ADD TO MAP and ADD TO REINFORCEMENT.",
                UiTooltip.Side.Right);

            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(def, sprite));
            AddEvent(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
            AddEvent(trigger, EventTriggerType.EndDrag, e => EndDrag((PointerEventData)e));
            // A click asks what the type is; a drag deploys one. Both live on
            // the same card because they are the same question at two speeds,
            // and uGUI raises PointerClick only when no drag happened.
            AddEvent(trigger, EventTriggerType.PointerClick, e =>
            {
                var pointer = (PointerEventData)e;
                if (pointer.button == PointerEventData.InputButton.Right)
                {
                    OpenCardMenu(def, pointer.position);
                    return;
                }
                InspectTypeRequested?.Invoke(def, _team);
            });
        }

        /// <summary>
        /// The right-click menu on a catalogue card: the two things you can do
        /// with a type that are not "tell me about it".
        ///
        /// Both already existed as gestures — drag to the map, and a schedule
        /// loaded from the file — and neither was reachable from the card. A
        /// drag is the wrong gesture for ground you have to pan to first, and
        /// there was no way at all to put a type on the arrival schedule. The
        /// menu is where a second and third verb can go without either of them
        /// needing a control of its own on a panel that has no room for one.
        /// </summary>
        void OpenCardMenu(UnitDefinition def, Vector2 screenPos)
        {
            var items = new List<ContextMenuUI.Item>
            {
                new ContextMenuUI.Item("ADD TO MAP", () => ArmPlacement(def)),
                new ContextMenuUI.Item("ADD TO REINFORCEMENT", () => AddToReinforcement(def))
            };
            ContextMenuUI.Open(_canvas, screenPos,
                $"{def.name}  ·  {(_team == Team.Enemy ? "ENEMY" : "FRIENDLY")}", items);
        }

        /// <summary>
        /// Puts a type on the arrival schedule for the side the palette is
        /// working for, and shows the tab it landed in.
        ///
        /// Switching to the tab is the answer to "did that do anything": the
        /// schedule is a list on a page the player is not looking at, and an
        /// action whose only feedback is a line in the flash bar reads as having
        /// been ignored.
        /// </summary>
        void AddToReinforcement(UnitDefinition def)
        {
            if (_reinforcements == null || def == null) return;

            var entry = _reinforcements.Add(def, _team, DefaultEchelon, DefaultArrivalMinutes);
            if (entry == null) return;

            SetListMode(ListMode.Reinforcement);
            DropRejected?.Invoke(entry.count > 1
                ? $"{def.name} — now {entry.count} arriving at H+{entry.arrivalMinutes}."
                : $"{def.name} joins the schedule at H+{entry.arrivalMinutes}.");
        }

        /// <summary>
        /// When a type first put on the schedule is set to arrive. Half an hour
        /// in: far enough that it is plainly a reinforcement rather than part of
        /// the opening laydown, near enough that a designer testing the scenario
        /// does not have to wait out an hour to see it work. The row's own ± is
        /// the answer for anything else.
        /// </summary>
        const int DefaultArrivalMinutes = 30;

        /// <summary>
        /// The attack / defence / speed row on an AVAILABLE card: a glyph and a
        /// figure, three times across.
        ///
        /// Laid out on a fixed pitch rather than flowed, so the numbers line up
        /// down the column — a list of cards is compared far more often than any
        /// single card is read, and columns are what make that possible. Each
        /// pair carries a hover caption, because a glyph is only self-evident to
        /// somebody who already knows what it stands for.
        /// </summary>
        void StatChips(RectTransform card, UnitDefinition def)
        {
            const float Pitch = 66f, Top = 34f, GlyphSize = 13f;

            void Chip(int index, Sprite glyph, string value, Color tint, string caption)
            {
                float x = CardTextX + index * Pitch;

                var icon = UIFactory.CreateImage(card, glyph, "Stat");
                icon.color = tint;
                icon.raycastTarget = false;
                UIFactory.PlaceTopLeft((RectTransform)icon.transform, x, Top + 2f,
                    GlyphSize, GlyphSize);

                var text = UIFactory.CreateText(card, value, UiTheme.FontSmall, UiTheme.Text,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.PlaceTopLeft(text.rectTransform, x + GlyphSize + 5f, Top,
                    Pitch - GlyphSize - 10f, 17f);
                UIFactory.Fit(text, 8);
                text.raycastTarget = false;

                UiTooltip.Attach(icon.gameObject, caption, UiTooltip.Side.Right);
            }

            Chip(0, UiIcons.Attack, $"{def.attack:0}", UiTheme.Hostile, "Attack");
            Chip(1, UiIcons.Guard, $"{def.defence:0}", UiTheme.Accent, "Defence");
            Chip(2, UiIcons.Gauge, $"{def.speedKmh:0}", UiTheme.TextDim, "Speed, km/h");
        }

        /// <summary>A unit actually on the map: call sign, type and readiness.</summary>
        void CreateDeployedCard(UnitActor actor, int index)
        {
            var s = actor.State;
            var card = UIFactory.CreateBorderedPanel(_listContent, "Deployed_" + s.instanceId,
                UiTheme.Surface, UiTheme.Border);
            card.sizeDelta = new Vector2(0, DeployedCardHeight);

            // Click selects the formation on the map; double-click also flies
            // the camera to it.
            //
            // A PointerClick trigger rather than a Button, because uGUI's
            // Button has no notion of a second click and
            // <see cref="PointerEventData.clickCount"/> already carries one —
            // timing clicks by hand here would be a worse copy of what the
            // event system has counted. The single-click path still runs on the
            // first click of a pair, which is right: flying to a formation you
            // have not selected would leave the map somewhere new with nothing
            // to show for it.
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.PointerClick, e =>
            {
                var pointer = (PointerEventData)e;
                if (pointer.clickCount >= 2) FocusUnitRequested?.Invoke(actor);
                else SelectUnitRequested?.Invoke(actor);
            });

            string folder = s.TeamEnum == Team.User ? "Friendly" : "Enemy";
            CardIcon(card, folder, actor.Def.id);

            string callSign = string.IsNullOrEmpty(s.customName)
                ? $"1-{index} {Abbreviate(actor.Def.name)}"
                : s.customName;

            // Side reads as a stripe down the card's left edge. It used to be a
            // dot in the text column, where it sat on top of the readiness line.
            var stripe = UIFactory.CreatePanel(card, "TeamStripe",
                s.TeamEnum == Team.User ? UiTheme.Friendly : UiTheme.Hostile);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = new Vector2(1, 0);
            stripe.sizeDelta = new Vector2(3, -2);
            stripe.GetComponent<Image>().raycastTarget = false;

            // Leaves room for the ⋮ button pinned to the card's right edge.
            float cardW = CardTextWidth - 26f;

            var title = UIFactory.CreateText(card, callSign, UiTheme.FontBody, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, CardTextX, 8f, cardW, 17f);
            UIFactory.Fit(title);

            var subtitle = UIFactory.CreateText(card, actor.Def.name, UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(subtitle.rectTransform, CardTextX, 25f, cardW, 15f);
            UIFactory.Fit(subtitle);

            // Third line is the design's metadata row. Real readiness data
            // rather than a decorative timestamp.
            var meta = UIFactory.CreateText(card,
                $"{s.echelon}  ·  STR {s.strength * 100f:0}%  ·  {s.status}",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(meta.rectTransform, CardTextX, 41f, cardW, 15f);
            UIFactory.Fit(meta);

            var kebab = UIFactory.CreateIconButton(card, UiIcons.Kebab,
                () => RemoveUnitRequested?.Invoke(actor), new Color(0, 0, 0, 0), UiTheme.TextFaint, 7f);
            UIFactory.Place((RectTransform)kebab.transform, new Vector2(1f, 0.5f), new Vector2(-6, 0), new Vector2(26, 26));
        }

        /// <summary>Framed unit icon, or a visible gap marker when the sprite is missing.</summary>
        Sprite CardIcon(RectTransform card, string folder, string unitId)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                icon.raycastTarget = false;
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(CardIconX, 0),
                    new Vector2(CardIconSize, CardIconSize));
                return sprite;
            }

            // Keep the layout intact and visibly flag the gap.
            var fallback = UIFactory.CreatePanel(card, "IconFallback", UiTheme.Panel);
            UIFactory.Place(fallback, new Vector2(0f, 0.5f), new Vector2(CardIconX, 0),
                new Vector2(CardIconSize, CardIconSize));
            var mark = UIFactory.CreateText(fallback, "?", 16, UiTheme.TextFaint, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(mark.rectTransform);
            return null;
        }

        /// <summary>"Mechanised infantry" → "MECH INF": a call-sign-length label.</summary>
        static string Abbreviate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "UNIT";
            var parts = name.Split(' ');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(p.Length <= 4 ? p.ToUpperInvariant()
                                        : p.Substring(0, 4).ToUpperInvariant());
                if (sb.Length >= 12) break;
            }
            return sb.ToString();
        }

        static void AddEvent(EventTrigger t, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(cb);
            t.triggers.Add(entry);
        }

        // ------------------------------------------------------- tool strip

        void BuildToolStrip(RectTransform panel)
        {
            var strip = UIFactory.CreatePanel(panel, "ToolStrip", UiTheme.Chrome);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.sizeDelta = new Vector2(0, ToolStripHeight);

            var rule = UIFactory.CreateDivider(strip, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = Vector2.zero;

            // Names the icon row. It used to sit under a full panel of labelled
            // controls that gave it context; alone at the foot of the rail it
            // reads as unexplained glyphs without this.
            var caption = UIFactory.CreateSectionHeader(strip, "TOOLS", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(caption.rectTransform, Pad, 8f, RailWidth - Pad * 2f, 14f);

            // Three tools, not five. The pencil and the square drew control
            // measures by hand; that whole feature is gone — see the class
            // remarks and docs/03-GAMEPLAY.md. Only the cursor latches now.
            AddTool(strip, 0, UiIcons.Cursor, () => SelectToolRequested?.Invoke());
            AddTool(strip, 1, UiIcons.Pin, () => GenerateSectorsRequested?.Invoke());
            AddTool(strip, 2, UiIcons.Chart, ToggleView);

            SetActiveTool(0);
        }

        void AddTool(RectTransform strip, int index, Sprite glyph, UnityEngine.Events.UnityAction action)
        {
            // Anchored to the strip's bottom, clear of the caption band above.
            var frame = UIFactory.CreateBorderedPanel(strip, "Tool" + index, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 0f), new Vector2(Pad + index * 42f, 10), new Vector2(36, 32));

            int captured = index;
            var btn = UIFactory.CreateIconButton(frame, glyph, () =>
            {
                // Sector generation and the view toggle are one-shot commands,
                // not modes — only the cursor latches.
                if (captured == 0) SetActiveTool(captured);
                action();
            }, new Color(0, 0, 0, 0), UiTheme.TextDim, 8f);
            UIFactory.Stretch((RectTransform)btn.transform);

            // Find the glyph by name: GetComponentInChildren searches the object
            // itself first and would hand back the button's own background.
            _tools.Add((frame.Find("Fill").GetComponent<Image>(),
                        btn.transform.Find("Glyph").GetComponent<Image>()));
        }

        void SetActiveTool(int index)
        {
            _activeTool = index;
            for (int i = 0; i < _tools.Count; i++)
            {
                bool on = i == index;
                _tools[i].fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                _tools[i].glyph.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        /// <summary>Puts the cursor tool back on top. Kept for callers that end a mode.</summary>
        public void ResetToolToSelect() => SetActiveTool(0);
    }
}
