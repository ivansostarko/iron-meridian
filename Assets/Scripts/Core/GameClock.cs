using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Operational clock for game mode. Starts at 01.01.1990 14:00 and runs
    /// only while a battle is in progress — the map editor is timeless.
    ///
    /// The selected speed drives <see cref="Time.timeScale"/>, so slowing or
    /// pausing time genuinely slows unit movement and combat ticks rather than
    /// only changing the readout. Effects that should keep animating while
    /// paused (range rings, deploy bursts) use unscaled time deliberately.
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        /// <summary>
        /// Game-seconds elapsed per real second at x1. **One.** The scenario
        /// clock runs at real time, which is what makes a formation's
        /// <c>speedKmh</c> mean what it says: a 45 km/h armoured battalion
        /// covers 45 km in an hour on the clock, and 12.5 m in the second you
        /// are watching.
        ///
        /// This used to be 60, with <see cref="UnitMover"/> separately
        /// multiplying march speed by its own hard-coded 60. The two agreed only
        /// by coincidence — nothing tied them together — and the result on
        /// screen was a battalion crossing a 20 km map in half a minute, which
        /// reads as an arcade unit rather than as an army moving.
        ///
        /// Compression is now the player's decision rather than the simulation's:
        /// <see cref="Speeds"/> reaches x300, so an hour of march is six real
        /// seconds when you want it to be and an hour when you don't.
        /// </summary>
        public const double GameSecondsPerRealSecond = 1.0;

        /// <summary>
        /// Selectable rates. Wider and coarser than the old 1/2/4/8 because x1
        /// is now real time: watching a march at real speed is the point, but
        /// getting to the other end of one has to stay cheap.
        /// </summary>
        static readonly float[] Speeds = { 0f, 1f, 2f, 5f, 15f, 60f, 300f };
        /// <summary>Index of x1 — where a fresh scenario starts and where RESET puts it back.</summary>
        public const int NormalSpeed = 1;

        /// <summary>The clock the scenario starts on. Editable from the map editor's Date &amp; Time section.</summary>
        public static readonly System.DateTime DefaultStart = new System.DateTime(1990, 1, 1, 14, 0, 0);

        public System.DateTime Now { get; private set; } = DefaultStart;

        /// <summary>
        /// H-hour for this scenario. Saved with the map, so a scenario carries
        /// the time of day it is meant to be fought at.
        /// </summary>
        public System.DateTime StartDateTime { get; private set; } = DefaultStart;

        /// <summary>Raised when the scenario start is changed (not every tick).</summary>
        public event System.Action StartChanged;

        public int SpeedIndex { get; private set; } = NormalSpeed;
        public float Speed => Speeds[SpeedIndex];
        public bool Paused => SpeedIndex == 0;
        public bool Running { get; private set; }

        /// <summary>Raised when the speed changes (not every tick).</summary>
        public event System.Action SpeedChanged;

        /// <summary>
        /// What Time.timeScale should be right now. The pause menu asks for
        /// this when it closes so it restores the chosen speed instead of
        /// blindly resetting to 1.
        /// </summary>
        public float DesiredTimeScale => Running ? Speed : 1f;

        int _speedBeforePause = NormalSpeed;

        public void SetRunning(bool running)
        {
            Running = running;
            ApplyTimeScale();
            SpeedChanged?.Invoke();
        }

        void Update()
        {
            // Time.timeScale is also driven to 0 by the pause menu, which must
            // freeze the clock even when the player's chosen speed is > 0.
            if (!Running || Paused || Time.timeScale <= 0f) return;
            // Time.deltaTime is already scaled by Speed via timeScale, so the
            // unscaled delta is used here to avoid squaring the multiplier.
            Now = Now.AddSeconds(Time.unscaledDeltaTime * GameSecondsPerRealSecond * Speed);
        }

        public void Faster() => SetSpeed(Mathf.Min(SpeedIndex + 1, Speeds.Length - 1));

        public void Slower() => SetSpeed(Mathf.Max(SpeedIndex - 1, 0));

        public void TogglePause() =>
            SetSpeed(Paused ? Mathf.Max(1, _speedBeforePause) : 0);

        public void SetSpeed(int index)
        {
            if (index == SpeedIndex) return;
            if (SpeedIndex > 0) _speedBeforePause = SpeedIndex;
            SpeedIndex = index;
            ApplyTimeScale();
            SpeedChanged?.Invoke();
        }

        void ApplyTimeScale() => Time.timeScale = DesiredTimeScale;

        /// <summary>
        /// Moves H-hour. The editor is timeless, so the running clock simply
        /// jumps to the new start — there is no elapsed battle time to preserve
        /// while the scenario is still being laid out. Changing it mid-battle
        /// therefore resets the clock, which is the honest reading of "this
        /// scenario now starts at a different time".
        /// </summary>
        public void SetStart(System.DateTime start)
        {
            StartDateTime = start;
            Now = start;
            StartChanged?.Invoke();
            SpeedChanged?.Invoke();      // refresh the HUD readout
        }

        /// <summary>Save-file form of the start: sortable, culture-independent, hand-editable.</summary>
        public const string SaveFormat = "yyyy-MM-dd HH:mm";

        public string StartToSaveString() => StartDateTime.ToString(SaveFormat,
            System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Restores the start from a save. A missing or malformed value falls
        /// back to the default rather than throwing — an old save, or one edited
        /// by hand, must still load.
        /// </summary>
        public void SetStartFromSaveString(string value)
        {
            if (!System.DateTime.TryParseExact(value, SaveFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                if (!string.IsNullOrEmpty(value))
                    Debug.LogWarning($"[GameClock] Could not read start date '{value}' " +
                        $"(expected {SaveFormat}); using the default. See docs/13-DATE-AND-TIME.md.");
                parsed = DefaultStart;
            }
            SetStart(parsed);
        }

        public string DateText => Now.ToString("dd.MM.yyyy");
        public string TimeText => Now.ToString("HH:mm");
        public string SpeedText => Paused ? "PAUSED" : $"x{Speed:0}";
        public string StartText => StartDateTime.ToString("HH:mm  ·  dd.MM.yyyy");

        void OnDisable()
        {
            // Never leave the game frozen behind us on scene change.
            if (Time.timeScale != 1f) Time.timeScale = 1f;
        }
    }
}
