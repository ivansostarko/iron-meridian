using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.Map
{
    /// <summary>
    /// Creates and owns the Cesium globe: georeference, world terrain,
    /// satellite imagery and 3D buildings. Handles the 2D/3D map switch.
    /// </summary>
    public class MapManager : MonoBehaviour
    {
        /// <summary>
        /// The map the current scene is running. Units need the view mode to
        /// decide how to travel, and they are spawned without a reference to
        /// the controller that owns it.
        /// </summary>
        public static MapManager Active { get; private set; }

        public CesiumGeoreference Georeference { get; private set; }
        public Cesium3DTileset Terrain { get; private set; }
        public Cesium3DTileset Buildings { get; private set; }
        /// <summary>The scene's key light. WeatherSystem drives it for time of day.</summary>
        public Light Sun { get; private set; }

        public ViewMode ViewMode { get; private set; } = ViewMode.Mode3D;
        public MapStyle Style { get; private set; } = MapStyle.Satellite;

        /// <summary>Whether the OSM Buildings tileset is showing. Independent of the 2D/3D view.</summary>
        public bool BuildingsVisible { get; private set; } = true;

        public event System.Action<bool> BuildingsVisibilityChanged;
        public event System.Action<ViewMode> ViewModeChanged;
        public event System.Action<MapStyle> StyleChanged;
        public event System.Action<string> LoadError;

        /// <summary>
        /// 0..1 estimate of how much of the terrain is loaded **for the current
        /// view** — what the loading screen reports. Note it can fall back
        /// toward 0 when the camera moves and new tiles are needed, which is why
        /// the loader only ever moves its bar forward.
        /// </summary>
        public float TerrainLoadProgress01 =>
            Terrain != null ? Mathf.Clamp01(Terrain.ComputeLoadProgress() / 100f) : 0f;

        CesiumIonRasterOverlay _overlay;
        CesiumUrlTemplateRasterOverlay _osmOverlay;
        string _token;

        public void Build(double lat, double lon)
        {
            Active = this;
            _token = CesiumTokenConfig.GetToken();

            Cesium3DTileset.OnCesium3DTilesetLoadFailure += OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure += OnOverlayLoadFailure;

            // Georeference origin at map centre
            var geoGo = new GameObject("CesiumGeoreference");
            Georeference = geoGo.AddComponent<CesiumGeoreference>();
            Georeference.SetOriginLongitudeLatitudeHeight(lon, lat, 0);

            // Cesium World Terrain (ion asset 1)
            Terrain = CreateTileset("CesiumWorldTerrain", 1, _token);

            // Imagery draped on the terrain — defaults to Bing Maps Aerial (ion asset 2)
            _overlay = Terrain.gameObject.AddComponent<CesiumIonRasterOverlay>();
            _overlay.ionAssetID = 2;
            _overlay.ionAccessToken = _token;

            // Cesium OSM Buildings (ion asset 96188) — visible in 3D mode only
            Buildings = CreateTileset("CesiumOSMBuildings", 96188, _token);

            // Sun light if the scene has none. Kept as a property because
            // WeatherSystem drives its angle, colour and intensity to set the
            // time of day — see docs/14-WEATHER.md.
            Sun = FindFirstObjectByType<Light>();
            if (Sun == null)
            {
                Sun = new GameObject("Sun").AddComponent<Light>();
                Sun.type = LightType.Directional;
                Sun.intensity = 1.35f;
                Sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            }
        }

        Cesium3DTileset CreateTileset(string name, long assetId, string token)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Georeference.transform, false);
            var ts = go.AddComponent<Cesium3DTileset>();
            ts.ionAssetID = assetId;
            ts.ionAccessToken = token;
            ts.maximumScreenSpaceError = 16f;
            ts.createPhysicsMeshes = true;   // needed for unit placement raycasts
            return ts;
        }

        /// <summary>
        /// Switches between the top-down 2D view and the tilted 3D one.
        ///
        /// Nothing else changes with the mode: buildings, effects, weather and
        /// unit labels all behave identically in both. The view is a camera
        /// choice, not a different world — buildings are governed by
        /// <see cref="SetBuildingsVisible"/> alone.
        /// </summary>
        public void SetViewMode(ViewMode mode)
        {
            ViewMode = mode;
            ViewModeChanged?.Invoke(mode);
        }

        public void ToggleViewMode() =>
            SetViewMode(ViewMode == ViewMode.Mode3D ? ViewMode.Mode2D : ViewMode.Mode3D);

        /// <summary>Cesium ion asset id backing each ion-hosted style; 0 means "not an ion overlay".</summary>
        static long IonAssetFor(MapStyle style) => style switch
        {
            MapStyle.Satellite => 2,          // Bing Maps Aerial
            MapStyle.SatelliteLabels => 3,    // Bing Maps Aerial with Labels
            MapStyle.Roads => 4,              // Bing Maps Road
            MapStyle.Sentinel2 => 3954,       // Sentinel-2 cloudless
            _ => 0
        };

        /// <summary>
        /// Switches the imagery draped on the terrain.
        ///
        /// Three kinds of style, handled separately: **Terrain** removes imagery
        /// entirely and shows bare shaded relief; **OpenStreetMap** comes from a
        /// public tile server through a URL-template overlay; everything else is
        /// a Cesium ion asset. Only one overlay is ever enabled — leaving both
        /// on would stack two imagery layers on the same tileset.
        /// </summary>
        public void SetMapStyle(MapStyle style)
        {
            Style = style;

            if (style == MapStyle.Terrain)
            {
                _overlay.enabled = false;
                if (_osmOverlay != null) _osmOverlay.enabled = false;
            }
            else if (style == MapStyle.OpenStreetMap)
            {
                _overlay.enabled = false;
                EnsureOsmOverlay();
                Reattach(_osmOverlay);
            }
            else
            {
                if (_osmOverlay != null) _osmOverlay.enabled = false;
                _overlay.ionAssetID = IonAssetFor(style);
                _overlay.ionAccessToken = _token;
                Reattach(_overlay);
            }

            StyleChanged?.Invoke(style);
        }

        /// <summary>
        /// Applies overlay property changes to the live tileset.
        ///
        /// This is what made the style switch look broken. A
        /// <see cref="CesiumRasterOverlay"/> hands its settings to the native
        /// tileset when it is *added* to it, and nothing re-reads them
        /// afterwards — so writing a new <c>ionAssetID</c> onto an overlay that
        /// is already enabled changes a C# field and nothing else, and the map
        /// carries on drawing the previous imagery. Enabling a disabled overlay
        /// does attach it (and so does pick the new id up), which is why the
        /// first switch away from a style appeared to work and every switch
        /// between two ion styles did not.
        ///
        /// <c>Refresh()</c> is Cesium's own remove-then-re-add; the enable path
        /// covers the case where the overlay was off and attaching it is enough.
        /// </summary>
        static void Reattach(CesiumRasterOverlay overlay)
        {
            if (overlay == null) return;
            if (!overlay.enabled) overlay.enabled = true;
            else overlay.Refresh();
        }

        /// <summary>
        /// Built on first use rather than at startup: most scenarios never pick
        /// OSM, and an unused overlay component still costs tile requests.
        /// </summary>
        void EnsureOsmOverlay()
        {
            if (_osmOverlay != null) return;

            _osmOverlay = Terrain.gameObject.AddComponent<CesiumUrlTemplateRasterOverlay>();
            _osmOverlay.templateUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
            _osmOverlay.minimumLevel = 0;
            // OSM's own tiles stop at z19; asking beyond that returns 404s.
            _osmOverlay.maximumLevel = 19;
            _osmOverlay.enabled = false;
        }

        /// <summary>
        /// Shows or hides the OSM Buildings tileset. Independent of the 2D/3D
        /// view: buildings used to vanish in 2D, which meant the two views were
        /// not showing the same world. The player decides now.
        /// </summary>
        public void SetBuildingsVisible(bool visible)
        {
            BuildingsVisible = visible;
            if (Buildings != null) Buildings.gameObject.SetActive(visible);
            BuildingsVisibilityChanged?.Invoke(visible);
        }

        /// <summary>
        /// Raycast the terrain under the mouse cursor. Skips unit colliders
        /// (icon billboards) so ordering a move near/behind a unit still hits
        /// the ground instead of stopping short at that unit's own icon quad.
        /// </summary>
        public bool RaycastGround(Camera cam, Vector2 screenPos, out Vector3 world)
        {
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 500000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<UnitActor>() != null) continue;
                world = hit.point;
                return true;
            }
            world = default;
            return false;
        }

        void OnTilesetLoadFailure(Cesium3DTilesetLoadFailureDetails details)
        {
            if (details.tileset != Terrain && details.tileset != Buildings) return;
            string name = details.tileset == Terrain ? "Terrain" : "OSM Buildings";
            string msg = $"[Cesium] {name} tileset failed to load ({details.httpStatusCode}): {details.message}";
            Debug.LogError(msg);
            LoadError?.Invoke($"{name} failed to load — check your Cesium ion token/asset access (HTTP {details.httpStatusCode}).");
        }

        void OnOverlayLoadFailure(CesiumRasterOverlayLoadFailureDetails details)
        {
            // The OSM overlay is reported too. It used to be skipped, so picking
            // a style whose tiles the server refused looked identical to picking
            // one that simply did not switch.
            bool ours = details.overlay == _overlay || (_osmOverlay != null && details.overlay == _osmOverlay);
            if (!ours) return;

            string styleName = Style.ToString();
            Debug.LogError($"[Cesium] {styleName} imagery failed to load ({details.httpStatusCode}): {details.message}");

            LoadError?.Invoke(details.overlay == _overlay
                ? $"{styleName} imagery failed (HTTP {details.httpStatusCode}) — your Cesium ion account may not have that asset. Try SATELLITE or TERRAIN."
                : $"{styleName} tiles failed (HTTP {details.httpStatusCode}) — the public OSM tile server refuses some clients. Try another style.");
        }

        void OnDestroy()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnOverlayLoadFailure;
            if (Active == this) Active = null;
        }
    }
}
