using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Mouse interaction with units on the map.
    ///   Left click  — select a unit (shows the info panel + selection ring)
    ///   Right click — order the selected unit to move to the clicked point
    ///   Esc         — deselect
    /// Disabled while a line-drawing tool is active.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        public UnitActor Selected { get; private set; }
        public System.Action<UnitActor> SelectionChanged;
        public System.Func<bool> InputBlocked;    // e.g. line tool active / over UI

        MapManager _map;
        Camera _cam;
        UnitActor _hover;

        public void Init(MapManager map, Camera cam) { _map = map; _cam = cam; }

        void Update()
        {
            if (_cam == null) return;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool blocked = overUI || (InputBlocked != null && InputBlocked());

            UpdateHover(blocked);

            if (blocked) return;

            if (Input.GetMouseButtonDown(0))
            {
                var unit = UnitUnderMouse();
                Select(unit);   // null = deselect
            }

            if (Input.GetMouseButtonDown(1) && Selected != null && Selected.IsAlive)
            {
                if (_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
                {
                    GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
                    Selected.Mover.MoveTo(lat, lon);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape)) Select(null);
        }

        void UpdateHover(bool blocked)
        {
            var unit = blocked ? null : UnitUnderMouse();
            if (unit != _hover)
            {
                if (_hover != null) _hover.SetHover(false);
                _hover = unit;
                if (_hover != null) _hover.SetHover(true);
            }
        }

        UnitActor UnitUnderMouse()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 500000f);
            UnitActor best = null;
            float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var actor = h.collider.GetComponentInParent<UnitActor>();
                if (actor != null && h.distance < bestDist)
                {
                    best = actor; bestDist = h.distance;
                }
            }
            return best;
        }

        public void Select(UnitActor unit)
        {
            if (Selected != null) Selected.SetSelected(false);
            Selected = unit;
            if (Selected != null) Selected.SetSelected(true);
            SelectionChanged?.Invoke(Selected);
        }
    }
}
