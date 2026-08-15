using System.Collections;
using UnityEngine;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Calling for fire: pick a nature, place the target area on the map, and
    /// ten seconds later five rounds land inside it.
    ///
    /// The delay is the feature. A strike that lands the instant you click is a
    /// paint tool; one that lands ten seconds later is a decision — the ground
    /// is committed to, the marker sits there advertising where the rounds are
    /// going, and nothing can be done about it afterwards. That is why a mission
    /// cannot be recalled once it is away, and why the marker escalates
    /// visually as it runs down.
    ///
    /// Everything up to the moment of impact lives in
    /// <see cref="CalledStrikeSystem{TKey}"/>, shared with
    /// <see cref="AirStrikeSystem"/>. This class is the natures and the salvo.
    ///
    /// See docs/17-ARTILLERY.md.
    /// </summary>
    public class ArtilleryStrikeSystem : CalledStrikeSystem<ArtilleryCaliber>
    {
        protected override float RadiusFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.Get(key).radiusMeters;

        protected override Color ColourFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.Get(key).markerColor;

        protected override string NameFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.Get(key).name;

        protected override string BudgetKeyFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.BudgetKey(key);

        protected override int BudgetLimitFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.Get(key).missions;

        protected override float CountdownFor(ArtilleryCaliber key) =>
            ArtilleryCatalog.CountdownSeconds;

        protected override string ArmedMessage(ArtilleryCaliber key) =>
            $"{ArtilleryCatalog.Get(key).name} — click the target area. Right-click or Esc to stand down.";

        protected override string AwayMessage(ArtilleryCaliber key) =>
            $"Fire mission away — {ArtilleryCatalog.Get(key).name}, " +
            $"{ArtilleryCatalog.ShellsPerMission} rounds, " +
            $"impact in {Mathf.RoundToInt(ArtilleryCatalog.CountdownSeconds)} seconds.";

        /// <summary>
        /// Lands the salvo: five rounds, scattered across the target area and
        /// spaced so the strike reads as a battery firing rather than one
        /// detonation. Each round carries its own burst, its own lingering smoke
        /// and its own report — all three from the nature's catalogue row.
        /// </summary>
        protected override IEnumerator RunStrike(ArtilleryCaliber key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = ArtilleryCatalog.Get(key);

            // Full alarm for the duration of the shooting, then the marker goes.
            if (marker != null) marker.SetAlarm(1f);

            // The target area itself, resolved once as the first round lands:
            // everything under the circle the player drew is destroyed, and a
            // shockwave the size of that circle says so. See StrikeImpact.
            var total = StrikeImpact.Arrive(lat, lon, def.radiusMeters,
                heavy: def.calibreMm >= 120);

            for (int i = 0; i < ArtilleryCatalog.ShellsPerMission; i++)
            {
                StrikeImpact.ScatterInCircle(lat, lon, def.radiusMeters, i,
                    ArtilleryCatalog.ShellsPerMission, out double roundLat, out double roundLon);

                VfxSystem.Play(def.burst, roundLat, roundLon, def.burstScale);

                // Every round is then resolved where it actually lands, not
                // against the target area as a whole — which is what damages
                // formations outside the ring, what makes the scatter matter and
                // why a wide sheaf is not strictly better.
                total += StrikeImpact.Round(roundLat, roundLon, def.radiusMeters,
                    def.LethalRadiusM, def.BlastRadiusM, def.MaxDamage);

                // Smoke loops by design and is dispersed explicitly, the same
                // way a wreck is burned out — see VfxSystem.PlayWreck.
                var smoke = VfxSystem.Play(def.smoke, roundLat, roundLon, def.burstScale);
                if (smoke != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

                if (i < ArtilleryCatalog.ShellsPerMission - 1)
                    yield return new WaitForSecondsRealtime(def.shellIntervalSeconds);
            }

            // The mission's mark on the ground: one site at the aim point, not
            // one per round — a salvo is a single event on the map, and five
            // overlapping fires would cost five times as much for a worse
            // picture. See StrikeAftermath.
            StrikeAftermath.Play(lat, lon, def.burstScale);

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Rounds complete — {def.name}, " +
                          $"{ArtilleryCatalog.ShellsPerMission} rounds. {total.Report()}");
        }

        // Scatter now lives in StrikeImpact.ScatterInCircle, shared with the
        // naval mission and the bombing run — all three were spreading ordnance
        // over a circle and only one of them was doing it correctly.
    }
}
