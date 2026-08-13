using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Mouse interaction with units on the map.
    ///   Left click        — select a single unit (clears any prior selection)
    ///   Left click + drag — box-select every friendly unit inside the rectangle
    ///   Shift + click     — add/remove one unit from the current selection
    ///   Shift + drag       — add every boxed friendly unit to the current selection
    ///   Right click       — order the whole selection to move to the clicked point
    ///                        (spread into a small circular formation when >1 unit)
    ///   Esc               — clear the selection
    /// Disabled while a line-drawing tool is active.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        public IReadOnlyList<UnitActor> Selection => _selection;
        public UnitActor Selected => _selection.Count > 0 ? _selection[0] : null;   // primary unit
        public System.Action<IReadOnlyList<UnitActor>> SelectionChanged;
        public System.Func<bool> InputBlocked;    // e.g. line tool active / over UI
        public System.Action<string> Flash;       // user feedback (e.g. failed move order)
        /// <summary>True while a battle is running: right-click orders a march instead of repositioning.</summary>
        public System.Func<bool> BattleRunning;

        const float DragThresholdPx = 6f;

        readonly List<UnitActor> _selection = new List<UnitActor>();
        MapManager _map;
        Camera _cam;
        Canvas _canvas;
        UnitActor _hover;

        Vector2 _dragStartScreen;
        bool _dragging;
        bool _pressStartedOnMap;
        UnitActor _pendingClickUnit;

        bool _rotating;
        readonly List<float> _headingsBeforeRotate = new List<float>();

        bool _moveArmed;
        /// <summary>Raised when an armed move order is placed or cancelled.</summary>
        public System.Action MoveOrderResolved;

        /// <summary>Arms the action bar's Move order: the next map click is the destination.</summary>
        public void ArmMoveOrder()
        {
            if (_selection.Count == 0) return;
            _moveArmed = true;
        }

        // ------------------------------------------------------- attack targeting

        AttackTask? _attackArmed;

        /// <summary>Raised with the chosen target once an armed attack order is placed.</summary>
        public System.Action<UnitActor, AttackTask> AttackTargetPicked;
        /// <summary>Raised when an armed attack order is placed or cancelled.</summary>
        public System.Action AttackOrderResolved;

        /// <summary>
        /// Arms an offensive task: the next click on the map picks the target.
        /// Unlike a move order this wants a *unit*, not a point on the ground —
        /// clicking bare terrain is a miss, not an order to attack that spot.
        /// </summary>
        public void ArmAttackOrder(AttackTask task)
        {
            if (_selection.Count == 0) return;
            _attackArmed = task;
        }

        /// <summary>True while the player is picking an attack target.</summary>
        public bool IsPickingTarget => _attackArmed.HasValue;

        void ResolveAttackOrder(string message)
        {
            _attackArmed = null;
            if (message != null) Flash?.Invoke(message);
            AttackOrderResolved?.Invoke();
        }

        // ------------------------------------------------------- recon targeting

        ReconTask? _reconArmed;

        /// <summary>Raised with the chosen ground point once an armed recon task is placed.</summary>
        public System.Action<double, double, ReconTask> ReconPointPicked;
        /// <summary>Raised when an armed recon task is placed or cancelled.</summary>
        public System.Action ReconOrderResolved;

        /// <summary>
        /// Arms a reconnaissance task: the next click on the map is the
        /// objective. Unlike an attack this wants a *point*, not a unit — the
        /// whole purpose is to look at ground you cannot currently see, which by
        /// definition has nothing clickable on it.
        /// </summary>
        public void ArmReconOrder(ReconTask task)
        {
            if (_selection.Count == 0) return;
            _reconArmed = task;
        }

        void ResolveReconOrder(string message)
        {
            _reconArmed = null;
            if (message != null) Flash?.Invoke(message);
            ReconOrderResolved?.Invoke();
        }

        void HandleReconPoint(ReconTask task)
        {
            var unit = Selected;
            if (unit == null || !unit.IsAlive)
            {
                ResolveReconOrder("Nothing selected to send.");
                return;
            }
            if (!_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;      // stay armed: the tiles may be a second away
            }

            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
            ReconPointPicked?.Invoke(lat, lon, task);
            ResolveReconOrder(null);
        }

        /// <summary>
        /// Turns a click into a target. Held in one place because every failure
        /// here needs a reason on screen — an armed order that silently does
        /// nothing when you click the wrong thing is the worst outcome.
        /// </summary>
        void HandleAttackTarget(AttackTask task)
        {
            var attacker = Selected;
            if (attacker == null || !attacker.IsAlive)
            {
                ResolveAttackOrder("Nothing selected to attack with.");
                return;
            }

            var target = UnitUnderMouse();
            if (target == null)
            {
                Flash?.Invoke("Click an enemy formation — attack orders need a target, not a point on the map.");
                return;      // stay armed: a miss is not a cancellation
            }
            if (target.State.TeamEnum == attacker.State.TeamEnum)
            {
                Flash?.Invoke("That is one of yours. Pick a target on the opposing side.");
                return;
            }

            AttackTargetPicked?.Invoke(target, task);
            ResolveAttackOrder(null);
        }
        Image _boxImage;
        RectTransform _boxRect;

        public void Init(MapManager map, Camera cam, Canvas canvas)
        {
            _map = map; _cam = cam; _canvas = canvas;
        }

        void Update()
        {
            if (_cam == null) return;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool toolBlocked = InputBlocked != null && InputBlocked();
            bool blocked = overUI || toolBlocked;

            // Facing mode owns the mouse while it is active, so it is handled
            // before selection/orders get a look at the input.
            if (_rotating) { UpdateRotation(overUI); return; }

            // An armed Move order turns the next map click into a destination
            // instead of a selection change.
            if (_moveArmed)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { ResolveMoveOrder("Move order cancelled."); return; }
                if (!blocked && Input.GetMouseButtonDown(0))
                {
                    HandleMoveOrder();
                    ResolveMoveOrder(null);
                }
                return;
            }

            // An armed attack task turns the next click into a target pick.
            // Kept armed through a miss, so a slightly-off click costs one more
            // click rather than the whole order.
            if (_attackArmed.HasValue)
            {
                // Leaving battle mid-pick disarms: there is nothing to attack
                // with in the scenario editor, and the order bar has gone.
                if (BattleRunning != null && !BattleRunning()) { ResolveAttackOrder(null); return; }
                if (Input.GetKeyDown(KeyCode.Escape)) { ResolveAttackOrder("Attack order cancelled."); return; }
                if (Input.GetMouseButtonDown(1)) { ResolveAttackOrder("Attack order cancelled."); return; }
                if (!blocked && Input.GetMouseButtonDown(0)) HandleAttackTarget(_attackArmed.Value);
                UpdateHover(blocked);
                return;
            }

            // An armed recon task turns the next click into an objective on the
            // ground. Also kept armed through a miss — terrain that has not
            // streamed in yet is a reason to try again, not to lose the order.
            if (_reconArmed.HasValue)
            {
                if (BattleRunning != null && !BattleRunning()) { ResolveReconOrder(null); return; }
                if (Input.GetKeyDown(KeyCode.Escape)) { ResolveReconOrder("Recon task cancelled."); return; }
                if (Input.GetMouseButtonDown(1)) { ResolveReconOrder("Recon task cancelled."); return; }
                if (!blocked && Input.GetMouseButtonDown(0)) HandleReconPoint(_reconArmed.Value);
                return;
            }
            // Bare C only — Ctrl+C is copy, and would otherwise drop straight
            // into facing mode as well.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (!ctrl && !toolBlocked && _selection.Count > 0 && Input.GetKeyDown(KeyCode.C))
            {
                BeginRotation();
                return;
            }

            UpdateHover(blocked);

            if (blocked)
            {
                // A press that starts over the palette or HUD must not become a
                // map click when it is released over the terrain — dropping a
                // dragged unit used to re-apply whatever was clicked before it.
                if (Input.GetMouseButtonDown(0)) { _pressStartedOnMap = false; _pendingClickUnit = null; }
                if (_dragging) { _dragging = false; HideBoxVisual(); }
                return;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetMouseButtonDown(0))
            {
                _pressStartedOnMap = true;
                _dragStartScreen = Input.mousePosition;
                _dragging = false;
                _pendingClickUnit = UnitUnderMouse();
            }

            if (_pressStartedOnMap && Input.GetMouseButton(0) && !_dragging &&
                Vector2.Distance(_dragStartScreen, Input.mousePosition) > DragThresholdPx)
            {
                _dragging = true;
            }

            if (_dragging) UpdateBoxVisual(_dragStartScreen, Input.mousePosition);

            if (Input.GetMouseButtonUp(0))
            {
                if (!_pressStartedOnMap)
                {
                    HideBoxVisual();     // release from a UI-originated drag: ignore
                }
                else if (_dragging)
                {
                    var boxed = UnitsInScreenRect(_dragStartScreen, Input.mousePosition, Team.User);
                    ApplyBoxSelection(boxed, shift);
                    HideBoxVisual();
                }
                else if (shift)
                {
                    ToggleInSelection(_pendingClickUnit);
                }
                else
                {
                    Select(_pendingClickUnit);
                }
                _dragging = false;
                _pressStartedOnMap = false;
                _pendingClickUnit = null;
            }

            if (Input.GetMouseButtonDown(1)) HandleMoveOrder();

            if (Input.GetKeyDown(KeyCode.Escape)) Select(null);
        }

        // ------------------------------------------------------- facing (C)

        /// <summary>True while the player is aiming the selection's facing.</summary>
        public bool IsRotating => _rotating;

        void BeginRotation()
        {
            _rotating = true;
            _headingsBeforeRotate.Clear();
            foreach (var u in _selection)
            {
                _headingsBeforeRotate.Add(u != null ? u.State.headingDeg : 0f);
                if (u != null) u.SetAiming(true);
            }

            Flash?.Invoke(_selection.Count > 1
                ? $"Facing {_selection.Count} units — move the mouse to aim, LMB/Enter confirms, Esc cancels."
                : "Facing — move the mouse to aim, LMB/Enter confirms, Esc cancels.");
        }

        void UpdateRotation(bool overUI)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelRotation(); return; }
            if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetMouseButtonDown(0))
            {
                _rotating = false;
                EndAiming();
                RecordHeadingUndo();
                Flash?.Invoke("Facing set.");
                return;
            }

            // Aiming needs a point on the ground; over UI or off the streamed
            // terrain there is nothing to aim at, so hold the last facing.
            if (overUI) return;
            if (!_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world)) return;
            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);

            foreach (var u in _selection)
            {
                if (u == null || !u.IsAlive) continue;
                // Every unit in the selection turns to face the same point,
                // so a line of units fans out to cover it.
                u.SetHeading(GeoUtils.BearingDeg(u.State.latitude, u.State.longitude, lat, lon));
            }

            // Live bearing readout, taken from the primary unit. The heading
            // arrows on the map show the direction; this states the number, so
            // an axis can be set precisely instead of by eye.
            var lead = Selected;
            if (lead != null)
                Flash?.Invoke($"Facing {lead.State.headingDeg:000}° — LMB/Enter confirms, Esc cancels.");
        }

        /// <summary>Drops the aiming highlight from every unit that was being turned.</summary>
        void EndAiming()
        {
            foreach (var u in _selection) if (u != null) u.SetAiming(false);
        }

        /// <summary>
        /// Snapshots where the selection currently stands so an editor-mode
        /// reposition can be taken back. Battle marches are not recorded — they
        /// are orders playing out, not edits.
        /// </summary>
        void RecordPositionUndo()
        {
            var units = new List<UnitActor>();
            var lats = new List<double>();
            var lons = new List<double>();
            foreach (var u in _selection)
            {
                if (u == null || !u.IsAlive) continue;
                units.Add(u);
                lats.Add(u.State.latitude);
                lons.Add(u.State.longitude);
            }
            if (units.Count == 0) return;

            EditHistory.Push(units.Count > 1 ? $"move {units.Count} units" : "move", () =>
            {
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null) units[i].SetPosition(lats[i], lons[i]);
            });
        }

        /// <summary>Makes the facing change reversible with Ctrl+Z.</summary>
        void RecordHeadingUndo()
        {
            var units = new List<UnitActor>(_selection);
            var before = new List<float>(_headingsBeforeRotate);
            if (units.Count == 0) return;

            EditHistory.Push(units.Count > 1 ? $"facing of {units.Count} units" : "facing", () =>
            {
                for (int i = 0; i < units.Count && i < before.Count; i++)
                    if (units[i] != null) units[i].SetHeading(before[i]);
            });
        }

        void ResolveMoveOrder(string message)
        {
            _moveArmed = false;
            if (message != null) Flash?.Invoke(message);
            MoveOrderResolved?.Invoke();
        }

        void CancelRotation()
        {
            _rotating = false;
            EndAiming();
            for (int i = 0; i < _selection.Count && i < _headingsBeforeRotate.Count; i++)
                if (_selection[i] != null) _selection[i].SetHeading(_headingsBeforeRotate[i]);
            Flash?.Invoke("Facing cancelled.");
        }

        void HandleMoveOrder()
        {
            if (_selection.Count == 0)
            {
                Flash?.Invoke("Select a unit first (left-click it) before ordering a move.");
                return;
            }
            if (!_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                Flash?.Invoke("Terrain not loaded here yet — try again in a moment.");
                return;
            }
            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);

            // Outside a battle the map editor is placing counters, so the unit
            // is repositioned instantly; once the battle is running the same
            // right-click is a march order and the unit travels there.
            bool marching = BattleRunning != null && BattleRunning();
            if (!marching) RecordPositionUndo();

            if (_selection.Count == 1)
            {
                var only = _selection[0];
                if (only == null || !only.IsAlive) return;
                if (marching) only.Mover.MoveTo(lat, lon);
                else only.SetPosition(lat, lon);
                return;
            }

            // Spread a multi-unit order into a small circular formation so units
            // don't all stack on the exact same point.
            int n = _selection.Count;
            double radiusKm = System.Math.Max(0.02, 0.03 * System.Math.Sqrt(n));
            for (int i = 0; i < n; i++)
            {
                var u = _selection[i];
                if (u == null || !u.IsAlive) continue;
                double bearing = i * 360.0 / n;
                GeoUtils.Destination(lat, lon, bearing, radiusKm, out double dLat, out double dLon);
                if (marching) u.Mover.MoveTo(dLat, dLon);
                else u.SetPosition(dLat, dLon);
            }
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

        // Reused across frames: UpdateHover() runs every frame, and
        // Physics.RaycastAll allocated a new array each time — against Cesium's
        // terrain colliders that is a per-frame garbage spike.
        static readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        UnitActor UnitUnderMouse()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            int count = Physics.RaycastNonAlloc(ray, _hitBuffer, 500000f);
            UnitActor best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hitBuffer[i];
                var actor = h.collider.GetComponentInParent<UnitActor>();
                if (actor != null && h.distance < bestDist)
                {
                    best = actor; bestDist = h.distance;
                }
            }
            return best;
        }

        List<UnitActor> UnitsInScreenRect(Vector2 a, Vector2 b, Team team)
        {
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            var result = new List<UnitActor>();
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.State.TeamEnum != team) continue;
                Vector3 sp = _cam.WorldToScreenPoint(u.transform.position);
                if (sp.z < 0) continue;
                if (sp.x >= min.x && sp.x <= max.x && sp.y >= min.y && sp.y <= max.y)
                    result.Add(u);
            }
            return result;
        }

        // ------------------------------------------------------- selection
        public void Select(UnitActor unit) =>
            ApplySelection(unit != null ? new List<UnitActor> { unit } : new List<UnitActor>());

        /// <summary>Programmatically replace the selection (e.g. recalling a named group).</summary>
        public void SetSelection(IEnumerable<UnitActor> units) => ApplySelection(new List<UnitActor>(units));

        void ToggleInSelection(UnitActor unit)
        {
            if (unit == null) return;
            var list = new List<UnitActor>(_selection);
            if (list.Contains(unit)) list.Remove(unit);
            else list.Add(unit);
            ApplySelection(list);
        }

        void ApplyBoxSelection(List<UnitActor> boxed, bool add)
        {
            if (!add) { ApplySelection(boxed); return; }
            var list = new List<UnitActor>(_selection);
            foreach (var u in boxed) if (!list.Contains(u)) list.Add(u);
            ApplySelection(list);
        }

        void ApplySelection(List<UnitActor> newSelection)
        {
            // An armed order belongs to the unit that was selected when it was
            // armed. Changing the selection from a panel while a target is being
            // picked would otherwise issue the order from a different formation.
            if (_attackArmed.HasValue) ResolveAttackOrder(null);
            if (_reconArmed.HasValue) ResolveReconOrder(null);

            foreach (var u in _selection) if (u != null && !newSelection.Contains(u)) u.SetSelected(false);
            foreach (var u in newSelection) if (u != null) u.SetSelected(true);
            _selection.Clear();
            _selection.AddRange(newSelection);
            // Pass a snapshot, not the live list — GroupPanelUI holds onto this
            // reference until "Create Group" is clicked, which must not silently
            // change if the selection is updated again in the meantime.
            SelectionChanged?.Invoke(new List<UnitActor>(_selection));
        }

        // ------------------------------------------------------- box visual
        void EnsureBoxVisual()
        {
            if (_boxImage != null) return;
            var go = new GameObject("SelectionBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);
            _boxImage = go.GetComponent<Image>();
            _boxImage.color = new Color(0.95f, 0.85f, 0.35f, 0.15f);
            _boxImage.raycastTarget = false;
            _boxRect = (RectTransform)go.transform;
            go.SetActive(false);
        }

        void UpdateBoxVisual(Vector2 startScreen, Vector2 curScreen)
        {
            EnsureBoxVisual();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, startScreen, _canvas.worldCamera, out Vector2 a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, curScreen, _canvas.worldCamera, out Vector2 b);
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            _boxRect.anchoredPosition = (min + max) * 0.5f;
            _boxRect.sizeDelta = max - min;
            _boxImage.gameObject.SetActive(true);
        }

        void HideBoxVisual()
        {
            if (_boxImage != null) _boxImage.gameObject.SetActive(false);
        }
    }
}
