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
                case VfxFallback.ArtilleryAirBurst:   BuildArtilleryAirBurst(root, def.tint);   break;
                case VfxFallback.ArtilleryDirtColumn: BuildArtilleryDirtColumn(root, def.tint); break;
                case VfxFallback.ArtilleryHeavyBlast: BuildArtilleryHeavyBlast(root, def.tint); break;
                case VfxFallback.Shockwave:           BuildShockwave(root, def.tint);           break;
                case VfxFallback.Debris:              BuildDebris(root, def.tint);              break;
                case VfxFallback.Motes:               BuildMotes(root, def.tint);               break;
            }
        }

        // ------------------------------------------------------- shockwave

        /// <summary>
        /// The overpressure ring: a flat circle of particles thrown outward along
        /// the ground, fading as it goes.
        ///
        /// **Everything about this is horizontal.** The emitter is a ring with no
        /// thickness, gravity is zero, and there is no upward component at all —
        /// a shockwave that rose would read as another puff of smoke, and the one
        /// job this effect has is to state a *radius*. It is emitted as a single
        /// burst rather than over time so the ring stays a ring instead of
        /// smearing into a disc.
        ///
        /// Authored at ~1 unit like everything else here, so the call site scales
        /// it to the strike's own target area — which is the point: the ring the
        /// player was shown and the ring that races out are the same circle.
        /// </summary>
        static void BuildShockwave(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Shockwave", loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
            // One unit of radius in roughly half a second. Tuned against the
            // lifetime above rather than independently — together they are what
            // decides where the ring stops, and that has to be the target area.
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.maxParticles = 160;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 140) });

            // A circle edge, lying flat: radiusThickness 0 keeps every particle
            // on the rim rather than filling the disc.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.alignToDirection = true;

            SetColourRamp(ps, new[]
            {
                (0.00f, Color.white,               0.85f),
                (0.25f, tint,                      0.55f),
                (1.00f, tint,                      0.00f)
            });

            // The rim thickens as it goes, so the ring stays visible as its
            // particles spread further apart around a growing circumference.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 1.9f)));

            // Drag: the ring decelerates as it expands, which is what stops it
            // travelling for ever and what makes its edge read as a distance
            // rather than as a speed.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.35f;
            limit.limit = new ParticleSystem.MinMaxCurve(2.0f);

            ps.Play();
        }

        // ---------------------------------------------------------- debris

        /// <summary>
        /// Soil and fragments thrown out of an impact on ballistic arcs.
        ///
        /// Gravity is strongly positive and the cone is wide and shallow, so the
        /// throw goes *out* rather than up and comes back down — which is what
        /// separates debris from a dirt column. Stretched billboards, because a
        /// tumbling fragment seen from two kilometres up is a streak, not a dot.
        /// </summary>
        static void BuildDebris(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Debris", loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.8f;
            main.maxParticles = 90;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 62f;              // wide and low: a throw, not a fountain
            shape.radius = 0.10f;
            shape.radiusThickness = 1f;

            SetColourRamp(ps, new[]
            {
                (0.00f, tint * 1.3f, 0.95f),
                (0.70f, tint,        0.80f),
                (1.00f, tint * 0.6f, 0.00f)
            });

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2.4f;

            ps.Play();
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

        // ----------------------------------------------------------- motes

        /// <summary>
        /// Sparse motes rising off a patch of ground, forever.
        ///
        /// The marker effect: it says *something is here*, and it must not say
        /// anything else. Three properties do that work and each is the opposite
        /// of the smoke plume's.
        ///
        /// • **Sparse and slow.** A twelfth of the smoke column's emission rate
        ///   over four times its area, so what the eye gets is a scatter of
        ///   specks over the site rather than a source pouring out of one point.
        /// • **It does not grow.** Smoke doubles in size as it climbs, which is
        ///   what makes a plume read as combustion; a mote that keeps its size
        ///   reads as something caught in the light.
        /// • **It does not grey.** The tint stays the tint from bottom to top —
        ///   the darkening toward grey is the single strongest cue that
        ///   something is burning, and a rear area must never read that way.
        ///
        /// Emitted off a flat disc rather than a cone, because the thing being
        /// marked is *ground*: a cone puts every mote over the centre point,
        /// which states a source where there is only an area.
        /// </summary>
        static void BuildMotes(GameObject root, Color tint)
        {
            var ps = NewSystem(root, "Motes", loop: true);

            var main = ps.main;
            // Long-lived and unhurried. A mote that crossed the site in a second
            // would read as an ember thrown off something.
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.20f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // A whisper of lift, so the drift is unmistakably upward without the
            // motes ever leaving the site they are marking.
            main.gravityModifier = -0.02f;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            // A disc lying on the ground, emitting straight up: the footprint is
            // the statement, not a point inside it.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.42f;
            shape.radiusThickness = 1f;
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            SetColourRamp(ps, new[]
            {
                (0.00f, tint, 0.00f),
                (0.20f, tint, 0.55f),
                (0.70f, tint, 0.40f),
                (1.00f, tint, 0.00f)
            });

            // Drift rather than churn. Enough to stop the motes rising in
            // parallel lines; far short of the smoke plume's boil.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.18f;
            noise.scrollSpeed = 0.16f;

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

        // -------------------------------------------------- artillery bursts
        //
        // Three signatures rather than one scaled explosion, because the three
        // events do not look alike from a map camera. What separates them is
        // the *shape* of the throw: flat and wide for a high-order airburst,
        // narrow and vertical for a mortar bomb, and a broad fireball with
        // arcing debris for a heavy shell. See docs/17-ARTILLERY.md.

        /// <summary>
        /// 105 mm: a bright crack. Almost all of the energy goes sideways in a
        /// flat shrapnel disc, there is very little soil, and it is over fast —
        /// which is what makes it read as light next to the heavier natures.
        /// </summary>
        static void BuildArtilleryAirBurst(GameObject root, Color tint)
        {
            // Flash: brief and white-hot, brighter than a general explosion's.
            var flash = NewSystem(root, "Flash", loop: false);
            var fm = flash.main;
            fm.duration = 0.2f;
            fm.startLifetime = 0.16f;
            fm.startSpeed = 0f;
            fm.startSize = 2.0f;
            fm.maxParticles = 4;
            flash.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetColourRamp(flash, new[]
            {
                (0.00f, new Color(1.00f, 1.00f, 0.95f), 1.00f),
                (0.45f, new Color(1.00f, 0.90f, 0.55f), 0.70f),
                (1.00f, tint,                            0.00f)
            });
            var flashSize = flash.sizeOverLifetime;
            flashSize.enabled = true;
            flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1.5f));
            flash.Play();

            // Shrapnel disc: emitted across the ground plane at high speed with
            // little drag, so it whips outward and stops. This flat, fast ring
            // is the signature of the nature.
            var frag = NewSystem(root, "Shrapnel", loop: false);
            frag.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var gm = frag.main;
            gm.duration = 0.3f;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            gm.startSpeed = new ParticleSystem.MinMaxCurve(4.0f, 7.5f);
            gm.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            gm.gravityModifier = 0.05f;
            gm.maxParticles = 60;
            frag.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });
            var gs = frag.shape;
            gs.shapeType = ParticleSystemShapeType.Circle;
            gs.radius = 0.12f;
            gs.radiusThickness = 1f;
            SetColourRamp(frag, new[]
            {
                (0.00f, new Color(1.00f, 0.96f, 0.78f), 0.95f),
                (0.40f, tint,                            0.65f),
                (1.00f, new Color(0.55f, 0.45f, 0.35f), 0.00f)
            });
            var gd = frag.limitVelocityOverLifetime;
            gd.enabled = true;
            gd.dampen = 0.30f;
            frag.Play();

            // A small, quick core. Deliberately not a rolling fireball.
            var core = NewSystem(root, "Core", loop: false);
            var cm = core.main;
            cm.duration = 0.3f;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            cm.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            cm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            cm.gravityModifier = -0.10f;
            cm.maxParticles = 24;
            core.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
            var cs = core.shape;
            cs.shapeType = ParticleSystemShapeType.Sphere;
            cs.radius = 0.16f;
            SetColourRamp(core, new[]
            {
                (0.00f, new Color(1.00f, 0.95f, 0.72f), 1.00f),
                (0.50f, tint,                            0.75f),
                (1.00f, new Color(0.35f, 0.22f, 0.12f), 0.00f)
            });
            core.Play();

            BuildDust(root, new Color(0.70f, 0.66f, 0.58f));
        }

        /// <summary>
        /// 120 mm mortar: a bomb arriving almost vertically. Nearly everything
        /// thrown up goes *up* — a narrow column of earth that rises and falls
        /// back — with only a small flash. More soil than fire, which is what a
        /// mortar impact actually looks like.
        /// </summary>
        static void BuildArtilleryDirtColumn(GameObject root, Color tint)
        {
            // Soil column: a tight cone straight up, with real gravity so the
            // ejecta arcs over and falls back rather than drifting away.
            var soil = NewSystem(root, "SoilColumn", loop: false);
            var sm = soil.main;
            sm.duration = 0.35f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 8.0f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.gravityModifier = 1.15f;
            sm.maxParticles = 70;
            soil.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 42) });
            var ss = soil.shape;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 11f;                    // narrow: this is the whole look
            ss.radius = 0.12f;
            SetColourRamp(soil, new[]
            {
                (0.00f, Color.Lerp(tint, Color.white, 0.35f), 0.95f),
                (0.35f, tint,                                 0.85f),
                (1.00f, Color.Lerp(tint, Color.black, 0.35f), 0.00f)
            });
            var soilSize = soil.sizeOverLifetime;
            soilSize.enabled = true;
            soilSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 1.5f));
            soil.Play();

            // Small muzzle-bright flash at the base, gone almost immediately.
            var flash = NewSystem(root, "Flash", loop: false);
            var fm = flash.main;
            fm.duration = 0.18f;
            fm.startLifetime = 0.14f;
            fm.startSpeed = 0f;
            fm.startSize = 1.1f;
            fm.maxParticles = 3;
            flash.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetColourRamp(flash, new[]
            {
                (0.00f, new Color(1.00f, 0.92f, 0.68f), 0.85f),
                (1.00f, new Color(0.85f, 0.55f, 0.25f), 0.00f)
            });
            flash.Play();

            // Skirt of earth thrown out along the ground at the base of the column.
            var skirt = NewSystem(root, "Skirt", loop: false);
            skirt.transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            var km = skirt.main;
            km.duration = 0.35f;
            km.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            km.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            km.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            km.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            km.gravityModifier = 0.45f;
            km.maxParticles = 40;
            skirt.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var ks = skirt.shape;
            ks.shapeType = ParticleSystemShapeType.Cone;
            ks.angle = 55f;
            ks.radius = 0.2f;
            SetColourRamp(skirt, new[]
            {
                (0.00f, tint,                                 0.00f),
                (0.15f, tint,                                 0.80f),
                (1.00f, Color.Lerp(tint, Color.grey, 0.5f),   0.00f)
            });
            skirt.Play();
        }

        /// <summary>
        /// 155 mm and 203 mm: a proper high-explosive shell. Fireball, a ground
        /// shock ring racing out along the terrain, and heavy debris arcing up
        /// and falling back. The two calibres share this signature and differ by
        /// the scale and lifetime on their catalogue rows, because that *is* the
        /// difference between them — same event, twice the size.
        /// </summary>
        static void BuildArtilleryHeavyBlast(GameObject root, Color tint)
        {
            // Flash.
            var flash = NewSystem(root, "Flash", loop: false);
            var fm = flash.main;
            fm.duration = 0.3f;
            fm.startLifetime = 0.26f;
            fm.startSpeed = 0f;
            fm.startSize = 3.0f;
            fm.maxParticles = 4;
            flash.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetColourRamp(flash, new[]
            {
                (0.00f, new Color(1.00f, 0.98f, 0.88f), 1.00f),
                (0.40f, new Color(1.00f, 0.78f, 0.35f), 0.80f),
                (1.00f, new Color(1.00f, 0.45f, 0.12f), 0.00f)
            });
            var flashSize = flash.sizeOverLifetime;
            flashSize.enabled = true;
            flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.9f));
            flash.Play();

            // Fireball: slower and heavier than the general-purpose explosion,
            // and it climbs — the start of a mushroom rather than a puff.
            var fire = NewSystem(root, "Fireball", loop: false);
            var bm = fire.main;
            bm.duration = 0.6f;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.8f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
            bm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bm.gravityModifier = -0.28f;
            bm.maxParticles = 70;
            fire.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 26),
                new ParticleSystem.Burst(0.12f, 12)
            });
            var bs = fire.shape;
            bs.shapeType = ParticleSystemShapeType.Sphere;
            bs.radius = 0.3f;
            SetColourRamp(fire, new[]
            {
                (0.00f, new Color(1.00f, 0.95f, 0.70f), 1.00f),
                (0.28f, tint,                            0.95f),
                (0.68f, new Color(0.45f, 0.15f, 0.05f), 0.60f),
                (1.00f, new Color(0.12f, 0.09f, 0.08f), 0.00f)
            });
            var drag = fire.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.45f;
            var fireSize = fire.sizeOverLifetime;
            fireSize.enabled = true;
            fireSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            fire.Play();

            // Shock ring: hugging the ground and moving fast. This is what makes
            // the blast sit *on* the terrain instead of hanging above it.
            var ring = NewSystem(root, "ShockRing", loop: false);
            ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var rm = ring.main;
            rm.duration = 0.3f;
            rm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            rm.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 8.0f);
            rm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            rm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            rm.maxParticles = 44;
            ring.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });
            var rs = ring.shape;
            rs.shapeType = ParticleSystemShapeType.Circle;
            rs.radius = 0.2f;
            rs.radiusThickness = 0f;          // emit from the rim: a ring, not a disc
            SetColourRamp(ring, new[]
            {
                (0.00f, new Color(0.86f, 0.80f, 0.70f), 0.85f),
                (0.55f, new Color(0.62f, 0.56f, 0.48f), 0.45f),
                (1.00f, new Color(0.50f, 0.46f, 0.42f), 0.00f)
            });
            var ringSize = ring.sizeOverLifetime;
            ringSize.enabled = true;
            ringSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.2f));
            var ringDrag = ring.limitVelocityOverLifetime;
            ringDrag.enabled = true;
            ringDrag.dampen = 0.55f;
            ring.Play();

            // Debris: heavy, gravity-bound, arcing up and falling back. Small
            // and sparse on purpose — it is punctuation, not the main event.
            var debris = NewSystem(root, "Debris", loop: false);
            var dm = debris.main;
            dm.duration = 0.3f;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(5.0f, 9.0f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            dm.gravityModifier = 1.3f;
            dm.maxParticles = 40;
            debris.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            var ds = debris.shape;
            ds.shapeType = ParticleSystemShapeType.Cone;
            ds.angle = 42f;
            ds.radius = 0.15f;
            SetColourRamp(debris, new[]
            {
                (0.00f, new Color(0.55f, 0.44f, 0.32f), 0.95f),
                (0.75f, new Color(0.40f, 0.33f, 0.26f), 0.75f),
                (1.00f, new Color(0.32f, 0.28f, 0.24f), 0.00f)
            });
            debris.Play();

            BuildDust(root, new Color(0.60f, 0.54f, 0.46f));
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
