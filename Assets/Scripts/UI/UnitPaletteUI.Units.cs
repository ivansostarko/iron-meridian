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
            float half = (InnerWidth - 6f) / 2f;
            _blueTab = SideButton(content, "FRIENDLY", Pad, half, () => SetTeam(Team.User), out _blueFill);
            _redTab = SideButton(content, "ENEMY", Pad + half + 6f, half, () => SetTeam(Team.Enemy), out _redFill);

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
            _availableTabBtn = ListModeButton(content, "AVAILABLE", Pad, () => SetListMode(ListMode.Available));
            _deployedTabBtn = ListModeButton(content, "DEPLOYED", Pad + 86f, () => SetListMode(ListMode.Deployed));

            var badge = UIFactory.CreatePanel(content, "CountBadge", UiTheme.AccentWash);
            UIFactory.Place(badge, new Vector2(0f, 1f), new Vector2(PanelWidth - Pad - 34, ListTop + 2f), new Vector2(34, 18));
            badge.GetComponent<Image>().raycastTarget = false;
            _listCount = UIFactory.CreateText(badge, "0", UiTheme.FontLabel, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_listCount.rectTransform);

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
            srt.offsetMax = new Vector2(0, ListTop - 26f);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset((int)Pad, (int)Pad, 4, 8);

            SetListMode(ListMode.Available);
        }

        Button SideButton(RectTransform content, string label, float x, float w,
            UnityEngine.Events.UnityAction action, out Image fill)
        {
            var b = UIFactory.CreateButton(content, label, action, UiTheme.Surface, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, -8), new Vector2(w, 32));
            fill = b.GetComponent<Image>();
            return b;
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

        Button ListModeButton(RectTransform content, string label, float x, UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.CreateButton(content, label, action, new Color(0, 0, 0, 0),
                UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(x, ListTop), new Vector2(82, 22));
            return b;
        }

        void SetListMode(ListMode mode)
        {
            _listMode = mode;
            TintListTab(_availableTabBtn, mode == ListMode.Available);
            TintListTab(_deployedTabBtn, mode == ListMode.Deployed);
            Populate();
        }

        static void TintListTab(Button b, bool active)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<Text>(true);
            if (t != null) t.color = active ? UiTheme.Accent : UiTheme.TextFaint;
            b.GetComponent<Image>().color = active ? UiTheme.AccentWash : new Color(0, 0, 0, 0);
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
            _blueFill.color = team == Team.User ? UiTheme.Friendly : UiTheme.Surface;
            _redFill.color = team == Team.Enemy ? UiTheme.Hostile : UiTheme.Surface;
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

            int count = _listMode == ListMode.Available ? PopulateAvailable() : PopulateDeployed();
            if (_listCount != null) _listCount.text = count.ToString();
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
        void BranchHeader(UnitBranch branch, int count, bool open)
        {
            var row = UIFactory.CreateBorderedPanel(_listContent, "Branch_" + branch,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, 30);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => ToggleBranch(branch));

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
                new Vector2(30f, 0f), new Vector2(InnerWidth - 90f, 16f));
            UIFactory.Fit(text, 8);

            var badge = UIFactory.CreateText(row, count.ToString(), UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight, FontStyle.Bold);
            badge.raycastTarget = false;
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(40f, 16f));
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

        void EmptyRow(string message)
        {
            var t = UIFactory.CreateText(_listContent, message, UiTheme.FontSmall,
                UiTheme.TextFaint, TextAnchor.UpperLeft);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 52);
        }

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

            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(def, sprite));
            AddEvent(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
            AddEvent(trigger, EventTriggerType.EndDrag, e => EndDrag((PointerEventData)e));
            // A click asks what the type is; a drag deploys one. Both live on
            // the same card because they are the same question at two speeds,
            // and uGUI raises PointerClick only when no drag happened.
            AddEvent(trigger, EventTriggerType.PointerClick,
                e => InspectTypeRequested?.Invoke(def, _team));
        }

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
