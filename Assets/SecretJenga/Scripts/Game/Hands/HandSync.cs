using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>One player's hand as seen by everyone (where its contact point is and what shape it makes).</summary>
    public struct HandNetState : INetworkSerializable
    {
        public ulong ClientId;
        public Vector3 Contact;
        public byte Pose;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Contact);
            serializer.SerializeValue(ref Pose);
        }
    }

    /// <summary>
    /// Shares every player's right hand with the table, all the time - not only during their turn - so you can watch
    /// the others hover, hesitate, drum their fingers or knock on the table. Each client sends its own hand ~15 times
    /// per second (unreliable, cosmetic); the host clamps it to the table area and rebroadcasts the whole set.
    /// While a player actually handles a block, the hand is driven by the replicated block itself instead, so the
    /// grip matches the authoritative physics on every screen.
    /// </summary>
    public class HandSync : NetworkBehaviour
    {
        public const float SendInterval = 1f / 15f;

        /// <summary>Latest known hand of every remote player (client side).</summary>
        public static readonly Dictionary<ulong, HandNetState> Hands = new Dictionary<ulong, HandNetState>();
        /// <summary>(clientId, position) - someone knocked on the table.</summary>
        public static event Action<ulong, Vector3> Knocked;

        readonly Dictionary<ulong, HandNetState> _server = new Dictionary<ulong, HandNetState>();
        readonly Dictionary<ulong, float> _lastKnock = new Dictionary<ulong, float>();
        float _broadcastTimer, _sendTimer;
        static HandSync _instance;

        public override void OnNetworkSpawn() { _instance = this; Hands.Clear(); }
        public override void OnNetworkDespawn() { if (_instance == this) _instance = null; Hands.Clear(); }

        /// <summary>Called by the local hand every frame; throttled here.</summary>
        public static void SubmitLocal(Vector3 contact, HandPoseId pose)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned) return;
            s._sendTimer -= Time.deltaTime;
            if (s._sendTimer > 0f) return;
            s._sendTimer = SendInterval;
            s.SubmitServerRpc(contact, (byte)pose);
        }

        public static void KnockLocal(Vector3 at)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned) return;
            s.KnockServerRpc(at);
        }

        static Vector3 ClampToTable(Vector3 p)
        {
            var flat = new Vector3(p.x, 0f, p.z);
            flat = Vector3.ClampMagnitude(flat, SeatLayout.TableRadius + 4f);
            return new Vector3(flat.x, Mathf.Clamp(p.y, JengaDims.TableTopY - 1f, JengaDims.TableTopY + 14f), flat.z);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        void SubmitServerRpc(Vector3 contact, byte pose, RpcParams rpc = default)
        {
            if (float.IsNaN(contact.x) || float.IsNaN(contact.y) || float.IsNaN(contact.z)) return;
            ulong id = rpc.Receive.SenderClientId;
            _server[id] = new HandNetState { ClientId = id, Contact = ClampToTable(contact), Pose = (byte)Mathf.Min(pose, (int)HandPoseId.Knock) };
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void KnockServerRpc(Vector3 at, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (_lastKnock.TryGetValue(id, out var t) && Time.time - t < 0.6f) return; // no spamming
            _lastKnock[id] = Time.time;
            KnockClientRpc(id, ClampToTable(at));
        }

        [ClientRpc]
        void KnockClientRpc(ulong clientId, Vector3 at) => Knocked?.Invoke(clientId, at);

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            _broadcastTimer -= Time.deltaTime;
            if (_broadcastTimer > 0f || _server.Count == 0) return;
            _broadcastTimer = SendInterval;
            // forget players that left
            var game = NetworkGameManager.Instance;
            if (game != null)
            {
                var gone = new List<ulong>();
                foreach (var id in _server.Keys) if (!game.TryGetPlayer(id, out var p) || !p.IsConnected) gone.Add(id);
                foreach (var id in gone) _server.Remove(id);
            }
            var arr = new HandNetState[_server.Count];
            _server.Values.CopyTo(arr, 0);
            BroadcastClientRpc(arr);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        void BroadcastClientRpc(HandNetState[] states)
        {
            Hands.Clear();
            foreach (var s in states) Hands[s.ClientId] = s;
        }
    }
}
