using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Map;
using IronMeridian.Models;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One bundle from the ramp to the ground: it falls clear of the aircraft,
    /// the canopy opens, it descends under it, drifting down-track, and lands.
    ///
    /// **The canopy is the whole point of modelling this at all.** A weapon is
    /// not modelled in flight — <see cref="BomberRun"/> says so plainly, because
    /// a bomb at map zoom is invisible and what reads is the aircraft and the
    /// burst. A parachute is the opposite case: it is deliberately large, it is
    /// slow, it is white against the ground, and watching the load come down
    /// *is* the event. Half a minute of canopies drifting onto the zone is what
    /// makes a supply mission feel different from everything else that arrives
    /// from the air in this game.
    ///
    /// Three phases, and each is doing a job:
    ///
    ///  • **Free fall** — a beat with the bundle small and tumbling, so the
    ///    canopy has something to open *from*. Without it the chute appears
    ///    fully open at the ramp, which reads as a balloon being released.
    ///  • **Deployment** — the canopy scales up over a few tenths of a second.
    ///  • **Descent** — a constant rate, with the drift the release imparted,
    ///    and the model's own pendulum clip swinging the load underneath.
    ///
    /// The ground is sampled once, at release, rather than every frame: terrain
    /// under a 400 m zone does not change while a crate is falling through it,
    /// and a raycast per bundle per frame would be a raycast per frame for the
    /// whole drop.
    ///
    /// See docs/29-AIR-SUPPLY.md.
    /// </summary>
    public class ParachuteDrop : MonoBehaviour
    {
        /// <summary>Called as the bundle touches down: latitude, longitude.</summary>
        public System.Action<double, double> Landed;

        /// <summary>Seconds of free fall before the canopy opens.</summary>
        const float FreeFallSeconds = 0.7f;
        /// <summary>Seconds the canopy takes to open.</summary>
        const float DeploySeconds = 0.45f;
        /// <summary>Metres per second while still falling free.</summary>
        const float FreeFallSpeed = 120f;
        /// <summary>Seconds the landed bundle stays on the ground before the icon takes over.</summary>
        const float SettleSeconds = 1.6f;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        Transform _model;

        double _lat, _lon;
        double _startLat, _startLon, _endLat, _endLon;
        double _groundHeight;
        float _height;              // metres above the ground, now
        float _releaseHeight;
        float _elapsed;
        float _descentSeconds;
        bool _landed;
        float _settle;

        /// <summary>
        /// Releases a bundle. <paramref name="lat"/>/<paramref name="lon"/> is
        /// where it leaves the aircraft; <paramref name="targetLat"/>/
        /// <paramref name="targetLon"/> where the canopy is expected to put it.
        /// Returns null — having logged why — if the model is unavailable, so
        /// the caller can still deliver the load.
        /// </summary>
        public static ParachuteDrop Release(CesiumGeoreference geo, SupplyDropDef def,
            double lat, double lon, double targetLat, double targetLon, float releaseHeight)
        {
            var go = new GameObject("SupplyBundle");
            go.transform.SetParent(geo.transform, false);

            var model = UnitModelLibrary.CreateInstance(AirSupplyCatalog.BundleModelId, go.transform);
            if (model == null)
            {
                Debug.LogWarning("[ParachuteDrop] No usable model for the supply bundle — " +
                    "the load will still be delivered, but with nothing to watch.");
                Destroy(go);
                return null;
            }

            var drop = go.AddComponent<ParachuteDrop>();
            drop._geo = geo;
            drop._anchor = go.AddComponent<CesiumGlobeAnchor>();
            drop._model = model.transform;

            drop._startLat = lat; drop._startLon = lon;
            drop._endLat = targetLat; drop._endLon = targetLon;
            drop._lat = lat; drop._lon = lon;
            drop._releaseHeight = releaseHeight;
            drop._height = releaseHeight;
            drop._descentSeconds = releaseHeight / Mathf.Max(1f, AirSupplyCatalog.DescentMetersPerSecond);

            drop._groundHeight = GeoUtils.SampleTerrainHeight(geo, targetLat, targetLon, 250.0);
            drop.ScaleModel(model, def);
            drop.Place();

            return drop;
        }

        /// <summary>
        /// Sizes the bundle from its own bounds and tints its canopy to the
        /// load. The tint is what tells three simultaneous drops apart in the
        /// air — the crates underneath are identical and far too small to read.
        /// </summary>
        void ScaleModel(GameObject model, SupplyDropDef def)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float height = Mathf.Max(0.001f, bounds.size.y);
            model.transform.localScale = Vector3.one * (AirSupplyCatalog.BundleHeightMeters / height);

            foreach (var r in renderers)
            {
                if (!r.name.StartsWith("Canopy") && !r.name.StartsWith("Skirt")) continue;
                // Instance material: the shared one is cached per colour by
                // ProceduralModels and tinting it would repaint every canopy in
                // the game, including the ones already in the air.
                var mat = r.material;
                mat.color = Color.Lerp(mat.color, def.markerColor, r.name.StartsWith("Skirt") ? 0.75f : 0.45f);
            }

            // The canopy is folded until it opens.
            _model.localScale = Vector3.one * 0.001f;
            _canopyScale = model.transform.localScale;
        }

        Vector3 _canopyScale;

        void Update()
        {
            // Unscaled, like the run that dropped it: a mission that is away
            // must finish even with the battle paused.
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;

            if (_landed)
            {
                _settle += dt;
                if (_settle >= SettleSeconds) Destroy(gameObject);
                return;
            }

            if (_elapsed < FreeFallSeconds)
            {
                // Falling clear of the aircraft, canopy still packed.
                _height = Mathf.Max(0f, _height - FreeFallSpeed * dt);
                _model.localScale = _canopyScale * 0.28f;
            }
            else
            {
                float sinceOpen = _elapsed - FreeFallSeconds;

                // The canopy blooms, and the descent settles to its steady rate.
                float open = Mathf.Clamp01(sinceOpen / DeploySeconds);
                _model.localScale = _canopyScale * Mathf.Lerp(0.28f, 1f, Mathf.SmoothStep(0f, 1f, open));

                float rate = Mathf.Lerp(FreeFallSpeed, AirSupplyCatalog.DescentMetersPerSecond, open);
                _height = Mathf.Max(0f, _height - rate * dt);

                // Drift down-track: the load carries the aircraft's motion into
                // the descent, which is why a stick of canopies lands in a line
                // rather than in a heap under the release point.
                float u = _releaseHeight <= 0f ? 1f : 1f - _height / _releaseHeight;
                _lat = _startLat + (_endLat - _startLat) * u;
                _lon = _startLon + (_endLon - _startLon) * u;
            }

            Place();

            if (_height > 0.5f) return;

            _landed = true;
            _model.localScale = _canopyScale;
            // The canopy collapses onto the load rather than standing up on the
            // ground — an inflated chute sitting in a field is the clearest tell
            // that nobody thought about what happens after touchdown.
            _model.localRotation = Quaternion.Euler(78f, UnityEngine.Random.Range(0f, 360f), 0f);
            Landed?.Invoke(_lat, _lon);
        }

        void Place()
        {
            _anchor.longitudeLatitudeHeight = new double3(_lon, _lat, _groundHeight + _height);
        }
    }
}
