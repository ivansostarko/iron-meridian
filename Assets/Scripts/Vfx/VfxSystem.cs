using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// The one way gameplay asks for fire, smoke, explosions and dust.
    ///
    /// Responsibilities:
    ///  - resolve a <see cref="VfxId"/> to an authored prefab, or to a
    ///    procedural stand-in when the prefab is missing or cannot render;
    ///  - anchor effects geodetically (lat/lon + sampled terrain height), so
    ///    they stay put on the globe like every other world object here;
    ///  - normalise scale, because effect packs are authored at human scale and
    ///    this map is measured in kilometres;
    ///  - keep a concurrent-effect budget so a division-sized engagement cannot
    ///    fill the scene with particle systems.
    ///
    /// Every entry point is a no-op when the system has not been initialised, so
    /// call sites never need a null check and non-game scenes cost nothing.
    /// </summary>
    public class VfxSystem : MonoBehaviour
    {
        public static VfxSystem Active { get; private set; }

        CesiumGeoreference _geo;
        Transform _root;
        readonly List<VfxInstance> _live = new List<VfxInstance>();
        readonly Dictionary<VfxId, GameObject> _prefabs = new Dictionary<VfxId, GameObject>();

        /// <summary>Effects currently on the map — exposed for the HUD/diagnostics.</summary>
        public int LiveCount => _live.Count;

        public void Init(CesiumGeoreference geo)
        {
            _geo = geo;
            Active = this;

            var rootGo = new GameObject("VFX");
            rootGo.transform.SetParent(geo.transform, false);
            _root = rootGo.transform;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        // ------------------------------------------------------- public API

        /// <summary>
        /// Plays an effect at a geodetic position, sitting on the terrain.
        /// Returns the handle (needed only for looping effects) or null if the
        /// effect was budgeted out or the system is not running.
        /// </summary>
        public static VfxInstance Play(VfxId id, double lat, double lon, float scaleMultiplier = 1f)
            => Active != null ? Active.SpawnAt(id, lat, lon, scaleMultiplier) : null;

        /// <summary>
        /// Plays an effect parented to a moving object — a burning unit carries
        /// its fire with it. The effect dies with its parent.
        /// </summary>
        public static VfxInstance Attach(VfxId id, Transform parent, float scaleMultiplier = 1f)
            => Active != null ? Active.SpawnOn(id, parent, scaleMultiplier) : null;

        /// <summary>
        /// The full "something just died here" composite: detonation, then a
        /// burning wreck that smoulders and smokes for a while before going out.
        /// <paramref name="severity01"/> scales it from a lost company to a lost
        /// division.
        /// </summary>
        public static void PlayWreck(double lat, double lon, float severity01)
        {
            if (Active == null) return;

            float s = Mathf.Clamp01(severity01);
            Active.SpawnAt(VfxId.Explosion, lat, lon, Mathf.Lerp(0.7f, 1.5f, s));

            var fire = Active.SpawnAt(VfxCatalog.FireForScale(s), lat, lon, 1f);
            var smoke = Active.SpawnAt(VfxId.SmokePlume, lat, lon, Mathf.Lerp(0.8f, 1.4f, s));

            // The wreck burns out rather than smouldering for the rest of the
            // battle — otherwise a long game ends up carpeted in permanent fires.
            float life = Mathf.Lerp(GameConfig.VfxWreckMinSeconds, GameConfig.VfxWreckMaxSeconds, s);
            if (fire != null) Active.StopAfter(fire, life);
            if (smoke != null) Active.StopAfter(smoke, life * 1.35f);
        }

        /// <summary>Stops a looping effect after a delay; safe if it is already gone.</summary>
        public void StopAfter(VfxInstance instance, float seconds)
        {
            if (instance == null) return;
            StartCoroutine(StopAfterRoutine(instance, seconds));
        }

        System.Collections.IEnumerator StopAfterRoutine(VfxInstance instance, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (instance != null) instance.Stop();
        }

        /// <summary>Stops every live effect — used when reloading a map.</summary>
        public void StopAll()
        {
            foreach (var i in new List<VfxInstance>(_live))
                if (i != null) i.Stop(true);
            _live.Clear();
        }

        // -------------------------------------------------------- spawning

        VfxInstance SpawnAt(VfxId id, double lat, double lon, float scaleMultiplier)
        {
            var def = VfxCatalog.Get(id);
            if (def == null || _geo == null) return null;
            if (!MakeRoom(def)) return null;

            var go = new GameObject($"VFX_{id}");
            go.transform.SetParent(_root, false);

            var anchor = go.AddComponent<CesiumGlobeAnchor>();
            double h = GeoUtils.SampleTerrainHeight(_geo, lat, lon, 250.0);
            // Lift slightly so the effect is not half-buried in the terrain mesh.
            anchor.longitudeLatitudeHeight = new double3(lon, lat, h + 6.0);

            return Populate(go, def, scaleMultiplier);
        }

        VfxInstance SpawnOn(VfxId id, Transform parent, float scaleMultiplier)
        {
            var def = VfxCatalog.Get(id);
            if (def == null || parent == null) return null;
            if (!MakeRoom(def)) return null;

            var go = new GameObject($"VFX_{id}");
            go.transform.SetParent(parent, false);
            return Populate(go, def, scaleMultiplier);
        }

        VfxInstance Populate(GameObject go, VfxDef def, float scaleMultiplier)
        {
            // Author-scale to map-scale. Both the prefab and procedural paths use
            // ParticleSystemScalingMode.Hierarchy, so this one transform scale
            // drives particle size and velocity together.
            go.transform.localScale = Vector3.one * (def.scaleMeters * Mathf.Max(0.01f, scaleMultiplier));

            var prefab = ResolvePrefab(def);
            if (prefab != null)
            {
                var visual = Instantiate(prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                NormaliseAuthoredPrefab(visual);
            }
            else
            {
                ProceduralVfx.Build(go, def);
            }

            var instance = go.AddComponent<VfxInstance>();
            instance.Init(def);
            _live.Add(instance);
            return instance;
        }

        /// <summary>
        /// Authored packs are built for their own demo scene: metre-scale, and
        /// often with local simulation space and their own scaling mode. Force
        /// the settings this project's map scale depends on.
        /// </summary>
        static void NormaliseAuthoredPrefab(GameObject visual)
        {
            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (!ps.isPlaying) ps.Play();
            }
        }

        // ------------------------------------------------- prefab resolution

        static bool _warnedPipeline;

        /// <summary>
        /// Loads the authored prefab for this effect, or returns null to fall
        /// back to procedural. Cached per id, including the null result.
        /// </summary>
        GameObject ResolvePrefab(VfxDef def)
        {
            if (_prefabs.TryGetValue(def.id, out var cached)) return cached;

            var prefab = LoadPrefab(def);
            _prefabs[def.id] = prefab;
            return prefab;
        }

        /// <summary>
        /// The authored prefab for an effect, or null when there is none the
        /// active render pipeline can draw.
        ///
        /// Static and public because the DEVELOPMENT → PARTICLES lab has
        /// to answer the same question with no <see cref="VfxSystem"/> in the
        /// scene — and a lab that decided prefab-versus-procedural by its own
        /// rules would be showing something other than what the game plays.
        /// </summary>
        public static GameObject LoadPrefab(VfxDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.prefabPath)) return null;

            var prefab = Resources.Load<GameObject>(def.prefabPath);
            if (prefab == null || Renderable(prefab)) return prefab;

            if (!_warnedPipeline)
            {
                _warnedPipeline = true;
                Debug.LogWarning(
                    "[VfxSystem] Authored VFX prefabs found but their shaders have no supported " +
                    "sub-shader for the active render pipeline — the Free Fire VFX pack is URP-only " +
                    "and this project runs the built-in pipeline. Falling back to procedural fire " +
                    "and smoke. See docs/08-PARTICLE-SYSTEMS.md.");
            }
            return null;
        }

        /// <summary>
        /// Forces the prefab and procedural settings an authored pack needs to
        /// obey at this project's map scale. Shared with the effects lab so the
        /// preview and the map treat a pack the same way.
        /// </summary>
        public static void Normalise(GameObject visual) => NormaliseAuthoredPrefab(visual);

        /// <summary>
        /// True when every material on the prefab has a sub-shader the current
        /// pipeline and hardware can actually run. Without this check a URP-only
        /// pack renders as magenta quads rather than degrading to something the
        /// player can read.
        /// </summary>
        static bool Renderable(GameObject prefab)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) return false;
                    if (!mat.shader.isSupported) return false;
                }
            return true;
        }

        // ---------------------------------------------------------- budget

        /// <summary>
        /// Enforces the concurrent-effect cap. Returns false only when the
        /// incoming effect is itself the least important thing on the map.
        /// </summary>
        bool MakeRoom(VfxDef incoming)
        {
            _live.RemoveAll(i => i == null);
            if (_live.Count < GameConfig.VfxMaxConcurrent) return true;

            // Evict the least important live effect: lowest priority first, and
            // among equals the one that has been running longest.
            VfxInstance victim = null;
            foreach (var i in _live)
            {
                if (i == null || i.Def == null) continue;
                if (victim == null ||
                    i.Def.priority < victim.Def.priority ||
                    (i.Def.priority == victim.Def.priority && i.SpawnedAt < victim.SpawnedAt))
                    victim = i;
            }

            if (victim == null) return true;
            if (victim.Def.priority > incoming.priority) return false;

            _live.Remove(victim);
            victim.Stop();
            return true;
        }
    }
}
