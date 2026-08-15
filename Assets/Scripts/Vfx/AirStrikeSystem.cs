using System.Collections;
using UnityEngine;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Tasking an air strike: pick an airframe, place the target area, and ten
    /// seconds later the aircraft runs in and puts a stick of five through it.
    ///
    /// The difference from artillery is what the player watches. A fire mission
    /// arrives out of nowhere; a strike arrives *with* something — the aircraft
    /// is on the map for the whole pass, the weapons walk along its track, and
    /// the blasts trail behind it. That is why the countdown here means "time
    /// until the aircraft is overhead" rather than "time until impact", and why
    /// the target marker stays up until the run is finished.
    ///
    /// Everything up to the moment the aircraft appears lives in
    /// <see cref="CalledStrikeSystem{TKey}"/>, shared with
    /// <see cref="ArtilleryStrikeSystem"/>.
    ///
    /// See docs/18-AIR-STRIKES.md.
    /// </summary>
    public class AirStrikeSystem : CalledStrikeSystem<StrikeAircraft>
    {
        protected override float RadiusFor(StrikeAircraft key) =>
            AirStrikeCatalog.Get(key).radiusMeters;

        protected override Color ColourFor(StrikeAircraft key) =>
            AirStrikeCatalog.Get(key).markerColor;

        protected override string NameFor(StrikeAircraft key) =>
            AirStrikeCatalog.Get(key).name;

        protected override string BudgetKeyFor(StrikeAircraft key) =>
            AirStrikeCatalog.BudgetKey(key);

        protected override int BudgetLimitFor(StrikeAircraft key) =>
            AirStrikeCatalog.Get(key).missions;

        protected override float CountdownFor(StrikeAircraft key) =>
            AirStrikeCatalog.CountdownSeconds;

        protected override string ArmedMessage(StrikeAircraft key) =>
            $"{AirStrikeCatalog.Get(key).name} — click the target area. Right-click or Esc to abort.";

        protected override string AwayMessage(StrikeAircraft key) =>
            $"Strike tasked — {AirStrikeCatalog.Get(key).name}, " +
            $"{AirStrikeCatalog.BombsPerStrike} weapons, " +
            $"on station in {Mathf.RoundToInt(AirStrikeCatalog.CountdownSeconds)} seconds.";

        /// <summary>
        /// Flies the pass. The aircraft owns the timing of the weapons — it
        /// releases them along its own track and calls back as each one lands —
        /// so the blasts follow the aeroplane rather than the aeroplane being
        /// decoration over a pre-planned pattern.
        /// </summary>
        protected override IEnumerator RunStrike(StrikeAircraft key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = AirStrikeCatalog.Get(key);

            _result = default;
            if (marker != null) marker.SetAlarm(1f);
            _ringResolved = false;
            _aimLat = lat;
            _aimLon = lon;

            // A random attack heading, so repeated strikes on the same ground do
            // not all run in on the same line.
            float heading = Random.Range(0f, 360f);

            var run = BomberRun.Launch(Map.Georeference, def, lat, lon, heading);

            if (run != null)
            {
                run.BombImpact = (bombLat, bombLon) => Detonate(def, bombLat, bombLon);

                // Wait for the aircraft to finish. It destroys itself once its
                // ordnance is down, so a null check is the completion test.
                while (run != null) yield return null;
            }
            else
            {
                // The model is not installed. The strike still lands — losing a
                // tasked mission to a missing art asset would be a far worse
                // failure than one with no aeroplane to look at. BomberRun has
                // already logged what to run to fix it.
                yield return FallbackStick(def, lat, lon, heading);
            }

            // One aftermath site at the target area rather than one per weapon:
            // the stick walks a few hundred metres, which on an operational map
            // is one struck position. See StrikeAftermath.
            StrikeAftermath.Play(lat, lon, def.burstScale);

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Strike complete — {def.name}, " +
                          $"{AirStrikeCatalog.BombsPerStrike} weapons. {_result.Report()}");
        }

        /// <summary>One weapon on the ground: burst, lingering smoke, report and damage.</summary>
        void Detonate(AircraftDef def, double lat, double lon)
        {
            VfxSystem.Play(def.burst, lat, lon, def.burstScale);

            var smoke = VfxSystem.Play(def.smoke, lat, lon, def.burstScale);
            if (smoke != null && VfxSystem.Active != null)
                VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

            // The target area is resolved once, on the first weapon down: the
            // circle the player drew is a kill zone and a shockwave that size
            // says so. Doing it per weapon would draw nine overlapping rings for
            // one event. See StrikeImpact.
            if (!_ringResolved)
            {
                _ringResolved = true;
                _result += StrikeImpact.Arrive(_aimLat, _aimLon, def.radiusMeters);
            }

            _result += StrikeImpact.Round(lat, lon, def.radiusMeters,
                def.lethalRadiusM, def.blastRadiusM, def.maxDamage);
        }

        /// <summary>The mission's aim point, and whether its target area has been resolved yet.</summary>
        double _aimLat, _aimLon;
        bool _ringResolved;

        /// <summary>
        /// What the run in progress has done so far. A field rather than a
        /// return value because the weapons detonate from the aircraft's own
        /// callbacks, long after RunStrike has stopped being able to see them.
        /// </summary>
        BlastResult _result;

        /// <summary>
        /// The stick as it would have fallen, without an aircraft to drop it.
        /// Spread over the same target circle as the real pass, so a missing
        /// model costs the aeroplane and nothing else.
        /// </summary>
        IEnumerator FallbackStick(AircraftDef def, double lat, double lon, float heading)
        {
            int n = AirStrikeCatalog.BombsPerStrike;

            for (int i = 0; i < n; i++)
            {
                StrikeImpact.ScatterInCircle(lat, lon, def.radiusMeters, i, n,
                    out double bombLat, out double bombLon);

                Detonate(def, bombLat, bombLon);

                if (i < n - 1) yield return new WaitForSecondsRealtime(def.releaseIntervalSeconds);
            }
        }
    }
}
