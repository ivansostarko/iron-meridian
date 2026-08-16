using System;
using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>
    /// One rank on one side's ladder.
    ///
    /// **Two ladders, not one shared enum.** NATO and the enemy do not have the
    /// same ranks, and folding them into one list would either invent a
    /// correspondence that does not exist (a Polkovnik is not quite a Colonel)
    /// or force one side to wear the other's insignia. Each side gets its own
    /// ordered ladder, and a commander's rank is stored as the rank's **name**
    /// so a saved order of battle survives a ladder gaining an entry.
    /// </summary>
    public class RankDef
    {
        public Team team;
        /// <summary>Full name as it appears in the panel.</summary>
        public string name;
        /// <summary>Short form for a list row — "LTC", "Polk".</summary>
        public string abbrev;
        /// <summary>
        /// The echelon this rank typically commands. Used to seed a plausible
        /// chain of command and to suggest a rank when one is not chosen.
        /// </summary>
        public Echelon commands;
        /// <summary>Position on the ladder, ascending. Seniority is a comparison of these.</summary>
        public int tier;
    }

    /// <summary>
    /// The two rank ladders. Ordered junior to senior; <see cref="RankDef.tier"/>
    /// is the index, so seniority is a plain integer comparison and the chain of
    /// command can be checked without knowing which side it belongs to.
    /// </summary>
    public static class RankCatalog
    {
        static readonly (string name, string abbrev, Echelon commands)[] Nato =
        {
            ("Lieutenant",          "LT",   Echelon.Platoon),
            ("Captain",             "CPT",  Echelon.Company),
            ("Major",               "MAJ",  Echelon.Company),
            ("Lieutenant Colonel",  "LTC",  Echelon.Battalion),
            ("Colonel",             "COL",  Echelon.Regiment),
            ("Brigadier General",   "BG",   Echelon.Brigade),
            ("Major General",       "MG",   Echelon.Division),
            ("Lieutenant General",  "LTG",  Echelon.Corps),
            ("General",             "GEN",  Echelon.Army)
        };

        /// <summary>
        /// Soviet-pattern ladder, transliterated rather than translated. A
        /// "General-Mayor" is not a Major General in any sense a player should be
        /// asked to reconcile, and giving the enemy its own words is most of what
        /// makes the two orders of battle read as two armies.
        /// </summary>
        static readonly (string name, string abbrev, Echelon commands)[] Enemy =
        {
            ("Leytenant",           "Lt",    Echelon.Platoon),
            ("Starshiy Leytenant",  "StLt",  Echelon.Platoon),
            ("Kapitan",             "Kpt",   Echelon.Company),
            ("Mayor",               "Mjr",   Echelon.Company),
            ("Podpolkovnik",        "PPolk", Echelon.Battalion),
            ("Polkovnik",           "Polk",  Echelon.Regiment),
            ("General-Mayor",       "GenM",  Echelon.Division),
            ("General-Leytenant",   "GenL",  Echelon.Corps),
            ("General-Polkovnik",   "GenP",  Echelon.Corps),
            ("General Armii",       "GenA",  Echelon.Army)
        };

        static RankDef[] _nato, _enemy;

        public static IReadOnlyList<RankDef> Of(Team team)
        {
            Build();
            return team == Team.Enemy ? _enemy : _nato;
        }

        static void Build()
        {
            if (_nato != null) return;
            _nato = Make(Team.User, Nato);
            _enemy = Make(Team.Enemy, Enemy);
        }

        static RankDef[] Make(Team team, (string name, string abbrev, Echelon commands)[] rows)
        {
            var list = new RankDef[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                list[i] = new RankDef
                {
                    team = team,
                    name = rows[i].name,
                    abbrev = rows[i].abbrev,
                    commands = rows[i].commands,
                    tier = i
                };
            return list;
        }

        /// <summary>
        /// A rank by name on a given side. Falls back to the ladder's foot rather
        /// than returning null: a saved commander whose rank was renamed should
        /// come back as a junior officer, not as a crash.
        /// </summary>
        public static RankDef Get(Team team, string name)
        {
            var ladder = Of(team);
            foreach (var r in ladder) if (r.name == name) return r;
            return ladder[0];
        }

        /// <summary>The most junior rank that would command this echelon.</summary>
        public static RankDef ForEchelon(Team team, Echelon echelon)
        {
            var ladder = Of(team);
            foreach (var r in ladder) if (r.commands >= echelon) return r;
            return ladder[ladder.Count - 1];
        }

        /// <summary>Steps a rank up or down its own ladder, clamped at both ends.</summary>
        public static RankDef Step(Team team, string current, int by)
        {
            var ladder = Of(team);
            int i = Get(team, current).tier + by;
            return ladder[Math.Max(0, Math.Min(ladder.Count - 1, i))];
        }
    }

    /// <summary>
    /// One officer in an order of battle.
    ///
    /// **A commander is a record, not a unit.** He is not on the map, cannot be
    /// shot at and occupies no ground; what he does is *own* formations, and own
    /// other commanders. That is why this is a flat list with a
    /// <see cref="superiorId"/> rather than a tree: a tree would have to be
    /// rebuilt every time somebody was reassigned, and a flat list with one
    /// parent pointer is the same information with none of that.
    ///
    /// Fields added later default harmlessly on old saves — `JsonUtility` leaves
    /// missing fields at their initialiser values.
    /// </summary>
    [Serializable]
    public class CommanderState
    {
        /// <summary>Stable id, referenced by <see cref="UnitState.commanderId"/> and by subordinates.</summary>
        public string id = "";
        /// <summary>"User" | "Enemy". A commander never crosses sides.</summary>
        public string team = Team.User.ToString();
        /// <summary>Surname as the panel shows it.</summary>
        public string name = "";
        /// <summary>A <see cref="RankDef.name"/> on this side's ladder.</summary>
        public string rank = "";

        /// <summary>
        /// Whether he is in post. A disabled commander keeps his formations and
        /// his place in the chain — he is simply not exercising command, and
        /// everything under him loses its command bonus. That is the point of
        /// the switch: it models a headquarters being knocked out without
        /// deleting the order of battle that would have to be rebuilt afterwards.
        /// </summary>
        public bool active = true;

        /// <summary>Immediate superior, or "" for the top of the chain.</summary>
        public string superiorId = "";

        /// <summary>
        /// Which of his side's photographs he wears — an index into
        /// <see cref="CommanderPortraits"/>, picked once when he is created and
        /// saved with him so the same face comes back.
        ///
        /// **-1 means "never chosen"**, which is what every roster saved before
        /// portraits existed deserializes to. It is not a missing face: the
        /// catalogue derives one from his id instead, so an old scenario opens
        /// with a spread of faces that are stable across loads.
        /// </summary>
        public int portrait = -1;

        public Team TeamEnum => team == Team.Enemy.ToString() ? Team.Enemy : Team.User;

        public CommanderState Clone() => new CommanderState
        {
            id = id, team = team, name = name, rank = rank,
            active = active, superiorId = superiorId, portrait = portrait
        };
    }
}
