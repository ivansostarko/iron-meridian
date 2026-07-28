using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// A terrain-conforming circle at a fixed real-world radius (km) around a
    /// geodetic point — used to show a unit's view range / weapon range.
    ///
    /// The ring is drawn as marching dashes with a breathing width and a short
    /// sweep-out reveal, and carries a billboarded caption ("Max view 4.5 km")
    /// pinned to its northern edge. The radius itself is never animated once
    /// revealed: it states a real distance and must not lie about it.
    /// </summary>
    public class RangeRing : MonoBehaviour
    {
        const int Segments = 96;
        const float RevealSeconds = 0.35f;
        const float DashesPerRing = 46f;
        const float ScrollSpeed = 0.12f;      // texture units/second

        CesiumGeoreference _geo;
        LineRenderer _lr;
        Material _mat;
        TextMesh _caption;
        Transform _captionAnchor;

        Vector3[] _points;
        Vector3 _captionPos;
        double _lat, _lon;
        float _radiusKm;
        float _baseWidth;
        Color _color;
        string _title = "";
        bool _visible;
        float _revealT;

        public static RangeRing Create(CesiumGeoreference geo, Transform parent, Color color,
            float width, string title)
        {
            var go = new GameObject("RangeRing_" + title);
            go.transform.SetParent(parent, false);
            var ring = go.AddComponent<RangeRing>();
            ring._geo = geo;
            ring._color = color;
            ring._baseWidth = width;
            ring._title = title;

            ring._lr = go.AddComponent<LineRenderer>();
            ring._lr.useWorldSpace = true;
            ring._lr.loop = true;
            ring._lr.startWidth = ring._lr.endWidth = width;
            ring._lr.numCornerVertices = 2;
            ring._lr.textureMode = LineTextureMode.Tile;
            ring._lr.alignment = LineAlignment.View;

            ring._mat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color));
            ring._lr.material = ring._mat;

            ring.BuildCaption(color);

            geo.changed += ring.OnGeoChanged;
            go.SetActive(false);
            return ring;
        }

        void BuildCaption(Color color)
        {
            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.LowerCenter;
            _caption.alignment = TextAlignment.Center;
            _caption.characterSize = 8;
            _caption.fontSize = 44;
            _caption.color = color;
            _caption.text = "";
        }

        /// <summary>Show (or reposition) the ring around a geodetic point at the given radius.</summary>
        public void Show(double lat, double lon, float radiusKm)
        {
            if (radiusKm <= 0f) { Hide(); return; }
            bool wasHidden = !_visible;
            _lat = lat; _lon = lon; _radiusKm = radiusKm;
            _visible = true;
            if (wasHidden) _revealT = 0f;      // replay the sweep for a new selection

            _caption.text = $"{_title} {radiusKm:0.#} km";
            gameObject.SetActive(true);
            Rebuild();
        }

        public void Hide()
        {
            _visible = false;
            gameObject.SetActive(false);
        }

        void OnGeoChanged()
        {
            if (_visible) Rebuild();
        }

        void LateUpdate()
        {
            if (!_visible) return;

            _revealT = Mathf.Min(_revealT + Time.unscaledDeltaTime, RevealSeconds);
            float reveal = Mathf.SmoothStep(0f, 1f, _revealT / RevealSeconds);

            // Dashes march around the ring; the width breathes gently so the
            // ring reads as live telemetry rather than a static overlay.
            _mat.mainTextureOffset = new Vector2(-Time.unscaledTime * ScrollSpeed, 0f);

            float breathe = 1f + Mathf.Sin(Time.unscaledTime * 2.6f) * 0.18f;
            float w = _baseWidth * breathe * Mathf.Lerp(0.4f, 1f, reveal);
            _lr.startWidth = _lr.endWidth = w;

            var c = _color;
            c.a = Mathf.Lerp(0f, Mathf.Lerp(0.55f, 1f, (Mathf.Sin(Time.unscaledTime * 2.6f) + 1f) * 0.5f), reveal);
            _mat.color = c;

            // The reveal sweeps the arc open rather than growing the radius, so
            // the circle is always drawn at its true distance.
            int shown = Mathf.Max(2, Mathf.RoundToInt(Segments * reveal));
            _lr.loop = shown >= Segments;
            if (_lr.positionCount != shown) ApplyArc(shown);

            BillboardCaption();
        }

        void BillboardCaption()
        {
            var cam = Camera.main;
            if (cam == null || _captionAnchor == null) return;

            // Position is cached with the ring geometry; only the facing and
            // the zoom-compensating scale need refreshing each frame.
            _captionAnchor.position = _captionPos;

            float depth = Mathf.Max(1f, Vector3.Dot(
                _captionPos - cam.transform.position, cam.transform.forward));
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(
                _captionPos - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// Samples the whole circle once and caches it. The per-frame reveal
        /// then just replays slices of this array — re-sampling terrain every
        /// frame would be ~96 physics raycasts per ring per frame.
        /// </summary>
        void Rebuild()
        {
            if (_points == null || _points.Length != Segments) _points = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                double bearing = i * 360.0 / Segments;
                GeoUtils.Destination(_lat, _lon, bearing, _radiusKm, out double lat2, out double lon2);
                double h = GeoUtils.SampleTerrainHeight(_geo, lat2, lon2, 250) + 8.0;
                _points[i] = GeoUtils.GeoToUnity(_geo, lat2, lon2, h);
            }

            GeoUtils.Destination(_lat, _lon, 0.0, _radiusKm, out double nLat, out double nLon);
            double ch = GeoUtils.SampleTerrainHeight(_geo, nLat, nLon, 250) + 40.0;
            _captionPos = GeoUtils.GeoToUnity(_geo, nLat, nLon, ch);

            ApplyArc(_lr.positionCount > 0 ? Mathf.Min(_lr.positionCount, Segments) : Segments);
        }

        void ApplyArc(int count)
        {
            if (_points == null) return;
            count = Mathf.Clamp(count, 2, Segments);
            _lr.positionCount = count;
            for (int i = 0; i < count; i++) _lr.SetPosition(i, _points[i]);
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= OnGeoChanged;
        }
    }
}
