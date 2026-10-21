using System;
using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Static texts for the secret missions.</summary>
    public static class MissionCatalog
    {
        public static string Title(MissionType t)
        {
            switch (t)
            {
                case MissionType.Architect: return "The Architect";
                case MissionType.SpeedDemon: return "Speed Demon";
                case MissionType.ColorHunter: return "Color Hunter";
                case MissionType.Guardian: return "The Guardian";
                case MissionType.Saboteur: return "The Saboteur";
                case MissionType.Master: return "The Master";
                case MissionType.RiskTaker: return "Risk Taker";
                default: return "No mission";
            }
        }

        public static string Description(MissionInfo m)
        {
            switch (m.Type)
            {
                case MissionType.Architect: return "Make 3 successful moves in a row (your own turns, no timeouts in between).";
                case MissionType.SpeedDemon: return $"Complete a move within {GameRules.SpeedDemonSeconds:0} seconds of your turn starting.";
                case MissionType.ColorHunter: return $"Successfully move 2 {m.TargetColor.ToString().ToUpperInvariant()} blocks to the top.";
                case MissionType.Guardian: return "Keep the tower calm: 5 moves in a row (anyone's) without strong wobbling.";
                case MissionType.Saboteur: return "Make a risky move without toppling the tower: leave a layer with only one edge block.";
                case MissionType.Master: return "Pull a block from one of the 3 bottom layers and place it on top.";
                case MissionType.RiskTaker: return "Complete a move while the tower is visibly shaking.";
                default: return "";
            }
        }

        public static bool IsSabotage(MissionType t) => t == MissionType.Saboteur || t == MissionType.RiskTaker;

        public static int TargetFor(MissionType t)
        {
            switch (t)
            {
                case MissionType.Architect: return 3;
                case MissionType.ColorHunter: return 2;
                case MissionType.Guardian: return 5;
                default: return 1;
            }
        }
    }

    /// <summary>
    /// Deals one unique secret mission per player and tracks progress on the host.
    /// Missions are never stored in replicated state: each client only receives its own mission through a targeted
    /// ClientRpc. All missions are revealed to everyone in one RPC when the match ends.
    /// (The host machine necessarily knows every mission - the host is the authority. The host UI only shows its own.)
    /// </summary>
    public class SecretMissionManager : NetworkBehaviour
    {
        public const float CalmWobble = 0.2f;
        public const float ShakySecondsNeeded = 0.25f;

        // ---- client side (local player only)
        public static MissionInfo LocalMission { get; private set; }
        public static bool HasLocalMission { get; private set; }
        public static event Action<MissionInfo, bool> LocalMissionChanged; // (mission, justCompleted)
        public static List<MissionResult> LastResults { get; private set; } = new List<MissionResult>();
        public static bool LastTeamWon { get; private set; }
        public static event Action ResultsRevealed;

        // ---- server side
        class ServerMission
        {
            public MissionInfo Info;
            public int ArchitectStreak;
            public int SuccessfulMoves;
        }

        readonly Dictionary<string, ServerMission> _missions = new Dictionary<string, ServerMission>();
        int _calmStreak;
        NetworkGameManager _game;

        void Awake() => _game = GetComponent<NetworkGameManager>();

        public override void OnNetworkSpawn()
        {
            ClearLocal();
        }

        public override void OnNetworkDespawn()
        {
            ClearLocal();
        }

        static void ClearLocal()
        {
            HasLocalMission = false;
            LocalMission = default;
            LocalMissionChanged?.Invoke(default, false);
        }

        string AuthOf(ulong clientId) => _game.TryGetPlayer(clientId, out var p) ? p.AuthId.ToString() : null;

        // ------------------------------------------------------------------ Server
        public void ServerDeal(List<PlayerData> players, JengaTower tower)
        {
            if (!IsServer) return;
            _missions.Clear();
            _calmStreak = 0;
            ClearAllClientRpc();

            var pool = new List<MissionType>
            {
                MissionType.Architect, MissionType.SpeedDemon, MissionType.ColorHunter, MissionType.Guardian,
                MissionType.Saboteur, MissionType.Master, MissionType.RiskTaker
            };
            for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

            // Make sure at least one sabotage-flavoured mission is in play when there are 3+ players (tension!)
            if (players.Count >= 3)
            {
                bool any = false;
                for (int i = 0; i < players.Count; i++) any |= MissionCatalog.IsSabotage(pool[i]);
                if (!any)
                {
                    int idx = pool.FindIndex(MissionCatalog.IsSabotage);
                    int swap = UnityEngine.Random.Range(0, players.Count);
                    (pool[idx], pool[swap]) = (pool[swap], pool[idx]);
                }
            }

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p.IsConnected) continue;
                var type = pool[i % pool.Count];
                var info = new MissionInfo { Type = type, Target = MissionCatalog.TargetFor(type) };
                if (type == MissionType.ColorHunter)
                {
                    var options = new List<BlockColor>();
                    foreach (BlockColor c in new[] { BlockColor.Red, BlockColor.Blue, BlockColor.Green, BlockColor.Gold })
                        if (tower.CountExtractableColor(c) >= 2) options.Add(c);
                    info.TargetColor = options.Count > 0 ? options[UnityEngine.Random.Range(0, options.Count)] : BlockColor.Red;
                }
                _missions[p.AuthId.ToString()] = new ServerMission { Info = info };
                SendTo(p.ClientId, info, false);
            }
        }

        public void ServerResendTo(ulong clientId)
        {
            if (!IsServer) return;
            var auth = AuthOf(clientId);
            if (auth != null && _missions.TryGetValue(auth, out var m)) SendTo(clientId, m.Info, false);
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            _missions.Clear();
            _calmStreak = 0;
            ClearAllClientRpc();
        }

        void SendTo(ulong clientId, MissionInfo info, bool justCompleted)
        {
            var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            MissionUpdateClientRpc(info, justCompleted, p);
        }

        public void ServerOnMove(MoveReport r)
        {
            if (!IsServer) return;

            // Global calm streak for Guardians (counts every player's successful move).
            if (r.Success) _calmStreak = r.PeakWobble < CalmWobble ? _calmStreak + 1 : 0;

            foreach (var kv in _missions)
            {
                var m = kv.Value;
                if (m.Info.Type == MissionType.Guardian && !m.Info.Completed)
                {
                    int before = m.Info.Progress;
                    m.Info.Progress = Mathf.Min(m.Info.Target, Mathf.Max(m.Info.Progress, _calmStreak));
                    if (r.Success && _calmStreak == 0) m.Info.Progress = 0; // streak broken
                    Commit(kv.Key, m, before);
                }
            }

            var auth = AuthOf(r.ClientId);
            if (auth == null || !_missions.TryGetValue(auth, out var mine)) return;
            if (r.Success) mine.SuccessfulMoves++;
            if (mine.Info.Completed && mine.Info.Type != MissionType.Architect) return;

            int prev = mine.Info.Progress;
            switch (mine.Info.Type)
            {
                case MissionType.Architect:
                    mine.ArchitectStreak = r.Success ? mine.ArchitectStreak + 1 : 0;
                    if (!mine.Info.Completed) mine.Info.Progress = Mathf.Min(mine.ArchitectStreak, mine.Info.Target);
                    break;
                case MissionType.SpeedDemon:
                    if (r.Success && !r.AutoPlaced && r.Seconds <= GameRules.SpeedDemonSeconds) mine.Info.Progress = 1;
                    break;
                case MissionType.ColorHunter:
                    if (r.Success && r.Color == mine.Info.TargetColor) mine.Info.Progress++;
                    break;
                case MissionType.Saboteur:
                    if (r.Success && r.RiskyStructure) mine.Info.Progress = 1;
                    break;
                case MissionType.Master:
                    if (r.Success && r.SourceLayer >= 0 && r.SourceLayer <= 2) mine.Info.Progress = 1;
                    break;
                case MissionType.RiskTaker:
                    if (r.Success && r.ShakyHoldSeconds >= ShakySecondsNeeded) mine.Info.Progress = 1;
                    break;
            }
            Commit(auth, mine, prev);
        }

        void Commit(string auth, ServerMission m, int previousProgress)
        {
            bool justCompleted = false;
            if (!m.Info.Completed && m.Info.Progress >= m.Info.Target)
            {
                m.Info.Completed = true;
                m.Info.Progress = m.Info.Target;
                justCompleted = true;
            }
            if (justCompleted || previousProgress != m.Info.Progress)
            {
                foreach (var p in _game.Players)
                    if (p.AuthId.ToString() == auth && p.IsConnected) SendTo(p.ClientId, m.Info, justCompleted);
            }
        }

        public void ServerReveal(bool teamWon, List<PlayerData> players)
        {
            if (!IsServer) return;
            var results = new List<MissionResult>();
            foreach (var p in players)
            {
                _missions.TryGetValue(p.AuthId.ToString(), out var m);
                results.Add(new MissionResult
                {
                    ClientId = p.ClientId,
                    Name = p.Name,
                    ColorIndex = p.ColorIndex,
                    Mission = m != null ? m.Info : default,
                    SuccessfulMoves = m != null ? m.SuccessfulMoves : 0,
                });
            }
            RevealClientRpc(results.ToArray(), teamWon);
        }

        // ------------------------------------------------------------------ Client RPCs
        [ClientRpc]
        void MissionUpdateClientRpc(MissionInfo info, bool justCompleted, ClientRpcParams rpcParams = default)
        {
            LocalMission = info;
            HasLocalMission = info.Type != MissionType.None;
            LocalMissionChanged?.Invoke(info, justCompleted);
            if (justCompleted) AudioManager.Instance?.Play(AudioManager.Sfx.MissionComplete);
        }

        [ClientRpc]
        void ClearAllClientRpc()
        {
            ClearLocal();
            LastResults = new List<MissionResult>();
        }

        [ClientRpc]
        void RevealClientRpc(MissionResult[] results, bool teamWon)
        {
            LastResults = new List<MissionResult>(results);
            LastTeamWon = teamWon;
            ResultsRevealed?.Invoke();
        }
    }
}
