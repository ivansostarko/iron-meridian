using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Game-wide audio control. Master volume is applied through
    /// AudioListener.volume so a single slider controls the whole game.
    /// </summary>
    public static class AudioManager
    {
        const string PrefKey = "im.masterVolume";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(PrefKey, 0.8f);
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefKey, v);
                AudioListener.volume = v;
            }
        }

        /// <summary>Call once at startup (done by every scene bootstrap).</summary>
        public static void Apply() => AudioListener.volume = MasterVolume;

        /// <summary>Simple procedural click for UI feedback.</summary>
        public static void PlayClick(GameObject host)
        {
            var src = host.GetComponent<AudioSource>();
            if (src == null)
            {
                src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.clip = BuildClick();
            }
            src.Play();
        }

        /// <summary>
        /// The UI click, so the DEVELOPMENT → AUDIO screen can list it with
        /// everything else. It is the one sound in the game with no catalogue
        /// row, because it has no file — leaving it off the register would make
        /// the register wrong rather than short.
        /// </summary>
        public static AudioClip ClickClip => BuildClick();

        static AudioClip _click;
        static AudioClip BuildClick()
        {
            if (_click != null) return _click;
            const int rate = 44100;
            const float dur = 0.05f;
            int n = (int)(rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = Mathf.Sin(2 * Mathf.PI * 1200f * t) * Mathf.Exp(-t * 60f) * 0.4f;
            }
            _click = AudioClip.Create("uiclick", n, 1, rate, false);
            _click.SetData(data, 0);
            return _click;
        }
    }
}
