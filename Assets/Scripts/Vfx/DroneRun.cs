using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Map;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One loitering-munition attack: the drone cruises in level toward the
    /// objective, tips over into a terminal dive, and is destroyed on the
    /// target with its warhead.
    ///
    /// It is a separate flight from <see cref="BomberRun"/> because it is a
    /// fundamentally different manoeuvre. An aircraft *passes over* a target and
    /// releases something that carries on without it; a loitering munition *is*
    /// the weapon and arrives at the impact point itself. That difference is the
    /// whole character of the thing on screen: there is no egress leg, no stick
    /// walking across the ground, and the flight ends where the explosion starts.
    ///
    /// Two phases:
    ///   **Cruise** — level, at altitude, closing on the objective. Long enough
    ///                that the drone is seen coming.
    ///   **Dive**   — nose over at <see cref="UavDef.diveAngleDeg"/> and run in
    ///                to the ground point, propellers still turning.
    ///
    /// The propeller buzz travels with it and is cut the instant it detonates —
    /// a drone that is still humming after its own warhead has gone off is the
    /// kind of detail that makes the rest look fake.
    ///
    /// See docs/19-UAV-STRIKES.md.
    /// </summary>
    public class DroneRun : MonoBehaviour
    {
        /// <summary>Called once the drone reaches the target: latitude, longitude.</summary>
        public System.Action<double, double> Impact;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        UavDef _def;
        AudioSource _buzz;

        double _startLat, _startLon, _targetLat, _targetLon;
        double _groundHeight;
        float _headingDeg;
        float _elapsed;
        bool _done;

        /// <summary>
        /// Launches an attack against a ground point. Returns null — having
        /// logged why — if the model is not installed, so the caller can still
        /// detonate the warhead rather than losing the strike to a missing asset.
        /// </summary>
        public static DroneRun Launch(CesiumGeoreference geo, UavDef def,
            double targetLat, double targetLon, float headingDeg)
        {
            var prefab = LoadModel(def);
            if (prefab == null) return null;

            var go = new GameObject("DroneRun_" + def.label);
            go.transform.SetParent(geo.transform, false);

            var run = go.AddComponent<DroneRun>();
            run._geo = geo;
            run._def = def;
            run._headingDeg = headingDeg;
            run._targetLat = targetLat;
            run._targetLon = targetLon;
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run.PlanTrack();
            run.BuildModel(prefab);

            // Propeller buzz, parented so it travels with the airframe.
            run._buzz = EffectAudio.PlayAt(EffectSound.DroneBuzz, go.transform.position,
                def.spanMeters * 6f, go.transform);

            return run;
        }

        /// <summary>
        /// Golden rule 10: model prefabs are reached through
        /// <see cref="UnitModelLibrary"/>, never by a Resources path at a call site.
        /// </summary>
        static GameObject LoadModel(UavDef def)
        {
            var model = UnitModelLibrary.Get(def.modelId);
            if (model == null)
            {
                Debug.LogError($"[DroneRun] No model '{def.modelId}' in UnitModelLibrary.");
                return null;
            }

            var prefab = Resources.Load<GameObject>(model.resourcePath);
            if (prefab == null)
                Debug.LogWarning($"[DroneRun] Model '{model.resourcePath}' is not installed — " +
                    "the strike will still land, but with no drone. Run " +
                    "Tools > Iron Meridian > Install Unit Models (docs/09-3D-MODELS.md).");

            return prefab;
        }

        void PlanTrack()
        {
            _groundHeight = GeoUtils.SampleTerrainHeight(_geo, _targetLat, _targetLon, 250.0);

            // Launched from behind the objective on the attack bearing.
            GeoUtils.Destination(_targetLat, _targetLon, _headingDeg + 180f, _def.approachKm,
                out _startLat, out _startLon);

            Place(0f);
        }

        void BuildModel(GameObject prefab)
        {
            var model = Instantiate(prefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Scale from the model's own bounds rather than a magic number, so a
            // replaced model needs no re-tuning.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float span = Mathf.Max(bounds.size.x, bounds.size.z);
                if (span > 0.0001f)
                    model.transform.localScale = Vector3.one * (_def.spanMeters / span);
            }

            model.transform.localRotation = Quaternion.Euler(0f, _def.noseYawOffsetDeg, 0f);

            RotorSpinner.Attach(model, _def.rotors);
        }

        void Update()
        {
            if (_done) return;

            // Unscaled to match the countdown that launched this: an attack must
            // finish even with the battle paused.
            _elapsed += Time.unscaledDeltaTime;
            Place(_elapsed);

            if (_elapsed < _def.FlightSeconds) return;

            _done = true;

            // Cut the buzz before the warhead, not after.
            if (_buzz != null) { EffectAudio.Stop(_buzz); _buzz = null; }

            Impact?.Invoke(_targetLat, _targetLon);
            Destroy(gameObject);
        }

        /// <summary>
        /// Puts the drone where it should be at <paramref name="t"/> seconds into
        /// the flight: level along the run-in, then nosing over into the dive.
        /// </summary>
        void Place(float t)
        {
            float cruise = Mathf.Max(0.01f, _def.cruiseSeconds);
            float dive = Mathf.Max(0.01f, _def.diveSeconds);

            double lat, lon;
            float altitude;
            float pitch;

            if (t <= cruise)
            {
                // Cruise: closes most of the ground, holding altitude. It stops
                // short of the objective so the dive has somewhere to happen.
                float u = t / cruise;
                float closed = u * DiveStartFraction;
                lat = _startLat + (_targetLat - _startLat) * closed;
                lon = _startLon + (_targetLon - _startLon) * closed;
                altitude = _def.cruiseAltitudeMeters;

                // Nose tips down over the last of the cruise, so the dive is
                // entered rather than snapped into.
                pitch = Mathf.Lerp(0f, _def.diveAngleDeg, Mathf.Clamp01((u - 0.75f) / 0.25f));
            }
            else
            {
                // Dive: the rest of the ground, and all of the height.
                float u = Mathf.Clamp01((t - cruise) / dive);
                float closed = Mathf.Lerp(DiveStartFraction, 1f, u);
                lat = _startLat + (_targetLat - _startLat) * closed;
                lon = _startLon + (_targetLon - _startLon) * closed;

                // Accelerating downward rather than easing: a diving munition
                // gains speed all the way in.
                altitude = Mathf.Lerp(_def.cruiseAltitudeMeters, 0f, u * u);
                pitch = _def.diveAngleDeg;
            }

            _anchor.longitudeLatitudeHeight = new double3(lon, lat, _groundHeight + altitude);
            transform.localRotation = Quaternion.Euler(pitch, _headingDeg, 0f);
        }

        /// <summary>
        /// How much of the run-in is flown level before the dive starts. The
        /// remainder is the dive's own ground run — at a 60-odd degree dive from
        /// a few hundred metres, that is a short distance, which is why this is
        /// most of the way there.
        /// </summary>
        const float DiveStartFraction = 0.82f;

        void OnDestroy()
        {
            if (_buzz != null) EffectAudio.Stop(_buzz);
        }
    }
}
