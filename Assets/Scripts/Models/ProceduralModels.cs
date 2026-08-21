using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.Models
{
    /// <summary>
    /// Models built from primitives in code, for the airframes the project owns
    /// outright rather than borrows from an asset pack.
    ///
    /// **Why build a model instead of importing one.** Everything else in
    /// <see cref="UnitModelLibrary"/> comes from a store pack, is turned into a
    /// prefab by the ModelInstaller editor tool, and stops existing
    /// the moment that pack is removed — which is exactly what happened to the
    /// kamikaze drone. A procedural model has no pack to lose: it is a few dozen
    /// lines of geometry, it ships with the source, and it cannot be missing.
    /// That is the same argument <see cref="Vfx.ProceduralVfx"/> and
    /// <see cref="Audio.ProceduralAudio"/> already make for effects and sound.
    ///
    /// **Why it is legitimate rather than a placeholder.** At map scale a
    /// loitering munition is forty pixels across. What has to read is the
    /// silhouette — a delta body, a warhead nose, a pusher propeller — and a
    /// silhouette is precisely what primitives are good at. Detail that cannot
    /// be seen would cost import size and buy nothing.
    ///
    /// Animation is a <see cref="AnimationClip"/> created at runtime and driven
    /// by legacy <see cref="Animation"/>. Clips *can* be built at runtime;
    /// Animator Controllers cannot (they are editor-only assets), which is why
    /// the whole project is on the legacy path — see docs/09-3D-MODELS.md.
    /// </summary>
    public static partial class ProceduralModels
    {
        /// <summary>Model ids this class can build. Matched against <see cref="UnitModelDef.proceduralId"/>.</summary>
        public const string KamikazeDrone = "kamikaze_drone";
        public const string ReconDrone = "recon_drone";
        public const string TransportAircraft = "airlift_transport";
        public const string SupplyBundle = "supply_bundle";

        /// <summary>
        /// Builds a model, or returns null if the id is not one of ours.
        /// The caller owns the returned object.
        /// </summary>
        public static GameObject Build(string proceduralId) => proceduralId switch
        {
            KamikazeDrone => BuildKamikazeDrone(),
            ReconDrone => BuildReconDrone(),
            TransportAircraft => BuildTransportAircraft(),
            SupplyBundle => BuildSupplyBundle(),
            // The six logistic installations live in their own file — they are
            // buildings rather than airframes and share none of the geometry
            // above. See ProceduralModels.Logistics.cs.
            _ => BuildLogisticsSite(proceduralId)
        };

        // ------------------------------------------------------------- palette

        static readonly Color Body = new Color(0.30f, 0.33f, 0.29f);
        static readonly Color Panel = new Color(0.22f, 0.24f, 0.21f);
        static readonly Color Warhead = new Color(0.42f, 0.20f, 0.16f);
        static readonly Color Blade = new Color(0.12f, 0.13f, 0.13f);
        /// <summary>Pale airframe of a surveillance drone — the class is not painted to hide.</summary>
        static readonly Color Survey = new Color(0.62f, 0.65f, 0.63f);
        /// <summary>The sensor turret's housing.</summary>
        static readonly Color Turret = new Color(0.16f, 0.17f, 0.18f);
        /// <summary>Its window. The one bright thing on the model, and the end that is looking.</summary>
        static readonly Color Lens = new Color(0.42f, 0.78f, 1.00f);
        /// <summary>Transport grey — a lighter, flatter paint than a combat airframe's.</summary>
        static readonly Color Transport = new Color(0.52f, 0.55f, 0.57f);
        /// <summary>Canopy silk. Deliberately near-white: a chute has to be findable in the sky.</summary>
        static readonly Color Canopy = new Color(0.90f, 0.91f, 0.88f);
        /// <summary>The band round the canopy's skirt, so the open mouth reads from above.</summary>
        static readonly Color CanopyBand = new Color(0.66f, 0.68f, 0.64f);
        static readonly Color Rigging = new Color(0.35f, 0.36f, 0.34f);
        /// <summary>Cargo crate — olive, the one warm thing in the drop.</summary>
        static readonly Color Crate = new Color(0.38f, 0.40f, 0.26f);

        // ------------------------------------------------------- kamikaze drone

        /// <summary>
        /// A delta-wing loitering munition, nose along **+Z** — the convention
        /// <see cref="Vfx.DroneRun"/> assumes, so no yaw correction is needed.
        ///
        /// Authored roughly 2.4 m nose to tail and 2.5 m across, which is about
        /// life size for the class. Callers scale it from its own bounds
        /// (`DroneRun.BuildModel`), so the absolute figures only matter in that
        /// they keep the proportions honest.
        /// </summary>
        static GameObject BuildKamikazeDrone()
        {
            var root = new GameObject("KamikazeDrone_Procedural");

            // Everything hangs off a Sway child rather than off the root. The
            // root's rotation belongs to whoever is flying the thing — DroneRun
            // sets the nose offset on it — so the idle clip animates this
            // instead, and the two never write the same transform.
            var swayGo = new GameObject("Sway");
            swayGo.transform.SetParent(root.transform, false);
            var sway = swayGo;

            // Fuselage: a stretched box rather than a capsule. The flat sides are
            // what make it read as an airframe from directly above, which is the
            // angle the map is usually looked at from.
            Box(sway, "Fuselage", new Vector3(0f, 0f, 0.05f),
                new Vector3(0.26f, 0.22f, 1.70f), Body);

            // Warhead nose: a cone, and the only warm colour on the model, so the
            // business end is identifiable at a glance in the dive.
            Cone(sway, "Warhead", new Vector3(0f, 0f, 1.05f), 0.13f, 0.55f, Warhead);

            // Delta wing, swept back. Two thin boxes rotated in plan rather than a
            // real swept mesh: at map scale the sweep is the only part that reads.
            Wing(sway, "WingLeft", -1f);
            Wing(sway, "WingRight", 1f);

            // Twin tail fins, canted outward — the shape that distinguishes this
            // class from a quadcopter in a single glance.
            Fin(sway, "FinLeft", -1f);
            Fin(sway, "FinRight", 1f);

            // Pusher propeller at the tail. The hub is what turns; the blades are
            // deliberately not called "Prop" anything, because RotorSpinner
            // matches by name *substring* and would otherwise spin the hub and
            // each blade independently, tearing the propeller apart.
            var hub = new GameObject("Propeller");
            hub.transform.SetParent(sway.transform, false);
            hub.transform.localPosition = new Vector3(0f, 0f, -0.92f);

            Box(hub, "BladeA", Vector3.zero, new Vector3(1.05f, 0.04f, 0.05f), Blade);
            Box(hub, "BladeB", Vector3.zero, new Vector3(0.05f, 0.04f, 1.05f), Blade);

            AttachAnimation(root, swayGo.transform);
            return root;
        }

        // --------------------------------------------------------- recon drone

        /// <summary>
        /// A twin-boom surveillance UAV, nose along **+Z**, built to the same
        /// convention as the loitering munition above.
        ///
        /// The silhouette is doing the work, and it is deliberately the opposite
        /// of the kamikaze drone's in every respect that reads at map scale: a
        /// **long straight high wing** instead of a swept delta, **twin tail
        /// booms** instead of a stubby fuselage, a **pale** finish instead of an
        /// olive one, and a **sensor turret under the nose** where the other has
        /// a warhead. A player glancing at the map has to be able to tell in one
        /// look whether the thing overhead is looking at them or coming for
        /// them, and colour alone will not carry that at forty pixels.
        ///
        /// Authored roughly 5 m nose to tail and 7 m across — about life size for
        /// a tactical ISR airframe. Callers scale it from its own bounds, so the
        /// absolute figures matter only in keeping the proportions honest.
        /// </summary>
        static GameObject BuildReconDrone()
        {
            var root = new GameObject("ReconDrone_Procedural");

            // Same arrangement as the munition: the flight code owns the root's
            // rotation, so everything the idle clip animates hangs off Sway.
            var swayGo = new GameObject("Sway");
            swayGo.transform.SetParent(root.transform, false);
            var sway = swayGo;

            // Slim fuselage pod.
            Box(sway, "Fuselage", new Vector3(0f, 0f, 0.10f),
                new Vector3(0.30f, 0.30f, 2.10f), Survey);

            // Rounded nose.
            var nose = Box(sway, "Nose", new Vector3(0f, 0.02f, 1.22f),
                new Vector3(0.24f, 0.24f, 0.36f), Survey);
            nose.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            // Sensor turret slung under the nose. Its own child so the clip can
            // turn it independently of the airframe — a gimbal that scans is the
            // single detail that says this drone is working rather than transiting.
            var turret = new GameObject("Sensor");
            turret.transform.SetParent(sway.transform, false);
            turret.transform.localPosition = new Vector3(0f, -0.26f, 0.98f);

            Box(turret, "TurretBody", Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), Turret);
            Box(turret, "TurretLens", new Vector3(0f, -0.02f, 0.18f),
                new Vector3(0.18f, 0.18f, 0.06f), Lens);

            // High straight wing, one piece across the top of the fuselage. A
            // long unswept span is the classic ISR planform and is what
            // distinguishes it from the delta at a glance.
            Box(sway, "Wing", new Vector3(0f, 0.18f, 0.05f),
                new Vector3(6.60f, 0.07f, 0.62f), Survey);

            // Twin tail booms running aft from the wing, with the tailplane
            // spanning them.
            Boom(sway, "BoomLeft", -1f);
            Boom(sway, "BoomRight", 1f);
            Box(sway, "Tailplane", new Vector3(0f, 0.14f, -1.62f),
                new Vector3(1.90f, 0.05f, 0.40f), Panel);
            TailFin(sway, "TailFinLeft", -1f);
            TailFin(sway, "TailFinRight", 1f);

            // Pusher propeller between the booms. As in the munition, the blades
            // avoid the word "Prop" so RotorSpinner cannot grab them individually
            // and tear the propeller apart — the hub is what turns.
            var hub = new GameObject("Propeller");
            hub.transform.SetParent(sway.transform, false);
            hub.transform.localPosition = new Vector3(0f, 0.02f, -1.02f);

            Box(hub, "BladeA", Vector3.zero, new Vector3(1.30f, 0.05f, 0.06f), Blade);
            Box(hub, "BladeB", Vector3.zero, new Vector3(0.06f, 0.05f, 1.30f), Blade);

            AttachReconAnimation(root);
            return root;
        }

        // --------------------------------------------------- transport aircraft

        /// <summary>
        /// The airlifter that flies a supply drop: a high-wing, four-turboprop,
        /// T-tailed transport, nose along **+Z**.
        ///
        /// **Why this silhouette.** Everything that has flown over this map so
        /// far has been something arriving to kill: a flying wing, a strike
        /// fighter, a gunship, a one-way drone. A supply drop has to read as the
        /// opposite from the first frame, and at map zoom the only thing the
        /// player can actually see is the outline — a fat slab fuselage, a
        /// straight high wing, four visibly turning propellers and an upswept
        /// tail with the ramp under it. Nobody mistakes that for an attack.
        ///
        /// Authored about 30 m long and 40 m across, which is roughly a C-130.
        /// <see cref="Vfx.SupplyRun"/> scales it from its own bounds, so the
        /// absolute figures matter only in keeping the proportions honest.
        /// </summary>
        static GameObject BuildTransportAircraft()
        {
            var root = new GameObject("TransportAircraft_Procedural");

            // Everything that moves hangs off this child, for the reason set out
            // on AttachAnimation: the run writes the root's own rotation.
            var sway = new GameObject("Sway");
            sway.transform.SetParent(root.transform, false);

            // Fuselage: a long box with a rounded nose cone and an upswept tail.
            Box(sway, "Fuselage", new Vector3(0f, 0f, 0f), new Vector3(3.4f, 3.6f, 22f), Transport);
            var nose = Cone(sway, "Nose", new Vector3(0f, 0f, 11f), 1.75f, 4.5f, Transport);
            nose.transform.localRotation = Quaternion.identity;      // cone already runs +Z

            // The upswept rear and the ramp under it — the one detail that says
            // "this one opens at the back".
            var tailCone = Box(sway, "TailCone", new Vector3(0f, 1.1f, -13f),
                new Vector3(3.0f, 2.6f, 6f), Transport);
            tailCone.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Box(sway, "Ramp", new Vector3(0f, -1.2f, -12.4f), new Vector3(2.6f, 0.35f, 4.6f), Panel);

            // High wing, straight, sitting on the spine.
            Box(sway, "Wing", new Vector3(0f, 2.0f, 1.2f), new Vector3(40f, 0.7f, 5.2f), Transport);

            // Four engine nacelles with propellers. Named so RotorSpinner cannot
            // grab the blades individually — the hub is what turns.
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1f : 1f;
                float out1 = (i % 2 == 0) ? 6.5f : 12.5f;
                float x = side * out1;

                Box(sway, $"Nacelle{i}", new Vector3(x, 1.8f, 2.2f),
                    new Vector3(2.0f, 1.8f, 6.5f), Panel);

                var hub = new GameObject($"Propeller{i}");
                hub.transform.SetParent(sway.transform, false);
                hub.transform.localPosition = new Vector3(x, 1.8f, 5.6f);
                Box(hub, "BladeA", Vector3.zero, new Vector3(7.0f, 0.22f, 0.14f), Blade);
                Box(hub, "BladeB", Vector3.zero, new Vector3(0.14f, 7.0f, 0.14f), Blade);
            }

            // T-tail: fin up the back, tailplane across its top.
            Box(sway, "Fin", new Vector3(0f, 4.4f, -13.4f), new Vector3(0.5f, 6.0f, 5.0f), Transport);
            Box(sway, "Tailplane", new Vector3(0f, 7.2f, -13.8f), new Vector3(14f, 0.5f, 3.4f), Transport);

            // Undercarriage blisters down the fuselage sides — cheap, and they
            // stop the belly reading as a flat plank.
            Box(sway, "SponsonLeft", new Vector3(-1.9f, -1.2f, 0f), new Vector3(1.0f, 1.4f, 8f), Panel);
            Box(sway, "SponsonRight", new Vector3(1.9f, -1.2f, 0f), new Vector3(1.0f, 1.4f, 8f), Panel);

            AttachTransportAnimation(root);
            return root;
        }

        /// <summary>
        /// The transport's idle clip: four propellers turning, and a very slight
        /// roll.
        ///
        /// Slower and shallower than the drones'. A loaded airlifter on a drop
        /// run is holding the steadiest line it can — that *is* the flying — so
        /// the sway is barely there, and its job is only to stop the model
        /// reading as a rigid prop sliding along a rail.
        /// </summary>
        static void AttachTransportAnimation(GameObject root)
        {
            var clip = new AnimationClip
            {
                name = ModelClips.CombatIdle,
                legacy = true,
                wrapMode = WrapMode.Loop
            };

            // A turboprop turns far slower than a model aircraft's pusher, and
            // at 45° steps a 0.5 s revolution still reads as a disc.
            for (int i = 0; i < 4; i++)
                SpinCurves(clip, $"Sway/Propeller{i}", Vector3.forward, 0.5f, steps: 8);

            SwayCurves(clip, "Sway", rollPeriod: 5.2f, rollDegrees: 1.6f,
                pitchPeriod: 6.7f, pitchDegrees: 0.6f);

            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, ModelClips.CombatIdle);
            animation.clip = clip;
            animation.wrapMode = WrapMode.Loop;
            animation.playAutomatically = true;
            animation.Play(ModelClips.CombatIdle);
        }

        // ------------------------------------------------------ supply bundle

        /// <summary>
        /// One air-dropped load under canopy: a palletised crate, four rigging
        /// lines, and an open parachute above it. Built **+Y up**, hanging from
        /// its own origin at the canopy's apex, because that is the point a
        /// falling bundle swings about.
        ///
        /// **A cone, not a dome.** A real canopy is a hemisphere, and at the
        /// zoom this map is played at a hemisphere and a cone are the same
        /// twelve pixels — but the cone's silhouette has a *point*, which is
        /// what makes it read as a parachute rather than as a ball. The same
        /// argument the loitering munition's delta body makes.
        ///
        /// The rocking is a clip rather than per-frame code so it plays wherever
        /// the model is shown, including standing still on a preview turntable.
        /// See <see cref="Vfx.ParachuteDrop"/> for the descent itself.
        /// </summary>
        static GameObject BuildSupplyBundle()
        {
            var root = new GameObject("SupplyBundle_Procedural");

            // The swinging part. The drop writes the root's rotation to face the
            // canopy into the drift, so the pendulum lives one level down.
            var swing = new GameObject("Swing");
            swing.transform.SetParent(root.transform, false);

            // Canopy: a cone with its apex up. Cone() builds along +Z from the
            // base, so it is stood up and pushed down by its own length.
            var canopy = Cone(swing, "Canopy", new Vector3(0f, 0f, 0f), 2.6f, 1.9f, Canopy);
            canopy.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // A skirt band in the darker shade, so the open mouth of the chute
            // is visible against the canopy from directly above — which is the
            // angle this game is usually looked at from.
            var skirt = Cone(swing, "Skirt", new Vector3(0f, 0.12f, 0f), 2.75f, 0.45f, CanopyBand);
            skirt.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Rigging: four lines from the skirt to the load.
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(a) * 1.25f, z = Mathf.Sin(a) * 1.25f;
                var line = Box(swing, $"Rigging{i}", new Vector3(x, -1.5f, z),
                    new Vector3(0.09f, 3.0f, 0.09f), Rigging);
                // Splayed out to the skirt, so the lines form a cone rather than
                // a cage.
                line.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 16f, 0f, -Mathf.Cos(a) * 16f);
            }

            // The load: a crate on a pallet.
            Box(swing, "Crate", new Vector3(0f, -3.4f, 0f), new Vector3(1.9f, 1.5f, 1.9f), Crate);
            Box(swing, "Pallet", new Vector3(0f, -4.25f, 0f), new Vector3(2.2f, 0.25f, 2.2f), Panel);
            // Banding across the crate, the universal read for "cargo".
            Box(swing, "StrapX", new Vector3(0f, -3.4f, 0f), new Vector3(2.0f, 0.22f, 0.22f), Panel);
            Box(swing, "StrapZ", new Vector3(0f, -3.4f, 0f), new Vector3(0.22f, 0.22f, 2.0f), Panel);

            AttachBundleAnimation(root);
            return root;
        }

        /// <summary>
        /// The bundle's pendulum. A load under canopy swings — that is the whole
        /// visual signature of a parachute, and a crate descending in a dead
        /// straight line reads as a lift rather than a drop. Two out-of-phase
        /// periods, so it never settles into a metronome.
        /// </summary>
        static void AttachBundleAnimation(GameObject root)
        {
            var clip = new AnimationClip
            {
                name = ModelClips.CombatIdle,
                legacy = true,
                wrapMode = WrapMode.Loop
            };

            SwayCurves(clip, "Swing", rollPeriod: 3.1f, rollDegrees: 9f,
                pitchPeriod: 4.3f, pitchDegrees: 6f);

            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, ModelClips.CombatIdle);
            animation.clip = clip;
            animation.wrapMode = WrapMode.Loop;
            animation.playAutomatically = true;
            animation.Play(ModelClips.CombatIdle);
        }

        static void Boom(GameObject parent, string name, float side)
        {
            Box(parent, name, new Vector3(side * 0.86f, 0.16f, -0.86f),
                new Vector3(0.11f, 0.11f, 2.00f), Panel);
        }

        static void TailFin(GameObject parent, string name, float side)
        {
            Box(parent, name, new Vector3(side * 0.86f, 0.40f, -1.66f),
                new Vector3(0.06f, 0.52f, 0.36f), Panel);
        }

        static void Wing(GameObject parent, string name, float side)
        {
            var wing = Box(parent, name, new Vector3(side * 0.62f, 0f, -0.16f),
                new Vector3(1.15f, 0.05f, 0.44f), Panel);
            // Swept back about 22°, and given a couple of degrees of dihedral so
            // the two wings do not read as one flat plate from the side.
            wing.transform.localRotation = Quaternion.Euler(0f, side * 22f, side * -4f);
        }

        static void Fin(GameObject parent, string name, float side)
        {
            var fin = Box(parent, name, new Vector3(side * 0.30f, 0.16f, -0.70f),
                new Vector3(0.05f, 0.40f, 0.34f), Panel);
            fin.transform.localRotation = Quaternion.Euler(0f, 0f, side * 18f);
        }

        // --------------------------------------------------------- primitives

        static GameObject Box(GameObject parent, string name, Vector3 position, Vector3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Paint(go, colour);
            return go;
        }

        /// <summary>
        /// A cone, built as a fan of triangles. Unity has no cone primitive and a
        /// squashed sphere reads as a bulb rather than as a warhead.
        /// </summary>
        static GameObject Cone(GameObject parent, string name, Vector3 position,
            float radius, float length, Color colour)
        {
            const int Sides = 12;

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;

            var verts = new List<Vector3>(Sides + 2) { new Vector3(0f, 0f, length) };
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            verts.Add(Vector3.zero);   // base centre

            var tris = new List<int>(Sides * 6);
            for (int i = 0; i < Sides; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % Sides;
                tris.Add(0); tris.Add(a); tris.Add(b);              // side
                tris.Add(verts.Count - 1); tris.Add(b); tris.Add(a); // base
            }

            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            Paint(go, colour);
            return go;
        }

        static void Paint(GameObject go, Color colour)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = MaterialFor(colour);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// The flat colours these models are painted with, one shared
        /// <see cref="Material"/> each.
        ///
        /// **Why a cache and not a material per part.** Every model here is a
        /// few dozen primitives and <c>RuntimeMaterials.UnlitColor</c> is a bare
        /// <c>new Material</c>, so painting them individually cost one material
        /// per box — and a material assigned to <c>sharedMaterial</c> is *not*
        /// reclaimed when its renderer's object is destroyed. That was survivable
        /// while the only procedural models were airframes that live for the
        /// length of one strike. A logistic installation is per-site and its
        /// models are switched on and off from the editor
        /// (<see cref="Logistics.LogisticsSite.SetModelVisible"/>), so a dozen
        /// sites toggled a few times orphaned materials by the thousand.
        ///
        /// **Sharing is safe here** because nothing repaints a model in place.
        /// The one caller that tints a part — <see cref="Vfx.ParachuteDrop"/>,
        /// colouring a canopy by its load — goes through <c>Renderer.material</c>,
        /// which takes an instance copy precisely so the shared one is left
        /// alone.
        ///
        /// Guarded with the Unity-null idiom rather than a plain null check: a
        /// material can be destroyed out from under a static cache, and the
        /// right answer then is to build another.
        /// </summary>
        static readonly Dictionary<Color, Material> _palette = new Dictionary<Color, Material>();

        static Material MaterialFor(Color colour)
        {
            if (_palette.TryGetValue(colour, out var cached) && cached != null) return cached;

            var mat = RuntimeMaterials.UnlitColor(colour);
            mat.name = "ProceduralModel_" + ColorUtility.ToHtmlStringRGB(colour);
            _palette[colour] = mat;
            return mat;
        }

        // --------------------------------------------------------- animation

        /// <summary>
        /// Legacy <see cref="Animation"/> with a clip built here rather than
        /// imported.
        ///
        /// Two motions, and both earn their place. The **propeller** turns,
        /// because a munition with a stopped prop reads as wreckage falling
        /// rather than as something flying itself onto the target. The airframe
        /// **rocks** a few degrees on two out-of-phase cycles, because a rigid
        /// object translating in a dead straight line is the clearest tell that
        /// a thing on screen is a prop and not a vehicle.
        ///
        /// It is a clip rather than per-frame code so it plays identically
        /// wherever the model is shown — in flight, and standing still on the
        /// units screen's preview turntable.
        ///
        /// **Rotation is animated as a quaternion, not as Euler angles.**
        /// <c>localEulerAngles</c> is not a real serialised property — it is a
        /// computed convenience on Transform — so a curve bound to it is not
        /// reliably applied by legacy playback. <c>localRotation.x/y/z/w</c> is
        /// the actual backing field, and legacy <see cref="Animation"/>
        /// normalises quaternion curves as it applies them, so linear
        /// interpolation between keys is safe as long as the keys are close
        /// enough together — hence 45° steps on the propeller.
        ///
        /// The sway is bound to the <c>Sway</c> child rather than to the model
        /// root, because <see cref="Vfx.DroneRun"/> sets the root's own rotation
        /// to apply the type's nose offset. Two things writing one transform is
        /// a fight whose winner depends on script execution order.
        /// </summary>
        static void AttachAnimation(GameObject root, Transform sway)
        {
            var clip = new AnimationClip
            {
                name = ModelClips.CombatIdle,
                legacy = true,
                wrapMode = WrapMode.Loop
            };

            // Propeller: a full turn every 0.14 s, in 45° steps.
            SpinCurves(clip, "Sway/Propeller", Vector3.forward, 0.14f, steps: 8);

            // Airframe sway: a slow roll with a shallower pitch on a different
            // period, so the two never line up into a metronome.
            SwayCurves(clip, "Sway", rollPeriod: 2.6f, rollDegrees: 4.5f,
                pitchPeriod: 3.7f, pitchDegrees: 1.4f);

            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, ModelClips.CombatIdle);
            animation.clip = clip;
            animation.wrapMode = WrapMode.Loop;
            animation.playAutomatically = true;
            animation.Play(ModelClips.CombatIdle);

            // Referenced so the parameter is not merely decorative: the caller
            // owns the child and this asserts the path the curves are bound to.
            if (sway == null || sway.name != "Sway")
                Debug.LogError("[ProceduralModels] Sway child missing — the idle clip will not bind.");
        }

        /// <summary>
        /// The reconnaissance drone's idle clip. Three motions, and the third is
        /// the one that matters.
        ///
        /// The **propeller** turns, slower than the munition's — this airframe
        /// loiters rather than sprints. The airframe **rocks**, gently: a
        /// surveying drone holding a steady orbit is not being thrown about.
        /// And the **sensor turret sweeps**, one revolution every eight seconds,
        /// which is the whole point of the model. A drone with a fixed gimbal
        /// looks like a drone; a drone with a turret quartering the ground below
        /// it looks like a drone doing a job, and that is the difference between
        /// this and the loitering munition it sits beside in the menu.
        ///
        /// Bound as quaternion curves for the reasons set out on
        /// <see cref="AttachAnimation"/> — <c>localEulerAngles</c> is not a real
        /// serialised property and legacy playback will not reliably apply it.
        /// </summary>
        static void AttachReconAnimation(GameObject root)
        {
            var clip = new AnimationClip
            {
                name = ModelClips.CombatIdle,
                legacy = true,
                wrapMode = WrapMode.Loop
            };

            // Propeller: a full turn every 0.22 s, in 45° steps.
            SpinCurves(clip, "Sway/Propeller", Vector3.forward, 0.22f, steps: 8);

            // Sensor turret: a slow quartering sweep about its own vertical axis.
            // Twelve steps rather than eight — at this speed the interpolation
            // between keys is visible, and a turret that ticks reads as broken.
            SpinCurves(clip, "Sway/Sensor", Vector3.up, 8.0f, steps: 12);

            // Airframe: shallower than the munition's, on longer periods.
            SwayCurves(clip, "Sway", rollPeriod: 3.4f, rollDegrees: 2.6f,
                pitchPeriod: 4.9f, pitchDegrees: 1.0f);

            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, ModelClips.CombatIdle);
            animation.clip = clip;
            animation.wrapMode = WrapMode.Loop;
            animation.playAutomatically = true;
            animation.Play(ModelClips.CombatIdle);
        }

        /// <summary>A continuous spin about <paramref name="axis"/>, as quaternion curves.</summary>
        static void SpinCurves(AnimationClip clip, string path, Vector3 axis, float period, int steps)
        {
            var x = new AnimationCurve();
            var y = new AnimationCurve();
            var z = new AnimationCurve();
            var w = new AnimationCurve();

            for (int i = 0; i <= steps; i++)
            {
                float t = period * i / steps;
                var q = Quaternion.AngleAxis(360f * i / steps, axis);
                x.AddKey(t, q.x); y.AddKey(t, q.y); z.AddKey(t, q.z); w.AddKey(t, q.w);
            }

            BindRotation(clip, path, x, y, z, w);
        }

        /// <summary>A roll and pitch oscillation, as quaternion curves.</summary>
        static void SwayCurves(AnimationClip clip, string path,
            float rollPeriod, float rollDegrees, float pitchPeriod, float pitchDegrees)
        {
            // Sampled over the least common cycle of the two periods so the clip
            // loops seamlessly; 24 samples is smooth at these amplitudes.
            const int Samples = 24;
            float period = rollPeriod * pitchPeriod;      // both complete whole cycles in this
            period = Mathf.Min(period, 12f);              // …but keep the clip short

            var x = new AnimationCurve();
            var y = new AnimationCurve();
            var z = new AnimationCurve();
            var w = new AnimationCurve();

            for (int i = 0; i <= Samples; i++)
            {
                float t = period * i / Samples;
                float roll = Mathf.Sin(t / rollPeriod * Mathf.PI * 2f) * rollDegrees;
                float pitch = Mathf.Sin(t / pitchPeriod * Mathf.PI * 2f) * pitchDegrees;
                var q = Quaternion.Euler(pitch, 0f, roll);
                x.AddKey(t, q.x); y.AddKey(t, q.y); z.AddKey(t, q.z); w.AddKey(t, q.w);
            }

            BindRotation(clip, path, x, y, z, w);
        }

        static void BindRotation(AnimationClip clip, string path,
            AnimationCurve x, AnimationCurve y, AnimationCurve z, AnimationCurve w)
        {
            foreach (var curve in new[] { x, y, z, w }) Linear(curve);

            clip.SetCurve(path, typeof(Transform), "localRotation.x", x);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", y);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", z);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", w);
        }

        /// <summary>
        /// Linear tangents throughout. <c>AnimationUtility.SetKeyLeftTangentMode</c>
        /// is editor-only, so the tangents are computed from the neighbouring
        /// keys directly — which is all that call does anyway.
        /// </summary>
        static void Linear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve[i];

                if (i > 0)
                {
                    var prev = curve[i - 1];
                    float dt = key.time - prev.time;
                    if (dt > 1e-5f) key.inTangent = (key.value - prev.value) / dt;
                }
                if (i < curve.length - 1)
                {
                    var next = curve[i + 1];
                    float dt = next.time - key.time;
                    if (dt > 1e-5f) key.outTangent = (next.value - key.value) / dt;
                }

                curve.MoveKey(i, key);
            }
        }
    }
}
