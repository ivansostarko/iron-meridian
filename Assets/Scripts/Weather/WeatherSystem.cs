using UnityEngine;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Units;

namespace IronMeridian.Weather
{
    /// <summary>
    /// Owns the sky and the weather: sun angle and colour, ambient light, fog,
    /// precipitation, and the ambience bed.
    ///
    /// Two independent axes (see <see cref="WeatherCatalog"/>): the **sky phase**
    /// is the time of day, the **condition** is what is falling. Either can be
    /// changed without disturbing the other, which is what makes a night storm
    /// expressible. When automatic day/night is on, the clock drives the sky and
    /// the player keeps the condition.
    ///
    /// Sky and fog apply immediately in the editor so the player can see what
    /// they are choosing. The **ambience bed plays in battle mode only** — a
    /// rain loop droning while counters are being laid out is noise, not
    /// atmosphere.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Active { get; private set; }

        public SkyPhase Phase { get; private set; } = SkyPhase.Day;
        public WeatherCondition Condition { get; private set; } = WeatherCondition.Clear;

        /// <summary>When on, the sky phase follows the scenario clock rather than the manual choice.</summary>
        public bool AutoDayNight { get; private set; }

        /// <summary>
        /// The sky the player picked by hand. Saves store this rather than
        /// <see cref="Phase"/>, which is the *derived* phase while automatic
        /// day/night is on — persisting that would silently overwrite the
        /// player's choice with whatever the clock happened to say.
        /// </summary>
        public SkyPhase ManualPhase => _manualPhase;

        /// <summary>Raised whenever the applied weather changes, so the UI can repaint.</summary>
        public event System.Action Changed;

        Light _sun;
        Camera _cam;
        GameClock _clock;
        Transform _precipRoot;
        ParticleSystem _precip;
        bool _battleRunning;

        /// <summary>Sky phase the manual picker last chose; restored when auto is turned off.</summary>
        SkyPhase _manualPhase = SkyPhase.Day;
        SkyPhase _appliedPhase = SkyPhase.Day;
        float _nextAutoCheck;
        /// <summary>Camera altitude the current precipitation was sized for.</summary>
        float _precipAltitude = 400f;

        public void Init(Light sun, Camera cam, GameClock clock)
        {
            Active = this;
            _sun = sun;
            _cam = cam;
            _clock = clock;
            BuildPrecipitationRig();
            Apply();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            // Leaving the scene must not leave a rain loop running under the menus.
            AmbienceManager.Stop();
        }

        /// <summary>
        /// Restores weather from a save. Unknown or missing names fall back to
        /// the defaults rather than throwing — an older save, or one edited by
        /// hand, must still load.
        /// </summary>
        public void LoadFrom(string skyPhase, string condition, bool autoDayNight)
        {
            if (!System.Enum.TryParse(skyPhase, out SkyPhase phase)) phase = SkyPhase.Day;
            if (!System.Enum.TryParse(condition, out WeatherCondition cond)) cond = WeatherCondition.Clear;

            _manualPhase = phase;
            Condition = cond;
            AutoDayNight = autoDayNight;
            Apply();
        }

        // ------------------------------------------------------------- setters

        public void SetCondition(WeatherCondition condition)
        {
            Condition = condition;
            Apply();
        }

        public void SetPhase(SkyPhase phase)
        {
            _manualPhase = phase;
            // Choosing a sky by hand is an implicit request to stop the clock
            // overriding it; silently ignoring the click would be worse.
            AutoDayNight = false;
            Apply();
        }

        public void SetAutoDayNight(bool on)
        {
            AutoDayNight = on;
            Apply();
        }

        /// <summary>Called by the controller when battle starts or stops.</summary>
        public void SetBattleRunning(bool running)
        {
            _battleRunning = running;
            ApplyAmbience();
        }

        // -------------------------------------------------------------- update

        void Update()
        {
            // Follow the camera so precipitation is always around the viewer;
            // world simulation space keeps the particles falling straight down
            // rather than dragging sideways with the camera.
            if (_precipRoot != null && _cam != null)
            {
                _precipRoot.position = _cam.transform.position;
                _precipRoot.rotation = Quaternion.identity;
            }

            // Precipitation is sized to camera altitude, so a big zoom change
            // has to resize it — otherwise rain chosen at ground level is
            // invisible from 12 km up, and vice versa.
            if (_precip != null && _precipRoot != null && _precipRoot.gameObject.activeSelf)
            {
                float altitude = CameraAltitude();
                if (altitude > _precipAltitude * 1.5f || altitude < _precipAltitude * 0.66f)
                {
                    _precipAltitude = altitude;
                    ApplyPrecipitation(WeatherCatalog.Get(Condition));
                }
            }

            if (!AutoDayNight || _clock == null) return;
            // Four checks a second is ample for something that changes on the
            // hour, and keeps DateTime work off the per-frame path.
            if (Time.unscaledTime < _nextAutoCheck) return;
            _nextAutoCheck = Time.unscaledTime + 0.25f;

            var phase = WeatherCatalog.PhaseForTime(_clock.Now);
            if (phase != _appliedPhase) Apply();
        }

        // --------------------------------------------------------------- apply

        void Apply()
        {
            var phase = AutoDayNight && _clock != null
                ? WeatherCatalog.PhaseForTime(_clock.Now)
                : _manualPhase;

            _appliedPhase = phase;
            Phase = phase;

            var sky = WeatherCatalog.GetSky(phase);
            var weather = WeatherCatalog.Get(Condition);

            ApplyLighting(sky, weather);
            ApplyFog(sky, weather);
            ApplyPrecipitation(weather);
            ApplyAmbience();

            Changed?.Invoke();
        }

        void ApplyLighting(SkyDef sky, WeatherDef weather)
        {
            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(sky.sunEuler);
                _sun.color = sky.sunColour * weather.lightTint;
                _sun.intensity = sky.sunIntensity * weather.lightMultiplier;
            }

            // Ambient carries the scene when the sun is low or blocked; without
            // dropping it too, night under cloud still looks like overcast noon.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = sky.ambient * Mathf.Lerp(1f, weather.lightMultiplier, 0.6f);

            if (_cam != null)
            {
                // Only meaningful where no terrain has streamed in yet, but that
                // is exactly where a bright blue void would break the mood.
                _cam.backgroundColor = Color.Lerp(sky.horizon, weather.lightTint * sky.horizon, 0.5f);
            }
        }

        void ApplyFog(SkyDef sky, WeatherDef weather)
        {
            RenderSettings.fog = weather.fog;
            if (!weather.fog) return;

            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = weather.fogDensity;
            // Fog takes the sky's colour, so distance fades toward the horizon
            // rather than toward an unrelated grey.
            RenderSettings.fogColor = Color.Lerp(sky.horizon, weather.lightTint, 0.35f);
        }

        void ApplyAmbience()
        {
            var weather = WeatherCatalog.Get(Condition);
            // Battle mode only — see the class summary.
            if (_battleRunning) AmbienceManager.Play(weather.ambience);
            else AmbienceManager.Stop();
        }

        // ------------------------------------------------------- precipitation

        /// <summary>
        /// One particle system, reconfigured per condition rather than one per
        /// weather type: only ever one can be falling, and rebuilding on every
        /// change would churn allocations for no gain.
        /// </summary>
        void BuildPrecipitationRig()
        {
            var root = new GameObject("Precipitation");
            _precipRoot = root.transform;

            _precip = root.AddComponent<ParticleSystem>();
            _precip.Stop();

            var main = _precip.main;
            main.loop = true;
            main.playOnAwake = false;
            // World space: the camera moves through the weather, it does not
            // carry it. Local space would make rain slide with every pan.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 4000;

            var shape = _precip.shape;
            shape.shapeType = ParticleSystemShapeType.Box;

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.material = RuntimeMaterials.UnlitTexture(ProceduralTextures.Puff(Color.white, 32, 1.4f));
            renderer.alignment = ParticleSystemRenderSpace.View;

            root.SetActive(false);
        }

        void ApplyPrecipitation(WeatherDef weather)
        {
            if (_precip == null) return;

            if (weather.precipitation == Precipitation.None)
            {
                _precip.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _precipRoot.gameObject.SetActive(false);
                return;
            }

            _precipRoot.gameObject.SetActive(true);

            // The strategic camera sits anywhere from 100 m to tens of km up.
            // Sizing the emitter volume and the drops to camera altitude is what
            // keeps precipitation legible at every zoom instead of vanishing.
            _precipAltitude = CameraAltitude();
            float box = Mathf.Clamp(_precipAltitude * 1.6f, 300f, 9000f);
            float scale = box / 600f;

            bool snow = weather.precipitation == Precipitation.Snow;

            var main = _precip.main;
            main.startLifetime = snow ? 6f : 2.2f;
            main.startSpeed = (snow ? 22f : 190f) * Mathf.Clamp(scale, 0.6f, 4f);
            main.startSize = (snow ? 3.2f : 2.4f) * scale;
            main.startColor = snow
                ? new Color(1f, 1f, 1f, 0.85f)
                : new Color(0.72f, 0.80f, 0.92f, 0.55f);
            main.gravityModifier = 0f;

            var shape = _precip.shape;
            shape.scale = new Vector3(box, 1f, box);
            shape.position = new Vector3(0f, box * 0.5f, 0f);
            shape.rotation = new Vector3(90f, 0f, 0f);   // emit downward

            var emission = _precip.emission;
            emission.rateOverTime = weather.particleRate;

            var renderer = _precip.GetComponent<ParticleSystemRenderer>();
            if (snow)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            else
            {
                // Stretched billboards read as falling streaks; round dots read
                // as snow whatever the speed.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.06f;
                renderer.lengthScale = 2.5f;
            }

            var noise = _precip.noise;
            noise.enabled = snow;          // snow drifts; rain does not
            if (snow)
            {
                noise.strength = 6f * scale;
                noise.frequency = 0.18f;
                noise.scrollSpeed = 0.4f;
            }

            if (!_precip.isPlaying) _precip.Play();
        }

        /// <summary>Height of the camera above the georeference origin, in metres.</summary>
        float CameraAltitude()
        {
            if (_cam == null) return 400f;
            // The strategy rig keeps the camera above the focus point, so its
            // local Y is a good enough stand-in for altitude without another
            // terrain sample every frame.
            return Mathf.Abs(_cam.transform.position.y);
        }
    }
}
