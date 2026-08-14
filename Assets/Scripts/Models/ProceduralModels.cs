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
    public static class ProceduralModels
    {
        /// <summary>Model ids this class can build. Matched against <see cref="UnitModelDef.proceduralId"/>.</summary>
        public const string KamikazeDrone = "kamikaze_drone";

        /// <summary>
        /// Builds a model, or returns null if the id is not one of ours.
        /// The caller owns the returned object.
        /// </summary>
        public static GameObject Build(string proceduralId) => proceduralId switch
        {
            KamikazeDrone => BuildKamikazeDrone(),
            _ => null
        };

        // ------------------------------------------------------------- palette

        static readonly Color Body = new Color(0.30f, 0.33f, 0.29f);
        static readonly Color Panel = new Color(0.22f, 0.24f, 0.21f);
        static readonly Color Warhead = new Color(0.42f, 0.20f, 0.16f);
        static readonly Color Blade = new Color(0.12f, 0.13f, 0.13f);

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
            renderer.sharedMaterial = RuntimeMaterials.UnlitColor(colour);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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
