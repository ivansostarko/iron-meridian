using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Spins the rotors and propellers of a flying model.
    ///
    /// None of the aircraft in this project are rigged — they are static meshes,
    /// so there is no skeleton to drive and no clip to play. What makes a
    /// helicopter read as a helicopter rather than a sculpture is the rotor, and
    /// both the helicopter and the quadcopter happen to ship their rotors as
    /// **separate named meshes** inside the FBX. That is enough: find those
    /// child transforms by name and turn them.
    ///
    /// Matching is by name substring rather than by path, because a pack's
    /// hierarchy changes between LOD levels and versions while the part names do
    /// not — and a spinner that quietly finds nothing is better than one that
    /// throws when a model is re-exported.
    /// </summary>
    public class RotorSpinner : MonoBehaviour
    {
        /// <summary>One set of spinning parts: which meshes, about what, how fast.</summary>
        public class Spec
        {
            /// <summary>Case-insensitive substring of the mesh name, e.g. "Screw_Main".</summary>
            public string nameContains;
            /// <summary>Spin axis in the part's own local space.</summary>
            public Vector3 axis = Vector3.up;
            /// <summary>Revolutions per minute. Real rotors are a blur; these are readable.</summary>
            public float rpm = 400f;
        }

        readonly List<(Transform part, Vector3 axis, float degPerSec)> _parts =
            new List<(Transform, Vector3, float)>();

        /// <summary>
        /// Attaches a spinner to <paramref name="model"/> for the given specs.
        /// Returns null — having logged nothing — when the model has none of the
        /// named parts, so a jet with no rotors costs no component at all.
        /// </summary>
        public static RotorSpinner Attach(GameObject model, IReadOnlyList<Spec> specs)
        {
            if (model == null || specs == null || specs.Count == 0) return null;

            var found = new List<(Transform, Vector3, float)>();

            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                foreach (var spec in specs)
                {
                    if (string.IsNullOrEmpty(spec.nameContains)) continue;
                    if (t.name.IndexOf(spec.nameContains, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    found.Add((t, spec.axis.normalized, spec.rpm * 6f));   // rpm → deg/s
                    break;   // one spec per part; the first match wins
                }
            }

            if (found.Count == 0) return null;

            var spinner = model.AddComponent<RotorSpinner>();
            spinner._parts.AddRange(found);
            return spinner;
        }

        void Update()
        {
            // Unscaled, to match the strike systems that spawn these: an
            // aircraft mid-run must keep flying with the battle paused, and a
            // helicopter hanging in the air with a dead rotor looks broken.
            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < _parts.Count; i++)
            {
                var (part, axis, degPerSec) = _parts[i];
                if (part == null) continue;
                part.Rotate(axis, degPerSec * dt, Space.Self);
            }
        }
    }
}
