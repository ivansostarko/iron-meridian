using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.UI;

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
        /// <summary>Raised with the unit under the cursor, or null. Drives the hover tooltip.</summary>
        public System.Action<UnitActor> HoverChanged;
        /// <summary>
        /// Raised with a clickable control measure that was clicked on bare
        /// ground — currently only the automatic front line, which opens its
        /// settings panel. Units win: a line running under a formation is
        /// scenery compared with the formation standing on it.
        /// </summary>
        public System.Action<Lines.MapLine> LineClicked;

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

        // ------------------------------------------------------- ground picking

        /// <summary>
        /// The pending "click a point on the map" order, if any. One mechanism
        /// for every order that wants ground — the five movement tasks, the
        /// three defensive ones and both planner axes.
        ///
        /// It used to be one bespoke armed flag per order, and each new order
        /// meant another flag, another branch in <see cref="Update"/> and
        /// another pair of resolve/clear callbacks. A callback carries
        /// everything that differed.
        /// </summary>
        System.Action<double, double> _groundPick;
        string _groundPickCancelMessage;
        /// <summary>
        /// True for a pick that only makes sense while a battle is running —
        /// every order. False for the editor's own picks, which are authoring
        /// actions and belong to scenario mode. See <see cref="ArmGroundPick"/>.
        /// </summary>
        bool _groundPickBattleOnly = true;

        /// <summary>Raised when a ground pick is placed or cancelled, so the order bar can un-latch.</summary>
        public System.Action GroundPickResolved;

        /// <summary>
        /// Offered the screen position of a right-click before it becomes a move
        /// order. Return true to say the click was taken — the map object under
        /// the cursor opened its own menu — and the move order is skipped.
        ///
        /// A hook rather than the menu itself, because what is on the map is not
        /// this class's business: it knows about formations and control
        /// measures, and the answer also has to cover logistic sites and
        /// whatever is added next.
        /// </summary>
        public System.Func<Vector2, bool> ContextMenuRequested;

        /// <summary>The formation at a screen position, for whoever is answering that hook.</summary>
        public UnitActor UnitAt(Vector2 screenPos) => UnitUnderMouse(screenPos);

        /// <summary>True while the player is picking a point for an order.</summary>
        public bool IsPickingGround => _groundPick != null;

        /// <summary>
        /// Arms a ground pick: the next click on the map calls
        /// <paramref name="onPicked"/> with its geodetic position.
        ///
        /// **Two flavours, and the defaults are the order one.** An order needs
        /// something to order and only means anything in a battle, so by default
        /// a pick is refused with an empty selection and disarms itself the
        /// moment battle mode ends. The editor's own picks are neither — putting
        /// a mission's headquarters on the map has no selection behind it and is
        /// done with the clock stopped — so they turn both guards off. Getting
        /// this wrong is silent: the pick is simply never armed, or is disarmed
        /// on the next frame, and the click lands as an ordinary selection.
        /// </summary>
        /// <param name="requireSelection">False for a pick that acts on the map rather than on a formation.</param>
        /// <param name="battleOnly">False for an authoring pick, which belongs to scenario mode.</param>
        public void ArmGroundPick(System.Action<double, double> onPicked, string cancelMessage,
            bool requireSelection = true, bool battleOnly = true)
        {
            if (onPicked == null) return;
            if (requireSelection && _selection.Count == 0) return;
            _groundPick = onPicked;
            _groundPickCancelMessage = cancelMessage;
            _groundPickBattleOnly = battleOnly;
        }

        void ResolveGroundPick(string message)
        {
            _groundPick = null;
            if (message != null) Flash?.Invoke(message);
            GroundPickResolved?.Invoke();
        }

        void HandleGroundPick()
        {
            var pick = _groundPick;
            if (pick == null) return;

            if (!_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;      // stay armed: the tiles may be a second away
            }

            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
            ResolveGroundPick(null);
            pick(lat, lon);
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

        /// <summary>Raised with a piece of ground to attack, when the click hit no formation.</summary>
        public System.Action<double, double, AttackTask> AttackGroundPicked;

        /// <summary>
        /// Turns a click into an objective. Held in one place because every
        /// failure here needs a reason on screen — an armed order that silently
        /// does nothing when you click the wrong thing is the worst outcome.
        ///
        /// **A click on bare ground is an order, not a miss.** It used to be
        /// refused, on the grounds that an attack needs a target; but with fog
        /// of war on, the ground you most want to attack is exactly the ground
        /// you cannot see a counter on. Clicking terrain now attacks the *area*
        /// — everything hostile inside it, and anything that walks into it — and
        /// clicking a formation attacks that formation. Both end up in the same
        /// order; the difference is only how the objective was named.
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
            if (target != null)
            {
                if (target.State.TeamEnum == attacker.State.TeamEnum)
                {
                    Flash?.Invoke("That is one of yours. Pick a target on the opposing side.");
                    return;      // stay armed: a mis-click is not a cancellation
                }

                AttackTargetPicked?.Invoke(target, task);
                ResolveAttackOrder(null);
                return;
            }

            if (!_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
            AttackGroundPicked?.Invoke(lat, lon, task);
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

            // An armed ground pick turns the next map click into a point for
            // whatever order asked for one, instead of a selection change.
            if (_groundPick != null)
            {
                if (_groundPickBattleOnly && BattleRunning != null && !BattleRunning())
                {
                    ResolveGroundPick(null);
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape)) { ResolveGroundPick(_groundPickCancelMessage); return; }
                if (Input.GetMouseButtonDown(1)) { ResolveGroundPick(_groundPickCancelMessage); return; }
                if (!blocked && Input.GetMouseButtonDown(0)) HandleGroundPick();
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
                else if (_pendingClickUnit == null && LineUnderMouse() is Lines.MapLine line)
                {
                    // A click on bare ground that happens to land on a clickable
                    // control measure. The selection is dropped first: the panel
                    // this opens shares the right-hand edge with the unit info
                    // panel, and two of them cannot be there at once.
                    Select(null);
                    LineClicked?.Invoke(line);
                }
                else
                {
                    Select(_pendingClickUnit);
                }
                _dragging = false;
                _pressStartedOnMap = false;
                _pendingClickUnit = null;
            }

            // Shift + right-click extends the march instead of replacing it, the
            // convention every RTS uses. Shift is free on this button — it is
            // left-click that already means "add to selection".
            if (Input.GetMouseButtonDown(1))
            {
                // Right-click on *something* opens that thing's own menu; on
                // bare ground it is a move order. The handler decides which by
                // looking at what is under the cursor, and says whether it took
                // the click — see GameController.OpenMapContextMenu.
                //
                // Shift bypasses it deliberately: shift-right-click means "the
                // ground here, append it to the march", and having a counter in
                // the way should not turn that into a menu.
                bool taken = !shift && ContextMenuRequested != null &&
                             ContextMenuRequested(Input.mousePosition);
                if (!taken) HandleMoveOrder(append: shift);
            }

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

        void CancelRotation()
        {
            _rotating = false;
            EndAiming();
            for (int i = 0; i < _selection.Count && i < _headingsBeforeRotate.Count; i++)
                if (_selection[i] != null) _selection[i].SetHeading(_headingsBeforeRotate[i]);
            Flash?.Invoke("Facing cancelled.");
        }

        /// <summary>
        /// Right-click. Outside a battle this repositions counters; inside one it
        /// is a march order. <paramref name="append"/> adds the point to the end
        /// of the existing route instead of replacing it, which is how a route
        /// with several legs gets built.
        /// </summary>
        void HandleMoveOrder(bool append = false)
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

            // Appending only means anything to a unit that is marching. In the
            // editor a shift-right-click is just a reposition, which is the
            // least surprising thing it could do.
            if (!marching)
            {
                append = false;
                RecordPositionUndo();
            }

            if (_selection.Count == 1)
            {
                var only = _selection[0];
                if (only == null || !only.IsAlive) return;
                if (marching) Order(only, lat, lon, append);
                else only.SetPosition(lat, lon);
                ReportOrder(only, append);
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
                if (marching) Order(u, dLat, dLon, append);
                else u.SetPosition(dLat, dLon);
            }

            // The formation arrives when its slowest element does, so that is
            // the figure quoted — an average would promise an arrival time that
            // a third of the units cannot make.
            if (marching)
            {
                UnitActor slowest = null;
                foreach (var u in _selection)
                    if (u != null && u.IsAlive && u.Mover.IsMoving &&
                        (slowest == null || u.Mover.EtaGameSeconds > slowest.Mover.EtaGameSeconds))
                        slowest = u;

                string tail = slowest != null ? $" — slowest {MarchSummary(slowest)}" : "";
                Flash?.Invoke(append
                    ? $"Waypoint added for {n} units{tail}."
                    : $"{n} units marching{tail}.");
            }
        }

        static void Order(UnitActor unit, double lat, double lon, bool append)
        {
            if (append) unit.Mover.AddWaypoint(lat, lon);
            else unit.Mover.MoveTo(lat, lon);
        }

        /// <summary>
        /// Says what the route now looks like. Appending is invisible without
        /// it — the unit carries on exactly as before and the only thing that
        /// changed is a line further ahead than the eye is following.
        ///
        /// The distance and the arrival time are the point of the message.
        /// Marches now run at the formation's real speed against a real-time
        /// clock, so "go there" is a decision with a cost — a truck company and
        /// a foot battalion given the same objective are two very different
        /// orders, and the only way to see that before committing is to be told.
        /// </summary>
        void ReportOrder(UnitActor unit, bool append)
        {
            if (BattleRunning == null || !BattleRunning() || unit == null) return;

            int legs = unit.Mover.WaypointsRemaining;
            string plan = MarchSummary(unit);

            if (append)
                Flash?.Invoke(legs > 1
                    ? $"Waypoint added — {legs} legs, {plan}."
                    : $"Waypoint added — {plan}.");
            else
                Flash?.Invoke($"Marching — {plan}. Shift + right-click to add waypoints.");
        }

        /// <summary>"12.4 km at 45 km/h · ETA 16 min" for the unit's current route.</summary>
        static string MarchSummary(UnitActor unit)
        {
            double km = unit.Mover.RemainingKm;
            float eta = unit.Mover.EtaGameSeconds;
            return $"{km:0.#} km at {unit.Def.speedKmh:0} km/h · ETA {UnitMover.FormatDuration(eta)}";
        }

        void UpdateHover(bool blocked)
        {
            var unit = blocked ? null : UnitUnderMouse();
            if (unit != _hover)
            {
                if (_hover != null) _hover.SetHover(false);
                _hover = unit;
                if (_hover != null) _hover.SetHover(true);
                HoverChanged?.Invoke(_hover);
            }
        }

        // Reused across frames: UpdateHover() runs every frame, and
        // Physics.RaycastAll allocated a new array each time — against Cesium's
        // terrain colliders that is a per-frame garbage spike.
        static readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        UnitActor UnitUnderMouse(Vector2? screenPos = null)
        {
            var ray = _cam.ScreenPointToRay(screenPos ?? (Vector2)Input.mousePosition);
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

        /// <summary>
        /// The nearest clickable control measure under the cursor, or null.
        /// Uses the same buffer as the unit pick — this only runs on a release
        /// that hit no unit, so the two never compete for it.
        /// </summary>
        Lines.MapLine LineUnderMouse()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            int count = Physics.RaycastNonAlloc(ray, _hitBuffer, 500000f);
            Lines.MapLine best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hitBuffer[i];
                var line = h.collider.GetComponentInParent<Lines.MapLine>();
                if (line != null && line.Pickable && h.distance < bestDist)
                {
                    best = line; bestDist = h.distance;
                }
            }
            return best;
        }

        List<UnitActor> UnitsInScreenRect(Vector2 a, Vector2 b, Team team)
        {
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            var result = new List<UnitActor>();
            foreach (var u in UnitRegistry.All)
                if (InScreenRect(u, team, min, max)) result.Add(u);
            return result;
        }

        /// <summary>
        /// How many units the marquee currently covers. Separate from
        /// <see cref="UnitsInScreenRect"/> because the readout runs every frame
        /// of a drag and the list does not — allocating one per frame to
        /// immediately throw it away is the kind of thing that shows up as
        /// stutter with a full order of battle deployed.
        /// </summary>
        int CountUnitsInScreenRect(Vector2 a, Vector2 b, Team team)
        {
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            int n = 0;
            foreach (var u in UnitRegistry.All)
                if (InScreenRect(u, team, min, max)) n++;
            return n;
        }

        bool InScreenRect(UnitActor u, Team team, Vector2 min, Vector2 max)
        {
            if (u == null || !u.IsAlive || u.State.TeamEnum != team) return false;
            // A formation the player cannot see is not one they can rubber-band
            // select — the fog withholds the position, so it must withhold the
            // click target too.
            if (u.HiddenByFog) return false;
            Vector3 sp = _cam.WorldToScreenPoint(u.transform.position);
            if (sp.z < 0) return false;
            return sp.x >= min.x && sp.x <= max.x && sp.y >= min.y && sp.y <= max.y;
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

        /// <summary>Edge thickness of the marquee, in canvas units.</summary>
        const float BoxBorderPx = 2f;
        static readonly Color BoxFill = new Color(0.95f, 0.85f, 0.35f, 0.12f);
        static readonly Color BoxEdge = new Color(1.00f, 0.93f, 0.55f, 0.95f);

        Text _boxCount;

        void EnsureBoxVisual()
        {
            if (_boxImage != null) return;
            var go = new GameObject("SelectionBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);
            _boxImage = go.GetComponent<Image>();
            _boxImage.color = BoxFill;
            _boxImage.raycastTarget = false;
            _boxRect = (RectTransform)go.transform;

            // A translucent wash alone is hard to place precisely against
            // photographic terrain — the eye needs an edge to aim with. Four
            // stretched strips rather than a sprite so there is still no image
            // asset to ship.
            BoxEdgeStrip("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, BoxBorderPx));
            BoxEdgeStrip("Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, BoxBorderPx));
            BoxEdgeStrip("Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(BoxBorderPx, 0));
            BoxEdgeStrip("Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(BoxBorderPx, 0));

            // Running count, so the player knows what the marquee has caught
            // before releasing rather than after.
            _boxCount = UIFactory.CreateText(_boxRect, "", 14, BoxEdge, TextAnchor.LowerLeft, FontStyle.Bold);
            _boxCount.raycastTarget = false;
            var countRect = _boxCount.rectTransform;
            countRect.anchorMin = countRect.anchorMax = new Vector2(0, 1);
            countRect.pivot = new Vector2(0, 0);
            countRect.anchoredPosition = new Vector2(2f, 4f);
            countRect.sizeDelta = new Vector2(160f, 20f);

            go.SetActive(false);
        }

        void BoxEdgeStrip(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_boxRect, false);
            var image = go.GetComponent<Image>();
            image.color = BoxEdge;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
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

            // Preview the catch. Hovering every boxed unit as well would fight
            // the cursor's own hover highlight, so the count states it instead.
            int n = CountUnitsInScreenRect(startScreen, curScreen, Team.User);
            _boxCount.text = n == 0 ? "" : (n == 1 ? "1 unit" : $"{n} units");
        }

        void HideBoxVisual()
        {
            if (_boxImage != null) _boxImage.gameObject.SetActive(false);
        }
    }
}
