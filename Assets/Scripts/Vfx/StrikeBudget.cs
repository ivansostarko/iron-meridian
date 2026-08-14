using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// How many called strikes a scenario has left — artillery, air, missile and
    /// UAV together.
    ///
    /// **Why one pool rather than four.** Every one of these menus asks the same
    /// question: commit a piece of ground and something lands on it. With
    /// unlimited missions the answer is always yes, and the choice between a
    /// 105 mm fire mission and an Iskander stops being a choice at all — you
    /// call both. A single shared allowance makes every strike cost the same
    /// thing, which is the *next* strike, and that is what turns the four menus
    /// into one decision: not "which of these is best here", but "is this worth
    /// one of the ninety-nine".
    ///
    /// Separate per-arm pools would have said the opposite — that artillery is
    /// free once air is spent — and would have needed four counters on screen to
    /// explain it.
    ///
    /// **Ninety-nine, and it is a hard stop.** The number is deliberately larger
    /// than any scenario needs, so it never gets in the way of laying one out;
    /// it exists to stop the map editor being used as an infinite paint tool.
    /// The count is consumed when a mission is *placed*, not when it lands: a
    /// mission cannot be recalled once away, so that is the moment the player
    /// spent it.
    ///
    /// Static because the four strike systems, the three rail sections and the
    /// missile board all read it and none of them owns it. It is scenario state,
    /// so <see cref="Reset"/> is called when the editor is reset or a scene
    /// starts — static fields survive a scene load, and a fresh map opening with
    /// forty strikes already spent would be a bug nobody could explain.
    /// </summary>
    public static class StrikeBudget
    {
        /// <summary>Strikes a scenario may call, across every delivery means.</summary>
        public const int Limit = 99;

        public static int Used { get; private set; }
        public static int Remaining => Mathf.Max(0, Limit - Used);
        public static bool Exhausted => Used >= Limit;

        /// <summary>Raised whenever the count moves, so every panel showing it can repaint.</summary>
        public static event System.Action Changed;

        /// <summary>
        /// Spends one strike. Returns false when there are none left, in which
        /// case nothing was spent and the caller must not fire.
        /// </summary>
        public static bool TryConsume()
        {
            if (Exhausted) return false;
            Used++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Back to a full allowance. Called when a scenario starts or is reset.</summary>
        public static void Reset()
        {
            if (Used == 0) return;
            Used = 0;
            Changed?.Invoke();
        }

        /// <summary>Readout for the panels: "STRIKES REMAINING   87 / 99".</summary>
        public static string RemainingText => $"{Remaining} / {Limit}";

        /// <summary>
        /// Turns amber as the allowance runs down and red once it is gone, so a
        /// player scanning a fire menu can see the state without reading it.
        /// </summary>
        public static Color RemainingColour(Color normal, Color warning, Color danger)
        {
            if (Exhausted) return danger;
            return Remaining <= Limit / 5 ? warning : normal;
        }

        /// <summary>The message shown when a strike is refused.</summary>
        public const string ExhaustedMessage =
            "No strikes remaining — this scenario's allowance of 99 is spent.";
    }
}
