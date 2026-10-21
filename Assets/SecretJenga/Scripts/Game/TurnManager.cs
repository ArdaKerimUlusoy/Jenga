using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Summary of one finished turn, handed to the mission system (server only).</summary>
    public struct MoveReport
    {
        public ulong ClientId;
        public bool Success;          // block extracted and placed, tower still standing
        public bool AutoPlaced;       // placed by the timer, not by the player
        public float Seconds;         // turn start -> placement
        public BlockColor Color;
        public int SourceLayer;
        public bool RiskyStructure;   // left the source layer with a single edge block (or empty)
        public float PeakWobble;      // highest wobble while the player handled the block (0..1)
        public float ShakyHoldSeconds;// time the tower was visibly shaking while the player handled the block
    }

    /// <summary>
    /// Host-authoritative turn flow. Only the active player's requests are accepted and only one block can be
    /// handled at a time. Stages: WaitingForGrab -> Dragging (physics push/pull inside the tower) -> Carrying
    /// (lifted out, kinematic) -> Settling (placed on top, waiting for the tower to calm down) -> next turn.
    /// </summary>
    public class TurnManager : NetworkBehaviour
    {
        public static event Action<ulong> TurnStarted;
        public static event Action<Vector3> BlockPlaced;
        public static event Action<string> PrivateMessage;

        public NetworkList<ulong> TurnOrder;
        public readonly NetworkVariable<ulong> ActiveClientId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<TurnStage> Stage = new NetworkVariable<TurnStage>(TurnStage.None);
        public readonly NetworkVariable<int> TurnNumber = new NetworkVariable<int>();
        public readonly NetworkVariable<int> MovesCompleted = new NetworkVariable<int>();
        public readonly NetworkVariable<double> TurnEndTime = new NetworkVariable<double>();
        public readonly NetworkVariable<ulong> HeldBlockId = new NetworkVariable<ulong>();
        public readonly NetworkVariable<Vector3> CursorPoint = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> PlaceCenter = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<float> PlaceRotY = new NetworkVariable<float>();
        public readonly NetworkVariable<byte> FreeSlotMask = new NetworkVariable<byte>();

        public const float MaxPullForce = 70f;
        public const float MaxDragSpeed = 1.6f;
        public const float ShakyWobble = 0.3f;

        NetworkGameManager _game;

        // ---- server-only turn context
        int _orderIndex = -1;
        JengaBlock _held;
        JengaBlock _touched;
        Vector3 _grabStart, _grabOffset, _dragPoint, _carryPoint;
        double _turnStart, _placedTime;
        bool _extracted, _autoPlaced, _turnFailed;
        JengaTower.ExtractInfo _extract;
        float _peakWobble, _shakySeconds, _calmTimer, _placementRefresh;

        /// <summary>Block that must not count as "fallen" right now (the one being pushed/pulled or carried).</summary>
        public JengaBlock ExcludedBlock => _held;

        public bool IsLocalPlayersTurn => IsSpawned && ActiveClientId.Value == NetworkManager.LocalClientId;
        public double TimeRemaining => _game == null ? 0 : Math.Max(0, TurnEndTime.Value - _game.ServerNow);

        void Awake()
        {
            TurnOrder = new NetworkList<ulong>();
            _game = GetComponent<NetworkGameManager>();
        }

        public override void OnNetworkSpawn()
        {
            ActiveClientId.OnValueChanged += OnActiveChanged;
        }

        public override void OnNetworkDespawn()
        {
            ActiveClientId.OnValueChanged -= OnActiveChanged;
        }

        void OnActiveChanged(ulong prev, ulong cur)
        {
            if (cur != ulong.MaxValue) TurnStarted?.Invoke(cur);
        }

        // ------------------------------------------------------------------ Server lifecycle
        public void ServerPrepare(List<ulong> order)
        {
            ServerStop();
            TurnOrder.Clear();
            foreach (var id in order) TurnOrder.Add(id);
            _orderIndex = -1;
            TurnNumber.Value = 0;
            MovesCompleted.Value = 0;
            _game.Stability.ServerReset();
        }

        public void ServerBeginFirstTurn() => NextTurn();

        public void ServerStop()
        {
            if (_held != null) _held.ServerEndHold();
            _held = null;
            _touched = null;
            HeldBlockId.Value = 0;
            Stage.Value = TurnStage.None;
            ActiveClientId.Value = ulong.MaxValue;
        }

        public void ServerOnPlayerDisconnected(ulong clientId)
        {
            if (_game.Phase.Value != GamePhase.Playing || ActiveClientId.Value != clientId) return;
            // The active player left: finish their turn safely.
            if (Stage.Value == TurnStage.Carrying) PlaceHeld(FirstFreeSlot(), true);
            else if (Stage.Value == TurnStage.Dragging || Stage.Value == TurnStage.WaitingForGrab) FailTurn();
        }

        public void ServerOnPlayerReconnected(ulong oldClientId, ulong newClientId)
        {
            for (int i = 0; i < TurnOrder.Count; i++)
                if (TurnOrder[i] == oldClientId) TurnOrder[i] = newClientId;
            if (ActiveClientId.Value == oldClientId) ActiveClientId.Value = newClientId;
        }

        void NextTurn()
        {
            _touched = null;
            _extract = new JengaTower.ExtractInfo { Layer = -1 };
            _held = null;
            HeldBlockId.Value = 0;

            ulong next = ulong.MaxValue;
            for (int k = 0; k < TurnOrder.Count; k++)
            {
                _orderIndex = (_orderIndex + 1) % TurnOrder.Count;
                var id = TurnOrder[_orderIndex];
                if (_game.TryGetPlayer(id, out var pd) && pd.IsConnected) { next = id; break; }
            }
            if (next == ulong.MaxValue)
            {
                Stage.Value = TurnStage.None;
                return;
            }

            _turnStart = _game.ServerNow;
            _extracted = _autoPlaced = _turnFailed = false;
            _peakWobble = _shakySeconds = 0f;
            TurnNumber.Value++;
            TurnEndTime.Value = _turnStart + _game.TurnSeconds.Value;
            Stage.Value = TurnStage.WaitingForGrab;
            if (ActiveClientId.Value == next) ActiveClientId.Value = ulong.MaxValue; // force change event (solo play)
            ActiveClientId.Value = next;
            TurnStartedClientRpc(next);
        }

        [ClientRpc]
        void TurnStartedClientRpc(ulong clientId)
        {
            if (clientId == NetworkManager.LocalClientId) AudioManager.Instance?.Play(AudioManager.Sfx.TurnStart);
        }

        // ------------------------------------------------------------------ Client requests
        bool IsActive(ServerRpcParams rpc) =>
            _game.Phase.Value == GamePhase.Playing && rpc.Receive.SenderClientId == ActiveClientId.Value;

        [ServerRpc(RequireOwnership = false)]
        public void RequestGrabServerRpc(ulong blockId, Vector3 hitPoint, ServerRpcParams rpc = default)
        {
            if (!IsActive(rpc) || Stage.Value != TurnStage.WaitingForGrab) return;
            var block = JengaBlock.Find(blockId);
            if (block == null) return;
            if (!_game.Tower.CanExtract(block, out var reason))
            {
                SendPrivateMessage(rpc.Receive.SenderClientId, reason);
                return;
            }
            // Touch-move rule is relaxed: a player may test several blocks; released blocks stay where they are.
            _held = block;
            _touched = block;
            _grabStart = block.Rb.position;
            _grabOffset = hitPoint - _grabStart;
            _grabOffset.y = 0;
            _dragPoint = hitPoint;
            _game.TryGetPlayer(rpc.Receive.SenderClientId, out var pd);
            block.ServerBeginHold(pd.ColorIndex);
            HeldBlockId.Value = block.NetworkObjectId;
            CursorPoint.Value = hitPoint;
            Stage.Value = TurnStage.Dragging;
        }

        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        public void DragInputServerRpc(Vector3 worldPoint, ServerRpcParams rpc = default)
        {
            if (!IsActive(rpc) || _held == null) return;
            if (!IsFinite(worldPoint)) return;
            worldPoint = Vector3.ClampMagnitude(worldPoint - JengaTower.BaseCenter, 12f) + JengaTower.BaseCenter;
            CursorPoint.Value = worldPoint;
            if (Stage.Value == TurnStage.Dragging) _dragPoint = worldPoint;
            else if (Stage.Value == TurnStage.Carrying) _carryPoint = worldPoint;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReleaseServerRpc(ServerRpcParams rpc = default)
        {
            if (!IsActive(rpc) || Stage.Value != TurnStage.Dragging || _held == null) return;
            _held.ServerEndHold();
            _held = null;
            HeldBlockId.Value = 0;
            Stage.Value = TurnStage.WaitingForGrab;
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlaceServerRpc(int slot, ServerRpcParams rpc = default)
        {
            if (!IsActive(rpc) || Stage.Value != TurnStage.Carrying || _held == null) return;
            if (slot < 0 || slot > 2 || (FreeSlotMask.Value & (1 << slot)) == 0) return;
            PlaceHeld(slot, false);
        }

        static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        void SendPrivateMessage(ulong clientId, string msg)
        {
            var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            PrivateMessageClientRpc(new FixedString128Bytes(msg), p);
        }

        [ClientRpc]
        void PrivateMessageClientRpc(FixedString128Bytes msg, ClientRpcParams rpcParams = default)
        {
            PrivateMessage?.Invoke(msg.ToString());
            AudioManager.Instance?.Play(AudioManager.Sfx.Error);
        }

        // ------------------------------------------------------------------ Server simulation
        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _game == null || _game.Phase.Value != GamePhase.Playing) return;
            float dt = Time.fixedDeltaTime;
            var stage = Stage.Value;

            if (stage == TurnStage.Dragging || stage == TurnStage.Carrying)
            {
                float w = _game.Stability.RawWobble;
                _peakWobble = Mathf.Max(_peakWobble, w);
                if (w >= ShakyWobble) _shakySeconds += dt;
            }

            switch (stage)
            {
                case TurnStage.Dragging: TickDragging(dt); break;
                case TurnStage.Carrying: TickCarrying(dt); break;
                case TurnStage.Settling: TickSettling(dt); break;
            }

            if ((stage == TurnStage.WaitingForGrab || stage == TurnStage.Dragging || stage == TurnStage.Carrying) && _game.ServerNow >= TurnEndTime.Value)
                OnTimeout();
        }

        void TickDragging(float dt)
        {
            if (_held == null || !_held.IsSpawned) { Stage.Value = TurnStage.WaitingForGrab; _held = null; HeldBlockId.Value = 0; return; }
            var rb = _held.Rb;
            var axis = _held.LongAxis;

            // Mouse movement projected mostly on the block's long axis (you push/pull a Jenga block, you don't steer it).
            Vector3 delta = (_dragPoint - _grabOffset) - _grabStart;
            delta.y = 0;
            Vector3 along = Vector3.Dot(delta, axis) * axis;
            Vector3 lateral = delta - along;
            Vector3 target = _grabStart + along + lateral * 0.12f;

            Vector3 toTarget = target - rb.position;
            toTarget.y = 0;
            Vector3 desiredVel = Vector3.ClampMagnitude(toTarget * 9f, MaxDragSpeed);
            Vector3 v = rb.linearVelocity;
            Vector3 dv = desiredVel - new Vector3(v.x, 0, v.z);
            Vector3 force = Vector3.ClampMagnitude(dv * rb.mass / dt * 0.5f, MaxPullForce);
            rb.AddForce(force, ForceMode.Force);
            rb.angularVelocity *= 0.85f;

            if (_game.Tower.IsOutOfTower(_held, _held.Layer.Value)) BeginCarry();
        }

        void BeginCarry()
        {
            _extract = _game.Tower.ServerDetach(_held);
            _held.ServerBeginCarry();
            _extracted = true;
            RefreshPlacement();
            _carryPoint = _held.Rb.position + Vector3.up * 0.8f;
            Stage.Value = TurnStage.Carrying;
        }

        void RefreshPlacement()
        {
            var p = _game.Tower.ComputePlacement();
            PlaceCenter.Value = p.Center;
            PlaceRotY.Value = p.RotY;
            FreeSlotMask.Value = p.FreeMask;
        }

        void TickCarrying(float dt)
        {
            if (_held == null || !_held.IsSpawned) { FailTurn(); return; }
            _placementRefresh -= dt;
            if (_placementRefresh <= 0f) { _placementRefresh = 0.25f; RefreshPlacement(); }

            var center = PlaceCenter.Value;
            var target = _carryPoint;
            float minY = center.y + JengaDims.BlockHeight * 2.5f;
            target.y = Mathf.Max(minY, Mathf.Min(target.y, minY + 2f));
            var flat = new Vector3(target.x - center.x, 0, target.z - center.z);
            flat = Vector3.ClampMagnitude(flat, 4f);
            target = new Vector3(center.x + flat.x, target.y, center.z + flat.z);
            _held.ServerMoveCarried(target, Quaternion.Euler(0, PlaceRotY.Value, 0), dt);
        }

        int FirstFreeSlot()
        {
            int[] preference = { 1, 0, 2 };
            foreach (var s in preference) if ((FreeSlotMask.Value & (1 << s)) != 0) return s;
            return 1;
        }

        void PlaceHeld(int slot, bool auto)
        {
            if (_held == null) { FailTurn(); return; }
            RefreshPlacement();
            if ((FreeSlotMask.Value & (1 << slot)) == 0) slot = FirstFreeSlot();
            var p = _game.Tower.ComputePlacement();
            var pos = JengaTower.SlotPosition(p.Center, p.RotY, slot);
            var rot = Quaternion.Euler(0, p.RotY, 0);
            _held.ServerPlaceAt(pos, rot);
            _game.Tower.ServerAttach(_held, p.Layer, slot);
            _autoPlaced = auto;
            _placedTime = _game.ServerNow;
            _calmTimer = 0;
            var placed = _held;
            _held = null;
            HeldBlockId.Value = 0;
            Stage.Value = TurnStage.Settling;
            BlockPlacedClientRpc(placed.transform.position);
        }

        [ClientRpc]
        void BlockPlacedClientRpc(Vector3 at)
        {
            AudioManager.Instance?.PlayPlace(at);
            EffectsManager.Instance?.Puff(at);
            BlockPlaced?.Invoke(at);
        }

        void OnTimeout()
        {
            if (Stage.Value == TurnStage.Carrying) PlaceHeld(FirstFreeSlot(), true);
            else FailTurn();
            _game.ServerNotify($"{_game.PlayerName(ActiveClientId.Value)} ran out of time.");
        }

        void FailTurn()
        {
            if (_held != null) _held.ServerEndHold();
            _held = null;
            HeldBlockId.Value = 0;
            _turnFailed = true;
            _placedTime = _game.ServerNow;
            _calmTimer = 0;
            Stage.Value = TurnStage.Settling; // watch the tower briefly before the next turn
        }

        void TickSettling(float dt)
        {
            float elapsed = (float)(_game.ServerNow - _placedTime);
            if (_game.Stability.MaxSpeed < 0.06f) _calmTimer += dt;
            else _calmTimer = 0f;

            float minWait = _turnFailed ? 0.5f : GameRules.SettleMinSeconds;
            if ((elapsed >= minWait && _calmTimer >= GameRules.SettleCalmSeconds) || elapsed >= GameRules.SettleMaxSeconds)
                FinishTurn();
        }

        void FinishTurn()
        {
            if (_game.Phase.Value != GamePhase.Playing) return; // collapse already ended the match

            bool success = _extracted && !_turnFailed;
            var color = _touched != null ? _touched.Color.Value : BlockColor.Natural;
            var report = new MoveReport
            {
                ClientId = ActiveClientId.Value,
                Success = success,
                AutoPlaced = _autoPlaced,
                Seconds = (float)(_placedTime - _turnStart),
                Color = color,
                SourceLayer = _extract.Layer,
                RiskyStructure = success && (_extract.Remaining == 0 || (_extract.Remaining == 1 && !_extract.RemainingIsCenterOnly)),
                PeakWobble = _peakWobble,
                ShakyHoldSeconds = _shakySeconds,
            };
            if (success) MovesCompleted.Value++;
            _game.Missions.ServerOnMove(report);

            if (success && _game.Tower.IsTargetReached(_game.TargetHeight.Value))
            {
                _game.ServerEndGame(true, $"The tower reached {_game.TargetHeight.Value} layers!");
                return;
            }
            NextTurn();
        }
    }
}
