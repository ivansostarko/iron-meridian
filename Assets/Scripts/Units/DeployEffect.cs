using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// One-shot "unit deployed here" burst: an expanding shockwave ring plus a
    /// dust puff, anchored to the drop point on the globe. Self-destructs when
    /// it finishes. Everything is built from code and procedural textures, so
    /// it survives a player build with no asset dependencies.
    /// </summary>
    public class DeployEffect : MonoBehaviour
    {
        const float ShockSeconds = 0.55f;
        const float LifeSeconds = 1.6f;
        const float StartRadius = 60f;
        const float EndRadius = 620f;

        Transform _shock;
        Material _shockMat;
        float _t;

        public static void Play(CesiumGeoreference geo, double lat, double lon, Color color)
        {
            var root = new GameObject("DeployEffect");
            root.transform.SetParent(geo.transform, false);

            var anchor = root.AddComponent<CesiumGlobeAnchor>();
            double h = GeoUtils.SampleTerrainHeight(geo, lat, lon, 250);
            anchor.longitudeLatitudeHeight = new double3(lon, lat, h + 6.0);

            root.AddComponent<DeployEffect>().Build(color);
        }

        void Build(Color color)
        {
            // --- shockwave ring, flat on the ground ---
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "Shockwave";
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = Vector3.one * StartRadius;

            _shockMat = RuntimeMaterials.UnlitTexture(
                ProceduralTextures.Ring(color, 128, 0.36f, 0.48f));
            quad.GetComponent<MeshRenderer>().material = _shockMat;
            _shock = quad.transform;

            BuildDust(color);
            Destroy(gameObject, LifeSeconds);
        }

        void BuildDust(Color color)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            // Emit across the ground plane rather than straight up.
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.75f;
            main.startSpeed = 190f;          // metres/second — map scale, not centimetres
            main.startSize = 70f;
            main.startColor = color;
            main.gravityModifier = 0f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 40f;
            shape.radiusThickness = 1f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.55f;             // dust settles instead of flying flat out

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = RuntimeMaterials.UnlitTexture(ProceduralTextures.Disc(Color.white));

            ps.Play();
        }

        void Update()
        {
            if (_shock == null) return;
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / ShockSeconds);

            // Fast out, slow down — reads as an impact rather than a balloon.
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            _shock.localScale = Vector3.one * Mathf.Lerp(StartRadius, EndRadius, eased);

            var c = _shockMat.color;
            c.a = 1f - k;
            _shockMat.color = c;

            if (k >= 1f) _shock.gameObject.SetActive(false);
        }
    }
}
