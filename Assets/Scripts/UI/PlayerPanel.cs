using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// The map editor's PLAYERS section: who is fighting this scenario, on
    /// which side, and how hard the computer is trying.
    ///
    /// **Why it is a section and not a lobby screen.** Who plays which side is
    /// a property of the *scenario*, in the same way its weather and its H-hour
    /// are — it is saved with the map, and it is decided while the map is being
    /// laid out rather than in a menu somewhere else. A lobby is what you need
    /// when two people are joining from different machines; a roster is what
    /// you need to author a scenario, and this is the authoring tool.
    ///
    /// **Teams and players are separate things.** A team is a side of the
    /// fight, and units belong to it; a player is who commands one, and can be
    /// moved between teams without anything on the map noticing. Collapsing the
    /// two would have made "add a second commander to Blue" impossible to
    /// express.
    ///
    /// Built by <see cref="UnitPaletteUI"/> into its section panel, in the same
    /// way <see cref="CommanderPanel"/> is: this file is a small application of
    /// its own, and inlining it would have been another two hundred lines in a
    /// file that is already the longest in the project.
    ///
    /// See docs/25-PLAYERS.md.
    /// </summary>
    public class PlayerPanel
    {
        /// <summary>Status line, wired to the HUD's flash.</summary>
        public System.Action<string> Flash;

        const float Pad = UiTheme.PanelPadding;
        const float Inner = UiTheme.SectionPanelWidth - Pad * 2f;
        const float PlayerRowHeight = 74f;
        const float TeamRowHeight = 44f;

        /// <summary>
        /// Left inset of everything inside the scrolling list, measured from the
        /// list's own padded edge.
        ///
        /// It matches the inset a team's name field and a player's glyph take
        /// inside their bordered rows, so the TEAMS and PLAYERS headings line up
        /// with the things they head instead of standing ten pixels out from
        /// them. A heading that does not share an edge with its list reads as
        /// belonging to the panel rather than to the rows.
        /// </summary>
        const float RowInset = 12f;

        readonly RectTransform _content;
        RectTransform _list;
        Text _summary;

        /// <summary>Which player's row is expanded to show its controls; null for none.</summary>
        string _openPlayerId;

        public PlayerPanel(RectTransform content) => _content = content;

        // ------------------------------------------------------------- build

        public void Build()
        {
            PlayerRegistry.EnsureDefaults();

            var title = UIFactory.CreateSectionHeader(_content, "WHO IS FIGHTING");
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -8),
                new Vector2(Inner, 18));

            _summary = UIFactory.CreateText(_content, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(_summary.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -28),
                new Vector2(Inner, 30));

            float y = -62f;

            ActionButton("ADD PLAYER", 0f, Inner * 0.5f - 3f, y, () =>
            {
                var player = PlayerRegistry.AddPlayer(null, PlayerKind.Human, DefaultTeamId());
                _openPlayerId = player.id;
                Flash?.Invoke($"Added {player.name}.");
                Rebuild();
            });

            ActionButton("ADD COMPUTER", Inner * 0.5f + 3f, Inner * 0.5f - 3f, y, () =>
            {
                var player = PlayerRegistry.AddPlayer(null, PlayerKind.Computer, DefaultTeamId(hostile: true));
                _openPlayerId = player.id;
                Flash?.Invoke($"Added {player.name} at {DifficultyInfo.DisplayName(Difficulty.Regular)}.");
                Rebuild();
            });

            y -= 36f;

            ActionButton("ADD TEAM", 0f, Inner * 0.5f - 3f, y, () =>
            {
                // A new team joins whichever side has fewer, so adding one is
                // not silently always another Blue.
                var team = PlayerRegistry.AddTeam(null, LeanerSide());
                Flash?.Invoke($"Added {team.name} on the {(team.SideEnum == Team.User ? "friendly" : "hostile")} side.");
                Rebuild();
            });

            var hint = UIFactory.CreateText(_content,
                "A TEAM is a side of the fight and units belong to it; a PLAYER commands one and can " +
                "be moved between them. Removing a team leaves its players unassigned rather than " +
                "deleting them. Everything here is saved with the map.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, y - 40f), new Vector2(Inner, 74));

            var scroll = UIFactory.CreateScrollView(_content, out _list, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 2);
            srt.offsetMax = new Vector2(0, y - 120f);

            var layout = _list.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset((int)Pad, (int)Pad, 4, 8);

            PlayerRegistry.Changed += Rebuild;
            Rebuild();
        }

        /// <summary>Detaches from the registry. Called when the editor's chrome is torn down.</summary>
        public void Dispose() => PlayerRegistry.Changed -= Rebuild;

        void ActionButton(string label, float x, float width, float y,
            UnityEngine.Events.UnityAction action)
        {
            var frame = UIFactory.CreateBorderedPanel(_content, "Act_" + label,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + x, y), new Vector2(width, 30));

            var btn = UIFactory.CreateButton(frame, label, action,
                new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);
            UIFactory.Fit(btn.GetComponentInChildren<Text>(), 8);
        }

        // ----------------------------------------------------------- content

        public void Rebuild()
        {
            if (_list == null) return;

            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var child = _list.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            int humans = 0, computers = 0;
            foreach (var p in PlayerRegistry.Players)
            {
                if (p.IsComputer) computers++; else humans++;
            }

            _summary.text =
                $"{PlayerRegistry.Teams.Count} team(s)  ·  {humans} human, {computers} computer";

            Header("TEAMS");
            foreach (var team in PlayerRegistry.Teams) TeamRow(team);

            Header("PLAYERS");
            if (PlayerRegistry.Players.Count == 0)
            {
                // Indented to the same edge as the headings and the rows, so
                // the empty state sits in the list rather than beside it.
                var emptyRow = UIFactory.CreateGroup(_list, "NoPlayers");
                emptyRow.sizeDelta = new Vector2(0, 34);

                var none = UIFactory.CreateText(emptyRow, "Nobody is playing this scenario yet.",
                    UiTheme.FontSmall, UiTheme.TextFaint, TextAnchor.UpperLeft);
                UIFactory.PlaceTopLeft(none.rectTransform, RowInset, 4f,
                    Inner - RowInset - 8f, 26f);
                return;
            }
            foreach (var player in PlayerRegistry.Players) PlayerRow(player);
        }

        void Header(string label)
        {
            var row = UIFactory.CreateGroup(_list, "Head_" + label);
            row.sizeDelta = new Vector2(0, 26);

            var text = UIFactory.CreateSectionHeader(row, label, UiTheme.Accent);
            UIFactory.PlaceTopLeft(text.rectTransform, RowInset, 8f,
                Inner - RowInset - 8f, 14f);
        }

        /// <summary>A team: name, which side it fights on, and a way off the board.</summary>
        void TeamRow(TeamState team)
        {
            var row = UIFactory.CreateBorderedPanel(_list, "Team_" + team.id,
                UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, TeamRowHeight);

            // Side reads as a stripe down the row's left edge, the same device
            // the deployed-units list uses for the same information.
            var stripe = UIFactory.CreatePanel(row, "SideStripe",
                team.SideEnum == Team.User ? UiTheme.Friendly : UiTheme.Hostile);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = new Vector2(1, 0);
            stripe.sizeDelta = new Vector2(3, -2);
            stripe.GetComponent<Image>().raycastTarget = false;

            var name = UIFactory.CreateInputField(row, "Team name…", UiTheme.FontSmall);
            var nrt = (RectTransform)name.transform;
            UIFactory.Place(nrt, new Vector2(0f, 1f), new Vector2(12, -6), new Vector2(Inner - 76f, 22));
            name.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            name.text = team.name;
            name.onEndEdit.AddListener(v => PlayerRegistry.RenameTeam(team.id, v));

            var remove = UIFactory.CreateButton(row, "✕", () =>
            {
                PlayerRegistry.RemoveTeam(team.id);
                Flash?.Invoke($"Removed {team.name}. Its players are unassigned.");
            }, new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 12);
            UIFactory.Place((RectTransform)remove.transform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(24, 22));

            // Which of the game's two hard sides this team fights on. Everything
            // from the combat model to the icon set is built on that split, so a
            // team is a label over one of them rather than a third side.
            SideToggle(row, team);
        }

        void SideToggle(RectTransform row, TeamState team)
        {
            float half = (Inner - 36f) / 2f;

            SideButton(row, team, Team.User, "FRIENDLY", 12f, half);
            SideButton(row, team, Team.Enemy, "HOSTILE", 12f + half + 4f, half);
        }

        void SideButton(RectTransform row, TeamState team, Team side, string label, float x, float width)
        {
            bool on = team.SideEnum == side;
            var frame = UIFactory.CreateBorderedPanel(row, "Side_" + side,
                on ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 0f), new Vector2(x, 6), new Vector2(width, 16));

            var btn = UIFactory.CreateButton(frame, label,
                () => PlayerRegistry.SetTeamSide(team.id, side),
                new Color(0, 0, 0, 0), on ? UiTheme.Accent : UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Stretch((RectTransform)btn.transform);
            UIFactory.Fit(btn.GetComponentInChildren<Text>(), 7);
        }

        /// <summary>
        /// A player: name, human or computer, which team, and — for a computer
        /// — the difficulty.
        ///
        /// The controls are folded away behind a click on the row rather than
        /// shown for every player at once. Six rows each carrying a team picker
        /// and three difficulty buttons is a wall; the row a player is actually
        /// editing is the only one that needs them.
        /// </summary>
        void PlayerRow(PlayerState player)
        {
            bool open = _openPlayerId == player.id;
            var team = PlayerRegistry.FindTeam(player.teamId);

            var row = UIFactory.CreateBorderedPanel(_list, "Player_" + player.id,
                open ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            // A computer's unfolded row carries a difficulty block the human's
            // does not, so the two open to different heights rather than the
            // human one leaving a hole where three buttons would have been.
            float openExtra = player.IsComputer ? 92f : 70f;
            row.sizeDelta = new Vector2(0, open ? PlayerRowHeight + openExtra : PlayerRowHeight);

            var stripe = UIFactory.CreatePanel(row, "SideStripe",
                team == null ? UiTheme.TextFaint
                : team.SideEnum == Team.User ? UiTheme.Friendly : UiTheme.Hostile);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = new Vector2(1, 0);
            stripe.sizeDelta = new Vector2(3, -2);
            stripe.GetComponent<Image>().raycastTarget = false;

            var glyph = UIFactory.CreateImage(row,
                player.IsComputer ? UiIcons.Gear : UiIcons.Person, "Kind");
            glyph.color = player.IsComputer ? UiTheme.Warning : UiTheme.Accent;
            glyph.raycastTarget = false;
            UIFactory.Place((RectTransform)glyph.transform, new Vector2(0f, 1f),
                new Vector2(12, -10), new Vector2(18, 18));

            var name = UIFactory.CreateInputField(row, "Player name…", UiTheme.FontSmall);
            var nrt = (RectTransform)name.transform;
            UIFactory.Place(nrt, new Vector2(0f, 1f), new Vector2(36, -8), new Vector2(Inner - 100f, 22));
            name.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            name.text = player.name;
            name.onEndEdit.AddListener(v => PlayerRegistry.RenamePlayer(player.id, v));

            var remove = UIFactory.CreateButton(row, "✕", () =>
            {
                PlayerRegistry.RemovePlayer(player.id);
                Flash?.Invoke($"Removed {player.name}.");
            }, new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 12);
            UIFactory.Place((RectTransform)remove.transform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(24, 22));

            string detail = player.IsComputer
                ? $"Computer  ·  {DifficultyInfo.DisplayName(player.DifficultyEnum)}  ·  {PlayerRegistry.TeamName(player.teamId)}"
                : $"Human  ·  {PlayerRegistry.TeamName(player.teamId)}";

            var sub = UIFactory.CreateText(row, detail, UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(36, -32),
                new Vector2(Inner - 60f, 15));
            UIFactory.Fit(sub, 8);

            var edit = UIFactory.CreateButton(row, open ? "DONE" : "EDIT", () =>
            {
                _openPlayerId = open ? null : player.id;
                Rebuild();
            }, UiTheme.Surface, open ? UiTheme.Accent : UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)edit.transform, new Vector2(0f, 1f),
                new Vector2(36, -50), new Vector2(70, 20));

            var kindBtn = UIFactory.CreateButton(row,
                player.IsComputer ? "MAKE HUMAN" : "MAKE COMPUTER",
                () => PlayerRegistry.SetPlayerKind(player.id,
                    player.IsComputer ? PlayerKind.Human : PlayerKind.Computer),
                UiTheme.Surface, UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.Place((RectTransform)kindBtn.transform, new Vector2(0f, 1f),
                new Vector2(112, -50), new Vector2(Inner - 128f, 20));
            UIFactory.Fit(kindBtn.GetComponentInChildren<Text>(), 7);

            if (!open) return;

            float y = -76f;

            // --- team ---
            var teamLabel = UIFactory.CreateSectionHeader(row, "TEAM", UiTheme.TextFaint);
            UIFactory.Place(teamLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12, y),
                new Vector2(Inner - 24f, 12));
            y -= 16f;

            int count = Mathf.Max(1, PlayerRegistry.Teams.Count);
            float w = (Inner - 24f - (count - 1) * 4f) / count;
            for (int i = 0; i < PlayerRegistry.Teams.Count; i++)
            {
                var t = PlayerRegistry.Teams[i];
                bool on = t.id == player.teamId;
                var frame = UIFactory.CreateBorderedPanel(row, "PT_" + t.id,
                    on ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(12 + i * (w + 4f), y),
                    new Vector2(w, 20));

                var btn = UIFactory.CreateButton(frame, t.name,
                    () => PlayerRegistry.SetPlayerTeam(player.id, t.id),
                    new Color(0, 0, 0, 0), on ? UiTheme.Accent : UiTheme.TextDim, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 7);
            }
            y -= 26f;

            if (!player.IsComputer)
            {
                var note = UIFactory.CreateText(row,
                    "A human player has no difficulty — they are the difficulty.",
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
                UIFactory.Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(12, y),
                    new Vector2(Inner - 24f, 22));
                return;
            }

            // --- difficulty ---
            var diffLabel = UIFactory.CreateSectionHeader(row, "DIFFICULTY", UiTheme.TextFaint);
            UIFactory.Place(diffLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12, y),
                new Vector2(Inner - 24f, 12));
            y -= 16f;

            float dw = (Inner - 24f - 8f) / 3f;
            for (int i = 0; i < DifficultyInfo.All.Length; i++)
            {
                var d = DifficultyInfo.All[i];
                bool on = player.DifficultyEnum == d;
                var frame = UIFactory.CreateBorderedPanel(row, "Diff_" + d,
                    on ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(12 + i * (dw + 4f), y),
                    new Vector2(dw, 20));

                var btn = UIFactory.CreateButton(frame, DifficultyInfo.DisplayName(d).ToUpperInvariant(),
                    () =>
                    {
                        PlayerRegistry.SetDifficulty(player.id, d);
                        Flash?.Invoke($"{player.name}: {DifficultyInfo.Blurb(d)}");
                    },
                    new Color(0, 0, 0, 0), on ? UiTheme.Accent : UiTheme.TextDim, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);
                UIFactory.Fit(btn.GetComponentInChildren<Text>(), 7);

                UiTooltip.Attach(btn.gameObject, DifficultyInfo.Blurb(d), UiTooltip.Side.Left);
            }
        }

        // ------------------------------------------------------------ helpers

        /// <summary>The team a newly added player joins.</summary>
        static string DefaultTeamId(bool hostile = false)
        {
            foreach (var t in PlayerRegistry.Teams)
                if ((t.SideEnum == Team.Enemy) == hostile) return t.id;
            return PlayerRegistry.Teams.Count > 0 ? PlayerRegistry.Teams[0].id : "";
        }

        /// <summary>Whichever side has fewer teams, so ADD TEAM alternates rather than piling up on Blue.</summary>
        static Team LeanerSide()
        {
            int blue = 0, red = 0;
            foreach (var t in PlayerRegistry.Teams)
            {
                if (t.SideEnum == Team.Enemy) red++; else blue++;
            }
            return red < blue ? Team.Enemy : Team.User;
        }

        static void Destroy(Object o) => Object.Destroy(o);
    }
}
