using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Data for one experimental special card. Effects are implemented in CardManager / TurnManager (one case per
    /// CardKind) and never in the UI; this asset defines wording, timing, target, duration and tuning values.
    /// </summary>
    [CreateAssetMenu(menuName = "The Last Block/Card Definition", fileName = "Card_New")]
    public class CardDefinition : ScriptableObject
    {
        public string id = "card";
        public string title = "New Card";
        [TextArea(2, 5)] public string description = "";
        public CardKind kind = CardKind.None;
        public CardTiming timing = CardTiming.OwnTurnBeforeExtraction;
        public CardTarget target = CardTarget.Self;
        [Tooltip("Shown to players: how long the effect lasts.")]
        public string durationText = "This turn";
        public bool inPool = true;

        [Header("Steady Grip")]
        [Tooltip("Multiplier on the maximum pull speed (lower = slower, finer control).")]
        [Range(0.2f, 1f)] public float pullSpeedMultiplier = 0.6f;
        [Tooltip("How much sideways mouse movement still reaches the block (normal = 0.12).")]
        [Range(0f, 0.12f)] public float lateralFollow = 0.03f;
        [Tooltip("Friction of the held block while pulling (normal held = 0.16).")]
        [Range(0.02f, 0.2f)] public float heldFriction = 0.09f;

        [Header("Second Chance")]
        [Tooltip("Extra time granted once if the turn would time out before the block is out (fraction of the turn time).")]
        [Range(0.2f, 1f)] public float extraTimeFraction = 0.5f;

        static CardDefinition Make(string id, string title, CardKind kind, CardTiming timing, CardTarget target, string duration, string desc)
        {
            var d = CreateInstance<CardDefinition>();
            d.name = "Card_" + id;
            d.id = id;
            d.title = title;
            d.kind = kind;
            d.timing = timing;
            d.target = target;
            d.durationText = duration;
            d.description = desc;
            return d;
        }

        /// <summary>The prototype card set. Used by the project generator and as a runtime fallback.</summary>
        public static List<CardDefinition> CreateDefaults() => new List<CardDefinition>
        {
            Make("steady_grip", "Steady Grip", CardKind.SteadyGrip, CardTiming.OwnTurnBeforeExtraction, CardTarget.Self, "Until your block is out",
                "Play on your turn before your block is out. Your pull becomes slower, straighter and slides more easily. Physics still decide."),
            Make("pressure", "Pressure", CardKind.Pressure, CardTiming.OwnTurnAnyStage, CardTarget.NextPlayer, "Next player's turn",
                "Play on your turn. On their next turn, the next player must commit to the first block they touch (touch-move rule)."),
            Make("second_chance", "Second Chance", CardKind.SecondChance, CardTiming.OwnTurnAnyStage, CardTarget.Self, "This turn",
                "Play on your turn. Once: extra time if you would time out, or retry if your placed block slides off a standing tower. Cannot save a collapse."),
        };
    }
}
