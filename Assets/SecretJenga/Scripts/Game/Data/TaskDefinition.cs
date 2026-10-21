using System.Collections.Generic;
using SecretJenga.Networking;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Data for one secret personal task. The evaluation logic lives in SecretTaskManager (one case per TaskKind);
    /// everything a designer may want to tweak - wording, targets, thresholds, whether it is in the pool - is here.
    /// Create more with Assets > Create > The Last Block > Task Definition and add them to the asset registry.
    ///
    /// Description tokens: {target} {rows} {threshold} {thresholdNum} {tolerance} {milestone}
    /// </summary>
    [CreateAssetMenu(menuName = "The Last Block/Task Definition", fileName = "Task_New")]
    public class TaskDefinition : ScriptableObject
    {
        public string id = "task";
        public string title = "New Task";
        [TextArea(2, 5)] public string description = "";
        public TaskKind kind = TaskKind.None;
        [Tooltip("Include this task when tasks are dealt.")]
        public bool inPool = true;

        [Header("Points")]
        [Tooltip("Points for completing this task. 0 = default for its kind (see ScoreRules).")]
        [Min(0)] public int bonusPoints = 0;

        [Header("Numbers")]
        [Min(1)] public int target = 2;
        [Tooltip("Lower the target when a player will get fewer turns than that this round (keeps every task achievable).")]
        public bool scaleTargetToTurns = true;
        [Tooltip("Risk Taker: rows 0..maxRiskyRow (counted from the table) are 'risky'.")]
        public int maxRiskyRow = 3;
        [Tooltip("Steady Hands: highest allowed tower wobble during the turn (0..1).")]
        [Range(0.05f, 1f)] public float wobbleThreshold = 0.3f;
        [Tooltip("Careful Builder: max displacement of any other block, in world units (1 unit = 5 cm).")]
        public float displacementTolerance = 0.2f;
        [Tooltip("Tower Keeper: fraction of GameRules.TaskTurnHint (group turns) that counts as the milestone.")]
        [Range(0.2f, 1f)] public float milestoneFraction = 0.66f;

        public int MilestoneFor(int requiredTurns) => Mathf.Clamp(Mathf.RoundToInt(requiredTurns * milestoneFraction), 1, requiredTurns);

        /// <summary>Localised title (Turkish table by id, else the asset's English title).</summary>
        public string Title => L.TaskTitle(this);

        public string Describe(TaskInfo info) => Describe(info, GameRules.TaskTurnHint);

        public string Describe(TaskInfo info, int requiredTurns)
        {
            var s = L.TaskDescriptionTemplate(this);
            if (string.IsNullOrEmpty(s)) s = Title;
            int t = info.Target > 0 ? info.Target : target;
            return s.Replace("{target}", t.ToString())
                .Replace("{rows}", (maxRiskyRow + 1).ToString())
                .Replace("{thresholdNum}", Mathf.RoundToInt(wobbleThreshold * 100f).ToString())
                .Replace("{threshold}", Mathf.RoundToInt(wobbleThreshold * 100f) + "%")
                .Replace("{tolerance}", Mathf.RoundToInt(displacementTolerance * 50f) + " mm")
                .Replace("{milestone}", MilestoneFor(requiredTurns).ToString());
        }

        static TaskDefinition Make(string id, string title, TaskKind kind, int target, string desc)
        {
            var d = CreateInstance<TaskDefinition>();
            d.name = "Task_" + id;
            d.id = id;
            d.title = title;
            d.kind = kind;
            d.target = target;
            d.description = desc;
            return d;
        }

        /// <summary>The prototype task set. Used by the project generator and as a runtime fallback.</summary>
        public static List<TaskDefinition> CreateDefaults()
        {
            var risk = Make("risk_taker", "Risk Taker", TaskKind.RiskTaker, 2,
                "Successfully pull {target} block(s) from the bottom {rows} rows and place them on top.");
            risk.maxRiskyRow = 3;

            var steady = Make("steady_hands", "Steady Hands", TaskKind.SteadyHands, 2,
                "Complete {target} of your own turns in a row without the tower wobbling past {threshold}.");
            steady.wobbleThreshold = 0.3f;

            var cards = Make("card_collector", "Card Collector", TaskKind.CardCollector, 2,
                "Play your special card, then complete {target} successful turn(s) after playing it.");

            var pressure = Make("pressure_player", "Pressure Player", TaskKind.PressurePlayer, 1,
                "Play an intervention card on another player's turn. (You are guaranteed one.)");
            pressure.scaleTargetToTurns = false;

            var keeper = Make("tower_keeper", "Tower Keeper", TaskKind.TowerKeeper, 1,
                "Help the table reach {milestone} successful turns (at least one of them yours) and never let the tower fall on your turn.");
            keeper.scaleTargetToTurns = false;
            keeper.milestoneFraction = 0.66f;

            var careful = Make("careful_builder", "Careful Builder", TaskKind.CarefulBuilder, 2,
                "Place {target} block(s) on top without knocking any other block more than {tolerance} out of place.");
            careful.displacementTolerance = 0.2f;

            return new List<TaskDefinition> { risk, steady, cards, pressure, keeper, careful };
        }
    }
}
