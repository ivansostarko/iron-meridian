using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// A live effect on the map, and the handle callers hold onto for looping
    /// ones (a burning unit, a smoke screen) so they can stop it later.
    ///
    /// Also owns this effect's screen-size culling: the strategic camera can sit
    /// tens of kilometres up, where a 200 m fire is a fraction of a pixel. Rather
    /// than simulate particles nobody can see, emission is switched off while the
    /// effect is sub-pixel and switched back on when the player zooms in.
    /// </summary>
    public class VfxInstance : MonoBehaviour
    {
        public VfxDef Def { get; private set; }
        public float SpawnedAt { get; private set; }

        /// <summary>True until <see cref="Stop"/> is called — a stopped instance is finishing its fade.</summary>
        public bool IsPlaying { get; private set; }

        ParticleSystem[] _systems;
        bool _culled;
        float _nextCullCheck;

        internal void Init(VfxDef def)
        {
            Def = def;
            SpawnedAt = Time.time;
            IsPlaying = true;
            _systems = GetComponentsInChildren<ParticleSystem>(true);

            // One-shots clean themselves up; looping effects live until stopped.
            if (!def.Loops) Destroy(gameObject, def.lifeSeconds);
        }

        /// <summary>
        /// Stops emission and lets particles already in flight finish, unless
        /// <paramref name="immediate"/> — used when the effect's owner is being
        /// destroyed and leaving a detached puff behind would look wrong.
        /// </summary>
        public void Stop(bool immediate = false)
        {
            if (!IsPlaying) return;
            IsPlaying = false;

            if (immediate || _systems == null)
            {
                Destroy(gameObject);
                return;
            }

            float residual = 0f;
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                residual = Mathf.Max(residual, ps.main.startLifetime.constantMax);
            }
            Destroy(gameObject, residual + 0.25f);
        }

        void Update()
        {
            // Only looping effects are worth culling — one-shots are gone before
            // a check would pay for itself.
            if (!IsPlaying || Def == null || !Def.Loops) return;
            if (Time.time < _nextCullCheck) return;
            _nextCullCheck = Time.time + 0.25f;

            var cam = Camera.main;
            if (cam == null) return;

            float dist = Mathf.Max(1f, Vector3.Distance(cam.transform.position, transform.position));
            // Fraction of the screen's height this effect covers.
            float apparent = (Def.scaleMeters / dist) /
                             (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

            bool shouldCull = apparent < GameConfig.VfxMinApparentSize;
            if (shouldCull == _culled) return;
            _culled = shouldCull;

            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.enabled = !shouldCull;
                if (shouldCull) ps.Clear(false);
            }
        }
    }
}
