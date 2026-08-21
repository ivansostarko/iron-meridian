using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;
using IronMeridian.Vfx;

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
    /// **Two ways onto the map, and the same preview for both.**
    ///
    /// • **Drag** a kind's button onto the terrain. The direct gesture, and the
    ///   one the unit palette already teaches: you are carrying the thing and
    ///   you put it down. It is what the LOGISTICS panel leads with.
    /// • **Arm** a kind and click. The gesture a drag cannot make — onto ground
    ///   you have to pan to first, and from a keyboard-only or pad-driven
    ///   session. The tool stays armed after a placement, because a rear area is
    ///   laid out several sites at a time; right-click or Escape puts it away.
    ///
    /// Both drive <see cref="TrackGround"/>, so what the preview shows and where
    /// the site lands cannot disagree.
    ///
    /// **The preview says three things**, because a rear area is judged on all
    /// three and a bare cursor answered none of them:
    ///
    /// | | |
    /// |---|---|
    /// | The **plate** | *what* you are about to drop — with six kinds on one panel this is the first question |
    /// | The **motes** | *where* it will land, in a form that survives a shallow camera angle and a fold of ground that a flat marker does not |
    /// | The **service ring** | *what it will reach* — the whole geometry of a laydown, draped on the terrain, before you commit rather than after |
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

        /// <summary>True while a click on the map would drop an installation.</summary>
        public bool IsArmed => _armed.HasValue;
        public LogisticsKind? Armed => _armed;

        /// <summary>True while a kind is being dragged out of the panel.</summary>
        public bool IsDragging => _dragging.HasValue;

        /// <summary>
        /// Which side a newly placed site belongs to. The panel's team tab sets
        /// it — there is one selected side in the editor, not one per panel.
        ///
        /// Re-dresses a live preview, because the plate's frame carries the
        /// side: switching tabs with a kind armed used to leave a blue plate
        /// following the cursor for a site that was about to land red.
        /// </summary>
        public Team Team
        {
            get => _team;
            set
            {
                if (_team == value) return;
                _team = value;
                var live = _dragging ?? _armed;
                if (live.HasValue && _ghostMat != null) DressGhost(live.Value);
            }
        }
        Team _team = Team.User;

        public IReadOnlyList<LogisticsSite> Sites => _sites;

        readonly List<LogisticsSite> _sites = new List<LogisticsSite>();

        MapManager _map;
        Camera _cam;
        LogisticsKind? _armed;
        LogisticsKind? _dragging;

        // --- the preview -------------------------------------------------
        GameObject _ghost;
        CesiumGlobeAnchor _ghostAnchor;
        Transform _ghostQuad;
        Material _ghostMat;
        VfxInstance _ghostMotes;
        RangeRing _ghostRing;
        float _pulse;

        bool _validGround;
        double _lat, _lon;

        // --- what the whole rear area is showing -------------------------
        bool _serviceRings;
        bool _models;

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
            BuildGhost();
        }

        // ------------------------------------------------------------ placing

        /// <summary>Arms a kind for click-to-place, or disarms it if the same one is picked again.</summary>
        public void Toggle(LogisticsKind kind)
        {
            if (_armed.HasValue && _armed.Value == kind) { Cancel(); return; }

            CancelDrag();
            _armed = kind;
            DressGhost(kind);

            var def = LogisticsCatalog.Get(kind);
            Flash?.Invoke($"Click the map to deploy a {def.name.ToLowerInvariant()} — " +
                          $"{def.serviceRadiusKm:0.#} km of ground served. " +
                          "Right-click or Esc to stop.");
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (!_armed.HasValue) return;
            _armed = null;
            HideGhost();
            Changed?.Invoke();
        }

        void Update()
        {
            // A drag is driven by the panel's pointer events, not from here —
            // uGUI owns the gesture and reading the mouse as well would place
            // one site and arm another off the same click.
            if (_dragging.HasValue || !_armed.HasValue) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();
            TrackGround(Input.mousePosition, overUI);

            if (Input.GetMouseButtonDown(0) && !overUI) PlaceHere(_armed.Value);
        }

        // -------------------------------------------------------- drag to drop

        /// <summary>
        /// Picks a kind up out of the panel. Called from the LOGISTICS button's
        /// <c>BeginDrag</c>.
        ///
        /// Arming is stood down first: they are two ways of saying the same
        /// thing, and a tool that was still armed when the drag ended would drop
        /// a second site on the next click.
        /// </summary>
        public void BeginDrag(LogisticsKind kind)
        {
            Cancel();
            _dragging = kind;
            _validGround = false;
            DressGhost(kind);
            Changed?.Invoke();
        }

        /// <summary>Moves the preview under the pointer. Called on every <c>Drag</c> event.</summary>
        public void DragTo(Vector2 screenPos, bool overUI)
        {
            if (!_dragging.HasValue) return;
            TrackGround(screenPos, overUI);
        }

        /// <summary>
        /// Puts the dragged kind down. Returns true when a site was actually
        /// deployed; a refusal has already been flashed.
        /// </summary>
        public bool EndDrag(Vector2 screenPos, bool overUI)
        {
            if (!_dragging.HasValue) return false;

            // Re-track at the release point before letting go of the kind. uGUI
            // does raise Drag before EndDrag, but a release that lands on the
            // same pixel the pointer was already on raises no Drag at all — and
            // placing off a stale point is exactly the bug the shared preview
            // exists to make impossible.
            TrackGround(screenPos, overUI);

            var kind = _dragging.Value;
            _dragging = null;

            // Released back over the panel or the HUD — not a place on the map,
            // so nothing is dropped onto whatever terrain happens to be behind
            // that interface.
            if (overUI)
            {
                HideGhost();
                Changed?.Invoke();
                Flash?.Invoke("Drop the installation onto the map, not the interface.");
                return false;
            }

            bool placed = PlaceHere(kind);
            HideGhost();
            Changed?.Invoke();
            return placed;
        }

        /// <summary>Abandons a drag without placing anything.</summary>
        public void CancelDrag()
        {
            if (!_dragging.HasValue) return;
            _dragging = null;
            HideGhost();
            Changed?.Invoke();
        }

        /// <summary>Keeps the preview on the ground point under the pointer.</summary>
        void TrackGround(Vector2 screenPos, bool overUI)
        {
            // Over a panel there is no map point to aim at, and raycasting
            // through the UI would put the preview on whatever terrain happens
            // to be behind it.
            Vector3 hit = default;
            _validGround = !overUI && _map.RaycastGround(_cam, screenPos, out hit);

            if (_validGround)
            {
                GeoUtils.UnityToGeo(_map.Georeference, hit, out _lat, out _lon, out _);

                // Two separate questions, and both must answer yes. The raycast
                // says the pointer is over *something*; this says the ground
                // under that point can actually be measured. They come apart at
                // a tile seam, and a site deployed there sits at the fallback
                // height inside a ridge.
                _validGround = GeoUtils.TrySampleTerrainHeight(_map.Georeference, _lat, _lon, out _);
            }

            if (!_validGround) { HideGhost(keepDressed: true); return; }

            double h = GeoUtils.SampleTerrainHeight(_map.Georeference, _lat, _lon, 250) + 60.0;
            _ghostAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(_lon, _lat, h);
            _ghost.SetActive(true);
            EnsureGhostMotes();

            var kind = _dragging ?? _armed;
            if (!kind.HasValue) return;

            // The ring follows the cursor at the kind's own service radius, so
            // the coverage of a site is judged before it is committed rather
            // than after. RangeRing re-samples the terrain only once the centre
            // has moved a few per cent of the radius, which is what makes this
            // affordable while dragging across a map.
            var def = LogisticsCatalog.Get(kind.Value);
            EnsureGhostRing();
            _ghostRing.Show(_lat, _lon, def.serviceRadiusKm,
                $"{def.name}  ·  {def.serviceRadiusKm:0.#} km");
        }

        /// <summary>Deploys one site at the tracked point, or refuses and says why.</summary>
        bool PlaceHere(LogisticsKind kind)
        {
            if (!_validGround)
            {
                Flash?.Invoke("No solid ground there yet — the terrain is still streaming in.");
                return false;
            }

            var def = LogisticsCatalog.Get(kind);
            Add(new LogisticsSiteData
            {
                id = NewId(),
                kind = kind.ToString(),
                team = Team.ToString(),
                latitude = _lat,
                longitude = _lon
            });

            Flash?.Invoke($"{def.name} deployed — {def.serviceRadiusKm:0.#} km of ground served, " +
                          $"{LogisticsCatalog.DefaultStock(kind):0.#} issues held.");
            return true;
        }

        static string NewId() =>
            "log-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

        // ------------------------------------------------------------- sites

        public LogisticsSite Add(LogisticsSiteData data)
        {
            if (string.IsNullOrEmpty(data.id)) data.id = NewId();
            StockIfUnset(data);
            var site = LogisticsSite.Create(_map.Georeference, data);
            Dress(site);
            _sites.Add(site);
            Changed?.Invoke();
            return site;
        }

        public void Remove(LogisticsSite site)
        {
            if (site == null) return;
            if (_ringSite == site) _ringSite = null;
            _sites.Remove(site);
            Destroy(site.gameObject);
            Changed?.Invoke();
        }

        /// <summary>Takes down every site. Used by RESET and before a map loads.</summary>
        public void Clear()
        {
            _ringSite = null;
            foreach (var s in _sites) if (s != null) Destroy(s.gameObject);
            _sites.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// Brings one site into line with what the rear area as a whole is
        /// showing. Applied on creation and on load as well as on a toggle, so a
        /// site that arrives while models are on arrives with its buildings —
        /// the same rule <c>UnitActor.ModelsVisible</c> follows for formations.
        /// </summary>
        void Dress(LogisticsSite site)
        {
            if (site == null) return;
            site.SetModelVisible(_models);
            site.ShowServiceRing(_serviceRings);
        }

        // ----------------------------------------------- what the map shows

        /// <summary>True while every site is drawing the ground it serves.</summary>
        public bool ServiceRingsVisible => _serviceRings;

        /// <summary>
        /// Draws the ground every installation serves, or drops them all.
        ///
        /// **A switch rather than always on.** Each ring is a band draped on the
        /// terrain — ~200 samples to build — and a rear area is a dozen sites, so
        /// paying for all of them on every georeference shift would be a real
        /// cost for a picture that is only wanted while a laydown is being
        /// judged. Switched on, it is the one view that answers *does this rear
        /// area actually cover the force*, which is the question the radii exist
        /// for. See docs/26-LOGISTICS.md §4.
        ///
        /// It does not fight the supply panel: a site whose panel is open shows
        /// its own ring through the same call, and turning the switch off puts
        /// back whatever the selection asked for.
        /// </summary>
        public void SetServiceRingsVisible(bool on)
        {
            if (_serviceRings == on) return;
            _serviceRings = on;

            // Switching off puts back whatever the *selection* asked for rather
            // than clearing the map: the open supply panel says its site's ring
            // is drawn, and a switch on another panel must not quietly make that
            // a lie.
            foreach (var s in _sites)
                if (s != null) s.ShowServiceRing(on || s == _ringSite);

            Changed?.Invoke();
        }

        /// <summary>
        /// The one site whose supply panel is open, and which therefore keeps
        /// its ring when the all-sites switch goes off. Null when no panel is up.
        /// </summary>
        LogisticsSite _ringSite;

        /// <summary>
        /// Shows one site's service ring on its own — what opening its supply
        /// panel does. A no-op while the rear area is showing all of them, so
        /// closing the panel cannot pull down a ring the switch put up.
        /// </summary>
        public void ShowRingFor(LogisticsSite site, bool on)
        {
            // Recorded either way, so the all-sites switch knows which ring to
            // leave standing when it is turned off — see SetServiceRingsVisible.
            if (on) _ringSite = site;
            else if (_ringSite == site) _ringSite = null;

            if (_serviceRings) return;
            if (site != null) site.ShowServiceRing(on);
        }

        /// <summary>
        /// Stands the rear area's buildings up, or takes them down — driven from
        /// the editor's **GENERAL → SHOW UNIT 3D MODELS**, the same switch the
        /// formations follow. See <see cref="LogisticsSite.SetModelVisible"/>.
        /// </summary>
        public void SetModelsVisible(bool on)
        {
            if (_models == on) return;
            _models = on;
            foreach (var s in _sites) if (s != null) s.SetModelVisible(on);
        }

        /// <summary>
        /// The site under a screen position, or null.
        ///
        /// **Screen space rather than a collider**, which is what every other
        /// click target on this map uses. A site's marker is drawn at a constant
        /// *apparent* size — it is the same number of pixels across at 500 m and
        /// at 50 km — so a collider would have to be resized every frame to keep
        /// matching it, and a pick that disagreed with what is on screen is
        /// worse than no pick at all. Projecting the marker and measuring pixels
        /// is the same test the eye is making.
        ///
        /// **Measured against the plate, not the ground.** The marker is a
        /// billboard standing above the site's ground point, by an offset that
        /// scales with zoom and grows again when the 3D model is up. Testing the
        /// ground point left the top half of every plate dead and, with models
        /// switched on, put the whole symbol outside its own hit area — see
        /// <see cref="LogisticsSite.MarkerWorldPosition"/>.
        ///
        /// The radius follows the plate's drawn size, with a floor so a site
        /// seen from 50 km up is still a target a hand can hit.
        ///
        /// Nearest wins, so two sites laid on top of each other are picked one
        /// at a time from the top rather than at random.
        /// </summary>
        public LogisticsSite PickAt(Camera cam, Vector2 screenPos, float radiusPx = 26f)
        {
            if (cam == null) return null;

            LogisticsSite best = null;
            float bestDistance = float.MaxValue;

            foreach (var site in _sites)
            {
                if (site == null) continue;
                // The ground under it has not been sampled yet, so it is not on
                // the map in any sense a click could mean.
                if (site.Anchor.sqrMagnitude < 1e-6f) continue;

                Vector3 marker = site.MarkerWorldPosition;

                // Behind the camera projects to a point in front of it, which
                // would make a site on the far side of the globe clickable.
                Vector3 view = cam.WorldToViewportPoint(marker);
                if (view.z <= 0f) continue;

                Vector3 centre = cam.WorldToScreenPoint(marker);
                float reach = Mathf.Max(radiusPx, ScreenRadius(cam, marker, site.MarkerWorldRadius));

                float distance = Vector2.Distance(screenPos, centre);
                if (distance > reach || distance >= bestDistance) continue;
                bestDistance = distance;
                best = site;
            }

            return best;
        }

        /// <summary>
        /// A world-space radius at a world-space point, in pixels.
        ///
        /// Projected by offsetting along the camera's own up axis rather than by
        /// dividing by depth: the marker is a billboard facing the camera, so
        /// its screen size is exactly the projection of that offset, at any
        /// field of view and any distance.
        /// </summary>
        static float ScreenRadius(Camera cam, Vector3 worldPoint, float worldRadius)
        {
            if (worldRadius <= 0f) return 0f;
            Vector3 centre = cam.WorldToScreenPoint(worldPoint);
            Vector3 edge = cam.WorldToScreenPoint(worldPoint + cam.transform.up * worldRadius);
            return Vector2.Distance(centre, edge);
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
                StockIfUnset(d);
                var site = LogisticsSite.Create(_map.Georeference, d);
                Dress(site);
                _sites.Add(site);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Gives an installation the stock its kind carries, when the record
        /// does not say.
        ///
        /// **Every site gets one, including old saves.** A scenario written
        /// before installations held anything arrives with `capacity` at zero,
        /// and a rear area that supplied nothing would be a silent regression
        /// for every map that already exists — so the catalogue's figure is
        /// filled in on load rather than only on placement. A designer who wants
        /// a different number edits the save; a zero in the file is not a
        /// deliberate empty depot, it is a file written before the field existed.
        ///
        /// An **airdropped** cache is never touched here: its stock is what the
        /// sortie carried, and <c>AirSupplySystem</c> has already set it.
        /// </summary>
        static void StockIfUnset(LogisticsSiteData data)
        {
            if (data == null || data.capacity > 0.0) return;
            data.capacity = LogisticsCatalog.DefaultStock(LogisticsCatalog.Parse(data.kind));
            data.stock = data.capacity;
        }

        // ------------------------------------------------------------- ghost

        /// <summary>
        /// The placement preview's billboard: the armed kind's **map marker** —
        /// the very texture the deployed site will wear.
        ///
        /// Deliberately the finished marker rather than a generic reticle or a
        /// bare glyph. With six kinds on one panel, "what am I about to drop" is
        /// the first question the preview has to answer, and showing the plate
        /// the site will actually carry makes the preview a promise rather than
        /// an approximation.
        /// </summary>
        void BuildGhost()
        {
            _ghost = new GameObject("LogisticsPlacementGhost");
            _ghost.transform.SetParent(_map.Georeference.transform, false);
            _ghostAnchor = _ghost.AddComponent<CesiumGlobeAnchor>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_ghost.transform, false);
            quad.transform.localScale = Vector3.one * 300f;

            _ghostMat = RuntimeMaterials.UnlitTexture(
                UI.UiIcons.MapMarkerFor(LogisticsKind.SupplyDepot, enemy: false));
            quad.GetComponent<MeshRenderer>().material = _ghostMat;
            _ghostQuad = quad.transform;

            _ghost.SetActive(false);
        }

        /// <summary>Points the preview at one kind and this side.</summary>
        void DressGhost(LogisticsKind kind)
        {
            _ghostMat.mainTexture = UI.UiIcons.MapMarkerFor(kind, Team == Team.Enemy);
            _ghostMat.color = Color.white;
        }

        /// <summary>
        /// Lights the motes rising out of the spot the site will land on. A flat
        /// marker foreshortens into a line at the shallow angle the editor is
        /// worked at, and vanishes behind a fold of ground entirely; something
        /// rising out of the ground survives both.
        ///
        /// **Attached only once the ghost is on screen.** A particle system
        /// built under a deactivated object never receives its <c>Play</c> — the
        /// call is dropped on an inactive GameObject — so the motes would exist
        /// and emit nothing. Building them at the first frame the preview is
        /// actually over terrain sidesteps that entirely.
        /// </summary>
        void EnsureGhostMotes()
        {
            if (_ghost == null || !_ghost.activeInHierarchy) return;

            if (_ghostMotes == null)
            {
                _ghostMotes = VfxSystem.Attach(VfxId.LogisticsPlacementMotes, _ghost.transform);
                return;
            }

            // **And restart them if they were switched off with the ghost.**
            // Deactivating a GameObject stops the particle systems under it, and
            // ProceduralVfx builds them with `playOnAwake` false and plays them
            // once — so re-enabling the object brings back a system that emits
            // nothing. The pointer crosses the panel between every placement,
            // which makes this the normal path rather than an edge case.
            foreach (var ps in _ghostMotes.GetComponentsInChildren<ParticleSystem>(true))
                if (!ps.isPlaying) ps.Play();
        }

        void EnsureGhostRing()
        {
            if (_ghostRing != null) return;
            _ghostRing = RangeRing.Create(_map.Georeference, _map.Georeference.transform,
                GameConfig.UiAccent, "SERVES");
        }

        /// <summary>
        /// Puts the preview away.
        ///
        /// <paramref name="keepDressed"/> is the difference between the pointer
        /// having wandered off the terrain — where the tool is still live and
        /// will need its motes back the moment it comes home — and the tool
        /// being stood down, where they should stop.
        /// </summary>
        void HideGhost(bool keepDressed = false)
        {
            if (_ghost != null) _ghost.SetActive(false);
            if (_ghostRing != null) _ghostRing.Hide();

            if (keepDressed) return;
            if (_ghostMotes != null) { _ghostMotes.Stop(); _ghostMotes = null; }
        }

        void LateUpdate()
        {
            if (_ghost == null || !_ghost.activeSelf) return;

            var cam = Camera.main;
            if (cam != null)
                _ghostQuad.rotation = Quaternion.LookRotation(
                    _ghostQuad.position - cam.transform.position, cam.transform.up);

            // A breathing alpha, so the preview reads as a cursor rather than as
            // a site that has already been placed. Unscaled: the editor spends
            // most of its life with the clock stopped.
            _pulse += Time.unscaledDeltaTime;
            var c = _ghostMat.color;
            c.a = Mathf.Lerp(0.45f, 1f, (Mathf.Sin(_pulse * 3.2f) + 1f) * 0.5f);
            _ghostMat.color = c;
        }

        void OnDestroy()
        {
            if (_ghostMat != null) Destroy(_ghostMat);
            if (_ghostRing != null) Destroy(_ghostRing.gameObject);
            if (_ghostMotes != null) _ghostMotes.Stop(immediate: true);
        }
    }
}
