using System;
using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Deals one secret personal task per player and measures progress on the host.
    ///
    /// Privacy: tasks are never stored in replicated state. Each client only receives its own task through a
    /// targeted ClientRpc; every task is revealed to everyone with the round results. (The host machine necessarily
    /// knows every task because it is the authority - the host UI only shows its own.)
    ///
    /// Fairness: no task requires another player to fail or the tower to fall. Numeric targets are lowered to the
    /// number of turns a player can expect in a typical round (GameRules.TaskTurnHint), so every task stays achievable.
    /// The round itself has no turn limit: it lasts until the tower falls, and whoever drops it loses.
    /// </summary>
    public class SecretTaskManager : NetworkBehaviour
    {
        // ---- client side (local player only)
        public static TaskInfo LocalTask { get; private set; }
        public static bool HasLocalTask { get; private set; }
        /// <summary>(task, justCompleted)</summary>
        public static event Action<TaskInfo, bool> LocalTaskChanged;

        // ---- server side
        class ServerTask
        {
            public TaskInfo Info;
            public TaskDefinition Def;
            public int Streak;
            public bool PlayedCard;
            public int OwnSuccessfulTurns;
            public bool CausedCollapse;
        }

        readonly Dictionary<string, ServerTask> _tasks = new Dictionary<string, ServerTask>();
        NetworkGameManager _game;

        void Awake() => _game = GetComponent<NetworkGameManager>();

        public override void OnNetworkSpawn() => ClearLocal();
        public override void OnNetworkDespawn() => ClearLocal();

        static void ClearLocal()
        {
            HasLocalTask = false;
            LocalTask = default;
            LocalTaskChanged?.Invoke(default, false);
        }

        string AuthOf(ulong clientId) => _game.TryGetPlayer(clientId, out var p) ? p.AuthId.ToString() : null;

        // ------------------------------------------------------------------ Server: dealing
        /// <summary>Deals tasks to the connected players. Returns clientId -> task kind (CardManager uses it).</summary>
        public Dictionary<ulong, TaskKind> ServerDeal(List<PlayerData> players)
        {
            var dealt = new Dictionary<ulong, TaskKind>();
            if (!IsServer) return dealt;
            _tasks.Clear();
            ClearAllClientRpc();

            var pool = new List<int>();
            var defs = GameContent.Tasks;
            bool anyCard = false;
            foreach (var c in GameContent.Cards) anyCard |= c.inPool && c.kind != CardKind.None;
            bool pressureCard = GameContent.FirstCardOfKind(CardKind.Pressure) >= 0;
            for (int i = 0; i < defs.Count; i++)
            {
                var kind = defs[i].kind;
                if (!defs[i].inPool || kind == TaskKind.None) continue;
                if (kind == TaskKind.CardCollector && !anyCard) continue;      // would be impossible without cards
                if (kind == TaskKind.PressurePlayer && !pressureCard) continue; // needs an intervention card to exist
                pool.Add(i);
            }
            if (pool.Count == 0) return dealt;
            for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

            int connected = 0;
            foreach (var p in players) if (p.IsConnected) connected++;
            int groupTurns = GameRules.TaskTurnHint;
            int expectedTurns = Mathf.Max(1, groupTurns / Mathf.Max(1, connected));

            int k = 0;
            foreach (var p in players)
            {
                if (!p.IsConnected) continue;
                int defIndex = pool[k++ % pool.Count];
                var def = defs[defIndex];
                int target = def.kind == TaskKind.TowerKeeper ? def.MilestoneFor(groupTurns) : def.target;
                if (def.scaleTargetToTurns) target = Mathf.Clamp(target, 1, expectedTurns);
                var st = new ServerTask
                {
                    Def = def,
                    Info = new TaskInfo { Kind = def.kind, DefinitionIndex = (byte)defIndex, Target = Mathf.Max(1, target) }
                };
                _tasks[p.AuthId.ToString()] = st;
                dealt[p.ClientId] = def.kind;
                SendTaskTo(p.ClientId, st.Info, false);
            }
            return dealt;
        }

        public void ServerResendTo(ulong clientId)
        {
            if (!IsServer) return;
            var auth = AuthOf(clientId);
            if (auth != null && _tasks.TryGetValue(auth, out var t)) SendTaskTo(clientId, t.Info, false);
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            _tasks.Clear();
            ClearAllClientRpc();
        }

        void SendTaskTo(ulong clientId, TaskInfo info, bool justCompleted)
        {
            if (_game.IsBot(clientId)) return; // bots keep their task on the host; it is revealed with the results
            var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            TaskUpdateClientRpc(info, justCompleted, p);
        }

        // ------------------------------------------------------------------ Server: events
        public void ServerOnTurnStarted(ulong clientId) { }

        public void ServerOnCardPlayed(ulong clientId, CardKind kind, ulong targetClientId)
        {
            if (!IsServer) return;
            var auth = AuthOf(clientId);
            if (auth == null || !_tasks.TryGetValue(auth, out var t)) return;
            t.PlayedCard = true;
            if (t.Info.Kind == TaskKind.PressurePlayer && !t.Info.Completed && kind == CardKind.Pressure && targetClientId != clientId && targetClientId != ulong.MaxValue)
            {
                int before = t.Info.Progress;
                t.Info.Progress = t.Info.Target;
                Commit(auth, t, before);
            }
        }

        public void ServerOnMove(MoveReport r)
        {
            if (!IsServer) return;
            int group = _game.Turns.MovesCompleted.Value;
            var auth = AuthOf(r.ClientId);
            ServerTask mine = null;
            if (auth != null && _tasks.TryGetValue(auth, out mine) && r.Success) mine.OwnSuccessfulTurns++;

            // Tasks that react to the whole group's progress.
            foreach (var kv in _tasks)
            {
                var t = kv.Value;
                if (t.Info.Kind != TaskKind.TowerKeeper || t.Info.Completed || t.Info.Failed) continue;
                int before = t.Info.Progress;
                t.Info.Progress = Mathf.Min(group, t.Info.Target);
                if (t.Info.Progress >= t.Info.Target && t.OwnSuccessfulTurns == 0) t.Info.Progress = t.Info.Target - 1; // needs one of your own
                Commit(kv.Key, t, before);
            }

            if (mine == null || mine.Info.Completed || mine.Info.Failed) return;
            var def = mine.Def;
            int prev = mine.Info.Progress;
            switch (mine.Info.Kind)
            {
                case TaskKind.RiskTaker:
                    if (r.Success && r.SourceLayer >= 0 && r.SourceLayer <= def.maxRiskyRow) mine.Info.Progress++;
                    break;
                case TaskKind.SteadyHands:
                    mine.Streak = r.Success && r.PeakWobble <= def.wobbleThreshold ? mine.Streak + 1 : 0;
                    mine.Info.Progress = Mathf.Min(mine.Streak, mine.Info.Target);
                    break;
                case TaskKind.CardCollector:
                    if (r.Success && mine.PlayedCard) mine.Info.Progress++;
                    break;
                case TaskKind.CarefulBuilder:
                    if (r.Success && !r.AutoPlaced && r.MaxDisplacement <= def.displacementTolerance) mine.Info.Progress++;
                    break;
            }
            Commit(auth, mine, prev);
        }

        /// <summary>The tower fell during this player's turn.</summary>
        public void ServerOnCollapse(ulong culpritClientId)
        {
            if (!IsServer) return;
            var auth = AuthOf(culpritClientId);
            if (auth == null || !_tasks.TryGetValue(auth, out var t)) return;
            t.CausedCollapse = true;
            if (t.Info.Kind == TaskKind.TowerKeeper)
            {
                t.Info.Completed = false;
                t.Info.Failed = true;
                SendTaskTo(culpritClientId, t.Info, false);
            }
        }

        void Commit(string auth, ServerTask t, int previousProgress)
        {
            bool justCompleted = false;
            if (!t.Info.Completed && !t.Info.Failed && t.Info.Progress >= t.Info.Target)
            {
                t.Info.Completed = true;
                t.Info.Progress = t.Info.Target;
                justCompleted = true;
            }
            if (justCompleted || previousProgress != t.Info.Progress)
                foreach (var p in _game.Players)
                    if (p.AuthId.ToString() == auth && p.IsConnected) SendTaskTo(p.ClientId, t.Info, justCompleted);
        }

        // ------------------------------------------------------------------ Server: results
        public bool ServerTryGetResult(string auth, out TaskInfo info, out int successfulTurns, out bool causedCollapse)
        {
            if (_tasks.TryGetValue(auth, out var t))
            {
                info = t.Info;
                successfulTurns = t.OwnSuccessfulTurns;
                causedCollapse = t.CausedCollapse;
                return true;
            }
            info = default;
            successfulTurns = 0;
            causedCollapse = false;
            return false;
        }

        // ------------------------------------------------------------------ Client RPCs
        [ClientRpc]
        void TaskUpdateClientRpc(TaskInfo info, bool justCompleted, ClientRpcParams rpcParams = default)
        {
            LocalTask = info;
            HasLocalTask = info.Kind != TaskKind.None;
            LocalTaskChanged?.Invoke(info, justCompleted);
            if (justCompleted) AudioManager.Instance?.Play(AudioManager.Sfx.MissionComplete);
        }

        [ClientRpc]
        void ClearAllClientRpc()
        {
            ClearLocal();
            RoundResults.Clear();
        }
    }
}
