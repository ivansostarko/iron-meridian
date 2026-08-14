using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Synthesises the effect sounds so the game is audible with no audio
    /// assets installed — the counterpart to
    /// <see cref="IronMeridian.Vfx.ProceduralVfx"/> for the visuals, and the
    /// same reasoning as <see cref="AudioManager.PlayClick"/>'s UI blip.
    ///
    /// These are honest stand-ins, not a substitute for recorded audio: drop a
    /// file into <c>Resources/Audio/effects/</c> and it wins (see
    /// <see cref="EffectAudio"/>).
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;

        public static AudioClip Build(EffectSound sound) => sound switch
        {
            EffectSound.Explosion => Explosion(),
            EffectSound.Fire => FireLoop(),
            EffectSound.Smoke => SmokeLoop(),
            EffectSound.Impact => Impact(),

            // Artillery: one shared model, four sets of numbers. See Shell().
            EffectSound.ArtilleryLight =>
                Shell("fx_arty_105", seed: 105, duration: 2.0f,
                      startHz: 170f, endHz: 62f, pitchFallSeconds: 0.45f,
                      bodyDecay: 3.4f, crackDecay: 7.5f, crackLowPass: 0.34f,
                      bodyMix: 0.55f, crackMix: 0.70f, rumbleMix: 0.10f),

            EffectSound.ArtilleryMortar =>
                Shell("fx_arty_120", seed: 120, duration: 2.2f,
                      startHz: 120f, endHz: 46f, pitchFallSeconds: 0.30f,
                      bodyDecay: 4.2f, crackDecay: 9.0f, crackLowPass: 0.10f,
                      bodyMix: 0.62f, crackMix: 0.40f, rumbleMix: 0.34f),

            EffectSound.ArtilleryMedium =>
                Shell("fx_arty_155", seed: 155, duration: 3.0f,
                      startHz: 92f, endHz: 30f, pitchFallSeconds: 0.85f,
                      bodyDecay: 2.0f, crackDecay: 5.0f, crackLowPass: 0.20f,
                      bodyMix: 0.80f, crackMix: 0.58f, rumbleMix: 0.30f),

            EffectSound.ArtilleryHeavy =>
                Shell("fx_arty_203", seed: 203, duration: 4.2f,
                      startHz: 66f, endHz: 19f, pitchFallSeconds: 1.35f,
                      bodyDecay: 1.15f, crackDecay: 3.2f, crackLowPass: 0.13f,
                      bodyMix: 1.00f, crackMix: 0.50f, rumbleMix: 0.52f),

            // Heavier and longer than any tube: a big air-dropped weapon.
            EffectSound.AerialBomb =>
                Shell("fx_aerial_bomb", seed: 2077, duration: 5.0f,
                      startHz: 58f, endHz: 15f, pitchFallSeconds: 1.7f,
                      bodyDecay: 0.95f, crackDecay: 2.6f, crackLowPass: 0.16f,
                      bodyMix: 1.00f, crackMix: 0.55f, rumbleMix: 0.62f),

            EffectSound.JetPass => JetPass(),

            // A few kilograms of warhead, not a shell: high, sharp, and gone.
            EffectSound.UavWarhead =>
                Shell("fx_uav_warhead", seed: 9001, duration: 1.4f,
                      startHz: 240f, endHz: 95f, pitchFallSeconds: 0.28f,
                      bodyDecay: 5.5f, crackDecay: 11f, crackLowPass: 0.45f,
                      bodyMix: 0.45f, crackMix: 0.85f, rumbleMix: 0.06f),

            EffectSound.DroneBuzz => DroneBuzz(),

            // Shahed class: fifty-odd kilograms, so a shell's report rather than
            // a munition's crack — between the 155 and the 203.
            EffectSound.ShahedWarhead =>
                Shell("fx_shahed_warhead", seed: 136, duration: 3.6f,
                      startHz: 78f, endHz: 24f, pitchFallSeconds: 1.1f,
                      bodyDecay: 1.5f, crackDecay: 4.2f, crackLowPass: 0.17f,
                      bodyMix: 0.92f, crackMix: 0.54f, rumbleMix: 0.42f),

            EffectSound.ShahedEngine => ShahedEngine(),

            // Missiles: the same three-layer report, at the three weights the
            // catalogue offers. Light is above every tube in the game; heavy is
            // below everything including the air-dropped bomb.
            EffectSound.MissileLight =>
                Shell("fx_missile_light", seed: 3001, duration: 2.6f,
                      startHz: 150f, endHz: 52f, pitchFallSeconds: 0.5f,
                      bodyDecay: 2.8f, crackDecay: 6.5f, crackLowPass: 0.40f,
                      bodyMix: 0.60f, crackMix: 0.85f, rumbleMix: 0.18f),

            EffectSound.MissileMedium =>
                Shell("fx_missile_medium", seed: 3002, duration: 4.4f,
                      startHz: 74f, endHz: 22f, pitchFallSeconds: 1.4f,
                      bodyDecay: 1.1f, crackDecay: 3.0f, crackLowPass: 0.18f,
                      bodyMix: 1.00f, crackMix: 0.60f, rumbleMix: 0.58f),

            EffectSound.MissileHeavy =>
                Shell("fx_missile_heavy", seed: 3003, duration: 6.5f,
                      startHz: 48f, endHz: 12f, pitchFallSeconds: 2.4f,
                      bodyDecay: 0.62f, crackDecay: 1.9f, crackLowPass: 0.11f,
                      bodyMix: 1.00f, crackMix: 0.48f, rumbleMix: 0.80f),

            EffectSound.MissileMotor => MissileMotor(),
            EffectSound.MissileIncoming => MissileIncoming(),

            _ => null
        };

        /// <summary>
        /// A Shahed-class engine: a small two-stroke, which is a completely
        /// different sound from a quadcopter and the reason the class has a
        /// nickname. Where <see cref="DroneBuzz"/> is a stack of clean detuned
        /// tones, this is a harsh buzz built from a sawtooth and its harmonics,
        /// with a slow irregular waver — a piston engine under load, not four
        /// electric motors holding station.
        /// </summary>
        static AudioClip ShahedEngine()
        {
            const float duration = 2.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(136);
            float hiss = 0f, phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                // Firing rate wavers a couple of per cent, the way a small
                // engine does. A rock-steady rate reads as a synthesiser.
                float freq = 86f * (1f + 0.022f * Mathf.Sin(t * 5.1f) + 0.013f * Mathf.Sin(t * 11.7f));
                phase += 2f * Mathf.PI * freq / Rate;
                if (phase > Mathf.PI * 2f) phase -= Mathf.PI * 2f;

                // Sawtooth from its first few harmonics: rich in the odd
                // partials that make the sound rasp rather than hum.
                float saw = 0f;
                for (int h = 1; h <= 6; h++)
                    saw += Mathf.Sin(phase * h) / h;
                saw *= 0.55f;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hiss = Mathf.Lerp(hiss, noise, 0.22f);

                data[i] = saw * 0.85f + hiss * 0.20f;
            }

            Normalise(data, 0.62f);
            CrossfadeLoop(data);
            return Make("fx_shahed_engine", data);
        }

        /// <summary>
        /// A rocket motor: broadband roar with a low pulsing core, looped so it
        /// can travel with the missile for as long as the flight lasts.
        ///
        /// No pitch slide, unlike <see cref="JetPass"/> — the motor is attached
        /// to the missile and heard from it, so the changing distance is the
        /// audio source's job rather than the clip's.
        /// </summary>
        static AudioClip MissileMotor()
        {
            const float duration = 2.4f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(5150);
            float low = 0f, mid = 0f, phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.035f);
                mid = Mathf.Lerp(mid, noise - low, 0.22f);

                // Combustion roughness — a low tone under the roar, which is
                // what separates a rocket from a waterfall.
                phase += 2f * Mathf.PI * 41f / Rate;
                float core = Mathf.Sin(phase) * 0.30f;

                data[i] = low * 0.95f + mid * 0.42f + core;
            }

            Normalise(data, 0.72f);
            CrossfadeLoop(data);
            return Make("fx_missile_motor", data);
        }

        /// <summary>
        /// The terminal descent: a rising whistle under a swelling roar, the
        /// one-shot played as the warhead comes down.
        ///
        /// This is the cue that gives the player the half-second of warning that
        /// makes an impact land emotionally rather than merely visually — you
        /// hear it arrive before you see it hit.
        /// </summary>
        static AudioClip MissileIncoming()
        {
            const float duration = 2.6f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(911);
            float air = 0f, phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = t / duration;

                // Everything grows toward the impact; nothing decays.
                float envelope = u * u;

                float freq = Mathf.Lerp(320f, 1350f, u * u);
                phase += 2f * Mathf.PI * freq / Rate;
                float whistle = Mathf.Sin(phase) * 0.55f;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                air = Mathf.Lerp(air, noise, 0.12f);

                data[i] = (whistle + air * 0.85f) * envelope;
            }

            Normalise(data, 0.78f);
            return Make("fx_missile_incoming", data);
        }

        /// <summary>
        /// Quadcopter propellers: a stack of close, slightly detuned tones over a
        /// thin airy hiss.
        ///
        /// The detuning is the whole trick. Four propellers never turn at exactly
        /// the same rate, and the slow beating between them is what the ear
        /// recognises as a multirotor rather than as a wasp or a lawnmower. A
        /// single clean tone at the same pitch sounds synthetic immediately.
        /// </summary>
        static AudioClip DroneBuzz()
        {
            const float duration = 2.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(4);
            float hiss = 0f;

            // Blade-pass frequencies, deliberately not harmonically related.
            float[] freqs = { 118f, 121.5f, 176f, 181f, 237f };
            float[] gains = { 1.00f, 0.85f, 0.42f, 0.36f, 0.18f };
            var phase = new float[freqs.Length];

            for (int i = 0; i < n; i++)
            {
                float sample = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    phase[k] += 2f * Mathf.PI * freqs[k] / Rate;
                    sample += Mathf.Sin(phase[k]) * gains[k];
                }
                sample /= 3f;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hiss = Mathf.Lerp(hiss, noise, 0.35f);

                data[i] = sample * 0.8f + hiss * 0.12f;
            }

            Normalise(data, 0.60f);
            CrossfadeLoop(data);
            return Make("fx_drone_buzz", data);
        }

        /// <summary>
        /// A jet passing overhead: broadband noise that swells and fades, over a
        /// low turbine tone that slides down as it goes by.
        ///
        /// The Doppler slide is done here rather than left to Unity's Doppler
        /// because the effect sources run with `dopplerLevel = 0` — this map is
        /// kilometres across and the camera is not a listener in motion, so the
        /// engine's Doppler produces nothing useful. Baking the slide into the
        /// clip gives the pass its sense of speed regardless.
        /// </summary>
        static AudioClip JetPass()
        {
            const float duration = 6.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(747);
            float low = 0f, band = 0f;
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = t / duration;

                // Approach and recede: loudest at the midpoint of the pass.
                float envelope = Mathf.Sin(u * Mathf.PI);
                envelope *= envelope;

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.05f);          // the roar
                band = Mathf.Lerp(band, noise - low, 0.30f);  // the air

                // Turbine tone, sliding down through the pass.
                float freq = Mathf.Lerp(115f, 62f, u);
                phase += 2f * Mathf.PI * freq / Rate;
                float tone = Mathf.Sin(phase);

                data[i] = (low * 0.70f + band * 0.20f + tone * 0.35f) * envelope;
            }

            Normalise(data, 0.80f);
            return Make("fx_jet_pass", data);
        }

        /// <summary>
        /// One artillery report, parameterised by calibre.
        ///
        /// Three layers, which is what separates the natures by ear:
        /// **body** — a sine whose pitch falls away after the detonation; the
        /// lower it starts and the slower it decays, the bigger the tube reads.
        /// **crack** — filtered noise for the shock front; open the filter and
        /// it is a sharp 105 mm snap, close it and it is a mortar's dull thump.
        /// **rumble** — a slow noise bed under everything, which is the ground
        /// shaking and is what makes the heavy calibres feel heavy rather than
        /// merely quiet.
        ///
        /// Every calibre also gets a deterministic seed, so a given nature
        /// always sounds like itself between runs.
        /// </summary>
        static AudioClip Shell(string name, int seed, float duration,
            float startHz, float endHz, float pitchFallSeconds,
            float bodyDecay, float crackDecay, float crackLowPass,
            float bodyMix, float crackMix, float rumbleMix)
        {
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(seed);
            float crackFilter = 0f, rumbleFilter = 0f;

            // Integrated phase rather than sin(2*pi*f(t)*t): with a sweeping
            // frequency the naive form makes the phase run backwards and the
            // "boom" turns into a warble.
            float phase = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                float freq = Mathf.Lerp(startHz, endHz, Mathf.Clamp01(t / pitchFallSeconds));
                phase += 2f * Mathf.PI * freq / Rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * bodyDecay);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                crackFilter = Mathf.Lerp(crackFilter, noise, crackLowPass);
                float crack = crackFilter * Mathf.Exp(-t * crackDecay);

                rumbleFilter = Mathf.Lerp(rumbleFilter, noise, 0.012f);
                float rumble = rumbleFilter * Mathf.Exp(-t * (bodyDecay * 0.55f));

                // Leading transient: without it the round fades in rather than
                // arriving, which robs every calibre of its impact.
                float click = Mathf.Exp(-t * 260f) * (float)(rng.NextDouble() * 2.0 - 1.0);

                data[i] = body * bodyMix + crack * crackMix + rumble * rumbleMix + click * 0.30f;
            }

            Normalise(data, 0.92f);
            return Make(name, data);
        }

        /// <summary>
        /// Detonation: a low body that drops in pitch under a wide noise crack,
        /// with a long tail. The pitch drop is what makes it read as a big
        /// distant blast rather than a click.
        /// </summary>
        static AudioClip Explosion()
        {
            const float duration = 2.4f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(1337);
            float lowPass = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                // Body: 90 Hz falling to ~28 Hz over the first second.
                float bodyFreq = Mathf.Lerp(90f, 28f, Mathf.Clamp01(t / 1.0f));
                float body = Mathf.Sin(2f * Mathf.PI * bodyFreq * t) * Mathf.Exp(-t * 2.2f);

                // Crack: white noise, rolled off so it is a roar not a hiss.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lowPass = Mathf.Lerp(lowPass, noise, 0.18f);
                float crack = lowPass * Mathf.Exp(-t * 5.5f);

                // Very short click to give the transient a leading edge.
                float click = Mathf.Exp(-t * 220f) * (float)(rng.NextDouble() * 2.0 - 1.0);

                data[i] = Mathf.Clamp(body * 0.75f + crack * 0.55f + click * 0.35f, -1f, 1f);
            }

            return Make("fx_explosion", data);
        }

        /// <summary>
        /// Fire: band-limited noise with random pops for crackle. Built to be
        /// seamless — the last 20 ms cross-fades into the first, so the loop
        /// has no click at the wrap.
        /// </summary>
        static AudioClip FireLoop()
        {
            const float duration = 3.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(4242);
            float low = 0f, band = 0f;

            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.06f);          // rumble
                band = Mathf.Lerp(band, noise - low, 0.55f);  // hiss

                float sample = low * 0.55f + band * 0.22f;

                // Crackle: sparse, sharp decaying pops.
                if (rng.NextDouble() < 0.0016)
                {
                    float pop = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.8f;
                    int popLen = 300 + (int)(rng.NextDouble() * 500);
                    for (int k = 0; k < popLen && i + k < n; k++)
                        data[i + k] += pop * Mathf.Exp(-k / (float)popLen * 6f);
                }

                data[i] += sample;
            }

            Normalise(data, 0.7f);
            CrossfadeLoop(data);
            return Make("fx_fire", data);
        }

        /// <summary>Smoke: a soft, slow-moving low hiss. Almost sub-audible on purpose.</summary>
        static AudioClip SmokeLoop()
        {
            const float duration = 3.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(909);
            float low = 0f, slower = 0f;

            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.03f);
                slower = Mathf.Lerp(slower, low, 0.02f);
                // Gentle swell so it breathes rather than sitting as flat static.
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.12f * (i / (float)Rate));
                data[i] = low * 0.5f * swell + slower * 0.5f;
            }

            Normalise(data, 0.45f);
            CrossfadeLoop(data);
            return Make("fx_smoke", data);
        }

        /// <summary>Impact: a short filtered thud for rounds landing.</summary>
        static AudioClip Impact()
        {
            const float duration = 0.5f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(77);
            float low = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.22f);
                float thud = Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 22f);
                data[i] = Mathf.Clamp(thud * 0.6f + low * Mathf.Exp(-t * 14f) * 0.5f, -1f, 1f);
            }

            return Make("fx_impact", data);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Blends the tail into the head so a looping clip does not click at
        /// the wrap — the single most audible flaw in synthesised loops.
        /// </summary>
        static void CrossfadeLoop(float[] data, float seconds = 0.02f)
        {
            int fade = Mathf.Min((int)(Rate * seconds), data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                int tail = data.Length - fade + i;
                data[i] = Mathf.Lerp(data[tail], data[i], k);
            }
        }

        static void Normalise(float[] data, float peak)
        {
            float max = 0f;
            foreach (float s in data) max = Mathf.Max(max, Mathf.Abs(s));
            if (max <= 0.0001f) return;
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
