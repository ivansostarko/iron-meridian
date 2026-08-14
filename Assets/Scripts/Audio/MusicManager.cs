using System.Collections;
using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Plays the background music bed, and keeps playing it across scene loads.
    ///
    /// The manager is a <see cref="Object.DontDestroyOnLoad"/> singleton on
    /// purpose. Every screen asks for its track on load, and asking for the
    /// track that is already playing is a no-op — so navigating menu → testing →
    /// units list continues the same music rather than restarting it at each
    /// screen, which is what a per-scene AudioSource would do.
    ///
    /// Master volume still governs everything through
    /// <see cref="AudioManager"/> / AudioListener; the per-track volume here is
    /// a mix level underneath it.
    /// </summary>
    public class MusicManager : MonoBehaviour
    {
        public static MusicManager Active { get; private set; }

        /// <summary>What is playing right now, across all scenes.</summary>
        public static MusicTrack Current { get; private set; } = MusicTrack.None;

        AudioSource _source;
        Coroutine _fade;
        static bool _warnedMissing;

        /// <summary>
        /// Starts <paramref name="track"/>, or does nothing if it is already
        /// playing. Safe to call from every scene bootstrap.
        /// </summary>
        public static void Play(MusicTrack track)
        {
            if (track == MusicTrack.None) { Stop(); return; }

            var mgr = Ensure();
            if (mgr == null || mgr._source == null) return;

            // The normal case on every scene load — this early return is what
            // makes the bed continuous between screens.
            if (Current == track && mgr._source.isPlaying) return;

            var def = AudioCatalog.Get(track);
            if (def == null)
            {
                Debug.LogWarning($"[MusicManager] No catalogue entry for track '{track}'. See docs/10-AUDIO.md.");
                return;
            }

            // Follow the fallback chain: a screen names its own track and
            // borrows the shared bed until that file exists. Bounded rather than
            // recursive, so a catalogue edit that makes a loop cannot hang the
            // screen it was meant to score.
            AudioClip clip = null;
            var resolved = def;
            for (int hop = 0; hop < 4 && resolved != null; hop++)
            {
                clip = Resources.Load<AudioClip>(resolved.resourcePath);
                if (clip != null) break;
                if (resolved.fallback == MusicTrack.None) break;
                resolved = AudioCatalog.Get(resolved.fallback);
            }

            if (clip == null || resolved == null)
            {
                // Warn once: this is called from every scene bootstrap and would
                // otherwise fill the console on every navigation.
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning($"[MusicManager] Missing audio clip: Resources/{def.resourcePath}. " +
                        "Music files must live under an Assets/Resources folder — see docs/10-AUDIO.md.");
                }
                return;
            }

            // Already playing this exact clip? Then the only thing that changes
            // is which track we say is current.
            //
            // This is the fallback chain's sharp edge. The early return above
            // compares *tracks*, and two screens that both borrow the shared bed
            // are different tracks resolving to the same file — so without this
            // check, navigating between them would stop and restart the same
            // audio, which is precisely the restart-on-every-navigation that
            // MusicManager exists to prevent (golden rule 9).
            if (mgr._source.clip == clip && mgr._source.isPlaying)
            {
                Current = track;
                return;
            }

            def = resolved;

            mgr._source.clip = clip;
            mgr._source.loop = def.loop;
            mgr._source.volume = 0f;
            mgr._source.Play();
            Current = track;

            mgr.FadeTo(def.volume, AudioCatalog.MusicFadeInSeconds);
        }

        /// <summary>Fades the music out and stops it.</summary>
        public static void Stop()
        {
            if (Active == null || Active._source == null) return;
            Current = MusicTrack.None;
            Active.FadeTo(0f, AudioCatalog.MusicFadeInSeconds * 0.5f, stopWhenDone: true);
        }

        static MusicManager Ensure()
        {
            if (Active != null) return Active;
            var go = new GameObject("MusicManager");
            DontDestroyOnLoad(go);
            return go.AddComponent<MusicManager>();   // Awake wires Active and the source
        }

        void Awake()
        {
            // A second manager can only appear if one is created before the
            // first finished its Awake; keep the original and drop the copy.
            if (Active != null && Active != this) { Destroy(gameObject); return; }
            Active = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;    // 2D — the listener moves with the map camera
            _source.priority = 64;
        }

        void OnDestroy()
        {
            if (Active == this) { Active = null; Current = MusicTrack.None; }
        }

        void FadeTo(float target, float seconds, bool stopWhenDone = false)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(target, seconds, stopWhenDone));
        }

        IEnumerator FadeRoutine(float target, float seconds, bool stopWhenDone)
        {
            float start = _source.volume;

            // Unscaled time: the pause menu sets timeScale to 0, and music
            // freezing mid-fade when the player pauses would be a bug.
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }

            _source.volume = target;
            if (stopWhenDone) _source.Stop();
            _fade = null;
        }
    }
}
