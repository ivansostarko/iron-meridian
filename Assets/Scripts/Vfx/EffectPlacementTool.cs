using UnityEngine;
using UnityEngine.EventSystems;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Lets the player drop a fire, explosion or smoke column onto the terrain
    /// by hand — for staging a scenario, or for marking something during a
    /// battle.
    ///
    /// Arming a tool shows a reticle that tracks the real ground point under
    /// the cursor, so what you see is where the effect lands. The tool stays
    /// armed after a placement so a line of fires can be laid down in one go;
    /// right-click or Escape puts it away.
    ///
    /// Every placement is ground-checked. Cesium streams terrain in, so a click
    /// over tiles that have not arrived has no ground to sit on — placing there
    /// would bury the effect inside the globe or leave it hanging in the air.
    /// Those clicks are refused with a message rather than guessed at.
    /// </summary>
    public class EffectPlacementTool : MonoBehaviour
    {
        /// <summary>User-facing messages (wired to the HUD's flash line).</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the armed effect changes, so the panel can repaint.</summary>
        public event System.Action ArmedChanged;

        public bool IsArmed => _armed.HasValue;
        public VfxId? Armed => _armed;

        MapManager _map;
        Camera _cam;
        VfxId? _armed;

        GameObject _reticle;
        CesiumGlobeAnchor _reticleAnchor;
        Transform _reticleQuad;
        Material _reticleMat;
        float _pulse;

        bool _validGround;
        double _lat, _lon;

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
            BuildReticle();
        }

        /// <summary>Arms a tool, or disarms it if the same one is picked again.</summary>
        public void Toggle(VfxId id)
        {
            if (_armed.HasValue && _armed.Value == id) { Cancel(); return; }
            _armed = id;
            Flash?.Invoke($"Click the map to place {Label(id)}. Right-click or Esc to stop.");
            ArmedChanged?.Invoke();
        }

        public void Cancel()
        {
            if (!_armed.HasValue) return;
            _armed = null;
            if (_reticle != null) _reticle.SetActive(false);
            ArmedChanged?.Invoke();
        }

        static string Label(VfxId id) => id switch
        {
            VfxId.Explosion => "an explosion",
            VfxId.SmokePlume => "a smoke column",
            _ => "a fire"
        };

        void Update()
        {
            if (!_armed.HasValue) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            TrackGround();

            if (Input.GetMouseButtonDown(0)) Place();
        }

        /// <summary>Keeps the reticle on the ground point under the cursor.</summary>
        void TrackGround()
        {
            // Over a panel there is no map point to aim at, and raycasting
            // through the UI would put the reticle on whatever terrain happens
            // to be behind it.
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            // Assigned before use: short-circuiting `overUI` would otherwise
            // leave `hit` unassigned on the path below.
            Vector3 hit = default;
            _validGround = !overUI &&
                           _map.RaycastGround(_cam, Input.mousePosition, out hit);

            if (!_validGround)
            {
                if (_reticle != null) _reticle.SetActive(false);
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, hit, out _lat, out _lon, out _);

            double h = GeoUtils.SampleTerrainHeight(_map.Georeference, _lat, _lon, 250) + 3.0;
            _reticleAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(_lon, _lat, h);
            _reticle.SetActive(true);
        }

        void Place()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!_validGround)
            {
                Flash?.Invoke("Terrain not loaded here yet — try again in a moment.");
                return;
            }

            var id = _armed.Value;
            if (id == VfxId.Explosion)
            {
                // A hand-placed explosion leaves a burning wreck behind, the
                // same as a destroyed unit — a detonation with nothing after it
                // reads as a firework.
                VfxSystem.PlayWreck(_lat, _lon, 0.6f);
            }
            else
            {
                VfxSystem.Play(id, _lat, _lon);
            }

            Flash?.Invoke($"Placed {Label(id)}.");
        }

        // ------------------------------------------------------------ reticle

        void BuildReticle()
        {
            _reticle = new GameObject("EffectPlacementPreview");
            _reticle.transform.SetParent(_map.Georeference.transform, false);
            _reticleAnchor = _reticle.AddComponent<CesiumGlobeAnchor>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_reticle.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = Vector3.one * 300f;

            _reticleMat = RuntimeMaterials.UnlitTexture(
                ProceduralTextures.Reticle(new Color(1.00f, 0.55f, 0.15f)));
            quad.GetComponent<MeshRenderer>().material = _reticleMat;
            _reticleQuad = quad.transform;

            _reticle.SetActive(false);
        }

        void LateUpdate()
        {
            if (_reticle == null || !_reticle.activeSelf) return;

            // Slow spin plus a breathing pulse, so it reads as a live cursor
            // rather than a decal stamped on the imagery. Unscaled so it keeps
            // moving while the battle is paused.
            _pulse += Time.unscaledDeltaTime;
            _reticleQuad.localRotation = Quaternion.Euler(90f, 0f, _pulse * 24f);

            var c = _reticleMat.color;
            c.a = Mathf.Lerp(0.4f, 0.95f, (Mathf.Sin(_pulse * 3.2f) + 1f) * 0.5f);
            _reticleMat.color = c;
        }
    }
}
