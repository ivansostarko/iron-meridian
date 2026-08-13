using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Ground arrow showing which way a selected unit is facing. Shown whenever
    /// the unit is selected — in the scenario editor and in battle alike — and
    /// brightened while the player is aiming the facing with <c>C</c>, so the
    /// arrow doubles as the aiming feedback for that mode.
    ///
    /// Orientation is derived geodetically rather than from a local Euler angle:
    /// the arrow looks at a point a short way along the unit's bearing and uses
    /// the local vertical as its up axis. A plain <c>Euler(90, heading, 0)</c>
    /// is only correct at the georeference origin — everywhere else the globe
    /// has curved under the unit and the arrow would lean off the ground and
    /// point somewhere other than north-relative.
    /// </summary>
    public class HeadingArrow : MonoBehaviour
    {
        /// <summary>Arrow length as a multiple of the unit icon's on-screen size.</summary>
        const float LengthFactor = 1.45f;
        /// <summary>How far the arrow's tail sits ahead of the icon's ground point, same units.</summary>
        const float TailGapFactor = 0.45f;
        /// <summary>Metres above the sampled ground, so the arrow is not buried by terrain LOD.</summary>
        const float GroundClearanceM = 6f;
        /// <summary>Bearing/position change below which the geodetic frame is reused.</summary>
        const float RecomputeDeg = 0.35f;

        CesiumGeoreference _geo;
        UnitActor _actor;
        Transform _quad;
        Material _material;
        Color _color;

        double _frameLat, _frameLon;
        float _frameHeading = float.NaN;
        Vector3 _base, _forward, _up;
        bool _framed;
        bool _aiming;

        public static HeadingArrow Create(CesiumGeoreference geo, UnitActor actor, Color color)
        {
            var go = new GameObject("HeadingArrow");
            // Parented to the georeference, not to the unit: CesiumGlobeAnchor
            // rotates the unit root to follow the globe, which would tilt a
            // child arrow off the ground the same way it does the selection ring.
            go.transform.SetParent(geo.transform, false);

            var arrow = go.AddComponent<HeadingArrow>();
            arrow._geo = geo;
            arrow._actor = actor;
            arrow._color = color;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Blade";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, false);
            // Lay the quad flat: its texture "up" (+V) then runs along the
            // parent's forward axis, which is aimed down the unit's bearing.
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arrow._quad = quad.transform;

            arrow._material = RuntimeMaterials.UnlitTexture(ProceduralTextures.HeadingArrow(color));
            quad.GetComponent<MeshRenderer>().material = arrow._material;

            go.SetActive(false);
            return arrow;
        }

        public void SetVisible(bool visible)
        {
            if (this == null) return;
            gameObject.SetActive(visible);
            if (visible) _framed = false;      // re-derive the frame on the next frame
        }

        /// <summary>Brightens the arrow while the player is aiming the facing.</summary>
        public void SetAiming(bool aiming) => _aiming = aiming;

        /// <summary>
        /// Called by <see cref="UnitActor"/> with the icon's current on-screen
        /// size, so the arrow tracks the zoom compensation the icon already does
        /// instead of computing its own and drifting out of proportion.
        /// </summary>
        public void UpdateArrow(float iconWorldSize)
        {
            if (!gameObject.activeSelf || _actor == null || _geo == null) return;

            var s = _actor.State;
            if (!_framed ||
                Mathf.Abs(Mathf.DeltaAngle(_frameHeading, s.headingDeg)) > RecomputeDeg ||
                s.latitude != _frameLat || s.longitude != _frameLon)
            {
                RebuildFrame();
            }

            float length = Mathf.Max(1f, iconWorldSize * LengthFactor);
            transform.position = _base + _up * GroundClearanceM;
            transform.rotation = Quaternion.LookRotation(_forward, _up);

            // After the quad's 90° pitch, local Y runs along the parent's
            // forward axis — so scaling Y lengthens the arrow down the course.
            _quad.localScale = new Vector3(length * 0.7f, length, 1f);
            _quad.localPosition = new Vector3(0f, 0f, iconWorldSize * TailGapFactor + length * 0.5f);

            var c = _color;
            c.a = _aiming ? 1f : 0.7f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.12f;
            if (_aiming) c *= 1.25f;
            _material.color = c;
        }

        /// <summary>
        /// Establishes the local ground frame: where the unit stands, which way
        /// its bearing points across the ground, and which way is up there.
        /// </summary>
        void RebuildFrame()
        {
            var s = _actor.State;
            _frameLat = s.latitude; _frameLon = s.longitude; _frameHeading = s.headingDeg;

            double h = s.heightMeters;
            _base = GeoUtils.GeoToUnity(_geo, s.latitude, s.longitude, h);

            // 200 m is far enough that float precision in the conversion does
            // not wobble the direction, and near enough that the great circle
            // has not curved away from the initial bearing.
            GeoUtils.Destination(s.latitude, s.longitude, s.headingDeg, 0.2,
                out double aheadLat, out double aheadLon);
            Vector3 ahead = GeoUtils.GeoToUnity(_geo, aheadLat, aheadLon, h);

            _up = (GeoUtils.GeoToUnity(_geo, s.latitude, s.longitude, h + 1000.0) - _base).normalized;
            Vector3 fwd = ahead - _base;
            // Project onto the local horizontal so a slope does not tip the arrow.
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            _framed = true;
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
