using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>
    /// Wraps Unity Lobby: room creation, room browser queries, join by code / id, heartbeat, status updates and leaving.
    /// Real-time room state (ready flags, colors, settings) lives in NGO (NetworkGameManager); the Lobby service is
    /// used for discovery, room codes and handing out the Relay join code.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        public const string KeyRelayCode = "relay";
        public const string KeyStatus = "status";   // indexed S1: "waiting" / "playing"
        public const string KeyVersion = "ver";     // indexed S2: build version
        public const string KeyTarget = "target";   // indexed N1: target height
        public const string StatusWaiting = "waiting";
        public const string StatusPlaying = "playing";

        public Lobby CurrentLobby { get; private set; }
        public bool IsLobbyHost => CurrentLobby != null && CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;

        Coroutine _heartbeat;
        float _lastQueryTime = -10f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static Player MakePlayer(string playerName) =>
            new Player(id: AuthenticationService.Instance.PlayerId, data: new Dictionary<string, PlayerDataObject>
            {
                { "name", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
            });

        public async Task<Lobby> CreateLobbyAsync(RoomSettings s, string relayCode, string playerName)
        {
            var options = new CreateLobbyOptions
            {
                IsPrivate = s.isPrivate,
                Player = MakePlayer(playerName),
                Data = new Dictionary<string, DataObject>
                {
                    { KeyRelayCode, new DataObject(DataObject.VisibilityOptions.Member, relayCode) },
                    { KeyStatus, new DataObject(DataObject.VisibilityOptions.Public, StatusWaiting, DataObject.IndexOptions.S1) },
                    { KeyVersion, new DataObject(DataObject.VisibilityOptions.Public, GameRules.BuildVersion, DataObject.IndexOptions.S2) },
                    { KeyTarget, new DataObject(DataObject.VisibilityOptions.Public, s.targetHeight.ToString(), DataObject.IndexOptions.N1) },
                }
            };
            string name = string.IsNullOrWhiteSpace(s.roomName) ? "Secret Jenga Room" : s.roomName.Trim();
            CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(name, Mathf.Clamp(s.maxPlayers, GameRules.MinPlayers, GameRules.MaxPlayers), options);
            GameSettings.LastLobbyId = CurrentLobby.Id;
            StartHeartbeat();
            Debug.Log($"[SecretJenga] Lobby created '{CurrentLobby.Name}' code={CurrentLobby.LobbyCode} id={CurrentLobby.Id}");
            return CurrentLobby;
        }

        /// <summary>Open rooms: not full, waiting for players, same build version. Rate limited to ~1 call/sec.</summary>
        public async Task<List<Lobby>> QueryOpenLobbiesAsync()
        {
            float wait = 1.1f - (Time.realtimeSinceStartup - _lastQueryTime);
            if (wait > 0) await Task.Delay((int)(wait * 1000));
            _lastQueryTime = Time.realtimeSinceStartup;

            var options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new QueryFilter(QueryFilter.FieldOptions.S1, StatusWaiting, QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.S2, GameRules.BuildVersion, QueryFilter.OpOptions.EQ),
                },
                Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) }
            };
            var response = await LobbyService.Instance.QueryLobbiesAsync(options);
            return response.Results ?? new List<Lobby>();
        }

        public async Task<Lobby> JoinByCodeAsync(string code, string playerName)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code, new JoinLobbyByCodeOptions { Player = MakePlayer(playerName) });
            GameSettings.LastLobbyId = CurrentLobby.Id;
            return CurrentLobby;
        }

        public async Task<Lobby> JoinByIdAsync(string lobbyId, string playerName)
        {
            CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions { Player = MakePlayer(playerName) });
            GameSettings.LastLobbyId = CurrentLobby.Id;
            return CurrentLobby;
        }

        /// <summary>Rejoin a lobby we are still a member of (after a crash / disconnect).</summary>
        public async Task<Lobby> ReconnectAsync(string lobbyId)
        {
            CurrentLobby = await LobbyService.Instance.ReconnectToLobbyAsync(lobbyId);
            return CurrentLobby;
        }

        public string GetRelayCode()
        {
            if (CurrentLobby?.Data != null && CurrentLobby.Data.TryGetValue(KeyRelayCode, out var d)) return d.Value;
            return null;
        }

        public async Task SetStatusAsync(string status, bool locked)
        {
            if (!IsLobbyHost) return;
            try
            {
                CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions
                {
                    IsLocked = locked,
                    Data = new Dictionary<string, DataObject>
                    {
                        { KeyStatus, new DataObject(DataObject.VisibilityOptions.Public, status, DataObject.IndexOptions.S1) }
                    }
                });
            }
            catch (Exception e) { Debug.LogWarning("[SecretJenga] Lobby status update failed: " + e.Message); }
        }

        public async Task SetTargetAsync(int target)
        {
            if (!IsLobbyHost) return;
            try
            {
                CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { KeyTarget, new DataObject(DataObject.VisibilityOptions.Public, target.ToString(), DataObject.IndexOptions.N1) }
                    }
                });
            }
            catch (Exception e) { Debug.LogWarning("[SecretJenga] Lobby target update failed: " + e.Message); }
        }

        /// <summary>Host only: frees the lobby slot of a player who left.</summary>
        public async Task RemovePlayerAsync(string playerId)
        {
            if (!IsLobbyHost || string.IsNullOrEmpty(playerId)) return;
            try { await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, playerId); }
            catch (Exception e) { Debug.LogWarning("[SecretJenga] RemovePlayer failed: " + e.Message); }
        }

        /// <summary>Host deletes the lobby, clients remove themselves.</summary>
        public async Task LeaveAsync()
        {
            StopHeartbeat();
            var lobby = CurrentLobby;
            CurrentLobby = null;
            if (lobby == null) return;
            try
            {
                if (lobby.HostId == AuthenticationService.Instance.PlayerId) await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                else await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e) { Debug.LogWarning("[SecretJenga] Leaving lobby failed (it will expire on its own): " + e.Message); }
        }

        public void ForgetLobby()
        {
            StopHeartbeat();
            CurrentLobby = null;
        }

        void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeat = StartCoroutine(HeartbeatLoop());
        }

        void StopHeartbeat()
        {
            if (_heartbeat != null) StopCoroutine(_heartbeat);
            _heartbeat = null;
        }

        IEnumerator HeartbeatLoop()
        {
            var wait = new WaitForSecondsRealtime(15f);
            while (CurrentLobby != null && IsLobbyHost)
            {
                var task = LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
                yield return new WaitUntil(() => task.IsCompleted);
                if (task.IsFaulted) Debug.LogWarning("[SecretJenga] Lobby heartbeat failed: " + task.Exception?.GetBaseException().Message);
                yield return wait;
            }
        }

        /// <summary>Human readable message for lobby errors (full room, wrong code ...).</summary>
        public static string Describe(Exception e)
        {
            if (e is LobbyServiceException le)
            {
                var r = le.Reason.ToString();
                if (r.Contains("LobbyFull")) return "This room is full.";
                if (r.Contains("LobbyNotFound")) return "Room not found. It may have closed.";
                if (r.Contains("InvalidJoinCode") || r.Contains("ValidationError")) return "Invalid room code.";
                if (r.Contains("LobbyLocked")) return "This game has already started.";
                if (r.Contains("RateLimited")) return "Too many requests. Wait a moment and try again.";
                if (r.Contains("LobbyConflict") || r.Contains("AlreadyMember")) return "You are already in this room (try 'Rejoin').";
                return "Lobby error: " + r;
            }
            return MultiplayerServiceManager.Describe(e);
        }
    }
}
