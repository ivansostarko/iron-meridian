using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.Map
{
    /// <summary>
    /// Creates and owns the Cesium globe: georeference, world terrain,
    /// satellite imagery and 3D buildings. Handles the 2D/3D map switch.
    /// </summary>
    public class MapManager : MonoBehaviour
    {
        public CesiumGeoreference Georeference { get; private set; }
        public Cesium3DTileset Terrain { get; private set; }
        public Cesium3DTileset Buildings { get; private set; }
        public ViewMode ViewMode { get; private set; } = ViewMode.Mode3D;

        public event System.Action<ViewMode> ViewModeChanged;

        public void Build(double lat, double lon)
        {
            string token = CesiumTokenConfig.GetToken();

            // Georeference origin at map centre
            var geoGo = new GameObject("CesiumGeoreference");
            Georeference = geoGo.AddComponent<CesiumGeoreference>();
            Georeference.SetOriginLongitudeLatitudeHeight(lon, lat, 0);

            // Cesium World Terrain (ion asset 1)
            Terrain = CreateTileset("CesiumWorldTerrain", 1, token);

            // Bing Maps Aerial imagery draped on the terrain (ion asset 2)
            var overlay = Terrain.gameObject.AddComponent<CesiumIonRasterOverlay>();
            overlay.ionAssetID = 2;
            overlay.ionAccessToken = token;

            // Cesium OSM Buildings (ion asset 96188) — visible in 3D mode only
            Buildings = CreateTileset("CesiumOSMBuildings", 96188, token);

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

        /// <summary>Raycast the terrain under the mouse cursor.</summary>
        public bool RaycastGround(Camera cam, Vector2 screenPos, out Vector3 world)
        {
            var ray = cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 500000f))
            {
                world = hit.point;
                return true;
            }
            world = default;
            return false;
        }
    }
}
