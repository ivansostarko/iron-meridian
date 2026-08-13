using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The visible record of a march: a solid team-coloured trail behind the
    /// unit showing the ground it has actually covered, and a faint dashed
    /// thread ahead showing the route it still intends to take. Both are
    /// clamped to the terrain, so the trail reads as tracks on the ground in
    /// 3D and as a drawn route line in 2D.
    ///
    /// Battle-mode only, because marching is: repositioning a counter in the
    /// scenario editor is an edit, and an edit leaves no tracks. The trail is
    /// created by <see cref="UnitMover"/> when a march starts and fades itself
    /// out a few seconds after the unit arrives (or the order is cancelled), so
    /// nothing here needs cleaning up by the caller.
    /// </summary>
    public class MoveTrail : MonoBehaviour
    {
        /// <summary>Ground covered between recorded trail vertices, metres.</summary>
        const double SampleIntervalM = 220.0;
        /// <summary>Cap on recorded vertices; a corps-scale march would otherwise grow without bound.</summary>
        const int MaxPoints = 220;
        /// <summary>Seconds the finished trail lingers before it has faded out completely.</summary>
        const float FadeSeconds = 5.5f;
        /// <summary>Metres above the sampled terrain — enough to clear LOD changes under the line.</summary>
        const double ClearanceM = 12.0;
        const float TravelledWidth = 42f;
        const float PlannedWidth = 26f;

        CesiumGeoreference _geo;
        LineRenderer _travelled, _planned;
        Material _travelledMat, _plannedMat;
        Color _color;

        readonly List<Vector3> _trailWorld = new List<Vector3>();
        readonly List<GeoPoint> _trailGeo = new List<GeoPoint>();
        readonly List<GeoPoint> _route = new List<GeoPoint>();

        double _lastLat, _lastLon, _headHeight;
        bool _hasLast;
        float _fadeT = -1f;                  // < 0 while the march is still running

        public static MoveTrail Create(CesiumGeoreference geo, IReadOnlyList<GeoPoint> route, Color color)
        {
            var go = new GameObject("MoveTrail");
            go.transform.SetParent(geo.transform, false);

            var trail = go.AddComponent<MoveTrail>();
            trail._geo = geo;
            trail._color = color;
            trail._travelledMat = RuntimeMaterials.UnlitColor(color);
            trail._plannedMat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color, 64, 0.45f));
            trail._travelled = trail.BuildRenderer("Travelled", TravelledWidth, trail._travelledMat);
            trail._planned = trail.BuildRenderer("Planned", PlannedWidth, trail._plannedMat);

            trail.SetRoute(route);
            geo.changed += trail.OnGeoChanged;
            return trail;
        }

        LineRenderer BuildRenderer(string name, float width, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Tile;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.startWidth = lr.endWidth = width;
            lr.material = material;
            lr.positionCount = 0;
            return lr;
        }

        /// <summary>
        /// Replaces the planned route ahead of the unit. Called when the march
        /// starts and whenever the mover moves on to the next leg, so the dashed
        /// thread only ever shows ground the unit has not covered yet.
        /// </summary>
        public void SetRoute(IReadOnlyList<GeoPoint> remaining)
        {
            _route.Clear();
            if (remaining != null) _route.AddRange(remaining);
            RebuildPlanned();
        }

        /// <summary>Records where the unit is now; called every frame by the mover.</summary>
        public void Track(double lat, double lon)
        {
            if (_fadeT >= 0f) return;

            if (_hasLast &&
                GeoUtils.DistanceKm(_lastLat, _lastLon, lat, lon) * 1000.0 < SampleIntervalM)
            {
                // Between samples the head vertex follows the unit, so the trail
                // stays attached to the icon instead of lagging a sample behind.
                // It reuses the last sampled ground height rather than raycasting
                // again — a per-frame terrain sample per marching unit is exactly
                // the cost the sampling cadence exists to avoid.
                int head = _trailWorld.Count - 1;
                if (head >= 0)
                {
                    _trailGeo[head] = new GeoPoint { latitude = lat, longitude = lon };
                    _trailWorld[head] = GeoUtils.GeoToUnity(_geo, lat, lon, _headHeight);
                    _travelled.SetPosition(head, _trailWorld[head]);
                }
                return;
            }

            Append(lat, lon);
            _lastLat = lat; _lastLon = lon; _hasLast = true;
        }

        void Append(double lat, double lon)
        {
            if (_trailGeo.Count >= MaxPoints)
            {
                _trailGeo.RemoveAt(0);
                _trailWorld.RemoveAt(0);
            }
            _headHeight = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250.0) + ClearanceM;
            _trailGeo.Add(new GeoPoint { latitude = lat, longitude = lon });
            _trailWorld.Add(GeoUtils.GeoToUnity(_geo, lat, lon, _headHeight));

            _travelled.positionCount = _trailWorld.Count;
            for (int i = 0; i < _trailWorld.Count; i++) _travelled.SetPosition(i, _trailWorld[i]);
        }

        /// <summary>Starts the fade-out. The object destroys itself when it finishes.</summary>
        public void Finish()
        {
            if (_fadeT < 0f) _fadeT = 0f;
            SetRoute(null);
        }

        void Update()
        {
            if (_fadeT < 0f) return;
            _fadeT += Time.unscaledDeltaTime;

            float a = Mathf.Clamp01(1f - _fadeT / FadeSeconds);
            var c = _color; c.a = a;
            _travelledMat.color = c;

            if (_fadeT >= FadeSeconds) Destroy(gameObject);
        }

        Vector3 Clamped(double lat, double lon)
        {
            double h = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250.0) + ClearanceM;
            return GeoUtils.GeoToUnity(_geo, lat, lon, h);
        }

        void RebuildPlanned()
        {
            if (_planned == null) return;
            _planned.positionCount = _route.Count >= 2 ? _route.Count : 0;
            for (int i = 0; i < _planned.positionCount; i++)
                _planned.SetPosition(i, Clamped(_route[i].latitude, _route[i].longitude));

            var c = _color; c.a = 0.55f;
            _plannedMat.color = c;
        }

        /// <summary>
        /// Cesium re-origins the georeference as the camera roams; world-space
        /// LineRenderer positions have to be recomputed when it does or the
        /// whole trail slides off the terrain.
        /// </summary>
        void OnGeoChanged()
        {
            for (int i = 0; i < _trailGeo.Count; i++)
                _trailWorld[i] = Clamped(_trailGeo[i].latitude, _trailGeo[i].longitude);
            _travelled.positionCount = _trailWorld.Count;
            for (int i = 0; i < _trailWorld.Count; i++) _travelled.SetPosition(i, _trailWorld[i]);
            RebuildPlanned();
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= OnGeoChanged;
            if (_travelledMat != null) Destroy(_travelledMat);
            if (_plannedMat != null) Destroy(_plannedMat);
        }
    }
}
