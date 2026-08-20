using UnityEngine;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// What every called strike does when it arrives — the one place artillery,
    /// naval gunfire, air, UAV and missile strikes agree on what "landing" means.
    ///
    /// **Two things happen, and they are different things.**
    ///
    ///   1. **The target area is resolved once.** The circle the player placed is
    ///      a kill zone: everything whose counter is inside it is destroyed, and
    ///      a shockwave the size of that circle is drawn so the promise and the
    ///      picture are visibly the same circle. Before this, a strike's damage
    ///      came only from individual rounds with lethal radii of a few tens of
    ///      metres scattered inside a target area of hundreds — so a formation
    ///      could sit under an air strike and survive it, which reads as the
    ///      weapon being broken.
    ///
    ///   2. **Each round is then resolved where it lands**, as before. That is
    ///      what damages formations *outside* the ring, and what keeps a wide
    ///      sheaf different from a tight one. Anything inside the ring is already
    ///      dead and is skipped.
    ///
    /// Keeping both in one helper is what stops the five strike systems drifting
    /// apart on the question they are all answering.
    ///
    /// See docs/17-ARTILLERY.md, 18-AIR-STRIKES, 19-UAV-STRIKES,
    /// 20-MISSILE-SYSTEMS, 21-NAVAL-GUNFIRE and 08-PARTICLE-SYSTEMS.
    /// </summary>
    public static class StrikeImpact
    {
        /// <summary>
        /// How far past the target area a strike still hurts, as a multiple of
        /// the ring radius. The falloff between the two is square, so the edge is
        /// nearly harmless — this is the difference between standing next to a
        /// bombed position and standing in it.
        /// </summary>
        public const float BlastReachFactor = 1.9f;

        /// <summary>
        /// Reference size the shockwave and debris effects are authored at. The
        /// catalogue rows carry this as their <c>scaleMeters</c>, so a multiplier
        /// of <c>ringRadius / ReferenceRadiusM</c> puts the ring exactly on the
        /// target area's edge.
        /// </summary>
        const float ReferenceRadiusM = 100f;

        /// <summary>
        /// Resolves the target area and draws the arrival. Call once per mission,
        /// as the first ordnance lands.
        /// </summary>
        /// <param name="ringRadiusM">The target area the player was shown.</param>
        /// <param name="heavy">Heavier strikes throw debris; a light one does not.</param>
        public static BlastResult Arrive(double lat, double lon, float ringRadiusM, bool heavy = true)
        {
            Shockwave(lat, lon, ringRadiusM);
            if (heavy) Debris(lat, lon, ringRadiusM);

            // Stores under the strike go up with everything else standing on
            // that ground — see WreckSupplies. Before the units, so a formation
            // and the cache it was sitting on are both gone in the same instant
            // rather than the cache surviving the round that killed the people
            // guarding it.
            WreckSupplies(lat, lon, ringRadiusM);

            return BlastDamage.ApplyRing(lat, lon, ringRadiusM);
        }

        /// <summary>
        /// The rear area a supplier can reach into.
        ///
        /// Set once by the controller. Static because <see cref="StrikeImpact"/>
        /// is the funnel every strike in the game goes through and none of them
        /// carries a reference to anything — the same arrangement
        /// <c>MapManager.Active</c> and <c>VfxSystem.Active</c> use, and for the
        /// same reason.
        /// </summary>
        public static Logistics.LogisticsSystem Logistics;

        /// <summary>
        /// Destroys the supply points under a strike.
        ///
        /// **Because a depot is a thing on the ground.** Until this existed a
        /// 203 mm mission could land squarely on an ammunition point and leave
        /// it issuing rounds, which made the rear area the one part of the map
        /// that could not be fought over — and made *finding* the enemy's
        /// logistics pointless, since there was nothing to do about it. Now the
        /// most valuable target on an operational map is a target.
        ///
        /// **Both sides**, like every other blast here. Ground does not check
        /// uniforms, and a strike called near your own rear area is a decision
        /// precisely because it can cost you the rear area.
        ///
        /// Centre-in-ring, the same test <see cref="BlastDamage.ApplyRing"/>
        /// uses on formations: an installation is a point on the map, and the
        /// promise the circle makes is that what is inside it is gone.
        /// </summary>
        static void WreckSupplies(double lat, double lon, float ringRadiusM)
        {
            if (Logistics == null || ringRadiusM <= 0f) return;

            var doomed = new System.Collections.Generic.List<Logistics.LogisticsSite>();
            foreach (var site in Logistics.Sites)
            {
                if (site == null) continue;
                double km = Map.GeoUtils.DistanceKm(lat, lon,
                    site.Data.latitude, site.Data.longitude);
                if (km * 1000.0 <= ringRadiusM) doomed.Add(site);
            }

            foreach (var site in doomed)
            {
                // A supply point going up is a supply point going up: stores
                // catch, and the fire is how the player learns from across the
                // map that the strike found something worth finding.
                VfxSystem.Play(VfxId.Explosion, site.Data.latitude, site.Data.longitude);
                VfxSystem.PlayWreck(site.Data.latitude, site.Data.longitude, 1f);
                Logistics.Remove(site);
            }

            if (doomed.Count > 0)
                SuppliesDestroyed?.Invoke(doomed.Count);
        }

        /// <summary>Raised with how many installations a strike destroyed, so the HUD can say so.</summary>
        public static System.Action<int> SuppliesDestroyed;

        /// <summary>The overpressure ring, scaled to sit exactly on the target area's edge.</summary>
        public static void Shockwave(double lat, double lon, float ringRadiusM)
        {
            if (ringRadiusM <= 0f) return;
            VfxSystem.Play(VfxId.BlastShockwave, lat, lon, ringRadiusM / ReferenceRadiusM);
        }

        /// <summary>
        /// Soil and fragments. Deliberately smaller than the ring: debris is
        /// thrown from the impact, not from the whole beaten zone, and matching
        /// it to the ring would make every strike look like a volcano.
        /// </summary>
        public static void Debris(double lat, double lon, float ringRadiusM)
        {
            if (ringRadiusM <= 0f) return;
            VfxSystem.Play(VfxId.BlastDebris, lat, lon, ringRadiusM * 0.55f / ReferenceRadiusM);
        }

        /// <summary>
        /// One round landing at its own point: the falloff pass that reaches past
        /// the ring. <paramref name="lethalRadiusM"/> and
        /// <paramref name="maxDamage"/> come from the strike's own catalogue row;
        /// the outer reach is derived from the ring so it cannot fall short of
        /// the circle the player was shown.
        /// </summary>
        public static BlastResult Round(double lat, double lon,
            float ringRadiusM, float lethalRadiusM, float blastRadiusM, float maxDamage)
        {
            float reach = Mathf.Max(blastRadiusM, ringRadiusM * BlastReachFactor);
            return BlastDamage.Apply(lat, lon, lethalRadiusM, reach, maxDamage);
        }

        /// <summary>
        /// Where round <paramref name="index"/> of <paramref name="count"/> falls
        /// inside a circular target area.
        ///
        /// **Over the whole circle, not along a line.** Sticks used to walk a
        /// track across the target with a little lateral jitter, which left most
        /// of the circle the player drew untouched — the ordnance visibly missed
        /// the area it had been given. The golden angle spreads successive rounds
        /// around the disc instead of clumping them, and the square root on the
        /// radius makes the scatter uniform *by area*: without it every round
        /// crowds the centre and the pattern looks nothing like a beaten zone.
        /// Jitter on top stops the pattern being recognisable between missions.
        /// </summary>
        public static void ScatterInCircle(double lat, double lon, float radiusMeters,
            int index, int count, out double outLat, out double outLon)
        {
            float t = (index + 0.5f) / Mathf.Max(1, count);
            float distance = Mathf.Sqrt(t) * radiusMeters * Random.Range(0.70f, 0.99f);
            float bearing = index * 137.508f + Random.Range(-18f, 18f);

            Map.GeoUtils.Destination(lat, lon, bearing, distance / 1000.0, out outLat, out outLon);
        }
    }
}
