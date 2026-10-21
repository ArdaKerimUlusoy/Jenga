using System.Collections.Generic;
using SecretJenga.Networking;

namespace SecretJenga.Game
{
    /// <summary>Client-side copy of the last round's outcome, filled by NetworkGameManager's reveal RPC.</summary>
    public static class RoundResults
    {
        public static List<PlayerRoundResult> Players { get; private set; } = new List<PlayerRoundResult>();
        public static bool GroupWon { get; private set; }
        public static RoundEndReason Reason { get; private set; }
        public static string ReasonText { get; private set; } = "";
        public static int SuccessfulTurns { get; private set; }
        public static int RequiredTurns { get; private set; }
        public static string CardSummary { get; private set; } = "";
        public static bool HasResults => Players.Count > 0;

        public static event System.Action Revealed;

        public static void Clear()
        {
            Players = new List<PlayerRoundResult>();
            GroupWon = false;
            Reason = RoundEndReason.None;
            ReasonText = "";
            SuccessfulTurns = RequiredTurns = 0;
            CardSummary = "";
        }

        public static void Set(PlayerRoundResult[] players, bool won, RoundEndReason reason, string reasonText, int successful, int required, string cards)
        {
            Players = new List<PlayerRoundResult>(players ?? new PlayerRoundResult[0]);
            GroupWon = won;
            Reason = reason;
            ReasonText = reasonText ?? "";
            SuccessfulTurns = successful;
            RequiredTurns = required;
            CardSummary = cards ?? "";
            Revealed?.Invoke();
        }
    }
}
