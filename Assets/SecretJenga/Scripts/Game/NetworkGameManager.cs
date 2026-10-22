using System;
using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Host-authoritative round state: player roster and seats, room settings, game phase and round flow
    /// (lobby -> countdown -> playing -> results -> play again / back to lobby). Rule: there is no turn limit - the
    /// round lasts until the tower falls, and the player whose turn it was loses; everyone else wins.
    /// The host can seat bots (simulated players) so the game can be tried alone. Lives on the networked session
    /// prefab together with TurnManager, JengaTower, SecretTaskManager, CardManager, TowerStabilityChecker and
    /// AtmosphereSync, so all game-wide state arrives on clients as one object.
    /// </summary>
    [RequireComponent(typeof(TurnManager), typeof(JengaTower), typeof(SecretTaskManager))]
    [RequireComponent(typeof(TowerStabilityChecker), typeof(CardManager), typeof(AtmosphereSync))]
    [RequireComponent(typeof(HandSync))]
    public class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance { get; private set; }
        public static GamePhase LastKnownPhase { get; private set; }

        public static event Action<NetworkGameManager> Spawned;
        public static event Action Despawned;
        /// <summary>Roster, settings or phase changed (UI refresh hook).</summary>
        public event Action StateChanged;
        public static event Action<string> Notification;
        /// <summary>Raised on every client when the round ends. Argument: did the LOCAL player win (did not drop the tower)?</summary>
        public static event Action<bool> MatchEnded;

        public NetworkList<PlayerData> Players;
        public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        public readonly NetworkVariable<int> StartLayers = new NetworkVariable<int>(GameRules.DefaultStartLayers);
        /// <summary>Turn timer in seconds, 0 = off.</summary>
        public readonly NetworkVariable<int> TurnSeconds = new NetworkVariable<int>(GameRules.DefaultTurnSeconds);
        public readonly NetworkVariable<int> MaxPlayers = new NetworkVariable<int>(GameRules.MaxPlayers);
        public readonly NetworkVariable<FixedString32Bytes> RoomCode = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<FixedString64Bytes> RoomName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<bool> IsDirectSession = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> CountdownEndTime = new NetworkVariable<double>();
        /// <summary>The player who dropped the tower (ulong.MaxValue = nobody, e.g. abandoned round).</summary>
        public readonly NetworkVariable<ulong> LoserClientId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<RoundEndReason> EndReasonKind = new NetworkVariable<RoundEndReason>();
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();
        public readonly NetworkVariable<int> MatchIndex = new NetworkVariable<int>();
        /// <summary>Rounds per match (points add up; highest total after the last round wins).</summary>
        public readonly NetworkVariable<int> MatchRounds = new NetworkVariable<int>(GameRules.DefaultMatchRounds);
        /// <summary>Current round of the match (1-based, 0 = no match yet).</summary>
        public readonly NetworkVariable<int> RoundNumber = new NetworkVariable<int>();

        public TurnManager Turns { get; private set; }
        public JengaTower Tower { get; private set; }
        public SecretTaskManager Tasks { get; private set; }
        public CardManager Cards { get; private set; }
        public TowerStabilityChecker Stability { get; private set; }
        public AtmosphereSync Atmosphere { get; private set; }

        int _nextJoinOrder;
        /// <summary>Host: penalty points collected this round, per player auth id.</summary>
        readonly Dictionary<string, int> _penalties = new Dictionary<string, int>();

        public void ServerAddPenalty(ulong clientId)
        {
            if (!IsServer || !TryGetPlayer(clientId, out var p)) return;
            var auth = p.AuthId.ToString();
            _penalties.TryGetValue(auth, out var v);
            _penalties[auth] = v + ScoreRules.TimeoutPenalty;
        }

        public double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        void Awake()
        {
            Players = new NetworkList<PlayerData>();
            Turns = GetComponent<TurnManager>();
            Tower = GetComponent<JengaTower>();
            Tasks = GetComponent<SecretTaskManager>();
            Cards = GetComponent<CardManager>();
            Stability = GetComponent<TowerStabilityChecker>();
            Atmosphere = GetComponent<AtmosphereSync>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Players.OnListChanged += OnPlayersChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            StartLayers.OnValueChanged += OnIntChanged;
            TurnSeconds.OnValueChanged += OnIntChanged;
            MaxPlayers.OnValueChanged += OnIntChanged;
            MatchRounds.OnValueChanged += OnIntChanged;
            RoundNumber.OnValueChanged += OnIntChanged;
            RoomCode.OnValueChanged += OnCodeChanged;
            LastKnownPhase = Phase.Value;

            if (IsServer && NetworkSessionManager.Instance != null && NetworkSessionManager.Instance.LocalPayload != null)
                ServerRegisterPlayer(NetworkManager.LocalClientId, NetworkSessionManager.Instance.LocalPayload);

            Spawned?.Invoke(this);
            StateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            Players.OnListChanged -= OnPlayersChanged;
            Phase.OnValueChanged -= OnPhaseChanged;
            StartLayers.OnValueChanged -= OnIntChanged;
            TurnSeconds.OnValueChanged -= OnIntChanged;
            MaxPlayers.OnValueChanged -= OnIntChanged;
            MatchRounds.OnValueChanged -= OnIntChanged;
            RoundNumber.OnValueChanged -= OnIntChanged;
            RoomCode.OnValueChanged -= OnCodeChanged;
            if (Instance == this) Instance = null;
            Despawned?.Invoke();
        }

        void OnPlayersChanged(NetworkListEvent<PlayerData> e) => StateChanged?.Invoke();
        void OnIntChanged(int a, int b) => StateChanged?.Invoke();
        void OnCodeChanged(FixedString32Bytes a, FixedString32Bytes b) => StateChanged?.Invoke();

        void OnPhaseChanged(GamePhase previous, GamePhase current)
        {
            LastKnownPhase = current;
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Queries (valid on every peer)
        public int ConnectedCount
        {
            get
            {
                int n = 0;
                foreach (var p in Players) if (p.IsConnected) n++;
                return n;
            }
        }

        public bool TryGetPlayer(ulong clientId, out PlayerData data)
        {
            foreach (var p in Players)
                if (p.ClientId == clientId) { data = p; return true; }
            data = default;
            return false;
        }

        public string PlayerName(ulong clientId) => TryGetPlayer(clientId, out var p) ? p.Name.ToString() : "?";
        public Color PlayerColor(ulong clientId) => TryGetPlayer(clientId, out var p) ? p.Color : Color.white;

        public bool IsLocalHost => IsServer;

        public bool IsBot(ulong clientId) => clientId >= GameRules.BotIdBase || (TryGetPlayer(clientId, out var p) && p.IsBot);

        public int BotCount
        {
            get
            {
                int n = 0;
                foreach (var p in Players) if (p.IsBot) n++;
                return n;
            }
        }

        /// <summary>The last round of the match has been played (shown on the results screen).</summary>
        public bool MatchOver => RoundNumber.Value >= MatchRounds.Value && RoundNumber.Value > 0;

        /// <summary>True when the local player lost the last round.</summary>
        public bool LocalLost => NetworkManager != null && LoserClientId.Value == NetworkManager.LocalClientId;

        public PlayerData? LocalPlayer
        {
            get
            {
                if (NetworkManager == null) return null;
                return TryGetPlayer(NetworkManager.LocalClientId, out var p) ? p : (PlayerData?)null;
            }
        }

        public List<PlayerData> PlayersSorted()
        {
            var list = new List<PlayerData>();
            foreach (var p in Players) list.Add(p);
            list.Sort((a, b) => a.JoinOrder.CompareTo(b.JoinOrder));
            return list;
        }

        int IndexByClient(ulong clientId)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].ClientId == clientId) return i;
            return -1;
        }

        int IndexByAuth(string authId)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].AuthId.ToString() == authId) return i;
            return -1;
        }

        public bool CanStart(out string reason)
        {
            int connected = ConnectedCount;
            bool solo = IsServer && GameSettings.AllowSoloStart;
            if (Phase.Value != GamePhase.Lobby) { reason = L.Msg("already_running"); return false; }
            if (connected < GameRules.MinPlayers && !solo) { reason = L.Msg("need_two"); return false; }
            foreach (var p in Players)
                if (p.IsConnected && !p.IsHost && !p.IsBot && !p.IsReady) { reason = L.Msg("wait_ready"); return false; }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ Server: room / roster
        public void ServerApplySettings(RoomSettings s)
        {
            MaxPlayers.Value = Mathf.Clamp(s.maxPlayers, GameRules.MinPlayers, GameRules.MaxPlayers);
            ApplyGameSettings(s.startLayers, s.turnSeconds, s.matchRounds);
            RoomName.Value = new FixedString64Bytes(Truncate(s.roomName, 60));
            IsDirectSession.Value = s.direct;
        }

        void ApplyGameSettings(int startLayers, int turnSeconds, int matchRounds)
        {
            MatchRounds.Value = Mathf.Clamp(matchRounds, GameRules.MinMatchRounds, GameRules.MaxMatchRounds);
            StartLayers.Value = Mathf.Clamp(startLayers, GameRules.MinStartLayers, GameRules.MaxStartLayers);
            TurnSeconds.Value = turnSeconds <= 0 ? 0 : Mathf.Clamp(turnSeconds, GameRules.MinTurnSeconds, GameRules.MaxTurnSeconds);
        }

        public void ServerSetRoomInfo(string code, string roomName)
        {
            RoomCode.Value = new FixedString32Bytes(code ?? "");
            RoomName.Value = new FixedString64Bytes(Truncate(roomName, 60));
        }

        static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) : s);

        public bool ServerCanAccept(ConnectionPayload p, out string reason)
        {
            int idx = IndexByAuth(p.authId);
            if (idx >= 0)
            {
                if (Players[idx].IsConnected) { reason = L.Resolve(L.Msg("already_conn")); return false; }
                reason = null;
                return true; // reconnecting player
            }
            if (Phase.Value != GamePhase.Lobby) { reason = L.Resolve(L.Msg("already_started")); return false; }
            if (ConnectedCount >= MaxPlayers.Value) { reason = L.Resolve(L.Msg("table_full")); return false; }
            reason = null;
            return true;
        }

        public void ServerRegisterPlayer(ulong clientId, ConnectionPayload p)
        {
            if (!IsServer || p == null) return;
            int idx = IndexByAuth(p.authId);
            if (idx >= 0)
            {
                var existing = Players[idx];
                ulong oldId = existing.ClientId;
                existing.ClientId = clientId;
                existing.IsConnected = true;
                Players[idx] = existing;
                Turns.ServerOnPlayerReconnected(oldId, clientId);
                Tasks.ServerResendTo(clientId);
                Cards.ServerResendTo(clientId);
                ServerNotify(L.Msg("back", existing.Name));
                return;
            }

            var data = new PlayerData
            {
                ClientId = clientId,
                AuthId = new FixedString64Bytes(p.authId ?? clientId.ToString()),
                Name = new FixedString32Bytes(UniqueName(GameSettings.SanitizeName(p.playerName))),
                ColorIndex = (byte)PickColor(p.colorPref),
                SeatIndex = (byte)PickSeat(),
                IsHost = clientId == NetworkManager.ServerClientId,
                IsConnected = true,
                JoinOrder = _nextJoinOrder++,
            };
            data.IsReady = data.IsHost;
            Players.Add(data);
            ServerNotify(L.Msg("sits", data.Name));
        }

        // ------------------------------------------------------------------ Server: bots
        static readonly string[] BotNames = { "Ayşe", "Mert", "Elif", "Can", "Defne", "Arda", "Zeynep", "Emir" };
        ulong _nextBotId = GameRules.BotIdBase;

        public bool ServerAddBot()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby) return false;
            if (ConnectedCount >= MaxPlayers.Value || Players.Count >= SeatLayout.SeatCount) return false;
            string name = BotNames[UnityEngine.Random.Range(0, BotNames.Length)];
            for (int i = 0; i < BotNames.Length; i++)
            {
                bool used = false;
                foreach (var p in Players) if (p.Name.ToString() == BotNames[i]) used = true;
                if (!used) { name = BotNames[i]; break; }
            }
            ulong id = _nextBotId++;
            var data = new PlayerData
            {
                ClientId = id,
                AuthId = new FixedString64Bytes("bot-" + id),
                Name = new FixedString32Bytes(UniqueName(name)),
                ColorIndex = (byte)PickColor(-1),
                SeatIndex = (byte)PickSeat(),
                IsHost = false,
                IsBot = true,
                IsConnected = true,
                IsReady = true,
                JoinOrder = _nextJoinOrder++,
            };
            Players.Add(data);
            ServerNotify(L.Msg("bot_join", data.Name));
            return true;
        }

        public bool ServerRemoveBot()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby) return false;
            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (!Players[i].IsBot) continue;
                var name = Players[i].Name;
                Players.RemoveAt(i);
                ServerNotify(L.Msg("bot_left", name));
                return true;
            }
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddBotServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ServerAddBot();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RemoveBotServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ServerRemoveBot();
        }

        string UniqueName(string n)
        {
            string candidate = n;
            int k = 2;
            bool Taken(string s) { foreach (var p in Players) if (p.Name.ToString() == s) return true; return false; }
            while (Taken(candidate)) candidate = n + " " + k++;
            return candidate;
        }

        int PickColor(int preferred)
        {
            bool Used(int c) { foreach (var p in Players) if (p.ColorIndex == c) return true; return false; }
            if (preferred >= 0 && preferred < Palette.PlayerColors.Length && !Used(preferred)) return preferred;
            for (int i = 0; i < Palette.PlayerColors.Length; i++) if (!Used(i)) return i;
            return 0;
        }

        int PickSeat()
        {
            bool Used(int s) { foreach (var p in Players) if (p.SeatIndex == s) return true; return false; }
            foreach (var s in SeatLayout.FillOrder) if (!Used(s)) return s;
            return 0;
        }

        public void ServerOnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            int idx = IndexByClient(clientId);
            if (idx < 0) return;
            var p = Players[idx];
            if (Phase.Value == GamePhase.Lobby || Phase.Value == GamePhase.GameOver)
            {
                Players.RemoveAt(idx);
                _ = LobbyManager.Instance?.RemovePlayerAsync(p.AuthId.ToString());
                ServerNotify(L.Msg("left", p.Name));
            }
            else
            {
                p.IsConnected = false;
                p.IsReady = false;
                Players[idx] = p;
                ServerNotify(L.Msg("lost_conn", p.Name));
                Turns.ServerOnPlayerDisconnected(clientId);
                // Nobody left to play against: end without a loser.
                if (Phase.Value == GamePhase.Playing && ConnectedCount < GameRules.MinPlayers && !GameSettings.AllowSoloStart)
                    ServerEndGame(RoundEndReason.NotEnoughPlayers, ulong.MaxValue, L.Msg("abandoned"));
            }
        }

        public void ServerNotify(string msg)
        {
            if (IsServer) NotifyClientRpc(new FixedString128Bytes(Truncate(msg, 120)));
        }

        // ------------------------------------------------------------------ RPCs from clients
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyServerRpc(bool ready, RpcParams rpc = default)
        {
            int idx = IndexByClient(rpc.Receive.SenderClientId);
            if (idx < 0 || Phase.Value != GamePhase.Lobby) return;
            var p = Players[idx];
            if (p.IsHost) return;
            p.IsReady = ready;
            Players[idx] = p;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetColorServerRpc(byte color, RpcParams rpc = default)
        {
            if (color >= Palette.PlayerColors.Length || Phase.Value != GamePhase.Lobby) return;
            foreach (var other in Players) if (other.ColorIndex == color && other.ClientId != rpc.Receive.SenderClientId) return;
            int idx = IndexByClient(rpc.Receive.SenderClientId);
            if (idx < 0) return;
            var p = Players[idx];
            p.ColorIndex = color;
            Players[idx] = p;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UpdateSettingsServerRpc(int startLayers, int turnSeconds, int matchRounds, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.Lobby) return;
            ApplyGameSettings(startLayers, turnSeconds, matchRounds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartGameServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (!CanStart(out var reason)) { ServerNotify(reason); return; }
            ServerStartMatch(true);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlayAgainServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.GameOver) return;
            if (ConnectedCount < GameRules.MinPlayers && !GameSettings.AllowSoloStart) { ServerNotify(L.Msg("need_two_back")); ServerReturnToLobby(); return; }
            ServerStartMatch(MatchOver); // next round of the match, or a fresh match after the last one
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToLobbyServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ServerReturnToLobby();
        }

        [ClientRpc]
        void NotifyClientRpc(FixedString128Bytes msg) => Notification?.Invoke(L.Resolve(msg.ToString()));

        [ClientRpc]
        void MatchEndedClientRpc(ulong loser) => MatchEnded?.Invoke(loser != NetworkManager.LocalClientId);

        [ClientRpc]
        void RoundResultsClientRpc(PlayerRoundResult[] results, ulong loser, RoundEndReason reason, FixedString128Bytes reasonText, int successful, FixedString512Bytes cards, int round, int rounds)
        {
            RoundResults.Set(results, loser, reason, reasonText.ToString(), successful, cards.ToString(), round, rounds);
        }

        // ------------------------------------------------------------------ Server: round flow
        /// <param name="newMatch">true = scores back to zero and round 1; false = next round of the running match.</param>
        void ServerStartMatch(bool newMatch)
        {
            // drop players that disconnected during a previous round
            for (int i = Players.Count - 1; i >= 0; i--)
                if (!Players[i].IsConnected) Players.RemoveAt(i);

            if (newMatch || RoundNumber.Value <= 0)
            {
                RoundNumber.Value = 0;
                for (int i = 0; i < Players.Count; i++)
                {
                    var p = Players[i];
                    if (p.Score == 0) continue;
                    p.Score = 0;
                    Players[i] = p;
                }
            }
            RoundNumber.Value++;
            _penalties.Clear();

            var order = new List<ulong>();
            foreach (var p in Players) if (p.IsConnected) order.Add(p.ClientId);
            Shuffle(order); // random first player and turn order

            Tower.ServerBuild(StartLayers.Value);
            var sorted = PlayersSorted();
            var dealt = Tasks.ServerDeal(sorted);
            Cards.ServerDeal(sorted, dealt);
            Turns.ServerPrepare(order);
            Atmosphere.ServerReset();

            LoserClientId.Value = ulong.MaxValue;
            EndReason.Value = default;
            EndReasonKind.Value = RoundEndReason.None;
            MatchIndex.Value++;
            CountdownEndTime.Value = ServerNow + GameRules.CountdownSeconds;
            Phase.Value = GamePhase.Countdown;
            _ = LobbyManager.Instance?.SetStatusAsync(LobbyManager.StatusPlaying, true);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            if (Phase.Value == GamePhase.Countdown && ServerNow >= CountdownEndTime.Value)
            {
                Phase.Value = GamePhase.Playing;
                Turns.ServerBeginFirstTurn();
            }
        }

        /// <summary>Called by TowerStabilityChecker when blocks have fallen (pure physics, nothing scripted). Whoever was playing loses.</summary>
        public void ServerOnTowerCollapsed(ulong culprit)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            Tasks.ServerOnCollapse(culprit);
            int done = Turns.MovesCompleted.Value;
            string msg = culprit == ulong.MaxValue ? L.Msg("fell_nobody") : L.Msg("fell", PlayerName(culprit), done);
            ServerEndGame(RoundEndReason.TowerCollapsed, culprit, msg);
        }

        public void ServerEndGame(RoundEndReason kind, ulong loser, string reason)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            LoserClientId.Value = loser;
            EndReasonKind.Value = kind;
            EndReason.Value = new FixedString128Bytes(Truncate(reason, 120));
            ulong culprit = loser;
            Turns.ServerStop();

            var results = new List<PlayerRoundResult>();
            foreach (var p in PlayersSorted())
            {
                string auth = p.AuthId.ToString();
                Tasks.ServerTryGetResult(auth, out var task, out int own, out bool caused);
                Cards.ServerTryGetCard(auth, out var card);

                // points: survival + secret task, added to the match total
                int survival = ScoreRules.SurvivalPoints(kind, p.ClientId == loser, p.IsConnected);
                int taskPts = ScoreRules.TaskPoints(task);
                _penalties.TryGetValue(auth, out int penalty);
                int idx = IndexByClient(p.ClientId);
                int total = p.Score + survival + taskPts + penalty;
                if (idx >= 0)
                {
                    var upd = Players[idx];
                    upd.Score = total;
                    Players[idx] = upd;
                }

                results.Add(new PlayerRoundResult
                {
                    SurvivalPoints = survival,
                    TaskPoints = taskPts,
                    PenaltyPoints = penalty,
                    TotalScore = total,
                    ClientId = p.ClientId,
                    Name = p.Name,
                    ColorIndex = p.ColorIndex,
                    Task = task,
                    Card = card,
                    SuccessfulTurns = own,
                    CausedCollapse = caused || p.ClientId == culprit,
                });
            }
            string cards = Cards.ServerLog.Count == 0 ? L.Msg("no_cards") : string.Join("\n", Cards.ServerLog);
            RoundResultsClientRpc(results.ToArray(), loser, kind, new FixedString128Bytes(Truncate(reason, 120)), Turns.MovesCompleted.Value, new FixedString512Bytes(Truncate(cards, 500)), RoundNumber.Value, MatchRounds.Value);
            Phase.Value = GamePhase.GameOver;
            MatchEndedClientRpc(loser);

            // bots react: the one who dropped it is not amused, the others raise their beer
            foreach (var p in Players)
            {
                if (!p.IsBot || !p.IsConnected) continue;
                if (p.ClientId == loser) HandSync.ServerBotGesture(p.ClientId, UnityEngine.Random.value < 0.6f ? Gesture.MiddleFinger : Gesture.Wave);
                else if (loser != ulong.MaxValue) HandSync.ServerBotGesture(p.ClientId, UnityEngine.Random.value < 0.6f ? Gesture.Beer : Gesture.Smoke);
            }
        }

        void ServerReturnToLobby()
        {
            Turns.ServerStop();
            Tower.ServerClear();
            Tasks.ServerClear();
            Cards.ServerClear();
            for (int i = Players.Count - 1; i >= 0; i--)
            {
                var p = Players[i];
                if (!p.IsConnected) { Players.RemoveAt(i); continue; }
                p.IsReady = p.IsHost || p.IsBot;
                Players[i] = p;
            }
            Phase.Value = GamePhase.Lobby;
            _ = LobbyManager.Instance?.SetStatusAsync(LobbyManager.StatusWaiting, false);
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
