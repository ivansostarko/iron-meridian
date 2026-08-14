using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Map;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One bombing pass: the aircraft flies in along a straight track, releases
    /// its stick over the target, and continues out the far side before
    /// disappearing.
    ///
    /// The flight is animated here rather than by an animation clip because the
    /// model is a static mesh — a flying wing has no moving surfaces, so there
    /// is nothing to rig and nothing to play. What makes it read as flight is
    /// the *track*: a real approach leg so the aircraft is seen coming, a bank
    /// into the run, and weapons that keep the aircraft's forward speed as they
    /// fall, so the blasts walk along the ground behind it rather than erupting
    /// underneath it.
    ///
    /// Weapons are not modelled as objects. A 3 m bomb falling from 1500 m is
    /// invisible at the zoom this map is played at; what the player actually
    /// reads is the aircraft passing and the stick walking through the target.
    /// So a release schedules its impact <see cref="AircraftDef.fallSeconds"/>
    /// later and the impact is where the burst happens.
    ///
    /// See docs/18-AIR-STRIKES.md.
    /// </summary>
    public class BomberRun : MonoBehaviour
    {
        /// <summary>Bank angle held through the run, degrees. Enough to read as flying, not aerobatics.</summary>
        const float BankDegrees = 8f;

        /// <summary>Called for each weapon as it reaches the ground: latitude, longitude.</summary>
        public System.Action<double, double> BombImpact;
        /// <summary>Called once the aircraft has left and this object is about to go.</summary>
        public System.Action RunComplete;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        AircraftDef _def;

        double _startLat, _startLon, _endLat, _endLon;
        double _groundHeight;
        float _headingDeg;

        float _elapsed;
        int _released;
        int _impacted;
        float _firstReleaseAt;

        /// <summary>
        /// Starts a pass against a ground point. Returns null — having logged
        /// why — if the model is not installed, so the caller can still land the
        /// bombs rather than losing the strike to a missing asset.
        /// </summary>
        public static BomberRun Launch(CesiumGeoreference geo, AircraftDef def,
            double targetLat, double targetLon, float headingDeg)
        {
            var prefab = LoadModel(def);
            if (prefab == null) return null;

            var go = new GameObject("BomberRun_" + def.label);
            go.transform.SetParent(geo.transform, false);

            var run = go.AddComponent<BomberRun>();
            run._geo = geo;
            run._def = def;
            run._headingDeg = headingDeg;
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run.PlanTrack(targetLat, targetLon);
            run.BuildModel(prefab);

            // The pass is loud and travels with the aircraft. This is the one
            // gameplay sound not carried by a VFX row — see docs/10-AUDIO.md.
            EffectAudio.PlayAt(EffectSound.JetPass, go.transform.position,
                def.wingspanMeters * 4f, go.transform);

            return run;
        }

        /// <summary>
        /// Golden rule 10: model prefabs are reached through
        /// <see cref="UnitModelLibrary"/>, never by a Resources path at a call site.
        /// </summary>
        static GameObject LoadModel(AircraftDef def)
        {
            var model = UnitModelLibrary.Get(def.modelId);
            if (model == null)
            {
                Debug.LogError($"[BomberRun] No model '{def.modelId}' in UnitModelLibrary.");
                return null;
            }

            var prefab = Resources.Load<GameObject>(model.resourcePath);
            if (prefab == null)
                Debug.LogWarning($"[BomberRun] Model '{model.resourcePath}' is not installed — " +
                    "the strike will still land, but with no aircraft. Run " +
                    "Tools > Iron Meridian > Import Bundled Packages, then Install Unit Models " +
                    "(docs/09-3D-MODELS.md).");

            return prefab;
        }

        /// <summary>
        /// Works out where the run starts and ends. The track runs through the
        /// target on <paramref name="targetLat"/>'s bearing, so the stick walks
        /// across the target area rather than landing in a heap on the centre.
        /// </summary>
        void PlanTrack(double targetLat, double targetLon)
        {
            _groundHeight = GeoUtils.SampleTerrainHeight(_geo, targetLat, targetLon, 250.0);

            GeoUtils.Destination(targetLat, targetLon, _headingDeg + 180f, _def.approachKm,
                out _startLat, out _startLon);
            GeoUtils.Destination(targetLat, targetLon, _headingDeg, _def.egressKm,
                out _endLat, out _endLon);

            // Centre the stick on the moment the aircraft is over the target, so
            // half the weapons fall short of it and half beyond.
            float stick = (AirStrikeCatalog.BombsPerStrike - 1) * _def.releaseIntervalSeconds;
            _firstReleaseAt = _def.approachSeconds - stick * 0.5f;

            Place(0f);
        }

        void BuildModel(GameObject prefab)
        {
            var model = Instantiate(prefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Scale from the model's own bounds rather than a magic number, so a
            // re-exported or replaced FBX does not need this re-tuned. The
            // largest horizontal dimension of a flying wing is its wingspan.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 0.0001f)
                model.transform.localScale = Vector3.one * (_def.wingspanMeters / span);

            // Nose down the local +Z unless the model says otherwise.
            model.transform.localRotation = Quaternion.Euler(0f, _def.noseYawOffsetDeg, 0f);
        }

        void Update()
        {
            // Unscaled to match the countdown that started this run: a pass must
            // finish even with the battle paused.
            _elapsed += Time.unscaledDeltaTime;

            Place(_elapsed);
            ReleaseDue();

            // The aircraft is not taken off the map until its ordnance is down.
            // Destroying it on the egress timer alone would kill the fall
            // coroutines with it and silently lose any weapon still in the air —
            // which is exactly what happens if someone later lengthens the fall
            // time or shortens the egress leg.
            if (_elapsed >= _def.RunSeconds && _impacted >= AirStrikeCatalog.BombsPerStrike)
            {
                RunComplete?.Invoke();
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Puts the aircraft where it should be at <paramref name="t"/> seconds
        /// into the run. Deliberately not clamped at the end of the track: once
        /// the egress leg is flown the aircraft keeps going in a straight line
        /// off into the distance, which is both what an aircraft does and what
        /// keeps it moving during the wait for the last weapon to land.
        /// </summary>
        void Place(float t)
        {
            float u = t / Mathf.Max(0.01f, _def.RunSeconds);

            // Linear in lat/lon: the track is under 10 km, where the difference
            // from a great circle is far below one pixel.
            double lat = _startLat + (_endLat - _startLat) * u;
            double lon = _startLon + (_endLon - _startLon) * u;

            _anchor.longitudeLatitudeHeight =
                new double3(lon, lat, _groundHeight + _def.altitudeMeters);

            // The anchor keeps local +Y along the globe normal, so heading is a
            // plain yaw. Bearings run clockwise from north and Unity yaw runs
            // clockwise from +Z, which is what makes this a direct assignment.
            transform.localRotation = Quaternion.Euler(0f, _headingDeg, BankDegrees);
        }

        /// <summary>Releases any weapon whose moment has come.</summary>
        void ReleaseDue()
        {
            while (_released < AirStrikeCatalog.BombsPerStrike &&
                   _elapsed >= _firstReleaseAt + _released * _def.releaseIntervalSeconds)
            {
                ReleaseOne(_released);
                _released++;
            }
        }

        void ReleaseOne(int index)
        {
            // A released weapon carries the aircraft's forward speed, so it lands
            // ahead of the release point rather than directly below it. Without
            // this the stick bunches up under the flight path and the pass reads
            // as the aircraft dropping straight down.
            float trackKm = (float)(_def.approachKm + _def.egressKm);
            float speedKmPerSec = trackKm / Mathf.Max(0.01f, _def.RunSeconds);
            float throwKm = speedKmPerSec * _def.fallSeconds;

            // Where the aircraft is right now, along the track.
            float u = _elapsed / Mathf.Max(0.01f, _def.RunSeconds);
            double lat = _startLat + (_endLat - _startLat) * u;
            double lon = _startLon + (_endLon - _startLon) * u;

            GeoUtils.Destination(lat, lon, _headingDeg, throwKm, out double impactLat, out double impactLon);

            // Lateral spread so the stick is a swathe rather than a pencil line.
            // Fully qualified: Unity.Mathematics is imported here for double3 and
            // brings its own Random with it.
            float spread = _def.radiusMeters * 0.30f;
            float offset = UnityEngine.Random.Range(-spread, spread);
            GeoUtils.Destination(impactLat, impactLon, _headingDeg + 90f, offset / 1000.0,
                out impactLat, out impactLon);

            StartCoroutine(Fall(impactLat, impactLon));
        }

        System.Collections.IEnumerator Fall(double lat, double lon)
        {
            yield return new WaitForSecondsRealtime(_def.fallSeconds);
            _impacted++;
            BombImpact?.Invoke(lat, lon);
        }
    }
}
