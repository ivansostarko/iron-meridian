using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Data;

namespace IronMeridian.Map
{
    /// <summary>
    /// Strategy camera.
    ///   3D mode — orbit/pan/zoom over terrain (WASD pan, Q/E rotate, R/F or
    ///             wheel zoom, middle-mouse drag rotates).
    ///   2D mode — locked top-down, north-up map view.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        /// <summary>Return true to freeze the camera entirely (e.g. the pause menu is open).</summary>
        public System.Func<bool> InputBlocked;

        /// <summary>
        /// Optional limit on where the camera may look: given a requested focus
        /// point, return the one it is allowed to have. Used to keep a mission
        /// inside the ground it was authored on — see
        /// <see cref="Data.MissionArea"/>.
        ///
        /// A hook rather than the area itself, because the rig works in Unity
        /// world space and the boundary is geodetic. Keeping the conversion at
        /// the caller leaves the camera free of any opinion about the globe.
        /// </summary>
        public System.Func<Vector3, Vector3> ClampFocus;

        float _distance = 14000f;
        float _yaw = 0f;
        float _pitch3D = 55f;
        Vector3 _focus;           // point on the ground the camera looks at
        ViewMode _mode = ViewMode.Mode3D;

        const float MinDistance = 300f;
        const float MaxDistance = 120000f;

        /// <summary>
        /// Ceiling on the standoff. Normally <see cref="MaxDistance"/>; a
        /// mission that bounds its ground lowers it, because being able to zoom
        /// out to a continent when the battle is a valley is the same problem as
        /// being able to pan to one.
        /// </summary>
        float _maxDistance = MaxDistance;

        /// <summary>Lowers (or restores) the zoom-out ceiling, metres.</summary>
        public void SetMaxDistance(float metres)
        {
            _maxDistance = Mathf.Clamp(metres, MinDistance * 2f, MaxDistance);
            _distance = Mathf.Min(_distance, _maxDistance);
            Apply();
        }

        public void Init(Vector3 focus, float startDistance)
        {
            _focus = focus;
            _distance = startDistance;

            var go = new GameObject("StrategyCamera");
            Cam = go.AddComponent<Camera>();
            Cam.farClipPlane = 1_000_000f;
            Cam.nearClipPlane = 5f;
            go.AddComponent<AudioListener>();
            Apply();
        }

        public void SetMode(ViewMode mode)
        {
            _mode = mode;
            if (mode == ViewMode.Mode2D) _yaw = 0f;   // north-up
            Apply();
        }

        void Update()
        {
            if (Cam == null) return;
            if (InputBlocked != null && InputBlocked()) return;
            float dt = Time.unscaledDeltaTime;
            float panSpeed = _distance * 0.9f * dt;

            Vector3 fwd = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;

            bool panning = false;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) { _focus += fwd * panSpeed; panning = true; }
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) { _focus -= fwd * panSpeed; panning = true; }
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) { _focus += right * panSpeed; panning = true; }
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) { _focus -= right * panSpeed; panning = true; }

            // A flight in progress yields to the player the moment they touch
            // the camera. An animation that has to be waited out is a camera
            // that has stopped answering.
            if (panning) CancelFlight();
            else TickFlight(dt);

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (!overUI)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f)
                {
                    _distance = Mathf.Clamp(_distance * (1f - scroll * 1.6f), MinDistance, _maxDistance);
                    CancelFlight();
                }
            }
            if (Input.GetKey(KeyCode.R)) { _distance = Mathf.Clamp(_distance * (1f - dt), MinDistance, _maxDistance); CancelFlight(); }
            if (Input.GetKey(KeyCode.F)) { _distance = Mathf.Clamp(_distance * (1f + dt), MinDistance, _maxDistance); CancelFlight(); }

            if (_mode == ViewMode.Mode3D)
            {
                if (Input.GetKey(KeyCode.Q)) _yaw -= 60f * dt;
                if (Input.GetKey(KeyCode.E)) _yaw += 60f * dt;
                if (!overUI && Input.GetMouseButton(2))   // middle mouse orbit
                {
                    _yaw += Input.GetAxis("Mouse X") * 3f;
                    _pitch3D = Mathf.Clamp(_pitch3D - Input.GetAxis("Mouse Y") * 2f, 20f, 85f);
                }
            }
            Apply();
        }

        void Apply()
        {
            // Clamped here rather than at each of the half-dozen places that
            // move the focus — panning, jumping, loading a map, flying to a
            // mission. One choke point is what makes the bound impossible to
            // slip past by adding another way to move.
            if (ClampFocus != null) _focus = ClampFocus(_focus);

            float pitch = _mode == ViewMode.Mode2D ? 89.9f : _pitch3D;
            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0);
            Cam.transform.position = _focus - rot * Vector3.forward * _distance;
            Cam.transform.rotation = rot;
        }

        public void JumpTo(Vector3 focus) { CancelFlight(); _focus = focus; Apply(); }

        // ------------------------------------------------------------- fly-to

        bool _flying;
        Vector3 _flyFrom, _flyTo;
        float _flyFromDistance, _flyToDistance;
        float _flyElapsed, _flyDuration;

        /// <summary>True while the camera is travelling to a point on its own.</summary>
        public bool Flying => _flying;

        /// <summary>
        /// Travels to a point rather than jumping to it.
        ///
        /// The distinction matters: a cut leaves the player to work out where on
        /// the globe they have landed, whereas watching the ground slide past
        /// carries the relationship between where they were and where they now
        /// are. That is the whole value of "fly to this formation" over
        /// "show me this formation" — the answer to *where is it* is in the
        /// travel, not in the destination.
        ///
        /// Timed on **unscaled** time, like the rest of the rig: the editor
        /// spends most of its life with the clock stopped, and a camera that
        /// only moves while a battle is running would not move at all there.
        /// Any pan or zoom input cancels it — see <see cref="Update"/>.
        /// </summary>
        /// <param name="focus">Ground point to end up looking at, Unity world space.</param>
        /// <param name="distance">Standoff to finish at; null keeps the current one.</param>
        /// <param name="seconds">Travel time. Zero or less jumps.</param>
        public void FlyTo(Vector3 focus, float? distance = null, float seconds = 0.75f)
        {
            float target = Mathf.Clamp(distance ?? _distance, MinDistance, _maxDistance);

            if (seconds <= 0f)
            {
                _focus = focus;
                _distance = target;
                CancelFlight();
                Apply();
                return;
            }

            _flyFrom = _focus;
            _flyTo = focus;
            _flyFromDistance = _distance;
            _flyToDistance = target;
            _flyElapsed = 0f;
            _flyDuration = seconds;
            _flying = true;
        }

        void TickFlight(float dt)
        {
            if (!_flying) return;

            _flyElapsed += dt;
            float u = Mathf.Clamp01(_flyElapsed / _flyDuration);
            // Smoothstep: the camera leaves and arrives at rest, which is what
            // separates a move that was made for you from one that snapped.
            float eased = u * u * (3f - 2f * u);

            _focus = Vector3.Lerp(_flyFrom, _flyTo, eased);

            // Distance is interpolated in log space so the zoom reads as an
            // even glide at every scale — the same argument ZoomBy makes for
            // being multiplicative rather than additive.
            _distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(_flyFromDistance), Mathf.Log(_flyToDistance), eased));

            if (u >= 1f) _flying = false;
        }

        void CancelFlight() => _flying = false;

        /// <summary>
        /// The ground point the camera is looking at, in Unity world space.
        /// Exposed so a caller can ask "where am I?" in geodetic terms via
        /// <see cref="GeoUtils.UnityToGeo"/> — which is what the map editor's
        /// MISSIONS panel uses to start a new mission here rather than at a
        /// hard-coded default.
        /// </summary>
        public Vector3 Focus => _focus;

        /// <summary>How far the camera is standing off that point, metres.</summary>
        public float Distance => _distance;

        /// <summary>
        /// Sets the standoff, clamped to the rig's own limits. A mission opens
        /// at its authored altitude, and asking for one outside the limits should
        /// give the nearest legal view rather than nothing.
        /// </summary>
        public void SetDistance(float metres)
        {
            CancelFlight();
            _distance = Mathf.Clamp(metres, MinDistance, _maxDistance);
            Apply();
        }

        // ------------------------------------------------- on-map map controls

        /// <summary>Compass heading the view is facing, degrees clockwise from north.</summary>
        public float Yaw => ((_yaw % 360f) + 360f) % 360f;

        /// <summary>Camera distance as 0 (closest) .. 1 (furthest), for a zoom readout.</summary>
        public float Zoom01 =>
            Mathf.InverseLerp(Mathf.Log(MinDistance), Mathf.Log(MaxDistance), Mathf.Log(_distance));

        /// <summary>Height above the focus point in metres — what the scale readout shows.</summary>
        public float DistanceMeters => _distance;

        /// <summary>
        /// Steps the zoom by a multiplicative factor. Multiplicative rather than
        /// additive so a click moves the same *proportion* at every altitude —
        /// a fixed step is imperceptible at 100 km and jarring at 500 m.
        /// </summary>
        public void ZoomBy(float factor)
        {
            CancelFlight();
            _distance = Mathf.Clamp(_distance * factor, MinDistance, _maxDistance);
            Apply();
        }

        public void ZoomIn() => ZoomBy(0.7f);
        public void ZoomOut() => ZoomBy(1f / 0.7f);

        /// <summary>Snaps the view back to north-up, keeping the current position and tilt.</summary>
        public void ResetNorth()
        {
            _yaw = 0f;
            Apply();
        }

        /// <summary>Restores the default tilt (3D) without changing heading or position.</summary>
        public void ResetTilt()
        {
            _pitch3D = 55f;
            Apply();
        }
    }
}
