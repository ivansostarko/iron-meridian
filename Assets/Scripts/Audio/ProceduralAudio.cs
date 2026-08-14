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

            _ => null
        };

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
