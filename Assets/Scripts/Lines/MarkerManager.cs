using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// Owns every task marker on the map (hold / guard / defend positions) and
    /// syncs them with save data — the point-graphic counterpart to
    /// <see cref="LineManager"/>.
    ///
    /// A marker belongs to the unit that was given the order, so re-tasking a
    /// unit replaces its marker rather than stacking a second one on the map,
    /// and a unit that leaves the map takes its marker with it.
    /// </summary>
    public class MarkerManager : MonoBehaviour
    {
        /// <summary>Seconds between sweeps for markers whose unit has gone.</summary>
        const float PruneSeconds = 2f;

        readonly List<TaskMarker> _markers = new List<TaskMarker>();
        CesiumGeoreference _geo;
        float _pruneTimer;

        public IReadOnlyList<TaskMarker> Markers => _markers;

        public void Init(CesiumGeoreference geo) => _geo = geo;

        /// <summary>Adds a marker, replacing whatever task the same unit already had.</summary>
        public TaskMarker Set(MapMarkerData data)
        {
            if (!string.IsNullOrEmpty(data.unitId)) RemoveForUnit(data.unitId);
            var marker = TaskMarker.Create(_geo, data);
            _markers.Add(marker);
            return marker;
        }

        public int RemoveForUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return 0;
            int removed = 0;
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                if (m == null) { _markers.RemoveAt(i); continue; }
                if (m.Data.unitId != unitId) continue;
                _markers.RemoveAt(i);
                Destroy(m.gameObject);
                removed++;
            }
            return removed;
        }

        public void Clear()
        {
            foreach (var m in _markers) if (m != null) Destroy(m.gameObject);
            _markers.Clear();
        }

        public List<MapMarkerData> Serialize()
        {
            var result = new List<MapMarkerData>();
            foreach (var m in _markers) if (m != null) result.Add(m.Data);
            return result;
        }

        /// <summary>
        /// Replaces every marker from save data. Call after the units are
        /// spawned — the prune sweep below drops markers whose unit is not on
        /// the map, and during a load that is briefly all of them.
        /// </summary>
        public void LoadFrom(List<MapMarkerData> data)
        {
            Clear();
            if (data == null) return;
            foreach (var d in data) _markers.Add(TaskMarker.Create(_geo, d));
        }

        void Update()
        {
            _pruneTimer -= Time.deltaTime;
            if (_pruneTimer > 0f) return;
            _pruneTimer = PruneSeconds;
            Prune();
        }

        /// <summary>
        /// Drops markers whose owning unit is no longer on the map. Swept on a
        /// timer rather than driven off <c>UnitRegistry.Changed</c>: that event
        /// fires for spawns and moves as well as removals, and during a load it
        /// fires while the map is still half-populated.
        /// </summary>
        void Prune()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                if (m == null) { _markers.RemoveAt(i); continue; }
                if (string.IsNullOrEmpty(m.Data.unitId)) continue;
                if (FindUnit(m.Data.unitId) != null) continue;
                _markers.RemoveAt(i);
                Destroy(m.gameObject);
            }
        }

        static UnitActor FindUnit(string instanceId)
        {
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.instanceId == instanceId) return u;
            return null;
        }
    }
}
