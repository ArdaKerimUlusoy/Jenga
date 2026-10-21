using System.Collections.Generic;
using SecretJenga.Networking;

namespace SecretJenga.Game
{
    /// <summary>Client-side copy of the last round's outcome, filled by NetworkGameManager's reveal RPC.</summary>
    public static class RoundResults
    {
        public static List<PlayerRoundResult> Players { get; private set; } = new List<PlayerRoundResult>();
        /// <summary>Who dropped the tower (ulong.MaxValue = nobody).</summary>
        public static ulong LoserId { get; private set; } = ulong.MaxValue;
        public static RoundEndReason Reason { get; private set; }
        /// <summary>Raw (token) text; call L.Resolve before showing it.</summary>
        public static string ReasonText { get; private set; } = "";
        public static int SuccessfulTurns { get; private set; }
        public static string CardSummary { get; private set; } = "";
        /// <summary>Round number in the match (1-based) and the match length.</summary>
        public static int Round { get; private set; }
        public static int Rounds { get; private set; }
        public static bool MatchOver => Rounds > 0 && Round >= Rounds;
        public static bool HasResults => Players.Count > 0;
        public static bool HasLoser => LoserId != ulong.MaxValue;

        public static event System.Action Revealed;

        public static void Clear()
        {
            Players = new List<PlayerRoundResult>();
            LoserId = ulong.MaxValue;
            Reason = RoundEndReason.None;
            ReasonText = "";
            SuccessfulTurns = 0;
            CardSummary = "";
            Round = Rounds = 0;
        }

        public static void Set(PlayerRoundResult[] players, ulong loser, RoundEndReason reason, string reasonText, int successful, string cards, int round, int rounds)
        {
            Round = round;
            Rounds = rounds;
            Players = new List<PlayerRoundResult>(players ?? new PlayerRoundResult[0]);
            LoserId = loser;
            Reason = reason;
            ReasonText = reasonText ?? "";
            SuccessfulTurns = successful;
            CardSummary = cards ?? "";
            Revealed?.Invoke();
        }
    }
}
