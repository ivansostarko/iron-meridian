using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Code-built fire, smoke, explosion and dust. These are the stand-ins used
    /// whenever an authored prefab is unavailable — not imported yet, stripped
    /// from a build, or (today) written for a render pipeline this project does
    /// not run. Same reasoning as <see cref="DeployEffect"/>: the game must look
    /// correct with zero asset dependencies.
    ///
    /// Everything here is authored at roughly **one world unit**. <see cref="VfxSystem"/>
    /// scales the root transform to <see cref="VfxDef.scaleMeters"/> and sets
    /// <see cref="ParticleSystemScalingMode.Hierarchy"/>, so particle size *and*
    /// velocity scale together — author once, use at any map scale.
    /// </summary>
    public static class ProceduralVfx
    {
        static Material _puffMat;

        /// <summary>
        /// One shared additive-ish billboard material for every procedural
        /// effect. Particle colour comes from the systems themselves, so a
        /// single white texture serves fire, smoke and dust alike.
        /// </summary>
        static Material PuffMaterial()
        {
            if (_puffMat == null)
                _puffMat = RuntimeMaterials.UnlitTexture(ProceduralTextures.Puff(Color.white));
            return _puffMat;
        }

        /// <summary>Builds the requested fallback under <paramref name="root"/>.</summary>
        public static void Build(GameObject root, VfxDef def)
        {
            switch (def.fallback)
            {
                case VfxFallback.Explosion: BuildExplosion(root, def.tint); break;
                case VfxFallback.Impact:    BuildImpact(root, def.tint);    break;
                case VfxFallback.Fire:      BuildFire(root, def.tint);      break;
                case VfxFallback.Smoke:     BuildSmoke(root, def.tint);     break;
                case VfxFallback.Dust:      BuildDust(root, def.tint);      break;
            }
        }

        // ------------------------------------------------------------ fire

        static void BuildFire(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Flames", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.40f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Negative gravity is the cheapest convincing buoyancy: flames climb
            // and accelerate rather than coasting to a stop like debris.
            main.gravityModifier = -0.12f;
            main.maxParticles = 90;

            var emission = ps.emission;
            emission.rateOverTime = 26f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.22f;
            shape.radiusThickness = 1f;

            // Yellow-white core cooling through orange to a dark, fading ember.
            SetColourRamp(ps, new[]
            {
                (0.00f, new Color(1.00f, 0.95f, 0.72f), 0.00f),
                (0.12f, new Color(1.00f, 0.86f, 0.40f), 0.95f),
                (0.45f, tint,                            0.80f),
                (0.78f, new Color(0.55f, 0.16f, 0.05f), 0.35f),
                (1.00f, new Color(0.20f, 0.09f, 0.05f), 0.00f)
            });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1.0f), new Keyframe(1f, 0.45f)));

            // Flames should not be a straight column — a slow noise field gives
            // the licking motion the eye reads as fire.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 0.55f;
            noise.scrollSpeed = 0.7f;

            ps.Play();

            // A short-lived smoke crown above the flames so fire always reads as
            // fire from a distance, even when the flame particles are sub-pixel.
            var smoke = NewSystem(root, "FireSmoke", loop: true);
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.gravityModifier = -0.05f;
            sm.maxParticles = 60;

            var se = smoke.emission;
            se.rateOverTime = 9f;

            var ss = smoke.shape;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 16f;
            ss.radius = 0.3f;

            SetColourRamp(smoke, new[]
            {
                (0.00f, new Color(0.35f, 0.28f, 0.24f), 0.00f),
                (0.20f, new Color(0.26f, 0.24f, 0.23f), 0.55f),
                (1.00f, new Color(0.30f, 0.30f, 0.30f), 0.00f)
            });

            var sSize = smoke.sizeOverLifetime;
            sSize.enabled = true;
            sSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 2.4f));

            smoke.Play();
        }

        // ----------------------------------------------------------- smoke

        static void BuildSmoke(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Smoke", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.35f;

            SetColourRamp(ps, new[]
            {
                (0.00f, tint,                                        0.00f),
                (0.18f, tint,                                        0.62f),
                (0.65f, Color.Lerp(tint, Color.grey, 0.45f),          0.40f),
                (1.00f, Color.Lerp(tint, Color.grey, 0.75f),          0.00f)
            });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.8f));

            // Churn: without rotation the billboards read as a stack of identical
            // discs sliding upward.
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.35f;

            ps.Play();
        }

        // ------------------------------------------------------- explosion

        static void BuildExplosion(GameObject root, Color tint)
        {
            // 1. Flash — a single huge, very short-lived particle. Reads as the
            //    detonation instant before the eye resolves anything else.
            var flash = NewSystem(root, "Flash", loop: false);
            var fm = flash.main;
            fm.duration = 0.3f;
            fm.startLifetime = 0.22f;
            fm.startSpeed = 0f;
            fm.startSize = 2.6f;
            fm.startColor = new Color(1f, 0.94f, 0.72f);
            fm.maxParticles = 4;
            flash.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetColourRamp(flash, new[]
            {
                (0.00f, new Color(1.00f, 0.98f, 0.85f), 0.95f),
                (1.00f, new Color(1.00f, 0.60f, 0.20f), 0.00f)
            });
            var flashSize = flash.sizeOverLifetime;
            flashSize.enabled = true;
            flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));
            flash.Play();

            // 2. Fireball — the expanding burning core.
            var fire = NewSystem(root, "Fireball", loop: false);
            var bm = fire.main;
            bm.duration = 0.5f;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            bm.gravityModifier = -0.15f;
            bm.maxParticles = 48;
            fire.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            var bs = fire.shape;
            bs.shapeType = ParticleSystemShapeType.Sphere;
            bs.radius = 0.25f;
            SetColourRamp(fire, new[]
            {
                (0.00f, new Color(1.00f, 0.92f, 0.60f), 1.00f),
                (0.35f, tint,                            0.90f),
                (0.72f, new Color(0.50f, 0.16f, 0.05f), 0.55f),
                (1.00f, new Color(0.15f, 0.10f, 0.08f), 0.00f)
            });
            var drag = fire.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.6f;
            fire.Play();

            // 3. Smoke column — lingers well after the fireball, and is what
            //    stays legible when the camera is kilometres up.
            var smoke = NewSystem(root, "Smoke", loop: false);
            var sm = smoke.main;
            sm.duration = 0.8f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            sm.startSize = new ParticleSystem.MinMaxCurve(1.0f, 1.9f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.gravityModifier = -0.06f;
            sm.maxParticles = 60;
            smoke.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.05f, 12),
                new ParticleSystem.Burst(0.35f, 8)
            });
            var ss = smoke.shape;
            ss.shapeType = ParticleSystemShapeType.Sphere;
            ss.radius = 0.4f;
            SetColourRamp(smoke, new[]
            {
                (0.00f, new Color(0.30f, 0.26f, 0.24f), 0.00f),
                (0.15f, new Color(0.22f, 0.21f, 0.20f), 0.85f),
                (1.00f, new Color(0.34f, 0.34f, 0.34f), 0.00f)
            });
            var sSize = smoke.sizeOverLifetime;
            sSize.enabled = true;
            sSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 2.6f));
            smoke.Play();

            // 4. Ground dust ring — sells the blast sitting *on* the terrain
            //    rather than floating in the air above it.
            BuildDust(root, new Color(0.62f, 0.56f, 0.47f));
        }

        // -------------------------------------------------------- impact

        static void BuildImpact(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Impact", loop: false);

            var main = ps.main;
            main.duration = 0.35f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 24;

            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            SetColourRamp(ps, new[]
            {
                (0.00f, Color.Lerp(tint, Color.white, 0.5f), 0.90f),
                (0.45f, tint,                                0.60f),
                (1.00f, tint,                                0.00f)
            });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.5f;

            ps.Play();
        }

        // ---------------------------------------------------------- dust

        static void BuildDust(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Dust", loop: false);
            // Emit across the ground plane rather than straight up.
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.maxParticles = 40;

            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.25f;
            shape.radiusThickness = 1f;

            SetColourRamp(ps, new[]
            {
                (0.00f, tint, 0.00f),
                (0.12f, tint, 0.70f),
                (1.00f, tint, 0.00f)
            });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.0f));

            // Dust settles instead of flying flat out to the horizon.
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.55f;

            ps.Play();
        }

        // ------------------------------------------------------- helpers

        static ParticleSystem NewSystem(GameObject root, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = loop;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            // Hierarchy scaling is what lets these be authored at ~1 unit and
            // used at 300 m: the root's scale multiplies size and velocity.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = PuffMaterial();
            // Face the camera plane rather than the camera position: the
            // strategic view is near-top-down, where per-particle facing makes
            // a plume splay outward at the screen edges.
            renderer.alignment = ParticleSystemRenderSpace.View;

            return ps;
        }

        /// <summary>Applies a colour-over-lifetime ramp from (time, colour, alpha) stops.</summary>
        static void SetColourRamp(ParticleSystem ps, (float t, Color c, float a)[] stops)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;

            var colourKeys = new GradientColorKey[stops.Length];
            var alphaKeys = new GradientAlphaKey[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                colourKeys[i] = new GradientColorKey(stops[i].c, stops[i].t);
                alphaKeys[i] = new GradientAlphaKey(stops[i].a, stops[i].t);
            }

            var grad = new Gradient();
            grad.SetKeys(colourKeys, alphaKeys);
            col.color = new ParticleSystem.MinMaxGradient(grad);
        }
    }
}
