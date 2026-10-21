using SecretJenga.Networking;

namespace SecretJenga.Game
{
    /// <summary>
    /// Points. A match is a few rounds (host setting, default 3); every round adds to each player's total and the
    /// highest total after the last round wins the match.
    ///
    ///   survived the round (did not drop the tower) ............ +3
    ///   dropped the tower ...................................... -3
    ///   secret task completed .................................. +1 .. +4 (harder task = more)
    ///   secret task at least half done (targets of 2+) ......... +1
    ///   round abandoned (not enough players) ................... survival 0, tasks still count
    ///
    /// Balance: dropping the tower swings you 6 points against a survivor, which is more than any single task is
    /// worth - so you can never "buy" a collapse by chasing your task, and the core rule (whoever drops it loses)
    /// stays the most important thing. But a completed hard task still lifts a careful player clearly above a merely
    /// safe one (+7 vs +3), and over a 3-round match one fall can be recovered with good tasks. The riskiest task
    /// (Risk Taker: pull from the bottom rows) pays the most; the nearly guaranteed one (Pressure Player) the least.
    /// Per-task values can be overridden on each TaskDefinition asset (bonusPoints, 0 = these defaults).
    /// </summary>
    public static class ScoreRules
    {
        public const int Survived = 3;
        public const int Dropped = -3;
        public const int HalfTask = 1;

        public static int DefaultTaskPoints(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.RiskTaker: return 4;
                case TaskKind.SteadyHands: return 3;
                case TaskKind.CarefulBuilder: return 3;
                case TaskKind.TowerKeeper: return 3;
                case TaskKind.CardCollector: return 2;
                case TaskKind.PressurePlayer: return 1;
                default: return 0;
            }
        }

        /// <summary>Points a completed task is worth.</summary>
        public static int TaskValue(TaskDefinition def, TaskKind kind) =>
            def != null && def.bonusPoints > 0 ? def.bonusPoints : DefaultTaskPoints(kind);

        /// <summary>Points actually earned with this task at the end of a round.</summary>
        public static int TaskPoints(TaskInfo t)
        {
            if (t.Kind == TaskKind.None || t.Failed) return 0;
            var def = GameContent.Task(t.DefinitionIndex);
            if (t.Completed) return TaskValue(def, t.Kind);
            if (t.Target >= 2 && t.Progress * 2 >= t.Target) return HalfTask;
            return 0;
        }

        public static int SurvivalPoints(RoundEndReason reason, bool isLoser, bool connected)
        {
            if (reason != RoundEndReason.TowerCollapsed || !connected) return 0;
            return isLoser ? Dropped : Survived;
        }

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }
}
