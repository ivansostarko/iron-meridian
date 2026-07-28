using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Smooth geodetic movement with ease-in/ease-out, heading turn and an
    /// animated destination marker. Speed comes from the unit definition
    /// (km/h) accelerated by GameConfig.MoveSpeedMultiplier game time.
    /// </summary>
    public class UnitMover : MonoBehaviour
    {
        UnitActor _actor;
        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;

        double _fromLat, _fromLon, _toLat, _toLon;
        float _t;                  // 0..1 progress
        float _duration;           // seconds
        bool _moving;
        GameObject _marker;

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

            double km = GeoUtils.DistanceKm(_fromLat, _fromLon, _toLat, _toLon);
            float speed = Mathf.Max(1f, _actor.Def.speedKmh) * GameConfig.MoveSpeedMultiplier;
            _duration = Mathf.Max(0.6f, (float)(km / speed * 3600.0));
            _t = 0f;
            _moving = true;
            s.status = UnitStatus.Moving.ToString();
            s.headingDeg = GeoUtils.BearingDeg(_fromLat, _fromLon, _toLat, _toLon);

            SpawnMarker(lat, lon);

            // Fuel cost
            if (_actor.Def.fuelUsePerKm > 0)
                s.fuel = Mathf.Max(0, s.fuel - (float)km * _actor.Def.fuelUsePerKm);
        }

        void Update()
        {
            if (!_moving) return;
            _t += Time.deltaTime / _duration;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));   // ease in/out

            var s = _actor.State;
            s.latitude = _fromLat + (_toLat - _fromLat) * k;
            s.longitude = _fromLon + (_toLon - _fromLon) * k;

            double h = GeoUtils.SampleTerrainHeight(_geo, s.latitude, s.longitude, s.heightMeters);
            s.heightMeters = h;

            // Slight hover bob while moving makes motion readable at map scale
            double bob = math.sin(_t * math.PI) * 12.0;
            _anchor.longitudeLatitudeHeight = new double3(s.longitude, s.latitude, h + 2.0 + bob);

            if (_t >= 1f)
            {
                _moving = false;
                s.status = UnitStatus.Idle.ToString();
                UnitRegistry.NotifyMoved();
            }
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
            var mat = new Material(Shader.Find("Sprites/Default"));
            var color = _actor.State.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam;
            mat.mainTexture = ProceduralTextures.Ring(color, 128, 0.30f, 0.42f);
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
