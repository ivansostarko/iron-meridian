using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.UI;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Plays one <see cref="VfxId"/> inside a uGUI panel, in 3D, with its sound.
    ///
    /// Built on the same device as <c>ModelPreview</c>: uGUI cannot draw
    /// particles, so the effect lives on a rig parked far below the scene, lit
    /// and filmed by its own camera, and rendered into a
    /// <see cref="RenderTexture"/> that a <see cref="RawImage"/> displays. The
    /// rig sits well below anything a menu camera can see.
    ///
    /// **Scale.** <see cref="VfxSystem"/> blows every effect up to
    /// <see cref="VfxDef.scaleMeters"/> because the map is measured in
    /// kilometres. Here the effect is shown at its *authored* size — roughly one
    /// world unit — and the camera is pulled back to frame it. A 760 m missile
    /// burst rendered at 760 units would be a wall of orange; what the lab is
    /// for is seeing the effect's shape, and the metre figure is on the panel
    /// beside it in words.
    ///
    /// **Sound is 2D here.** <see cref="EffectAudio"/> places world sources with
    /// kilometre rolloff, which at this rig's distance from the listener is
    /// silence. The lab resolves the same clip through the same registry and
    /// plays it flat, so what you hear is the clip a burst would use — including
    /// the synthesised stand-in where no file is installed.
    ///
    /// See docs/08-PARTICLE-SYSTEMS.md.
    /// </summary>
    public class VfxPreview : MonoBehaviour, IDragHandler, IScrollHandler
    {
        const float RigDepth = -8000f;
        const float AutoSpinDegPerSec = 9f;
        const float MinPitch = -6f, MaxPitch = 60f;
        const float MinZoom = 0.45f, MaxZoom = 3.2f;
        /// <summary>Seconds a one-shot effect is left running before it is restarted.</summary>
        const float ReplayPadding = 0.6f;

        RawImage _image;
        Text _placeholder;
        RenderTexture _rt;
        Transform _rig, _pivot;
        Camera _cam;
        GameObject _effect;
        AudioSource _audio;
        /// <summary>Built here, so it has to be torn down here — materials are not collected with their renderer.</summary>
        Material _floorMat;

        VfxDef _def;
        float _yaw = 35f, _pitch = 16f, _zoom = 1f;
        float _framedDistance = 4f;
        bool _userTookOver;
        float _restartAt;

        /// <summary>True when the effect currently shown came from an authored prefab.</summary>
        public bool UsingAuthoredPrefab { get; private set; }

        /// <summary>Whether the preview replays a one-shot effect when it finishes.</summary>
        public bool Looping { get; set; } = true;

        public static VfxPreview Create(Transform uiParent, Vector2 size)
        {
            var image = UIFactory.CreateRawImage(uiParent, "VfxPreview");
            var preview = image.gameObject.AddComponent<VfxPreview>();
            preview.Build(image, size);
            return preview;
        }

        void Build(RawImage image, Vector2 size)
        {
            _image = image;
            _image.raycastTarget = true;           // needed for orbit/zoom input
            _image.color = Color.white;

            _placeholder = UIFactory.CreateText(transform, "", 18, GameConfig.UiTextDim);
            UIFactory.Stretch(_placeholder.rectTransform);
            _placeholder.gameObject.SetActive(false);

            // Render at device pixels rather than reference pixels, so a plume
            // is not soft on a 1440p screen.
            int w = Mathf.Max(64, Mathf.RoundToInt(size.x * 1.5f));
            int h = Mathf.Max(64, Mathf.RoundToInt(size.y * 1.5f));
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "VfxPreviewRT",
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
            };
            _image.texture = _rt;

            var rigGo = new GameObject("VfxPreviewRig");
            rigGo.transform.position = new Vector3(0f, RigDepth, 0f);
            _rig = rigGo.transform;

            // The bed the effect stands on. Fire and smoke read as rising off
            // something; without a ground plane they float in a void and the
            // difference between a ground fire and a plume disappears.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Ground";
            floor.transform.SetParent(_rig, false);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = Vector3.one * 40f;
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            Destroy(floor.GetComponent<Collider>());
            _floorMat = RuntimeMaterials.UnlitColor(new Color(0.08f, 0.10f, 0.13f));
            floor.GetComponent<MeshRenderer>().sharedMaterial = _floorMat;

            var pivotGo = new GameObject("Pivot");
            pivotGo.transform.SetParent(_rig, false);
            _pivot = pivotGo.transform;

            var camGo = new GameObject("VfxPreviewCamera");
            camGo.transform.SetParent(_pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
            _cam.fieldOfView = 40f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 400f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;                  // rendered on demand only

            // No light. Every particle material here is unlit — the procedural
            // builders' billboards and the ground quad both are — and a
            // directional light is global, so one added for this rig would also
            // relight whatever screen happens to be showing it.

            // 2D and on the rig, not in the world: see the class note.
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.priority = 100;
        }

        // ------------------------------------------------------------- show

        /// <summary>Builds and starts an effect. Restarting the same id replays it.</summary>
        public void Show(VfxDef def)
        {
            Clear();
            _def = def;

            if (def == null)
            {
                ShowPlaceholder("Select an effect to preview it.");
                return;
            }

            _effect = new GameObject("Preview_" + def.id);
            _effect.transform.SetParent(_rig, false);
            _effect.transform.localPosition = Vector3.zero;

            var prefab = VfxSystem.LoadPrefab(def);
            UsingAuthoredPrefab = prefab != null;

            if (prefab != null)
            {
                var visual = Instantiate(prefab, _effect.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                VfxSystem.Normalise(visual);
            }
            else
            {
                ProceduralVfx.Build(_effect, def);
            }

            Frame();
            PlaySound();

            _placeholder.gameObject.SetActive(false);
            _image.enabled = true;
            _cam.enabled = true;

            _restartAt = def.Loops ? 0f : Time.unscaledTime + def.lifeSeconds + ReplayPadding;
        }

        /// <summary>Restarts whatever is shown, from the beginning, with its sound.</summary>
        public void Replay()
        {
            if (_def != null) Show(_def);
        }

        /// <summary>
        /// Takes the effect off the rig and silences it. <see cref="_def"/> is
        /// deliberately kept, so REPLAY still knows what to start again —
        /// clearing it would make STOP a dead end that only reselecting the row
        /// could get out of.
        /// </summary>
        public void Stop()
        {
            Clear();
            _restartAt = 0f;
            ShowPlaceholder(_def == null ? "Select an effect to preview it." : "Stopped — REPLAY to run it again.");
        }

        /// <summary>The sound this effect carries, or None.</summary>
        public EffectSound Sound => _def?.sound ?? EffectSound.None;

        void PlaySound()
        {
            _audio.Stop();
            if (_def == null || _def.sound == EffectSound.None) return;

            var clip = EffectAudio.Clip(_def.sound);
            if (clip == null) return;

            _audio.clip = clip;
            _audio.loop = EffectAudio.IsLooping(_def.sound);
            // A shade under the mix an effect gets in the world: the lab plays
            // one sound at a time with nothing under it.
            _audio.volume = 0.6f;
            _audio.Play();
        }

        /// <summary>
        /// Pulls the camera back until the effect's particles fit. Particle
        /// systems have no meaningful renderer bounds before they have emitted,
        /// so this frames from the authored particle *size* instead, which is
        /// stable and does not pop as the first puff appears.
        /// </summary>
        void Frame()
        {
            float extent = 1.4f;
            foreach (var ps in _effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                float size = Mathf.Max(main.startSize.constantMax, main.startSize.constant);
                float reach = Mathf.Max(main.startSpeed.constantMax, main.startSpeed.constant) *
                              Mathf.Max(main.startLifetime.constantMax, main.startLifetime.constant);
                extent = Mathf.Max(extent, size * 1.5f + reach * 0.6f);
            }

            float fovRad = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            _framedDistance = Mathf.Clamp(extent / Mathf.Tan(fovRad) * 1.25f, 2f, 120f);
            _cam.farClipPlane = _framedDistance * 8f;

            // Look slightly above the ground plane: an effect anchored at the
            // origin grows upward, and centring on the origin puts half the
            // frame under the floor.
            _pivot.localPosition = new Vector3(0f, extent * 0.45f, 0f);
            ApplyOrbit();
        }

        void ShowPlaceholder(string message)
        {
            _placeholder.text = message;
            _placeholder.gameObject.SetActive(true);
            _image.enabled = false;
            _cam.enabled = false;
            _audio.Stop();
        }

        void Clear()
        {
            if (_effect != null) Destroy(_effect);
            _effect = null;
            _audio.Stop();
            if (!_userTookOver) { _yaw = 35f; _pitch = 16f; _zoom = 1f; }
        }

        void Update()
        {
            if (_effect == null) return;

            if (!_userTookOver) _yaw += AutoSpinDegPerSec * Time.unscaledDeltaTime;
            ApplyOrbit();

            // A one-shot effect is over in a couple of seconds, and a lab that
            // showed it once and then an empty box would be showing nothing most
            // of the time. Loop it until the effect is changed or stopped.
            if (Looping && _restartAt > 0f && Time.unscaledTime >= _restartAt)
                Replay();
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
            if (_effect == null) return;
            _userTookOver = true;                  // stop the turntable once the player is steering
            _yaw -= e.delta.x * 0.4f;
            _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.3f, MinPitch, MaxPitch);
            ApplyOrbit();
        }

        public void OnScroll(PointerEventData e)
        {
            if (_effect == null) return;
            _zoom = Mathf.Clamp(_zoom - e.scrollDelta.y * 0.1f, MinZoom, MaxZoom);
            ApplyOrbit();
        }

        void OnDestroy()
        {
            // The rig is a root object, not a child of the UI, so it has to be
            // torn down explicitly or it survives the screen that created it.
            if (_rig != null) Destroy(_rig.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_floorMat != null) Destroy(_floorMat);
        }
    }
}
