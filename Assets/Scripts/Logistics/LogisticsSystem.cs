using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Logistics
{
    /// <summary>
    /// The scenario's rear area: every depot, supply, fuel, ammunition, repair
    /// and medical point on the map, and the tool that puts them there.
    ///
    /// **Why logistics are their own system.** They are neither units nor task
    /// markers. A unit fights, moves and dies, and none of that is true of a
    /// fuel point; a task marker belongs to the formation that was given the
    /// order and is swept away with it, whereas an installation belongs to the
    /// scenario and outlives every formation that draws on it. Giving them
    /// their own owner is what lets them save, load and be edited without
    /// touching either of those.
    ///
    /// **Placement is arm-then-click**, the same gesture the effect tool uses:
    /// pick a kind, and a ghost of that kind's own symbol tracks the ground
    /// under the cursor so what you see is where it lands. The tool stays armed
    /// after a placement, because a rear area is laid out several sites at a
    /// time; right-click or Escape puts it away.
    ///
    /// Every placement is ground-checked. Cesium streams terrain in, so a click
    /// over tiles that have not arrived has no ground to sit on, and those
    /// clicks are refused with a message rather than guessed at.
    ///
    /// The catalogue owns what the six kinds *are*
    /// (<see cref="LogisticsCatalog"/>); this owns where they are. See
    /// docs/26-LOGISTICS.md.
    /// </summary>
    public class LogisticsSystem : MonoBehaviour
    {
        /// <summary>User-facing messages (wired to the HUD's flash line).</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the armed kind or the list of sites changes, so the panel can repaint.</summary>
        public event System.Action Changed;

        public bool IsArmed => _armed.HasValue;
        public LogisticsKind? Armed => _armed;

        /// <summary>Which side a newly placed site belongs to. The panel's team tab sets it.</summary>
        public Team Team { get; set; } = Team.User;

        public IReadOnlyList<LogisticsSite> Sites => _sites;

        readonly List<LogisticsSite> _sites = new List<LogisticsSite>();

        MapManager _map;
        Camera _cam;
        LogisticsKind? _armed;

        GameObject _ghost;
        CesiumGlobeAnchor _ghostAnchor;
        Transform _ghostQuad;
        Material _ghostMat;
        float _pulse;

        bool _validGround;
        double _lat, _lon;

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
            BuildGhost();
        }

        // ------------------------------------------------------------ placing

        /// <summary>Arms a kind, or disarms it if the same one is picked again.</summary>
        public void Toggle(LogisticsKind kind)
        {
            if (_armed.HasValue && _armed.Value == kind) { Cancel(); return; }

            _armed = kind;
            var def = LogisticsCatalog.Get(kind);
            _ghostMat.mainTexture = UI.UiIcons.GlyphFor(kind).texture;
            _ghostMat.color = def.tint;

            Flash?.Invoke($"Click the map to deploy a {def.name.ToLowerInvariant()}. " +
                          "Right-click or Esc to stop.");
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (!_armed.HasValue) return;
            _armed = null;
            if (_ghost != null) _ghost.SetActive(false);
            Changed?.Invoke();
        }

        void Update()
        {
            if (!_armed.HasValue) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            TrackGround();

            if (Input.GetMouseButtonDown(0)) PlaceHere();
        }

        /// <summary>Keeps the ghost on the ground point under the cursor.</summary>
        void TrackGround()
        {
            // Over a panel there is no map point to aim at, and raycasting
            // through the UI would put the ghost on whatever terrain happens to
            // be behind it.
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            Vector3 hit = default;
            _validGround = !overUI && _map.RaycastGround(_cam, Input.mousePosition, out hit);

            if (!_validGround)
            {
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, hit, out _lat, out _lon, out _);

            double h = GeoUtils.SampleTerrainHeight(_map.Georeference, _lat, _lon, 250) + 60.0;
            _ghostAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(_lon, _lat, h);
            _ghost.SetActive(true);
        }

        void PlaceHere()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!_validGround)
            {
                Flash?.Invoke("Terrain not loaded here yet — try again in a moment.");
                return;
            }

            var def = LogisticsCatalog.Get(_armed.Value);
            Add(new LogisticsSiteData
            {
                id = NewId(),
                kind = _armed.Value.ToString(),
                team = Team.ToString(),
                latitude = _lat,
                longitude = _lon
            });

            Flash?.Invoke($"{def.name} deployed — {def.serviceRadiusKm:0.#} km of ground served.");
        }

        static string NewId() =>
            "log-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

        // ------------------------------------------------------------- sites

        public LogisticsSite Add(LogisticsSiteData data)
        {
            if (string.IsNullOrEmpty(data.id)) data.id = NewId();
            var site = LogisticsSite.Create(_map.Georeference, data);
            _sites.Add(site);
            Changed?.Invoke();
            return site;
        }

        public void Remove(LogisticsSite site)
        {
            if (site == null) return;
            _sites.Remove(site);
            Destroy(site.gameObject);
            Changed?.Invoke();
        }

        /// <summary>Takes down every site. Used by RESET and before a map loads.</summary>
        public void Clear()
        {
            foreach (var s in _sites) if (s != null) Destroy(s.gameObject);
            _sites.Clear();
            Changed?.Invoke();
        }

        /// <summary>How many sites one side has on the map — the panel's readout.</summary>
        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var s in _sites)
                if (s != null && s.Data.team == team.ToString()) n++;
            return n;
        }

        // ------------------------------------------------------------- saving

        public List<LogisticsSiteData> Serialize()
        {
            var result = new List<LogisticsSiteData>();
            foreach (var s in _sites) if (s != null) result.Add(s.Data);
            return result;
        }

        public void LoadFrom(List<LogisticsSiteData> data)
        {
            Clear();
            if (data == null) return;
            foreach (var d in data)
            {
                if (d == null) continue;
                _sites.Add(LogisticsSite.Create(_map.Georeference, d));
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------- ghost

        /// <summary>
        /// The placement preview: the armed kind's own symbol, standing on the
        /// ground under the cursor. Deliberately the symbol rather than a
        /// generic reticle — with six kinds on one panel, "what am I about to
        /// drop" is the question the preview has to answer.
        /// </summary>
        void BuildGhost()
        {
            _ghost = new GameObject("LogisticsPlacementGhost");
            _ghost.transform.SetParent(_map.Georeference.transform, false);
            _ghostAnchor = _ghost.AddComponent<CesiumGlobeAnchor>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_ghost.transform, false);
            quad.transform.localScale = Vector3.one * 260f;

            _ghostMat = RuntimeMaterials.UnlitTexture(UI.UiIcons.Depot.texture);
            quad.GetComponent<MeshRenderer>().material = _ghostMat;
            _ghostQuad = quad.transform;

            _ghost.SetActive(false);
        }

        void LateUpdate()
        {
            if (_ghost == null || !_ghost.activeSelf) return;

            var cam = Camera.main;
            if (cam != null)
                _ghostQuad.rotation = Quaternion.LookRotation(
                    _ghostQuad.position - cam.transform.position, cam.transform.up);

            // A breathing alpha, so the ghost reads as a cursor rather than as a
            // site that has already been placed. Unscaled: the editor spends
            // most of its life with the clock stopped.
            _pulse += Time.unscaledDeltaTime;
            var c = _ghostMat.color;
            c.a = Mathf.Lerp(0.35f, 0.9f, (Mathf.Sin(_pulse * 3.2f) + 1f) * 0.5f);
            _ghostMat.color = c;
        }

        void OnDestroy()
        {
            if (_ghostMat != null) Destroy(_ghostMat);
        }
    }
}
