using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// The order of battle above the units: who commands what, on both sides.
    ///
    /// **Why command is worth modelling at all.** Without it a scenario is a bag
    /// of counters that all fight equally well whatever happens to the ones
    /// behind them. With it, a formation belongs to somebody, that somebody
    /// belongs to somebody, and knocking out a headquarters degrades everything
    /// under it — which is the reason armies are shaped like this and the reason
    /// deep strikes are worth flying.
    ///
    /// **Flat list, one parent pointer.** A commander owns formations
    /// (<see cref="UnitState.commanderId"/>) and may own other commanders
    /// (<see cref="CommanderState.superiorId"/>). Storing it as a tree would mean
    /// rebuilding the tree on every reassignment; storing the parent is the same
    /// information and survives a subordinate being deleted.
    ///
    /// **Static, like <see cref="UnitRegistry"/>**, because units are spawned
    /// without knowing which controller owns them and have to be able to ask who
    /// commands them from anywhere. Cleared and reloaded with the map.
    ///
    /// See docs/23-COMMANDERS.md.
    /// </summary>
    public static class CommanderRegistry
    {
        /// <summary>Commanders seeded per side by <see cref="Seed"/>.</summary>
        public const int SeedCount = 20;

        /// <summary>
        /// How much an effective chain of command is worth, at the foot and at
        /// the head of the ladder. A junior officer in post is worth a little; an
        /// army commander with an unbroken chain beneath him is worth a fifth
        /// again. Deliberately modest — command should decide close fights, not
        /// replace the fighting.
        /// </summary>
        const float MinBonus = 1.04f, MaxBonus = 1.20f;

        /// <summary>
        /// What a formation loses when its commander is out of action. Not
        /// nothing, and not catastrophic: a battalion whose brigade headquarters
        /// has been destroyed still fights, just worse and without coordination.
        /// </summary>
        const float LeaderlessPenalty = 0.88f;

        static readonly List<CommanderState> _all = new List<CommanderState>();

        public static IReadOnlyList<CommanderState> All => _all;

        /// <summary>Raised whenever the list or an assignment changes, so panels repaint.</summary>
        public static event System.Action Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        // ------------------------------------------------------------- access

        public static CommanderState Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in _all) if (c.id == id) return c;
            return null;
        }

        public static List<CommanderState> OfTeam(Team team)
        {
            var list = new List<CommanderState>();
            foreach (var c in _all) if (c.TeamEnum == team) list.Add(c);
            // Senior first: the panel reads as a chain of command rather than as
            // the order somebody happened to create them in.
            list.Sort((a, b) =>
            {
                int t = RankCatalog.Get(b.TeamEnum, b.rank).tier
                    .CompareTo(RankCatalog.Get(a.TeamEnum, a.rank).tier);
                return t != 0 ? t : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static int CountOfTeam(Team team)
        {
            int n = 0;
            foreach (var c in _all) if (c.TeamEnum == team) n++;
            return n;
        }

        /// <summary>Formations this officer commands directly.</summary>
        public static List<UnitActor> UnitsOf(CommanderState commander)
        {
            var list = new List<UnitActor>();
            if (commander == null) return list;
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.commanderId == commander.id) list.Add(u);
            return list;
        }

        /// <summary>Officers who report to this one.</summary>
        public static List<CommanderState> SubordinatesOf(CommanderState commander)
        {
            var list = new List<CommanderState>();
            if (commander == null) return list;
            foreach (var c in _all) if (c.superiorId == commander.id) list.Add(c);
            return list;
        }

        // --------------------------------------------------------- the chain

        /// <summary>
        /// True when this officer and every officer above him is in post.
        ///
        /// The walk is bounded rather than recursive. A superior pointer is
        /// editable, and a cycle — A reports to B reports to A — is one mis-click
        /// away; a plain recursion would hang the game rather than degrade the
        /// bonus, which is not a trade worth making for four lines of code.
        /// </summary>
        public static bool ChainIntact(CommanderState commander)
        {
            var walk = commander;
            for (int hop = 0; hop < 16 && walk != null; hop++)
            {
                if (!walk.active) return false;
                if (string.IsNullOrEmpty(walk.superiorId)) return true;
                walk = Get(walk.superiorId);
            }
            // Ran out of hops: treat a cycle as a broken chain. It is the safe
            // reading — an order of battle that eats its own tail is not one
            // anybody is being commanded through.
            return walk == null;
        }

        /// <summary>
        /// Would making <paramref name="superior"/> the boss of
        /// <paramref name="commander"/> create a loop? Checked before the
        /// assignment rather than after, so the list can never hold one.
        /// </summary>
        public static bool WouldCycle(CommanderState commander, CommanderState superior)
        {
            if (commander == null || superior == null) return false;
            if (commander == superior) return true;

            var walk = superior;
            for (int hop = 0; hop < 32 && walk != null; hop++)
            {
                if (walk.id == commander.id) return true;
                if (string.IsNullOrEmpty(walk.superiorId)) return false;
                walk = Get(walk.superiorId);
            }
            return true;
        }

        // --------------------------------------------------- combat effect

        /// <summary>
        /// The multiplier a formation's fire carries because of who commands it.
        ///
        /// Three cases, and each says something:
        ///   • **No commander** — 1.0. Unassigned is neutral, not punished; a
        ///     scenario that has not been given an order of battle must play
        ///     exactly as it did before commanders existed.
        ///   • **Commander in post, chain intact** — a bonus scaled by his rank.
        ///   • **Commander out of action, or a broken chain above him** — a
        ///     penalty. This is what makes a strike on a headquarters worth
        ///     flying.
        /// </summary>
        public static float CommandBonus(UnitActor unit)
        {
            if (unit == null) return 1f;

            var commander = Get(unit.State.commanderId);
            if (commander == null) return 1f;
            if (!ChainIntact(commander)) return LeaderlessPenalty;

            var ladder = RankCatalog.Of(commander.TeamEnum);
            var rank = RankCatalog.Get(commander.TeamEnum, commander.rank);
            float t = ladder.Count <= 1 ? 1f : rank.tier / (float)(ladder.Count - 1);
            return Mathf.Lerp(MinBonus, MaxBonus, t);
        }

        // ------------------------------------------------------- assignment

        /// <summary>Puts a formation under an officer, or under nobody when <paramref name="commander"/> is null.</summary>
        public static void Assign(UnitActor unit, CommanderState commander)
        {
            if (unit == null) return;
            // Command does not cross the line. Assigning a Red battalion to a
            // NATO colonel would be a data error that quietly changed a fight.
            if (commander != null && commander.TeamEnum != unit.State.TeamEnum) return;

            unit.State.commanderId = commander?.id ?? "";
        }

        /// <summary>Releases every formation this officer holds.</summary>
        public static int ClearAssignments(CommanderState commander)
        {
            if (commander == null) return 0;
            int n = 0;
            foreach (var u in UnitRegistry.All)
                if (u != null && u.State.commanderId == commander.id) { u.State.commanderId = ""; n++; }
            return n;
        }

        // ---------------------------------------------------- list editing

        public static CommanderState Add(Team team, string name, string rank)
        {
            var c = new CommanderState
            {
                id = System.Guid.NewGuid().ToString("N").Substring(0, 10),
                team = team.ToString(),
                name = name,
                rank = rank,
                // Rolled once, here, and saved with him. Picking a face when the
                // panel draws one would give an officer a new head every time a
                // row was rebuilt — see CommanderPortraits.
                portrait = CommanderPortraits.Pick()
            };
            _all.Add(c);
            Changed?.Invoke();
            return c;
        }

        /// <summary>
        /// Removes an officer. His formations are released and his subordinates
        /// are promoted to his superior rather than orphaned — an army does not
        /// stop having a chain of command because one headquarters was struck,
        /// and leaving dangling pointers would break <see cref="ChainIntact"/>
        /// for everybody underneath.
        /// </summary>
        public static bool Remove(CommanderState commander)
        {
            if (commander == null || !_all.Contains(commander)) return false;

            ClearAssignments(commander);
            foreach (var c in _all)
                if (c.superiorId == commander.id) c.superiorId = commander.superiorId;

            _all.Remove(commander);
            Changed?.Invoke();
            return true;
        }

        public static void Clear(Team team)
        {
            var doomed = OfTeam(team);
            foreach (var c in doomed) Remove(c);
            Changed?.Invoke();
        }

        public static void ClearAll()
        {
            foreach (var u in UnitRegistry.All) if (u != null) u.State.commanderId = "";
            _all.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------- save

        public static List<CommanderState> Serialize()
        {
            var copy = new List<CommanderState>(_all.Count);
            foreach (var c in _all) copy.Add(c.Clone());
            return copy;
        }

        public static void LoadFrom(List<CommanderState> saved)
        {
            _all.Clear();
            if (saved != null)
                foreach (var c in saved)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    if (string.IsNullOrEmpty(c.rank))
                        c.rank = RankCatalog.Of(c.TeamEnum)[0].name;
                    _all.Add(c);
                }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------- seed

        /// <summary>
        /// Builds a plausible chain of command for one side: twenty officers in a
        /// pyramid, senior at the top, each reporting to one above.
        ///
        /// A flat list of twenty peers would be twenty rows and no structure —
        /// the whole point is the chain, so the seed makes one. The shape is a
        /// real one: one army commander, two corps, four divisions, six brigades,
        /// seven battalions.
        /// </summary>
        public static void Seed(Team team, bool replace = true)
        {
            if (replace) Clear(team);

            var ladder = RankCatalog.Of(team);
            var names = team == Team.Enemy ? EnemyNames : NatoNames;

            // (how many at this level, how far down the ladder from the top)
            (int count, int fromTop)[] tiers =
            {
                (1, 0),   // army / front
                (2, 1),   // corps
                (4, 2),   // division
                (6, 3),   // brigade / regiment
                (7, 4)    // battalion
            };

            var previousLevel = new List<CommanderState>();
            int nameCursor = 0;

            foreach (var (count, fromTop) in tiers)
            {
                var level = new List<CommanderState>(count);
                int tier = Mathf.Clamp(ladder.Count - 1 - fromTop, 0, ladder.Count - 1);

                for (int i = 0; i < count; i++)
                {
                    string name = names[nameCursor % names.Length];
                    nameCursor++;

                    var c = Add(team, name, ladder[tier].name);
                    // Spread subordinates evenly across the level above rather
                    // than hanging them all off its first officer.
                    if (previousLevel.Count > 0)
                        c.superiorId = previousLevel[i % previousLevel.Count].id;
                    level.Add(c);
                }

                previousLevel = level;
            }

            Changed?.Invoke();
        }

        // Surnames only. A full name would be two thirds of a 250 px row spent on
        // a first name nobody refers to an officer by.
        static readonly string[] NatoNames =
        {
            "Whitfield", "Ashcombe", "Draycott", "Halloran", "Merrick",
            "Ferrers", "Lonsdale", "Rutherford", "Calloway", "Brandt",
            "Sinclair", "Ainsworth", "Vandermeer", "Okonkwo", "Larsen",
            "Petersen", "Mackenzie", "Delacroix", "Ryder", "Northcott"
        };

        static readonly string[] EnemyNames =
        {
            "Volkov", "Zaytsev", "Morozov", "Baranov", "Kalinin",
            "Rybakov", "Shvedov", "Tarasov", "Yefimov", "Gorbunov",
            "Lebedev", "Sokolov", "Panteleyev", "Dorokhin", "Vasilenko",
            "Kuznetsov", "Ostapenko", "Bortnik", "Zhilin", "Nesterov"
        };
    }
}
