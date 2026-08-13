using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The axis of an attack drawn on the map: a dashed shaft running from the
    /// attacker to its target with an arrowhead planted on the target, marching
    /// toward what is about to be hit.
    ///
    /// It is the confirmation that an order was understood — you clicked a
    /// target and the map answers with the line the attack will run along — and
    /// it is only wanted while the attack is *pending*. Once the unit reaches
    /// its firing position (or an ambush springs) the arrow fades out and the
    /// muzzle flashes, impacts and fires carry the story instead. A permanent
    /// arrow per engagement would bury the front line in graphics.
    ///
    /// Both ends move, so the trace is resampled on a cadence rather than every
    /// frame — each vertex is a terrain raycast, and this is drawn for every
    /// live attack order at once. Between resamples the endpoints slide along
    /// at their last sampled height so the arrow stays glued to both units.
    /// </summary>
    public class AttackArrow : MonoBehaviour
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
        Color _color;

        LineRenderer _shaft;
        Material _shaftMat, _headMat;
        Transform _head;

        readonly Vector3[] _points = new Vector3[Segments];
        readonly double[] _heights = new double[Segments];
        float _resampleTimer;
        float _fadeT = -1f;                // < 0 while the order is still pending

        public static AttackArrow Create(CesiumGeoreference geo, UnitActor from, UnitActor to, Color color)
        {
            var go = new GameObject($"AttackArrow_{from.State.instanceId}");
            go.transform.SetParent(geo.transform, false);

            var arrow = go.AddComponent<AttackArrow>();
            arrow._geo = geo;
            arrow._from = from;
            arrow._to = to;
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

            arrow.Resample();
            geo.changed += arrow.Resample;
            return arrow;
        }

        /// <summary>Begins the fade-out. The arrow destroys itself when it finishes.</summary>
        public void Finish()
        {
            if (_fadeT < 0f) _fadeT = 0f;
        }

        void LateUpdate()
        {
            // Either end going away ends the arrow — there is no axis of attack
            // without an attacker and a target. The geometry it already has is
            // left alone and simply fades.
            bool live = _from != null && _to != null;
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
            if (_from == null || _to == null || _geo == null) return;

            var a = _from.State;
            var b = _to.State;
            double totalKm = GeoUtils.DistanceKm(a.latitude, a.longitude, b.latitude, b.longitude);
            float bearing = GeoUtils.BearingDeg(a.latitude, a.longitude, b.latitude, b.longitude);

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
            if (_from == null || _to == null) return;

            _points[0] = GeoUtils.GeoToUnity(_geo, _from.State.latitude, _from.State.longitude, _heights[0]);
            _points[Segments - 1] = GeoUtils.GeoToUnity(_geo, _to.State.latitude, _to.State.longitude,
                _heights[Segments - 1]);
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

            Vector3 up = (GeoUtils.GeoToUnity(_geo, _to.State.latitude, _to.State.longitude,
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
