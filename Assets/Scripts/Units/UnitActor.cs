using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

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
        MeshRenderer _iconRenderer;
        Transform _ring;
        Transform _bar;
        TextMesh _label;
        float _baseScale;
        bool _selected;

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
            var bb = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bb.name = "Icon";
            Destroy(bb.GetComponent<MeshCollider>());
            var box = bb.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.2f);
            bb.transform.SetParent(transform, false);
            bb.transform.localScale = new Vector3(_baseScale, _baseScale * 0.75f, 1f);
            bb.transform.localPosition = new Vector3(0, _baseScale * 0.55f, 0);
            _billboard = bb.transform;

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
            var rm = new Material(Shader.Find("Sprites/Default"));
            rm.mainTexture = ProceduralTextures.Ring(
                state.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);
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
            var bm = new Material(Shader.Find("Unlit/Color"));
            bm.color = Color.green;
            bar.GetComponent<MeshRenderer>().material = bm;
            _bar = bar.transform;

            // --- echelon + name label ---
            var lbl = new GameObject("Label");
            lbl.transform.SetParent(_billboard, false);
            lbl.transform.localPosition = new Vector3(0, 0.85f, 0);
            lbl.transform.localScale = Vector3.one * 0.02f;
            _label = lbl.AddComponent<TextMesh>();
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 8;
            _label.fontSize = 48;
            _label.color = state.TeamEnum == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam;
            _label.text = $"{EchelonInfo.Indicator(state.EchelonEnum)}\n{ShortName()}";

            Mover = gameObject.AddComponent<UnitMover>();
            Mover.Init(this, _geo, _anchor);
        }

        string ShortName() =>
            string.IsNullOrEmpty(State.customName) ? Def.name : State.customName;

        public void SnapToTerrain()
        {
            double h = GeoUtils.SampleTerrainHeight(_geo, State.latitude, State.longitude,
                State.heightMeters > 0 ? State.heightMeters : 250.0);
            State.heightMeters = h;
            _anchor.longitudeLatitudeHeight = new double3(State.longitude, State.latitude, h + 2.0);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _billboard == null) return;

            // Billboard towards camera
            _billboard.rotation = Quaternion.LookRotation(
                _billboard.position - cam.transform.position, cam.transform.up);

            // Keep icons readable at any zoom: scale with distance
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float s = Mathf.Clamp(dist / 18f, 30f, 2600f) / 260f;
            _billboard.localScale = new Vector3(_baseScale * s, _baseScale * 0.75f * s, 1f);

            if (_selected && _ring != null)
            {
                float pulse = 1.45f + Mathf.Sin(Time.time * 4f) * 0.18f;
                _ring.localScale = Vector3.one * _baseScale * s * pulse;
            }

            // Strength bar colour/scale
            float str = Mathf.Clamp01(State.strength);
            _bar.localScale = new Vector3(0.9f * str, 0.07f, 1f);
            _bar.GetComponent<MeshRenderer>().material.color =
                Color.Lerp(Color.red, Color.green, str);
        }

        public void SetSelected(bool sel)
        {
            _selected = sel;
            if (_ring != null) _ring.gameObject.SetActive(sel);
        }

        public void SetHover(bool hover)
        {
            if (_iconRenderer != null)
                _iconRenderer.material.color = hover ? new Color(1.2f, 1.2f, 1.2f, 1f) : Color.white;
        }

        public float CurrentPower() => Def.PowerAt(State.EchelonEnum, State.strength);

        public void ApplyDamage(float dmg)
        {
            State.strength = Mathf.Max(0f, State.strength - dmg);
            State.morale = Mathf.Max(0f, State.morale - dmg * 40f);
            if (State.strength <= 0.01f) Die();
            else if (State.strength < 0.3f) State.status = UnitStatus.Routed.ToString();
        }

        void Die()
        {
            State.status = UnitStatus.Destroyed.ToString();
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
            // Sprites/Default: transparent, unlit, and exposes _Color for
            // hover highlight and death fade.
            var mat = new Material(Shader.Find("Sprites/Default"));
            var tex = Resources.Load<Texture2D>($"Icons/{team}/{unitId}");
            if (tex != null) mat.mainTexture = tex;
            return mat;
        }

        /// <summary>Refresh saved state (e.g. before serialising the map).</summary>
        public UnitState Snapshot() => State;
    }
}
