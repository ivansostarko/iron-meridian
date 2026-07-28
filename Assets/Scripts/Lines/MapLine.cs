using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// A rendered polyline on the globe: sector boundary or defensive line.
    /// In 3D mode vertices are clamped to the terrain; in 2D mode the line is
    /// drawn at a constant height for a clean flat-map look.
    /// </summary>
    public class MapLine : MonoBehaviour
    {
        public MapLineData Data { get; private set; }

        CesiumGeoreference _geo;
        LineRenderer _lr;

        public static MapLine Create(CesiumGeoreference geo, MapLineData data)
        {
            var go = new GameObject($"Line_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);
            var line = go.AddComponent<MapLine>();
            line.Build(geo, data);
            return line;
        }

        void Build(CesiumGeoreference geo, MapLineData data)
        {
            _geo = geo; Data = data;
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.textureMode = LineTextureMode.Tile;
            _lr.alignment = LineAlignment.View;
            _lr.numCapVertices = 4;
            _lr.numCornerVertices = 4;
            ApplyStyle();
            Rebuild();

            // LineRenderer positions are absolute world-space, but Cesium
            // periodically re-origins the georeference for floating-point
            // precision as the camera roams — without this, drawn lines
            // drift away from the terrain the moment that happens.
            _geo.changed += Rebuild;
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= Rebuild;
        }

        void ApplyStyle()
        {
            System.Enum.TryParse(Data.kind, out LineKind kind);

            Color color;
            float width;
            switch (kind)
            {
                case LineKind.LateralBoundary:
                case LineKind.RearBoundary:
                    // Boundaries belong to the formation whose AO they bound,
                    // so they take the owning side's colour.
                    color = SideColor(GameConfig.BoundaryYellow);
                    width = 45f;
                    break;

                case LineKind.Feba:
                    color = SideColor(GameConfig.BlueTeam);
                    width = 80f;
                    break;

                case LineKind.PhaseLine:
                    color = GameConfig.BoundaryYellow;
                    width = 45f;
                    break;

                case LineKind.Boundary:
                    color = GameConfig.BoundaryYellow;
                    width = 55f;
                    break;

                default:                                   // DefensiveLine
                    color = SideColor(GameConfig.BlueTeam);
                    width = 85f;
                    break;
            }

            // FM 101-5-1 / SS0529: actual control measures are solid, planned
            // or on-order ones are broken.
            var mat = Data.planned
                ? RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color, 64, 0.5f))
                : RuntimeMaterials.UnlitColor(color);
            if (Data.planned) mat.color = color;

            _lr.startWidth = _lr.endWidth = width;
            _lr.material = mat;
        }

        Color SideColor(Color fallback) =>
            Data.team == Team.Enemy.ToString() ? GameConfig.RedTeam
            : Data.team == Team.User.ToString() ? GameConfig.BlueTeam
            : fallback;

        /// <summary>Recompute world positions from geodetic points.</summary>
        public void Rebuild()
        {
            var pts = Data.points;
            _lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                double h = Data.is3D
                    ? GeoUtils.SampleTerrainHeight(_geo, pts[i].latitude, pts[i].longitude, 250) + 25.0
                    : 450.0;   // flat 2D height band
                pts[i].heightMeters = h;
                _lr.SetPosition(i, GeoUtils.GeoToUnity(_geo, pts[i].latitude, pts[i].longitude, h));
            }
        }

        public void SetPoints(List<GeoPoint> points)
        {
            Data.points = points;
            Rebuild();
        }

        public void Set3D(bool is3D)
        {
            Data.is3D = is3D;
            Rebuild();
        }
    }
}
