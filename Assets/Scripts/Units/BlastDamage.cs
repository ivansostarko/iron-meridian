using UnityEngine;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// What a shell, a bomb or a warhead actually does to the formations under
    /// it. Shared by artillery, air and UAV strikes, so all three answer the
    /// question the same way.
    ///
    /// Two radii, because a blast is not a switch:
    ///
    ///   **Lethal radius** — a direct hit. Anything inside is destroyed
    ///     outright, whatever its strength was. A battalion that takes a 203 mm
    ///     shell through its position does not lose a percentage.
    ///   **Blast radius** — the outer edge of the effect. Between the two,
    ///     damage falls off with the *square* of the distance, because blast
    ///     overpressure does. Linear falloff makes the edge of the circle as
    ///     dangerous as the middle, which is what turns artillery into a
    ///     stamp-shaped area-denial tool rather than a weapon you have to aim.
    ///
    /// **It hits both sides.** A strike is placed on a piece of ground, and
    /// ground does not check uniforms. Friendly fire is not a special case here;
    /// it is what falls out of doing the honest thing, and it is what makes
    /// placing a mission near your own line a decision.
    ///
    /// Damage is dealt through <see cref="UnitActor.ApplyDamage"/>, so the
    /// existing burning, routing and death sequences all follow from it without
    /// this class knowing anything about them.
    /// </summary>
    public static class BlastDamage
    {
        /// <summary>
        /// Shock (morale and organisation) dealt as a multiple of the strength
        /// damage. Being shelled and surviving still costs a formation its
        /// composure, and near the edge of the blast that is the whole effect.
        /// </summary>
        const float ShockMultiplier = 55f;

        /// <summary>
        /// Applies one detonation. Returns how many formations were destroyed by
        /// it, so callers can report a result rather than guess at one.
        /// </summary>
        /// <param name="lethalRadiusM">Inside this, destroyed outright.</param>
        /// <param name="blastRadiusM">Outside this, untouched.</param>
        /// <param name="maxDamage">Strength removed at the lethal edge, 0..1.</param>
        public static int Apply(double lat, double lon,
            float lethalRadiusM, float blastRadiusM, float maxDamage)
        {
            if (blastRadiusM <= 0f) return 0;

            int destroyed = 0;

            // A copy, because killing a unit unregisters it and would otherwise
            // mutate the list this is walking.
            var units = new System.Collections.Generic.List<UnitActor>(UnitRegistry.All);

            foreach (var unit in units)
            {
                if (unit == null || !unit.IsAlive) continue;

                double km = GeoUtils.DistanceKm(lat, lon, unit.State.latitude, unit.State.longitude);
                float metres = (float)(km * 1000.0);
                if (metres > blastRadiusM) continue;

                if (metres <= lethalRadiusM)
                {
                    // A direct hit. Enough damage to take any formation from
                    // full strength to nothing, which routes it through the
                    // normal death path — wreck effect, fade, deregistration.
                    unit.ApplyDamage(2f);
                    destroyed++;
                    continue;
                }

                // Square falloff from the lethal edge to the blast edge.
                float t = 1f - (metres - lethalRadiusM) / Mathf.Max(0.01f, blastRadiusM - lethalRadiusM);
                float damage = maxDamage * t * t;

                bool wasAlive = unit.IsAlive;
                unit.ApplyDamage(damage);
                unit.ApplyShock(damage * ShockMultiplier);
                if (wasAlive && !unit.IsAlive) destroyed++;
            }

            return destroyed;
        }
    }
}
