using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One surface-to-air missile: off the launcher, up onto the intercept, and
    /// gone in the burst that kills what it was fired at.
    ///
    /// It is a fourth flight alongside <see cref="BomberRun"/>,
    /// <see cref="DroneRun"/> and <see cref="MissileRun"/>, and it is the only
    /// one that **chases**. The other three fly to a point on the ground that
    /// was decided when they launched; this one is aimed at something that is
    /// still flying, and its whole character is that it closes on a moving
    /// track. So there is no precomputed trajectory: every frame it interpolates
    /// from where it started to where the target *is now*, which produces the
    /// lead and the final tightening for free and — this is the point — cannot
    /// miss. See <see cref="Units.AirDefenceSystem"/> for why it must not.
    ///
    /// **The airframe is built in code**, exactly as <see cref="MissileRun"/>
    /// builds its own: a SAM at map scale is a bright sliver with a plume behind
    /// it, and the plume is doing most of the work. There is no
    /// <see cref="Models.UnitModelLibrary"/> entry because there is no prefab
    /// and no pack — see docs/09-3D-MODELS.md on why the project would rather
    /// own a silhouette than borrow one.
    ///
    /// See docs/24-AIR-DEFENCE.md.
    /// </summary>
    public class InterceptorRun : MonoBehaviour
    {
        /// <summary>Called when the missile reaches the track, with the intercept point.</summary>
        public System.Action<double, double, double> Intercept;

        /// <summary>
        /// How high the missile arcs over the straight line to the target, as a
        /// fraction of the distance it has to cover. A SAM does not fly the
        /// chord — it climbs off the rail and comes down onto the track — and
        /// without this a shot at something almost overhead is a vertical line
        /// with nothing to watch.
        /// </summary>
        const float LoftFraction = 0.18f;
        /// <summary>Length of the airframe, metres. Real enough for the class, big enough to see.</summary>
        const float BodyMeters = 34f;
        /// <summary>Height above the launcher the missile leaves from, metres.</summary>
        const float RailHeightMeters = 12f;

        CesiumGlobeAnchor _anchor;
        AudioSource _motor;
        VfxInstance _trail;

        AirTarget _target;
        double _startLat, _startLon, _startAltitude;
        /// <summary>
        /// Terrain height under the launcher and under the track. Both drone
        /// altitude and missile altitude are quoted *above the ground beneath
        /// them*, and over real terrain those are two different datums —
        /// several hundred metres apart in a valley. Interpolating between them
        /// along the flight is what stops the missile passing under a drone that
        /// is holding station over a ridge.
        /// </summary>
        double _launcherGround, _targetGround;
        double _lat, _lon, _altitude;
        float _flightSeconds;
        float _elapsed;
        bool _done;

        /// <summary>
        /// Fires a missile at a track. Returns null if the target has already
        /// gone — a launcher must not spend a missile on empty sky.
        /// </summary>
        public static InterceptorRun Launch(CesiumGeoreference geo, AirTarget target,
            double launcherLat, double launcherLon, float flightSeconds)
        {
            if (geo == null || target == null || target.Destroyed) return null;

            var go = new GameObject("InterceptorRun");
            go.transform.SetParent(geo.transform, false);

            var run = go.AddComponent<InterceptorRun>();
            run._target = target;
            run._flightSeconds = Mathf.Max(0.2f, flightSeconds);
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run._launcherGround = GeoUtils.SampleTerrainHeight(geo, launcherLat, launcherLon, 250.0);
            // Sampled once, where the track is at launch: the drone moves a few
            // hundred metres during the flight, which is nothing next to a
            // terrain tile, and re-sampling would raycast the globe every frame.
            run._targetGround = GeoUtils.SampleTerrainHeight(geo,
                target.Latitude, target.Longitude, run._launcherGround);
            run._startLat = launcherLat;
            run._startLon = launcherLon;
            run._startAltitude = RailHeightMeters;
            run._lat = launcherLat;
            run._lon = launcherLon;
            run._altitude = RailHeightMeters;

            run.BuildBody();
            run.Place(0f);

            // Off the rail, at the launcher, on the ground: this is the cue that
            // tells the player which of their batteries answered.
            VfxSystem.Play(VfxId.InterceptorLaunch, launcherLat, launcherLon);

            run._motor = EffectAudio.PlayAt(EffectSound.MissileMotor, go.transform.position,
                BodyMeters * 30f, go.transform);
            run._trail = VfxSystem.Attach(VfxId.InterceptorTrail, go.transform);

            return run;
        }

        /// <summary>
        /// The airframe: a body, a nose and four tail fins, nose along +Z.
        /// Deliberately spare, and unlit and bright so it stays visible against
        /// dark terrain from altitude.
        /// </summary>
        void BuildBody()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);

            float calibre = BodyMeters * 0.075f;

            Part(body.transform, "Tube", PrimitiveType.Cylinder,
                Vector3.zero,
                new Vector3(calibre, BodyMeters * 0.42f, calibre),
                Quaternion.Euler(90f, 0f, 0f),
                new Color(0.90f, 0.91f, 0.93f));

            Part(body.transform, "Nose", PrimitiveType.Sphere,
                new Vector3(0f, 0f, BodyMeters * 0.44f),
                new Vector3(calibre, calibre, calibre * 2.4f),
                Quaternion.identity,
                new Color(0.32f, 0.34f, 0.36f));

            for (int i = 0; i < 4; i++)
            {
                var fin = Part(body.transform, "Fin" + i, PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(calibre * 2.4f, calibre * 0.12f, BodyMeters * 0.15f),
                    Quaternion.identity,
                    new Color(0.58f, 0.60f, 0.63f));
                fin.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                fin.localPosition = fin.localRotation * new Vector3(calibre * 1.25f, 0f, -BodyMeters * 0.34f);
            }
        }

        static Transform Part(Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale, Quaternion rotation, Color colour)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimeMaterials.UnlitColor(colour);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        void Update()
        {
            if (_done) return;

            // The track can end before the missile arrives — the sortie finished
            // its own flight, or the scene was reloaded. There is nothing left
            // to hit, so the missile goes away without a burst rather than
            // detonating on nothing.
            if (_target == null || _target.Destroyed)
            {
                _done = true;
                Destroy(gameObject);
                return;
            }

            // Unscaled, like every other flight: an engagement has to resolve
            // even with the battle paused, and the editor is paused nearly all
            // of the time.
            _elapsed += Time.unscaledDeltaTime;
            Place(_elapsed);

            if (_elapsed < _flightSeconds) return;

            _done = true;

            // Motor and plume die before the warhead, not after.
            if (_motor != null) { EffectAudio.Stop(_motor); _motor = null; }
            if (_trail != null) { _trail.Stop(true); _trail = null; }

            Intercept?.Invoke(_lat, _lon, _altitude);
            Destroy(gameObject);
        }

        /// <summary>
        /// Puts the missile where it should be at <paramref name="t"/> seconds
        /// into the flight.
        ///
        /// Interpolated toward the target's **current** position rather than
        /// along a course plotted at launch. That is what makes it a chase: as
        /// the drone flies on, the fraction already covered is re-measured
        /// against a destination that has moved, so the missile curves after it
        /// and arrives exactly as the fraction reaches one.
        /// </summary>
        void Place(float t)
        {
            float u = Mathf.Clamp01(t / _flightSeconds);

            double targetGround = _target.AltitudeMeters;
            _lat = _startLat + (_target.Latitude - _startLat) * u;
            _lon = _startLon + (_target.Longitude - _startLon) * u;

            // Height climbs to the target's, plus a loft that peaks halfway and
            // is gone by the intercept, so the shot arcs rather than ruling a
            // line between two points.
            double straight = _startAltitude + (targetGround - _startAltitude) * u;
            double km = GeoUtils.DistanceKm(_startLat, _startLon, _target.Latitude, _target.Longitude);
            double loft = km * 1000.0 * LoftFraction * Mathf.Sin(u * Mathf.PI);
            _altitude = straight + loft;

            double datum = _launcherGround + (_targetGround - _launcherGround) * u;

            var previous = transform.position;
            _anchor.longitudeLatitudeHeight = new double3(_lon, _lat, datum + _altitude);

            // Nose along the path actually travelled. Derived rather than
            // authored, so the missile always points where it is going — steeply
            // off the rail, flattening over the top, and down onto the track.
            var delta = transform.position - previous;
            if (delta.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        void OnDestroy()
        {
            if (_motor != null) EffectAudio.Stop(_motor);
            if (_trail != null) _trail.Stop(true);
        }
    }
}
