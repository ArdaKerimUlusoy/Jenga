using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SecretJenga.Game;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>
    /// Orchestrates the whole online flow:
    /// Create room  = sign in -> Relay allocation -> StartHost -> spawn session object -> create Lobby (stores relay code).
    /// Join room    = sign in -> join Lobby (code / id / rejoin) -> read relay code -> join Relay -> StartClient (with approval payload).
    /// Also owns NGO connection approval and disconnect handling (host lost, rejected, transport failure).
    /// </summary>
    public class NetworkSessionManager : MonoBehaviour
    {
        public enum SessionState { Offline, Busy, InRoom }

        public static NetworkSessionManager Instance { get; private set; }

        public SessionState State { get; private set; } = SessionState.Offline;

        /// <summary>Progress text for the loading overlay.</summary>
        public event Action<string> StatusChanged;
        /// <summary>Raised when we are connected and the room is usable.</summary>
        public event Action SessionStarted;
        /// <summary>Raised when the session is over. Argument = error/reason to show (null when the user left on purpose).</summary>
        public event Action<string> SessionEnded;

        public ConnectionPayload LocalPayload { get; private set; }

        readonly Dictionary<ulong, ConnectionPayload> _pending = new Dictionary<ulong, ConnectionPayload>();
        TaskCompletionSource<string> _connectWaiter;
        bool _hooked;
        bool _leaving;

        static NetworkManager NM => NetworkManager.Singleton;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() => Hook();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_hooked && NM != null)
            {
                NM.ConnectionApprovalCallback = null;
                NM.OnClientConnectedCallback -= OnClientConnected;
                NM.OnClientDisconnectCallback -= OnClientDisconnect;
                NM.OnTransportFailure -= OnTransportFailure;
            }
        }

        void Hook()
        {
            if (_hooked || NM == null) return;
            NM.ConnectionApprovalCallback = ApprovalCheck;
            NM.OnClientConnectedCallback += OnClientConnected;
            NM.OnClientDisconnectCallback += OnClientDisconnect;
            NM.OnTransportFailure += OnTransportFailure;
            _hooked = true;
        }

        void SetStatus(string s) => StatusChanged?.Invoke(s);

        void PrepareNetworkManager()
        {
            Hook();
            if (NM == null) throw new Exception("NetworkManager missing from the scene. Run Tools > Secret Jenga > Generate Project.");
            if (NM.IsListening) NM.Shutdown();
            NM.NetworkConfig.ConnectionApproval = true;
            NM.NetworkConfig.EnableSceneManagement = false;
            LocalPayload = new ConnectionPayload
            {
                authId = MultiplayerServiceManager.Instance.PlayerId,
                playerName = GameSettings.PlayerName,
                version = GameRules.BuildVersion,
                colorPref = GameSettings.PreferredColor
            };
            NM.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(LocalPayload));
            GameBootstrap.EnsureNetworkPrefabs();
        }

        // ------------------------------------------------------------------ Create
        public async Task<bool> CreateRoomAsync(RoomSettings settings)
        {
            if (State != SessionState.Offline) return false;
            State = SessionState.Busy;
            try
            {
                SetStatus("Signing in...");
                await MultiplayerServiceManager.Instance.EnsureSignedInAsync();

                SetStatus("Allocating relay server...");
                string relayCode = await RelayConnectionManager.Instance.AllocateHostAsync(settings.maxPlayers);

                PrepareNetworkManager();
                SetStatus("Starting host...");
                if (!NM.StartHost()) throw new Exception("NetworkManager.StartHost failed.");

                var prefab = SecretJengaAssets.Instance.sessionPrefab;
                if (prefab == null) throw new Exception("Session prefab missing. Run Tools > Secret Jenga > Generate Project.");
                var go = Instantiate(prefab);
                var ngm = go.GetComponent<NetworkGameManager>();
                ngm.ServerApplySettings(settings);
                go.GetComponent<NetworkObject>().Spawn();

                SetStatus("Creating room...");
                Lobby lobby = await LobbyManager.Instance.CreateLobbyAsync(settings, relayCode, GameSettings.PlayerName);
                if (ngm != null) ngm.ServerSetRoomInfo(lobby.LobbyCode, lobby.Name);

                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[SecretJenga] Create room failed: " + e);
                await CleanupAsync(true);
                State = SessionState.Offline;
                SessionEnded?.Invoke("Could not create room.\n" + LobbyManager.Describe(e));
                return false;
            }
        }

        // ------------------------------------------------------------------ Join
        public Task<bool> JoinByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Trim().Length < 4)
            {
                SessionEnded?.Invoke("Please enter a valid room code.");
                return Task.FromResult(false);
            }
            return JoinInternalAsync(() => LobbyManager.Instance.JoinByCodeAsync(code, GameSettings.PlayerName));
        }

        public Task<bool> JoinByIdAsync(string lobbyId) =>
            JoinInternalAsync(() => LobbyManager.Instance.JoinByIdAsync(lobbyId, GameSettings.PlayerName));

        public bool CanRejoin => !string.IsNullOrEmpty(GameSettings.LastLobbyId);

        public Task<bool> RejoinAsync()
        {
            var id = GameSettings.LastLobbyId;
            return JoinInternalAsync(async () =>
            {
                try { return await LobbyManager.Instance.ReconnectAsync(id); }
                catch (Exception) { return await LobbyManager.Instance.JoinByIdAsync(id, GameSettings.PlayerName); }
            });
        }

        async Task<bool> JoinInternalAsync(Func<Task<Lobby>> joinLobby)
        {
            if (State != SessionState.Offline) return false;
            State = SessionState.Busy;
            try
            {
                SetStatus("Signing in...");
                await MultiplayerServiceManager.Instance.EnsureSignedInAsync();

                SetStatus("Joining room...");
                Lobby lobby = await joinLobby();
                string relayCode = LobbyManager.Instance.GetRelayCode();
                if (string.IsNullOrEmpty(relayCode)) throw new Exception("This room has no relay code (host still starting?). Try again.");

                SetStatus("Connecting to host...");
                await RelayConnectionManager.Instance.JoinAsync(relayCode);

                PrepareNetworkManager();
                _connectWaiter = new TaskCompletionSource<string>();
                if (!NM.StartClient()) throw new Exception("NetworkManager.StartClient failed.");

                var finished = await Task.WhenAny(_connectWaiter.Task, Task.Delay(15000));
                if (finished != _connectWaiter.Task) throw new Exception("Connection to the host timed out.");
                string err = _connectWaiter.Task.Result;
                _connectWaiter = null;
                if (err != null) throw new Exception(err);

                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                Debug.Log($"[SecretJenga] Joined room '{lobby.Name}' as client {NM.LocalClientId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SecretJenga] Join failed: " + e.Message);
                _connectWaiter = null;
                await CleanupAsync(true);
                State = SessionState.Offline;
                SessionEnded?.Invoke("Could not join.\n" + LobbyManager.Describe(e));
                return false;
            }
        }

        // ------------------------------------------------------------------ Leave
        /// <summary>Leave the current room voluntarily (host closes the room for everyone).</summary>
        public async Task LeaveAsync()
        {
            if (_leaving || State == SessionState.Offline) return;
            _leaving = true;
            try { await CleanupAsync(true); }
            finally
            {
                _leaving = false;
                State = SessionState.Offline;
                SessionEnded?.Invoke(null);
            }
        }

        async Task CleanupAsync(bool forgetLobby)
        {
            _pending.Clear();
            if (NM != null && NM.IsListening) NM.Shutdown();
            if (LobbyManager.Instance != null)
            {
                if (forgetLobby) await LobbyManager.Instance.LeaveAsync();
                else LobbyManager.Instance.ForgetLobby();
            }
            if (forgetLobby) GameSettings.LastLobbyId = "";
        }

        async void HandleSessionLost(string reason)
        {
            if (_leaving || State == SessionState.Offline) return;
            _leaving = true;
            // Keep LastLobbyId when a match was running, so the player can use "Rejoin".
            bool keepForRejoin = NetworkGameManager.LastKnownPhase == GamePhase.Playing || NetworkGameManager.LastKnownPhase == GamePhase.Countdown;
            try { await CleanupAsync(!keepForRejoin); }
            finally
            {
                _leaving = false;
                State = SessionState.Offline;
                SessionEnded?.Invoke(reason + (keepForRejoin ? "\nYou can try 'Rejoin Last Game' from the main menu." : ""));
            }
        }

        // ------------------------------------------------------------------ NGO callbacks
        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }

            ConnectionPayload payload = null;
            try { payload = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(request.Payload)); }
            catch (Exception) { /* invalid payload */ }

            string reason = null;
            if (payload == null || string.IsNullOrEmpty(payload.authId)) reason = "Invalid connection data.";
            else if (payload.version != GameRules.BuildVersion) reason = "Version mismatch between host and client builds.";
            else if (NetworkGameManager.Instance == null) reason = "Room is not ready yet. Try again in a moment.";
            else if (!NetworkGameManager.Instance.ServerCanAccept(payload, out reason)) { }

            if (reason != null)
            {
                response.Approved = false;
                response.Reason = reason;
                Debug.Log($"[SecretJenga] Rejected client {request.ClientNetworkId}: {reason}");
                return;
            }

            _pending[request.ClientNetworkId] = payload;
            response.Approved = true;
        }

        void OnClientConnected(ulong clientId)
        {
            if (NM.IsServer)
            {
                if (clientId == NetworkManager.ServerClientId) return; // host registers itself on session spawn
                if (_pending.TryGetValue(clientId, out var payload))
                {
                    _pending.Remove(clientId);
                    NetworkGameManager.Instance?.ServerRegisterPlayer(clientId, payload);
                }
                return;
            }

            if (clientId == NM.LocalClientId) _connectWaiter?.TrySetResult(null);
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (NM == null) return;
            if (NM.IsServer)
            {
                if (clientId == NetworkManager.ServerClientId) return;
                _pending.Remove(clientId);
                NetworkGameManager.Instance?.ServerOnClientDisconnected(clientId);
                return;
            }

            // We are a client and we got disconnected (rejected, host left, network lost).
            string reason = NM.DisconnectReason;
            if (_connectWaiter != null && !_connectWaiter.Task.IsCompleted)
            {
                _connectWaiter.TrySetResult(string.IsNullOrEmpty(reason) ? "The host refused or dropped the connection." : reason);
                return;
            }
            if (State == SessionState.InRoom)
                HandleSessionLost(string.IsNullOrEmpty(reason) ? "Connection to the host was lost (the host may have left)." : reason);
        }

        void OnTransportFailure()
        {
            Debug.LogWarning("[SecretJenga] Transport failure.");
            _connectWaiter?.TrySetResult("Network transport failure.");
            if (State == SessionState.InRoom) HandleSessionLost("Network connection failed.");
        }

        void OnApplicationQuit()
        {
            if (State == SessionState.InRoom && LobbyManager.Instance != null && LobbyManager.Instance.IsLobbyHost)
                _ = LobbyManager.Instance.LeaveAsync(); // best effort; the lobby also expires without heartbeats
        }
    }
}
