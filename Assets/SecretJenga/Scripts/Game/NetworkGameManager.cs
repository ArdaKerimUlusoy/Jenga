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
    /// (lobby -> countdown -> playing -> results -> play again / back to lobby). Lives on the networked session
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
        public static event Action<bool> MatchEnded;

        public NetworkList<PlayerData> Players;
        public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        /// <summary>Shared objective: successful turns the group must complete.</summary>
        public readonly NetworkVariable<int> RequiredTurns = new NetworkVariable<int>(GameRules.DefaultRequiredTurns);
        public readonly NetworkVariable<int> StartLayers = new NetworkVariable<int>(GameRules.DefaultStartLayers);
        /// <summary>Turn timer in seconds, 0 = off.</summary>
        public readonly NetworkVariable<int> TurnSeconds = new NetworkVariable<int>(GameRules.DefaultTurnSeconds);
        public readonly NetworkVariable<int> MaxPlayers = new NetworkVariable<int>(GameRules.MaxPlayers);
        public readonly NetworkVariable<FixedString32Bytes> RoomCode = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<FixedString64Bytes> RoomName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<bool> IsDirectSession = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> CountdownEndTime = new NetworkVariable<double>();
        public readonly NetworkVariable<bool> TeamWon = new NetworkVariable<bool>();
        public readonly NetworkVariable<RoundEndReason> EndReasonKind = new NetworkVariable<RoundEndReason>();
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();
        public readonly NetworkVariable<int> MatchIndex = new NetworkVariable<int>();

        public TurnManager Turns { get; private set; }
        public JengaTower Tower { get; private set; }
        public SecretTaskManager Tasks { get; private set; }
        public CardManager Cards { get; private set; }
        public TowerStabilityChecker Stability { get; private set; }
        public AtmosphereSync Atmosphere { get; private set; }

        int _nextJoinOrder;

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
            RequiredTurns.OnValueChanged += OnIntChanged;
            StartLayers.OnValueChanged += OnIntChanged;
            TurnSeconds.OnValueChanged += OnIntChanged;
            MaxPlayers.OnValueChanged += OnIntChanged;
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
            RequiredTurns.OnValueChanged -= OnIntChanged;
            StartLayers.OnValueChanged -= OnIntChanged;
            TurnSeconds.OnValueChanged -= OnIntChanged;
            MaxPlayers.OnValueChanged -= OnIntChanged;
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
            if (Phase.Value != GamePhase.Lobby) { reason = "A round is already running."; return false; }
            if (connected < GameRules.MinPlayers && !solo) { reason = "Need at least 2 players at the table."; return false; }
            foreach (var p in Players)
                if (p.IsConnected && !p.IsHost && !p.IsReady) { reason = "Waiting for everyone to be ready."; return false; }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ Server: room / roster
        public void ServerApplySettings(RoomSettings s)
        {
            MaxPlayers.Value = Mathf.Clamp(s.maxPlayers, GameRules.MinPlayers, GameRules.MaxPlayers);
            ApplyGameSettings(s.requiredTurns, s.startLayers, s.turnSeconds);
            RoomName.Value = new FixedString64Bytes(Truncate(s.roomName, 60));
            IsDirectSession.Value = s.direct;
        }

        void ApplyGameSettings(int requiredTurns, int startLayers, int turnSeconds)
        {
            StartLayers.Value = Mathf.Clamp(startLayers, GameRules.MinStartLayers, GameRules.MaxStartLayers);
            RequiredTurns.Value = Mathf.Clamp(requiredTurns, GameRules.MinRequiredTurns, GameRules.MaxRequiredTurns);
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
                if (Players[idx].IsConnected) { reason = "This player is already connected to the room."; return false; }
                reason = null;
                return true; // reconnecting player
            }
            if (Phase.Value != GamePhase.Lobby) { reason = "The round has already started."; return false; }
            if (ConnectedCount >= MaxPlayers.Value) { reason = "The table is full."; return false; }
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
                ServerNotify($"{existing.Name} is back at the table.");
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
            ServerNotify($"{data.Name} takes a seat.");
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
                ServerNotify($"{p.Name} left the table.");
            }
            else
            {
                p.IsConnected = false;
                p.IsReady = false;
                Players[idx] = p;
                ServerNotify($"{p.Name} lost connection. Their turns are skipped until they return.");
                Turns.ServerOnPlayerDisconnected(clientId);
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
        public void UpdateSettingsServerRpc(int requiredTurns, int startLayers, int turnSeconds, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.Lobby) return;
            ApplyGameSettings(requiredTurns, startLayers, turnSeconds);
            _ = LobbyManager.Instance?.SetTargetAsync(RequiredTurns.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void StartGameServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (!CanStart(out var reason)) { ServerNotify(reason); return; }
            ServerStartMatch();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlayAgainServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.GameOver) return;
            if (ConnectedCount < GameRules.MinPlayers && !GameSettings.AllowSoloStart) { ServerNotify("Need at least 2 connected players. Returning to the lobby."); ServerReturnToLobby(); return; }
            ServerStartMatch();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReturnToLobbyServerRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ServerReturnToLobby();
        }

        [ClientRpc]
        void NotifyClientRpc(FixedString128Bytes msg) => Notification?.Invoke(msg.ToString());

        [ClientRpc]
        void MatchEndedClientRpc(bool won) => MatchEnded?.Invoke(won);

        [ClientRpc]
        void RoundResultsClientRpc(PlayerRoundResult[] results, bool won, RoundEndReason reason, FixedString128Bytes reasonText, int successful, int required, FixedString512Bytes cards)
        {
            RoundResults.Set(results, won, reason, reasonText.ToString(), successful, required, cards.ToString());
        }

        // ------------------------------------------------------------------ Server: round flow
        void ServerStartMatch()
        {
            // drop players that disconnected during a previous round
            for (int i = Players.Count - 1; i >= 0; i--)
                if (!Players[i].IsConnected) Players.RemoveAt(i);

            var order = new List<ulong>();
            foreach (var p in Players) if (p.IsConnected) order.Add(p.ClientId);
            Shuffle(order); // random first player and turn order

            Tower.ServerBuild(StartLayers.Value);
            var sorted = PlayersSorted();
            var dealt = Tasks.ServerDeal(sorted, RequiredTurns.Value);
            Cards.ServerDeal(sorted, dealt);
            Turns.ServerPrepare(order);
            Atmosphere.ServerReset();

            TeamWon.Value = false;
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

        /// <summary>Called by TowerStabilityChecker when blocks have fallen (pure physics, nothing scripted).</summary>
        public void ServerOnTowerCollapsed(ulong culprit)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            Tasks.ServerOnCollapse(culprit);
            int done = Turns.MovesCompleted.Value;
            ServerEndGame(false, RoundEndReason.TowerCollapsed, $"The tower fell during {PlayerName(culprit)}'s turn after {done} of {RequiredTurns.Value} successful turns.");
        }

        public void ServerEndGame(bool won, RoundEndReason kind, string reason)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            TeamWon.Value = won;
            EndReasonKind.Value = kind;
            EndReason.Value = new FixedString128Bytes(Truncate(reason, 120));
            ulong culprit = kind == RoundEndReason.TowerCollapsed ? Turns.ActiveClientId.Value : ulong.MaxValue;
            Turns.ServerStop();

            var results = new List<PlayerRoundResult>();
            foreach (var p in PlayersSorted())
            {
                string auth = p.AuthId.ToString();
                Tasks.ServerTryGetResult(auth, out var task, out int own, out bool caused);
                Cards.ServerTryGetCard(auth, out var card);
                results.Add(new PlayerRoundResult
                {
                    ClientId = p.ClientId,
                    Name = p.Name,
                    ColorIndex = p.ColorIndex,
                    Task = task,
                    Card = card,
                    SuccessfulTurns = own,
                    CausedCollapse = caused || p.ClientId == culprit,
                });
            }
            string cards = Cards.ServerLog.Count == 0 ? "No cards were played." : string.Join("\n", Cards.ServerLog);
            RoundResultsClientRpc(results.ToArray(), won, kind, new FixedString128Bytes(Truncate(reason, 120)), Turns.MovesCompleted.Value, RequiredTurns.Value, new FixedString512Bytes(Truncate(cards, 500)));
            Phase.Value = GamePhase.GameOver;
            MatchEndedClientRpc(won);
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
                p.IsReady = p.IsHost;
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
