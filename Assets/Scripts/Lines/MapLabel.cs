using UnityEngine;
using CesiumForUnity;
using IronMeridian.Map;

namespace IronMeridian.Lines
{
    /// <summary>
    /// A billboarded caption pinned to a geodetic point — used for the label
    /// groups that straddle a boundary and for FEBA / phase-line end captions.
    ///
    /// A boundary label group follows the doctrinal stacked layout
    /// (FM 101-5-1 ch.3, subcourse SS0529 §8):
    ///
    ///     2-79 IN        ← formation on one side
    ///        II          ← echelon size marking, across the line
    ///     TF 2-1 AR      ← formation on the other side
    ///
    /// Doctrine draws the size marking perpendicular to the boundary inside a
    /// gap in the line. Billboarding the whole group instead keeps it legible
    /// from any camera angle, which matters more here than exact overlay
    /// fidelity — text is never allowed to render upside-down.
    /// </summary>
    public class MapLabel : MonoBehaviour
    {
        CesiumGeoreference _geo;
        TextMesh _text;
        double _lat, _lon;
        Vector3 _world;
        bool _placed;

        public static MapLabel Create(CesiumGeoreference geo, Transform parent, string id)
        {
            var go = new GameObject("Label_" + id);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<MapLabel>();
            label._geo = geo;

            label._text = go.AddComponent<TextMesh>();
            label._text.anchor = TextAnchor.MiddleCenter;
            label._text.alignment = TextAlignment.Center;
            label._text.characterSize = 8;
            label._text.fontSize = 40;
            return label;
        }

        public void Set(string content, Color color, double lat, double lon)
        {
            _text.text = content;
            _text.color = color;
            _lat = lat; _lon = lon;
            _placed = false;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (!_placed)
            {
                // Terrain sampling is a physics raycast; the label does not
                // move once placed, so do it once rather than every frame.
                double h = GeoUtils.SampleTerrainHeight(_geo, _lat, _lon, 250) + 60.0;
                _world = GeoUtils.GeoToUnity(_geo, _lat, _lon, h);
                _placed = true;
            }

            transform.position = _world;
            float depth = Mathf.Max(1f, Vector3.Dot(_world - cam.transform.position, cam.transform.forward));
            transform.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 8f);
            transform.rotation = Quaternion.LookRotation(_world - cam.transform.position, cam.transform.up);
        }

        /// <summary>Re-sample the ground on the next frame (after a georeference shift).</summary>
        public void Invalidate() => _placed = false;
    }
}
