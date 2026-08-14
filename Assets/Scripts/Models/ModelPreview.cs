using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.UI;

namespace IronMeridian.Models
{
    /// <summary>
    /// Shows an animated 3D model inside a uGUI panel.
    ///
    /// uGUI cannot draw a mesh, so the model lives on a small rig parked far
    /// below the scene, lit and filmed by its own camera, and rendered into a
    /// <see cref="RenderTexture"/> that a <see cref="RawImage"/> displays. The
    /// rig sits at Y = -5000 so the menu scene's own camera — default far clip
    /// 1000 — can never see it.
    ///
    /// Attach by calling <see cref="Create"/>; the component lives on the
    /// RawImage so it can receive the drag and scroll events that orbit and zoom
    /// the camera.
    /// </summary>
    public class ModelPreview : MonoBehaviour, IDragHandler, IScrollHandler
    {
        const float RigDepth = -5000f;
        const float AutoSpinDegPerSec = 14f;
        const float MinPitch = -12f, MaxPitch = 45f;
        const float MinZoom = 0.55f, MaxZoom = 2.4f;

        RawImage _image;
        Text _placeholder;
        RenderTexture _rt;
        Transform _rig;        // parked container for everything below
        Transform _pivot;      // sits at the model's centre; yaw/pitch applied here
        Camera _cam;
        GameObject _model;

        float _yaw = 155f;     // three-quarter view, weapon side toward the camera
        float _pitch = 8f;
        float _zoom = 1f;
        float _framedDistance = 3f;
        bool _userTookOver;

        /// <summary>Builds the RawImage, render texture, rig and camera. Nothing is shown until <see cref="Show"/>.</summary>
        public static ModelPreview Create(Transform uiParent, Vector2 size)
        {
            var image = UIFactory.CreateRawImage(uiParent, "ModelPreview");
            var preview = image.gameObject.AddComponent<ModelPreview>();
            preview.Build(image, size);
            return preview;
        }

        void Build(RawImage image, Vector2 size)
        {
            _image = image;
            _image.raycastTarget = true;          // needed for orbit/zoom input
            _image.color = Color.white;

            _placeholder = UIFactory.CreateText(transform, "", 20, GameConfig.UiTextDim);
            UIFactory.Stretch(_placeholder.rectTransform);
            _placeholder.gameObject.SetActive(false);

            // Render at device pixels rather than reference pixels so the model
            // is not soft on a 1440p screen.
            int w = Mathf.Max(64, Mathf.RoundToInt(size.x * 1.5f));
            int h = Mathf.Max(64, Mathf.RoundToInt(size.y * 1.5f));
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "ModelPreviewRT",
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
            };
            _image.texture = _rt;

            var rigGo = new GameObject("ModelPreviewRig");
            rigGo.transform.position = new Vector3(0f, RigDepth, 0f);
            _rig = rigGo.transform;

            var pivotGo = new GameObject("Pivot");
            pivotGo.transform.SetParent(_rig, false);
            _pivot = pivotGo.transform;

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // A touch lighter than the panel so the silhouette separates from it.
            _cam.backgroundColor = new Color(0.09f, 0.11f, 0.15f, 1f);
            _cam.fieldOfView = 28f;               // long lens: less perspective distortion on a figure
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 200f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;                 // rendered on demand, only while a model is shown

            // Lights ride with the camera, so a model stays readable at any orbit.
            MakeLight(camGo.transform, "Key", new Vector3(28f, -22f, 0f), 1.15f, new Color(1f, 0.97f, 0.9f));
            MakeLight(camGo.transform, "Fill", new Vector3(-12f, 140f, 0f), 0.45f, new Color(0.65f, 0.75f, 1f));
        }

        static void MakeLight(Transform parent, string name, Vector3 euler, float intensity, Color colour)
        {
            var go = new GameObject("PreviewLight_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = colour;
            light.shadows = LightShadows.None;    // a single figure on a flat background gains nothing from shadows
        }

        /// <summary>
        /// Swaps in the model for this unit type. Shows an explanatory
        /// placeholder — never an empty box — when the unit has no model yet.
        /// </summary>
        public void Show(UnitDefinition unit)
        {
            Show(UnitModelLibrary.Resolve(unit), unit == null
                ? "Select a unit to preview its model."
                : $"No 3D model for {unit.name} yet.\nSee docs/09-3D-MODELS.md.");
        }

        /// <summary>
        /// Shows a model by its <see cref="UnitModelLibrary"/> id — the route
        /// the weapon catalogues take, since an airframe or a UAV is a
        /// <c>modelId</c> on a strike definition rather than a unit type.
        /// Still through the library, never a Resources path (golden rule 10).
        /// </summary>
        public void ShowModel(string modelId, string what)
        {
            if (string.IsNullOrEmpty(modelId))
            {
                Show(null, $"{what} has no 3D model.\nSee docs/09-3D-MODELS.md.");
                return;
            }
            Show(UnitModelLibrary.Get(modelId),
                $"Model '{modelId}' is not registered.\nSee docs/09-3D-MODELS.md.");
        }

        /// <summary>Swaps in a resolved model, or explains why there is not one.</summary>
        public void Show(UnitModelDef def, string missingMessage)
        {
            ClearModel();

            if (def == null)
            {
                ShowPlaceholder(missingMessage);
                return;
            }

            // Through the library: a procedural model has no prefab to load, and
            // checking for one here would report the one model that *cannot* be
            // missing as missing.
            _model = UnitModelLibrary.CreateInstance(def, _rig);
            if (_model == null)
            {
                ShowPlaceholder(def.IsProcedural
                    ? $"Model '{def.proceduralId}' could not be built.\nSee ProceduralModels."
                    : $"Model '{def.resourcePath}' is not installed.\n" +
                      "Run Tools > Iron Meridian > Install Unit Models.");
                return;
            }

            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            AddRimOutline();

            Frame(def.framing);
            PlayIdle(def);

            _placeholder.gameObject.SetActive(false);
            _image.enabled = true;
            _cam.enabled = true;
        }

        /// <summary>
        /// Traces the model's silhouette so it separates from the panel behind
        /// it. A dark vehicle against a dark background otherwise reads as a
        /// hole rather than an object, and no amount of relighting fixes that
        /// from every orbit angle.
        ///
        /// This is the one place QuickOutline is the right tool: it extrudes
        /// geometry along vertex normals, which needs an actual mesh with
        /// varying normals. It cannot outline the map's unit icons — those are
        /// single camera-facing quads whose normals all point at the viewer, so
        /// the extrusion collapses to a depth offset and draws nothing. The map
        /// icons trace their own alpha instead; see IconOutline.shader.
        ///
        /// OutlineVisible rather than OutlineAll, so the outline does not show
        /// through the model's near side and turn a solid figure into a wireframe.
        ///
        /// Note this is a hard compile-time dependency on the QuickOutline
        /// import (<c>Assets/QuickOutline/</c>) — the global <c>Outline</c> type.
        /// Deleting that package means deleting this method with it.
        /// </summary>
        void AddRimOutline()
        {
            if (_model == null) return;

            var outline = _model.AddComponent<Outline>();
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineColor = GameConfig.UiAccent;
            outline.OutlineWidth = 3.5f;
        }

        /// <summary>
        /// Points the camera at whatever was just loaded. Framing from renderer
        /// bounds rather than a per-model magic number means a new model drops in
        /// correctly sized without hand-tuning.
        /// </summary>
        void Frame(float framingMultiplier)
        {
            var renderers = _model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { _framedDistance = 3f; ApplyOrbit(); return; }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            _pivot.position = bounds.center;

            // Distance that fits the model's height in the vertical FOV, with a
            // margin so it does not touch the panel edges.
            float extent = Mathf.Max(bounds.extents.y, bounds.extents.x * 0.75f);
            float fovRad = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            _framedDistance = Mathf.Max(0.5f, extent / Mathf.Tan(fovRad) * 1.35f * framingMultiplier);
            _cam.farClipPlane = _framedDistance * 6f;

            ApplyOrbit();
        }

        void PlayIdle(UnitModelDef def)
        {
            // Vehicles and props are static meshes and are meant to be — the
            // turntable is their animation. Warning about a missing clip they
            // never had would be noise on every selection.
            if (string.IsNullOrEmpty(def.idleClip)) return;

            var anim = _model.GetComponentInChildren<Animation>();
            if (anim == null || anim.GetClip(def.idleClip) == null)
            {
                Debug.LogWarning($"[ModelPreview] '{def.resourcePath}' has no legacy clip '{def.idleClip}'. " +
                    "Showing a static pose. Re-run Tools > Iron Meridian > Install Unit Models " +
                    "(see docs/09-3D-MODELS.md).");
                return;
            }

            anim.wrapMode = WrapMode.Loop;
            anim.Play(def.idleClip);
        }

        void ShowPlaceholder(string message)
        {
            _placeholder.text = message;
            _placeholder.gameObject.SetActive(true);
            _image.enabled = false;
            _cam.enabled = false;
        }

        void ClearModel()
        {
            if (_model != null) Destroy(_model);
            _model = null;
            // A fresh unit gets the default framing back; the previous unit's
            // orbit would otherwise look arbitrary on a differently sized model.
            if (!_userTookOver) { _yaw = 155f; _pitch = 8f; _zoom = 1f; }
        }

        void Update()
        {
            if (_model == null) return;
            if (!_userTookOver) _yaw += AutoSpinDegPerSec * Time.unscaledDeltaTime;
            ApplyOrbit();
        }

        void ApplyOrbit()
        {
            if (_pivot == null || _cam == null) return;
            _pivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.transform.localPosition = new Vector3(0f, 0f, -_framedDistance * _zoom);
            _cam.transform.localRotation = Quaternion.identity;
        }

        // --- input: drag to orbit, wheel to zoom ---

        public void OnDrag(PointerEventData e)
        {
            if (_model == null) return;
            _userTookOver = true;                 // stop the turntable once the player is steering
            _yaw -= e.delta.x * 0.4f;
            _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.3f, MinPitch, MaxPitch);
            ApplyOrbit();
        }

        public void OnScroll(PointerEventData e)
        {
            if (_model == null) return;
            _zoom = Mathf.Clamp(_zoom - e.scrollDelta.y * 0.08f, MinZoom, MaxZoom);
            ApplyOrbit();
        }

        void OnDestroy()
        {
            // The rig is a root object, not a child of the UI, so it has to be
            // torn down explicitly or it survives the screen that created it.
            if (_rig != null) Destroy(_rig.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
