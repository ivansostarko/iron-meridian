using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Logistics
{
    /// <summary>
    /// The map graphic for one logistic installation: a ground ring in the
    /// owning side's colour, the function's own glyph standing in the middle of
    /// it, and a caption naming it.
    ///
    /// **The glyph billboards; the ring lies flat.** A logistics laydown is
    /// read two ways — from directly overhead, where what matters is *where*
    /// the sites are relative to the units they serve, and from a working
    /// camera angle, where what matters is *which* site is which. A flat ring
    /// answers the first at any tilt and a billboarded symbol answers the
    /// second, so the marker keeps both rather than trading one for the other.
    ///
    /// Sized like a task marker (constant apparent size, clamped) so a rear
    /// area reads as part of the same map as the formations it supports rather
    /// than as a separate layer of furniture.
    ///
    /// Clamped to the terrain and re-clamped until the ground under it has
    /// actually streamed in — see <see cref="Place"/>.
    /// </summary>
    public class LogisticsSite : MonoBehaviour
    {
        public LogisticsSiteData Data { get; private set; }
        public LogisticsKind Kind { get; private set; }

        /// <summary>
        /// Where the marker stands, in world space — what a screen-space pick
        /// projects to find it. Zero until the ground under it has been sampled,
        /// which is the honest answer: a site whose terrain has not streamed in
        /// is not on the map yet either. See <c>LogisticsSystem.PickAt</c>.
        /// </summary>
        public Vector3 Anchor => _base;

        /// <summary>Metres above the sampled ground.</summary>
        const double ClearanceM = 10.0;
        const float ReclampSeconds = 1.2f;
        /// <summary>Ring diameter in metres at the reference zoom, before camera scaling.</summary>
        const float RingMeters = 520f;

        CesiumGeoreference _geo;
        Transform _ring, _glyph;
        Material _ringMat, _glyphMat;
        TextMesh _caption;
        Transform _captionAnchor;
        Color _sideColour, _tint;

        Vector3 _base, _up, _forward;
        bool _placed;
        float _reclampTimer;

        public static LogisticsSite Create(CesiumGeoreference geo, LogisticsSiteData data)
        {
            var go = new GameObject($"Logistics_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);

            var site = go.AddComponent<LogisticsSite>();
            site._geo = geo;
            site.Data = data;
            site.Kind = LogisticsCatalog.Parse(data.kind);
            site.Build();
            return site;
        }

        void Build()
        {
            var def = LogisticsCatalog.Get(Kind);
            _tint = def.tint;
            _sideColour = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;

            // The ring is the side's; the symbol inside it is the function's.
            // Colouring both the same would make a rear area one wash of blue
            // in which nothing can be picked out.
            _ring = Quad("Ring", ProceduralTextures.Ring(_sideColour, 128, 0.40f, 0.48f),
                out _ringMat, flat: true);
            _glyph = Quad("Glyph", UI.UiIcons.GlyphFor(Kind).texture, out _glyphMat, flat: false);
            _glyphMat.color = _tint;

            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.UpperCenter;
            _caption.alignment = TextAlignment.Center;
            // characterSize absorbs MapFont's fixed rasterisation size, so the
            // caption keeps the size it had while sharing the map's font atlas.
            _caption.characterSize = 8f * 40f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = _sideColour;
            _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
        }

        Transform Quad(string name, Texture2D texture, out Material material, bool flat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            if (flat) quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            material = RuntimeMaterials.UnlitTexture(texture);
            quad.GetComponent<MeshRenderer>().material = material;
            return quad.transform;
        }

        /// <summary>Re-reads the record in place — a renamed or re-sided site.</summary>
        public void Refresh()
        {
            var def = LogisticsCatalog.Get(Kind);
            _sideColour = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;
            if (_ringMat != null) _ringMat.color = _sideColour;
            if (_caption != null)
            {
                _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
                _caption.color = _sideColour;
            }
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
            // unit icons and task markers use, so all three scale together.
            float depth = Mathf.Max(1f, Vector3.Dot(_base - cam.transform.position, cam.transform.forward));
            float s = Mathf.Clamp(depth / 18f, 30f, 2600f) / 260f;

            _ring.localScale = Vector3.one * RingMeters * s;

            // The symbol stands up to face the camera, lifted just clear of the
            // ring so the two read as one marker rather than as a decal with
            // something floating over it.
            float glyphSize = RingMeters * s * 0.62f;
            _glyph.position = _base + _up * (glyphSize * 0.55f);
            _glyph.localScale = Vector3.one * glyphSize;
            _glyph.rotation = Quaternion.LookRotation(_glyph.position - cam.transform.position, cam.transform.up);

            _captionAnchor.position = _base - _up * (RingMeters * s * 0.06f);
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(_base - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// Samples the ground and builds the local frame. Retried on a cadence
        /// until the terrain is there — a site placed while tiles are still
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

            // Any horizontal axis will do — the marker has no facing — but
            // taking it from local north keeps the ring's own orientation
            // stable as the camera moves, rather than spinning with it.
            GeoUtils.Destination(Data.latitude, Data.longitude, 0.0, 0.2,
                out double northLat, out double northLon);
            Vector3 fwd = GeoUtils.GeoToUnity(_geo, northLat, northLon, h) - _base;
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            _placed = found;
        }

        void OnDestroy()
        {
            if (_ringMat != null) Destroy(_ringMat);
            if (_glyphMat != null) Destroy(_glyphMat);
        }
    }
}
