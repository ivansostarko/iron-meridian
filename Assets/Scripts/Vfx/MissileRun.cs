using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// One missile in flight: up off the horizon on a ballistic arc, over the
    /// top, and down onto the target.
    ///
    /// It is a third flight alongside <see cref="BomberRun"/> and
    /// <see cref="DroneRun"/> because it is a third manoeuvre. An aircraft flies
    /// *through* and leaves; a loitering munition flies *at* the target from the
    /// next ridge; a missile arrives from **above** having come from somewhere
    /// off the map entirely. That vertical arrival is the whole character of the
    /// thing, and it is what the trajectory here is for.
    ///
    /// **The airframe is built in code**, like the kamikaze drone — see
    /// <see cref="Models.ProceduralModels"/> for why the project would rather
    /// own a silhouette than borrow one. A missile at map scale is a bright
    /// sliver with a plume behind it; the plume is doing most of the work, and a
    /// detailed mesh would be invisible behind it.
    ///
    /// The motor roar travels with it and is cut on impact; the terminal
    /// whistle is fired at the ground point a moment before arrival, so the
    /// player hears it coming down before they see it land.
    ///
    /// See docs/20-MISSILE-SYSTEMS.md.
    /// </summary>
    public class MissileRun : MonoBehaviour
    {
        /// <summary>Called once the warhead reaches the target: latitude, longitude.</summary>
        public System.Action<double, double> Impact;

        /// <summary>Seconds before impact that the incoming whistle is played at the target.</summary>
        const float WhistleLeadSeconds = 1.6f;
        /// <summary>Fraction of the flight spent climbing, before the arc turns over.</summary>
        const float ClimbFraction = 0.42f;

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        MissileSystemDef _def;
        AudioSource _motor;
        VfxInstance _trail;
        Transform _body;

        double _startLat, _startLon, _targetLat, _targetLon;
        double _groundHeight;
        float _headingDeg;
        float _elapsed;
        bool _done, _whistled;

        public static MissileRun Launch(CesiumGeoreference geo, MissileSystemDef def,
            double targetLat, double targetLon, float headingDeg)
        {
            var go = new GameObject("MissileRun_" + def.label);
            go.transform.SetParent(geo.transform, false);

            var run = go.AddComponent<MissileRun>();
            run._geo = geo;
            run._def = def;
            run._headingDeg = headingDeg;
            run._targetLat = targetLat;
            run._targetLon = targetLon;
            run._anchor = go.AddComponent<CesiumGlobeAnchor>();

            run.PlanTrack();
            run.BuildBody();

            // Motor roar, parented so it travels with the missile.
            run._motor = EffectAudio.PlayAt(EffectSound.MissileMotor, go.transform.position,
                def.bodyMeters * 12f, go.transform);

            // Exhaust plume, attached to the airframe. Attached rather than
            // played at a point: a trail spawned at the launch site would sit
            // there while the missile left it behind.
            run._trail = VfxSystem.Attach(VfxId.MissileTrail, go.transform);

            return run;
        }

        void PlanTrack()
        {
            _groundHeight = GeoUtils.SampleTerrainHeight(_geo, _targetLat, _targetLon, 250.0);

            // Launched from over the horizon on the run-in bearing.
            GeoUtils.Destination(_targetLat, _targetLon, _headingDeg + 180f, _def.approachKm,
                out _startLat, out _startLon);

            Place(0f);
        }

        /// <summary>
        /// The airframe: a body, a nose cone and four tail fins, nose along +Z.
        /// Deliberately spare — see the class remarks. Everything is unlit and
        /// bright so it stays visible against dark terrain from altitude.
        /// </summary>
        void BuildBody()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            _body = body.transform;

            float length = _def.bodyMeters;
            float calibre = length * 0.085f;

            Part(body.transform, "Tube", PrimitiveType.Cylinder,
                new Vector3(0f, 0f, 0f),
                new Vector3(calibre, length * 0.42f, calibre),
                Quaternion.Euler(90f, 0f, 0f),
                new Color(0.86f, 0.87f, 0.90f));

            Part(body.transform, "Nose", PrimitiveType.Sphere,
                new Vector3(0f, 0f, length * 0.44f),
                new Vector3(calibre, calibre, calibre * 2.2f),
                Quaternion.identity,
                new Color(0.35f, 0.36f, 0.38f));

            for (int i = 0; i < 4; i++)
            {
                float roll = i * 90f;
                var fin = Part(body.transform, "Fin" + i, PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(calibre * 2.6f, calibre * 0.12f, length * 0.16f),
                    Quaternion.identity,
                    new Color(0.55f, 0.57f, 0.60f));
                // Positioned after the roll so all four sit on the tail rather
                // than fanning out from the origin.
                fin.localRotation = Quaternion.Euler(0f, 0f, roll);
                fin.localPosition = fin.localRotation * new Vector3(calibre * 1.3f, 0f, -length * 0.36f);
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

            // Unscaled to match the countdown that launched this: a mission must
            // land even with the battle paused, and the editor is paused nearly
            // all the time.
            _elapsed += Time.unscaledDeltaTime;
            Place(_elapsed);

            // The whistle plays at the *target*, not on the missile — it is the
            // sound of something arriving where you are looking, and riding it
            // down on the airframe would put it in the wrong place until the
            // last instant.
            if (!_whistled && _elapsed >= _def.flightSeconds - WhistleLeadSeconds)
            {
                _whistled = true;
                EffectAudio.PlayAt(EffectSound.MissileIncoming,
                    GeoUtils.GeoToUnity(_geo, _targetLat, _targetLon, _groundHeight),
                    _def.radiusMeters * 3f);
            }

            if (_elapsed < _def.flightSeconds) return;

            _done = true;

            // Motor and plume die before the warhead, not after.
            if (_motor != null) { EffectAudio.Stop(_motor); _motor = null; }
            if (_trail != null) { _trail.Stop(true); _trail = null; }

            Impact?.Invoke(_targetLat, _targetLon);
            Destroy(gameObject);
        }

        /// <summary>
        /// Puts the missile where it should be at <paramref name="t"/> seconds
        /// into the flight.
        ///
        /// Ground track closes at a constant rate; height follows a parabola
        /// through <see cref="MissileSystemDef.apogeeMeters"/>, skewed so the
        /// climb is quicker than the descent. That skew is what makes it read as
        /// a missile rather than as a thrown ball: it goes up under power and
        /// comes down under gravity, and those are not the same curve.
        /// </summary>
        void Place(float t)
        {
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, _def.flightSeconds));

            double lat = _startLat + (_targetLat - _startLat) * u;
            double lon = _startLon + (_targetLon - _startLon) * u;

            // Two parabolas meeting at apogee, so the ascent can be given less
            // of the flight than the descent.
            float height;
            if (u <= ClimbFraction)
            {
                float a = u / ClimbFraction;
                height = _def.apogeeMeters * (1f - (1f - a) * (1f - a));
            }
            else
            {
                float d = (u - ClimbFraction) / (1f - ClimbFraction);
                height = _def.apogeeMeters * (1f - d * d);
            }

            _anchor.longitudeLatitudeHeight = new double3(lon, lat, _groundHeight + height);

            // Nose along the flight path. Derived from the trajectory rather
            // than authored, so the missile always points where it is going —
            // steeply up off the launcher, level at the top, and near-vertical
            // on the way in, which is the shape the whole flight is about.
            float ground = _def.approachKm * 1000f;
            float dhdu = HeightSlope(u) * _def.apogeeMeters;
            float pitch = -Mathf.Atan2(dhdu, ground) * Mathf.Rad2Deg;

            transform.localRotation = Quaternion.Euler(pitch, _headingDeg, 0f);
            if (_body != null) _body.localRotation = Quaternion.identity;
        }

        /// <summary>Derivative of the height curve in <see cref="Place"/>, per unit of u.</summary>
        static float HeightSlope(float u)
        {
            if (u <= ClimbFraction)
            {
                float a = u / ClimbFraction;
                return 2f * (1f - a) / ClimbFraction;
            }
            float d = (u - ClimbFraction) / (1f - ClimbFraction);
            return -2f * d / (1f - ClimbFraction);
        }

        void OnDestroy()
        {
            if (_motor != null) EffectAudio.Stop(_motor);
            if (_trail != null) _trail.Stop(true);
        }
    }
}
