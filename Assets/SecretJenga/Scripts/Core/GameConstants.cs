using UnityEngine;

namespace SecretJenga
{
    /// <summary>Physical dimensions of the game world (1 unit ~ 5 cm; blocks are scaled up for stable physics).</summary>
    public static class JengaDims
    {
        public const float BlockLength = 1.5f;
        public const float BlockWidth = 0.5f;
        public const float BlockHeight = 0.3f;
        public const float Gap = 0.008f;          // gap between blocks inside a layer
        public const float Bevel = 0.035f;
        public const float TableTopY = 0f;        // world Y of the table surface
        public const float BlockMass = 1f;

        public static float SlotSpacing => BlockWidth + Gap;
        public static Vector3 BlockSize => new Vector3(BlockLength, BlockHeight, BlockWidth);

        /// <summary>Center Y of a block sitting in logical layer `layer` on a perfect tower.</summary>
        public static float LayerCenterY(int layer) => TableTopY + BlockHeight * 0.5f + layer * BlockHeight;
    }

    public enum BlockColor : byte { Natural = 0, Red = 1, Blue = 2, Green = 3, Gold = 4 }

    public enum GamePhase : byte { Lobby = 0, Countdown = 1, Playing = 2, GameOver = 3 }

    public enum TurnStage : byte { None = 0, WaitingForGrab = 1, Dragging = 2, Carrying = 3, Settling = 4 }

    public enum MissionType : byte { None = 0, Architect = 1, SpeedDemon = 2, ColorHunter = 3, Guardian = 4, Saboteur = 5, Master = 6, RiskTaker = 7 }

    public static class GameRules
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;
        public const int DefaultTargetHeight = 15;
        public const int DefaultStartLayers = 10;
        public const int DefaultTurnSeconds = 30;
        public const int MinTargetHeight = 8;
        public const int MaxTargetHeight = 30;
        public const int MinStartLayers = 6;
        public const int MaxStartLayers = 18;
        public const float CountdownSeconds = 3f;
        public const float SettleMinSeconds = 1.2f;
        public const float SettleMaxSeconds = 6f;
        public const float SettleCalmSeconds = 0.6f;
        public const float SpeedDemonSeconds = 10f;
        public const string BuildVersion = "sj-1.0";
    }

    public static class Palette
    {
        public static readonly Color[] PlayerColors =
        {
            new Color(0.93f, 0.36f, 0.30f), // coral
            new Color(0.25f, 0.62f, 0.95f), // sky
            new Color(0.40f, 0.80f, 0.42f), // leaf
            new Color(0.98f, 0.76f, 0.26f), // sun
            new Color(0.70f, 0.45f, 0.95f), // violet
            new Color(0.20f, 0.82f, 0.78f), // teal
            new Color(0.97f, 0.52f, 0.78f), // pink
            new Color(0.95f, 0.60f, 0.25f), // orange
        };

        public static Color Player(int index) => PlayerColors[Mathf.Abs(index) % PlayerColors.Length];

        public static Color BlockTint(BlockColor c)
        {
            switch (c)
            {
                case BlockColor.Red: return new Color(0.95f, 0.38f, 0.32f);
                case BlockColor.Blue: return new Color(0.42f, 0.62f, 1.0f);
                case BlockColor.Green: return new Color(0.48f, 0.85f, 0.45f);
                case BlockColor.Gold: return new Color(1.0f, 0.82f, 0.38f);
                default: return Color.white;
            }
        }

        public static Color BlockUIColor(BlockColor c)
        {
            switch (c)
            {
                case BlockColor.Red: return new Color(0.90f, 0.28f, 0.24f);
                case BlockColor.Blue: return new Color(0.26f, 0.50f, 0.95f);
                case BlockColor.Green: return new Color(0.30f, 0.72f, 0.32f);
                case BlockColor.Gold: return new Color(0.95f, 0.75f, 0.20f);
                default: return new Color(0.85f, 0.70f, 0.50f);
            }
        }

        // UI theme
        public static readonly Color Ink = new Color(0.16f, 0.10f, 0.07f);
        public static readonly Color Cream = new Color(0.99f, 0.95f, 0.87f);
        public static readonly Color Paper = new Color(0.96f, 0.90f, 0.80f);
        public static readonly Color Walnut = new Color(0.29f, 0.18f, 0.11f);
        public static readonly Color WalnutDark = new Color(0.17f, 0.10f, 0.06f);
        public static readonly Color Accent = new Color(0.95f, 0.62f, 0.22f);
        public static readonly Color AccentDark = new Color(0.78f, 0.42f, 0.12f);
        public static readonly Color Good = new Color(0.36f, 0.75f, 0.40f);
        public static readonly Color Bad = new Color(0.86f, 0.30f, 0.26f);
        public static readonly Color Muted = new Color(0.55f, 0.47f, 0.40f);
    }
}
