using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The map graphic for one mine or obstacle: its doctrinal symbol lying flat
    /// on the terrain, in the owning side's colour, with a caption.
    ///
    /// **Flat, not billboarded** — the opposite choice from a logistic site's
    /// symbol, and for a reason. A supply point is a *place*, and what matters
    /// is which one it is, so its glyph stands up to face the camera. An
    /// obstacle is a piece of *ground* — it has an extent, it lies across an
    /// axis, and reading it means seeing how it sits against the terrain and
    /// what it blocks. A symbol painted on the map answers that; one standing up
    /// like a signpost does not.
    ///
    /// **Sized in metres, not in pixels.** Every other marker on this map holds
    /// a constant apparent size, because a counter is a counter at any zoom. An
    /// obstacle belt is 500 m of ground and has to *stay* 500 m of ground: a
    /// minefield that shrank as you zoomed out would be lying about what it
    /// covers, which is the one thing a control measure exists to state.
    ///
    /// **A minefield is drawn as the ground it covers.** When the record
    /// carries a polygon (<see cref="ObstacleSiteData.HasArea"/>) the graphic is
    /// not a symbol at all: it is the doctrinal minefield area — the outline of
    /// the belt with **mine symbols studded along it**, which is how APP-6 and
    /// MIL-STD-2525 draw one and how it has been drawn on paper for eighty
    /// years. A single filled circle at a nominal 520 m told the reader "mines
    /// somewhere about here"; the outline tells them where the edge is, which is
    /// the only question a minefield graphic is ever asked.
    ///
    /// The studs are <see cref="UI.UiIcons.MineGeneral"/> — the doctrinal filled
    /// circle — rather than the composite MINEFIELD glyph, because the composite
    /// *is* an outline with mines on it and nesting one inside another would be
    /// the symbol drawn twice.
    ///
    /// Clamped to the terrain and re-clamped until the ground under it has
    /// actually streamed in. See docs/31-OBSTACLES.md.
    /// </summary>
    public class ObstacleMarker : MonoBehaviour
    {
        public ObstacleSiteData Data { get; private set; }
        public ObstacleKind Kind { get; private set; }

        /// <summary>Metres above the sampled ground — enough to beat z-fighting, low enough to read as painted on.</summary>
        const double ClearanceM = 18.0;
        const float ReclampSeconds = 1.2f;

        /// <summary>Ground between mine symbols along an outlined belt, metres.</summary>
        const double StudSpacingM = 420.0;
        /// <summary>
        /// Studs on one belt. Floored so a small field still reads as a
        /// minefield rather than as a plain outline, capped so tracing a
        /// hundred-kilometre boundary does not cost a hundred terrain samples
        /// every re-clamp.
        /// </summary>
        const int MinStuds = 6, MaxStuds = 44;
        /// <summary>Diameter of one stud on the ground, metres.</summary>
        const float StudSizeM = 150f;
        /// <summary>Width of the belt outline, metres. A control measure, so narrow.</summary>
        const float OutlineWidthM = 90f;

        CesiumGeoreference _geo;
        Transform _symbol;
        Material _material;
        TextMesh _caption;
        Transform _captionAnchor;
        Color _colour;

        /// <summary>
        /// The belt outline and its mine studs — null for a point graphic.
        ///
        /// **Both hang off the georeference, not off this marker.** The marker's
        /// own transform is rewritten every <see cref="LateUpdate"/> to keep the
        /// stamped symbol standing on its patch of ground, and anything
        /// parented to it is dragged along by that. The studs are positioned in
        /// world space from their own terrain samples, so being dragged would
        /// put every one of them somewhere else. They are torn down explicitly
        /// in <see cref="OnDestroy"/> instead, which is what parenting would
        /// otherwise have bought.
        /// </summary>
        MapLine _outline;
        Transform _studRoot;
        readonly System.Collections.Generic.List<Transform> _studs =
            new System.Collections.Generic.List<Transform>();
        Material _studMaterial;

        /// <summary>True when this graphic is a drawn belt rather than a stamped symbol.</summary>
        public bool IsArea => Data != null && Data.HasArea;

        Vector3 _base, _up, _forward;
        bool _placed;
        float _reclampTimer;

        /// <summary>Where the marker stands, in world space — what a screen-space pick projects to find.</summary>
        public Vector3 Anchor => _base;

        public static ObstacleMarker Create(CesiumGeoreference geo, ObstacleSiteData data)
        {
            var go = new GameObject($"Obstacle_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);

            var marker = go.AddComponent<ObstacleMarker>();
            marker._geo = geo;
            marker.Data = data;
            marker.Kind = ObstacleCatalog.Parse(data.kind);
            marker.Build();
            return marker;
        }

        void Build()
        {
            var def = ObstacleCatalog.Get(Kind);

            // The type's own tint, darkened toward the owning side's colour: an
            // obstacle belt is read first as "mines" and second as "whose", and
            // the type is the more urgent of the two.
            Color side = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;
            _colour = Color.Lerp(def.tint, side, 0.35f);

            if (IsArea) BuildArea();
            else BuildSymbol(def);

            var anchor = new GameObject("Caption");
            anchor.transform.SetParent(transform, false);
            _captionAnchor = anchor.transform;

            _caption = anchor.AddComponent<TextMesh>();
            _caption.anchor = TextAnchor.UpperCenter;
            _caption.alignment = TextAlignment.Center;
            _caption.characterSize = 8f * 40f / UI.MapFont.FontSize;
            UI.MapFont.Apply(_caption);
            _caption.color = _colour;
            _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
        }

        /// <summary>The stamped graphic: one doctrinal symbol lying on the ground.</summary>
        void BuildSymbol(ObstacleDef def)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Symbol";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * def.widthMeters;

            _material = RuntimeMaterials.UnlitTexture(UI.UiIcons.GlyphFor(Kind).texture);
            _material.color = _colour;
            quad.GetComponent<MeshRenderer>().material = _material;
            _symbol = quad.transform;
        }

        /// <summary>
        /// The drawn belt: its outline, then mine symbols studded along it.
        ///
        /// The outline is a <see cref="MapLine"/> of the <c>Boundary</c> kind —
        /// the one kind that is always draped flat on the terrain, which a belt
        /// edge has to be to stay readable across a ridge or a river bank. It is
        /// parented under this marker so removing the barrier removes its
        /// graphic, without <see cref="ObstacleSystem"/> having to know that an
        /// area is two objects rather than one.
        /// </summary>
        void BuildArea()
        {
            var ring = new System.Collections.Generic.List<GeoPoint>(Data.points)
            {
                Data.points[0]      // closed: a belt with a gap in its edge is a gap in the belt
            };

            _outline = MapLine.Create(_geo, new MapLineData
            {
                id = "obstacle-" + Data.id,
                kind = nameof(LineKind.Boundary),
                team = Data.team,
                is3D = false,
                autoGenerated = true,
                label = "",
                colorHex = "#" + ColorUtility.ToHtmlStringRGB(_colour),
                widthMeters = OutlineWidthM,
                points = new System.Collections.Generic.List<GeoPoint>()
            });
            _outline.SetPoints(ring);

            _studMaterial = RuntimeMaterials.UnlitTexture(UI.UiIcons.MineGeneral.texture);
            _studMaterial.color = _colour;

            var studs = new GameObject($"MineStuds_{Data.id}");
            studs.transform.SetParent(_geo.transform, false);
            _studRoot = studs.transform;

            foreach (var at in StudPoints(ring))
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Mine";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(_studRoot, false);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = Vector3.one * StudSizeM;
                quad.GetComponent<MeshRenderer>().material = _studMaterial;
                _studs.Add(quad.transform);
                _studGeo.Add(at);
            }
        }

        /// <summary>Where each stud sits, geodetic — kept so a re-clamp can re-sample the ground.</summary>
        readonly System.Collections.Generic.List<GeoPoint> _studGeo =
            new System.Collections.Generic.List<GeoPoint>();

        /// <summary>
        /// Points evenly spaced round the belt's edge, at
        /// <see cref="StudSpacingM"/> or as near as the floor and cap allow.
        ///
        /// Spaced by *distance travelled along the perimeter* rather than one
        /// per corner: a belt tied into a road bend has its corners bunched at
        /// the bend, and a symbol per corner would draw six mines in the bend
        /// and none along the two-kilometre run away from it.
        /// </summary>
        static System.Collections.Generic.List<GeoPoint> StudPoints(
            System.Collections.Generic.List<GeoPoint> ring)
        {
            var result = new System.Collections.Generic.List<GeoPoint>();
            if (ring == null || ring.Count < 2) return result;

            double perimeterM = 0.0;
            for (int i = 0; i + 1 < ring.Count; i++)
                perimeterM += GeoUtils.DistanceKm(ring[i].latitude, ring[i].longitude,
                                                  ring[i + 1].latitude, ring[i + 1].longitude) * 1000.0;
            if (perimeterM <= 1.0) return result;

            int count = Mathf.Clamp(Mathf.RoundToInt((float)(perimeterM / StudSpacingM)),
                                    MinStuds, MaxStuds);
            double step = perimeterM / count;

            double walked = 0.0, nextAt = step * 0.5;   // half a step in, so no stud sits on a corner
            for (int i = 0; i + 1 < ring.Count && result.Count < count; i++)
            {
                double legM = GeoUtils.DistanceKm(ring[i].latitude, ring[i].longitude,
                                                  ring[i + 1].latitude, ring[i + 1].longitude) * 1000.0;
                if (legM <= 0.0) continue;

                while (nextAt <= walked + legM && result.Count < count)
                {
                    double t = (nextAt - walked) / legM;
                    result.Add(new GeoPoint
                    {
                        latitude = ring[i].latitude + (ring[i + 1].latitude - ring[i].latitude) * t,
                        longitude = ring[i].longitude + (ring[i + 1].longitude - ring[i].longitude) * t
                    });
                    nextAt += step;
                }
                walked += legM;
            }
            return result;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (!_placed)
            {
                _reclampTimer -= Time.unscaledDeltaTime;
                if (_reclampTimer <= 0f) Place();
            }

            transform.position = _base;
            transform.rotation = Quaternion.LookRotation(_forward, _up);

            // The caption is the one part that does hold a constant apparent
            // size: it is text, and text that scaled with the ground would be
            // unreadable at every zoom but one.
            var def = ObstacleCatalog.Get(Kind);
            float depth = Mathf.Max(1f, Vector3.Dot(_base - cam.transform.position, cam.transform.forward));
            // An outlined belt captions its own centre. A stamped symbol is
            // pushed clear of itself, or the words sit on top of the graphic
            // they are naming.
            _captionAnchor.position = IsArea ? _base : _base - _forward * (def.widthMeters * 0.55f);
            _captionAnchor.localScale = Vector3.one * Mathf.Clamp(depth / 2600f, 0.05f, 6f);
            _captionAnchor.rotation = Quaternion.LookRotation(_base - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// Samples the ground and builds the local frame. Retried on a cadence
        /// until the terrain is there — a graphic placed while tiles are still
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

            // The graphic is laid along its own bearing — an obstacle lies
            // *across* something, and a belt drawn at whatever angle the globe
            // happened to hand it would be a belt across nothing.
            GeoUtils.Destination(Data.latitude, Data.longitude, Data.headingDeg, 0.2,
                out double aheadLat, out double aheadLon);
            Vector3 fwd = GeoUtils.GeoToUnity(_geo, aheadLat, aheadLon, h) - _base;
            fwd -= _up * Vector3.Dot(fwd, _up);
            _forward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

            PlaceStuds();

            _placed = found;
        }

        /// <summary>
        /// Lays each mine symbol on its own patch of ground. Runs on the same
        /// re-clamp cadence as the marker itself — a belt drawn across ground
        /// that has not streamed in yet would otherwise leave its studs buried
        /// or floating for the rest of the session.
        /// </summary>
        void PlaceStuds()
        {
            for (int i = 0; i < _studs.Count && i < _studGeo.Count; i++)
            {
                var stud = _studs[i];
                if (stud == null) continue;

                var at = _studGeo[i];
                bool found = GeoUtils.TrySampleTerrainHeight(_geo, at.latitude, at.longitude,
                    out double ground);
                double h = (found ? ground : Data.heightMeters) + ClearanceM;

                Vector3 world = GeoUtils.GeoToUnity(_geo, at.latitude, at.longitude, h);
                Vector3 up = (GeoUtils.GeoToUnity(_geo, at.latitude, at.longitude, h + 1000.0) - world)
                    .normalized;

                stud.position = world;
                stud.rotation = Quaternion.LookRotation(up, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            }
        }

        /// <summary>Re-reads the record in place — a renamed, re-sided or re-aimed graphic.</summary>
        public void Refresh()
        {
            var def = ObstacleCatalog.Get(Kind);
            Color side = Data.team == nameof(Team.Enemy) ? GameConfig.RedTeam : GameConfig.BlueTeam;
            _colour = Color.Lerp(def.tint, side, 0.35f);

            if (_material != null) _material.color = _colour;
            if (_studMaterial != null) _studMaterial.color = _colour;
            if (_outline != null)
            {
                _outline.Data.team = Data.team;
                _outline.Data.colorHex = "#" + ColorUtility.ToHtmlStringRGB(_colour);
                _outline.RefreshStyle();
            }
            if (_caption != null)
            {
                _caption.text = string.IsNullOrEmpty(Data.label) ? def.name : Data.label;
                _caption.color = _colour;
            }
            _placed = false;
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_studMaterial != null) Destroy(_studMaterial);
            // Neither is a child of this object — see the _outline remarks — so
            // neither goes with it unless it is taken.
            if (_outline != null) Destroy(_outline.gameObject);
            if (_studRoot != null) Destroy(_studRoot.gameObject);
            if (_symbol != null)
            {
                var filter = _symbol.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Quad"))
                {
                    // The primitive's mesh is Unity's shared built-in one and is
                    // deliberately not destroyed — doing so would take the quad
                    // out from under every other primitive in the scene.
                }
            }
        }
    }
}
