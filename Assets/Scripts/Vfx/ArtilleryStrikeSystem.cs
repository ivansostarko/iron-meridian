using System.Collections;
using UnityEngine;
using IronMeridian.Map;

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

            for (int i = 0; i < ArtilleryCatalog.ShellsPerMission; i++)
            {
                ScatterPoint(lat, lon, def.radiusMeters, i, out double roundLat, out double roundLon);

                VfxSystem.Play(def.burst, roundLat, roundLon, def.burstScale);

                // Smoke loops by design and is dispersed explicitly, the same
                // way a wreck is burned out — see VfxSystem.PlayWreck.
                var smoke = VfxSystem.Play(def.smoke, roundLat, roundLon, def.burstScale);
                if (smoke != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

                if (i < ArtilleryCatalog.ShellsPerMission - 1)
                    yield return new WaitForSecondsRealtime(def.shellIntervalSeconds);
            }

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Rounds complete — {def.name}, {ArtilleryCatalog.ShellsPerMission} rounds fired.");
        }

        /// <summary>
        /// Where round <paramref name="index"/> lands inside the target area.
        ///
        /// The golden angle spreads successive rounds around the circle instead
        /// of clumping them, and the square root on the radius makes the scatter
        /// uniform by *area* — without it every round crowds the centre and the
        /// sheaf looks nothing like a beaten zone. Jitter on top stops the
        /// pattern from being recognisable between missions.
        /// </summary>
        static void ScatterPoint(double lat, double lon, float radiusMeters, int index,
            out double outLat, out double outLon)
        {
            float t = (index + 0.5f) / ArtilleryCatalog.ShellsPerMission;
            float distance = Mathf.Sqrt(t) * radiusMeters * Random.Range(0.72f, 0.98f);
            float bearing = index * 137.508f + Random.Range(-20f, 20f);

            GeoUtils.Destination(lat, lon, bearing, distance / 1000.0, out outLat, out outLon);
        }
    }
}
