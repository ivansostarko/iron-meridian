using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// What a strike leaves behind: **thirty minutes of fire, then two hours of
    /// smoke.**
    ///
    /// The bursts every strike system already plays last two to four seconds,
    /// which is the right length for a detonation and the wrong length for its
    /// consequence. Ten seconds after a battery has put five rounds into a
    /// position the map showed nothing at all — the ground was as clean as
    /// before the mission, and a player looking away for a moment could not tell
    /// where anything had happened. Ordnance does not work like that: what it
    /// leaves is the part that is visible for hours, and on an operational map
    /// that mark is genuinely useful information. It says which positions have
    /// been worked over and roughly how long ago.
    ///
    /// **The two phases are different statements.** Fire says *this is burning
    /// now*; smoke says *this burned*. Thirty minutes and two hours are ordinary
    /// figures for a struck position — long enough to still be there when the
    /// player comes back to the sector, short enough that a long battle does not
    /// end up carpeted.
    ///
    /// **Both are measured in scenario time**, through
    /// <see cref="GameClock.ScenarioDelta"/>. Thirty minutes of fire means
    /// thirty minutes on the operational clock: half an hour at x1, thirty
    /// seconds at x60, and frozen while the battle is paused. Real seconds would
    /// have meant a fire that outlived a whole day of fighting at high speed and
    /// vanished in a blink at x1 — the same effect telling two different stories
    /// depending on a setting that has nothing to do with it.
    ///
    /// One site per **mission**, at the aim point, not one per round: a
    /// five-round salvo is one event on the ground, and five overlapping fires
    /// would be five times the particle cost for a worse picture.
    ///
    /// See docs/08-PARTICLE-SYSTEMS.md.
    /// </summary>
    public class StrikeAftermath : MonoBehaviour
    {
        /// <summary>Scenario minutes the impact site burns for.</summary>
        public const float FireMinutes = 30f;
        /// <summary>Scenario minutes it smokes for once the fire is out.</summary>
        public const float SmokeMinutes = 120f;

        /// <summary>
        /// Concurrent burning/smoking sites. Well under the effect budget
        /// (<see cref="GameConfig.VfxMaxConcurrent"/>) on purpose: these are
        /// long-lived loops, and left unbounded they would fill the budget and
        /// start evicting the bursts of the strikes still landing. Past the cap
        /// the oldest site is retired, which is the one whose story is furthest
        /// from being news.
        /// </summary>
        const int MaxSites = 20;

        public static StrikeAftermath Active { get; private set; }

        enum Phase { Fire, Smoke }

        class Site
        {
            public double lat, lon;
            public float scale;
            public Phase phase;
            /// <summary>Scenario seconds left in the current phase.</summary>
            public float remaining;
            public VfxInstance live;
        }

        readonly List<Site> _sites = new List<Site>();

        void Awake() => Active = this;

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>
        /// Marks a place something landed. Safe to call when the system is not
        /// running — every strike system calls it unconditionally, exactly as
        /// they call <see cref="VfxSystem.Play"/>.
        /// </summary>
        public static void Play(double lat, double lon, float scale = 1f)
        {
            if (Active != null) Active.Begin(lat, lon, scale);
        }

        void Begin(double lat, double lon, float scale)
        {
            Trim();

            var site = new Site
            {
                lat = lat,
                lon = lon,
                scale = Mathf.Max(0.2f, scale),
                phase = Phase.Fire,
                remaining = FireMinutes * 60f
            };
            site.live = VfxSystem.Play(VfxId.StrikeAftermathFire, lat, lon, site.scale);
            _sites.Add(site);
        }

        /// <summary>Retires the oldest sites until there is room for one more.</summary>
        void Trim()
        {
            _sites.RemoveAll(s => s == null);
            while (_sites.Count >= MaxSites)
            {
                Retire(_sites[0]);
                _sites.RemoveAt(0);
            }
        }

        void Update()
        {
            if (_sites.Count == 0) return;

            float dt = GameClock.ScenarioDelta;
            if (dt <= 0f) return;                 // battle paused: the ground waits too

            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                var site = _sites[i];
                site.remaining -= dt;
                if (site.remaining > 0f) continue;

                if (site.phase == Phase.Fire)
                {
                    // Fire out, smoke up. The smoke is started where the fire
                    // was rather than being a second effect layered on top from
                    // the beginning: a column of smoke over a fire that is still
                    // burning is what the fire effect already draws.
                    Retire(site);
                    site.phase = Phase.Smoke;
                    site.remaining = SmokeMinutes * 60f;
                    site.live = VfxSystem.Play(VfxId.StrikeAftermathSmoke, site.lat, site.lon, site.scale);
                    continue;
                }

                Retire(site);
                _sites.RemoveAt(i);
            }
        }

        static void Retire(Site site)
        {
            if (site?.live == null) return;
            site.live.Stop();
            site.live = null;
        }

        /// <summary>
        /// Clears every site. Used on a map reload and by RESET, alongside
        /// <see cref="VfxSystem.StopAll"/> — the effects themselves are stopped
        /// there, and this is what stops the bookkeeping outliving them.
        /// </summary>
        public void ClearAll()
        {
            foreach (var site in _sites) Retire(site);
            _sites.Clear();
        }

        /// <summary>Sites currently burning or smoking — for the HUD and diagnostics.</summary>
        public int SiteCount => _sites.Count;
    }
}
