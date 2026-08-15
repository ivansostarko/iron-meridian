using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// How many missions a scenario has left **of each delivery system** —
    /// two B-2 sorties, twenty 81 mm fire missions, one DF-26.
    ///
    /// **Why per type and not one pool.** A single shared allowance made every
    /// strike cost the same thing, which was the next strike — so the choice
    /// between a 60 mm mortar mission and an Iskander was free, and the only
    /// rational play was to spend the pool on whatever was biggest. That is the
    /// opposite of the decision the fire menus are meant to present. What makes
    /// a heavy weapon a real choice is that there are **two of them**: an
    /// allowance attached to the system itself says what is scarce and what is
    /// plentiful, and it says it on the button, at the moment of choosing,
    /// without a word of explanation.
    ///
    /// It also puts the number where the rest of a weapon's numbers already
    /// live. <c>missions</c> is a field on the catalogue row beside the beaten
    /// zone and the countdown, so it is visible in **Development → Units List**
    /// and tunable there like any other stat — see docs/04-UNITS.md.
    ///
    /// **This class only counts.** It does not know the limits; the caller
    /// passes each system's own <c>missions</c> figure in with the key. That is
    /// deliberate — the catalogues are the single source of truth for what a
    /// weapon can do, and a second copy of the limits here would be a second
    /// thing to keep in step. The count is consumed when a mission is *placed*,
    /// not when it lands: a mission cannot be recalled once away, so that is the
    /// moment the player spent it.
    ///
    /// Static because the five strike systems and every panel showing an
    /// allowance read it and none of them owns it. It is scenario state, so
    /// <see cref="Reset"/> is called when the editor is reset or a scene starts
    /// — static fields survive a scene load, and a fresh map opening with a
    /// spent bomber would be a bug nobody could explain.
    /// </summary>
    public static class StrikeBudget
    {
        /// <summary>Missions flown, per system key. Absent means none.</summary>
        static readonly Dictionary<string, int> _used = new Dictionary<string, int>();

        /// <summary>Raised whenever a count moves, so every panel showing one can repaint.</summary>
        public static event System.Action Changed;

        /// <summary>
        /// The key one delivery system is counted under. Prefixed by family
        /// because the five catalogues have five unrelated enums and
        /// <c>Medium</c> means something different in each.
        /// </summary>
        public static string Key(string family, object id) => family + "/" + id;

        public static int UsedOf(string key) =>
            key != null && _used.TryGetValue(key, out int n) ? n : 0;

        public static int RemainingOf(string key, int limit) =>
            Mathf.Max(0, limit - UsedOf(key));

        public static bool IsExhausted(string key, int limit) => UsedOf(key) >= limit;

        /// <summary>
        /// Spends one mission of this system. Returns false when it has none
        /// left, in which case nothing was spent and the caller must not fire.
        /// </summary>
        public static bool TryConsume(string key, int limit)
        {
            if (key == null || IsExhausted(key, limit)) return false;
            _used[key] = UsedOf(key) + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Back to full allowances. Called when a scenario starts or is reset.</summary>
        public static void Reset()
        {
            if (_used.Count == 0) return;
            _used.Clear();
            Changed?.Invoke();
        }

        /// <summary>Readout for a button: "3 / 6".</summary>
        public static string RemainingText(string key, int limit) =>
            $"{RemainingOf(key, limit)} / {limit}";

        /// <summary>
        /// Turns amber as a system runs down and red once it is gone, so a
        /// player scanning a fire menu can see which weapons are still available
        /// without reading a number off each row.
        /// </summary>
        public static Color RemainingColour(string key, int limit,
            Color normal, Color warning, Color danger)
        {
            int left = RemainingOf(key, limit);
            if (left <= 0) return danger;
            // One third, floored at one, so a two-mission system warns on its
            // last one rather than never warning at all.
            return left <= Mathf.Max(1, limit / 3) ? warning : normal;
        }

        /// <summary>The message shown when a system is out of missions.</summary>
        public static string ExhaustedMessage(string name, int limit) =>
            $"{name} has no missions left — this scenario's allowance of {limit} is spent.";
    }
}
