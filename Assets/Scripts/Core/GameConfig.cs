using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>Global constants for Iron Meridian.</summary>
    public static class GameConfig
    {
        public const string GameName = "Iron Meridian";
        public const string Version = "0.1.0-dev";

        // Scene names (created by Tools > Iron Meridian > Setup Project)
        public const string SceneMainMenu = "MainMenu";
        public const string SceneSettings = "Settings";
        public const string SceneTesting = "Testing";
        public const string SceneEastFrance = "EastFrance";
        public const string SceneUnitsList = "UnitsList";
        public const string SceneGame = "Game";

        // Default dev map: Lyon, France
        public const double LyonLatitude = 45.7640;
        public const double LyonLongitude = 4.8357;

        // Team colours
        public static readonly Color BlueTeam = new Color(0.20f, 0.55f, 1.00f);
        public static readonly Color RedTeam = new Color(0.95f, 0.25f, 0.25f);
        public static readonly Color NeutralGreen = new Color(0.45f, 0.85f, 0.45f);
        public static readonly Color UnknownYellow = new Color(0.95f, 0.90f, 0.30f);
        public static readonly Color BoundaryYellow = new Color(1.00f, 0.85f, 0.10f);
        // Red marks how far the unit can see, light blue how far it can shoot.
        public static readonly Color ViewRangeColor = new Color(0.95f, 0.30f, 0.20f);
        public static readonly Color WeaponRangeColor = new Color(0.35f, 0.80f, 0.95f);

        // UI palette
        public static readonly Color UiBackground = new Color(0.07f, 0.09f, 0.12f);
        public static readonly Color UiPanel = new Color(0.12f, 0.15f, 0.20f, 0.97f);
        public static readonly Color UiPanelLight = new Color(0.18f, 0.22f, 0.29f);
        public static readonly Color UiAccent = new Color(0.85f, 0.65f, 0.13f);
        public static readonly Color UiText = new Color(0.92f, 0.93f, 0.95f);
        public static readonly Color UiTextDim = new Color(0.60f, 0.64f, 0.70f);

        // Gameplay
        public const float CombatTickSeconds = 1.0f;
        public const float FrontlineUpdateSeconds = 3.0f;
        public const float MoveSpeedMultiplier = 60f;   // game-time acceleration for movement

        // Particle effects (see docs/08-PARTICLE-SYSTEMS.md)
        /// <summary>Hard cap on live effects; a corps-scale battle would otherwise spawn hundreds.</summary>
        public const int VfxMaxConcurrent = 48;
        /// <summary>Fraction of screen height below which a looping effect stops emitting.</summary>
        public const float VfxMinApparentSize = 0.005f;
        /// <summary>Combat ticks every second — impact puffs are throttled well below that.</summary>
        public const float VfxImpactCooldownSeconds = 1.8f;
        /// <summary>Firing signatures are rarer still; they mark "this unit is shooting", not each shot.</summary>
        public const float VfxWeaponFireCooldownSeconds = 2.6f;
        /// <summary>Strength at or below which a unit visibly burns.</summary>
        public const float VfxBurningStrength = 0.45f;
        /// <summary>How long a wreck burns, from a small loss to a catastrophic one.</summary>
        public const float VfxWreckMinSeconds = 14f;
        public const float VfxWreckMaxSeconds = 32f;
    }
}
