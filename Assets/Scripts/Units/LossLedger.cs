using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// What the battle has cost each side, kept as it happens.
    ///
    /// **Why a ledger and not a count of the survivors.** The obvious way to
    /// answer "what have I lost?" is to compare the units on the map now with
    /// the ones in the save file. That fails the moment anything is reinforced,
    /// deployed mid-battle or removed in the editor, and it cannot say anything
    /// at all about the formations that are still alive but half gone —
    /// which is where most of the casualties in a fight actually are. So losses
    /// are recorded at the moment they are inflicted: every point of strength
    /// taken off a formation is booked here as men, whether or not the
    /// formation survives it.
    ///
    /// **Two numbers per row, and they mean different things.** *Formations* is
    /// how many counters have been destroyed outright — the operational cost,
    /// what the player has stopped being able to command. *Personnel* is the
    /// manpower behind the strength that has been lost, across destroyed and
    /// surviving formations alike — the human cost, and the one that keeps
    /// rising while nothing on the map appears to be happening.
    ///
    /// The ledger is **static and per-scenario**: it is cleared when the map is
    /// reloaded or reset (see <c>GameController.ClearMapContents</c>), because a
    /// casualty list carried over from the last scenario is worse than none.
    ///
    /// Read by <see cref="UI.LossesDialog"/> — TAB in battle mode.
    /// </summary>
    public static class LossLedger
    {
        /// <summary>One unit type's losses on one side.</summary>
        public class Row
        {
            public Team team;
            public string defId;
            public string name;
            /// <summary>Counters destroyed outright.</summary>
            public int formations;
            /// <summary>Men behind the strength lost, destroyed and surviving formations alike.</summary>
            public float personnel;
        }

        static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

        /// <summary>Raised whenever anything is booked, so an open dialog can refresh.</summary>
        public static event System.Action Changed;

        static string Key(Team team, string defId) => (int)team + "/" + defId;

        // ------------------------------------------------------------ writing

        /// <summary>
        /// Books the men behind a loss of strength.
        ///
        /// <paramref name="strengthLost"/> is the fraction actually removed, not
        /// the damage asked for: a guaranteed kill is dealt as a damage value
        /// far above 1 (see <see cref="BlastDamage"/>), and booking that at face
        /// value would report a battalion of eight hundred as having lost
        /// sixteen hundred men.
        /// </summary>
        public static void RecordAttrition(UnitActor unit, float strengthLost)
        {
            if (unit == null || strengthLost <= 0f) return;

            var row = RowFor(unit);
            row.personnel += FullStrengthManpower(unit) * Mathf.Clamp01(strengthLost);
            Changed?.Invoke();
        }

        /// <summary>Books a formation destroyed. Its people are already booked by <see cref="RecordAttrition"/>.</summary>
        public static void RecordDestroyed(UnitActor unit)
        {
            if (unit == null) return;
            RowFor(unit).formations++;
            Changed?.Invoke();
        }

        static Row RowFor(UnitActor unit)
        {
            string key = Key(unit.State.TeamEnum, unit.Def.id);
            if (_rows.TryGetValue(key, out var row)) return row;

            row = new Row
            {
                team = unit.State.TeamEnum,
                defId = unit.Def.id,
                name = unit.Def.name
            };
            _rows[key] = row;
            return row;
        }

        static float FullStrengthManpower(UnitActor unit) =>
            unit.Def.manpower * EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum);

        /// <summary>Wipes the ledger — a new or reloaded scenario starts at nil.</summary>
        public static void Clear()
        {
            if (_rows.Count == 0) return;
            _rows.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ reading

        /// <summary>Has anything at all been lost yet?</summary>
        public static bool Any => _rows.Count > 0;

        /// <summary>
        /// One side's rows, heaviest first. Sorted by formations lost and then
        /// by people, so the line that matters operationally leads and ties
        /// break on the line that matters humanly.
        /// </summary>
        public static List<Row> For(Team team)
        {
            var list = new List<Row>();
            foreach (var row in _rows.Values)
                if (row.team == team && (row.formations > 0 || row.personnel >= 0.5f))
                    list.Add(row);

            list.Sort((a, b) =>
            {
                int c = b.formations.CompareTo(a.formations);
                if (c != 0) return c;
                c = b.personnel.CompareTo(a.personnel);
                if (c != 0) return c;
                return string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>Totals for one side: counters destroyed and men lost.</summary>
        public static (int formations, float personnel) Total(Team team)
        {
            int formations = 0;
            float personnel = 0f;
            foreach (var row in _rows.Values)
            {
                if (row.team != team) continue;
                formations += row.formations;
                personnel += row.personnel;
            }
            return (formations, personnel);
        }

        /// <summary>
        /// Formations of this side still on the map. Not part of the ledger —
        /// it is read live from the registry — but it belongs beside the losses,
        /// because "eleven destroyed" means nothing without "of thirty-four".
        /// </summary>
        public static int Surviving(Team team)
        {
            int count = 0;
            foreach (var unit in UnitRegistry.OfTeam(team))
                if (unit != null && unit.IsAlive) count++;
            return count;
        }
    }
}
