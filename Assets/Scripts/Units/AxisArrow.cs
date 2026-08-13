using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The axis of a pending order drawn on the map: a dashed shaft running from
    /// the unit to what it has been pointed at, with an arrowhead planted on the
    /// far end, dashes marching toward it.
    ///
    /// It is the confirmation that an order was understood — you clicked
    /// something and the map answers with the line the order will run along —
    /// and it is only wanted while that order is *pending*. An attack retires it
    /// when the unit reaches its firing position; a recon task retires it on
    /// arrival. A permanent arrow per order would bury the front line in
    /// graphics.
    ///
    /// The far end is either **another unit** (an attack target, which moves) or
    /// a **fixed point** (a recon objective, which does not). Both are supported
    /// here rather than by faking a unit at the objective: a stand-in
    /// <see cref="UnitActor"/> would land in the registry and be swept up by
    /// combat, selection and fog.
    ///
    /// The ends move, so the trace is resampled on a cadence rather than every
    /// frame — each vertex is a terrain raycast, and this is drawn for every
    /// live order at once. Between resamples the endpoints slide along at their
    /// last sampled height so the arrow stays glued to both ends.
    /// </summary>
    public class AxisArrow : MonoBehaviour
    {
        /// <summary>Vertices along the shaft. Enough to follow a valley, few enough to resample often.</summary>
        const int Segments = 14;
        /// <summary>Seconds between full terrain resamples of the shaft.</summary>
        const float ResampleSeconds = 0.3f;
        /// <summary>Metres above the sampled ground.</summary>
        const double ClearanceM = 30.0;
        /// <summary>Seconds the arrow takes to fade once the attack goes in.</summary>
        const float FadeSeconds = 1.1f;
        /// <summary>Dashes marched per second along the shaft, toward the target.</summary>
        const float ScrollSpeed = 0.55f;
        const float ShaftWidth = 46f;
        /// <summary>Arrowhead size as a fraction of the on-screen icon scale.</summary>
        const float HeadFactor = 2.1f;

        CesiumGeoreference _geo;
        UnitActor _from, _to;
        /// <summary>Far end when it is a place rather than a formation.</summary>
        double _toLat, _toLon;
        bool _toIsPoint;
        Color _color;

        LineRenderer _shaft;
        Material _shaftMat, _headMat;
        Transform _head;

        readonly Vector3[] _points = new Vector3[Segments];
        readonly double[] _heights = new double[Segments];
        float _resampleTimer;
        float _fadeT = -1f;                // < 0 while the order is still pending

        /// <summary>Axis from a unit to another unit — an attack target, which moves.</summary>
        public static AxisArrow Create(CesiumGeoreference geo, UnitActor from, UnitActor to, Color color)
        {
            var arrow = Build(geo, from, color);
            arrow._to = to;
            arrow.Begin(geo);
            return arrow;
        }

        /// <summary>Axis from a unit to a point on the ground — a recon objective, which does not.</summary>
        public static AxisArrow CreateToPoint(CesiumGeoreference geo, UnitActor from,
            double lat, double lon, Color color)
        {
            var arrow = Build(geo, from, color);
            arrow._toIsPoint = true;
            arrow._toLat = lat;
            arrow._toLon = lon;
            arrow.Begin(geo);
            return arrow;
        }

        static AxisArrow Build(CesiumGeoreference geo, UnitActor from, Color color)
        {
            var go = new GameObject($"Axis_{from.State.instanceId}");
            go.transform.SetParent(geo.transform, false);

            var arrow = go.AddComponent<AxisArrow>();
            arrow._geo = geo;
            arrow._from = from;
            arrow._color = color;

            arrow._shaftMat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color, 64, 0.5f));
            var shaftGo = new GameObject("Shaft");
            shaftGo.transform.SetParent(go.transform, false);
            arrow._shaft = shaftGo.AddComponent<LineRenderer>();
            arrow._shaft.useWorldSpace = true;
            arrow._shaft.alignment = LineAlignment.View;
            arrow._shaft.textureMode = LineTextureMode.Tile;
            arrow._shaft.numCornerVertices = 2;
            arrow._shaft.startWidth = arrow._shaft.endWidth = ShaftWidth;
            arrow._shaft.material = arrow._shaftMat;
            arrow._shaft.positionCount = Segments;

            var headGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            headGo.name = "Head";
            Destroy(headGo.GetComponent<Collider>());
            headGo.transform.SetParent(go.transform, false);
            arrow._head = headGo.transform;
            arrow._headMat = RuntimeMaterials.UnlitTexture(ProceduralTextures.HeadingArrow(color));
            headGo.GetComponent<MeshRenderer>().material = arrow._headMat;
            return arrow;
        }

        /// <summary>First trace, once both ends are known.</summary>
        void Begin(CesiumGeoreference geo)
        {
            Resample();
            geo.changed += Resample;
        }

        /// <summary>Where the arrow points: a fixed objective, or wherever the target unit is now.</summary>
        void FarEnd(out double lat, out double lon)
        {
            if (_toIsPoint || _to == null) { lat = _toLat; lon = _toLon; return; }
            lat = _to.State.latitude;
            lon = _to.State.longitude;
        }

        /// <summary>Begins the fade-out. The arrow destroys itself when it finishes.</summary>
        public void Finish()
        {
            if (_fadeT < 0f) _fadeT = 0f;
        }

        void LateUpdate()
        {
            // Either end going away ends the arrow — there is no axis without a
            // unit and something to point at. The geometry it already has is
            // left alone and simply fades.
            bool live = _from != null && (_toIsPoint || _to != null);
            if (!live) Finish();

            if (_fadeT >= 0f)
            {
                _fadeT += Time.unscaledDeltaTime;
                if (_fadeT >= FadeSeconds) { Destroy(gameObject); return; }
            }

            if (live)
            {
                _resampleTimer -= Time.unscaledDeltaTime;
                if (_resampleTimer <= 0f) Resample();
                else TrackEnds();
                UpdateHead();
            }

            // Dashes run toward the target, so the arrow reads as directional
            // even before the eye finds the head.
            _shaftMat.mainTextureOffset = new Vector2(-Time.unscaledTime * ScrollSpeed, 0f);

            float alpha = _fadeT < 0f
                ? 0.78f + Mathf.Sin(Time.unscaledTime * 3.4f) * 0.16f
                : Mathf.Clamp01(1f - _fadeT / FadeSeconds) * 0.9f;
            var c = _color; c.a = alpha;
            _shaftMat.color = c;
            _headMat.color = c;
        }

        /// <summary>
        /// Rebuilds the whole trace along the great circle between the two
        /// units, clamped to the terrain.
        /// </summary>
        void Resample()
        {
            _resampleTimer = ResampleSeconds;
            if (_geo == null || _from == null || !(_toIsPoint || _to != null)) return;

            var a = _from.State;
            FarEnd(out double bLat, out double bLon);
            double totalKm = GeoUtils.DistanceKm(a.latitude, a.longitude, bLat, bLon);
            float bearing = GeoUtils.BearingDeg(a.latitude, a.longitude, bLat, bLon);

            for (int i = 0; i < Segments; i++)
            {
                double t = i / (double)(Segments - 1);
                GeoUtils.Destination(a.latitude, a.longitude, bearing, totalKm * t,
                    out double lat, out double lon);
                _heights[i] = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250.0) + ClearanceM;
                _points[i] = GeoUtils.GeoToUnity(_geo, lat, lon, _heights[i]);
                _shaft.SetPosition(i, _points[i]);
            }
        }

        /// <summary>
        /// Between resamples, drags the two end vertices onto wherever the units
        /// are now, reusing their last sampled heights. Both ends can be moving
        /// at march speed, and an arrow that only caught up three times a second
        /// would visibly detach from the icons.
        /// </summary>
        void TrackEnds()
        {
            if (_from == null || !(_toIsPoint || _to != null)) return;

            FarEnd(out double bLat, out double bLon);
            _points[0] = GeoUtils.GeoToUnity(_geo, _from.State.latitude, _from.State.longitude, _heights[0]);
            _points[Segments - 1] = GeoUtils.GeoToUnity(_geo, bLat, bLon, _heights[Segments - 1]);
            _shaft.SetPosition(0, _points[0]);
            _shaft.SetPosition(Segments - 1, _points[Segments - 1]);
        }

        /// <summary>
        /// Plants the arrowhead flat on the ground at the target, pointing the
        /// way the shaft arrives. Sized off camera depth like the unit icons, so
        /// it stays legible at any zoom instead of vanishing when zoomed out.
        /// </summary>
        void UpdateHead()
        {
            var cam = Camera.main;
            if (cam == null || _head == null) return;

            Vector3 tip = _points[Segments - 1];
            Vector3 back = _points[Segments - 2];

            FarEnd(out double bLat, out double bLon);
            Vector3 up = (GeoUtils.GeoToUnity(_geo, bLat, bLon,
                _heights[Segments - 1] + 1000.0) - tip).normalized;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;

            Vector3 fwd = tip - back;
            fwd -= up * Vector3.Dot(fwd, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            // Same depth-along-forward measure the unit icons use, so the head
            // holds a constant apparent size at every zoom.
            float depth = Mathf.Max(1f, Vector3.Dot(tip - cam.transform.position, cam.transform.forward));
            float size = Mathf.Clamp(depth / 18f, 30f, 2600f) * HeadFactor;

            // The head texture points along +V; the 90° pitch lays the quad flat
            // and maps that +V onto the shaft's direction. It is pulled back
            // along the shaft so its tip lands on the target rather than past it.
            _head.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(90f, 0f, 0f);
            _head.localScale = new Vector3(size * 0.7f, size, 1f);
            _head.position = tip - fwd * (size * 0.5f);
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= Resample;
            if (_shaftMat != null) Destroy(_shaftMat);
            if (_headMat != null) Destroy(_headMat);
        }
    }
}
