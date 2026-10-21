using System;
using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Host-authoritative match state: player roster, room settings, game phase and match flow
    /// (lobby -> countdown -> playing -> game over -> play again / back to lobby).
    /// Lives on the networked "SJ_Session" prefab together with TurnManager, JengaTower,
    /// SecretMissionManager and TowerStabilityChecker.
    /// </summary>
    [RequireComponent(typeof(TurnManager), typeof(JengaTower), typeof(SecretMissionManager))]
    [RequireComponent(typeof(TowerStabilityChecker))]
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
        public readonly NetworkVariable<int> TargetHeight = new NetworkVariable<int>(GameRules.DefaultTargetHeight);
        public readonly NetworkVariable<int> StartLayers = new NetworkVariable<int>(GameRules.DefaultStartLayers);
        public readonly NetworkVariable<int> TurnSeconds = new NetworkVariable<int>(GameRules.DefaultTurnSeconds);
        public readonly NetworkVariable<int> MaxPlayers = new NetworkVariable<int>(GameRules.MaxPlayers);
        public readonly NetworkVariable<FixedString32Bytes> RoomCode = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<FixedString64Bytes> RoomName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<double> CountdownEndTime = new NetworkVariable<double>();
        public readonly NetworkVariable<bool> TeamWon = new NetworkVariable<bool>();
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();
        public readonly NetworkVariable<int> MatchIndex = new NetworkVariable<int>();

        public TurnManager Turns { get; private set; }
        public JengaTower Tower { get; private set; }
        public SecretMissionManager Missions { get; private set; }
        public TowerStabilityChecker Stability { get; private set; }

        int _nextJoinOrder;

        public double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        void Awake()
        {
            Players = new NetworkList<PlayerData>();
            Turns = GetComponent<TurnManager>();
            Tower = GetComponent<JengaTower>();
            Missions = GetComponent<SecretMissionManager>();
            Stability = GetComponent<TowerStabilityChecker>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Players.OnListChanged += OnPlayersChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            TargetHeight.OnValueChanged += OnIntChanged;
            StartLayers.OnValueChanged += OnIntChanged;
            TurnSeconds.OnValueChanged += OnIntChanged;
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
            TargetHeight.OnValueChanged -= OnIntChanged;
            StartLayers.OnValueChanged -= OnIntChanged;
            TurnSeconds.OnValueChanged -= OnIntChanged;
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
            if (Phase.Value != GamePhase.Lobby) { reason = "Match already running."; return false; }
            if (connected < GameRules.MinPlayers && !solo) { reason = "Need at least 2 players."; return false; }
            foreach (var p in Players)
                if (p.IsConnected && !p.IsHost && !p.IsReady) { reason = "Waiting for everyone to be ready."; return false; }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ Server: room / roster
        public void ServerApplySettings(RoomSettings s)
        {
            MaxPlayers.Value = Mathf.Clamp(s.maxPlayers, GameRules.MinPlayers, GameRules.MaxPlayers);
            ApplyGameSettings(s.targetHeight, s.startLayers, s.turnSeconds);
            RoomName.Value = new FixedString64Bytes(Truncate(s.roomName, 60));
        }

        void ApplyGameSettings(int target, int startLayers, int turnSeconds)
        {
            StartLayers.Value = Mathf.Clamp(startLayers, GameRules.MinStartLayers, GameRules.MaxStartLayers);
            TargetHeight.Value = Mathf.Clamp(target, Mathf.Max(GameRules.MinTargetHeight, StartLayers.Value + 2), GameRules.MaxTargetHeight);
            TurnSeconds.Value = Mathf.Clamp(turnSeconds, 10, 90);
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
            if (Phase.Value != GamePhase.Lobby) { reason = "The match has already started."; return false; }
            if (ConnectedCount >= MaxPlayers.Value) { reason = "This room is full."; return false; }
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
                Missions.ServerResendTo(clientId);
                ServerNotify($"{existing.Name} reconnected.");
                return;
            }

            var data = new PlayerData
            {
                ClientId = clientId,
                AuthId = new FixedString64Bytes(p.authId ?? clientId.ToString()),
                Name = new FixedString32Bytes(UniqueName(GameSettings.SanitizeName(p.playerName))),
                ColorIndex = (byte)PickColor(p.colorPref),
                IsHost = clientId == NetworkManager.ServerClientId,
                IsConnected = true,
                JoinOrder = _nextJoinOrder++,
            };
            data.IsReady = data.IsHost;
            Players.Add(data);
            ServerNotify($"{data.Name} joined the room.");
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
                ServerNotify($"{p.Name} left the room.");
            }
            else
            {
                p.IsConnected = false;
                p.IsReady = false;
                Players[idx] = p;
                ServerNotify($"{p.Name} disconnected. Their turns are skipped until they rejoin.");
                Turns.ServerOnPlayerDisconnected(clientId);
            }
        }

        public void ServerNotify(string msg)
        {
            if (IsServer) NotifyClientRpc(new FixedString128Bytes(Truncate(msg, 120)));
        }

        // ------------------------------------------------------------------ RPCs from clients
        [ServerRpc(RequireOwnership = false)]
        public void SetReadyServerRpc(bool ready, ServerRpcParams rpc = default)
        {
            int idx = IndexByClient(rpc.Receive.SenderClientId);
            if (idx < 0 || Phase.Value != GamePhase.Lobby) return;
            var p = Players[idx];
            if (p.IsHost) return;
            p.IsReady = ready;
            Players[idx] = p;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetColorServerRpc(byte color, ServerRpcParams rpc = default)
        {
            if (color >= Palette.PlayerColors.Length || Phase.Value != GamePhase.Lobby) return;
            foreach (var other in Players) if (other.ColorIndex == color && other.ClientId != rpc.Receive.SenderClientId) return;
            int idx = IndexByClient(rpc.Receive.SenderClientId);
            if (idx < 0) return;
            var p = Players[idx];
            p.ColorIndex = color;
            Players[idx] = p;
        }

        [ServerRpc(RequireOwnership = false)]
        public void UpdateSettingsServerRpc(int target, int startLayers, int turnSeconds, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.Lobby) return;
            ApplyGameSettings(target, startLayers, turnSeconds);
            _ = LobbyManager.Instance?.SetTargetAsync(TargetHeight.Value);
        }

        [ServerRpc(RequireOwnership = false)]
        public void StartGameServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (!CanStart(out var reason)) { ServerNotify(reason); return; }
            ServerStartMatch();
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayAgainServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId || Phase.Value != GamePhase.GameOver) return;
            if (ConnectedCount < GameRules.MinPlayers && !GameSettings.AllowSoloStart) { ServerNotify("Need at least 2 connected players. Returning to lobby."); ServerReturnToLobby(); return; }
            ServerStartMatch();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReturnToLobbyServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            ServerReturnToLobby();
        }

        [ClientRpc]
        void NotifyClientRpc(FixedString128Bytes msg) => Notification?.Invoke(msg.ToString());

        [ClientRpc]
        void MatchEndedClientRpc(bool won) => MatchEnded?.Invoke(won);

        // ------------------------------------------------------------------ Server: match flow
        void ServerStartMatch()
        {
            // drop players that disconnected during a previous match
            for (int i = Players.Count - 1; i >= 0; i--)
                if (!Players[i].IsConnected) Players.RemoveAt(i);

            var order = new List<ulong>();
            foreach (var p in Players) if (p.IsConnected) order.Add(p.ClientId);
            Shuffle(order);

            Tower.ServerBuild(StartLayers.Value);
            Missions.ServerDeal(PlayersSorted(), Tower);
            Turns.ServerPrepare(order);

            TeamWon.Value = false;
            EndReason.Value = default;
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

        public void ServerEndGame(bool won, string reason)
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            TeamWon.Value = won;
            EndReason.Value = new FixedString128Bytes(Truncate(reason, 120));
            Turns.ServerStop();
            Missions.ServerReveal(won, PlayersSorted());
            Phase.Value = GamePhase.GameOver;
            MatchEndedClientRpc(won);
        }

        void ServerReturnToLobby()
        {
            Turns.ServerStop();
            Tower.ServerClear();
            Missions.ServerClear();
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
