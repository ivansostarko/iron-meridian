using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The map graphic for one mine or obstacle: its doctrinal symbol lying flat
    /// on the terrain, in the owning side's colour, with a caption.
    ///
    /// **Flat, not billboarded** — the opposite choice from a logistic site's
    /// symbol, and for a reason. A supply point is a *place*, and what matters
    /// is which one it is, so its glyph stands up to face the camera. An
    /// obstacle is a piece of *ground* — it has an extent, it lies across an
    /// axis, and reading it means seeing how it sits against the terrain and
    /// what it blocks. A symbol painted on the map answers that; one standing up
    /// like a signpost does not.
    ///
    /// **Sized in metres, not in pixels.** Every other marker on this map holds
    /// a constant apparent size, because a counter is a counter at any zoom. An
    /// obstacle belt is 500 m of ground and has to *stay* 500 m of ground: a
    /// minefield that shrank as you zoomed out would be lying about what it
    /// covers, which is the one thing a control measure exists to state.
    ///
    /// Clamped to the terrain and re-clamped until the ground under it has
    /// actually streamed in. See docs/31-OBSTACLES.md.
    /// </summary>
    public class ObstacleMarker : MonoBehaviour
    {
        public ObstacleSiteData Data { get; private set; }
        public ObstacleKind Kind { get; private set; }

        /// <summary>Metres above the sampled ground — enough to beat z-fighting, low enough to read as painted on.</summary>
        const double ClearanceM = 18.0;
        const float ReclampSeconds = 1.2f;

        CesiumGeoreference _geo;
        Transform _symbol;
        Material _material;
        TextMesh _caption;
        Transform _captionAnchor;
        Color _colour;

        Vector3 _base, _up, _forward;
        bool _placed;
        float _reclampTimer;

        /// <summary>Where the marker stands, in world space — what a screen-space pick projects to find.</summary>
        public Vector3 Anchor => _base;

        public static ObstacleMarker Create(CesiumGeoreference geo, ObstacleSiteData data)
        {
            var go = new GameObject($"Obstacle_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);

            var marker = go.AddComponent<ObstacleMarker>();
            marker._geo = geo;
            marker.Data = data;
            marker.Kind = ObstacleCatalog.Parse(data.kind);
            marker.Build();
            return marker;
        }

        void Build()
        {
            var def = ObstacleCatalog.Get(Kind);

            // The type's own tint, darkened toward the owning side's colour: an
            // obstacle belt is read first as "mines" and second as "whose", and
            // the type is the more urgent of the two.
            Color side = Data.team == Team.Enemy.ToString() ? GameConfig.RedTeam : GameConfig.BlueTeam;
            _colour = Color.Lerp(def.tint, side, 0.35f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Symbol";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * def.widthMeters;

            _material = RuntimeMaterials.UnlitTexture(UI.UiIcons.GlyphFor(Kind).texture);
            _material.color = _colour;
            quad.GetComponent<MeshRenderer>().material = _material;
            _symbol = quad.transform;

            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.UpperCenter;
            _caption.alignment = TextAlignment.Center;
            _caption.characterSize = 8f * 40f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = _colour;
            _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
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

            // The caption is the one part that does hold a constant apparent
            // size: it is text, and text that scaled with the ground would be
            // unreadable at every zoom but one.
            var def = ObstacleCatalog.Get(Kind);
            float depth = Mathf.Max(1f, Vector3.Dot(_base - cam.transform.position, cam.transform.forward));
            _captionAnchor.position = _base - _forward * (def.widthMeters * 0.55f);
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(_base - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// Samples the ground and builds the local frame. Retried on a cadence
        /// until the terrain is there — a graphic placed while tiles are still
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

            // The graphic is laid along its own bearing — an obstacle lies
            // *across* something, and a belt drawn at whatever angle the globe
            // happened to hand it would be a belt across nothing.
            GeoUtils.Destination(Data.latitude, Data.longitude, Data.headingDeg, 0.2,
                out double aheadLat, out double aheadLon);
            Vector3 fwd = GeoUtils.GeoToUnity(_geo, aheadLat, aheadLon, h) - _base;
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            _placed = found;
        }

        /// <summary>Re-reads the record in place — a renamed, re-sided or re-aimed graphic.</summary>
        public void Refresh()
        {
            var def = ObstacleCatalog.Get(Kind);
            Color side = Data.team == Team.Enemy.ToString() ? GameConfig.RedTeam : GameConfig.BlueTeam;
            _colour = Color.Lerp(def.tint, side, 0.35f);

            if (_material != null) _material.color = _colour;
            if (_caption != null)
            {
                _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
                _caption.color = _colour;
            }
            _placed = false;
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_symbol != null)
            {
                var filter = _symbol.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Quad"))
                {
                    // The primitive's mesh is Unity's shared built-in one and is
                    // deliberately not destroyed — doing so would take the quad
                    // out from under every other primitive in the scene.
                }
            }
        }
    }
}
