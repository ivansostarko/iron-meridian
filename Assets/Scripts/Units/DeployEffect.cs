using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// "A formation is now here": the burst played when a unit is dropped onto
    /// the map from the palette.
    ///
    /// Four layers, because a flat expanding ring on its own disappears the
    /// moment the camera tilts — which is most of the time in 3D:
    ///
    ///   **Ring wall**    a cylinder of light standing on the ground, expanding
    ///                    outward and fading with height. This is the part that
    ///                    reads at any camera angle.
    ///   **Ground disc**  a bright flash under it, which is what reads from
    ///                    directly overhead.
    ///   **Marker column** a beam at the exact drop point, so the eye is told
    ///                    *where* rather than merely *near here*.
    ///   **Particles**    dust thrown outward along the ground, and embers
    ///                    rising through the column.
    ///
    /// Everything is procedural — mesh, textures and particles — so it survives
    /// a player build with no asset dependencies.
    ///
    /// The caller is responsible for having checked that there is ground here;
    /// <see cref="Play"/> refuses rather than guessing if the terrain has not
    /// streamed in, so a unit is never marked at a point on the globe that has
    /// no surface yet.
    /// </summary>
    public class DeployEffect : MonoBehaviour
    {
        const float RingSeconds = 0.85f;
        const float ColumnSeconds = 1.1f;
        const float LifeSeconds = 2.2f;
        const float StartRadius = 55f;
        const float EndRadius = 700f;
        /// <summary>Wall height at the start of the expansion, metres. It flattens as it spreads.</summary>
        const float WallHeight = 260f;
        /// <summary>How far the marker column reaches up, metres.</summary>
        const float ColumnHeight = 900f;
        const int Segments = 48;

        Transform _ring, _disc, _column;
        Material _ringMat, _discMat, _columnMat;
        UnityEngine.Mesh _ringMesh, _columnMesh;
        float _t;

        /// <summary>
        /// Plays the effect at a geodetic point. Returns false if the terrain
        /// there has not streamed in — the caller has already refused the drop
        /// in that case, and an effect floating at a guessed height would be the
        /// only thing on screen suggesting it succeeded.
        /// </summary>
        public static bool Play(CesiumGeoreference geo, double lat, double lon, Color color)
        {
            if (!GeoUtils.TrySampleTerrainHeight(geo, lat, lon, out double h))
            {
                Debug.LogWarning($"[DeployEffect] No terrain at {lat:0.0000}, {lon:0.0000} — " +
                                 "deploy burst skipped.");
                return false;
            }

            var root = new GameObject("DeployEffect");
            root.transform.SetParent(geo.transform, false);

            var anchor = root.AddComponent<CesiumGlobeAnchor>();
            anchor.longitudeLatitudeHeight = new double3(lon, lat, h + 4.0);

            root.AddComponent<DeployEffect>().Build(color);
            return true;
        }

        void Build(Color color)
        {
            BuildRingWall(color);
            BuildGroundDisc(color);
            BuildColumn(color);
            BuildDust(color);
            BuildEmbers(color);

            Destroy(gameObject, LifeSeconds);
        }

        // ------------------------------------------------------------ meshes

        /// <summary>
        /// The expanding wall. Built at unit radius and scaled outward, so the
        /// mesh is made once rather than rebuilt every frame.
        /// </summary>
        void BuildRingWall(Color color)
        {
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();

            for (int i = 0; i <= Segments; i++)
            {
                float a = (i % Segments) * Mathf.PI * 2f / Segments;
                float x = Mathf.Cos(a), z = Mathf.Sin(a);

                verts.Add(new Vector3(x, 0f, z));
                colours.Add(new Color(1f, 1f, 1f, 1f));
                verts.Add(new Vector3(x, 1f, z));
                colours.Add(new Color(1f, 1f, 1f, 0f));
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = i * 2;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }

            _ringMat = RuntimeMaterials.UnlitColor(color);
            _ringMesh = MakeMesh("RingWall", verts, colours, tris);
            _ring = Attach("RingWall", _ringMesh, _ringMat);
            _ring.localScale = new Vector3(StartRadius, WallHeight, StartRadius);
        }

        /// <summary>A flat bright disc under the wall — what reads from straight above.</summary>
        void BuildGroundDisc(Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "GroundDisc";
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * StartRadius * 2f;

            _discMat = RuntimeMaterials.UnlitTexture(
                ProceduralTextures.Ring(color, 128, 0.30f, 0.48f));
            quad.GetComponent<MeshRenderer>().material = _discMat;
            _disc = quad.transform;
        }

        /// <summary>
        /// Two crossed vertical blades marking the exact drop point. Crossed
        /// rather than one, so the marker is visible from any bearing.
        /// </summary>
        void BuildColumn(Color color)
        {
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();

            for (int blade = 0; blade < 2; blade++)
            {
                float a = blade * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                int b = verts.Count;
                verts.Add(-dir); colours.Add(new Color(1f, 1f, 1f, 0.9f));
                verts.Add(dir); colours.Add(new Color(1f, 1f, 1f, 0.9f));
                verts.Add(dir + Vector3.up); colours.Add(new Color(1f, 1f, 1f, 0f));
                verts.Add(-dir + Vector3.up); colours.Add(new Color(1f, 1f, 1f, 0f));

                AddQuad(tris, b, b + 1, b + 2, b + 3);
            }

            _columnMat = RuntimeMaterials.UnlitColor(color);
            _columnMesh = MakeMesh("Column", verts, colours, tris);
            _column = Attach("Column", _columnMesh, _columnMat);
            _column.localScale = new Vector3(28f, 0f, 28f);
        }

        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(a); tris.Add(d); tris.Add(c);
        }

        static UnityEngine.Mesh MakeMesh(string name, List<Vector3> verts, List<Color> colours, List<int> tris)
        {
            var mesh = new UnityEngine.Mesh { name = "Deploy_" + name };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        Transform Attach(string name, UnityEngine.Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // --------------------------------------------------------- particles

        /// <summary>Dust thrown outward along the ground by the arrival.</summary>
        void BuildDust(Color color)
        {
            var ps = NewSystem("Dust", Quaternion.Euler(-90f, 0f, 0f));

            var main = ps.main;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(150f, 320f);
            main.startSize = new ParticleSystem.MinMaxCurve(55f, 120f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 90;

            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 45f;
            shape.radiusThickness = 1f;

            Ramp(ps, new Color(0.72f, 0.68f, 0.60f), 0.8f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.0f));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.55f;

            ps.Play();
        }

        /// <summary>Embers rising through the marker column — the team-coloured layer.</summary>
        void BuildEmbers(Color color)
        {
            var ps = NewSystem("Embers", Quaternion.identity);

            var main = ps.main;
            main.duration = 0.9f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(180f, 420f);
            main.startSize = new ParticleSystem.MinMaxCurve(22f, 52f);
            main.gravityModifier = 0.08f;
            main.maxParticles = 70;

            ps.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 22),
                new ParticleSystem.Burst(0.18f, 10)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 30f;

            Ramp(ps, color, 1f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.35f;

            ps.Play();
        }

        ParticleSystem NewSystem(string name, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = localRotation;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = RuntimeMaterials.UnlitTexture(ProceduralTextures.Puff(Color.white));
            renderer.alignment = ParticleSystemRenderSpace.View;
            return ps;
        }

        static void Ramp(ParticleSystem ps, Color colour, float peakAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.12f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            _t += Time.unscaledDeltaTime;

            // --- expanding wall + disc ---
            float k = Mathf.Clamp01(_t / RingSeconds);
            // Fast out, slowing down — reads as an impact rather than a balloon.
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            float radius = Mathf.Lerp(StartRadius, EndRadius, eased);

            if (_ring != null)
            {
                // The wall flattens as it spreads, the way a shock front does.
                _ring.localScale = new Vector3(radius, WallHeight * (1f - eased * 0.75f), radius);
                var c = _ringMat.color; c.a = (1f - k) * 0.9f; _ringMat.color = c;
                if (k >= 1f) _ring.gameObject.SetActive(false);
            }

            if (_disc != null)
            {
                _disc.localScale = Vector3.one * radius * 2f;
                var c = _discMat.color; c.a = (1f - k); _discMat.color = c;
                if (k >= 1f) _disc.gameObject.SetActive(false);
            }

            // --- marker column: shoots up, then fades from the top down ---
            if (_column != null)
            {
                float ck = Mathf.Clamp01(_t / ColumnSeconds);
                float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(ck * 2.2f), 3f);
                _column.localScale = new Vector3(28f, ColumnHeight * rise, 28f);

                var c = _columnMat.color;
                c.a = Mathf.Clamp01(1f - Mathf.Pow(ck, 1.8f));
                _columnMat.color = c;
                if (ck >= 1f) _column.gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (_ringMat != null) Destroy(_ringMat);
            if (_discMat != null) Destroy(_discMat);
            if (_columnMat != null) Destroy(_columnMat);
            if (_ringMesh != null) Destroy(_ringMesh);
            if (_columnMesh != null) Destroy(_columnMesh);
        }
    }
}
