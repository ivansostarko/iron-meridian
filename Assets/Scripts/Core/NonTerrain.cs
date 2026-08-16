using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Marks a collider that is **not ground**, so terrain sampling steps over
    /// it — see <see cref="Map.GeoUtils.TrySampleTerrainHeight"/>.
    ///
    /// **Why this exists.** Height on this map is measured by raycasting
    /// straight down and taking the first thing hit. That is correct as long as
    /// the only colliders in the world are Cesium's terrain, and it is quietly
    /// catastrophic the moment one of the game's own graphics has a collider of
    /// its own: the ray hits the graphic, reports its height as the ground, and
    /// whatever is being clamped is placed a clearance *above its own previous
    /// position*. Do that on a cadence and the object climbs — the automatic
    /// front line, whose click ribbon is a 400 m-wide collider lying along it,
    /// rose thirty metres every three seconds until it was hanging in the sky.
    ///
    /// A marker component rather than a layer, because layers are a project
    /// asset and this project builds its scenes from code; and rather than a
    /// whitelist of "only Cesium tiles count as ground", because a whitelist
    /// that stops matching — a Cesium version that re-parents its tiles — fails
    /// by putting *everything* on the map at its fallback height, which is a far
    /// worse failure than the one being fixed.
    ///
    /// Anything that adds a collider for picking, hovering or triggering, and is
    /// not itself terrain, should carry one of these.
    /// </summary>
    public class NonTerrain : MonoBehaviour
    {
        /// <summary>Adds the marker to a collider's object, once.</summary>
        public static void Mark(GameObject go)
        {
            if (go != null && go.GetComponent<NonTerrain>() == null) go.AddComponent<NonTerrain>();
        }
    }
}
