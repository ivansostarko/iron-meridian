using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The visible record of a march: a fine team-coloured trail behind the unit
    /// showing the ground it has actually covered, a faint dashed thread ahead
    /// showing the route it still intends to take, and **arrowheads marching
    /// along that thread** so the direction of travel is stated rather than
    /// inferred. All of it is clamped to the terrain, so the trail reads as
    /// tracks on the ground in 3D and as a drawn route line in 2D.
    ///
    /// The lines are deliberately thin. They were three times this width, which
    /// on a corps-scale advance turned the map into a bundle of ribbons wide
    /// enough to hide the terrain being fought over — and a route line's job is
    /// to be followed, not to dominate. What was lost in presence is given back
    /// by the arrows and by the motes lifting off the head of the trail, both of
    /// which read at a glance without covering any ground.
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

        const float TravelledWidth = 15f;
        const float PlannedWidth = 9f;

        /// <summary>Ground between arrowheads on the planned route, metres.</summary>
        const float ArrowSpacingM = 620f;
        /// <summary>Arrowhead length and half-width, metres.</summary>
        const float ArrowLengthM = 190f;
        const float ArrowHalfWidthM = 85f;
        /// <summary>Metres per second the arrows march forward along the route.</summary>
        const float ArrowMarchMps = 140f;
        /// <summary>Hard cap on arrowheads, so a 100 km route does not build a thousand.</summary>
        const int MaxArrows = 64;

        CesiumGeoreference _geo;
        LineRenderer _travelled, _planned;
        Material _travelledMat, _plannedMat, _arrowMat;
        Color _color;

        Transform _arrows;
        UnityEngine.Mesh _arrowMesh;
        ParticleSystem _motes;

        readonly List<Vector3> _trailWorld = new List<Vector3>();
        readonly List<GeoPoint> _trailGeo = new List<GeoPoint>();
        readonly List<GeoPoint> _route = new List<GeoPoint>();

        /// <summary>Planned route in world space, with cumulative distance — the arrows ride this.</summary>
        readonly List<Vector3> _routeWorld = new List<Vector3>();
        readonly List<float> _routeCum = new List<float>();

        readonly List<Vector3> _arrowVerts = new List<Vector3>();
        readonly List<int> _arrowTris = new List<int>();
        readonly List<Color> _arrowColours = new List<Color>();

        double _lastLat, _lastLon, _headHeight;
        bool _hasLast;
        float _fadeT = -1f;                  // < 0 while the march is still running
        float _march;

        public static MoveTrail Create(CesiumGeoreference geo, IReadOnlyList<GeoPoint> route, Color color)
        {
            var go = new GameObject("MoveTrail");
            go.transform.SetParent(geo.transform, false);
            // Arrow vertices are computed from world positions and converted into
            // this object's space, so it must sit exactly on the georeference.
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var trail = go.AddComponent<MoveTrail>();
            trail._geo = geo;
            trail._color = color;
            trail._travelledMat = RuntimeMaterials.UnlitColor(color);
            trail._plannedMat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color, 64, 0.45f));
            trail._arrowMat = RuntimeMaterials.UnlitColor(color);
            trail._travelled = trail.BuildRenderer("Travelled", TravelledWidth, trail._travelledMat);
            trail._planned = trail.BuildRenderer("Planned", PlannedWidth, trail._plannedMat);
            trail.BuildArrows();
            trail.BuildMotes(color);

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

        void BuildArrows()
        {
            var go = new GameObject("Arrows");
            go.transform.SetParent(transform, false);

            _arrowMesh = new UnityEngine.Mesh { name = "MoveTrailArrows" };
            _arrowMesh.MarkDynamic();       // rebuilt every frame as the arrows march

            go.AddComponent<MeshFilter>().sharedMesh = _arrowMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _arrowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _arrows = go.transform;
        }

        /// <summary>Motes lifting off the head of the trail while the unit is actually moving.</summary>
        void BuildMotes(Color color)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);

            _motes = go.AddComponent<ParticleSystem>();
            _motes.Stop();

            var main = _motes.main;
            main.loop = true;
            main.playOnAwake = false;
            // World space: the emitter is dragged along behind the unit, and the
            // motes must stay where they were shed rather than being towed.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(30f, 70f);
            main.maxParticles = 90;

            var emission = _motes.emission;
            emission.rateOverTime = 14f;

            var shape = _motes.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 40f;

            var colour = _motes.colorOverLifetime;
            colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(grad);

            var size = _motes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = RuntimeMaterials.UnlitTexture(ProceduralTextures.Puff(Color.white));
            renderer.alignment = ParticleSystemRenderSpace.View;

            _motes.Play();
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
                    if (_motes != null) _motes.transform.position = _trailWorld[head];
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

            if (_motes != null) _motes.transform.position = _trailWorld[_trailWorld.Count - 1];
        }

        /// <summary>Starts the fade-out. The object destroys itself when it finishes.</summary>
        public void Finish()
        {
            if (_fadeT < 0f) _fadeT = 0f;
            SetRoute(null);

            // Stop shedding immediately; particles already in the air finish.
            if (_motes != null)
            {
                var emission = _motes.emission;
                emission.enabled = false;
            }
        }

        void Update()
        {
            // Arrows march forward along the route while the order stands, which
            // is what turns a static thread into a statement of direction.
            _march += ArrowMarchMps * Time.deltaTime;
            RebuildArrows();

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

            _routeWorld.Clear();
            _routeCum.Clear();

            for (int i = 0; i < _route.Count; i++)
            {
                var p = Clamped(_route[i].latitude, _route[i].longitude);
                _routeWorld.Add(p);
                _routeCum.Add(i == 0 ? 0f : _routeCum[i - 1] + Vector3.Distance(_routeWorld[i - 1], p));
            }

            _planned.positionCount = _routeWorld.Count >= 2 ? _routeWorld.Count : 0;
            for (int i = 0; i < _planned.positionCount; i++) _planned.SetPosition(i, _routeWorld[i]);

            var c = _color; c.a = 0.55f;
            _plannedMat.color = c;

            RebuildArrows();
        }

        /// <summary>
        /// Lays arrowheads along the planned route at a fixed ground spacing,
        /// each pointing the way the unit will actually be travelling when it
        /// gets there — so a route that bends has arrows that bend with it.
        ///
        /// Rebuilt every frame because they march, which is affordable only
        /// because the route's world positions are cached: this walks arithmetic,
        /// never the terrain.
        /// </summary>
        void RebuildArrows()
        {
            if (_arrowMesh == null) return;

            if (_routeWorld.Count < 2 || _fadeT >= 0f)
            {
                if (_arrowMesh.vertexCount > 0) _arrowMesh.Clear();
                return;
            }

            float total = _routeCum[_routeCum.Count - 1];
            if (total < ArrowLengthM)
            {
                if (_arrowMesh.vertexCount > 0) _arrowMesh.Clear();
                return;
            }

            // Start offset cycles within one spacing, so arrows appear to flow
            // forward continuously instead of popping in at the start line.
            float offset = _march % ArrowSpacingM;

            // Reused across frames: this runs every frame on every marching
            // unit, and three fresh Lists per unit per frame is a steady stream
            // of garbage for a mesh that never exceeds a couple of hundred
            // vertices.
            _arrowVerts.Clear();
            _arrowTris.Clear();
            _arrowColours.Clear();
            var verts = _arrowVerts;
            var tris = _arrowTris;
            var colours = _arrowColours;

            int count = 0;
            for (float d = offset; d < total && count < MaxArrows; d += ArrowSpacingM, count++)
            {
                if (!SampleRoute(d, out Vector3 pos, out Vector3 forward)) continue;

                // Local up is Unity's +Y here: the georeference puts the map's
                // origin frame that way, which is the same assumption the unit
                // selection rings make.
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                if (right.sqrMagnitude < 0.5f) continue;      // degenerate: skip this one

                // Fade the arrows in over the first spacing and out over the
                // last, so neither end of the run pops.
                float edge = Mathf.Min(d, total - d);
                float alpha = Mathf.Clamp01(edge / ArrowSpacingM) * 0.9f;

                Vector3 local = transform.InverseTransformPoint(pos);
                Vector3 f = transform.InverseTransformDirection(forward);
                Vector3 r = transform.InverseTransformDirection(right);

                int b = verts.Count;
                verts.Add(local + f * (ArrowLengthM * 0.5f));                       // tip
                verts.Add(local - f * (ArrowLengthM * 0.5f) + r * ArrowHalfWidthM); // back right
                verts.Add(local - f * (ArrowLengthM * 0.5f) - r * ArrowHalfWidthM); // back left
                for (int k = 0; k < 3; k++) colours.Add(new Color(1f, 1f, 1f, alpha));

                // Both windings: an arrow lying on the ground is looked at from
                // above and, in 3D, from underneath a ridge.
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            }

            _arrowMesh.Clear();
            if (verts.Count == 0) return;

            _arrowMesh.SetVertices(verts);
            _arrowMesh.SetColors(colours);
            _arrowMesh.SetTriangles(tris, 0);
            _arrowMesh.RecalculateBounds();

            var c = _color; c.a = 1f;
            _arrowMat.color = c;
        }

        /// <summary>Position and direction at <paramref name="distance"/> along the cached route.</summary>
        bool SampleRoute(float distance, out Vector3 position, out Vector3 forward)
        {
            position = default; forward = default;

            for (int i = 1; i < _routeCum.Count; i++)
            {
                if (distance > _routeCum[i]) continue;

                float segLen = _routeCum[i] - _routeCum[i - 1];
                if (segLen < 1e-3f) return false;

                float t = (distance - _routeCum[i - 1]) / segLen;
                position = Vector3.Lerp(_routeWorld[i - 1], _routeWorld[i], t);
                forward = (_routeWorld[i] - _routeWorld[i - 1]).normalized;
                return true;
            }
            return false;
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
            if (_arrowMat != null) Destroy(_arrowMat);
            if (_arrowMesh != null) Destroy(_arrowMesh);
        }
    }
}
