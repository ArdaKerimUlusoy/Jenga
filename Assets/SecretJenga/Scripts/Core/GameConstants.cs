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

    /// <summary>
    /// Layout of the room: one round table in the middle, five fixed seats around it.
    /// Seat k sits at angle 270 + 72k degrees (seat 0 is on the -Z side looking towards +Z).
    /// </summary>
    public static class SeatLayout
    {
        public const int SeatCount = 5;
        public const float TableRadius = 8.2f;
        public const float FloorY = -14f;          // table is ~70 cm high
        public const float ChairRadius = 11.6f;    // chair center distance from the tower
        public const float EyeRadius = 9.6f;      // seated eye distance from the tower
        public const float EyeHeight = 8.6f;       // seated eye height above the table top (high enough to see the top of a tall tower)
        public const float LampHeight = 22f;       // wagon-wheel chandelier, high enough to never hide the tower

        /// <summary>Order in which seats are handed out so that 2-4 players are spread around the table.</summary>
        public static readonly int[] FillOrder = { 0, 3, 1, 4, 2 };

        public static float AngleDeg(int seat) => 270f + 72f * Mathf.Repeat(seat, SeatCount);

        /// <summary>Horizontal unit direction from the tower towards the seat.</summary>
        public static Vector3 Outward(int seat)
        {
            float a = AngleDeg(seat) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        public static Vector3 EyePosition(int seat) => Outward(seat) * EyeRadius + Vector3.up * (JengaDims.TableTopY + EyeHeight);
        public static Vector3 ChairPosition(int seat) => Outward(seat) * ChairRadius + Vector3.up * FloorY;

        /// <summary>Rotation of a body sitting in the seat, facing the tower.</summary>
        public static Quaternion Facing(int seat) => Quaternion.LookRotation(-Outward(seat), Vector3.up);
    }

    public enum GamePhase : byte { Lobby = 0, Countdown = 1, Playing = 2, GameOver = 3 }

    public enum TurnStage : byte { None = 0, WaitingForGrab = 1, Dragging = 2, Carrying = 3, Settling = 4 }

    /// <summary>Evaluators for secret personal tasks. A TaskDefinition asset picks one and supplies the numbers.</summary>
    public enum TaskKind : byte { None = 0, RiskTaker = 1, SteadyHands = 2, CardCollector = 3, PressurePlayer = 4, TowerKeeper = 5, CarefulBuilder = 6 }

    /// <summary>Effects of the experimental special cards. A CardDefinition asset picks one and supplies the numbers.</summary>
    public enum CardKind : byte { None = 0, SteadyGrip = 1, Pressure = 2, SecondChance = 3 }

    /// <summary>When a card may be played.</summary>
    public enum CardTiming : byte { OwnTurnBeforeExtraction = 0, OwnTurnAnyStage = 1 }

    /// <summary>Who a card affects.</summary>
    public enum CardTarget : byte { Self = 0, NextPlayer = 1 }

    /// <summary>Why a round ended (shown on the results screen).</summary>
    public enum RoundEndReason : byte { None = 0, ObjectiveComplete = 1 /* unused: rounds no longer have a turn limit */, TowerCollapsed = 2, NotEnoughPlayers = 3 }

    public static class GameRules
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 5;

        // Rule: there is no turn limit. Play goes round the table until the tower falls; whoever drops it loses,
        // everybody else wins. TaskTurnHint is only the "typical round length" used to size secret task targets.
        public const int TaskTurnHint = 12;

        /// <summary>Rounds per match; points add up over the match and the highest total wins (see ScoreRules).</summary>
        public const int DefaultMatchRounds = 3;
        public const int MinMatchRounds = 1;
        public const int MaxMatchRounds = 9;

        /// <summary>Bots get client ids far above anything NGO hands out.</summary>
        public const ulong BotIdBase = 9_000_000_000UL;

        public const int DefaultStartLayers = 15;
        public const int MinStartLayers = 8;
        public const int MaxStartLayers = 18;

        /// <summary>Turn timer in seconds. 0 = no timer.</summary>
        public const int DefaultTurnSeconds = 60;
        public const int MinTurnSeconds = 20;
        public const int MaxTurnSeconds = 120;

        public const float CountdownSeconds = 4f;
        public const float SettleMinSeconds = 1.2f;
        public const float SettleMaxSeconds = 6f;
        public const float SettleCalmSeconds = 0.6f;
        public const string BuildVersion = "tlb-0.2";
        public const string GameTitle = "THE LAST BLOCK";
    }

    /// <summary>Colors for a dark, restrained look. Player colors are muted so they read as small accents only.</summary>
    public static class Palette
    {
        public static readonly Color[] PlayerColors =
        {
            new Color(0.78f, 0.30f, 0.26f), // rust red
            new Color(0.36f, 0.56f, 0.72f), // cold blue
            new Color(0.48f, 0.66f, 0.42f), // moss
            new Color(0.82f, 0.66f, 0.32f), // tarnished brass
            new Color(0.62f, 0.46f, 0.72f), // faded violet
        };

        public static Color Player(int index) => PlayerColors[Mathf.Abs(index) % PlayerColors.Length];

        // UI theme
        public static readonly Color Ink = new Color(0.04f, 0.04f, 0.04f);
        public static readonly Color Cream = new Color(0.86f, 0.83f, 0.76f);      // bone white text
        public static readonly Color Paper = new Color(0.62f, 0.60f, 0.56f);      // secondary text
        public static readonly Color Walnut = new Color(0.075f, 0.072f, 0.07f);   // panel
        public static readonly Color WalnutDark = new Color(0.035f, 0.033f, 0.032f);
        public static readonly Color Accent = new Color(0.74f, 0.20f, 0.16f);     // dried blood red
        public static readonly Color AccentDark = new Color(0.40f, 0.10f, 0.08f);
        public static readonly Color Lamp = new Color(0.95f, 0.78f, 0.48f);       // tungsten lamp
        public static readonly Color Good = new Color(0.46f, 0.62f, 0.44f);
        public static readonly Color Bad = new Color(0.78f, 0.24f, 0.20f);
        public static readonly Color Muted = new Color(0.40f, 0.39f, 0.37f);
    }
}
