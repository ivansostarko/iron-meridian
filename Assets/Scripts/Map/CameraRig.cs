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

        float _distance = 14000f;
        float _yaw = 0f;
        float _pitch3D = 55f;
        Vector3 _focus;           // point on the ground the camera looks at
        ViewMode _mode = ViewMode.Mode3D;

        const float MinDistance = 300f;
        const float MaxDistance = 120000f;

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

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) _focus += fwd * panSpeed;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) _focus -= fwd * panSpeed;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) _focus += right * panSpeed;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) _focus -= right * panSpeed;

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (!overUI)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f)
                    _distance = Mathf.Clamp(_distance * (1f - scroll * 1.6f), MinDistance, MaxDistance);
            }
            if (Input.GetKey(KeyCode.R)) _distance = Mathf.Clamp(_distance * (1f - dt), MinDistance, MaxDistance);
            if (Input.GetKey(KeyCode.F)) _distance = Mathf.Clamp(_distance * (1f + dt), MinDistance, MaxDistance);

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
            float pitch = _mode == ViewMode.Mode2D ? 89.9f : _pitch3D;
            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0);
            Cam.transform.position = _focus - rot * Vector3.forward * _distance;
            Cam.transform.rotation = rot;
        }

        public void JumpTo(Vector3 focus) { _focus = focus; Apply(); }
    }
}
