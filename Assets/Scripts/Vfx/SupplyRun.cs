using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Map;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One supply pass: the transport runs in low along a straight track, opens
    /// its ramp over the zone, and pushes its bundles out one at a time before
    /// continuing out the far side.
    ///
    /// The same shape as <see cref="BomberRun"/> — a planned track, a release
    /// schedule, an object that outlives the aircraft — with the one difference
    /// that matters: **the loads are real objects**. A bomb is not modelled in
    /// flight because at map zoom it is invisible and the burst is the event; a
    /// canopy is deliberately large, slow and white, and watching it come down
    /// *is* the event. See <see cref="ParachuteDrop"/>.
    ///
    /// The aircraft is not taken off the map until the last bundle is down —
    /// destroying it earlier would take the drops with it.
    ///
    /// See docs/29-AIR-SUPPLY.md.
    /// </summary>
    public class SupplyRun : MonoBehaviour
    {
        /// <summary>Called as each bundle touches down: latitude, longitude.</summary>
        public System.Action<double, double> BundleLanded;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        SupplyDropDef _def;

        double _startLat, _startLon, _endLat, _endLon;
        double _targetLat, _targetLon;
        double _groundHeight;
        float _headingDeg;

        float _elapsed;
        int _released;
        int _landed;
        float _firstReleaseAt;

        /// <summary>
        /// Starts a pass over a drop zone. Returns null — having logged why — if
        /// the model is unavailable, so the caller can still deliver the load
        /// rather than losing a tasked mission to a missing asset.
        /// </summary>
        public static SupplyRun Launch(CesiumGeoreference geo, SupplyDropDef def,
            double targetLat, double targetLon, float headingDeg)
        {
            var go = new GameObject("SupplyRun_" + def.label);
            go.transform.SetParent(geo.transform, false);

            var model = UnitModelLibrary.CreateInstance(AirSupplyCatalog.TransportModelId, go.transform);
            if (model == null)
            {
                Debug.LogWarning("[SupplyRun] No usable model for the transport — " +
                    "the drop will still arrive, but with no aircraft.");
                Destroy(go);
                return null;
            }

            var run = go.AddComponent<SupplyRun>();
            run._geo = geo;
            run._def = def;
            run._headingDeg = headingDeg;
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run.PlanTrack(targetLat, targetLon);
            run.ShapeModel(model);

            // The pass travels with the aircraft. Reusing the jet's roar rather
            // than synthesising a turboprop: it is an aircraft passing low
            // overhead, which is what the cue has to say, and a sound nobody
            // asked for is not worth a new synth — see docs/10-AUDIO.md.
            EffectAudio.PlayAt(EffectSound.JetPass, go.transform.position,
                AirSupplyCatalog.WingspanMeters * 4f, go.transform);

            return run;
        }

        void PlanTrack(double targetLat, double targetLon)
        {
            _targetLat = targetLat;
            _targetLon = targetLon;
            _groundHeight = GeoUtils.SampleTerrainHeight(_geo, targetLat, targetLon, 250.0);

            GeoUtils.Destination(targetLat, targetLon, _headingDeg + 180f, AirSupplyCatalog.ApproachKm,
                out _startLat, out _startLon);
            GeoUtils.Destination(targetLat, targetLon, _headingDeg, AirSupplyCatalog.EgressKm,
                out _endLat, out _endLon);

            // The stick is centred on the moment the aircraft is over the zone,
            // and biased *early* by the time a canopy takes to fall: a bundle
            // released overhead lands well down-track, so releasing overhead
            // would put the whole load beyond the zone the player drew.
            float stick = (_def.bundles - 1) * AirSupplyCatalog.ReleaseIntervalSeconds;
            float descent = AirSupplyCatalog.AltitudeMeters /
                            Mathf.Max(1f, AirSupplyCatalog.DescentMetersPerSecond);
            _firstReleaseAt = AirSupplyCatalog.ApproachSeconds - stick * 0.5f - descent * 0.35f;

            Place(0f);
        }

        void ShapeModel(GameObject model)
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Scaled from the model's own bounds, so a re-authored transport
            // does not need this re-tuned. The largest horizontal dimension of a
            // straight-winged airlifter is its wingspan.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 0.0001f)
                model.transform.localScale = Vector3.one * (AirSupplyCatalog.WingspanMeters / span);
        }

        void Update()
        {
            _elapsed += Time.unscaledDeltaTime;

            Place(_elapsed);
            ReleaseDue();

            // Not taken off the map until every bundle is down: the drops are
            // separate objects, but a run that vanished mid-descent would leave
            // the mission's completion hanging on nothing.
            if (_elapsed >= AirSupplyCatalog.RunSeconds && _landed >= _def.bundles)
                Destroy(gameObject);
        }

        /// <summary>
        /// Puts the transport where it should be at <paramref name="t"/> seconds
        /// into the run. Not clamped at the end: once the egress leg is flown it
        /// keeps going in a straight line, which is what an aircraft does and
        /// what keeps it moving while the last canopies come down.
        /// </summary>
        void Place(float t)
        {
            float u = t / Mathf.Max(0.01f, AirSupplyCatalog.RunSeconds);
            double lat = _startLat + (_endLat - _startLat) * u;
            double lon = _startLon + (_endLon - _startLon) * u;

            _anchor.longitudeLatitudeHeight =
                new double3(lon, lat, _groundHeight + AirSupplyCatalog.AltitudeMeters);

            // Wings level, nose very slightly up. A loaded airlifter on a drop
            // run does not bank — it holds the steadiest line it can, which is
            // the visual difference between this and a strike pass.
            transform.localRotation = Quaternion.Euler(-2f, _headingDeg, 0f);
        }

        void ReleaseDue()
        {
            while (_released < _def.bundles &&
                   _elapsed >= _firstReleaseAt + _released * AirSupplyCatalog.ReleaseIntervalSeconds)
            {
                ReleaseOne(_released);
                _released++;
            }
        }

        void ReleaseOne(int index)
        {
            float u = _elapsed / Mathf.Max(0.01f, AirSupplyCatalog.RunSeconds);
            double lat = _startLat + (_endLat - _startLat) * u;
            double lon = _startLon + (_endLon - _startLon) * u;

            // Where this bundle is meant to end up: a point spread evenly over
            // the whole drop zone, from the same golden-angle disc the artillery
            // sheaf and the bomb stick use. A drop that landed in a heap on the
            // centre would leave most of the circle the player drew empty.
            StrikeImpact.ScatterInCircle(_targetLat, _targetLon, _def.radiusMeters,
                index, _def.bundles, out double aimLat, out double aimLon);

            // …blended with the down-track throw the release imparts, so the
            // canopies drift forward as they fall instead of dropping on a
            // plumb line. Weighted to the aim point: covering the zone is the
            // promise, the drift is the flourish.
            GeoUtils.Destination(lat, lon, _headingDeg,
                AirSupplyCatalog.AltitudeMeters * AirSupplyCatalog.DriftFraction / 1000f,
                out double driftLat, out double driftLon);

            const float AimWeight = 0.72f;
            double endLat = driftLat + (aimLat - driftLat) * AimWeight;
            double endLon = driftLon + (aimLon - driftLon) * AimWeight;

            var drop = ParachuteDrop.Release(_geo, _def, lat, lon, endLat, endLon,
                AirSupplyCatalog.AltitudeMeters);

            if (drop == null)
            {
                // No bundle model: deliver it anyway, after the time it would
                // have taken to fall. A missing asset costs the canopy, not the
                // supplies.
                StartCoroutine(DeliverWithoutBundle(endLat, endLon));
                return;
            }

            drop.Landed = (dropLat, dropLon) =>
            {
                _landed++;
                BundleLanded?.Invoke(dropLat, dropLon);
            };
        }

        System.Collections.IEnumerator DeliverWithoutBundle(double lat, double lon)
        {
            float fall = AirSupplyCatalog.AltitudeMeters /
                         Mathf.Max(1f, AirSupplyCatalog.DescentMetersPerSecond);
            yield return new WaitForSecondsRealtime(fall);
            _landed++;
            BundleLanded?.Invoke(lat, lon);
        }
    }
}
