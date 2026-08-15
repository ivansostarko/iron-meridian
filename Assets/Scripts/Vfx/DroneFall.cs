using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// A drone coming down after being hit: the wreck falls, tumbling and
    /// trailing fire, and burns on the ground where it lands.
    ///
    /// **Why the fall is its own object.** The flight that was interrupted owns
    /// a trajectory, an engine note and a set of callbacks, all of which have
    /// just stopped being true — an aircraft that is falling is not flying a
    /// mission any more. Rather than teach every flight a second mode it would
    /// then have to guard every line of its own Update against, the flight
    /// hands its **model** over and destroys itself. What is left is this: no
    /// mission, no callbacks, no destination, just a wreck and gravity.
    ///
    /// **The physics is authored, not simulated.** A rigid body dropped from
    /// four hundred metres onto Cesium terrain would need a collider on tiles
    /// that stream in and out underneath it, and would land at whatever moment
    /// the physics happened to resolve. The fall here integrates a real
    /// acceleration against the ground height sampled at the impact point, so
    /// it accelerates the way a falling thing does, drifts downwind of where it
    /// was hit, tumbles about all three axes, and lands exactly on the terrain.
    ///
    /// See docs/24-AIR-DEFENCE.md.
    /// </summary>
    public class DroneFall : MonoBehaviour
    {
        /// <summary>Downward acceleration, m/s². Real gravity — the fall is the one part with nothing stylised about it.</summary>
        const float Gravity = 9.81f;
        /// <summary>Terminal descent rate, m/s. A light airframe with bits missing does not reach 200 km/h.</summary>
        const float TerminalSpeed = 62f;
        /// <summary>How far the wreck travels on along its old heading while it comes down, per second, metres.</summary>
        const float ForwardDriftMps = 22f;
        /// <summary>Give up if the ground is never found — a fall that never lands would leave a burning object in the sky.</summary>
        const float MaxFallSeconds = 20f;

        CesiumGlobeAnchor _anchor;
        Transform _model;
        VfxInstance _trail;

        double _lat, _lon, _altitude, _groundHeight;
        float _headingDeg;
        float _fallSpeed;
        float _elapsed;
        float _burstScale;
        Vector3 _tumble;
        bool _landed;

        /// <summary>
        /// Takes over a stricken flight. <paramref name="model"/> is reparented
        /// onto the wreck, so the caller may destroy itself immediately
        /// afterwards — which it should, for the reasons in the class remarks.
        /// </summary>
        public static DroneFall Begin(CesiumGeoreference geo, Transform model,
            double lat, double lon, double altitudeMeters, float headingDeg, float burstScale)
        {
            if (geo == null) return null;

            var go = new GameObject("DroneFall");
            go.transform.SetParent(geo.transform, false);

            var fall = go.AddComponent<DroneFall>();
            fall._lat = lat;
            fall._lon = lon;
            fall._altitude = Mathf.Max(0f, (float)altitudeMeters);
            fall._headingDeg = headingDeg;
            fall._burstScale = burstScale;
            fall._anchor = go.AddComponent<CesiumGlobeAnchor>();

            // Sampled once, at the point it was hit rather than where it will
            // land: the drift is small next to a terrain tile, and re-sampling
            // every frame would raycast the globe sixty times for a wreck.
            fall._groundHeight = GeoUtils.SampleTerrainHeight(geo, lat, lon, 250.0);

            if (model != null)
            {
                model.SetParent(go.transform, false);
                model.localPosition = Vector3.zero;
                fall._model = model;

                // Whatever idle animation the airframe was playing is a flying
                // animation. A wreck with its propeller turning and its sensor
                // turret still quartering the ground is the single detail that
                // would give the whole thing away.
                foreach (var animation in model.GetComponentsInChildren<Animation>())
                    animation.Stop();
            }

            // A tumble rate per axis, so no two wrecks come down the same way.
            //
            // Fully qualified: this file needs `Unity.Mathematics` for double3,
            // and that namespace has a Random of its own, so the bare name is
            // ambiguous. Every flight in this folder has the same collision.
            fall._tumble = new Vector3(
                UnityEngine.Random.Range(-160f, 160f),
                UnityEngine.Random.Range(-90f, 90f),
                UnityEngine.Random.Range(-220f, 220f));

            // Burning debris, attached so it follows the wreck down and reads as
            // a trail rather than as smoke hanging where the hit happened.
            fall._trail = VfxSystem.Attach(VfxId.DroneFallTrail, go.transform, burstScale);

            fall.Place();
            return fall;
        }

        void Update()
        {
            if (_landed) return;

            // Unscaled, like every other flight: the editor is paused nearly all
            // the time, and a wreck frozen in mid-air would be worse than no
            // wreck at all.
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;

            _fallSpeed = Mathf.Min(TerminalSpeed, _fallSpeed + Gravity * dt);
            _altitude -= _fallSpeed * dt;

            // Carries on along its old heading as it comes down.
            GeoUtils.Destination(_lat, _lon, _headingDeg, ForwardDriftMps * dt / 1000.0,
                out _lat, out _lon);

            if (_model != null) _model.Rotate(_tumble * dt, Space.Self);

            Place();

            if (_altitude > 0.0 && _elapsed < MaxFallSeconds) return;

            _landed = true;
            Impact();
        }

        void Place()
        {
            _anchor.longitudeLatitudeHeight =
                new double3(_lon, _lat, _groundHeight + System.Math.Max(0.0, _altitude));
        }

        /// <summary>
        /// What is left where it went in: a small burst, then a wreck fire and
        /// its smoke on the operational clock.
        ///
        /// Deliberately a **fraction** of what the drone's own warhead would
        /// have done. A loitering munition shot down short of its target has not
        /// delivered its attack — the point of the interception is that the
        /// thing it was going to hit is still standing — so this leaves a
        /// burning airframe, not a crater. It does no damage for the same
        /// reason.
        /// </summary>
        void Impact()
        {
            if (_trail != null) { _trail.Stop(true); _trail = null; }

            VfxSystem.Play(VfxId.UavWarheadBurst, _lat, _lon, _burstScale * 0.6f);
            StrikeAftermath.Play(_lat, _lon, _burstScale * 0.5f);

            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_trail != null) _trail.Stop(true);
        }
    }
}
