using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Map;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// The shared machinery behind every called strike: pick something, place a
    /// target area on the map, wait out a countdown, and then something lands.
    ///
    /// Artillery and air strikes differ only in what is chosen and what happens
    /// at zero — the arming, the target marker tracking the cursor, the ground
    /// checks, the countdown, the escalating marker and the HUD banner are
    /// identical, and were identical in two places before this existed.
    /// Subclasses supply the numbers for their natures and implement
    /// <see cref="RunStrike"/>; everything above that is here.
    ///
    /// <typeparamref name="TKey"/> is whatever identifies one option in the
    /// subclass's menu — a calibre, an airframe.
    ///
    /// See docs/17-ARTILLERY.md and docs/18-AIR-STRIKES.md.
    /// </summary>
    public abstract class CalledStrikeSystem<TKey> : MonoBehaviour where TKey : struct
    {
        /// <summary>User-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the armed option changes, so the panel can repaint.</summary>
        public event System.Action ArmedChanged;

        /// <summary>
        /// Per-frame countdown readout: title, seconds left, total and colour.
        /// A null title means nothing is in flight. More than one strike system
        /// can be running, so the caller — not this class — decides which one
        /// the single HUD banner shows.
        /// </summary>
        public System.Action<string, float, float, Color> CountdownChanged;

        public TKey? Armed => _armed;
        public bool IsArmed => _armed.HasValue;
        public bool MissionInFlight => _missions.Count > 0;

        protected MapManager Map { get; private set; }
        protected Camera Cam { get; private set; }

        TKey? _armed;

        /// <summary>Follows the cursor while something is armed; hidden otherwise.</summary>
        TargetAreaMarker _aimMarker;

        bool _validGround;
        double _lat, _lon;

        protected class Mission
        {
            public TKey key;
            public double lat, lon;
            public float remaining;
            public float total;
            public TargetAreaMarker marker;
        }

        readonly List<Mission> _missions = new List<Mission>();

        public void Init(MapManager map, Camera cam)
        {
            Map = map;
            Cam = cam;
        }

        // ---------------------------------------------------- subclass hooks

        /// <summary>Radius of the target area in metres for this option.</summary>
        protected abstract float RadiusFor(TKey key);
        /// <summary>Marker and banner colour for this option.</summary>
        protected abstract Color ColourFor(TKey key);
        /// <summary>Name shown in the countdown banner and in messages.</summary>
        protected abstract string NameFor(TKey key);
        /// <summary>Seconds between the call and the first thing landing.</summary>
        protected abstract float CountdownFor(TKey key);
        /// <summary>Message when this option is armed.</summary>
        protected abstract string ArmedMessage(TKey key);
        /// <summary>Message when a mission has been placed.</summary>
        protected abstract string AwayMessage(TKey key);

        /// <summary>
        /// What actually happens when the countdown reaches zero. The routine
        /// owns <paramref name="marker"/> from this point and must destroy it —
        /// artillery drops it as the salvo ends, an air strike keeps it up until
        /// the aircraft has finished its run.
        /// </summary>
        protected abstract IEnumerator RunStrike(TKey key, double lat, double lon, TargetAreaMarker marker);

        // ------------------------------------------------------------ arming

        /// <summary>Arms an option, or disarms it if the same one is picked again.</summary>
        public void Toggle(TKey key)
        {
            if (_armed.HasValue && _armed.Value.Equals(key)) { Cancel(); return; }

            // Refused at the point of arming as well as at the point of firing.
            // Letting a spent allowance arm normally and only complain on the
            // map click would waste the player's aim and read as the click
            // having missed.
            if (StrikeBudget.Exhausted)
            {
                Flash?.Invoke(StrikeBudget.ExhaustedMessage);
                return;
            }

            _armed = key;

            if (_aimMarker == null)
                _aimMarker = TargetAreaMarker.Create(Map.Georeference, RadiusFor(key), ColourFor(key));
            else
                _aimMarker.Reshape(RadiusFor(key), ColourFor(key));

            _aimMarker.SetAlarm(0f);
            _aimMarker.SetVisible(false);   // shown once it has real ground under it

            Flash?.Invoke(ArmedMessage(key));
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
                var key = _armed.Value;
                Cancel();
                Flash?.Invoke($"{NameFor(key)} — stood down.");
                return;
            }

            TrackGround();

            if (Input.GetMouseButtonDown(0)) Launch();
        }

        /// <summary>Keeps the target area on the ground point under the cursor.</summary>
        void TrackGround()
        {
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            Vector3 hit = default;
            _validGround = !overUI && Map.RaycastGround(Cam, Input.mousePosition, out hit);

            if (!_validGround)
            {
                if (_aimMarker != null) _aimMarker.SetVisible(false);
                return;
            }

            GeoUtils.UnityToGeo(Map.Georeference, hit, out _lat, out _lon, out _);
            _aimMarker.MoveTo(_lat, _lon);
            _aimMarker.SetVisible(true);
        }

        void Launch()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!_validGround)
            {
                // Stay armed: the tiles may be a second away, and losing the
                // whole mission to a click on unloaded terrain would punish the
                // player for the streamer rather than for anything they did.
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;
            }

            var key = _armed.Value;

            // Spent when the mission is *placed*, because that is the moment it
            // becomes irrevocable — nothing can recall a strike once away, so
            // nothing should be able to get the allowance back either.
            if (!StrikeBudget.TryConsume())
            {
                Cancel();
                Flash?.Invoke(StrikeBudget.ExhaustedMessage);
                return;
            }

            // The mission gets its own marker so the aiming one stays with the
            // cursor — the next target can be lined up while this one is in the air.
            var marker = TargetAreaMarker.Create(Map.Georeference, RadiusFor(key), ColourFor(key));
            marker.MoveTo(_lat, _lon);
            marker.SetAlarm(0f);

            _missions.Add(new Mission
            {
                key = key,
                lat = _lat,
                lon = _lon,
                remaining = CountdownFor(key),
                total = CountdownFor(key),
                marker = marker
            });

            Flash?.Invoke(AwayMessage(key));

            // That was the last one: stand the launcher down rather than leaving
            // it armed over a map that can no longer answer a click.
            if (StrikeBudget.Exhausted)
            {
                Cancel();
                Flash?.Invoke(StrikeBudget.ExhaustedMessage);
            }
        }

        void TickMissions()
        {
            if (_missions.Count == 0)
            {
                CountdownChanged?.Invoke(null, 0f, 0f, Color.white);
                return;
            }

            // Unscaled, so a mission always lands. Tying this to game time would
            // leave rounds hanging in the air whenever the battle is paused —
            // and the map editor spends most of its life paused.
            float dt = Time.unscaledDeltaTime;

            Mission soonest = null;

            for (int i = _missions.Count - 1; i >= 0; i--)
            {
                var mission = _missions[i];
                mission.remaining -= dt;

                if (mission.remaining <= 0f)
                {
                    _missions.RemoveAt(i);
                    StartCoroutine(RunStrike(mission.key, mission.lat, mission.lon, mission.marker));
                    continue;
                }

                if (mission.marker != null)
                    mission.marker.SetAlarm(1f - mission.remaining / mission.total);

                if (soonest == null || mission.remaining < soonest.remaining) soonest = mission;
            }

            if (soonest == null) CountdownChanged?.Invoke(null, 0f, 0f, Color.white);
            else CountdownChanged?.Invoke(NameFor(soonest.key), soonest.remaining, soonest.total,
                ColourFor(soonest.key));
        }

        protected virtual void OnDestroy()
        {
            if (_aimMarker != null) Destroy(_aimMarker.gameObject);
            foreach (var mission in _missions)
                if (mission.marker != null) Destroy(mission.marker.gameObject);
            _missions.Clear();
        }
    }
}
