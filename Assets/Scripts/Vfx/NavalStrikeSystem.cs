using System.Collections;
using UnityEngine;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Naval gunfire support: pick a gun, place the target area, and ten seconds
    /// later a ship over the horizon walks a mission of rounds across it.
    ///
    /// Everything up to the moment the first round lands is
    /// <see cref="CalledStrikeSystem{TKey}"/>'s — the arming, the ring following
    /// the cursor, the ground checks, the countdown, the escalating marker, the
    /// HUD banner and the shared strike allowance. This class is the guns and
    /// the mission.
    ///
    /// **What makes it read as naval rather than as more artillery.** The
    /// effects are deliberately the same ones a land gun of that calibre uses —
    /// a 127 mm shell landing is a 127 mm shell landing — so the difference has
    /// to be in the mission itself, and it is:
    ///
    ///  • **Rate of fire.** These are automatic mountings. A Mk 110 puts twelve
    ///    rounds down in under two seconds; a howitzer's five take three. The
    ///    strike reads as a hosing rather than as separate impacts.
    ///  • **Dispersion.** Fired from a moving platform at extreme range, so the
    ///    beaten zone is wider than the equivalent field piece's — the ring the
    ///    player places says so before anything is committed.
    ///  • **Every round is resolved where it lands**, as with artillery, which
    ///    is what makes the wider sheaf a real trade rather than a free upgrade.
    ///
    /// See docs/21-NAVAL-GUNFIRE.md.
    /// </summary>
    public class NavalStrikeSystem : CalledStrikeSystem<NavalGun>
    {
        protected override float RadiusFor(NavalGun key) => NavalCatalog.Get(key).radiusMeters;
        protected override Color ColourFor(NavalGun key) => NavalCatalog.Get(key).markerColor;
        protected override string NameFor(NavalGun key) => NavalCatalog.Get(key).name;
        protected override string BudgetKeyFor(NavalGun key) => NavalCatalog.BudgetKey(key);
        protected override int BudgetLimitFor(NavalGun key) => NavalCatalog.Get(key).missions;
        protected override float CountdownFor(NavalGun key) => NavalCatalog.CountdownSeconds;

        protected override string ArmedMessage(NavalGun key)
        {
            var def = NavalCatalog.Get(key);
            return $"{def.name} — {def.radiusMeters:0} m beaten zone. Click the target area. " +
                   "Right-click or Esc to check fire.";
        }

        protected override string AwayMessage(NavalGun key)
        {
            var def = NavalCatalog.Get(key);
            return $"Naval gunfire away — {def.name}, {def.roundsPerMission} rounds, " +
                   $"splash in {Mathf.RoundToInt(NavalCatalog.CountdownSeconds)} seconds.";
        }

        /// <summary>
        /// Lands the mission. Each round gets its own burst, its own lingering
        /// smoke and its own report, and is resolved against whatever is under
        /// the point it actually fell on.
        /// </summary>
        protected override IEnumerator RunStrike(NavalGun key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = NavalCatalog.Get(key);

            // Full alarm for as long as the rounds are coming, then the marker goes.
            if (marker != null) marker.SetAlarm(1f);

            // The target area, resolved once as the first round arrives —
            // everything under the circle the player drew is destroyed, and a
            // shockwave the size of that circle says so. See StrikeImpact.
            var total = StrikeImpact.Arrive(lat, lon, def.radiusMeters);

            for (int i = 0; i < def.roundsPerMission; i++)
            {
                StrikeImpact.ScatterInCircle(lat, lon, def.radiusMeters, i, def.roundsPerMission,
                    out double roundLat, out double roundLon);

                VfxSystem.Play(def.burst, roundLat, roundLon, def.burstScale);

                total += StrikeImpact.Round(roundLat, roundLon, def.radiusMeters,
                    def.LethalRadiusM, def.BlastRadiusM, def.MaxDamage);

                // Smoke loops by design and is dispersed explicitly, the same
                // way a wreck is burned out — see VfxSystem.PlayWreck.
                var smoke = VfxSystem.Play(def.smoke, roundLat, roundLon, def.burstScale);
                if (smoke != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

                if (i < def.roundsPerMission - 1)
                    yield return new WaitForSecondsRealtime(def.roundIntervalSeconds);
            }

            // Thirty scenario minutes of fire, then two hours of smoke, at the
            // aim point — one site per mission. See StrikeAftermath.
            StrikeAftermath.Play(lat, lon, def.burstScale);

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Rounds complete — {def.name}, {def.roundsPerMission} rounds. " +
                          total.Report());
        }

        // Scatter now lives in StrikeImpact.ScatterInCircle, shared with the
        // artillery salvo and the bombing run.
    }
}
