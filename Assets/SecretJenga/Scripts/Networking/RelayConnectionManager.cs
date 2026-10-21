using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>Creates / joins Unity Relay allocations and feeds them into Unity Transport.</summary>
    public class RelayConnectionManager : MonoBehaviour
    {
        public static RelayConnectionManager Instance { get; private set; }

        /// <summary>"dtls" for desktop/mobile, "wss" for WebGL.</summary>
        public static string ConnectionType
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return "wss";
#else
                return "dtls";
#endif
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static UnityTransport Transport
        {
            get
            {
                var nm = NetworkManager.Singleton;
                var t = nm != null ? nm.GetComponent<UnityTransport>() : null;
                if (t == null) throw new System.Exception("UnityTransport not found on the NetworkManager.");
#if UNITY_WEBGL && !UNITY_EDITOR
                t.UseWebSockets = true;
#endif
                return t;
            }
        }

        /// <summary>Allocates a relay server for (maxPlayers - 1) remote clients and returns the relay join code.</summary>
        public async Task<string> AllocateHostAsync(int maxPlayers)
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));
            Debug.Log($"[TheLastBlock] Relay allocation created. Region={allocation.Region} JoinCode={joinCode}");
            return joinCode;
        }

        public async Task JoinAsync(string relayJoinCode)
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));
            Debug.Log($"[TheLastBlock] Joined relay allocation {allocation.AllocationId}");
        }
    }
}
