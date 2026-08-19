using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Map
{
    /// <summary>
    /// A camera path over the battle: a list of shots the player records from
    /// wherever the camera happens to be, and a PLAY that flies from one to the
    /// next.
    ///
    /// **Battle mode only, and deliberately so.** This is not an authoring tool
    /// — it lays nothing on the map and changes nothing about the scenario. It
    /// is a way of *watching* a fight that is already happening: a sweep along
    /// the front, a fall onto the objective, a pull-back over the depth. The map
    /// editor has its own reasons to move the camera and none of them want a
    /// second thing driving it.
    ///
    /// **Shots are poses, not places.** A waypoint records where the camera was
    /// looking, how far off it stood, and which way round and how far over it
    /// was tilted — so replaying it reproduces the shot rather than merely the
    /// position. Stored geodetically (lat/lon), like everything else that
    /// remembers a point on this map: Unity world coordinates are relative to a
    /// Cesium origin that moves.
    ///
    /// **The flight is <see cref="CameraRig"/>'s, not this class's.** A tour is
    /// a queue of ordinary fly-tos, which is what gives it the rig's easing, its
    /// unscaled clock, and — the part that matters — its rule that touching the
    /// camera cancels the flight. Grab the camera mid-tour and the tour stops,
    /// because the rig reports the interruption
    /// (<see cref="CameraRig.FlightEnded"/>) rather than the arrival.
    ///
    /// Runtime only: a tour is not part of the scenario and is not saved. See
    /// docs/03-GAMEPLAY.md § Cinema mode.
    /// </summary>
    public class CinemaSystem : MonoBehaviour
    {
        /// <summary>One recorded camera pose.</summary>
        public class Shot
        {
            public double latitude, longitude;
            /// <summary>Height of the focus point itself — the ground it was standing on.</summary>
            public double heightMeters;
            public float distanceMeters;
            public float yawDeg;
            public float pitchDeg;
        }

        /// <summary>Shortest and longest a leg may be told to take, seconds.</summary>
        public const float MinLegSeconds = 2f;
        public const float MaxLegSeconds = 30f;
        /// <summary>What the stepper moves by.</summary>
        public const float LegStepSeconds = 1f;

        readonly List<Shot> _shots = new List<Shot>();

        MapManager _map;
        CameraRig _rig;

        /// <summary>Raised whenever the list, the timing or the playing state changes.</summary>
        public System.Action Changed;
        /// <summary>Optional one-line report to the HUD.</summary>
        public System.Action<string> Flash;

        public IReadOnlyList<Shot> Shots => _shots;

        /// <summary>
        /// How long one leg takes. Six seconds rather than the rig's
        /// three-quarters of a second default: a fly-to is a way of getting
        /// somewhere and wants to be over, and a cinema leg is the thing being
        /// watched.
        /// </summary>
        public float LegSeconds { get; private set; } = 6f;

        public bool IsPlaying { get; private set; }

        /// <summary>Which shot the camera is flying towards, or -1 when stopped.</summary>
        public int CurrentShot { get; private set; } = -1;

        /// <summary>
        /// Set by the rig's arrival callback and read on the next Update.
        ///
        /// The next leg cannot be started from inside the callback: the rig
        /// raises it in the middle of its own flight bookkeeping, and starting a
        /// flight there would have the rig cancelling the flight it had just
        /// begun. One frame's latency buys a state machine that cannot trip over
        /// itself.
        /// </summary>
        bool _legArrived;

        public void Init(MapManager map, CameraRig rig)
        {
            _map = map;
            _rig = rig;
            if (_rig != null) _rig.FlightEnded += OnFlightEnded;
        }

        void OnDestroy()
        {
            if (_rig != null) _rig.FlightEnded -= OnFlightEnded;
        }

        // ------------------------------------------------------------- shots

        /// <summary>
        /// Records the camera exactly as it is now and appends it to the tour.
        /// Returns the new shot's index, or -1 if the map is not ready.
        /// </summary>
        public int Add()
        {
            if (_map == null || _rig == null || _map.Georeference == null) return -1;

            GeoUtils.UnityToGeo(_map.Georeference, _rig.Focus,
                out double lat, out double lon, out double height);

            _shots.Add(new Shot
            {
                latitude = lat,
                longitude = lon,
                // Kept, not re-sampled on playback: the focus is wherever the
                // player left it, which over a valley is not the same as the
                // terrain height under it — and re-sampling would also fail
                // outright for a shot over ground Cesium has since unloaded.
                heightMeters = height,
                distanceMeters = _rig.Distance,
                yawDeg = _rig.Yaw,
                pitchDeg = _rig.Pitch
            });

            Changed?.Invoke();
            return _shots.Count - 1;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _shots.Count) return;
            // A tour whose shots are moving under it is no longer the tour that
            // was started, so editing the list stops playback rather than
            // trying to renumber around it.
            Stop();
            _shots.RemoveAt(index);
            Changed?.Invoke();
        }

        public void Clear()
        {
            Stop();
            if (_shots.Count == 0) return;
            _shots.Clear();
            Changed?.Invoke();
        }

        /// <summary>Flies to one shot on its own, so the player can check it.</summary>
        public void Preview(int index)
        {
            if (_rig == null || index < 0 || index >= _shots.Count) return;
            Stop();
            FlyToShot(index, LegSeconds);
        }

        // ------------------------------------------------------------ timing

        public void StepLegSeconds(float delta)
        {
            float next = Mathf.Clamp(LegSeconds + delta, MinLegSeconds, MaxLegSeconds);
            if (Mathf.Approximately(next, LegSeconds)) return;
            LegSeconds = next;
            Changed?.Invoke();
        }

        // --------------------------------------------------------- playback

        /// <summary>
        /// Runs the tour from the top.
        ///
        /// Two shots is the floor, because one shot is a place rather than a
        /// path — there is nothing to fly along. The first leg starts from
        /// wherever the camera is now rather than cutting to shot 1, so pressing
        /// PLAY never begins with a jump.
        /// </summary>
        public void Play()
        {
            if (_rig == null) return;
            if (_shots.Count < 2)
            {
                Flash?.Invoke("Cinema needs at least two waypoints — add one from the current view.");
                return;
            }

            IsPlaying = true;
            _legArrived = false;
            CurrentShot = -1;
            NextLeg();
            Changed?.Invoke();
        }

        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            CurrentShot = -1;
            _legArrived = false;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!IsPlaying || !_legArrived) return;
            _legArrived = false;
            NextLeg();
        }

        /// <summary>
        /// Steps to the shot after the current one, or ends the tour if that was
        /// the last. It runs once through rather than looping: a tour that
        /// restarted by itself would be a camera the player had to take back
        /// rather than one that handed itself over.
        /// </summary>
        void NextLeg()
        {
            int next = CurrentShot + 1;
            if (next >= _shots.Count)
            {
                IsPlaying = false;
                CurrentShot = -1;
                Flash?.Invoke("Cinema finished.");
                Changed?.Invoke();
                return;
            }

            CurrentShot = next;
            FlyToShot(next, LegSeconds);
            Changed?.Invoke();
        }

        void FlyToShot(int index, float seconds)
        {
            var shot = _shots[index];
            var focus = GeoUtils.GeoToUnity(_map.Georeference,
                shot.latitude, shot.longitude, shot.heightMeters);

            // Starting a leg cancels the one before it, and the rig reports that
            // cancellation the same way it reports the player grabbing the
            // camera. Flagged so the tour does not read its own handover as an
            // interruption and stop itself on the second leg.
            _driving = true;
            try { _rig.FlyTo(focus, shot.distanceMeters, seconds, shot.yawDeg, shot.pitchDeg); }
            finally { _driving = false; }
        }

        /// <summary>True for the instant this class is itself starting a leg.</summary>
        bool _driving;

        /// <summary>
        /// The rig reporting how a leg ended. An arrival queues the next one; an
        /// interruption — a pan, a zoom, a fly-to from somewhere else — ends the
        /// tour, which is how the player takes the camera back without having to
        /// find the STOP button first.
        /// </summary>
        void OnFlightEnded(bool arrived)
        {
            if (_driving || !IsPlaying) return;

            if (arrived) { _legArrived = true; return; }

            IsPlaying = false;
            CurrentShot = -1;
            _legArrived = false;
            Flash?.Invoke("Cinema stopped — the camera was taken back.");
            Changed?.Invoke();
        }
    }
}
