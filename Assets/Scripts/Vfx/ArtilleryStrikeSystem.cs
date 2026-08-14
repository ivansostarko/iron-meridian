using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Calling for fire: pick a nature, place the target area on the map, and
    /// ten seconds later five rounds land inside it.
    ///
    /// The delay is the feature. An artillery strike that lands the instant you
    /// click is a paint tool; one that lands ten seconds later is a decision —
    /// the ground is committed to, the marker sits there advertising where the
    /// rounds are going, and nothing can be done about it afterwards. That is
    /// also why the countdown cannot be cancelled once the mission is away, and
    /// why the marker escalates visually as it runs down.
    ///
    /// Missions run independently, so fire can be walked across a position by
    /// placing several before the first lands. The HUD shows whichever is
    /// nearest to impact.
    ///
    /// Placement is ground-checked like <see cref="EffectPlacementTool"/>:
    /// Cesium streams terrain in, and a click over tiles that have not arrived
    /// has no ground to put an impact on.
    ///
    /// See docs/17-ARTILLERY.md.
    /// </summary>
    public class ArtilleryStrikeSystem : MonoBehaviour
    {
        /// <summary>User-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the armed nature changes, so the panel can repaint.</summary>
        public event System.Action ArmedChanged;

        /// <summary>
        /// Per-frame countdown readout for the HUD: the nature, seconds left and
        /// the total, or null when nothing is in flight.
        /// </summary>
        public System.Action<ArtilleryDef, float, float> CountdownChanged;

        public ArtilleryCaliber? Armed => _armed;
        public bool IsArmed => _armed.HasValue;
        public bool MissionInFlight => _missions.Count > 0;

        MapManager _map;
        Camera _cam;
        ArtilleryCaliber? _armed;

        /// <summary>Follows the cursor while a nature is armed; null when disarmed.</summary>
        TargetAreaMarker _aimMarker;

        bool _validGround;
        double _lat, _lon;

        class Mission
        {
            public ArtilleryDef def;
            public double lat, lon;
            public float remaining;
            public TargetAreaMarker marker;
        }

        readonly List<Mission> _missions = new List<Mission>();

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
        }

        // ------------------------------------------------------------ arming

        /// <summary>Arms a nature, or disarms it if the same one is picked again.</summary>
        public void Toggle(ArtilleryCaliber caliber)
        {
            if (_armed.HasValue && _armed.Value == caliber) { Cancel(); return; }

            _armed = caliber;
            var def = ArtilleryCatalog.Get(caliber);

            if (_aimMarker == null)
                _aimMarker = TargetAreaMarker.Create(_map.Georeference, def.radiusMeters, def.markerColor);
            else
                _aimMarker.Reshape(def.radiusMeters, def.markerColor);

            _aimMarker.SetAlarm(0f);
            _aimMarker.SetVisible(false);   // shown once it has real ground under it

            Flash?.Invoke($"{def.name} — click the target area. Right-click or Esc to stand down.");
            ArmedChanged?.Invoke();
        }

        public void Cancel()
        {
            if (!_armed.HasValue) return;
            _armed = null;
            if (_aimMarker != null) _aimMarker.SetVisible(false);
            ArmedChanged?.Invoke();
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            TickMissions();

            if (!_armed.HasValue) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                Flash?.Invoke("Fire mission cancelled.");
                return;
            }

            TrackGround();

            if (Input.GetMouseButtonDown(0)) Fire();
        }

        /// <summary>Keeps the target area on the ground point under the cursor.</summary>
        void TrackGround()
        {
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            Vector3 hit = default;
            _validGround = !overUI && _map.RaycastGround(_cam, Input.mousePosition, out hit);

            if (!_validGround)
            {
                if (_aimMarker != null) _aimMarker.SetVisible(false);
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, hit, out _lat, out _lon, out _);
            _aimMarker.MoveTo(_lat, _lon);
            _aimMarker.SetVisible(true);
        }

        void Fire()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!_validGround)
            {
                // Stay armed: the tiles may be a second away, and losing the
                // whole fire mission to a click on unloaded terrain would be a
                // punishment for the streamer rather than for the player.
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;
            }

            var def = ArtilleryCatalog.Get(_armed.Value);

            // The mission gets its own marker so the aiming one stays with the
            // cursor — the player can line up the next target while this one
            // is still in the air.
            var marker = TargetAreaMarker.Create(_map.Georeference, def.radiusMeters, def.markerColor);
            marker.MoveTo(_lat, _lon);
            marker.SetAlarm(0f);

            _missions.Add(new Mission
            {
                def = def,
                lat = _lat,
                lon = _lon,
                remaining = ArtilleryCatalog.CountdownSeconds,
                marker = marker
            });

            Flash?.Invoke($"Fire mission away — {def.name}, " +
                          $"{ArtilleryCatalog.ShellsPerMission} rounds, " +
                          $"impact in {Mathf.RoundToInt(ArtilleryCatalog.CountdownSeconds)} seconds.");
        }

        // ---------------------------------------------------------- missions

        void TickMissions()
        {
            if (_missions.Count == 0)
            {
                CountdownChanged?.Invoke(null, 0f, 0f);
                return;
            }

            // Unscaled, so a mission always lands. Tying it to game time would
            // leave rounds hanging in the air indefinitely whenever the battle
            // is paused — and the map editor spends most of its life paused.
            float dt = Time.unscaledDeltaTime;

            Mission soonest = null;

            for (int i = _missions.Count - 1; i >= 0; i--)
            {
                var mission = _missions[i];
                mission.remaining -= dt;

                if (mission.remaining <= 0f)
                {
                    _missions.RemoveAt(i);
                    StartCoroutine(RunSalvo(mission));
                    continue;
                }

                if (mission.marker != null)
                    mission.marker.SetAlarm(1f - mission.remaining / ArtilleryCatalog.CountdownSeconds);

                if (soonest == null || mission.remaining < soonest.remaining) soonest = mission;
            }

            CountdownChanged?.Invoke(soonest?.def, soonest?.remaining ?? 0f,
                ArtilleryCatalog.CountdownSeconds);
        }

        /// <summary>
        /// Lands the salvo: five rounds, scattered across the target area and
        /// spaced so the strike reads as a battery firing rather than one
        /// detonation. Each round carries its own burst, its own lingering smoke
        /// and its own report — all three come from the nature's catalogue row.
        /// </summary>
        IEnumerator RunSalvo(Mission mission)
        {
            var def = mission.def;

            // Full alarm for the duration of the shooting, then the marker goes.
            if (mission.marker != null) mission.marker.SetAlarm(1f);

            for (int i = 0; i < ArtilleryCatalog.ShellsPerMission; i++)
            {
                ScatterPoint(mission.lat, mission.lon, def.radiusMeters, i,
                    out double lat, out double lon);

                VfxSystem.Play(def.burst, lat, lon, def.burstScale);

                // Smoke loops by design and is dispersed explicitly, the same
                // way a wreck is burned out — see VfxSystem.PlayWreck.
                var smoke = VfxSystem.Play(def.smoke, lat, lon, def.burstScale);
                if (smoke != null && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(smoke, def.smokeSeconds);

                if (i < ArtilleryCatalog.ShellsPerMission - 1)
                    yield return new WaitForSecondsRealtime(def.shellIntervalSeconds);
            }

            if (mission.marker != null) Destroy(mission.marker.gameObject);

            Flash?.Invoke($"Rounds complete — {def.name}, {ArtilleryCatalog.ShellsPerMission} rounds fired.");
        }

        /// <summary>
        /// Where round <paramref name="index"/> lands inside the target area.
        ///
        /// The golden angle spreads successive rounds around the circle instead
        /// of clumping them, and the square root on the radius makes the scatter
        /// uniform by *area* — without it every round crowds the centre and the
        /// sheaf looks nothing like a beaten zone. Jitter on top stops the
        /// pattern from being recognisable between missions.
        /// </summary>
        static void ScatterPoint(double lat, double lon, float radiusMeters, int index,
            out double outLat, out double outLon)
        {
            float t = (index + 0.5f) / ArtilleryCatalog.ShellsPerMission;
            float distance = Mathf.Sqrt(t) * radiusMeters * Random.Range(0.72f, 0.98f);
            float bearing = index * 137.508f + Random.Range(-20f, 20f);

            GeoUtils.Destination(lat, lon, bearing, distance / 1000.0, out outLat, out outLon);
        }

        void OnDestroy()
        {
            if (_aimMarker != null) Destroy(_aimMarker.gameObject);
            foreach (var mission in _missions)
                if (mission.marker != null) Destroy(mission.marker.gameObject);
            _missions.Clear();
        }
    }
}
