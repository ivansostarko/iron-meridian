using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

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
        }

        void ApplyStyle()
        {
            var mat = new Material(Shader.Find("Unlit/Color"));
            bool boundary = Data.kind == LineKind.Boundary.ToString();
            if (boundary)
            {
                mat.color = GameConfig.BoundaryYellow;
                _lr.startWidth = _lr.endWidth = 55f;
            }
            else
            {
                mat.color = Data.team == Team.Enemy.ToString()
                    ? GameConfig.RedTeam : GameConfig.BlueTeam;
                _lr.startWidth = _lr.endWidth = 85f;
            }
            _lr.material = mat;
        }

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
