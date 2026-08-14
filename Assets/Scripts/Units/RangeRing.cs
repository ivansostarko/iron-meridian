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
    /// **This used to be a dashed line and was effectively invisible.** A
    /// `LineRenderer` twelve metres wide, drawn on a circle kilometres across and
    /// viewed from a camera kilometres up, is well under a pixel; and being
    /// depth-tested against the terrain it followed, whatever survived was
    /// chopped up by every fold of ground it passed behind. Neither is fixable by
    /// making the line wider — a line thick enough to see from altitude is a
    /// smear up close.
    ///
    /// So the range is drawn as a **fence of light standing on the terrain**: a
    /// translucent wall rising out of the ground along the whole circumference,
    /// fading out with height, over a bright band where it meets the ground. The
    /// wall's base is sunk below the terrain, so the fence always cuts the
    /// surface and can never float or be buried however rough the ground is. From
    /// directly overhead the band reads as a circle; from a shallow angle the
    /// wall reads as a boundary standing in the landscape; and it stays legible
    /// over a ridge because it is tall rather than thin. Motes drift up off the
    /// rim so it reads as live telemetry rather than a decal.
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
        /// <summary>Segments around the circumference.</summary>
        const int Segments = 128;
        /// <summary>Metres the wall is sunk below the sampled terrain, so it always cuts the surface.</summary>
        const float SkirtBelowM = 90f;
        /// <summary>Wall height as a fraction of the radius, and its hard limits in metres.</summary>
        const float WallHeightFraction = 0.055f;
        const float MinWallM = 140f, MaxWallM = 900f;
        /// <summary>Ground band width as a fraction of the radius, and its floor in metres.</summary>
        const float BandFraction = 0.022f;
        const float MinBandM = 45f;
        /// <summary>Metres the ground band floats above the terrain, to beat z-fighting.</summary>
        const float BandLiftM = 10f;
        /// <summary>Re-sample the terrain once the centre has moved this fraction of the radius.</summary>
        const float RebuildMoveFraction = 0.04f;
        const float RevealSeconds = 0.4f;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        Material _wallMat, _bandMat;
        Transform _wall, _band;
        ParticleSystem _motes;
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

            ring._wallMat = RuntimeMaterials.UnlitColor(color);
            ring._bandMat = RuntimeMaterials.UnlitColor(color);
            ring.BuildCaption(color);
            ring.BuildMotes(color);

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
        /// Motes drifting up off the rim. Emitted from the circle's edge rather
        /// than its area, so they mark the boundary rather than filling a disc
        /// the size of a town.
        /// </summary>
        void BuildMotes(Color color)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            // Circle shapes emit in the object's XY plane; stand it up so the
            // ring lies flat on the ground.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            _motes = go.AddComponent<ParticleSystem>();
            _motes.Stop();

            var main = _motes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(18f, 46f);
            main.startSize = new ParticleSystem.MinMaxCurve(24f, 62f);
            main.startColor = color;
            main.maxParticles = 220;

            var shape = _motes.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radiusThickness = 0f;      // the rim, not the disc
            shape.arc = 360f;

            var colour = _motes.colorOverLifetime;
            colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(grad);

            var size = _motes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = RuntimeMaterials.UnlitTexture(ProceduralTextures.Puff(Color.white));
            renderer.alignment = ParticleSystemRenderSpace.View;
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
            if (radiusChanged || movedM > radiusKm * 1000f * RebuildMoveFraction || _wall == null)
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

            var wall = _color; wall.a = 0.34f * breathe * reveal;
            _wallMat.color = wall;

            var band = _color; band.a = Mathf.Lerp(0.75f, 1f, breathe) * reveal;
            _bandMat.color = band;

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
        /// Samples the circumference once and bakes it into two meshes, in the
        /// anchor's local east-north-up frame with heights relative to the
        /// centre. Local space is the whole point: the ring then follows a
        /// marching unit by moving its anchor, with no re-sampling at all.
        /// </summary>
        void Rebuild()
        {
            _builtLat = _lat; _builtLon = _lon; _builtRadiusKm = _radiusKm;

            float radiusM = _radiusKm * 1000f;
            float wallH = Mathf.Clamp(radiusM * WallHeightFraction, MinWallM, MaxWallM);
            float bandW = Mathf.Max(MinBandM, radiusM * BandFraction);

            // Height of the terrain under each point on the circle, relative to
            // the centre — the local frame's +Y.
            var drop = new float[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                double bearing = (i % Segments) * 360.0 / Segments;
                GeoUtils.Destination(_lat, _lon, bearing, _radiusKm, out double lat2, out double lon2);
                double h = GeoUtils.SampleTerrainHeight(_geo, lat2, lon2, _centreHeight);
                drop[i] = (float)(h - _centreHeight);
            }

            BuildWall(radiusM, wallH, drop);
            BuildBand(radiusM, bandW, drop);
            PlaceCaption(radiusM, drop[0], wallH);

            if (_motes != null)
            {
                var shape = _motes.shape;
                shape.radius = radiusM;
                var main = _motes.main;
                // Emission scales with circumference, so a 400 m ring is not a
                // blizzard and a 12 km one is not four lonely dots.
                var emission = _motes.emission;
                emission.rateOverTime = Mathf.Clamp(radiusM * 0.06f, 8f, 90f);
                main.startSize = new ParticleSystem.MinMaxCurve(radiusM * 0.010f, radiusM * 0.024f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(wallH * 0.12f, wallH * 0.30f);
                if (!_motes.isPlaying) _motes.Play();
            }
        }

        /// <summary>The wall: a quad strip standing on the circumference, fading out with height.</summary>
        void BuildWall(float radiusM, float wallH, float[] drop)
        {
            var verts = new List<Vector3>((Segments + 1) * 2);
            var colours = new List<Color>((Segments + 1) * 2);
            var tris = new List<int>(Segments * 12);

            for (int i = 0; i <= Segments; i++)
            {
                float a = (i % Segments) * Mathf.PI * 2f / Segments;
                float east = Mathf.Sin(a) * radiusM;
                float north = Mathf.Cos(a) * radiusM;

                verts.Add(new Vector3(east, drop[i] - SkirtBelowM, north));
                colours.Add(new Color(1f, 1f, 1f, 1f));
                verts.Add(new Vector3(east, drop[i] + wallH, north));
                colours.Add(new Color(1f, 1f, 1f, 0f));
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = i * 2;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }

            _wall = Mesh("Wall", verts, colours, tris, _wallMat, _wall);
        }

        /// <summary>The ground band: what reads as a circle from directly above.</summary>
        void BuildBand(float radiusM, float bandW, float[] drop)
        {
            float inner = radiusM - bandW * 0.5f;
            float outer = radiusM + bandW * 0.5f;

            var verts = new List<Vector3>((Segments + 1) * 2);
            var colours = new List<Color>((Segments + 1) * 2);
            var tris = new List<int>(Segments * 12);

            for (int i = 0; i <= Segments; i++)
            {
                float a = (i % Segments) * Mathf.PI * 2f / Segments;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                float y = drop[i] + BandLiftM;

                verts.Add(new Vector3(s * inner, y, c * inner));
                colours.Add(new Color(1f, 1f, 1f, 0.35f));
                verts.Add(new Vector3(s * outer, y, c * outer));
                colours.Add(new Color(1f, 1f, 1f, 1f));
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = i * 2;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }

            _band = Mesh("Band", verts, colours, tris, _bandMat, _band);
        }

        void PlaceCaption(float radiusM, float northDrop, float wallH)
        {
            // Due north on the circle, lifted clear of the wall so the text is
            // never lost inside it.
            _captionAnchor.localPosition = new Vector3(0f, northDrop + wallH * 0.55f + 40f, radiusM);
        }

        /// <summary>
        /// Both windings, because the ring is translucent and is looked at from
        /// outside, inside and directly overhead — and because the fallback
        /// shaders in <see cref="RuntimeMaterials"/> do cull.
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
            if (_wallMat != null) Destroy(_wallMat);
            if (_bandMat != null) Destroy(_bandMat);

            foreach (var t in new[] { _wall, _band })
            {
                if (t == null) continue;
                var f = t.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
            }
        }
    }
}
