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

            var total = default(BlastResult);

            for (int i = 0; i < def.roundsPerMission; i++)
            {
                ScatterPoint(lat, lon, def.radiusMeters, i, def.roundsPerMission,
                    out double roundLat, out double roundLon);

                VfxSystem.Play(def.burst, roundLat, roundLon, def.burstScale);

                total += BlastDamage.Apply(roundLat, roundLon,
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

        /// <summary>
        /// Where round <paramref name="index"/> falls inside the beaten zone.
        ///
        /// Same construction as the artillery salvo, and for the same reasons:
        /// the golden angle spreads successive rounds around the circle instead
        /// of clumping them on one bearing, the square root on the radius makes
        /// the scatter uniform by *area* (without it every round crowds the
        /// centre and the sheaf looks nothing like a beaten zone), and jitter on
        /// top stops the pattern being recognisable between missions.
        ///
        /// A naval mission has more rounds than a battery's five, so the count is
        /// a parameter rather than a constant.
        /// </summary>
        static void ScatterPoint(double lat, double lon, float radiusMeters, int index, int count,
            out double outLat, out double outLon)
        {
            float t = (index + 0.5f) / Mathf.Max(1, count);
            float distance = Mathf.Sqrt(t) * radiusMeters * Random.Range(0.70f, 0.99f);
            float bearing = index * 137.508f + Random.Range(-22f, 22f);

            GeoUtils.Destination(lat, lon, bearing, distance / 1000.0, out outLat, out outLon);
        }
    }
}
