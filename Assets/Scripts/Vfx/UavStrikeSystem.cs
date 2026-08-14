using System.Collections;
using UnityEngine;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Tasking a UAV strike: pick a type, place the target area, and ten seconds
    /// later the drone launches, flies to the objective and is expended on it.
    ///
    /// The difference from an air strike is that there is no aircraft left at
    /// the end. A bomber releases weapons and flies away; a loitering munition
    /// *is* the weapon, so the flight and the explosion are one event and the
    /// target area is small — this is a single warhead aimed at a single thing,
    /// not a beaten zone.
    ///
    /// Everything up to launch lives in <see cref="CalledStrikeSystem{TKey}"/>,
    /// shared with artillery and air strikes.
    ///
    /// See docs/19-UAV-STRIKES.md.
    /// </summary>
    public class UavStrikeSystem : CalledStrikeSystem<UavType>
    {
        protected override float RadiusFor(UavType key) => UavCatalog.Get(key).radiusMeters;
        protected override Color ColourFor(UavType key) => UavCatalog.Get(key).markerColor;
        protected override string NameFor(UavType key) => UavCatalog.Get(key).name;
        protected override float CountdownFor(UavType key) => UavCatalog.CountdownSeconds;

        protected override string ArmedMessage(UavType key) =>
            $"{UavCatalog.Get(key).name} — click the target area. Right-click or Esc to abort.";

        protected override string AwayMessage(UavType key) =>
            $"UAV tasked — {UavCatalog.Get(key).name}, " +
            $"launch in {Mathf.RoundToInt(UavCatalog.CountdownSeconds)} seconds.";

        /// <summary>
        /// Flies the attack. The drone owns its own flight and calls back once
        /// it reaches the objective, so the explosion happens where the aircraft
        /// actually ended up rather than at a point decided in advance.
        /// </summary>
        protected override IEnumerator RunStrike(UavType key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = UavCatalog.Get(key);

            if (marker != null) marker.SetAlarm(1f);

            // A random run-in bearing, so repeated strikes on the same ground do
            // not all approach on the same line.
            float heading = Random.Range(0f, 360f);

            var run = DroneRun.Launch(Map.Georeference, def, lat, lon, heading);

            if (run != null)
            {
                run.Impact = (impactLat, impactLon) => Detonate(def, impactLat, impactLon);

                // The drone destroys itself on impact, so a null check is the
                // completion test.
                while (run != null) yield return null;
            }
            else
            {
                // The model is not installed. The warhead still goes off — losing
                // a tasked strike to a missing art asset would be a far worse
                // failure than one with nothing to watch. DroneRun has already
                // logged what to run to fix it.
                yield return new WaitForSecondsRealtime(def.FlightSeconds);
                Detonate(def, lat, lon);
            }

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Strike complete — {def.name} expended on target.");
        }

        /// <summary>The warhead: burst, lingering smoke, report and damage.</summary>
        void Detonate(UavDef def, double lat, double lon)
        {
            VfxSystem.Play(def.burst, lat, lon, def.burstScale);

            var smoke = VfxSystem.Play(def.smoke, lat, lon, def.burstScale);
            if (smoke != null && VfxSystem.Active != null)
                VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

            // Wreck fire, for the types that leave one. It outlives the smoke on
            // purpose: the smoke says something just happened here, the fire
            // says something is still happening here.
            if (def.wreckFireSeconds > 0f)
            {
                var fire = VfxSystem.Play(def.wreckFire, lat, lon, def.burstScale * 0.8f);
                if (fire != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(fire, def.wreckFireSeconds);
            }

            int destroyed = BlastDamage.Apply(lat, lon,
                def.lethalRadiusM, def.blastRadiusM, def.maxDamage);

            if (destroyed > 0)
                Flash?.Invoke($"{def.name} — direct hit, {destroyed} formation(s) destroyed.");
        }
    }
}
