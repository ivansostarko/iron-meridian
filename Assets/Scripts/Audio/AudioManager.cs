using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Game-wide audio control: the master volume, the four channel volumes
    /// under it, and the interface's own click and hover.
    ///
    /// **Master is the listener, channels are mix levels.** The master volume
    /// goes through <c>AudioListener.volume</c>, so one slider governs
    /// everything including sounds nothing else knows about. The channels —
    /// music, ambience, effects, interface — are multipliers the players of
    /// each kind apply to their own sources, which is the only way to let a
    /// player turn the music down without turning the battle down with it.
    ///
    /// Every level is a <see cref="PlayerPrefs"/> entry, so the mix survives a
    /// restart. See docs/10-AUDIO.md.
    /// </summary>
    public static class AudioManager
    {
        const string MasterKey = "im.masterVolume";
        const string MusicKey = "im.vol.music";
        const string AmbienceKey = "im.vol.ambience";
        const string EffectsKey = "im.vol.effects";
        const string InterfaceKey = "im.vol.interface";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(MasterKey, 0.8f);
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MasterKey, v);
                AudioListener.volume = v;
            }
        }

        /// <summary>Mix level for the music bed — see <see cref="MusicManager"/>.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 1f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); MusicManager.RefreshVolume(); }
        }

        /// <summary>Mix level for the weather bed — see <see cref="AmbienceManager"/>.</summary>
        public static float AmbienceVolume
        {
            get => PlayerPrefs.GetFloat(AmbienceKey, 1f);
            set { PlayerPrefs.SetFloat(AmbienceKey, Mathf.Clamp01(value)); AmbienceManager.RefreshVolume(); }
        }

        /// <summary>Mix level for gunfire, explosions and every other world sound.</summary>
        public static float EffectsVolume
        {
            get => PlayerPrefs.GetFloat(EffectsKey, 1f);
            set => PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value));
        }

        /// <summary>Mix level for the interface's click and hover.</summary>
        public static float InterfaceVolume
        {
            get => PlayerPrefs.GetFloat(InterfaceKey, 1f);
            set => PlayerPrefs.SetFloat(InterfaceKey, Mathf.Clamp01(value));
        }

        /// <summary>
        /// The player's preference: does a button make a sound under the
        /// cursor at all? Set on the settings screen, and remembered.
        /// </summary>
        public static bool HoverSounds
        {
            get => PlayerPrefs.GetInt(HoverKey, 1) == 1;
            set => PlayerPrefs.SetInt(HoverKey, value ? 1 : 0);
        }

        const string HoverKey = "im.ui.hover";

        /// <summary>
        /// A screen saying "not here" — separate from the preference above,
        /// because they answer different questions and must not overwrite each
        /// other. The map editor sets this for as long as it exists: the rail,
        /// the order bar and the fire menus put a hundred controls under a
        /// cursor that crosses them constantly on its way to the map, and a
        /// sound on each one is noise, not feedback. A player who turned hover
        /// sounds off in settings still finds them off when they come back.
        /// </summary>
        public static bool HoverSuppressed { get; set; }

        /// <summary>Whether a hover will actually be heard: the preference, and the screen's say.</summary>
        public static bool UiHoverEnabled => HoverSounds && !HoverSuppressed;

        /// <summary>Call once at startup (done by every scene bootstrap).</summary>
        public static void Apply() => AudioListener.volume = MasterVolume;

        // -------------------------------------------------------- interface

        /// <summary>The click a button makes. Called by every button UIFactory builds.</summary>
        public static void PlayClick(GameObject host) => PlayUi(host, UiSound.Click);

        /// <summary>
        /// The sound the cursor makes arriving on a button. A no-op when
        /// <see cref="UiHoverEnabled"/> is off, so the map editor stays quiet.
        /// </summary>
        public static void PlayHover(GameObject host)
        {
            if (!UiHoverEnabled) return;
            PlayUi(host, UiSound.Hover);
        }

        /// <summary>
        /// Plays one interface sound on a source borrowed from the control
        /// itself.
        ///
        /// <c>PlayOneShot</c> rather than <c>Play</c>: a row that is clicked
        /// while its own hover is still ringing must not cut the hover off, and
        /// one source per control is already one more than the mix needs.
        /// </summary>
        static void PlayUi(GameObject host, UiSound sound)
        {
            if (host == null) return;

            var clip = Clip(sound);
            if (clip == null) return;

            var src = host.GetComponent<AudioSource>();
            if (src == null)
            {
                src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;      // 2D — interface sounds have no position
            }

            var def = AudioCatalog.GetUi(sound);
            src.PlayOneShot(clip, Mathf.Clamp01((def?.volume ?? 1f) * InterfaceVolume));
        }

        /// <summary>
        /// The clip behind one interface sound, cached by sound.
        ///
        /// The click falls back to the synthesised one when its file is missing:
        /// a game with no click at all reads as unresponsive, and this is the
        /// one sound the interface cannot do without. The hover has no fallback
        /// — silence is the correct absence for a sound whose whole job is to be
        /// barely there.
        /// </summary>
        public static AudioClip Clip(UiSound sound)
        {
            if (_clips.TryGetValue(sound, out var cached) && cached != null) return cached;

            var def = AudioCatalog.GetUi(sound);
            AudioClip clip = def != null ? Resources.Load<AudioClip>(def.resourcePath) : null;

            if (clip == null && def != null && !_warned.Contains(sound))
            {
                _warned.Add(sound);
                Debug.LogWarning($"[AudioManager] Missing interface sound: Resources/{def.resourcePath}. " +
                    "Audio files must live under an Assets/Resources folder — see docs/10-AUDIO.md.");
            }

            if (clip == null && sound == UiSound.Click) clip = BuildClick();

            if (clip != null) _clips[sound] = clip;
            return clip;
        }

        static readonly Dictionary<UiSound, AudioClip> _clips = new Dictionary<UiSound, AudioClip>();
        static readonly HashSet<UiSound> _warned = new HashSet<UiSound>();

        /// <summary>
        /// The synthesised click, kept as the click's fallback and so the
        /// DEVELOPMENT → AUDIO screen can list what is actually heard when the
        /// file is absent.
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
