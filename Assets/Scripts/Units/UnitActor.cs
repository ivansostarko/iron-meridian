using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// A unit on the map: APP-6 icon billboard anchored to the globe, with
    /// selection ring, strength bar and echelon label. Movement is handled by
    /// <see cref="UnitMover"/>.
    /// </summary>
    public class UnitActor : MonoBehaviour
    {
        public UnitState State { get; private set; }
        public UnitDefinition Def { get; private set; }
        public UnitMover Mover { get; private set; }
        public bool IsAlive => State.strength > 0.01f && State.status != UnitStatus.Destroyed.ToString();

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        Transform _billboard;
        Transform _iconVisual;
        MeshRenderer _iconRenderer;
        Transform _ring;
        Transform _bar;
        TextMesh _label;
        HeadingArrow _arrow;
        float _baseScale;
        bool _selected;

        // --- terrain clamping ---
        /// <summary>Seconds between ground samples while the terrain under the unit is still unknown.</summary>
        const float GroundRetrySeconds = 0.4f;
        /// <summary>Seconds between ground samples once the unit is sitting on real terrain.</summary>
        const float GroundRefreshSeconds = 2.5f;
        float _groundTimer;
        bool _grounded;

        // --- map label sizing ---
        const string LabelScalePref = "im.unitLabelScale";
        /// <summary>Multiplier applied to every unit's map label. 1 = the authored size.</summary>
        public static float LabelScale { get; private set; } = PlayerPrefs.GetFloat(LabelScalePref, 1f);

        /// <summary>
        /// Resizes every unit label on the map and remembers the choice.
        /// Applied to the live actors rather than only to newly spawned ones, so
        /// the slider reads as a direct manipulation instead of a setting that
        /// takes effect later.
        /// </summary>
        public static void SetLabelScale(float scale)
        {
            LabelScale = Mathf.Clamp(scale, 0.5f, 2.5f);
            PlayerPrefs.SetFloat(LabelScalePref, LabelScale);
            foreach (var actor in UnitRegistry.All)
                if (actor != null) actor.ApplyLabelScale();
        }

        void ApplyLabelScale()
        {
            if (_label == null) return;
            _label.transform.localScale = Vector3.one * (BaseLabelScale * LabelScale);
        }

        /// <summary>Authored label scale, before the player's multiplier.</summary>
        const float BaseLabelScale = 0.02f;

        // --- battle damage effects (see docs/08-PARTICLE-SYSTEMS.md) ---
        VfxInstance _burning;
        float _nextImpactVfx;
        float _nextWeaponVfx;

        public static UnitActor Spawn(CesiumGeoreference geo, UnitState state)
        {
            var def = UnitDatabase.Get(state.defId);
            if (def == null) { Debug.LogError($"Unknown unit def '{state.defId}'"); return null; }

            var go = new GameObject($"Unit_{state.team}_{state.defId}_{state.instanceId}");
            go.transform.SetParent(geo.transform, false);
            var actor = go.AddComponent<UnitActor>();
            actor.Build(geo, state, def);
            UnitRegistry.Register(actor);
            return actor;
        }

        void Build(CesiumGeoreference geo, UnitState state, UnitDefinition def)
        {
            _geo = geo; State = state; Def = def;

            _anchor = gameObject.AddComponent<CesiumGlobeAnchor>();
            SnapToTerrain();

            _baseScale = 260f + 60f * (int)state.EchelonEnum / (float)(int)Echelon.Army;

            // --- icon billboard ---
            // _billboard faces the camera and carries the shared scale for the
            // icon + strength bar + label. _iconVisual is a separate child so the
            // icon alone can roll to show facing/heading without rotating the bar
            // and text label sideways with it.
            var billboardGo = new GameObject("Billboard");
            billboardGo.transform.SetParent(transform, false);
            _billboard = billboardGo.transform;
            // Vertical offset (icon "stands" on the ground point) and scale
            // are both set every frame in LateUpdate, proportional to zoom.

            var bb = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bb.name = "Icon";
            Destroy(bb.GetComponent<MeshCollider>());
            var box = bb.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.2f);
            bb.transform.SetParent(_billboard, false);
            _iconVisual = bb.transform;

            _iconRenderer = bb.GetComponent<MeshRenderer>();
            _iconRenderer.material = IconMaterial(state.TeamEnum == Team.User ? "Friendly" : "Enemy", def.id);

            // --- selection ring (flat on ground) ---
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "Ring";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ring.transform.localScale = Vector3.one * _baseScale * 1.6f;
            ring.transform.localPosition = new Vector3(0, 6f, 0);
            var rm = RuntimeMaterials.UnlitTexture(ProceduralTextures.Ring(
                state.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam));
            ring.GetComponent<MeshRenderer>().material = rm;
            _ring = ring.transform;
            _ring.gameObject.SetActive(false);

            // --- strength bar ---
            var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bar.name = "StrengthBar";
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(_billboard, false);
            bar.transform.localPosition = new Vector3(0, 0.62f, -0.01f);
            bar.transform.localScale = new Vector3(0.9f, 0.07f, 1f);
            var bm = RuntimeMaterials.UnlitColor(Color.green);
            bar.GetComponent<MeshRenderer>().material = bm;
            _bar = bar.transform;

            // --- echelon + name label ---
            var lbl = new GameObject("Label");
            lbl.transform.SetParent(_billboard, false);
            lbl.transform.localPosition = new Vector3(0, 0.85f, 0);
            lbl.transform.localScale = Vector3.one * (BaseLabelScale * LabelScale);
            _label = lbl.AddComponent<TextMesh>();
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 8;
            _label.fontSize = 48;
            _label.color = state.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam;
            _label.text = $"{EchelonInfo.Indicator(state.EchelonEnum)}\n{ShortName()}";

            // Facing arrow on the ground; shown only while the unit is selected.
            _arrow = HeadingArrow.Create(_geo, this,
                state.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);

            Mover = gameObject.AddComponent<UnitMover>();
            Mover.Init(this, _geo, _anchor);

            // A unit restored from a save at low strength is already burning —
            // damage state is part of the map, not just something that happens live.
            RefreshBurning();
        }

        string ShortName() =>
            string.IsNullOrEmpty(State.customName) ? Def.name : State.customName;

        public void SnapToTerrain()
        {
            _grounded = GeoUtils.TrySampleTerrainHeight(_geo, State.latitude, State.longitude, out double h);
            if (!_grounded) h = State.heightMeters > 0 ? State.heightMeters : 250.0;
            State.heightMeters = h;
            _groundTimer = _grounded ? GroundRefreshSeconds : GroundRetrySeconds;
            _anchor.longitudeLatitudeHeight = new double3(State.longitude, State.latitude, h + 2.0);
        }

        /// <summary>
        /// Keeps the icon standing on the terrain rather than at whatever height
        /// it was placed at.
        ///
        /// Cesium streams tiles in, so the sample taken when a unit spawns
        /// routinely misses: the map is loading, there is nothing to hit, and
        /// the unit is left at the 250 m fallback — floating over a valley or
        /// buried inside a ridge. Retrying until the ground is actually found,
        /// then refreshing slowly, is what makes an icon visible at every zoom
        /// in both 2D and 3D. A miss never overwrites a good height, so a unit
        /// that has been grounded once cannot be lost again when tiles unload.
        ///
        /// Skipped while marching: <see cref="UnitMover"/> is already sampling
        /// on a much tighter cadence and easing the unit along the contour.
        /// </summary>
        void ClampToGround()
        {
            if (Mover != null && Mover.IsMoving) return;

            _groundTimer -= Time.deltaTime;
            if (_groundTimer > 0f) return;
            _groundTimer = _grounded ? GroundRefreshSeconds : GroundRetrySeconds;

            if (!GeoUtils.TrySampleTerrainHeight(_geo, State.latitude, State.longitude, out double h)) return;

            _grounded = true;
            State.heightMeters = h;
            _anchor.longitudeLatitudeHeight = new double3(State.longitude, State.latitude, h + 2.0);
        }

        // Camera.main is a tagged-object lookup; with a full order of battle on
        // the map that ran once per unit per frame. The rig's camera doesn't
        // change during a scene, so resolve it once and share it.
        static Camera _mainCam;
        static Camera MainCamera()
        {
            if (_mainCam == null) _mainCam = Camera.main;
            return _mainCam;
        }

        void LateUpdate()
        {
            var cam = MainCamera();
            if (cam == null || _billboard == null) return;

            ClampToGround();

            // Keep icons a constant apparent size at any zoom. Use depth along
            // the camera's forward axis rather than raw Euclidean distance —
            // raw distance made icons shrink toward the screen edges in the
            // near-top-down 2D view, since off-centre units are farther from
            // the camera in a straight line even at the same zoom/altitude.
            float depth = Mathf.Max(1f, Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward));
            float s = Mathf.Clamp(depth / 18f, 30f, 2600f) / 260f;
            _billboard.localScale = new Vector3(_baseScale * s, _baseScale * 0.75f * s, 1f);

            // Anchor the icon's base (not its centre) at the ground point. This
            // offset must scale with `s` too — a fixed offset sized for one zoom
            // level makes the icon visibly float above the terrain at any other
            // zoom, worst when zoomed in close. Set before the LookRotation below
            // so the facing calculation uses this frame's position, not last
            // frame's (a stale read here made the billboard swim slightly on
            // fast zoom changes).
            _billboard.localPosition = new Vector3(0, _baseScale * 0.55f * s, 0);

            // Billboard towards camera
            _billboard.rotation = Quaternion.LookRotation(
                _billboard.position - cam.transform.position, cam.transform.up);

            // Icon rolls in-plane to show facing/heading; bar and label above stay upright.
            _iconVisual.localRotation = Quaternion.Euler(0, 0, State.headingDeg);

            // The facing arrow tracks the icon's on-screen size rather than
            // computing its own, so it stays in proportion at every zoom.
            if (_selected && _arrow != null) _arrow.UpdateArrow(_baseScale * s);

            if (_selected && _ring != null)
            {
                // CesiumGlobeAnchor rotates the unit root to correct for globe
                // curvature as it moves (adjustOrientationForGlobeWhenMoving),
                // so a plain local rotation/offset here would tilt and drift
                // away from the unit once it's not exactly at the georeference
                // origin. Set the ring's world transform directly each frame,
                // the same way the billboard already bypasses that tilt.
                float pulse = 1.45f + Mathf.Sin(Time.time * 4f) * 0.18f;
                _ring.rotation = Quaternion.Euler(90, 0, 0);
                _ring.position = transform.position + Vector3.up * 6f;
                _ring.localScale = Vector3.one * _baseScale * s * pulse;
            }

            // Strength bar colour/scale
            float str = Mathf.Clamp01(State.strength);
            _bar.localScale = new Vector3(0.9f * str, 0.07f, 1f);
            _bar.GetComponent<MeshRenderer>().material.color =
                Color.Lerp(Color.red, Color.green, str);
        }

        /// <summary>Manually set the unit's facing (0..360, degrees from north); rotates the icon.</summary>
        public void SetHeading(float deg) => State.headingDeg = ((deg % 360f) + 360f) % 360f;

        /// <summary>
        /// Drops the unit straight onto a new spot — the map-editor reposition.
        /// No travel animation and no fuel cost: this is the designer moving a
        /// counter, not the unit marching. Travel belongs to <see cref="UnitMover.MoveTo"/>.
        /// </summary>
        public void SetPosition(double lat, double lon)
        {
            Mover.Cancel();
            State.latitude = lat;
            State.longitude = lon;
            SnapToTerrain();
            State.status = UnitStatus.Idle.ToString();
            UnitRegistry.NotifyMoved();
        }

        /// <summary>Deletes this unit from the map immediately (no confirmation — caller decides).</summary>
        public void RemoveFromMap()
        {
            UnitRegistry.Unregister(this);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // The arrow is parented to the georeference (so globe curvature does
            // not tilt it), which means it does not go away with this object.
            if (_arrow != null) Destroy(_arrow.gameObject);
        }

        public void SetSelected(bool sel)
        {
            _selected = sel;
            if (_ring != null) _ring.gameObject.SetActive(sel);
            // Facing is shown for any selected unit — in the scenario editor as
            // much as in battle, because knowing which way a counter points is
            // what the editor is for.
            if (_arrow != null) _arrow.SetVisible(sel);
        }

        /// <summary>
        /// Marks the unit as being aimed with the facing tool (<c>C</c>), which
        /// brightens its heading arrow while the player swings it around.
        /// </summary>
        public void SetAiming(bool aiming)
        {
            if (_arrow != null) _arrow.SetAiming(aiming);
        }

        public void SetHover(bool hover)
        {
            if (_iconRenderer != null)
                _iconRenderer.material.color = hover ? new Color(1.2f, 1.2f, 1.2f, 1f) : Color.white;
        }

        public float CurrentPower() => Def.PowerAt(State.EchelonEnum, State.strength);

        /// <summary>
        /// This formation's size on a 0..1 scale (team → army). Drives how big
        /// its fire, smoke and death explosion read on the map: a burning squad
        /// and a burning army should not look the same.
        /// </summary>
        public float FormationScale01 =>
            (int)State.EchelonEnum / (float)(int)Echelon.Army;

        public void ApplyDamage(float dmg)
        {
            State.strength = Mathf.Max(0f, State.strength - dmg);
            State.morale = Mathf.Max(0f, State.morale - dmg * 40f);

            // Rounds landing. Throttled hard: combat ticks once a second against
            // every opponent in range, so an unthrottled puff per exchange would
            // blanket the front line.
            if (Time.time >= _nextImpactVfx)
            {
                _nextImpactVfx = Time.time + GameConfig.VfxImpactCooldownSeconds;
                VfxSystem.Play(VfxId.ImpactBurst, State.latitude, State.longitude,
                    Mathf.Lerp(0.7f, 1.4f, FormationScale01));
            }

            if (State.strength <= 0.01f) Die();
            else if (State.strength < 0.3f) State.status = UnitStatus.Routed.ToString();

            RefreshBurning();
        }

        /// <summary>
        /// Damage to a formation's ability to act rather than to its numbers:
        /// morale and organisation only. This is what suppressive fire is for —
        /// a pinned battalion still has all its people and cannot do anything
        /// with them. A formation whose organisation collapses is marked
        /// Suppressed, which reads on the map and in the info panel.
        /// </summary>
        public void ApplyShock(float amount)
        {
            if (amount <= 0f || !IsAlive) return;
            State.morale = Mathf.Max(0f, State.morale - amount);
            State.organisation = Mathf.Max(0f, State.organisation - amount * 1.4f);

            // Routed is the worse state and is owned by ApplyDamage; suppression
            // must not quietly promote a routed formation back up to pinned.
            if (State.organisation < 25f && State.status != UnitStatus.Routed.ToString())
                State.status = UnitStatus.Suppressed.ToString();
        }

        /// <summary>
        /// Muzzle/dust signature when this unit shoots. Called by
        /// <see cref="CombatSystem"/>; throttled so it marks "this formation is
        /// in action" rather than firing once per resolved exchange.
        /// </summary>
        public void NotifyFiring()
        {
            if (Time.time < _nextWeaponVfx) return;
            _nextWeaponVfx = Time.time + GameConfig.VfxWeaponFireCooldownSeconds;
            VfxSystem.Play(VfxId.WeaponFire, State.latitude, State.longitude,
                Mathf.Lerp(0.8f, 1.5f, FormationScale01));
        }

        /// <summary>
        /// A badly mauled formation burns. The fire is parented to the unit so
        /// it travels with a withdrawing unit, and is cleared if the unit is
        /// reinforced back above the threshold.
        /// </summary>
        void RefreshBurning()
        {
            bool shouldBurn = IsAlive && State.strength <= GameConfig.VfxBurningStrength;

            if (shouldBurn && _burning == null)
            {
                _burning = VfxSystem.Attach(VfxCatalog.FireForScale(FormationScale01), transform);
            }
            else if (!shouldBurn && _burning != null)
            {
                _burning.Stop();
                _burning = null;
            }
        }

        void Die()
        {
            State.status = UnitStatus.Destroyed.ToString();

            // Cut the attached fire immediately — the wreck effect below takes
            // over, and it is world-anchored so it outlives this GameObject.
            if (_burning != null) { _burning.Stop(true); _burning = null; }

            VfxSystem.PlayWreck(State.latitude, State.longitude, FormationScale01);

            StartCoroutine(FadeOut());
        }

        System.Collections.IEnumerator FadeOut()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime)
            {
                if (_iconRenderer != null)
                {
                    var c = _iconRenderer.material.color;
                    c.a = 1f - t;
                    _iconRenderer.material.color = c;
                }
                transform.Rotate(0, 0, Time.deltaTime * 30f);
                yield return null;
            }
            UnitRegistry.Unregister(this);
            Destroy(gameObject);
        }

        static Material IconMaterial(string team, string unitId)
        {
            // Unlit + transparent, and exposes _Color for hover highlight and
            // death fade.
            var tex = Resources.Load<Texture2D>($"Icons/{team}/{unitId}");
            if (tex == null)
                Debug.LogWarning($"[UnitActor] Missing icon texture: Resources/Icons/{team}/{unitId}");
            return RuntimeMaterials.UnlitTexture(tex);
        }

        /// <summary>Refresh saved state (e.g. before serialising the map).</summary>
        public UnitState Snapshot() => State;
    }
}
