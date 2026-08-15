using System.Collections;
using UnityEngine;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Tasking a missile system: pick a launcher, place the aim point, and ten
    /// seconds later a missile comes over the horizon and puts a warhead on it.
    ///
    /// Everything up to launch lives in <see cref="CalledStrikeSystem{TKey}"/>,
    /// shared with artillery, air strikes and UAVs — the arming, the target
    /// area tracking the cursor, the ground checks, the escalating marker and
    /// the HUD countdown are identical for all four, and the fourth one being
    /// identical for free is the entire reason that base class exists.
    ///
    /// What is specific here is the **destruction radius**. The target marker
    /// the base class draws is sized from <see cref="MissileSystemDef.radiusMeters"/>,
    /// so a player arming DF-26 sees a 900 m circle follow the cursor and one
    /// arming NASAMS sees 1.3 km of engagement footprint — the ring is the
    /// weapon's claim about what it covers, shown before the commitment rather
    /// than after.
    ///
    /// See docs/20-MISSILE-SYSTEMS.md.
    /// </summary>
    public class MissileStrikeSystem : CalledStrikeSystem<MissileSystemId>
    {
        protected override float RadiusFor(MissileSystemId key) => MissileCatalog.Get(key).radiusMeters;
        protected override Color ColourFor(MissileSystemId key) => MissileCatalog.Get(key).markerColor;
        protected override string NameFor(MissileSystemId key) => MissileCatalog.Get(key).name;
        protected override string BudgetKeyFor(MissileSystemId key) => MissileCatalog.BudgetKey(key);
        protected override int BudgetLimitFor(MissileSystemId key) => MissileCatalog.Get(key).missions;
        protected override float CountdownFor(MissileSystemId key) => MissileCatalog.CountdownSeconds;

        protected override string ArmedMessage(MissileSystemId key)
        {
            var def = MissileCatalog.Get(key);
            string what = def.role == MissileRole.AirDefence
                ? "engagement footprint"
                : "destruction radius";
            return $"{def.name} — {MissileCatalog.RadiusText(def)} {what}. " +
                   "Click the aim point. Right-click or Esc to abort.";
        }

        protected override string AwayMessage(MissileSystemId key) =>
            $"{MissileCatalog.Get(key).name} — launch in " +
            $"{Mathf.RoundToInt(MissileCatalog.CountdownSeconds)} seconds.";

        /// <summary>
        /// Flies the missile in and detonates it. The missile owns its own
        /// flight and calls back on arrival, so the warhead goes off where it
        /// actually ended up rather than at a point decided ten seconds earlier.
        /// </summary>
        protected override IEnumerator RunStrike(MissileSystemId key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = MissileCatalog.Get(key);

            if (marker != null) marker.SetAlarm(1f);

            // A random run-in bearing, so repeated launches on the same ground
            // do not all arrive down the same line.
            float heading = Random.Range(0f, 360f);

            var run = MissileRun.Launch(Map.Georeference, def, lat, lon, heading);

            if (run != null)
            {
                run.Impact = (impactLat, impactLon) => Detonate(def, impactLat, impactLon);

                // The missile destroys itself on impact, so a null check is the
                // completion test.
                while (run != null) yield return null;
            }
            else
            {
                // Nothing should be able to fail here — the airframe is built in
                // code — but losing a tasked mission to a graphics problem would
                // be a far worse failure than one with nothing to watch.
                yield return new WaitForSecondsRealtime(def.flightSeconds);
                Detonate(def, lat, lon);
            }

            // Thirty minutes of fire, then two hours of smoke. See StrikeAftermath.
            StrikeAftermath.Play(lat, lon, def.burstScale);

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Impact — {def.name} on target.");
        }

        /// <summary>The warhead: burst, lingering smoke and fire, damage, report.</summary>
        void Detonate(MissileSystemDef def, double lat, double lon)
        {
            VfxSystem.Play(def.burst, lat, lon, def.burstScale);

            var smoke = VfxSystem.Play(def.smoke, lat, lon, def.burstScale);
            if (smoke != null && VfxSystem.Active != null)
                VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

            // Fire outlives the smoke: the smoke says something just happened
            // here, the fire says the ground is still burning.
            if (def.fireSeconds > 0f)
            {
                var fire = VfxSystem.Play(def.fire, lat, lon, def.burstScale * 0.9f);
                if (fire != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(fire, def.fireSeconds);
            }

            // A missile is one warhead on one point, so the ring and the round
            // arrive together: the target area is destroyed outright and the
            // falloff reaches past it. See StrikeImpact.
            var result = StrikeImpact.Arrive(lat, lon, def.radiusMeters);
            result += StrikeImpact.Round(lat, lon, def.radiusMeters,
                def.lethalRadiusM, def.blastRadiusM, def.maxDamage);

            if (result.hit > 0) Flash?.Invoke($"{def.name} — {result.Report()}");
        }
    }
}
