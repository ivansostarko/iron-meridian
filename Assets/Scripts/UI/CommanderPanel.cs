using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// The map editor's COMMANDERS section: the order of battle above the units.
    ///
    /// **Why this is its own class.** <see cref="UnitPaletteUI"/> already builds
    /// thirteen sections and is 2 700 lines; the commander section is the first
    /// one that is a small application in its own right — two lists, a detail
    /// block, a chain of command and an assignment gesture. Bolting it on would
    /// have made the palette the place where nothing can be found. The palette
    /// owns the panel and the nav row; this owns everything inside it.
    ///
    /// **One page, one scroll.** The commander list, the selected officer's
    /// details and the formations he holds are all laid out down a single
    /// fixed-height page inside one scroll view. Nested scroll views inside a
    /// 274 px rail are a fight the player always loses.
    ///
    /// See docs/23-COMMANDERS.md.
    /// </summary>
    public class CommanderPanel
    {
        const float Pad = UiTheme.PanelPadding;
        const float InnerWidth = UiTheme.SectionPanelWidth - Pad * 2f;
        /// <summary>A roster row — tall enough to carry a face beside the name.</summary>
        const float RowHeight = 46f;
        const float Gap = 4f;

        /// <summary>The selected officer's card: photograph, name, rank, standing.</summary>
        const float ProfileHeight = 96f;
        const float ProfilePortrait = 80f;
        /// <summary>Thumbnail on a roster row.</summary>
        const float RowPortrait = 34f;

        /// <summary>Assign the map's current selection to the chosen officer.</summary>
        public System.Action<CommanderState> AssignSelectionRequested;
        /// <summary>Select this officer's formations on the map.</summary>
        public System.Action<CommanderState> SelectUnitsRequested;
        /// <summary>Short user-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;

        readonly RectTransform _section;
        RectTransform _page;

        Team _team = Team.User;
        CommanderState _selected;

        public CommanderPanel(RectTransform section) => _section = section;

        // ------------------------------------------------------------- build

        public void Build()
        {
            _page = ScrollPage(_section);
            CommanderRegistry.Changed += Rebuild;
            UnitRegistry.Changed += Rebuild;
            Rebuild();
        }

        /// <summary>Called when the section is opened; catches up on anything missed while it was shut.</summary>
        public void OnShown()
        {
            if (_dirty) Rebuild();
        }

        public void Dispose()
        {
            CommanderRegistry.Changed -= Rebuild;
            UnitRegistry.Changed -= Rebuild;
        }

        /// <summary>
        /// One scroll view over a page laid out by absolute offsets. The stock
        /// content stacks its children with a <see cref="VerticalLayoutGroup"/>,
        /// which is **disabled rather than destroyed**: `Destroy` on a component
        /// is deferred to end of frame, so a destroyed layout group would still
        /// lay out the rows added to it a few lines later.
        /// </summary>
        static RectTransform ScrollPage(RectTransform section)
        {
            var scroll = UIFactory.CreateScrollView(section, out RectTransform page, withScrollbar: true);
            UIFactory.Stretch((RectTransform)scroll.transform);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var layout = page.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = page.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            return page;
        }

        static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        // ----------------------------------------------------------- rebuild

        /// <summary>Running Y as the page is filled, so the page can size itself at the end.</summary>
        float _y;

        /// <summary>
        /// Set when something changed while the section was closed. Loading a
        /// map spawns every unit one at a time and each spawn raises
        /// <see cref="UnitRegistry.Changed"/> — rebuilding a page of thirty rows
        /// once per formation, for a panel nobody is looking at, is a hundred
        /// wasted rebuilds on every load.
        /// </summary>
        bool _dirty;

        public void Rebuild()
        {
            if (_page == null) return;

            if (!_section.gameObject.activeInHierarchy) { _dirty = true; return; }
            _dirty = false;

            ClearChildren(_page);
            _y = 8f;

            // A selection that has been deleted, or belongs to the other side,
            // must not survive the rebuild — it would sit in the detail block
            // describing an officer the list no longer shows.
            if (_selected != null &&
                (CommanderRegistry.Get(_selected.id) == null || _selected.TeamEnum != _team))
                _selected = null;

            BuildTeamTabs();
            BuildRoster();
            BuildDetail();
            BuildList();

            _page.sizeDelta = new Vector2(0, _y + 16f);
        }

        void BuildTeamTabs()
        {
            float half = (InnerWidth - 4f) / 2f;
            Tab("FRIENDLY", Team.User, 0, half);
            Tab("ENEMY", Team.Enemy, 1, half);
            _y += 34f + Gap * 2f;

            void Tab(string label, Team team, int index, float w)
            {
                bool on = _team == team;
                Color fill = on
                    ? (team == Team.User ? UiTheme.Friendly : UiTheme.Hostile)
                    : UiTheme.Surface;

                var btn = UIFactory.CreateButton(_page, $"{label}  {CommanderRegistry.CountOfTeam(team)}",
                    () => { _team = team; _selected = null; Rebuild(); },
                    fill, on ? Color.white : UiTheme.TextDim, UiTheme.FontSmall);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f),
                    new Vector2(Pad + index * (w + 4f), -_y), new Vector2(w, 34));
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 9);
            }
        }

        /// <summary>
        /// The roster's one action.
        ///
        /// **SEED is gone.** A chain of command is not an optional extra a
        /// player might press a button for — every formation on the map belongs
        /// under somebody, and a scenario with an empty roster was a scenario
        /// where the COMMANDERS panel had nothing to say and every unit read as
        /// unassigned. Both sides are now seeded automatically when a map comes
        /// up with no chain of its own (see
        /// <c>GameController.EnsureCommanders</c>), so the button had become a
        /// way of doing again what had already been done.
        ///
        /// CLEAR ALL stays: emptying the roster deliberately is still a thing a
        /// designer may want, and it is the one action here that cannot be
        /// undone by looking away.
        /// </summary>
        void BuildRoster()
        {
            Label("ROSTER");

            var clear = UIFactory.CreateButton(_page, "CLEAR ALL", () =>
            {
                CommanderRegistry.Clear(_team);
                _selected = null;
                Flash?.Invoke($"Cleared the {SideWord()} chain of command. Every formation is now unassigned.");
            }, UiTheme.Danger, Color.white, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)clear.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -_y), new Vector2(InnerWidth, 32));
            UIFactory.Fit(clear.GetComponentInChildren<Text>(), 9);

            _y += 32f + Gap * 2f;
        }

        string SideWord() => _team == Team.User ? "friendly" : "enemy";

        // ------------------------------------------------------------ detail

        void BuildDetail()
        {
            if (_selected == null)
            {
                Note("Pick an officer below to see what he commands, set his rank, " +
                     "put him under another officer, or take him out of action.");
                return;
            }

            var c = _selected;
            var rank = RankCatalog.Get(c.TeamEnum, c.rank);

            Label("SELECTED OFFICER");

            BuildProfile(c, rank);

            // --- name ---
            var name = UIFactory.CreateInputField(_page, "surname", UiTheme.FontSmall);
            UIFactory.Place((RectTransform)name.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -_y), new Vector2(InnerWidth, 30));
            name.GetComponent<Image>().color = UiTheme.Surface;
            name.text = c.name;
            name.onEndEdit.AddListener(v =>
            {
                if (!string.IsNullOrWhiteSpace(v)) c.name = v.Trim();
                Rebuild();
            });
            _y += 30f + Gap;

            // --- rank, stepped along this side's own ladder ---
            Stepper($"{rank.abbrev} · {rank.name}",
                () => Step(c, -1), () => Step(c, +1));

            // --- superior ---
            var superior = CommanderRegistry.Get(c.superiorId);
            Stepper(superior == null ? "REPORTS TO: nobody"
                                     : $"REPORTS TO: {Short(superior)}",
                () => StepSuperior(c, -1), () => StepSuperior(c, +1));

            // --- in post ---
            var toggle = UIFactory.CreateButton(_page,
                c.active ? "IN POST" : "OUT OF ACTION",
                () => { c.active = !c.active; CommanderRegistry.RaiseChanged(); },
                c.active ? UiTheme.Success : UiTheme.Danger, Color.white, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)toggle.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -_y), new Vector2(InnerWidth, 32));
            _y += 32f + Gap;

            // The chain is what the switch actually costs, so say it rather than
            // leaving the player to infer it from a combat result.
            bool intact = CommanderRegistry.ChainIntact(c);
            Note(intact
                ? "Chain intact — his formations fight with a command bonus."
                : "Chain broken — his formations, and everything below him, fight at a penalty.");

            // --- assignment ---
            float half = (InnerWidth - 4f) / 2f;

            var assign = UIFactory.CreateButton(_page, "ASSIGN SELECTED",
                () => AssignSelectionRequested?.Invoke(c),
                UiTheme.Accent, GameConfig.UiBackground, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)assign.transform, new Vector2(0f, 1f),
                new Vector2(Pad, -_y), new Vector2(half, 32));
            UIFactory.Fit(assign.GetComponentInChildren<Text>(), 9);

            var release = UIFactory.CreateButton(_page, "RELEASE ALL", () =>
            {
                int n = CommanderRegistry.ClearAssignments(c);
                CommanderRegistry.RaiseChanged();
                Flash?.Invoke(n == 0
                    ? $"{Short(c)} holds no formations."
                    : $"{Short(c)} released {n} formation(s).");
            }, UiTheme.Surface, UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)release.transform, new Vector2(0f, 1f),
                new Vector2(Pad + half + 4f, -_y), new Vector2(half, 32));
            UIFactory.Fit(release.GetComponentInChildren<Text>(), 9);
            _y += 32f + Gap * 2f;

            // --- what he commands ---
            var units = CommanderRegistry.UnitsOf(c);
            var subs = CommanderRegistry.SubordinatesOf(c);

            Label($"COMMANDS  ·  {units.Count} FORMATION(S)");

            if (units.Count == 0)
            {
                Note("Nothing assigned. Select formations on the map, then press ASSIGN SELECTED.");
            }
            else
            {
                foreach (var u in units)
                    Row(UnitLabel(u), UiTheme.Text, () => SelectUnitsRequested?.Invoke(c));
            }

            if (subs.Count > 0)
            {
                Label($"SUBORDINATE OFFICERS  ·  {subs.Count}");
                foreach (var s in subs)
                {
                    var captured = s;
                    Row(Short(s), s.active ? UiTheme.Text : UiTheme.TextFaint,
                        () => { _selected = captured; Rebuild(); });
                }
            }
        }

        /// <summary>
        /// The officer's own card: his photograph, who he is, and where he
        /// stands — read before any of the controls under it.
        ///
        /// It repeats his rank and standing, which the stepper and the lamp
        /// below also carry. That is deliberate: those are *controls*, read for
        /// what they will do next, and a profile is read for who is being
        /// looked at. The card is the answer to "who is this", in one block,
        /// beside the face.
        /// </summary>
        void BuildProfile(CommanderState c, RankDef rank)
        {
            var card = UIFactory.CreateBorderedPanel(_page, "Profile", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(card, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                new Vector2(InnerWidth, ProfileHeight));

            Portrait(card, c, 8f, 8f, ProfilePortrait);

            float x = 8f + ProfilePortrait + 10f;
            float w = InnerWidth - x - 8f;

            var title = UIFactory.CreateText(card, Short(c), UiTheme.FontBody,
                c.active ? UiTheme.Text : UiTheme.TextFaint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, x, 12f, w, 18f);
            UIFactory.Fit(title, 9);

            var ladder = UIFactory.CreateText(card, rank.name, UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(ladder.rectTransform, x, 32f, w, 14f);
            UIFactory.Fit(ladder, 8);

            bool intact = CommanderRegistry.ChainIntact(c);
            var status = UIFactory.CreateText(card,
                !c.active ? "OUT OF ACTION" : intact ? "IN POST" : "IN POST · CHAIN BROKEN",
                UiTheme.FontLabel,
                !c.active ? UiTheme.Danger : intact ? UiTheme.Success : UiTheme.Warning,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(status.rectTransform, x, 50f, w, 14f);
            UIFactory.Fit(status, 8);

            int held = CommanderRegistry.UnitsOf(c).Count;
            var commands = UIFactory.CreateText(card,
                held == 1 ? "1 formation" : $"{held} formations",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(commands.rectTransform, x, 66f, w, 14f);
            UIFactory.Fit(commands, 8);

            _y += ProfileHeight + Gap * 2f;
        }

        /// <summary>
        /// One officer's photograph in a hairline frame — see
        /// <see cref="CommanderPortraits"/> for which face he gets and why it
        /// does not change between rebuilds.
        ///
        /// Nothing here takes a raycast: the frame sits on top of the row's own
        /// click target, and a photograph that swallowed the click would make
        /// the most obvious part of the row the one part that does nothing.
        /// A missing file leaves the empty frame, which reads as a portrait not
        /// yet taken rather than as a hole in the panel.
        /// </summary>
        static void Portrait(RectTransform parent, CommanderState c, float x, float top, float size)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Portrait", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.PlaceTopLeft(frame, x, top, size, size);
            frame.GetComponent<Image>().raycastTarget = false;

            var sprite = UIFactory.LoadSprite(CommanderPortraits.PathFor(c));
            if (sprite == null) return;          // LoadSprite has already warned

            var photo = UIFactory.CreateImage(frame, sprite, "Photo");
            photo.raycastTarget = false;
            // An officer out of action is greyed rather than hidden: he is still
            // in the order of battle, which is the whole point of the switch.
            photo.color = c.active ? Color.white : new Color(0.5f, 0.54f, 0.6f, 1f);
            var rt = (RectTransform)photo.transform;
            UIFactory.Stretch(rt);
            rt.offsetMin = new Vector2(2, 2);
            rt.offsetMax = new Vector2(-2, -2);
        }

        void Step(CommanderState c, int by)
        {
            c.rank = RankCatalog.Step(c.TeamEnum, c.rank, by).name;
            CommanderRegistry.RaiseChanged();
        }

        /// <summary>
        /// Walks the superior through the roster: nobody, then each other
        /// officer on this side. Assignments that would make a loop are skipped
        /// rather than refused — stepping past them is what the player means, and
        /// an error message on a cycle button would be an error message on every
        /// third press.
        /// </summary>
        void StepSuperior(CommanderState c, int by)
        {
            var roster = CommanderRegistry.OfTeam(c.TeamEnum);
            var options = new List<CommanderState> { null };
            foreach (var other in roster)
                if (other != c && !CommanderRegistry.WouldCycle(c, other)) options.Add(other);

            int current = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null && options[i].id == c.superiorId) { current = i; break; }

            int next = ((current + by) % options.Count + options.Count) % options.Count;
            c.superiorId = options[next]?.id ?? "";
            CommanderRegistry.RaiseChanged();
        }

        // -------------------------------------------------------------- list

        void BuildList()
        {
            var roster = CommanderRegistry.OfTeam(_team);
            Label($"{(_team == Team.User ? "NATO" : "ENEMY")} ORDER OF BATTLE  ·  {roster.Count}");

            if (roster.Count == 0)
            {
                Note($"No {SideWord()} commanders — CLEAR ALL emptied the roster. Reload the map to rebuild a " +
                     "chain of command — one army commander, two corps, four divisions, " +
                     "six brigades and seven battalions.");
                return;
            }

            foreach (var c in roster)
            {
                var captured = c;
                int held = CommanderRegistry.UnitsOf(c).Count;
                bool intact = CommanderRegistry.ChainIntact(c);

                var frame = UIFactory.CreateBorderedPanel(_page, "Cmd_" + c.id,
                    c == _selected ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                    new Vector2(InnerWidth, RowHeight));

                var btn = UIFactory.CreateButton(frame, "",
                    () => { _selected = captured; Rebuild(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, 1);
                UIFactory.Stretch((RectTransform)btn.transform);
                var made = btn.GetComponentInChildren<Text>(true);
                if (made != null) made.gameObject.SetActive(false);

                // A lamp, not a colour on the text: "out of action" has to
                // survive being read on a row that is also using colour for
                // selection.
                var lamp = UIFactory.CreatePanel(frame, "Lamp",
                    !c.active ? UiTheme.Danger : intact ? UiTheme.Success : UiTheme.Warning);
                UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(7, 7));
                lamp.GetComponent<Image>().raycastTarget = false;

                // The face, between the lamp and the name — twenty rows of rank
                // and surname are a spreadsheet; twenty faces are an army.
                Portrait(frame, captured, 20f, (RowHeight - RowPortrait) * 0.5f, RowPortrait);

                float textX = 20f + RowPortrait + 8f;
                var text = UIFactory.CreateText(frame, Short(c), UiTheme.FontSmall,
                    c.active ? UiTheme.Text : UiTheme.TextFaint, TextAnchor.MiddleLeft);
                UIFactory.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(textX, 0),
                    new Vector2(InnerWidth - textX - 46f, 16));
                UIFactory.Fit(text, 8);
                text.raycastTarget = false;

                var count = UIFactory.CreateText(frame, held > 0 ? held.ToString() : "—",
                    UiTheme.FontLabel, held > 0 ? UiTheme.Accent : UiTheme.TextFaint,
                    TextAnchor.MiddleRight);
                UIFactory.Place(count.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                    new Vector2(36, 16));
                count.raycastTarget = false;

                _y += RowHeight + Gap;
            }
        }

        // ------------------------------------------------------------ pieces

        void Label(string text)
        {
            var t = UIFactory.CreateSectionHeader(_page, text);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                new Vector2(InnerWidth, 18));
            UIFactory.Fit(t, 8);
            _y += 22f;
        }

        void Note(string text)
        {
            var t = UIFactory.CreateText(_page, text, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                new Vector2(InnerWidth, 52));
            _y += 56f;
        }

        /// <summary>A caption with ◀ ▶ either side — the cycle control the rail uses everywhere.</summary>
        void Stepper(string caption, System.Action back, System.Action forward)
        {
            var frame = UIFactory.CreateBorderedPanel(_page, "Step_" + caption,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                new Vector2(InnerWidth, 30));

            var left = UIFactory.CreateButton(frame, "◀", () => back(),
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)left.transform, new Vector2(0f, 0.5f),
                new Vector2(2, 0), new Vector2(26, 26));

            var right = UIFactory.CreateButton(frame, "▶", () => forward(),
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)right.transform, new Vector2(1f, 0.5f),
                new Vector2(-2, 0), new Vector2(26, 26));

            var t = UIFactory.CreateText(frame, caption, UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleCenter);
            UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(InnerWidth - 60f, 16));
            UIFactory.Fit(t, 8);

            _y += 30f + Gap;
        }

        void Row(string text, Color colour, System.Action onClick)
        {
            var frame = UIFactory.CreateBorderedPanel(_page, "Row", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -_y),
                new Vector2(InnerWidth, 28));

            var btn = UIFactory.CreateButton(frame, "", () => onClick(),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var t = UIFactory.CreateText(frame, text, UiTheme.FontLabel, colour, TextAnchor.MiddleLeft);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(InnerWidth - 20f, 16));
            UIFactory.Fit(t, 8);
            t.raycastTarget = false;

            _y += 28f + Gap;
        }

        static string Short(CommanderState c) =>
            $"{RankCatalog.Get(c.TeamEnum, c.rank).abbrev} {c.name}";

        static string UnitLabel(UnitActor u) =>
            string.IsNullOrEmpty(u.State.customName)
                ? $"{u.State.EchelonEnum} · {u.Def.name}"
                : u.State.customName;
    }
}
