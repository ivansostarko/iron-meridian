using System.Collections;
using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Logistics;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Calling an air supply drop: pick a load, place the drop zone, and ten
    /// seconds later a transport runs in and pushes it out on canopies.
    ///
    /// **The mission that builds something.** Every other entry in the fire
    /// menus exists to take something away — a battery, a bomber, a drone, a
    /// missile, a naval gun. This one arrives on the same machinery, through the
    /// same countdown, and leaves a **supply point standing on the map**: each
    /// bundle that touches down becomes a real
    /// <see cref="LogisticsSystem"/> site of the matching kind, with the icon,
    /// the save entry and the right-click menu that any hand-placed one has. A
    /// drop is not an effect that plays; it is ground the player now owns
    /// something on.
    ///
    /// Everything up to the moment the transport appears lives in
    /// <see cref="CalledStrikeSystem{TKey}"/>, shared with the artillery and the
    /// air strikes — the arming, the drop-zone marker tracking the cursor, the
    /// ground checks, the escalating ring and the HUD countdown are identical,
    /// and identical machinery should not be written twice.
    ///
    /// See docs/29-AIR-SUPPLY.md.
    /// </summary>
    public class AirSupplySystem : CalledStrikeSystem<SupplyKind>
    {
        /// <summary>
        /// Where a landed bundle registers itself. Set by the controller; with
        /// no logistics system the drop still flies and lands, it just leaves
        /// nothing behind — which is the right failure for a decoration to have.
        /// </summary>
        public LogisticsSystem Logistics;

        /// <summary>Which side the dropped supplies belong to.</summary>
        public Team Team = Team.User;

        protected override float RadiusFor(SupplyKind key) => AirSupplyCatalog.Get(key).radiusMeters;
        protected override Color ColourFor(SupplyKind key) => AirSupplyCatalog.Get(key).markerColor;
        protected override string NameFor(SupplyKind key) => AirSupplyCatalog.Get(key).name;
        protected override string BudgetKeyFor(SupplyKind key) => AirSupplyCatalog.BudgetKey(key);
        protected override int BudgetLimitFor(SupplyKind key) => AirSupplyCatalog.Get(key).missions;
        protected override float CountdownFor(SupplyKind key) => AirSupplyCatalog.CountdownSeconds;

        protected override string ArmedMessage(SupplyKind key) =>
            $"{AirSupplyCatalog.Get(key).name} — click the drop zone. Right-click or Esc to abort.";

        protected override string AwayMessage(SupplyKind key)
        {
            var def = AirSupplyCatalog.Get(key);
            return $"Air supply tasked — {def.name}, {def.bundles} bundles, " +
                   $"overhead in {Mathf.RoundToInt(AirSupplyCatalog.CountdownSeconds)} seconds.";
        }

        /// <summary>
        /// Flies the pass. The transport owns the timing of the bundles — it
        /// pushes them out along its own track and calls back as each one lands
        /// — so the canopies follow the aeroplane rather than the aeroplane
        /// being decoration over a pre-planned pattern.
        ///
        /// The marker stays up for the whole run, like an air strike's and for
        /// the same reason: the zone is what the mission is *aimed at*, and it
        /// is still being aimed at while the loads are in the air.
        /// </summary>
        protected override IEnumerator RunStrike(SupplyKind key, double lat, double lon,
            TargetAreaMarker marker)
        {
            var def = AirSupplyCatalog.Get(key);
            if (marker != null) marker.SetAlarm(1f);

            _delivered = 0;

            // A random run-in heading, so repeated drops on one zone do not all
            // come in on the same line.
            float heading = Random.Range(0f, 360f);

            var run = SupplyRun.Launch(Map.Georeference, def, lat, lon, heading);

            if (run != null)
            {
                run.BundleLanded = (bundleLat, bundleLon) => Deliver(def, bundleLat, bundleLon);
                while (run != null) yield return null;
            }
            else
            {
                // No transport model. The supplies still arrive: losing a tasked
                // mission to a missing asset would be a far worse failure than
                // one with no aeroplane to watch. SupplyRun has already logged
                // what to fix.
                yield return FallbackDrop(def, lat, lon);
            }

            if (marker != null) Destroy(marker.gameObject);

            Flash?.Invoke($"Air supply delivered — {def.name}, {_delivered} bundle(s) on the ground.");
        }

        /// <summary>Bundles that have reached the ground on the run in progress.</summary>
        int _delivered;

        /// <summary>
        /// One bundle on the ground: the dust of the landing, and the supply
        /// point it becomes.
        ///
        /// The site is created through <see cref="LogisticsSystem"/> rather than
        /// drawn here, so an airdropped point is the same object as a placed one
        /// — same icon, same list, same save entry, same right-click REMOVE.
        /// A drop that produced a special kind of marker only this system
        /// understood would be a second logistics system with one member.
        /// </summary>
        void Deliver(SupplyDropDef def, double lat, double lon)
        {
            _delivered++;

            VfxSystem.Play(VfxId.SupplyLandingDust, lat, lon, 0.8f);

            if (Logistics == null) return;

            Logistics.Add(new LogisticsSiteData
            {
                kind = def.leaves.ToString(),
                team = Team.ToString(),
                label = def.cacheLabel,
                latitude = lat,
                longitude = lon
            });
        }

        /// <summary>
        /// The load as it would have landed, with no transport to drop it.
        /// Spread over the same zone, on the same schedule, so a missing model
        /// costs the aeroplane and the canopies and nothing else.
        /// </summary>
        IEnumerator FallbackDrop(SupplyDropDef def, double lat, double lon)
        {
            for (int i = 0; i < def.bundles; i++)
            {
                StrikeImpact.ScatterInCircle(lat, lon, def.radiusMeters, i, def.bundles,
                    out double bundleLat, out double bundleLon);

                Deliver(def, bundleLat, bundleLon);

                if (i < def.bundles - 1)
                    yield return new WaitForSecondsRealtime(AirSupplyCatalog.ReleaseIntervalSeconds);
            }
        }
    }
}
