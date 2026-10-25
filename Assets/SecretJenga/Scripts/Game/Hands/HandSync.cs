using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Little left-hand gestures anyone can make at the table (number keys 1-6).</summary>
    public enum Gesture : byte { None = 0, Ok = 1, Smoke = 2, MiddleFinger = 3, Beer = 4, Wave = 5, Horns = 6 }

    /// <summary>One player's hand as seen by everyone (where its contact point is and what shape it makes).</summary>
    public struct HandNetState : INetworkSerializable
    {
        public ulong ClientId;
        public Vector3 Contact;
        public byte Pose;
        /// <summary>Where this player's camera looks (unit world direction; zero = unknown, e.g. bots).</summary>
        public Vector3 Look;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Contact);
            serializer.SerializeValue(ref Pose);
            serializer.SerializeValue(ref Look);
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
        /// <summary>(clientId, gesture) - someone made a gesture with their left hand.</summary>
        public static event Action<ulong, Gesture> GestureMade;

        readonly Dictionary<ulong, float> _lastGesture = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> _botNextGesture = new Dictionary<ulong, float>();
        static float _localGestureAt = -10f;

        readonly Dictionary<ulong, HandNetState> _server = new Dictionary<ulong, HandNetState>();
        readonly Dictionary<ulong, float> _lastKnock = new Dictionary<ulong, float>();
        float _broadcastTimer, _sendTimer;
        static HandSync _instance;

        public override void OnNetworkSpawn() { _instance = this; Hands.Clear(); _hasPending = false; }
        public override void OnNetworkDespawn() { if (_instance == this) _instance = null; Hands.Clear(); }

        static Vector3 _pendingContact;
        static HandPoseId _pendingPose;
        static bool _hasPending;

        /// <summary>Called by the local hand every frame; sent ~15 times a second from Update (with the look direction).</summary>
        public static void SubmitLocal(Vector3 contact, HandPoseId pose)
        {
            _pendingContact = contact;
            _pendingPose = pose;
            _hasPending = true;
        }

        /// <summary>Every client: send our hand and where our camera looks (the head of our character follows it).</summary>
        void ClientSend()
        {
            _sendTimer -= Time.deltaTime;
            if (_sendTimer > 0f || !_hasPending) return;
            _sendTimer = SendInterval;
            var cam = Camera.main;
            var look = cam != null ? cam.transform.forward : Vector3.zero;
            SubmitServerRpc(_pendingContact, (byte)_pendingPose, look);
        }

        /// <summary>Host only: publish a bot's hand (bots have no client to send it).</summary>
        public static void ServerSetHand(ulong clientId, Vector3 contact, HandPoseId pose)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned || !s.IsServer) return;
            s._server[clientId] = new HandNetState { ClientId = clientId, Contact = ClampToTable(contact), Pose = (byte)pose };
        }

        /// <summary>Host only: forget a bot's hand so it goes back to resting on the table.</summary>
        public static void ServerClearHand(ulong clientId)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned || !s.IsServer) return;
            s._server.Remove(clientId);
        }

        /// <summary>Local player makes a gesture: played at once here, and shared with the table.</summary>
        public static void GestureLocal(Gesture g)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned || g == Gesture.None) return;
            if (Time.time - _localGestureAt < GestureCooldown) return;
            _localGestureAt = Time.time;
            GestureMade?.Invoke(s.NetworkManager.LocalClientId, g);
            s.GestureServerRpc((byte)g);
        }

        /// <summary>Host only: a bot makes a gesture.</summary>
        public static void ServerBotGesture(ulong botId, Gesture g)
        {
            var s = _instance;
            if (s == null || !s.IsSpawned || !s.IsServer) return;
            s._lastGesture[botId] = Time.time;
            s.GestureClientRpc(botId, (byte)g);
        }

        public const float GestureCooldown = 1.6f;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void GestureServerRpc(byte gesture, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (gesture == 0 || gesture > (byte)Gesture.Horns) return;
            if (_lastGesture.TryGetValue(id, out var t) && Time.time - t < GestureCooldown - 0.2f) return;
            _lastGesture[id] = Time.time;
            GestureClientRpc(id, gesture);
        }

        [ClientRpc]
        void GestureClientRpc(ulong clientId, byte gesture)
        {
            if (clientId == NetworkManager.LocalClientId) return; // already playing locally
            GestureMade?.Invoke(clientId, (Gesture)gesture);
        }

        /// <summary>Bots are people too: now and then they sip their beer or show the table something.</summary>
        void ServerBotGestures()
        {
            var game = NetworkGameManager.Instance;
            if (game == null) return;
            foreach (var p in game.Players)
            {
                if (!p.IsBot || !p.IsConnected) continue;
                if (!_botNextGesture.TryGetValue(p.ClientId, out var next)) { _botNextGesture[p.ClientId] = Time.time + UnityEngine.Random.Range(6f, 20f); continue; }
                if (Time.time < next) continue;
                _botNextGesture[p.ClientId] = Time.time + UnityEngine.Random.Range(14f, 38f);
                if (game.Turns != null && game.Turns.ActiveClientId.Value == p.ClientId && game.Phase.Value == GamePhase.Playing) continue; // busy
                float r = UnityEngine.Random.value;
                var g = r < 0.38f ? Gesture.Beer : r < 0.66f ? Gesture.Smoke : r < 0.78f ? Gesture.Ok : r < 0.88f ? Gesture.Wave : r < 0.95f ? Gesture.Horns : Gesture.MiddleFinger;
                ServerBotGesture(p.ClientId, g);
            }
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
        void SubmitServerRpc(Vector3 contact, byte pose, Vector3 look, RpcParams rpc = default)
        {
            if (float.IsNaN(contact.x) || float.IsNaN(contact.y) || float.IsNaN(contact.z)) return;
            if (float.IsNaN(look.x) || float.IsNaN(look.y) || float.IsNaN(look.z) || look.sqrMagnitude < 0.01f) look = Vector3.zero;
            else look = look.normalized;
            ulong id = rpc.Receive.SenderClientId;
            _server[id] = new HandNetState { ClientId = id, Contact = ClampToTable(contact), Pose = (byte)Mathf.Min(pose, (int)HandPoseId.Cigarette), Look = look };
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
            if (IsSpawned && IsClient) ClientSend();
            if (!IsSpawned || !IsServer) return;
            ServerBotGestures();
            _broadcastTimer -= Time.deltaTime;
            // an empty list is still sent: it is how clients learn that the last published hand went away
            if (_broadcastTimer > 0f) return;
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
