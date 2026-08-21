using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;
using IronMeridian.Vfx;

namespace IronMeridian.Logistics
{
    /// <summary>
    /// The map graphic for one logistic installation: a ground ring in the
    /// owning side's colour, the function's own symbol on a framed plate
    /// standing in the middle of it, a stock bar, a caption naming it — and, on
    /// demand, the ground it serves and the buildings that are actually there.
    ///
    /// **The plate billboards; the ring lies flat.** A logistics laydown is
    /// read two ways — from directly overhead, where what matters is *where*
    /// the sites are relative to the units they serve, and from a working
    /// camera angle, where what matters is *which* site is which. A flat ring
    /// answers the first at any tilt and a billboarded symbol answers the
    /// second, so the marker keeps both rather than trading one for the other.
    ///
    /// **The symbol is on a plate rather than bare.** It used to be a white
    /// silhouette drawn straight onto the terrain, which is legible over a
    /// field and gone over a town, a snowfield or a river — the ground a rear
    /// area is most often on. The plate carries the side in its frame and the
    /// function in its glyph, and gives both a dark ground of their own, so the
    /// marker's contrast stops being a property of whatever imagery is behind
    /// it. See <see cref="UI.UiIcons.MapMarkerFor"/>.
    ///
    /// **Three things are shown only when they are asked for**, because each
    /// costs something a rear area of a dozen sites cannot pay all the time:
    /// the service ring (terrain-draped, ~200 raycasts a rebuild — see
    /// <see cref="ShowServiceRing"/>), the 3D model (geometry, tied to the
    /// editor's SHOW UNIT 3D MODELS switch — see <see cref="SetModelVisible"/>)
    /// and the ambient motes over a working site.
    ///
    /// Sized like a task marker (constant apparent size, clamped) so a rear
    /// area reads as part of the same map as the formations it supports rather
    /// than as a separate layer of furniture.
    ///
    /// Clamped to the terrain and re-clamped until the ground under it has
    /// actually streamed in — see <see cref="Place"/>. See docs/26-LOGISTICS.md.
    /// </summary>
    public class LogisticsSite : MonoBehaviour
    {
        public LogisticsSiteData Data { get; private set; }
        public LogisticsKind Kind { get; private set; }

        /// <summary>
        /// Where the marker stands, in world space — what a screen-space pick
        /// projects to find it. Zero until the ground under it has been sampled,
        /// which is the honest answer: a site whose terrain has not streamed in
        /// is not on the map yet either. See <c>LogisticsSystem.PickAt</c>.
        /// </summary>
        public Vector3 Anchor => _base;

        /// <summary>
        /// Where the **marker plate is actually drawn**, in world space — what a
        /// click has to be measured against.
        ///
        /// Not <see cref="Anchor"/>. That is the ground the installation stands
        /// on; the plate is a billboard lifted above it by an offset that scales
        /// with zoom, and by more than twice as much again when the site's 3D
        /// model is up (see <see cref="LateUpdate"/>). Testing clicks against the
        /// ground point meant the top half of every plate was dead, and with
        /// models switched on the plate floated clear of its own hit area
        /// entirely — you clicked the symbol and nothing happened.
        ///
        /// <see cref="Units.UnitActor.IconWorldPosition"/> exists for exactly
        /// this reason and this mirrors it deliberately: on this map the thing
        /// you click is the counter, never the ground under it.
        ///
        /// Falls back to the ground point before the plate has been laid out.
        /// </summary>
        public Vector3 MarkerWorldPosition =>
            _marker != null ? _marker.position : _base;

        /// <summary>
        /// Half the plate's drawn width in world units, so a pick can be sized
        /// to the symbol the player is aiming at rather than to a fixed number
        /// of pixels that is right at one zoom and wrong at every other.
        /// </summary>
        public float MarkerWorldRadius =>
            _marker != null ? _marker.lossyScale.x * 0.5f : 0f;

        /// <summary>True while this site's service ring is drawn.</summary>
        public bool ServiceRingVisible => _ring != null && _ringShown;

        /// <summary>Metres above the sampled ground.</summary>
        const double ClearanceM = 10.0;
        const float ReclampSeconds = 1.2f;
        /// <summary>Ground-ring diameter in metres at the reference zoom, before camera scaling.</summary>
        const float RingMeters = 520f;

        /// <summary>
        /// Height of an installation's model on the ground, metres.
        ///
        /// Oversized, like every other model on this map: a real warehouse is
        /// twenty metres across, which at the zoom this game is played at is a
        /// smudge. Sized to sit inside the site's own ground ring so the two
        /// read as one object rather than as a shed beside a decal — and a
        /// shade larger than an airdropped bundle, because an installation
        /// genuinely is the bigger thing.
        /// </summary>
        const float InstallationModelMeters = 320f;

        /// <summary>Height of an airdropped cache's model on the ground, metres.</summary>
        const float CacheModelMeters = 190f;

        CesiumGeoreference _geo;
        Transform _groundRing, _marker, _stockTrack, _stockFill;
        /// <summary>The 3D installation — a dropped cache's bundle, or the site's own buildings.</summary>
        Transform _model;
        Material _groundRingMat, _markerMat, _stockTrackMat, _stockFillMat;
        TextMesh _caption;
        Transform _captionAnchor;
        Color _sideColour;

        /// <summary>The ground it serves, draped on the terrain. Built on first use.</summary>
        RangeRing _ring;
        bool _ringShown;

        /// <summary>Ambient motes over a working site. Null while models are off.</summary>
        VfxInstance _ambient;

        Vector3 _base, _up, _forward;
        bool _placed;
        float _reclampTimer;

        public static LogisticsSite Create(CesiumGeoreference geo, LogisticsSiteData data)
        {
            var go = new GameObject($"Logistics_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);

            var site = go.AddComponent<LogisticsSite>();
            site._geo = geo;
            site.Data = data;
            site.Kind = LogisticsCatalog.Parse(data.kind);
            site.Build();
            return site;
        }

        void Build()
        {
            _sideColour = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;

            // The ground ring is the side's; the plate inside it carries the
            // side in its frame and the function in its glyph. Colouring
            // everything the same would make a rear area one wash of blue in
            // which nothing can be picked out.
            _groundRing = Quad("GroundRing", ProceduralTextures.Ring(_sideColour, 128, 0.40f, 0.48f),
                out _groundRingMat, flat: true);
            _marker = Quad("Marker", MarkerTexture(), out _markerMat, flat: false);

            BuildStockBar();

            // A dropped cache is drawn as the thing it is, always. A placed
            // installation follows the editor's models switch — see
            // SetModelVisible.
            if (Data.airdropped) BuildModel();

            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.UpperCenter;
            _caption.alignment = TextAlignment.Center;
            // characterSize absorbs MapFont's fixed rasterisation size, so the
            // caption keeps the size it had while sharing the map's font atlas.
            _caption.characterSize = 8f * 40f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = _sideColour;
            _caption.text = CaptionText();
            RefreshStock();
        }

        Texture2D MarkerTexture() =>
            UI.UiIcons.MapMarkerFor(Kind, Data.team == nameof(Team.Enemy));

        // ------------------------------------------------------- the stock bar

        /// <summary>
        /// A two-quad bar under the plate: a dark track, and a fill as long as
        /// the fraction of stock left.
        ///
        /// **The number is already in the caption; this is the same fact at a
        /// glance.** Standing over a rear area the question is *which of these
        /// is nearly out*, and the answer has to survive being read at a zoom
        /// where "12 / 40 ISSUES" is four pixels tall. A bar is the one form
        /// that does — and it is deliberately the same three-stage green /
        /// amber / red the formation strength bars use, so a rear area in
        /// trouble looks like a formation in trouble.
        ///
        /// A site that does not track stock (an old save's inexhaustible depot)
        /// gets no bar at all rather than a full one, which would be a claim
        /// about a quantity nobody recorded.
        /// </summary>
        void BuildStockBar()
        {
            if (!Data.TracksStock) return;

            // Unity's built-in white texture rather than one drawn here: a bar
            // is a rectangle of flat colour, and the quad already is the
            // rectangle. Both are tinted through their material — the track once,
            // the fill on every change of stock.
            _stockTrack = Quad("StockTrack", Texture2D.whiteTexture, out _stockTrackMat, flat: false);
            _stockTrackMat.color = new Color(0.02f, 0.02f, 0.03f, 0.75f);

            _stockFill = Quad("StockFill", Texture2D.whiteTexture, out _stockFillMat, flat: false);
        }

        /// <summary>
        /// Stands the installation's 3D model on the ground.
        ///
        /// **Why a dropped cache and a placed depot are drawn differently.**
        /// They are different sorts of object and the map should say so. A depot
        /// is a *place* — what matters about it is which one it is and how far
        /// it reaches, which is what a doctrinal symbol says and a crate cannot
        /// — so its buildings appear only when the editor's SHOW UNIT 3D MODELS
        /// switch is on, alongside the formations'. A cache is a *thing somebody
        /// just put there*: the player watched it come down under a canopy, and
        /// what they want afterwards is to find it again on the ground where it
        /// landed, so its bundle is always drawn.
        ///
        /// The symbol does not go away in either case — it shrinks and rides
        /// above the model, so a site is still identifiable as ammunition or
        /// fuel from a distance at which the model is a dot. See
        /// <see cref="LateUpdate"/>.
        /// </summary>
        void BuildModel()
        {
            if (_model != null) return;

            string modelId = Data.airdropped
                ? Models.UnitModelLibrary.SupplyBundle
                : LogisticsCatalog.Get(Kind).modelId;

            var go = Models.UnitModelLibrary.CreateInstance(modelId, transform);
            // A missing model is not a missing site: the plate and the ring are
            // still there and the thing still supplies. Golden rule 10's library
            // has already said what to install.
            if (go == null) return;

            go.name = "SiteModel";
            // Nothing on it takes a click. Picking is done in screen space
            // against the site's anchor, and a mesh collider here would also put
            // geometry in the way of every terrain raycast on this map.
            foreach (var collider in go.GetComponentsInChildren<Collider>()) Destroy(collider);

            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float span = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float target = Data.airdropped ? CacheModelMeters : InstallationModelMeters;
                if (span > 0.0001f) t.localScale = Vector3.one * (target / span);
            }
            _model = t;
        }

        void DestroyModel()
        {
            if (_model == null) return;
            Destroy(_model.gameObject);
            _model = null;
        }

        /// <summary>
        /// Shows or hides the installation's buildings, following the editor's
        /// **GENERAL → SHOW UNIT 3D MODELS** switch.
        ///
        /// The rear area follows the same switch the formations do rather than
        /// carrying one of its own, because the question a player is asking when
        /// they reach for it is "let me see this piece of the battle", and a
        /// battlefield where the units are solid and the depots are decals is
        /// exactly the inconsistency the switch exists to remove.
        ///
        /// The **ambient motes come with the model**, not with the marker. A map
        /// of counters is a map of counters and should stay clean; a map you
        /// have switched into 3D wants the rear area to look occupied.
        ///
        /// An airdropped cache ignores this entirely — its bundle is the object
        /// the player watched land, and hiding it would hide the drop.
        /// </summary>
        public void SetModelVisible(bool on)
        {
            if (Data.airdropped) return;      // always drawn; see BuildModel

            if (on)
            {
                BuildModel();
                if (_ambient == null)
                    _ambient = VfxSystem.Attach(LogisticsCatalog.Get(Kind).siteVfx, transform);
            }
            else
            {
                DestroyModel();
                if (_ambient != null) { _ambient.Stop(); _ambient = null; }
            }
        }

        // ---------------------------------------------------- the service ring

        /// <summary>
        /// Draws or drops the ring showing the ground this installation serves.
        ///
        /// **It is the same instrument a weapon range is drawn with** —
        /// <see cref="RangeRing"/>, a feathered band draped on the terrain — for
        /// the reason the docs used to give for not drawing it at all: a flat
        /// disc at the site's own altitude would sink into every hill and float
        /// over every valley across a 25 km radius, which is worse than not
        /// drawing it. A draped band dips and rides with the ground, and states
        /// the distance honestly.
        ///
        /// **On demand rather than always**, because that band costs ~200
        /// terrain samples to build and a rear area is a dozen sites. It is
        /// shown for the site whose supply panel is open, and for every site at
        /// once when the LOGISTICS panel's SHOW SERVICE RINGS is switched on —
        /// which is the moment a designer is actually judging coverage, and the
        /// moment the cost is worth paying. See <c>LogisticsSystem</c>.
        ///
        /// Built lazily: a site nobody ever looks at never pays for a ring.
        /// </summary>
        public void ShowServiceRing(bool on)
        {
            _ringShown = on;

            if (!on)
            {
                if (_ring != null) _ring.Hide();
                return;
            }

            // Nothing to draw over until the ground has actually been sampled.
            //
            // `_placed` is the only honest test. `_base` stops being zero after
            // the first Place(), which runs whether or not the terrain answered
            // — it falls back to a nominal altitude — so a `_base` check would
            // be dead from the second frame onward. And a ring built on the
            // fallback is not merely wrong once: RangeRing only re-bakes its
            // heights when the centre or the radius moves, so it would stay a
            // flat disc buried in every ridge for the rest of the session.
            //
            // LateUpdate re-asks the moment the ground arrives.
            if (!_placed) return;

            var def = LogisticsCatalog.Get(Kind);
            if (_ring == null)
                _ring = RangeRing.Create(_geo, _geo.transform, _sideColour, "SERVES");

            _ring.Show(Data.latitude, Data.longitude, def.serviceRadiusKm,
                $"{def.name}  ·  {def.serviceRadiusKm:0.#} km");
        }

        /// <summary>
        /// Repaints what a change of stock changes: the caption carries the
        /// issues left, the bar shortens and recolours, and the ground ring dims
        /// as the site empties.
        ///
        /// Separate from <see cref="Refresh"/> because it runs on every draw —
        /// several times a minute across a rear area — and re-clamping the site
        /// to the terrain each time would be a terrain sample per issue.
        /// </summary>
        public void RefreshStock()
        {
            if (_caption != null) _caption.text = CaptionText();

            float fraction = Data.TracksStock
                ? Mathf.Clamp01((float)(Data.stock / Data.capacity))
                : 1f;

            if (_groundRingMat != null)
                // Down to a third rather than to nothing: a spent depot is still
                // an installation on the map and still somewhere the next convoy
                // comes to. Fading it out entirely would be hiding a thing that
                // is there.
                _groundRingMat.color = Color.Lerp(_sideColour * 0.35f, _sideColour, fraction);

            if (_stockFillMat != null) _stockFillMat.color = StockColour(fraction);
        }

        /// <summary>
        /// Green above half, amber below, red on the last fifth — the same three
        /// stages a formation's strength bar uses, so the two read as one
        /// language rather than as two colour schemes.
        /// </summary>
        static Color StockColour(float fraction) =>
            fraction > 0.5f ? new Color(0.42f, 0.82f, 0.45f)
            : fraction > 0.2f ? new Color(0.95f, 0.74f, 0.28f)
            : new Color(0.92f, 0.33f, 0.30f);

        /// <summary>
        /// The caption: what the site is, and — when it tracks stock — how much
        /// of it is left.
        ///
        /// On the map rather than only in the panel, because the question a
        /// player has while looking at a rear area is which of these is nearly
        /// out, and answering it should not need six clicks.
        /// </summary>
        string CaptionText()
        {
            var def = LogisticsCatalog.Get(Kind);
            string name = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
            if (!Data.TracksStock) return name;
            return $"{name}\n{Data.stock:0.#} / {Data.capacity:0.#} ISSUES";
        }

        /// <summary>Re-reads the record in place — a renamed or re-sided site.</summary>
        public void Refresh()
        {
            _sideColour = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;
            if (_groundRingMat != null) _groundRingMat.color = _sideColour;
            // The plate's frame is the side's, so a captured site needs the
            // other side's texture rather than a tint over the same one.
            if (_markerMat != null) _markerMat.mainTexture = MarkerTexture();
            if (_caption != null)
            {
                _caption.text = CaptionText();
                _caption.color = _sideColour;
            }
            RefreshStock();
            _placed = false;

            // A ring already up is in the old side's colour and possibly at the
            // old kind's radius. Rebuild it rather than leave it stating
            // something that is no longer true.
            if (_ringShown && _ring != null)
            {
                Destroy(_ring.gameObject);
                _ring = null;
                ShowServiceRing(true);
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (!_placed)
            {
                _reclampTimer -= Time.unscaledDeltaTime;
                if (_reclampTimer <= 0f)
                {
                    Place();
                    // The ring could not be built while the ground under it was
                    // unknown — its heights are baked from terrain samples. Now
                    // that the ground is there, put up the one that was asked for.
                    if (_placed && _ringShown) ShowServiceRing(true);
                }
            }

            transform.position = _base;
            transform.rotation = Quaternion.LookRotation(_forward, _up);

            // Constant apparent size, the same depth-along-forward measure the
            // unit icons and task markers use, so all three scale together.
            float depth = Mathf.Max(1f, Vector3.Dot(_base - cam.transform.position, cam.transform.forward));
            float s = Mathf.Clamp(depth / 18f, 30f, 2600f) / 260f;

            _groundRing.localScale = Vector3.one * RingMeters * s;

            // The plate stands up to face the camera, lifted just clear of the
            // ring so the two read as one marker rather than as a decal with
            // something floating over it. Over a model it is smaller and higher:
            // the model is the object and the plate is the label on it.
            bool overModel = _model != null;
            float plate = RingMeters * s * (overModel ? 0.42f : 0.62f);
            var toCamera = _base - cam.transform.position;

            _marker.position = _base + _up * (plate * (overModel ? 1.45f : 0.55f));
            _marker.localScale = Vector3.one * plate;
            _marker.rotation = Quaternion.LookRotation(_marker.position - cam.transform.position, cam.transform.up);

            LayOutStockBar(plate);

            _captionAnchor.position = _base - _up * (RingMeters * s * 0.06f);
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(toCamera, cam.transform.up);
        }

        /// <summary>
        /// Hangs the stock bar under the plate, in the plate's own frame, so it
        /// billboards with it and shortens from the left like every other bar in
        /// the game.
        /// </summary>
        void LayOutStockBar(float plate)
        {
            if (_stockTrack == null) return;

            float width = plate * 0.86f, height = plate * 0.11f;
            Vector3 down = -_marker.up * (plate * 0.60f);

            _stockTrack.position = _marker.position + down;
            _stockTrack.rotation = _marker.rotation;
            _stockTrack.localScale = new Vector3(width, height, 1f);

            float fraction = Mathf.Clamp01((float)(Data.stock / Mathf.Max(1e-4f, (float)Data.capacity)));
            // Anchored to the track's left edge rather than centred, so an
            // emptying depot's bar retreats instead of shrinking symmetrically
            // into the middle — the same motion a strength bar makes.
            float inner = width * 0.88f;
            _stockFill.position = _stockTrack.position
                                  - _marker.right * (inner * 0.5f)
                                  + _marker.right * (inner * fraction * 0.5f);
            _stockFill.rotation = _marker.rotation;
            _stockFill.localScale = new Vector3(inner * fraction, height * 0.6f, 1f);
            _stockFill.gameObject.SetActive(fraction > 0.001f);
        }

        Transform Quad(string name, Texture2D texture, out Material material, bool flat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            if (flat) quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            material = RuntimeMaterials.UnlitTexture(texture);
            quad.GetComponent<MeshRenderer>().material = material;
            return quad.transform;
        }

        /// <summary>
        /// Samples the ground and builds the local frame. Retried on a cadence
        /// until the terrain is there — a site placed while tiles are still
        /// streaming would otherwise sit at the fallback height forever.
        /// </summary>
        void Place()
        {
            _reclampTimer = ReclampSeconds;

            bool found = GeoUtils.TrySampleTerrainHeight(_geo, Data.latitude, Data.longitude, out double ground);
            double h = (found ? ground : (Data.heightMeters > 0 ? Data.heightMeters : 250.0)) + ClearanceM;
            Data.heightMeters = h;

            _base = GeoUtils.GeoToUnity(_geo, Data.latitude, Data.longitude, h);
            _up = (GeoUtils.GeoToUnity(_geo, Data.latitude, Data.longitude, h + 1000.0) - _base).normalized;

            // Any horizontal axis will do — the marker has no facing — but
            // taking it from local north keeps the ring's own orientation
            // stable as the camera moves, rather than spinning with it.
            GeoUtils.Destination(Data.latitude, Data.longitude, 0.0, 0.2,
                out double northLat, out double northLon);
            Vector3 fwd = GeoUtils.GeoToUnity(_geo, northLat, northLon, h) - _base;
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            _placed = found;
        }

        void OnDestroy()
        {
            if (_groundRingMat != null) Destroy(_groundRingMat);
            if (_markerMat != null) Destroy(_markerMat);
            if (_stockTrackMat != null) Destroy(_stockTrackMat);
            if (_stockFillMat != null) Destroy(_stockFillMat);
            // The ring and the ambient effect are not children of this object —
            // the ring anchors itself to the globe and the effect is owned by
            // the VFX system — so neither goes with the site unless it is told.
            if (_ring != null) Destroy(_ring.gameObject);
            if (_ambient != null) _ambient.Stop(immediate: true);
        }
    }
}
