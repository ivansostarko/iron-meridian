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
        public ViewMode ViewMode { get; private set; } = ViewMode.Mode3D;
        public MapStyle Style { get; private set; } = MapStyle.Satellite;

        public event System.Action<ViewMode> ViewModeChanged;
        public event System.Action<MapStyle> StyleChanged;
        public event System.Action<string> LoadError;

        CesiumIonRasterOverlay _overlay;
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

            // Sun light if the scene has none
            if (FindFirstObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.35f;
                sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
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

        public void SetViewMode(ViewMode mode)
        {
            ViewMode = mode;
            // In 2D (top-down) mode buildings only add noise and cost.
            if (Buildings != null) Buildings.gameObject.SetActive(mode == ViewMode.Mode3D);
            ViewModeChanged?.Invoke(mode);
        }

        public void ToggleViewMode() =>
            SetViewMode(ViewMode == ViewMode.Mode3D ? ViewMode.Mode2D : ViewMode.Mode3D);

        /// <summary>Switches the imagery draped on the terrain (Satellite/Roads), or removes it (Terrain).</summary>
        public void SetMapStyle(MapStyle style)
        {
            Style = style;
            if (style == MapStyle.Terrain)
            {
                _overlay.enabled = false;
            }
            else
            {
                _overlay.ionAssetID = style == MapStyle.Satellite ? 2 : 4; // 4 = Bing Maps Road
                _overlay.ionAccessToken = _token;
                _overlay.enabled = true;
            }
            StyleChanged?.Invoke(style);
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
            if (details.overlay != _overlay) return;
            string styleName = Style.ToString();
            string msg = $"[Cesium] {styleName} imagery failed to load ({details.httpStatusCode}): {details.message}";
            Debug.LogError(msg);
            LoadError?.Invoke($"{styleName} imagery failed to load — check your Cesium ion token/asset access (HTTP {details.httpStatusCode}).");
        }

        void OnDestroy()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnOverlayLoadFailure;
            if (Active == this) Active = null;
        }
    }
}
