using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// The target area of a fire mission, drawn as a volume rather than a decal.
    ///
    /// A flat ring painted on the imagery is unreadable on this map: the camera
    /// spends most of its time at a shallow pitch, where a circle on sloping
    /// ground foreshortens into a line and disappears entirely behind a ridge.
    /// So the area is a **cylinder of light** — a translucent wall rising out of
    /// the ground, a bright rim where it meets the terrain, a rotating sweep
    /// inside it, and a centre column marking the aim point. From directly above
    /// it reads as a circle; from the side it reads as a volume standing on the
    /// ground; and it stays visible over a crest because the wall is tall enough
    /// to clear it.
    ///
    /// Everything is one procedural mesh with vertex colours (plus a second for
    /// the rotating sweep), so there is no texture to blur when the area is
    /// 260 m across and no material asset to ship.
    ///
    /// Two states, driven by <see cref="SetAlarm"/>: calm while the player is
    /// aiming, and increasingly urgent — faster pulse, hotter colour, quicker
    /// sweep — as the countdown runs out.
    /// </summary>
    public class TargetAreaMarker : MonoBehaviour
    {
        /// <summary>Wall height as a fraction of the radius. Tall enough to clear a ridge, short enough not to be a tower.</summary>
        const float HeightRatio = 0.60f;
        /// <summary>Segments around the circle. 72 is smooth at 260 m and still a trivial mesh.</summary>
        const int Segments = 72;
        /// <summary>Dashes in the rotating sweep ring.</summary>
        const int SweepDashes = 16;

        CesiumGeoreference _geo;
        Transform _sweep;
        Material _bodyMat;
        Material _sweepMat;
        Color _baseColour;
        CesiumGlobeAnchor _anchor;

        float _radius;
        float _time;
        /// <summary>0 while aiming, 1 the instant before impact.</summary>
        float _alarm;

        public float RadiusMeters => _radius;

        public static TargetAreaMarker Create(CesiumGeoreference geo, float radiusMeters, Color colour)
        {
            var go = new GameObject("ArtilleryTargetArea");
            go.transform.SetParent(geo.transform, false);

            var marker = go.AddComponent<TargetAreaMarker>();
            marker._geo = geo;
            marker._radius = radiusMeters;
            marker._baseColour = colour;
            marker._anchor = go.AddComponent<CesiumGlobeAnchor>();
            marker.BuildMesh();
            return marker;
        }

        /// <summary>
        /// Rebuilds for a different nature without respawning the object — the
        /// player flipping between 105 mm and 203 mm should see the area resize,
        /// not blink.
        /// </summary>
        public void Reshape(float radiusMeters, Color colour)
        {
            if (Mathf.Approximately(_radius, radiusMeters) && colour == _baseColour) return;
            _radius = radiusMeters;
            _baseColour = colour;

            // Rebuilt wholesale: the mesh is a few hundred vertices, and editing
            // it in place would mean a second copy of every offset calculation
            // below purely for the update path.
            ClearBuilt();
            BuildMesh();
        }

        /// <summary>
        /// Tears down everything <see cref="BuildMesh"/> made.
        ///
        /// Meshes and materials created with <c>new</c> are not owned by the
        /// GameObject and are not collected when it is destroyed, so both have
        /// to go explicitly or every switch between natures leaks one of each.
        /// The children are deactivated before being destroyed because
        /// <c>Destroy</c> is deferred to the end of the frame — without it the
        /// old marker draws over the new one for a frame and the switch reads
        /// as a flicker.
        /// </summary>
        void ClearBuilt()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;

                var filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);

                child.SetActive(false);
                Destroy(child);
            }

            if (_bodyMat != null) { Destroy(_bodyMat); _bodyMat = null; }
            if (_sweepMat != null) { Destroy(_sweepMat); _sweepMat = null; }
            _sweep = null;
        }

        /// <summary>Puts the area on a geodetic point, sitting on the terrain there.</summary>
        public void MoveTo(double lat, double lon)
        {
            double h = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250.0);
            // Lifted a little so the ground disc is not z-fighting the terrain mesh.
            _anchor.longitudeLatitudeHeight = new double3(lon, lat, h + 3.0);
        }

        /// <summary>
        /// How close the mission is to landing, 0..1. Drives the whole visual
        /// escalation, so the marker is a countdown in its own right and the
        /// player does not have to watch the HUD to feel the clock.
        /// </summary>
        public void SetAlarm(float t01) => _alarm = Mathf.Clamp01(t01);

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        void LateUpdate()
        {
            // Unscaled: the marker must keep breathing while the battle is
            // paused, exactly like the effect-placement reticle.
            _time += Time.unscaledDeltaTime;

            // Pulse accelerates from a slow breath to an urgent flash.
            float pulseHz = Mathf.Lerp(1.1f, 5.0f, _alarm);
            float pulse = Mathf.Lerp(0.72f, 1.0f, (Mathf.Sin(_time * pulseHz * Mathf.PI * 2f) + 1f) * 0.5f);

            // Colour runs from the nature's own colour toward a hot warning red.
            var colour = Color.Lerp(_baseColour, new Color(1.00f, 0.28f, 0.18f), _alarm * 0.75f);
            colour.a = pulse;

            if (_bodyMat != null) _bodyMat.color = colour;
            if (_sweepMat != null)
            {
                var sweepColour = colour;
                sweepColour.a = Mathf.Min(1f, pulse * 1.25f);
                _sweepMat.color = sweepColour;
            }

            if (_sweep != null)
                _sweep.localRotation = Quaternion.Euler(0f, _time * Mathf.Lerp(30f, 150f, _alarm), 0f);
        }

        void OnDestroy() => ClearBuilt();

        // --------------------------------------------------------- geometry

        void BuildMesh()
        {
            float r = _radius;
            float h = r * HeightRatio;

            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();

            // Ground disc — faint fill so the area reads as a piece of ground
            // and not merely an outline, brightening toward the edge.
            AppendDisc(verts, colours, tris, r * 0.985f, 0f, 0.05f, 0.16f);

            // Rim where the wall meets the terrain: the brightest element, and
            // the one the eye actually uses to judge where the rounds will fall.
            AppendAnnulus(verts, colours, tris, r * 0.93f, r, 0.6f, 0.95f, 0f);

            // The wall. Alpha falls to nothing at the top so the volume fades
            // out rather than ending in a hard edge that reads as a solid drum.
            AppendWall(verts, colours, tris, r, h, 0.34f, 0f);

            // Faint ring at the top, which closes the cylinder visually without
            // capping it — a solid lid would hide everything inside from above.
            AppendAnnulus(verts, colours, tris, r * 0.90f, r, 0.0f, 0.14f, h);

            // Cardinal ticks on the ground: four short radial bars. They give
            // the circle an orientation, which is what stops a plain ring from
            // looking like a smudge at a shallow camera angle.
            for (int i = 0; i < 4; i++)
                AppendRadialTick(verts, colours, tris, i * 90f, r * 0.52f, r * 0.90f, r * 0.022f, 0.55f);

            // Centre column: the aim point itself, as two crossed vertical
            // blades so it is visible from any bearing.
            AppendBlade(verts, colours, tris, 0f, r * 0.030f, h * 0.85f, 0.45f);
            AppendBlade(verts, colours, tris, 90f, r * 0.030f, h * 0.85f, 0.45f);

            _bodyMat = RuntimeMaterials.UnlitColor(_baseColour);
            MakeRenderer("Volume", verts, colours, tris, _bodyMat);

            // The sweep is a separate object because it spins; folding it into
            // the body mesh would mean rotating the whole marker.
            var sweepVerts = new List<Vector3>();
            var sweepColours = new List<Color>();
            var sweepTris = new List<int>();
            AppendDashedRing(sweepVerts, sweepColours, sweepTris, r * 0.78f, r * 0.86f, 0.75f, r * 0.012f);

            _sweepMat = RuntimeMaterials.UnlitColor(_baseColour);
            _sweep = MakeRenderer("Sweep", sweepVerts, sweepColours, sweepTris, _sweepMat).transform;
        }

        GameObject MakeRenderer(string name, List<Vector3> verts, List<Color> colours,
            List<int> tris, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var mesh = new Mesh { name = "TargetArea_" + name };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // Triangles are emitted in both windings throughout: the marker is
        // translucent and is looked at from outside, from inside and from
        // directly above, so no face may be culled away.
        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(a); tris.Add(d); tris.Add(c);
        }

        static Vector3 OnCircle(float radius, float angleRad, float y) =>
            new Vector3(Mathf.Cos(angleRad) * radius, y, Mathf.Sin(angleRad) * radius);

        void AppendDisc(List<Vector3> verts, List<Color> colours, List<int> tris,
            float radius, float y, float centreAlpha, float edgeAlpha)
        {
            int centre = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            colours.Add(new Color(1f, 1f, 1f, centreAlpha));

            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                verts.Add(OnCircle(radius, a, y));
                colours.Add(new Color(1f, 1f, 1f, edgeAlpha));
            }

            for (int i = 0; i < Segments; i++)
            {
                int p = centre + 1 + i, q = centre + 2 + i;
                tris.Add(centre); tris.Add(p); tris.Add(q);
                tris.Add(centre); tris.Add(q); tris.Add(p);
            }
        }

        void AppendAnnulus(List<Vector3> verts, List<Color> colours, List<int> tris,
            float inner, float outer, float innerAlpha, float outerAlpha, float y)
        {
            int start = verts.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                verts.Add(OnCircle(inner, a, y));
                colours.Add(new Color(1f, 1f, 1f, innerAlpha));
                verts.Add(OnCircle(outer, a, y));
                colours.Add(new Color(1f, 1f, 1f, outerAlpha));
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = start + i * 2;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }
        }

        void AppendWall(List<Vector3> verts, List<Color> colours, List<int> tris,
            float radius, float height, float baseAlpha, float topAlpha)
        {
            int start = verts.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                verts.Add(OnCircle(radius, a, 0f));
                colours.Add(new Color(1f, 1f, 1f, baseAlpha));
                verts.Add(OnCircle(radius, a, height));
                colours.Add(new Color(1f, 1f, 1f, topAlpha));
            }

            for (int i = 0; i < Segments; i++)
            {
                int b = start + i * 2;
                AddQuad(tris, b, b + 1, b + 3, b + 2);
            }
        }

        /// <summary>A short bar running outward along one bearing, lying on the ground.</summary>
        void AppendRadialTick(List<Vector3> verts, List<Color> colours, List<int> tris,
            float bearingDeg, float from, float to, float halfWidth, float alpha)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0f, dir.x) * halfWidth;

            int b = verts.Count;
            verts.Add(dir * from - side);
            verts.Add(dir * from + side);
            verts.Add(dir * to + side);
            verts.Add(dir * to - side);
            for (int i = 0; i < 4; i++) colours.Add(new Color(1f, 1f, 1f, alpha));

            AddQuad(tris, b, b + 1, b + 2, b + 3);
        }

        /// <summary>A vertical blade through the centre on a given bearing, fading upward.</summary>
        void AppendBlade(List<Vector3> verts, List<Color> colours, List<int> tris,
            float bearingDeg, float halfWidth, float height, float baseAlpha)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * halfWidth;

            int b = verts.Count;
            verts.Add(-dir);
            colours.Add(new Color(1f, 1f, 1f, baseAlpha));
            verts.Add(dir);
            colours.Add(new Color(1f, 1f, 1f, baseAlpha));
            verts.Add(dir + Vector3.up * height);
            colours.Add(new Color(1f, 1f, 1f, 0f));
            verts.Add(-dir + Vector3.up * height);
            colours.Add(new Color(1f, 1f, 1f, 0f));

            AddQuad(tris, b, b + 1, b + 2, b + 3);
        }

        /// <summary>Broken ring used for the rotating sweep, lifted just off the ground.</summary>
        void AppendDashedRing(List<Vector3> verts, List<Color> colours, List<int> tris,
            float inner, float outer, float alpha, float y)
        {
            // Half of each slot is drawn, half is the gap.
            float slot = Mathf.PI * 2f / SweepDashes;
            const int stepsPerDash = 4;

            for (int d = 0; d < SweepDashes; d++)
            {
                float a0 = d * slot;
                int start = verts.Count;

                for (int s = 0; s <= stepsPerDash; s++)
                {
                    float a = a0 + slot * 0.5f * (s / (float)stepsPerDash);
                    verts.Add(OnCircle(inner, a, y));
                    colours.Add(new Color(1f, 1f, 1f, alpha));
                    verts.Add(OnCircle(outer, a, y));
                    colours.Add(new Color(1f, 1f, 1f, alpha));
                }

                for (int s = 0; s < stepsPerDash; s++)
                {
                    int b = start + s * 2;
                    AddQuad(tris, b, b + 1, b + 3, b + 2);
                }
            }
        }
    }
}
