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
        /// <summary>Game-seconds elapsed per real second at 1x.</summary>
        const double GameSecondsPerRealSecond = 60.0;

        static readonly float[] Speeds = { 0f, 1f, 2f, 4f, 8f };
        const int NormalSpeed = 1;

        public System.DateTime Now { get; private set; } = new System.DateTime(1990, 1, 1, 14, 0, 0);
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

        void SetSpeed(int index)
        {
            if (index == SpeedIndex) return;
            if (SpeedIndex > 0) _speedBeforePause = SpeedIndex;
            SpeedIndex = index;
            ApplyTimeScale();
            SpeedChanged?.Invoke();
        }

        void ApplyTimeScale() => Time.timeScale = DesiredTimeScale;

        public string DateText => Now.ToString("dd.MM.yyyy");
        public string TimeText => Now.ToString("HH:mm");
        public string SpeedText => Paused ? "PAUSED" : $"x{Speed:0}";

        void OnDisable()
        {
            // Never leave the game frozen behind us on scene change.
            if (Time.timeScale != 1f) Time.timeScale = 1f;
        }
    }
}
