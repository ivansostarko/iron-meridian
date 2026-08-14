using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Map;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One reconnaissance sortie: transit to the objective, hold an orbit over
    /// it while the sensor works, then go home.
    ///
    /// It is a separate flight from <see cref="DroneRun"/> because it is the
    /// opposite manoeuvre. A loitering munition's flight is a countdown to a
    /// single moment and ends in the ground; this one *is* the mission, and the
    /// interesting part is the middle. There is no dive, no impact and no
    /// callback saying where the warhead went — instead there is a moment the
    /// drone reaches the objective (<see cref="OnStation"/>), a period during
    /// which it is working, and a moment it leaves.
    ///
    /// Three phases:
    ///   **Ingress**  — straight in from <see cref="UavDef.approachKm"/> away on
    ///                  a random bearing, at cruise altitude. Real seconds: this
    ///                  is travel the player watches, not time the battle spends.
    ///   **Station**  — a circular orbit of <see cref="UavDef.orbitRadiusMeters"/>
    ///                  around the objective, banked into the turn, for
    ///                  <see cref="UavDef.onStationMinutes"/> of **scenario**
    ///                  time. That is the part that costs the player something,
    ///                  so it is measured on the operational clock and runs at
    ///                  whatever speed the battle is being watched at — five
    ///                  minutes at x1, five seconds at x60, frozen when paused.
    ///   **Egress**   — back out along the run-in bearing, climbing away, and
    ///                  the object destroys itself at the end of it.
    ///
    /// The engine note travels with the airframe for the whole sortie and stops
    /// with it, which is what makes a drone leaving read as a drone leaving
    /// rather than as one vanishing.
    ///
    /// See docs/19-UAV-STRIKES.md.
    /// </summary>
    public class ReconDroneRun : MonoBehaviour
    {
        /// <summary>Raised once, when the drone reaches the objective and begins working.</summary>
        public System.Action OnStation;
        /// <summary>Raised once, when it turns for home — the sensor is off from this moment.</summary>
        public System.Action OffStation;

        enum Phase { Ingress, Station, Egress }

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        UavDef _def;
        AudioSource _engine;

        double _startLat, _startLon, _targetLat, _targetLon;
        double _groundHeight;
        float _headingDeg;

        Phase _phase = Phase.Ingress;
        /// <summary>Seconds spent in the current phase — real for transits, scenario on station.</summary>
        float _elapsed;
        /// <summary>Where on the orbit the drone is, radians. Kept across frames so the circle is continuous.</summary>
        float _orbit;

        /// <summary>Scenario seconds still to run on station; 0 outside that phase.</summary>
        public float StationSecondsLeft =>
            _phase == Phase.Station ? Mathf.Max(0f, _def.onStationMinutes * 60f - _elapsed) : 0f;

        /// <summary>
        /// Sends a drone to a ground point. Returns null — having logged why —
        /// if the model cannot be built, so the caller can still run the mission
        /// rather than losing it to a missing asset.
        /// </summary>
        public static ReconDroneRun Launch(CesiumGeoreference geo, UavDef def,
            double targetLat, double targetLon, float headingDeg)
        {
            var go = new GameObject("ReconRun_" + def.label);
            go.transform.SetParent(geo.transform, false);

            var model = UnitModelLibrary.CreateInstance(def.modelId, go.transform);
            if (model == null)
            {
                Debug.LogWarning($"[ReconDroneRun] No usable model for '{def.modelId}' — " +
                    "the sortie will still run, but with nothing to watch.");
                Destroy(go);
                return null;
            }

            var run = go.AddComponent<ReconDroneRun>();
            run._geo = geo;
            run._def = def;
            run._headingDeg = headingDeg;
            run._targetLat = targetLat;
            run._targetLon = targetLon;
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run.PlanTrack();
            run.ShapeModel(model);

            run._engine = EffectAudio.PlayAt(def.engineSound, go.transform.position,
                def.spanMeters * 6f, go.transform);

            return run;
        }

        void PlanTrack()
        {
            _groundHeight = GeoUtils.SampleTerrainHeight(_geo, _targetLat, _targetLon, 250.0);

            // Comes in from behind the objective on the run-in bearing, and
            // leaves the same way.
            GeoUtils.Destination(_targetLat, _targetLon, _headingDeg + 180f, _def.approachKm,
                out _startLat, out _startLon);

            PlaceIngress(0f);
        }

        /// <summary>Sizes and orients an already-built model on the airframe root.</summary>
        void ShapeModel(GameObject model)
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Scaled from the model's own bounds rather than a magic number, so a
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
            switch (_phase)
            {
                case Phase.Ingress: TickIngress(); break;
                case Phase.Station: TickStation(); break;
                case Phase.Egress: TickEgress(); break;
            }
        }

        void TickIngress()
        {
            // Unscaled: the transit is the player watching something happen, and
            // it must finish even with the battle paused — the same argument the
            // strike countdown makes.
            _elapsed += Time.unscaledDeltaTime;
            PlaceIngress(_elapsed);

            if (_elapsed < Mathf.Max(0.01f, _def.transitSeconds)) return;

            _phase = Phase.Station;
            _elapsed = 0f;
            OnStation?.Invoke();
        }

        void TickStation()
        {
            // Scenario time: this is the part of the sortie that costs the
            // player minutes of the battle. See GameClock.ScenarioDelta.
            float dt = GameClock.ScenarioDelta;
            _elapsed += dt;

            // The orbit itself is drawn in real time regardless, so the drone
            // keeps flying while the battle is paused rather than hanging
            // motionless in the sky over a stopped world.
            _orbit += Time.unscaledDeltaTime * OrbitRadiansPerSecond;
            PlaceOrbit();

            if (_elapsed < _def.onStationMinutes * 60f) return;

            _phase = Phase.Egress;
            _elapsed = 0f;
            OffStation?.Invoke();
        }

        void TickEgress()
        {
            _elapsed += Time.unscaledDeltaTime;
            float duration = Mathf.Max(0.01f, _def.transitSeconds);
            PlaceEgress(Mathf.Clamp01(_elapsed / duration));

            if (_elapsed < duration) return;

            if (_engine != null) { EffectAudio.Stop(_engine); _engine = null; }
            Destroy(gameObject);
        }

        /// <summary>How fast the orbit is flown. One circuit every ~14 seconds of watching.</summary>
        const float OrbitRadiansPerSecond = 0.45f;

        // --------------------------------------------------------- placement

        void PlaceIngress(float t)
        {
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, _def.transitSeconds));

            // Closes on the point the orbit starts at rather than on the
            // objective itself, so the turn onto station is a continuation of
            // the run-in instead of an overshoot and a doubling back.
            EntryPoint(out double entryLat, out double entryLon);

            double lat = _startLat + (entryLat - _startLat) * u;
            double lon = _startLon + (entryLon - _startLon) * u;

            // Descends into the working altitude over the last of the run-in.
            float altitude = Mathf.Lerp(_def.cruiseAltitudeMeters * 1.25f,
                _def.cruiseAltitudeMeters, u);

            Set(lat, lon, altitude, _headingDeg, roll: 0f);
        }

        void PlaceOrbit()
        {
            OrbitPoint(_orbit, out double lat, out double lon);

            // Nose along the tangent, banked into the turn — a level aeroplane
            // flying a circle is the clearest tell that a thing on screen is a
            // sprite being moved rather than something flying.
            float bearing = OrbitBearingDeg(_orbit);
            Set(lat, lon, _def.cruiseAltitudeMeters, bearing, roll: 16f);
        }

        void PlaceEgress(float u)
        {
            OrbitPoint(_orbit, out double fromLat, out double fromLon);

            double lat = fromLat + (_startLat - fromLat) * u;
            double lon = fromLon + (_startLon - fromLon) * u;

            // Climbing away, so the departure reads as leaving rather than as
            // sliding sideways off the map.
            float altitude = Mathf.Lerp(_def.cruiseAltitudeMeters,
                _def.cruiseAltitudeMeters * 1.45f, u);

            Set(lat, lon, altitude, _headingDeg + 180f, roll: 0f);
        }

        /// <summary>The point on the orbit the run-in aims at.</summary>
        void EntryPoint(out double lat, out double lon) => OrbitPoint(0f, out lat, out lon);

        void OrbitPoint(float angleRad, out double lat, out double lon)
        {
            double km = _def.orbitRadiusMeters / 1000.0;
            // Bearing zero on the orbit is the side the drone arrives from, so
            // the run-in and the first leg of the circle agree.
            double bearing = _headingDeg + 180.0 + angleRad * Mathf.Rad2Deg;
            GeoUtils.Destination(_targetLat, _targetLon, bearing, km, out lat, out lon);
        }

        /// <summary>Nose bearing on the orbit: the tangent, 90° off the radius.</summary>
        float OrbitBearingDeg(float angleRad) =>
            _headingDeg + 180f + angleRad * Mathf.Rad2Deg + 90f;

        void Set(double lat, double lon, float altitude, float yawDeg, float roll)
        {
            _anchor.longitudeLatitudeHeight = new double3(lon, lat, _groundHeight + altitude);
            transform.localRotation = Quaternion.Euler(0f, yawDeg, roll);
        }

        void OnDestroy()
        {
            if (_engine != null) EffectAudio.Stop(_engine);
        }
    }
}
