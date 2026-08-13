using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// Battle-time travel. The unit does not drive straight at its objective:
    /// <see cref="RoutePlanner"/> lays a road-like route over the terrain first,
    /// and the unit marches that route leg by leg — accelerating to a march
    /// speed, easing off through the bends, and braking onto the objective.
    /// Motion is strictly along the ground; the unit stays clamped to the
    /// terrain and never hovers. Speed comes from the unit definition (km/h)
    /// accelerated by GameConfig.MoveSpeedMultiplier.
    ///
    /// **Game mode only.** Marching, its animation and its trail all belong to a
    /// running battle. Repositioning a unit in the scenario editor is not a move
    /// — that is <see cref="UnitActor.SetPosition"/>, which is instant and
    /// unanimated — and a march already under way is abandoned the moment the
    /// battle stops, handing the map back to the editor with every unit standing
    /// where it actually is.
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
        /// <summary>Seconds between terrain height samples while travelling.</summary>
        const float TerrainSampleInterval = 0.12f;
        /// <summary>Exponential rate at which the unit settles onto the sampled height.</summary>
        const float HeightEaseRate = 9f;
        /// <summary>Fraction of cruise speed a column carries through a right-angle bend.</summary>
        const float CornerSpeedFraction = 0.35f;
        /// <summary>
        /// Ground covered between dust puffs along a march. Distance-based, not
        /// time-based, so the trail has consistent spacing on the map whatever
        /// the formation's speed. See docs/08-PARTICLE-SYSTEMS.md.
        /// </summary>
        const double DustIntervalM = 500.0;

        /// <summary>Planned route, start point first, objective last.</summary>
        readonly List<GeoPoint> _route = new List<GeoPoint>();
        /// <summary>Leg being driven: from _route[_leg] to _route[_leg + 1].</summary>
        int _leg;

        double _legFromLat, _legFromLon, _legToLat, _legToLon;
        double _legTotalM, _legTravelledM;
        float _courseBearing;      // great-circle heading at the start of this leg
        float _speedMps, _cruiseMps;
        bool _moving, _pivoting;
        float? _faceOnArrival;
        GameObject _marker;
        MoveTrail _trail;
        float _sampleTimer;
        double _targetHeight;
        double _travelledTotalM;
        double _nextDustAtM;

        public bool IsMoving => _moving;

        /// <summary>The route currently being driven — start point first. Empty when idle.</summary>
        public IReadOnlyList<GeoPoint> Route => _route;

        public void Init(UnitActor actor, CesiumGeoreference geo, CesiumGlobeAnchor anchor)
        {
            _actor = actor; _geo = geo; _anchor = anchor;
        }

        /// <summary>
        /// Orders a march to a geodetic point. Returns false — and does nothing
        /// — outside a running battle, where the caller should be repositioning
        /// with <see cref="UnitActor.SetPosition"/> instead.
        /// </summary>
        /// <param name="faceOnArrival">
        /// Heading (deg from north) to settle onto once the objective is
        /// reached; null leaves the unit facing along its final leg. Defensive
        /// tasks use it so a formation ends up oriented on the threat rather
        /// than on whichever way the road happened to run.
        /// </param>
        public bool MoveTo(double lat, double lon, float? faceOnArrival = null)
        {
            if (!CombatSystem.BattleRunning) return false;

            var s = _actor.State;
            var route = RoutePlanner.Plan(_geo, s.latitude, s.longitude, lat, lon);
            if (route.Count < 2) return false;

            if (_marker != null) Destroy(_marker);
            if (_trail != null) _trail.Finish();

            _route.Clear();
            _route.AddRange(route);
            _leg = 0;
            _faceOnArrival = faceOnArrival;

            _cruiseMps = Mathf.Max(1f, _actor.Def.speedKmh) * GameConfig.MoveSpeedMultiplier / 3.6f;
            _speedMps = 0f;
            _moving = true;
            _sampleTimer = 0f;                 // sample the ground on the first frame
            _targetHeight = s.heightMeters;
            _travelledTotalM = 0.0;
            _nextDustAtM = DustIntervalM;      // no puff on the start line itself
            s.status = UnitStatus.Moving.ToString();

            BeginLeg();
            // A formation swings onto its heading before rolling, and the
            // bigger the formation the longer that takes. Only on the first leg:
            // later bends are driven through, not stopped for.
            _pivoting = Mathf.Abs(Mathf.DeltaAngle(s.headingDeg, _courseBearing)) > PivotThresholdDeg;

            SpawnMarker(lat, lon);
            _trail = MoveTrail.Create(_geo, _route,
                s.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);
            _trail.Track(s.latitude, s.longitude);

            // Fuel is charged against the route actually driven, not the
            // straight line — going round the high ground costs more.
            double routeKm = RoutePlanner.LengthKm(_route);
            if (_actor.Def.fuelUsePerKm > 0)
                s.fuel = Mathf.Max(0, s.fuel - (float)routeKm * _actor.Def.fuelUsePerKm);

            return true;
        }

        void BeginLeg()
        {
            var a = _route[_leg];
            var b = _route[_leg + 1];
            _legFromLat = a.latitude; _legFromLon = a.longitude;
            _legToLat = b.latitude; _legToLon = b.longitude;
            _legTotalM = GeoUtils.DistanceKm(_legFromLat, _legFromLon, _legToLat, _legToLon) * 1000.0;
            _legTravelledM = 0.0;
            _courseBearing = GeoUtils.BearingDeg(_legFromLat, _legFromLon, _legToLat, _legToLon);
        }

        /// <summary>
        /// Speed the unit should be doing as it crosses the end of the current
        /// leg: zero on the objective, otherwise a cornering speed set by how
        /// sharp the next bend is. This is what stops a routed march from
        /// halting at every waypoint.
        /// </summary>
        float ExitSpeed()
        {
            if (_leg >= _route.Count - 2) return 0f;

            float next = GeoUtils.BearingDeg(_route[_leg + 1].latitude, _route[_leg + 1].longitude,
                                             _route[_leg + 2].latitude, _route[_leg + 2].longitude);
            float turn = Mathf.Abs(Mathf.DeltaAngle(_courseBearing, next));
            return _cruiseMps * Mathf.Lerp(1f, CornerSpeedFraction, Mathf.Clamp01(turn / 90f));
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
            _route.Clear();
            _faceOnArrival = null;
            if (_marker != null) { Destroy(_marker); _marker = null; }
            if (_trail != null) { _trail.Finish(); _trail = null; }
            if (_actor.State.status == UnitStatus.Moving.ToString())
                _actor.State.status = UnitStatus.Idle.ToString();
        }

        void Update()
        {
            if (!_moving) return;

            // Leaving battle hands the map back to the scenario editor, and an
            // order still playing out there would keep moving a counter the
            // designer is trying to place.
            if (!CombatSystem.BattleRunning) { Cancel(); return; }

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

            // --- 2. Accelerate, cruise, then bleed speed off to whatever the end
            // of this leg calls for. Braking distance comes from (v² - vExit²)/2a,
            // which is why this is driven by real distance rather than a
            // normalised 0..1 curve — a long march no longer spends 15% of its
            // length easing away from the start line, and a gentle bend halfway
            // along costs a little speed instead of a full stop.
            double remainingM = System.Math.Max(0.0, _legTotalM - _legTravelledM);
            float accel = Mathf.Max(0.5f, _cruiseMps / AccelSeconds);
            float exit = ExitSpeed();
            float brakingM = Mathf.Max(0f, (_speedMps * _speedMps - exit * exit) / (2f * accel));

            _speedMps = remainingM <= brakingM
                ? Mathf.MoveTowards(_speedMps, exit, accel * dt)
                : Mathf.MoveTowards(_speedMps, _cruiseMps, accel * dt);

            // Course corrections cost speed, the way a column slows through a
            // turn. This scales the ground covered this frame only — folding it
            // back into _speedMps would compound the penalty and corrupt the
            // braking distance above.
            float offCourse = Mathf.Abs(Mathf.DeltaAngle(s.headingDeg, _courseBearing));
            float turnPenalty = offCourse > 1f
                ? Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(offCourse / 90f))
                : 1f;

            double stepM = _speedMps * turnPenalty * dt;
            _legTravelledM += stepM;
            _travelledTotalM += stepM;
            if (_legTravelledM > _legTotalM) _legTravelledM = _legTotalM;

            // Follow the great circle: a leg's start point plus its initial
            // bearing defines it, so stepping the travelled distance along that
            // bearing traces the real path instead of lerping lat/lon
            // independently (which bows off-course over longer distances).
            GeoUtils.Destination(_legFromLat, _legFromLon, _courseBearing, _legTravelledM / 1000.0,
                out double lat, out double lon);
            s.latitude = lat;
            s.longitude = lon;

            // Dust kicked up by the column. Lowest priority in the catalogue, so
            // a mass advance sheds its trail rather than crowding out combat
            // effects when the concurrent budget is reached.
            if (_travelledTotalM >= _nextDustAtM)
            {
                _nextDustAtM = _travelledTotalM + DustIntervalM;
                VfxSystem.Play(VfxId.Dust, lat, lon,
                    Mathf.Lerp(0.6f, 1.3f, _actor.FormationScale01));
            }

            // Keep steering at whatever the bearing to this leg's waypoint is now.
            float want = GeoUtils.BearingDeg(lat, lon, _legToLat, _legToLon);
            s.headingDeg = Mathf.MoveTowardsAngle(s.headingDeg, want, turn);
            if (s.headingDeg < 0f) s.headingDeg += 360f;

            UpdateGroundHeight(lat, lon, s);
            _anchor.longitudeLatitudeHeight = new double3(lon, lat, s.heightMeters + 2.0);

            if (_trail != null) _trail.Track(lat, lon);

            // Leg used up (or the unit has stalled on the doorstep, which a pure
            // speed test would never resolve).
            bool legDone = _legTravelledM >= _legTotalM - 0.01 ||
                           (_speedMps <= 0.01f && remainingM < 1.0);
            if (!legDone) return;

            s.latitude = _legToLat;
            s.longitude = _legToLon;

            if (_leg < _route.Count - 2)
            {
                _leg++;
                BeginLeg();
                // The dashed thread ahead now shows only what is left to drive.
                if (_trail != null) _trail.SetRoute(_route.GetRange(_leg, _route.Count - _leg));
                return;
            }

            Arrive(s);
        }

        void Arrive(UnitState s)
        {
            _moving = false;
            _route.Clear();
            s.status = UnitStatus.Idle.ToString();
            if (_faceOnArrival.HasValue) _actor.SetHeading(_faceOnArrival.Value);
            _faceOnArrival = null;
            if (_trail != null) { _trail.Finish(); _trail = null; }
            UnitRegistry.NotifyMoved();
        }

        /// <summary>
        /// Keeps the unit sitting on the ground as it travels, in both view
        /// modes. 2D used to glide at a constant height instead — the map looks
        /// flat from straight above, so nobody could tell — but that left a unit
        /// at a different height depending on how it got there, and dropped it
        /// onto the terrain the moment the view was switched to 3D. The ground
        /// is the same ground in both views, so the unit rides it in both.
        ///
        /// The terrain is sampled on a fixed cadence rather than every frame — a
        /// per-frame physics raycast per moving unit is expensive, and it made
        /// units jitter vertically whenever a tile streamed in at a different
        /// resolution. Between samples the height eases toward the target, which
        /// reads as a vehicle riding the contour instead of snapping to it.
        /// </summary>
        void UpdateGroundHeight(double lat, double lon, UnitState s)
        {
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

        void OnDestroy()
        {
            // Both outlive this component's GameObject: the ping is parented to
            // the georeference, and the trail keeps fading after the unit dies.
            if (_marker != null) Destroy(_marker);
            if (_trail != null) _trail.Finish();
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
