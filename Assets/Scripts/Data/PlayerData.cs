using System;
using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>Who is playing a side: a person at this machine, or the computer.</summary>
    public enum PlayerKind
    {
        Human,
        Computer
    }

    /// <summary>
    /// How hard the computer plays. Three settings and no more: a slider of
    /// twenty would be twenty numbers nobody could tell apart, and the three
    /// names are what a player actually chooses between.
    ///
    /// The multipliers are held here rather than in the AI because there is no
    /// AI yet — the enemy does not issue orders of its own (see
    /// docs/07-ARCHITECTURE.md). Putting the numbers in the data now means the
    /// difficulty is a real, saved, inspectable property of the player from the
    /// moment the behaviour behind it exists, rather than a setting that has to
    /// be retro-fitted onto one.
    /// </summary>
    public enum Difficulty
    {
        Recruit,
        Regular,
        Veteran
    }

    public static class DifficultyInfo
    {
        public static readonly Difficulty[] All =
        {
            Difficulty.Recruit, Difficulty.Regular, Difficulty.Veteran
        };

        public static string DisplayName(Difficulty d) => d switch
        {
            Difficulty.Recruit => "Recruit",
            Difficulty.Veteran => "Veteran",
            _ => "Regular"
        };

        public static string Blurb(Difficulty d) => d switch
        {
            Difficulty.Recruit => "Fights cautiously and reacts late. The setting to learn a scenario on.",
            Difficulty.Veteran => "Concentrates, counter-attacks and does not waste a formation. Expect to lose ground.",
            _ => "An even fight — no handicap either way."
        };

        /// <summary>
        /// What the computer's formations are worth against a human's, as a
        /// multiplier on combat power. Exactly 1 at Regular, so a scenario
        /// played at the middle setting resolves precisely as it did before
        /// difficulty existed.
        /// </summary>
        public static float CombatMultiplier(Difficulty d) => d switch
        {
            Difficulty.Recruit => 0.85f,
            Difficulty.Veteran => 1.20f,
            _ => 1f
        };
    }

    /// <summary>
    /// One player: a name, whether a person or the computer is behind it, the
    /// difficulty if it is the computer, and which team they belong to.
    ///
    /// Serialisable so it rides in the map file with everything else — see
    /// <see cref="MapSaveData"/>.
    /// </summary>
    [Serializable]
    public class PlayerState
    {
        public string id;
        public string name;
        /// <summary>Team id this player commands. Empty means unassigned.</summary>
        public string teamId;
        /// <summary><see cref="PlayerKind"/> as a string, the way the rest of the save schema stores enums.</summary>
        public string kind = nameof(PlayerKind.Human);
        /// <summary><see cref="Difficulty"/> as a string. Read only for a computer player.</summary>
        public string difficulty = nameof(Difficulty.Regular);

        public PlayerKind KindEnum =>
            Enum.TryParse(kind, out PlayerKind k) ? k : PlayerKind.Human;

        public Difficulty DifficultyEnum =>
            Enum.TryParse(difficulty, out Difficulty d) ? d : Difficulty.Regular;

        public bool IsComputer => KindEnum == PlayerKind.Computer;
    }

    /// <summary>
    /// One side of the fight. A team is what units belong to and what players
    /// command; the two are separate because a side can be shared by several
    /// commanders and a commander can be moved between sides without the units
    /// noticing.
    /// </summary>
    [Serializable]
    public class TeamState
    {
        public string id;
        public string name;
        /// <summary>
        /// Which of the game's two hard sides this team fights on —
        /// <see cref="Team.User"/> or <see cref="Team.Enemy"/>. Everything from
        /// the combat model to the icon set is built on that two-way split, so
        /// a team is a *label over* one of them rather than a third side.
        /// </summary>
        public string side = nameof(Team.User);

        public Team SideEnum => Enum.TryParse(side, out Team t) ? t : Team.User;
    }

    /// <summary>
    /// The roster: who is playing, on which side, and how hard the computer is
    /// trying.
    ///
    /// **Why this exists before there is an AI.** The map editor could already
    /// deploy both sides and fight them against each other, but there was
    /// nothing anywhere that said *who* either side was — so a scenario could
    /// not record that Red is the computer at Veteran and Blue is the person
    /// sitting here. That is the first thing a single-player mission needs to
    /// know and the first thing a multiplayer lobby needs to write down, and
    /// leaving it implicit meant both would have invented their own answer.
    ///
    /// **Two teams and two players by default**, because that is what every
    /// scenario in the game currently is: Blue, played by the user; Red, played
    /// by the computer. <see cref="EnsureDefaults"/> creates exactly that when
    /// a map has no roster of its own, so an old save opens with a sensible one
    /// rather than an empty list.
    ///
    /// Static, like <see cref="Units.CommanderRegistry"/> and for the same
    /// reason: the palette, the save system and (later) the AI all read it and
    /// none of them owns it.
    /// </summary>
    public static class PlayerRegistry
    {
        /// <summary>Team ids for the two sides every scenario starts with.</summary>
        public const string BlueTeamId = "team-blue";
        public const string RedTeamId = "team-red";

        static readonly List<TeamState> _teams = new List<TeamState>();
        static readonly List<PlayerState> _players = new List<PlayerState>();

        public static IReadOnlyList<TeamState> Teams => _teams;
        public static IReadOnlyList<PlayerState> Players => _players;

        /// <summary>Raised on any change, so the panel showing the roster can repaint.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------ teams

        public static TeamState FindTeam(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in _teams) if (t.id == id) return t;
            return null;
        }

        public static string TeamName(string id) => FindTeam(id)?.name ?? "Unassigned";

        public static TeamState AddTeam(string name, Team side)
        {
            var team = new TeamState
            {
                id = NewId("team"),
                name = string.IsNullOrWhiteSpace(name) ? $"Team {_teams.Count + 1}" : name.Trim(),
                side = side.ToString()
            };
            _teams.Add(team);
            Changed?.Invoke();
            return team;
        }

        /// <summary>
        /// Removes a team. Its players are **unassigned rather than deleted** —
        /// losing a player because their team was removed would be destroying
        /// something the player did not ask to destroy, and a roster row with
        /// no team is a visible, fixable state.
        /// </summary>
        public static bool RemoveTeam(string id)
        {
            var team = FindTeam(id);
            if (team == null) return false;

            _teams.Remove(team);
            foreach (var p in _players) if (p.teamId == id) p.teamId = "";
            Changed?.Invoke();
            return true;
        }

        public static void RenameTeam(string id, string name)
        {
            var team = FindTeam(id);
            if (team == null || string.IsNullOrWhiteSpace(name)) return;
            team.name = name.Trim();
            Changed?.Invoke();
        }

        public static void SetTeamSide(string id, Team side)
        {
            var team = FindTeam(id);
            if (team == null) return;
            team.side = side.ToString();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------- players

        public static PlayerState FindPlayer(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in _players) if (p.id == id) return p;
            return null;
        }

        public static PlayerState AddPlayer(string name, PlayerKind kind, string teamId)
        {
            var player = new PlayerState
            {
                id = NewId("player"),
                name = string.IsNullOrWhiteSpace(name)
                    ? (kind == PlayerKind.Computer ? "Computer" : $"Player {_players.Count + 1}")
                    : name.Trim(),
                kind = kind.ToString(),
                teamId = teamId ?? "",
                difficulty = nameof(Difficulty.Regular)
            };
            _players.Add(player);
            Changed?.Invoke();
            return player;
        }

        public static bool RemovePlayer(string id)
        {
            var player = FindPlayer(id);
            if (player == null) return false;
            _players.Remove(player);
            Changed?.Invoke();
            return true;
        }

        public static void SetPlayerTeam(string playerId, string teamId)
        {
            var player = FindPlayer(playerId);
            if (player == null) return;
            player.teamId = teamId ?? "";
            Changed?.Invoke();
        }

        public static void SetPlayerKind(string playerId, PlayerKind kind)
        {
            var player = FindPlayer(playerId);
            if (player == null) return;
            player.kind = kind.ToString();
            Changed?.Invoke();
        }

        public static void SetDifficulty(string playerId, Difficulty difficulty)
        {
            var player = FindPlayer(playerId);
            if (player == null) return;
            player.difficulty = difficulty.ToString();
            Changed?.Invoke();
        }

        public static void RenamePlayer(string playerId, string name)
        {
            var player = FindPlayer(playerId);
            if (player == null || string.IsNullOrWhiteSpace(name)) return;
            player.name = name.Trim();
            Changed?.Invoke();
        }

        /// <summary>Players commanding this side, in roster order.</summary>
        public static List<PlayerState> OfSide(Team side)
        {
            var list = new List<PlayerState>();
            foreach (var p in _players)
            {
                var team = FindTeam(p.teamId);
                if (team != null && team.SideEnum == side) list.Add(p);
            }
            return list;
        }

        // ------------------------------------------------------ persistence

        public static void LoadFrom(List<TeamState> teams, List<PlayerState> players)
        {
            _teams.Clear();
            _players.Clear();
            if (teams != null) _teams.AddRange(teams);
            if (players != null) _players.AddRange(players);
            EnsureDefaults();
            Changed?.Invoke();
        }

        public static List<TeamState> SaveTeams() => new List<TeamState>(_teams);
        public static List<PlayerState> SavePlayers() => new List<PlayerState>(_players);

        public static void Clear()
        {
            _teams.Clear();
            _players.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// The roster every scenario starts with: Blue played by the user, Red
        /// played by the computer at Regular. Only fills in what is missing, so
        /// a map that carries its own roster is left alone and one saved before
        /// this existed opens with the arrangement it was always implicitly
        /// being played under.
        /// </summary>
        public static void EnsureDefaults()
        {
            if (FindTeam(BlueTeamId) == null)
                _teams.Insert(0, new TeamState
                {
                    id = BlueTeamId, name = "Blue Force", side = nameof(Team.User)
                });

            if (FindTeam(RedTeamId) == null)
                _teams.Add(new TeamState
                {
                    id = RedTeamId, name = "Red Force", side = nameof(Team.Enemy)
                });

            if (_players.Count == 0)
            {
                _players.Add(new PlayerState
                {
                    id = NewId("player"), name = "User",
                    kind = nameof(PlayerKind.Human), teamId = BlueTeamId
                });
                _players.Add(new PlayerState
                {
                    id = NewId("player"), name = "Computer",
                    kind = nameof(PlayerKind.Computer), teamId = RedTeamId,
                    difficulty = nameof(Difficulty.Regular)
                });
            }
        }

        static string NewId(string prefix) =>
            prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
