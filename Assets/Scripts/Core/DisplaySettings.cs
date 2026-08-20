using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// The video settings the player owns, and the one place they are applied.
    ///
    /// **Why a class rather than settings-screen code.** Quality settings are
    /// process-wide and Unity resets none of them for you, so the screen that
    /// *changes* a setting cannot also be the only thing that *knows* it — the
    /// game has to come up in the state the player left it in, before any
    /// settings screen has been opened. <see cref="Apply"/> is called from the
    /// main menu, which every launch passes through.
    ///
    /// **Everything here does something.** A settings screen full of controls
    /// that write a preference and change nothing on screen is worse than a
    /// short one: it teaches the player that the screen does not work. Each
    /// property below maps to a Unity setting that visibly bites on the map.
    ///
    /// Stored in <see cref="PlayerPrefs"/> under `im.gfx.*`. Resolution and
    /// window mode stay where they were, under `im.res.*`, written by the
    /// settings screen's APPLY.
    /// </summary>
    public static class DisplaySettings
    {
        const string QualityKey = "im.gfx.quality";
        const string VSyncKey = "im.gfx.vsync";
        const string FrameCapKey = "im.gfx.framecap";
        const string AntiAliasKey = "im.gfx.aa";
        const string ShadowKey = "im.gfx.shadows";
        const string TextureKey = "im.gfx.texture";
        const string AnisoKey = "im.gfx.aniso";

        /// <summary>Frame-rate caps offered, in the order the dropdown lists them. 0 = uncapped.</summary>
        public static readonly int[] FrameCaps = { 0, 30, 60, 75, 120, 144, 240 };

        /// <summary>Anti-aliasing sample counts Unity's quality settings accept.</summary>
        public static readonly int[] AntiAliasSamples = { 0, 2, 4, 8 };

        // ------------------------------------------------------------ values

        /// <summary>Index into <c>QualitySettings.names</c>.</summary>
        public static int QualityLevel
        {
            get => PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel());
            set => PlayerPrefs.SetInt(QualityKey, value);
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            set => PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
        }

        /// <summary>Index into <see cref="FrameCaps"/>.</summary>
        public static int FrameCapIndex
        {
            get => PlayerPrefs.GetInt(FrameCapKey, 0);
            set => PlayerPrefs.SetInt(FrameCapKey, value);
        }

        /// <summary>
        /// Whether the player has ever chosen a frame cap.
        ///
        /// The difference between "they picked unlimited" and "nobody has
        /// picked anything" — which are the same stored value and very
        /// different intentions. A platform that wants to seed a default
        /// (<see cref="SteamDeck"/>) must only do so for the second.
        /// </summary>
        public static bool HasFrameCap => PlayerPrefs.HasKey(FrameCapKey);

        /// <summary>
        /// Sets the frame cap **only if the player has never chosen one**, by
        /// value in frames per second rather than by index.
        ///
        /// A platform default is a starting point, not a policy: overwriting a
        /// choice on every launch is how a settings screen stops being believed.
        /// An fps figure that is not one of <see cref="FrameCaps"/> is ignored
        /// rather than rounded — the list is what the UI can show, and a value
        /// outside it would leave that screen displaying something the player
        /// cannot get back to.
        /// </summary>
        public static void SeedFrameCap(int fps)
        {
            if (HasFrameCap) return;
            int index = System.Array.IndexOf(FrameCaps, fps);
            if (index < 0) return;
            FrameCapIndex = index;
            // With vsync on the display sets the rate and the cap is inert, so
            // seeding one means turning the other off.
            if (!PlayerPrefs.HasKey(VSyncKey)) VSync = false;
        }

        /// <summary>Index into <see cref="AntiAliasSamples"/>.</summary>
        public static int AntiAliasIndex
        {
            get => PlayerPrefs.GetInt(AntiAliasKey, 1);
            set => PlayerPrefs.SetInt(AntiAliasKey, value);
        }

        /// <summary>
        /// 0 = off, 1 = hard only, 2 = hard and soft. Named <c>ShadowLevel</c>
        /// rather than <c>ShadowQuality</c> so it does not shadow — in the C#
        /// sense — the <see cref="UnityEngine.ShadowQuality"/> enum it is
        /// assigned from.
        /// </summary>
        public static int ShadowLevel
        {
            get => PlayerPrefs.GetInt(ShadowKey, 2);
            set => PlayerPrefs.SetInt(ShadowKey, value);
        }

        /// <summary>0 = full, 1 = half, 2 = quarter — Unity's mipmap limit, inverted for reading.</summary>
        public static int TextureQuality
        {
            get => PlayerPrefs.GetInt(TextureKey, 0);
            set => PlayerPrefs.SetInt(TextureKey, value);
        }

        public static bool Anisotropic
        {
            get => PlayerPrefs.GetInt(AnisoKey, 1) == 1;
            set => PlayerPrefs.SetInt(AnisoKey, value ? 1 : 0);
        }

        // ------------------------------------------------------------- apply

        /// <summary>
        /// Pushes every stored value into Unity. Safe to call repeatedly — the
        /// main menu does, on every visit.
        ///
        /// The quality *level* goes first and on its own: switching level
        /// rewrites shadows, anti-aliasing and the rest wholesale, so anything
        /// set before it would be thrown away by it.
        /// </summary>
        public static void Apply()
        {
            var levels = QualitySettings.names;
            if (levels != null && levels.Length > 0)
            {
                int level = Mathf.Clamp(QualityLevel, 0, levels.Length - 1);
                if (level != QualitySettings.GetQualityLevel())
                    QualitySettings.SetQualityLevel(level, true);
            }

            QualitySettings.vSyncCount = VSync ? 1 : 0;

            // A cap only means anything with vsync off — with it on, the display
            // sets the rate. -1 is Unity's "as fast as it can".
            int cap = FrameCaps[Mathf.Clamp(FrameCapIndex, 0, FrameCaps.Length - 1)];
            Application.targetFrameRate = VSync || cap == 0 ? -1 : cap;

            QualitySettings.antiAliasing =
                AntiAliasSamples[Mathf.Clamp(AntiAliasIndex, 0, AntiAliasSamples.Length - 1)];

            QualitySettings.shadows = ShadowLevel switch
            {
                0 => UnityEngine.ShadowQuality.Disable,
                1 => UnityEngine.ShadowQuality.HardOnly,
                _ => UnityEngine.ShadowQuality.All
            };

            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(TextureQuality, 0, 2);
            QualitySettings.anisotropicFiltering = Anisotropic
                ? AnisotropicFiltering.ForceEnable
                : AnisotropicFiltering.Disable;
        }
    }
}
