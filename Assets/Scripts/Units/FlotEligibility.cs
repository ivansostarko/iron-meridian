using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>
    /// Which formations are allowed to move the front line, and by how much.
    ///
    /// **The FLOT is where effective control by combat formations ends — not
    /// where the most advanced counter happens to be standing.** So a formation
    /// has to pass two tests before it gets a vote:
    ///
    ///  • **It is the kind of thing that holds ground.** Infantry, mechanised
    ///    infantry and armour push a front; artillery shapes it from behind it,
    ///    logistics and medical live behind it, and aircraft and drones are
    ///    over it rather than on it. None of those should drag the line to
    ///    wherever they are parked.
    ///  • **It is combat-effective right now.** A destroyed, routed or
    ///    shattered battalion is not holding anything, whatever its position
    ///    says.
    ///
    /// **Derived from what a unit is, not stored on it.** The unit catalogue
    /// already says everything needed — category, branch, the support flag —
    /// and a per-unit `canInfluenceFlot` field in units.json would be one more
    /// thing to keep in step with the branch it duplicates. The mapping lives
    /// here, in one place, where changing it changes every unit at once.
    /// </summary>
    public static class FlotEligibility
    {
        /// <summary>
        /// Below this fraction of strength a formation no longer holds its
        /// ground for FLOT purposes. Matches the rout threshold — a formation
        /// about to break is not a formation the line should rest on.
        /// </summary>
        public const float MinStrength = 0.25f;

        /// <summary>True if this formation can move the front line at all.</summary>
        public static bool CanInfluence(UnitActor unit) => Weight(unit) > 0f;

        /// <summary>
        /// The formation's pull on the line — its combat power scaled by how
        /// much of a line-holder its arm of service is. Zero for anything that
        /// should never move a front.
        ///
        /// Armour weighs a little more than infantry per point of power because
        /// a tank battalion projects control over more ground than it stands
        /// on; the factor is deliberately small, because power already carries
        /// most of the difference.
        /// </summary>
        public static float Weight(UnitActor unit)
        {
            if (unit == null || !unit.IsAlive || unit.Def == null) return 0f;

            // Only things that stand on and take ground. This excludes air,
            // drones and naval by category before branch is even consulted.
            if (!unit.Def.HoldsGround || unit.Def.isSupport) return 0f;

            float branch = unit.Def.Branch switch
            {
                UnitBranch.Infantry => 1.0f,
                UnitBranch.Mechanised => 1.1f,
                UnitBranch.Armour => 1.25f,
                // Artillery, AA, logistics, air, navy, other: real formations,
                // no vote on where the forward edge is.
                _ => 0f
            };
            if (branch <= 0f) return 0f;

            // Combat-effective: enough strength left, and not broken.
            if (unit.State.strength < MinStrength) return 0f;
            string status = unit.State.status;
            if (status == UnitStatus.Routed.ToString() ||
                status == UnitStatus.Destroyed.ToString()) return 0f;

            return unit.CurrentPower() * branch;
        }
    }
}
