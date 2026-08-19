using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — the strike allowance readout and the drag-to-deploy flow that is the editor's core loop.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: strike allowance, drag to deploy.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // --------------------------------------------------- strike allowance

        /// <summary>Every "STRIKES REMAINING" readout on the rail, repainted together.</summary>
        /// <summary>
        /// Every per-system "missions left" readout on a fire button, with the
        /// budget key and limit it reports. Repainted together whenever a
        /// mission is spent — see <see cref="RefreshStrikeBudget"/>.
        /// </summary>
        readonly List<(Text label, string key, int limit)> _budgetLabels =
            new List<(Text, string, int)>();

        /// <summary>
        /// The shared strike allowance, shown at the head of each fire menu.
        ///
        /// It is on **all three** of them, and on the missile board, because the
        /// pool is shared: a player who spends it on artillery has spent it on
        /// air strikes too, and a counter that appeared only in the menu being
        /// used would let them find that out the hard way. See
        /// <see cref="StrikeBudget"/>.
        /// </summary>
        /// <summary>
        /// Names the right-hand column of the fire buttons below it.
        ///
        /// It used to be the allowance itself — one shared count of ninety-nine
        /// for every strike in the game. The count is now attached to each
        /// system (see <see cref="StrikeBudget"/>), so what this row does is
        /// say what the second figure on every button beneath it means. A
        /// column of bare "4 / 6"s with nothing to read them against is the
        /// kind of number a player learns to ignore.
        /// </summary>
        void StrikeBudgetRow(RectTransform content, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "AllowanceLegend",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 28));

            var name = UIFactory.CreateText(frame, "MISSIONS AVAILABLE", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(InnerWidth - 110f, 14));

            var note = UIFactory.CreateText(frame, "PER SYSTEM", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            note.raycastTarget = false;
            UIFactory.Place(note.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(94, 16));
        }

        /// <summary>
        /// The "missions left" figure on a fire button, under its radius. Every
        /// fire menu builds its right-hand column the same way, so a player
        /// reads the same two numbers in the same place whichever one is open.
        /// </summary>
        Text AllowanceLabel(RectTransform frame, string key, int limit)
        {
            var label = UIFactory.CreateText(frame, "", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            label.raycastTarget = false;
            UIFactory.Place(label.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, -9),
                new Vector2(56, 14));

            _budgetLabels.Add((label, key, limit));
            return label;
        }

        /// <summary>Repaints every allowance readout. Driven by the budget's own event.</summary>
        void RefreshStrikeBudget()
        {
            foreach (var (label, key, limit) in _budgetLabels)
            {
                if (label == null) continue;
                label.text = StrikeBudget.RemainingText(key, limit);
                label.color = StrikeBudget.RemainingColour(key, limit,
                    UiTheme.Accent, UiTheme.Warning, UiTheme.Hostile);
            }
        }

        void ToggleView()
        {
            _map.ToggleViewMode();
            _rig.SetMode(_map.ViewMode);
        }

        void OnViewModeChanged(ViewMode mode)
        {
            if (_viewBtnLabel == null) return;
            _viewBtnLabel.text = mode == ViewMode.Mode3D ? "VIEW: 3D" : "VIEW: 2D";
        }

        void OnStyleSelected(int index) => _map.SetMapStyle(Styles[index]);

        void OnStyleChanged(MapStyle style)
        {
            if (_styleDropdown == null) return;
            int idx = System.Array.IndexOf(Styles, style);
            _styleDropdown.SetValueWithoutNotify(idx);
            _styleDropdown.RefreshShownValue();
        }

        // ---------------------------------------------------- drag to deploy

        void BeginDrag(UnitDefinition def, Sprite sprite)
        {
            _dragging = def;
            _dragGhost.sprite = sprite;
            _dragGhost.gameObject.SetActive(sprite != null);
            _lastDropValid = false;
        }

        void Drag(PointerEventData e)
        {
            if (_dragging == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, e.position, _canvas.worldCamera, out Vector2 local);
            ((RectTransform)_dragGhost.transform).anchoredPosition = local;

            // Live WYSIWYG ground marker: over UI or off the loaded terrain,
            // there's nowhere valid to drop, so hide it instead of guessing.
            bool overUI = e.pointerCurrentRaycast.gameObject != null;
            Vector3 world = default;
            _lastDropValid = !overUI && _map.RaycastGround(_worldCam, e.position, out world);
            if (_lastDropValid)
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);

                // Two separate questions, and both must answer yes. The raycast
                // above says the cursor is over *something*; this says the
                // ground under that point can actually be measured. They come
                // apart at a tile seam, where the ray clips the edge of a tile
                // that is streaming out — and a unit deployed there is left at
                // the fallback height, floating over a valley or buried in a
                // ridge. Refusing costs one more click; the alternative is a
                // formation nobody can find.
                if (!GeoUtils.TrySampleTerrainHeight(_map.Georeference, lat, lon, out double ground))
                {
                    _lastDropValid = false;
                    _placementMarker.SetVisible(false);
                    return;
                }

                // Remember exactly where the ring is sitting: the deploy uses
                // this point rather than re-raycasting on release, so the unit
                // cannot land somewhere the preview never showed.
                _dropLat = lat; _dropLon = lon;
                _placementMarker.MoveTo(lat, lon);
                _placementMarker.SetVisible(true);
            }
            else
            {
                _placementMarker.SetVisible(false);
            }
        }

        void EndDrag(PointerEventData e)
        {
            _dragGhost.gameObject.SetActive(false);
            _placementMarker.SetVisible(false);
            if (_dragging == null) return;

            // Released back over the palette, HUD bar or info panel — not a valid
            // deploy point, so don't silently place the unit on whatever terrain
            // happens to be behind that UI.
            if (e.pointerCurrentRaycast.gameObject != null)
            {
                DropRejected?.Invoke("Drop the unit onto the map, not the UI.");
                _dragging = null;
                return;
            }

            if (!_lastDropValid)
            {
                DropRejected?.Invoke("No solid ground there yet — the terrain is still streaming in.");
                _dragging = null;
                return;
            }

            DropRequested?.Invoke(_dragging, _team, _affiliation, _echelon, _dropLat, _dropLon);
            _dragging = null;
        }

        /// <summary>
        /// The drop preview: the same 3D volume a strike target area uses,
        /// scaled down to a formation's footprint.
        ///
        /// It was a flat spinning reticle, which had the failing every decal on
        /// this map has — at the shallow camera pitch the editor is usually
        /// worked at, a circle on sloping ground foreshortens into a line, and
        /// behind a fold of terrain it disappears entirely. You could not see
        /// where you were about to put a battalion. A volume standing on the
        /// ground reads from any angle, and reusing TargetAreaMarker means the
        /// preview and the strike markers stay visually consistent for free —
        /// motes included.
        /// </summary>
        void BuildGroundMarker()
        {
            _placementMarker = TargetAreaMarker.Create(_map.Georeference,
                PlacementRadiusMeters, UiTheme.Accent);
            _placementMarker.SetAlarm(0f);
            _placementMarker.SetVisible(false);
        }

        /// <summary>
        /// Footprint of the drop preview, metres. About the ground a deployed
        /// battalion's icon covers, so what you see is the space it will take.
        /// </summary>
        const float PlacementRadiusMeters = 260f;

        void OnDestroy()
        {
            // Build() subscribes to the map and registry; without this the
            // callbacks fire into a destroyed component on scene reload.
            UnitRegistry.Changed -= OnUnitsChanged;
            StrikeBudget.Changed -= RefreshStrikeBudget;
            LossLedger.Changed -= RefreshStats;
            CaptureSystem.Changed -= RefreshCapture;
            // The commanders and players panels subscribe to registries of their own.
            _commanders?.Dispose();
            _players?.Dispose();
            if (_clock != null) _clock.StartChanged -= RefreshStartLabel;
            if (_weather != null) _weather.Changed -= RefreshWeather;
            if (_effects != null) _effects.ArmedChanged -= RefreshEffects;
            if (_artillery != null) _artillery.ArmedChanged -= RefreshArtillery;
            if (_airStrike != null) _airStrike.ArmedChanged -= RefreshAirStrike;
            if (_uavStrike != null) _uavStrike.ArmedChanged -= RefreshUavStrike;
            if (_naval != null) _naval.ArmedChanged -= RefreshNavalStrike;
            if (_map == null) return;
            _map.ViewModeChanged -= OnViewModeChanged;
            _map.StyleChanged -= OnStyleChanged;
        }
    }
}
