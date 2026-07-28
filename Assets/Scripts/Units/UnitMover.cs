using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Battle-time travel: the unit marches along the great circle to its
    /// objective, accelerating to a march speed, holding it, then slowing at
    /// the destination, turning onto its course as it goes. Motion is strictly
    /// along the ground — the unit stays clamped to the terrain and never
    /// hovers. Speed comes from the unit definition (km/h) accelerated by
    /// GameConfig.MoveSpeedMultiplier.
    ///
    /// Repositioning a unit in the map editor is not a move: that is
    /// <see cref="UnitActor.SetPosition"/>, which is instant and unanimated.
    /// </summary>
    public class UnitMover : MonoBehaviour
    {
        UnitActor _actor;
        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;

        /// <summary>Seconds a unit takes to reach its cruising speed from a standstill.</summary>
        const float AccelSeconds = 3.5f;
        /// <summary>Turn rate for the smallest formation, degrees/second.</summary>
        const float BaseTurnRateDegPerSec = 110f;
        /// <summary>Course change beyond which the unit halts and pivots before moving off.</summary>
        const float PivotThresholdDeg = 28f;
        /// <summary>Seconds between terrain height samples while travelling (3D only).</summary>
        const float TerrainSampleInterval = 0.12f;
        /// <summary>Exponential rate at which the unit settles onto the sampled height.</summary>
        const float HeightEaseRate = 9f;
        /// <summary>Constant altitude used in the flat 2D map view.</summary>
        const float FlatMapHeight = 250f;

        double _fromLat, _fromLon, _toLat, _toLon;
        double _totalKm;
        double _travelledM;        // metres covered along the course
        float _courseBearing;      // great-circle heading at departure
        float _speedMps, _cruiseMps;
        bool _moving, _pivoting;
        GameObject _marker;
        float _sampleTimer;
        double _targetHeight;

        public bool IsMoving => _moving;

        public void Init(UnitActor actor, CesiumGeoreference geo, CesiumGlobeAnchor anchor)
        {
            _actor = actor; _geo = geo; _anchor = anchor;
        }

        public void MoveTo(double lat, double lon)
        {
            var s = _actor.State;
            _fromLat = s.latitude; _fromLon = s.longitude;
            _toLat = lat; _toLon = lon;

            _totalKm = GeoUtils.DistanceKm(_fromLat, _fromLon, _toLat, _toLon);
            _courseBearing = GeoUtils.BearingDeg(_fromLat, _fromLon, _toLat, _toLon);
            _cruiseMps = Mathf.Max(1f, _actor.Def.speedKmh) * GameConfig.MoveSpeedMultiplier / 3.6f;
            _travelledM = 0.0;
            _speedMps = 0f;
            _moving = true;
            // A formation swings onto its heading before rolling, and the
            // bigger the formation the longer that takes.
            _pivoting = Mathf.Abs(Mathf.DeltaAngle(s.headingDeg, _courseBearing)) > PivotThresholdDeg;
            _sampleTimer = 0f;                 // sample the ground on the first frame
            _targetHeight = s.heightMeters;
            s.status = UnitStatus.Moving.ToString();

            SpawnMarker(lat, lon);

            // Fuel cost
            if (_actor.Def.fuelUsePerKm > 0)
                s.fuel = Mathf.Max(0, s.fuel - (float)_totalKm * _actor.Def.fuelUsePerKm);
        }

        /// <summary>Degrees/second this formation can turn — larger echelons swing wider.</summary>
        float TurnRate()
        {
            float bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(_actor.State.EchelonEnum));
            return Mathf.Clamp(BaseTurnRateDegPerSec / Mathf.Sqrt(bulk), 12f, BaseTurnRateDegPerSec);
        }

        /// <summary>Abandons any move in progress, leaving the unit where it stands.</summary>
        public void Cancel()
        {
            if (!_moving) return;
            _moving = false;
            if (_marker != null) { Destroy(_marker); _marker = null; }
            if (_actor.State.status == UnitStatus.Moving.ToString())
                _actor.State.status = UnitStatus.Idle.ToString();
        }

        void Update()
        {
            if (!_moving) return;
            var s = _actor.State;
            float dt = Time.deltaTime;
            float turn = TurnRate() * dt;

            // --- 1. Pivot in place onto the course before setting off.
            if (_pivoting)
            {
                s.headingDeg = Mathf.MoveTowardsAngle(s.headingDeg, _courseBearing, turn);
                if (s.headingDeg < 0f) s.headingDeg += 360f;
                if (Mathf.Abs(Mathf.DeltaAngle(s.headingDeg, _courseBearing)) < 1f) _pivoting = false;
                return;
            }

            // --- 2. Accelerate, cruise, then brake so the unit stops ON the
            // objective. Braking distance comes from v²/2a, which is why this
            // is driven by real distance rather than a normalised 0..1 curve —
            // a long march no longer spends 15% of its length easing away from
            // the start line.
            double totalM = _totalKm * 1000.0;
            double remainingM = System.Math.Max(0.0, totalM - _travelledM);
            float accel = Mathf.Max(0.5f, _cruiseMps / AccelSeconds);
            float brakingM = _speedMps * _speedMps / (2f * accel);

            _speedMps = remainingM <= brakingM
                ? Mathf.MoveTowards(_speedMps, 0f, accel * dt)
                : Mathf.MoveTowards(_speedMps, _cruiseMps, accel * dt);

            // Course corrections cost speed, the way a column slows through a
            // turn. This scales the ground covered this frame only — folding it
            // back into _speedMps would compound the penalty and corrupt the
            // braking distance above.
            float offCourse = Mathf.Abs(Mathf.DeltaAngle(s.headingDeg, _courseBearing));
            float turnPenalty = offCourse > 1f
                ? Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(offCourse / 90f))
                : 1f;

            _travelledM += _speedMps * turnPenalty * dt;
            if (_travelledM > totalM) _travelledM = totalM;

            // Follow the great circle: a start point plus an initial bearing
            // defines it, so stepping the travelled distance along that bearing
            // traces the real path instead of lerping lat/lon independently
            // (which bows off-course over longer distances).
            GeoUtils.Destination(_fromLat, _fromLon, _courseBearing, _travelledM / 1000.0,
                out double lat, out double lon);
            s.latitude = lat;
            s.longitude = lon;

            // Keep steering at whatever the bearing to the objective is now.
            float want = GeoUtils.BearingDeg(lat, lon, _toLat, _toLon);
            s.headingDeg = Mathf.MoveTowardsAngle(s.headingDeg, want, turn);
            if (s.headingDeg < 0f) s.headingDeg += 360f;

            UpdateGroundHeight(lat, lon, s);
            _anchor.longitudeLatitudeHeight = new double3(lon, lat, s.heightMeters + 2.0);

            // Arrived once the distance is used up (or the unit has stalled on
            // the doorstep, which a pure speed test would never resolve).
            if (_travelledM >= totalM - 0.01 || (_speedMps <= 0.01f && remainingM < 1.0))
            {
                _moving = false;
                s.latitude = _toLat;
                s.longitude = _toLon;
                s.status = UnitStatus.Idle.ToString();
                UnitRegistry.NotifyMoved();
            }
        }

        /// <summary>
        /// Keeps the unit sitting on the ground as it travels.
        ///
        /// In 3D the terrain is sampled on a fixed cadence rather than every
        /// frame — a per-frame physics raycast per moving unit is expensive,
        /// and it made units jitter vertically whenever a tile streamed in at a
        /// different resolution. Between samples the height eases toward the
        /// target, which reads as a vehicle riding the contour instead of
        /// snapping to it.
        ///
        /// In 2D the map is flat, so terrain sampling is skipped entirely and
        /// the unit glides at a constant height.
        /// </summary>
        void UpdateGroundHeight(double lat, double lon, UnitState s)
        {
            bool flat = MapManager.Active != null && MapManager.Active.ViewMode == ViewMode.Mode2D;
            if (flat)
            {
                s.heightMeters = Mathf.Lerp((float)s.heightMeters, FlatMapHeight,
                    1f - Mathf.Exp(-HeightEaseRate * Time.deltaTime));
                return;
            }

            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = TerrainSampleInterval;
                _targetHeight = GeoUtils.SampleTerrainHeight(_geo, lat, lon, s.heightMeters);
            }

            s.heightMeters = Mathf.Lerp((float)s.heightMeters, (float)_targetHeight,
                1f - Mathf.Exp(-HeightEaseRate * Time.deltaTime));
        }


        void SpawnMarker(double lat, double lon)
        {
            if (_marker != null) Destroy(_marker);
            _marker = new GameObject("MoveMarker");
            _marker.transform.SetParent(_geo.transform, false);
            var anchor = _marker.AddComponent<CesiumGlobeAnchor>();
            double h = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250);
            anchor.longitudeLatitudeHeight = new double3(lon, lat, h + 4.0);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_marker.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var color = _actor.State.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam;
            var mat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Ring(color, 128, 0.30f, 0.42f));
            quad.GetComponent<MeshRenderer>().material = mat;

            _marker.AddComponent<MarkerPing>().Init(quad.transform, mat);
        }

        /// <summary>Expanding, fading ring played at the move destination.</summary>
        class MarkerPing : MonoBehaviour
        {
            Transform _quad; Material _mat; float _t;
            public void Init(Transform quad, Material mat) { _quad = quad; _mat = mat; }
            void Update()
            {
                _t += Time.deltaTime;
                float cycle = _t % 1.2f / 1.2f;
                _quad.localScale = Vector3.one * Mathf.Lerp(120f, 620f, cycle);
                var c = _mat.color; c.a = 1f - cycle; _mat.color = c;
                if (_t > 6f) Destroy(gameObject);   // marker lives ~6 s
            }
        }
    }
}
