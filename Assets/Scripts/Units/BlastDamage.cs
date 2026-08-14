using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// What one detonation did: how many formations it touched, how many it
    /// killed, and how much strength it took off between them.
    ///
    /// A count of the dead was not enough to report a mission honestly. Most
    /// strikes destroy nothing and hurt several things, and "0 formations
    /// destroyed" reads as *nothing happened* — which was the complaint, and was
    /// wrong. Results add, so a caller can accumulate a salvo and report the
    /// mission rather than the last round of it.
    /// </summary>
    public struct BlastResult
    {
        /// <summary>Formations that took any damage at all.</summary>
        public int hit;
        /// <summary>Formations destroyed, whether outright or by accumulated damage.</summary>
        public int destroyed;
        /// <summary>Total strength removed, summed across formations (1.0 = one full-strength unit).</summary>
        public float strengthLost;

        public static BlastResult operator +(BlastResult a, BlastResult b) => new BlastResult
        {
            hit = a.hit + b.hit,
            destroyed = a.destroyed + b.destroyed,
            strengthLost = a.strengthLost + b.strengthLost
        };

        /// <summary>
        /// One sentence for the HUD. Deliberately states the miss as plainly as
        /// the hit: a mission that landed on empty ground should say so, rather
        /// than leaving the player to wonder whether the damage model ran.
        /// </summary>
        public string Report()
        {
            if (hit == 0) return "No formations under it.";

            string what = destroyed > 0
                ? $"{hit} formation(s) hit, {destroyed} destroyed"
                : $"{hit} formation(s) hit";

            // Combat strength lost, as a percentage of one full formation. It is
            // the number that says whether a mission was worth the round count.
            return $"{what} — {strengthLost * 100f:0} % combat strength lost.";
        }
    }

    /// <summary>
    /// What a shell, a bomb or a warhead actually does to the formations under
    /// it. Shared by artillery, naval gunfire, air, UAV and missile strikes, so
    /// all five answer the question the same way.
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
    /// **Distance is measured to the formation, not to its map pin.** This is
    /// the change that made strikes land. A unit is stored as one lat/lon and
    /// drawn as one counter, but a battalion is a kilometre of dispersed
    /// sub-units; measuring to the stored coordinate asked whether the round hit
    /// the formation's exact *centre*. With a 155 mm blast radius of 130 m
    /// against a 550 m battalion, that meant a fire mission whose rounds visibly
    /// straddled the counter routinely did nothing whatever — which is what the
    /// player saw, and it was the model being wrong rather than the player
    /// aiming badly. The formation's own footprint
    /// (<see cref="EchelonInfo.FootprintRadiusMeters"/>) is now subtracted from
    /// the range, so a round landing anywhere in the ground a formation occupies
    /// counts as landing on it.
    ///
    /// That does **not** make the lethal radius a formation-wide kill: a direct
    /// hit still has to fall inside the lethal radius measured from the edge of
    /// the footprint, i.e. genuinely among the sub-units, and the damage still
    /// falls off with the square of how far past that it lands. What changed is
    /// that the beaten zone the player is shown and the ground the shells
    /// actually affect are now the same ground.
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
        /// How much of a formation's footprint counts as "under the round".
        ///
        /// Not all of it. A formation is dispersed across its frontage, so a
        /// shell landing at the far edge of a brigade's ground is nowhere near
        /// most of the brigade; crediting the full radius would make a single
        /// 57 mm round on the corner of a division a hit on the division. Two
        /// thirds is close enough to the ground the formation's fighting
        /// elements actually occupy to be honest, and far enough short of the
        /// full extent that a big formation is not a magnet.
        /// </summary>
        const float FootprintShare = 0.66f;

        /// <summary>
        /// Applies one detonation and reports what it did.
        /// </summary>
        /// <param name="lethalRadiusM">Inside this — measured from the edge of the target's footprint — destroyed outright.</param>
        /// <param name="blastRadiusM">Outside this, untouched.</param>
        /// <param name="maxDamage">Strength removed at the lethal edge, 0..1.</param>
        public static BlastResult Apply(double lat, double lon,
            float lethalRadiusM, float blastRadiusM, float maxDamage)
        {
            var result = default(BlastResult);
            if (blastRadiusM <= 0f) return result;

            // A copy, because killing a unit unregisters it and would otherwise
            // mutate the list this is walking.
            var units = new System.Collections.Generic.List<UnitActor>(UnitRegistry.All);

            foreach (var unit in units)
            {
                if (unit == null || !unit.IsAlive) continue;

                double km = GeoUtils.DistanceKm(lat, lon, unit.State.latitude, unit.State.longitude);
                float metres = (float)(km * 1000.0);

                // Range to the formation's near edge rather than to its map pin.
                float footprint = EchelonInfo.FootprintRadiusMeters(unit.State.EchelonEnum) * FootprintShare;
                float range = Mathf.Max(0f, metres - footprint);

                if (range > blastRadiusM) continue;

                float before = unit.State.strength;

                if (range <= lethalRadiusM)
                {
                    // A direct hit. Enough damage to take any formation from
                    // full strength to nothing, which routes it through the
                    // normal death path — wreck effect, fade, deregistration.
                    unit.ApplyDamage(2f);
                    result.hit++;
                    result.destroyed++;
                    result.strengthLost += before;
                    continue;
                }

                // Square falloff from the lethal edge to the blast edge.
                float t = 1f - (range - lethalRadiusM) / Mathf.Max(0.01f, blastRadiusM - lethalRadiusM);
                float damage = maxDamage * t * t;
                if (damage <= 0.0001f) continue;

                unit.ApplyDamage(damage);
                unit.ApplyShock(damage * ShockMultiplier);

                result.hit++;
                result.strengthLost += before - Mathf.Max(0f, unit.State.strength);
                if (!unit.IsAlive) result.destroyed++;
            }

            return result;
        }
    }
}
