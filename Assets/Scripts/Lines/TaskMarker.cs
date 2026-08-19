using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The map graphic for a defensive task pinned to a place: a pulsing ground
    /// ring in the task's colour, a facing arrow showing what the position is
    /// oriented on, and a billboarded caption naming the task and the unit
    /// holding it ("HOLD / 2 RIFLES").
    ///
    /// Clamped to the terrain and re-clamped until the ground under it has
    /// actually streamed in, so the marker is readable at any zoom in both view
    /// modes rather than sinking into a hillside.
    /// </summary>
    public class TaskMarker : MonoBehaviour
    {
        public MapMarkerData Data { get; private set; }

        /// <summary>Metres above the sampled ground.</summary>
        const double ClearanceM = 10.0;
        const float ReclampSeconds = 1.2f;
        /// <summary>Ring diameter in metres at the reference zoom, before camera scaling.</summary>
        const float RingMeters = 460f;

        CesiumGeoreference _geo;
        Transform _ring;
        Transform _chevron;
        Material _ringMat, _chevronMat;
        TextMesh _caption;
        Transform _captionAnchor;
        Color _color;

        Vector3 _base, _forward, _up;
        bool _placed;
        float _reclampTimer;

        public static TaskMarker Create(CesiumGeoreference geo, MapMarkerData data)
        {
            var go = new GameObject($"Marker_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);

            var marker = go.AddComponent<TaskMarker>();
            marker._geo = geo;
            marker.Data = data;
            marker._color = ColorFor(data);
            marker.BuildVisuals();
            return marker;
        }

        /// <summary>
        /// Hold is the yellow of a control measure you may not give up; guard is
        /// the green of a security task out in front; defend takes the owning
        /// side's colour, because it is that formation's own position.
        /// </summary>
        static Color ColorFor(MapMarkerData data)
        {
            if (!System.Enum.TryParse(data.kind, out MarkerKind kind)) kind = MarkerKind.Hold;
            // Intent, not decoration. The four families a marker can belong to
            // are the four a player has to tell apart on a map carrying dozens:
            // defending, attacking, looking, and going somewhere.
            switch (kind)
            {
                case MarkerKind.Guard: return GameConfig.NeutralGreen;
                case MarkerKind.Defend:
                    return data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;
                case MarkerKind.Attack: return new Color(1.00f, 0.68f, 0.28f);
                case MarkerKind.Recon: return new Color(0.45f, 0.85f, 0.70f);
                case MarkerKind.Withdraw: return new Color(0.95f, 0.72f, 0.30f);
                case MarkerKind.Retreat: return new Color(0.95f, 0.42f, 0.36f);
                default: return GameConfig.BoundaryYellow;
            }
        }

        void BuildVisuals()
        {
            _ring = Quad("Ring", ProceduralTextures.Ring(_color, 128, 0.34f, 0.46f), out _ringMat);
            _chevron = Quad("Facing", ProceduralTextures.HeadingArrow(_color), out _chevronMat);

            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.LowerCenter;
            _caption.alignment = TextAlignment.Center;
            // characterSize absorbs MapFont's fixed rasterisation size, so the
            // caption keeps the size it had while sharing the map's font atlas.
            _caption.characterSize = 8f * 44f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = _color;
            _caption.text = Data.label;
        }

        Transform Quad(string name, Texture2D texture, out Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            material = RuntimeMaterials.UnlitTexture(texture);
            quad.GetComponent<MeshRenderer>().material = material;
            return quad.transform;
        }

        /// <summary>Re-captions and re-aims the marker in place (e.g. the threat moved).</summary>
        public void Refresh()
        {
            _color = ColorFor(Data);
            if (_caption != null) { _caption.text = Data.label; _caption.color = _color; }
            _placed = false;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (!_placed)
            {
                _reclampTimer -= Time.unscaledDeltaTime;
                if (_reclampTimer <= 0f) Place();
            }

            transform.position = _base;
            transform.rotation = Quaternion.LookRotation(_forward, _up);

            // Constant apparent size, the same depth-along-forward measure the
            // unit icons use so markers and icons scale together.
            float depth = Mathf.Max(1f, Vector3.Dot(_base - cam.transform.position, cam.transform.forward));
            float s = Mathf.Clamp(depth / 18f, 30f, 2600f) / 260f;

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.4f) * 0.10f;
            _ring.localScale = Vector3.one * RingMeters * s * pulse;

            float chev = RingMeters * s * 0.55f;
            _chevron.localScale = new Vector3(chev * 0.7f, chev, 1f);
            _chevron.localPosition = new Vector3(0f, 0f, RingMeters * s * 0.62f);

            _captionAnchor.position = _base + _up * (RingMeters * s * 0.10f);
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(_base - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// Samples the ground and builds the local frame. Retried on a cadence
        /// until the terrain is there — a marker placed while tiles are still
        /// streaming would otherwise sit at the fallback height forever.
        /// </summary>
        void Place()
        {
            _reclampTimer = ReclampSeconds;

            bool found = GeoUtils.TrySampleTerrainHeight(_geo, Data.latitude, Data.longitude, out double ground);
            double h = (found ? ground : (Data.heightMeters > 0 ? Data.heightMeters : 250.0)) + ClearanceM;
            Data.heightMeters = h;

            _base = GeoUtils.GeoToUnity(_geo, Data.latitude, Data.longitude, h);
            _up = (GeoUtils.GeoToUnity(_geo, Data.latitude, Data.longitude, h + 1000.0) - _base).normalized;

            GeoUtils.Destination(Data.latitude, Data.longitude, Data.headingDeg, 0.2,
                out double aheadLat, out double aheadLon);
            Vector3 fwd = GeoUtils.GeoToUnity(_geo, aheadLat, aheadLon, h) - _base;
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            _placed = found;
        }

        void OnDestroy()
        {
            if (_ringMat != null) Destroy(_ringMat);
            if (_chevronMat != null) Destroy(_chevronMat);
        }
    }
}
