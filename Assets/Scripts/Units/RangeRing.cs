using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// A range at a fixed real-world radius around a geodetic point — a unit's
    /// line of sight, its weapon reach, or the uncertainty around a fog contact.
    ///
    /// **It is a 2D instrument drawn on the ground, not an object standing on
    /// it.** Two earlier versions got this wrong in opposite directions. A
    /// twelve-metre <c>LineRenderer</c> on a circle kilometres across is well
    /// under a pixel from operational altitude, so the first was invisible; the
    /// second answered that with a translucent wall of light rising out of the
    /// terrain along the whole circumference, which is legible but is a lie
    /// about what it represents. A weapon range is a distance measured across
    /// the map. Standing it up hides the very ground the reach is being judged
    /// against, makes two overlapping rings — line of sight and weapon range —
    /// into a thicket of curtains, and turns a flat statement into a piece of
    /// scenery.
    ///
    /// So the ring is drawn as a **feathered band lying on the terrain**: a
    /// bright rim fading out to nothing on both sides, with cardinal tick spurs
    /// so it reads as an instrument rather than a smear of paint, and a caption
    /// at its north point. The band is wide in *metres* rather than in pixels,
    /// which is what keeps it visible from altitude without becoming a wall up
    /// close, and it is clamped to the sampled ground the whole way round, so it
    /// dips into valleys and rides over spurs exactly as the terrain does.
    ///
    /// The radius itself is never animated. It states a real distance and must
    /// not lie about it — only brightness and the reveal breathe.
    ///
    /// Geometry is built in the anchor's **local east-north-up frame**, with
    /// heights stored relative to the centre. That is what makes a range ring
    /// affordable on a marching unit: the ring follows by moving its anchor, and
    /// the mesh is only re-sampled when the centre has moved far enough for the
    /// ground under it to have genuinely changed.
    /// </summary>
    public class RangeRing : MonoBehaviour
    {
        /// <summary>
        /// Segments around the circumference — and terrain samples per rebuild.
        ///
        /// Denser than the wall version needed. A wall that is hundreds of
        /// metres tall reads correctly however coarsely the ground under it is
        /// sampled; a band lying flat on the terrain is buried wherever the
        /// ground between two samples rises more than the lift, so the drape
        /// has to be fine enough that it does not. Rebuilds are throttled by
        /// <see cref="RebuildMoveFraction"/>, so this is paid on selection and
        /// then rarely.
        /// </summary>
        const int Segments = 192;
        /// <summary>Band width as a fraction of the radius, and its limits in metres.</summary>
        const float BandFraction = 0.030f;
        const float MinBandM = 70f, MaxBandM = 900f;
        /// <summary>
        /// Share of the band's width given to the soft shoulder on each side of
        /// the bright core. Feathering is what lets the band be tens of metres
        /// across — wide enough to see from 20 km up — without reading as a
        /// painted stripe when the camera comes down to it.
        /// </summary>
        const float FeatherFraction = 0.36f;
        /// <summary>
        /// Metres the band floats above the terrain. Enough to beat z-fighting
        /// with the streamed mesh and to carry the band over the small rises
        /// between two samples of the circumference; low enough that from a
        /// shallow angle it still reads as painted on the ground.
        /// </summary>
        const float BandLiftM = 30f;
        /// <summary>Cardinal tick spurs: how far in and out of the band they reach, as a fraction of the radius.</summary>
        const float TickReachFraction = 0.018f;
        const float MinTickReachM = 60f;
        /// <summary>Angular half-width of a tick spur, degrees.</summary>
        const float TickHalfAngleDeg = 0.9f;
        /// <summary>Re-sample the terrain once the centre has moved this fraction of the radius.</summary>
        const float RebuildMoveFraction = 0.04f;
        const float RevealSeconds = 0.4f;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        Material _bandMat, _tickMat;
        Transform _band, _ticks;
        TextMesh _caption;
        Transform _captionAnchor;

        Color _color;
        string _title = "";
        double _lat, _lon;
        /// <summary>Centre the geometry was last built for; a move past the threshold rebuilds.</summary>
        double _builtLat, _builtLon;
        float _radiusKm, _builtRadiusKm;
        double _centreHeight;
        bool _visible;
        float _revealT;

        public static RangeRing Create(CesiumGeoreference geo, Transform parent, Color color, string title)
        {
            var go = new GameObject("RangeRing_" + title);
            go.transform.SetParent(parent, false);

            var ring = go.AddComponent<RangeRing>();
            ring._geo = geo;
            ring._color = color;
            ring._title = title;
            ring._anchor = go.AddComponent<CesiumGlobeAnchor>();

            ring._bandMat = RuntimeMaterials.UnlitColor(color);
            ring._tickMat = RuntimeMaterials.UnlitColor(color);
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
            // characterSize absorbs MapFont's fixed rasterisation size, so the
            // caption keeps the size it had while sharing the map's font atlas.
            _caption.characterSize = 8f * 44f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = color;
            _caption.text = "";
        }

        /// <summary>
        /// Show (or reposition) the ring. <paramref name="caption"/> overrides
        /// the default "&lt;title&gt; 4.5 km" readout — fog contacts need to
        /// state a time and a designation, not just a distance.
        /// </summary>
        public void Show(double lat, double lon, float radiusKm, string caption = null)
        {
            if (radiusKm <= 0f) { Hide(); return; }

            bool wasHidden = !_visible;
            _lat = lat; _lon = lon; _radiusKm = radiusKm;
            _visible = true;
            if (wasHidden) _revealT = 0f;      // replay the reveal for a new selection

            _caption.text = caption ?? $"{_title} {radiusKm:0.#} km";
            gameObject.SetActive(true);

            MoveAnchor();

            // Re-sampling 128 terrain raycasts every frame would be ruinous on a
            // marching unit, and pointless: the ground under a ring does not
            // meaningfully change until the ring has moved a fair way across it.
            float movedM = (float)(GeoUtils.DistanceKm(_builtLat, _builtLon, lat, lon) * 1000.0);
            bool radiusChanged = !Mathf.Approximately(_builtRadiusKm, radiusKm);
            if (radiusChanged || movedM > radiusKm * 1000f * RebuildMoveFraction || _band == null)
                Rebuild();
        }

        public void Hide()
        {
            _visible = false;
            gameObject.SetActive(false);
        }

        void MoveAnchor()
        {
            _centreHeight = GeoUtils.SampleTerrainHeight(_geo, _lat, _lon, 250.0);
            _anchor.longitudeLatitudeHeight = new double3(_lon, _lat, _centreHeight);
        }

        void OnGeoChanged()
        {
            // The anchor keeps the ring on the globe by itself; only the sampled
            // heights baked into the mesh need refreshing.
            if (_visible) Rebuild();
        }

        void LateUpdate()
        {
            if (!_visible) return;

            _revealT = Mathf.Min(_revealT + Time.unscaledDeltaTime, RevealSeconds);
            float reveal = Mathf.SmoothStep(0f, 1f, _revealT / RevealSeconds);

            // A slow breath, so the ring reads as live telemetry. Brightness
            // only — the radius states a real distance and never moves.
            float breathe = Mathf.Lerp(0.72f, 1f, (Mathf.Sin(Time.unscaledTime * 2.2f) + 1f) * 0.5f);

            var band = _color; band.a = Mathf.Lerp(0.78f, 1f, breathe) * reveal;
            _bandMat.color = band;

            // The ticks are the graduations on the instrument: steady rather
            // than breathing, so the eye reads them as marks on a scale.
            var tick = _color; tick.a = 0.9f * reveal;
            _tickMat.color = tick;

            BillboardCaption();
        }

        void BillboardCaption()
        {
            var cam = Camera.main;
            if (cam == null || _captionAnchor == null) return;

            Vector3 pos = _captionAnchor.position;
            float depth = Mathf.Max(1f, Vector3.Dot(pos - cam.transform.position, cam.transform.forward));
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up);
        }

        // -------------------------------------------------------- geometry

        /// <summary>
        /// Samples the circumference once and bakes it into two flat meshes, in
        /// the anchor's local east-north-up frame with heights relative to the
        /// centre. Local space is the whole point: the ring then follows a
        /// marching unit by moving its anchor, with no re-sampling at all.
        /// </summary>
        void Rebuild()
        {
            _builtLat = _lat; _builtLon = _lon; _builtRadiusKm = _radiusKm;

            float radiusM = _radiusKm * 1000f;
            float bandW = Mathf.Clamp(radiusM * BandFraction, MinBandM, MaxBandM);

            // Height of the terrain under each point on the circle, relative to
            // the centre — the local frame's +Y. This is what makes the ring a
            // line drawn on the ground rather than a disc floating over it.
            var drop = new float[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                double bearing = (i % Segments) * 360.0 / Segments;
                GeoUtils.Destination(_lat, _lon, bearing, _radiusKm, out double lat2, out double lon2);
                double h = GeoUtils.SampleTerrainHeight(_geo, lat2, lon2, _centreHeight);
                drop[i] = (float)(h - _centreHeight);
            }

            BuildBand(radiusM, bandW, drop);
            BuildTicks(radiusM, bandW, drop);
            PlaceCaption(radiusM, drop[0], bandW);
        }

        /// <summary>
        /// The band: a flat ribbon on the ground, bright along the true radius
        /// and fading to nothing on both shoulders. Four vertex rings — outer
        /// fade, core, core, inner fade — so the fall-off is linear across the
        /// shoulders instead of across the whole width, which would leave the
        /// stated radius no brighter than the ground either side of it.
        /// </summary>
        void BuildBand(float radiusM, float bandW, float[] drop)
        {
            float feather = bandW * FeatherFraction;
            float coreHalf = Mathf.Max(1f, bandW * 0.5f - feather);

            float[] radii =
            {
                radiusM - coreHalf - feather,
                radiusM - coreHalf,
                radiusM + coreHalf,
                radiusM + coreHalf + feather
            };
            float[] alpha = { 0f, 1f, 1f, 0f };

            var verts = new List<Vector3>((Segments + 1) * radii.Length);
            var colours = new List<Color>((Segments + 1) * radii.Length);
            var tris = new List<int>(Segments * (radii.Length - 1) * 12);

            for (int i = 0; i <= Segments; i++)
            {
                float a = (i % Segments) * Mathf.PI * 2f / Segments;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                float y = drop[i] + BandLiftM;

                for (int r = 0; r < radii.Length; r++)
                {
                    verts.Add(new Vector3(s * radii[r], y, c * radii[r]));
                    colours.Add(new Color(1f, 1f, 1f, alpha[r]));
                }
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = i * radii.Length;
                int n = b + radii.Length;
                for (int r = 0; r + 1 < radii.Length; r++)
                    AddQuad(tris, b + r, b + r + 1, n + r + 1, n + r);
            }

            _band = Mesh("Band", verts, colours, tris, _bandMat, _band);
        }

        /// <summary>
        /// Four spurs at the cardinals, reaching in and out across the band.
        /// Small, and the only thing on the graphic that is not a circle — which
        /// is exactly why they work: they give the eye an orientation and make
        /// the ring read as a measurement rather than as a halo.
        /// </summary>
        void BuildTicks(float radiusM, float bandW, float[] drop)
        {
            float reach = Mathf.Max(MinTickReachM, radiusM * TickReachFraction) + bandW * 0.5f;
            float inner = Mathf.Max(1f, radiusM - reach);
            float outer = radiusM + reach;

            var verts = new List<Vector3>(16);
            var colours = new List<Color>(16);
            var tris = new List<int>(48);

            for (int q = 0; q < 4; q++)
            {
                float mid = q * 90f;
                for (int e = 0; e < 2; e++)
                {
                    float deg = mid + (e == 0 ? -TickHalfAngleDeg : TickHalfAngleDeg);
                    float a = deg * Mathf.Deg2Rad;
                    float s = Mathf.Sin(a), c = Mathf.Cos(a);

                    // Nearest sampled height on the circle — the spur is a few
                    // tenths of a degree wide, so one sample covers it.
                    int seg = Mathf.Clamp(Mathf.RoundToInt(((deg + 360f) % 360f) / 360f * Segments), 0, Segments);
                    float y = drop[seg] + BandLiftM;

                    verts.Add(new Vector3(s * inner, y, c * inner));
                    colours.Add(new Color(1f, 1f, 1f, 0.25f));
                    verts.Add(new Vector3(s * outer, y, c * outer));
                    colours.Add(new Color(1f, 1f, 1f, 1f));
                }

                int b = q * 4;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }

            _ticks = Mesh("Ticks", verts, colours, tris, _tickMat, _ticks);
        }

        void PlaceCaption(float radiusM, float northDrop, float bandW)
        {
            // Due north on the circle, just clear of the band so the text never
            // sits inside the graphic it is captioning.
            _captionAnchor.localPosition =
                new Vector3(0f, northDrop + BandLiftM + 30f, radiusM + bandW * 0.5f + 40f);
        }

        /// <summary>
        /// Both windings, because the band is translucent and is looked at from
        /// above, from a shallow angle and from underneath a ridge — and because
        /// the fallback shaders in <see cref="RuntimeMaterials"/> do cull.
        /// </summary>
        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(a); tris.Add(d); tris.Add(c);
        }

        Transform Mesh(string name, List<Vector3> verts, List<Color> colours, List<int> tris,
            Material material, Transform existing)
        {
            var go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            var filter = go.GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new UnityEngine.Mesh { name = "RangeRing_" + name };
                filter.sharedMesh = mesh;
            }

            // Cleared before re-filling: a shrinking mesh would otherwise keep
            // triangles indexing vertices that no longer exist.
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            return go.transform;
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= OnGeoChanged;
            if (_bandMat != null) Destroy(_bandMat);
            if (_tickMat != null) Destroy(_tickMat);

            foreach (var t in new[] { _band, _ticks })
            {
                if (t == null) continue;
                var f = t.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
            }
        }
    }
}
