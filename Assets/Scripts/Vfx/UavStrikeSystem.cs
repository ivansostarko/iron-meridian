using System.Collections;
using UnityEngine;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Tasking a UAV: pick a type, place the objective, and ten seconds later a
    /// drone launches and flies to it.
    ///
    /// Two kinds of thing happen at the far end, and which one is
    /// <see cref="UavDef.isRecon"/>'s to say:
    ///
    ///  • **Attack types** are expended on the objective. The difference from an
    ///    air strike is that there is no aircraft left at the end — a bomber
    ///    releases weapons and flies away; a loitering munition *is* the weapon,
    ///    so the flight and the explosion are one event and the target area is
    ///    small: a single warhead aimed at a single thing, not a beaten zone.
    ///
    ///  • **The reconnaissance type** carries no warhead. It holds an orbit over
    ///    the objective for five operational minutes, lifts the fog off
    ///    everything within ten kilometres while it is there, and goes home.
    ///
    /// They share this class because everything before the objective is the same
    /// decision, and it is most of the machinery: the arming, the ring following
    /// the cursor, the ground checks, the countdown, the escalating marker, the
    /// HUD banner and the strike allowance all live in
    /// <see cref="CalledStrikeSystem{TKey}"/> and cost nothing to reuse. What a
    /// player is choosing in the UAV menu is genuinely one choice — which
    /// unmanned aircraft to send to a point on the map.
    ///
    /// See docs/19-UAV-STRIKES.md.
    /// </summary>
    public class UavStrikeSystem : CalledStrikeSystem<UavType>
    {
        protected override float RadiusFor(UavType key) => UavCatalog.Get(key).radiusMeters;
        protected override Color ColourFor(UavType key) => UavCatalog.Get(key).markerColor;
        protected override string NameFor(UavType key) => UavCatalog.Get(key).name;
        protected override string BudgetKeyFor(UavType key) => UavCatalog.BudgetKey(key);
        protected override int BudgetLimitFor(UavType key) => UavCatalog.Get(key).missions;
        protected override float CountdownFor(UavType key) => UavCatalog.CountdownSeconds;

        protected override string ArmedMessage(UavType key)
        {
            var def = UavCatalog.Get(key);
            return def.isRecon
                ? $"{def.name} — click the centre of the area to search. The ring is the " +
                  $"{def.reconRadiusKm:0} km it will uncover. Right-click or Esc to abort."
                : $"{def.name} — click the target area. Right-click or Esc to abort.";
        }

        protected override string AwayMessage(UavType key)
        {
            var def = UavCatalog.Get(key);
            return def.isRecon
                ? $"Reconnaissance tasked — {def.name}, launch in " +
                  $"{Mathf.RoundToInt(UavCatalog.CountdownSeconds)} seconds, " +
                  $"{def.onStationMinutes:0} minutes on station."
                : $"UAV tasked — {def.name}, " +
                  $"launch in {Mathf.RoundToInt(UavCatalog.CountdownSeconds)} seconds.";
        }

        /// <summary>Sends the sortie the chosen type actually flies.</summary>
        protected override IEnumerator RunStrike(UavType key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = UavCatalog.Get(key);
            return def.isRecon
                ? RunReconnaissance(def, lat, lon, marker)
                : RunAttack(def, lat, lon, marker);
        }

        /// <summary>
        /// Flies the attack. The drone owns its own flight and calls back once
        /// it reaches the objective, so the explosion happens where the aircraft
        /// actually ended up rather than at a point decided in advance.
        /// </summary>
        IEnumerator RunAttack(UavDef def, double lat, double lon, TargetAreaMarker marker)
        {
            if (marker != null) marker.SetAlarm(1f);

            // A random run-in bearing, so repeated strikes on the same ground do
            // not all approach on the same line.
            float heading = Random.Range(0f, 360f);

            var run = DroneRun.Launch(Map.Georeference, def, lat, lon, heading);
            bool shotDown = false;

            if (run != null)
            {
                run.Impact = (impactLat, impactLon) => Detonate(def, impactLat, impactLon);
                run.Aborted = () => shotDown = true;

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

            // Intercepted short of the objective. Nothing is left on the target
            // — no warhead, no burning ground, no report of a strike complete —
            // because nothing happened there. The wreck and its fire are where
            // the drone came down, and DroneFall owns those. See
            // docs/24-AIR-DEFENCE.md.
            if (shotDown)
            {
                Flash?.Invoke($"{def.name} was shot down short of the objective — target untouched.");
                yield break;
            }

            // Thirty minutes of fire, then two hours of smoke. See StrikeAftermath.
            StrikeAftermath.Play(lat, lon, def.burstScale);

            Flash?.Invoke($"Strike complete — {def.name} expended on target.");
        }

        // ------------------------------------------------------ reconnaissance

        /// <summary>
        /// Flies the reconnaissance sortie.
        ///
        /// The **fog sensor is registered when the drone arrives, not when it is
        /// tasked** — the ground is uncovered because something is over it
        /// looking, and a footprint that appeared the moment the mission was
        /// ordered would be intelligence the player has not paid for yet. It is
        /// removed the moment the drone turns for home, for the same reason.
        ///
        /// What survives the sortie is what reconnaissance actually leaves
        /// behind: the terrain it uncovered stays explored in the fog blanket,
        /// and every enemy formation it saw becomes a last-known contact with a
        /// time stamp on it. The live view goes home with the drone — see
        /// docs/16-FOG-OF-WAR.md.
        /// </summary>
        IEnumerator RunReconnaissance(UavDef def, double lat, double lon, TargetAreaMarker marker)
        {
            // The search ring holds steady rather than escalating. Nothing is
            // about to land here; the alarm states belong to strikes.
            if (marker != null) marker.SetAlarm(0f);

            // Motes over the objective for the whole sortie, under the ring the
            // player placed. The ring says how much ground; the motes say *this
            // piece of ground*, and they mark it from the moment the mission is
            // flown rather than only once the drone is overhead — the point is
            // chosen at the start, and the map should say so the whole way.
            var motes = VfxSystem.Play(VfxId.ReconMarker, lat, lon);

            float heading = Random.Range(0f, 360f);
            var run = ReconDroneRun.Launch(Map.Georeference, def, lat, lon, heading);
            bool shotDown = false;

            FogOfWarSystem.Sensor sensor = null;

            // The sensor, by contrast, waits for the drone: the ground is
            // uncovered because something is over it looking, and a footprint
            // that arrived with the marker would be intelligence the player has
            // not paid for yet.
            void Begin()
            {
                if (FogOfWarSystem.Active != null)
                    sensor = FogOfWarSystem.Active.AddSensor(lat, lon, def.reconRadiusKm, def.name);

                Flash?.Invoke($"{def.name} on station — {def.reconRadiusKm:0} km uncovered for " +
                              $"{def.onStationMinutes:0} minutes.");
            }

            void End()
            {
                if (sensor != null && FogOfWarSystem.Active != null)
                    FogOfWarSystem.Active.RemoveSensor(sensor);
                sensor = null;
            }

            if (run != null)
            {
                run.OnStation = Begin;
                run.OffStation = End;
                run.Aborted = () => shotDown = true;

                // The drone destroys itself once it has flown home, so a null
                // check is the completion test.
                while (run != null) yield return null;
            }
            else
            {
                // No model to fly. The sortie still runs — losing intelligence
                // the player paid a strike for to a graphics problem would be a
                // far worse failure than one with nothing to watch.
                yield return new WaitForSecondsRealtime(def.transitSeconds);
                Begin();

                float remaining = def.onStationMinutes * 60f;
                while (remaining > 0f)
                {
                    remaining -= Core.GameClock.ScenarioDelta;
                    yield return null;
                }
            }

            // Belt and braces: the callbacks above run End, but a run destroyed
            // early (a scene reload, a RESET) would never reach OffStation, and
            // a sensor left registered is fog lifted off ground nothing is
            // watching.
            End();

            if (motes != null) motes.Stop();
            if (marker != null) Destroy(marker.gameObject);

            // What it managed to see before it was hit still counts — explored
            // ground and last-known contacts are already on the map, and taking
            // them back would be un-learning something the player was shown.
            // Only the drone is lost.
            Flash?.Invoke(shotDown
                ? $"{def.name} was shot down over the objective. What it had already seen stands."
                : $"{def.name} off station — the drone is returning. " +
                  "What it saw is on the map as last-known contacts.");
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

            // A loitering munition carries a few kilograms, so it gets the ring
            // and the shockwave but throws no debris — a quadcopter grenade that
            // fountained soil like a 203 mm shell would be lying about its size.
            var result = StrikeImpact.Arrive(lat, lon, def.radiusMeters,
                heavy: def.blastRadiusM >= 150f);
            result += StrikeImpact.Round(lat, lon, def.radiusMeters,
                def.lethalRadiusM, def.blastRadiusM, def.maxDamage);

            if (result.hit > 0) Flash?.Invoke($"{def.name} — {result.Report()}");
        }
    }
}
