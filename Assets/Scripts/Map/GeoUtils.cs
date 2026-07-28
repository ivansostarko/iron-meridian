using Unity.Mathematics;
using UnityEngine;
using CesiumForUnity;

namespace IronMeridian.Map
{
    /// <summary>Geodetic helpers bridging WGS84 lat/lon and Unity world space.</summary>
    public static class GeoUtils
    {
        public const double EarthRadiusM = 6371000.0;

        /// <summary>Unity world position for a lat/lon/height (relative to the georeference).</summary>
        public static Vector3 GeoToUnity(CesiumGeoreference geo, double lat, double lon, double height)
        {
            double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(lon, lat, height));
            double3 unity = geo.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
        }

        /// <summary>lat/lon/height for a Unity world position.</summary>
        public static void UnityToGeo(CesiumGeoreference geo, Vector3 world,
            out double lat, out double lon, out double height)
        {
            double3 ecef = geo.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(world.x, world.y, world.z));
            double3 llh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            lon = llh.x; lat = llh.y; height = llh.z;
        }

        /// <summary>Great-circle distance in km.</summary>
        public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = math.radians(lat2 - lat1);
            double dLon = math.radians(lon2 - lon1);
            double a = math.sin(dLat / 2) * math.sin(dLat / 2) +
                       math.cos(math.radians(lat1)) * math.cos(math.radians(lat2)) *
                       math.sin(dLon / 2) * math.sin(dLon / 2);
            return EarthRadiusM / 1000.0 * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));
        }

        /// <summary>Initial bearing (deg, 0 = north) from point 1 to point 2.</summary>
        public static float BearingDeg(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = math.radians(lat1), p2 = math.radians(lat2);
            double dl = math.radians(lon2 - lon1);
            double y = math.sin(dl) * math.cos(p2);
            double x = math.cos(p1) * math.sin(p2) - math.sin(p1) * math.cos(p2) * math.cos(dl);
            return (float)((math.degrees(math.atan2(y, x)) + 360.0) % 360.0);
        }

        /// <summary>Destination point given a start, initial bearing (deg, 0 = north) and distance (km).</summary>
        public static void Destination(double lat, double lon, double bearingDeg, double distanceKm,
            out double destLat, out double destLon)
        {
            double delta = distanceKm / (EarthRadiusM / 1000.0);
            double theta = math.radians(bearingDeg);
            double p1 = math.radians(lat), l1 = math.radians(lon);

            double p2 = math.asin(math.sin(p1) * math.cos(delta) + math.cos(p1) * math.sin(delta) * math.cos(theta));
            double l2 = l1 + math.atan2(
                math.sin(theta) * math.sin(delta) * math.cos(p1),
                math.cos(delta) - math.sin(p1) * math.sin(p2));

            destLat = math.degrees(p2);
            destLon = math.degrees(l2);
        }

        /// <summary>
        /// Projects a geodetic point into a local east/north plane in km,
        /// centred on <paramref name="originLat"/>/<paramref name="originLon"/>.
        ///
        /// Tactical graphics (boundaries, bisectors, lateral ordering) are all
        /// planar geometry. Doing that maths directly on lat/lon degrees skews
        /// badly with latitude — a degree of longitude is ~111 km at the
        /// equator but ~71 km at Lyon — so sectors would come out lopsided.
        /// Over a battlefield-sized area this flat-earth approximation is
        /// accurate to well under a metre.
        /// </summary>
        public static void ToLocalKm(double originLat, double originLon,
            double lat, double lon, out double east, out double north)
        {
            double kmPerDegLat = EarthRadiusM / 1000.0 * math.PI / 180.0;
            double kmPerDegLon = kmPerDegLat * math.cos(math.radians(originLat));
            east = (lon - originLon) * kmPerDegLon;
            north = (lat - originLat) * kmPerDegLat;
        }

        /// <summary>Inverse of <see cref="ToLocalKm"/>.</summary>
        public static void FromLocalKm(double originLat, double originLon,
            double east, double north, out double lat, out double lon)
        {
            double kmPerDegLat = EarthRadiusM / 1000.0 * math.PI / 180.0;
            double kmPerDegLon = kmPerDegLat * math.cos(math.radians(originLat));
            lat = originLat + north / kmPerDegLat;
            lon = originLon + (math.abs(kmPerDegLon) < 1e-9 ? 0.0 : east / kmPerDegLon);
        }

        /// <summary>
        /// Terrain height (meters, WGS84 ellipsoid) at lat/lon by raycasting the
        /// Cesium physics meshes. Falls back to <paramref name="fallback"/>.
        /// </summary>
        public static double SampleTerrainHeight(CesiumGeoreference geo, double lat, double lon,
            double fallback = 250.0)
        {
            Vector3 high = GeoToUnity(geo, lat, lon, 9000.0);
            Vector3 low = GeoToUnity(geo, lat, lon, -500.0);
            Vector3 dir = (low - high).normalized;
            if (Physics.Raycast(high, dir, out RaycastHit hit, 20000f))
            {
                UnityToGeo(geo, hit.point, out _, out _, out double h);
                return h;
            }
            return fallback;
        }
    }
}
