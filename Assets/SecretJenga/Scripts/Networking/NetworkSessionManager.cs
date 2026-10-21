using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SecretJenga.Game;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>
    /// Orchestrates the online flow. Host-client architecture: the host runs the authoritative simulation.
    /// Two interchangeable connection modes (the rest of the game does not care which one is used):
    ///  * Direct (IP)  = StartHost listening on a UDP port / StartClient to "address:port". Works on a LAN, on one PC,
    ///                   or over the internet with port forwarding / a VPN. Needs no Unity services.
    ///  * Online (Relay + Lobby) = sign in -> Relay allocation -> StartHost -> create Lobby (stores relay code);
    ///                   join by room code. Requires the project to be linked to Unity Cloud with Relay + Lobby enabled.
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
        bool _direct;
        static bool _directRejoinAvailable;

        /// <summary>True while connected through Direct (IP) mode.</summary>
        public bool IsDirect => _direct;

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
            if (NM == null) throw new Exception("NetworkManager missing from the scene. Run Tools > The Last Block > Generate Project.");
            if (NM.IsListening) NM.Shutdown();
            NM.NetworkConfig.ConnectionApproval = true;
            NM.NetworkConfig.EnableSceneManagement = false;
            var svc = MultiplayerServiceManager.Instance;
            LocalPayload = new ConnectionPayload
            {
                authId = svc != null && !string.IsNullOrEmpty(svc.PlayerId) ? svc.PlayerId : GameSettings.OfflinePlayerId,
                playerName = GameSettings.PlayerName,
                version = GameRules.BuildVersion,
                colorPref = GameSettings.PreferredColor
            };
            NM.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(LocalPayload));
            GameBootstrap.EnsureNetworkPrefabs();
        }

        static UnityTransport Transport => NM != null ? NM.GetComponent<UnityTransport>() : null;

        // ------------------------------------------------------------------ Direct (IP)
        /// <summary>Host a table on this machine (UDP port). Other players join with this PC's address.</summary>
        public Task<bool> CreateDirectRoomAsync(RoomSettings settings)
        {
            if (State != SessionState.Offline) return Task.FromResult(false);
            State = SessionState.Busy;
            try
            {
                SetStatus("Opening the table...");
                PrepareNetworkManager();
                var t = Transport;
                if (t == null) throw new Exception("UnityTransport not found on the NetworkManager.");
                t.SetConnectionData(true, "127.0.0.1", settings.port, "0.0.0.0");
                if (!NM.StartHost()) throw new Exception($"Could not start hosting on port {settings.port} (is it already in use?).");
                _direct = true;

                var go = SpawnSession(settings);
                var ngm = go.GetComponent<NetworkGameManager>();
                ngm.ServerSetRoomInfo(PrimaryLocalAddress() + ":" + settings.port, settings.roomName);

                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[TheLastBlock] Direct host failed: " + e);
                _direct = false;
                if (NM != null && NM.IsListening) NM.Shutdown();
                State = SessionState.Offline;
                SessionEnded?.Invoke("Could not host.\n" + e.Message);
                return Task.FromResult(false);
            }
        }

        /// <summary>Join a table hosted in Direct (IP) mode.</summary>
        public async Task<bool> JoinDirectAsync(string address, ushort port)
        {
            if (State != SessionState.Offline) return false;
            address = (address ?? "").Trim();
            if (address.Length == 0) { SessionEnded?.Invoke("Enter the host's IP address."); return false; }
            State = SessionState.Busy;
            try
            {
                SetStatus($"Connecting to {address}:{port}...");
                PrepareNetworkManager();
                var t = Transport;
                if (t == null) throw new Exception("UnityTransport not found on the NetworkManager.");
                t.SetConnectionData(true, address, port);
                _connectWaiter = new TaskCompletionSource<string>();
                if (!NM.StartClient()) throw new Exception("NetworkManager.StartClient failed.");
                _direct = true;

                var finished = await Task.WhenAny(_connectWaiter.Task, Task.Delay(12000));
                if (finished != _connectWaiter.Task) throw new Exception("No answer from the host. Check the address, the port and the host's firewall.");
                string err = _connectWaiter.Task.Result;
                _connectWaiter = null;
                if (err != null) throw new Exception(err);

                GameSettings.LastDirectAddress = address;
                GameSettings.DirectPort = port;
                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheLastBlock] Direct join failed: " + e.Message);
                _connectWaiter = null;
                _direct = false;
                if (NM != null && NM.IsListening) NM.Shutdown();
                State = SessionState.Offline;
                SessionEnded?.Invoke("Could not join.\n" + e.Message);
                return false;
            }
        }

        /// <summary>Best guess at this PC's LAN IPv4 address (shown to the host so they can tell the others).</summary>
        public static string PrimaryLocalAddress()
        {
            var all = LocalAddresses();
            return all.Count > 0 ? all[0] : "127.0.0.1";
        }

        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var ip = ua.Address.ToString();
                            if (!ip.StartsWith("169.254.") && !list.Contains(ip)) list.Add(ip);
                        }
                }
            }
            catch (Exception e) { Debug.LogWarning("[TheLastBlock] Could not read network interfaces: " + e.Message); }
            // private LAN ranges first
            list.Sort((a, b) => (IsPrivate(b) ? 1 : 0).CompareTo(IsPrivate(a) ? 1 : 0));
            return list;
        }

        static bool IsPrivate(string ip) => ip.StartsWith("192.168.") || ip.StartsWith("10.") || (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var b) && b >= 16 && b <= 31);

        GameObject SpawnSession(RoomSettings settings)
        {
            var prefab = SecretJengaAssets.Instance.sessionPrefab;
            if (prefab == null) throw new Exception("Session prefab missing. Run Tools > The Last Block > Generate Project.");
            var go = Instantiate(prefab);
            var ngm = go.GetComponent<NetworkGameManager>();
            // spawn first: NetworkVariables may only be written once they know their NetworkBehaviour
            go.GetComponent<NetworkObject>().Spawn();
            ngm.ServerApplySettings(settings);
            return go;
        }

        // ------------------------------------------------------------------ Create (Relay + Lobby)
        public async Task<bool> CreateRoomAsync(RoomSettings settings)
        {
            if (settings.direct) return await CreateDirectRoomAsync(settings);
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
                _direct = false;

                var go = SpawnSession(settings);
                var ngm = go.GetComponent<NetworkGameManager>();

                SetStatus("Creating room...");
                Lobby lobby = await LobbyManager.Instance.CreateLobbyAsync(settings, relayCode, GameSettings.PlayerName);
                if (ngm != null) ngm.ServerSetRoomInfo(lobby.LobbyCode, lobby.Name);

                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[TheLastBlock] Create room failed: " + e);
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

        public bool CanRejoin => _directRejoinAvailable || !string.IsNullOrEmpty(GameSettings.LastLobbyId);

        public Task<bool> RejoinAsync()
        {
            if (_directRejoinAvailable) return JoinDirectAsync(GameSettings.LastDirectAddress, GameSettings.DirectPort);
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

                _direct = false;
                State = SessionState.InRoom;
                SessionStarted?.Invoke();
                Debug.Log($"[TheLastBlock] Joined room '{lobby.Name}' as client {NM.LocalClientId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheLastBlock] Join failed: " + e.Message);
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
                _direct = false;
                State = SessionState.Offline;
                SessionEnded?.Invoke(null);
            }
        }

        async Task CleanupAsync(bool forgetLobby)
        {
            _pending.Clear();
            if (forgetLobby) _directRejoinAvailable = false;
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
            bool wasDirect = _direct;
            try { await CleanupAsync(!keepForRejoin); }
            finally
            {
                _leaving = false;
                _direct = false;
                if (keepForRejoin && wasDirect) _directRejoinAvailable = true;
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
                Debug.Log($"[TheLastBlock] Rejected client {request.ClientNetworkId}: {reason}");
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
            Debug.LogWarning("[TheLastBlock] Transport failure.");
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
