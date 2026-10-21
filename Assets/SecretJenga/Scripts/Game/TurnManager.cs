using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Summary of one finished turn, handed to the task system (host only).</summary>
    public struct MoveReport
    {
        public ulong ClientId;
        public bool Success;            // block extracted and placed, tower still standing
        public bool AutoPlaced;         // placed by the timer, not by the player
        public bool TimedOut;
        public float Seconds;           // turn start -> placement
        public int SourceLayer;         // layer the block was pulled from (-1 if none)
        public float PeakWobble;        // highest tower wobble during the turn (0..1)
        public float MaxDisplacement;   // largest movement of any other block during the turn (world units)
    }

    /// <summary>
    /// Host-authoritative turn flow. Only the active player's requests are accepted and only one block can be
    /// handled at a time. Stages: WaitingForGrab -> Dragging (physics push/pull inside the tower) -> Carrying
    /// (lifted out, kinematic) -> Settling (placed on top, waiting for the tower to calm down) -> next turn.
    /// A turn is "successful" when a block was extracted and placed and the tower is still standing after settling.
    /// There is no turn limit: the round goes on until the tower falls - the player whose turn it was loses.
    /// Bots (host-side BotBrain) drive their turns through the same Server* methods the RPCs use.
    /// Card effects (Steady Grip, Pressure, Second Chance) are applied here; CardManager validates who may play what.
    /// </summary>
    public class TurnManager : NetworkBehaviour
    {
        public static event Action<ulong> TurnStarted;
        public static event Action<Vector3> BlockPlaced;
        public static event Action<string> PrivateMessage;

        public NetworkList<ulong> TurnOrder;
        public readonly NetworkVariable<ulong> ActiveClientId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<ulong> NextClientId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<TurnStage> Stage = new NetworkVariable<TurnStage>(TurnStage.None);
        public readonly NetworkVariable<int> TurnNumber = new NetworkVariable<int>();
        /// <summary>Successful turns completed at the table this round.</summary>
        public readonly NetworkVariable<int> MovesCompleted = new NetworkVariable<int>();
        public readonly NetworkVariable<bool> TimerActive = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> TurnEndTime = new NetworkVariable<double>();
        public readonly NetworkVariable<ulong> HeldBlockId = new NetworkVariable<ulong>();
        public readonly NetworkVariable<Vector3> CursorPoint = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> PlaceCenter = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<float> PlaceRotY = new NetworkVariable<float>();
        public readonly NetworkVariable<byte> FreeSlotMask = new NetworkVariable<byte>();

        // ---- card effects active on the current turn (replicated so every player sees them)
        public readonly NetworkVariable<bool> SteadyGripActive = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> TouchMoveActive = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> SecondChanceArmed = new NetworkVariable<bool>();
        /// <summary>Touch-move rule: the block the pressured player must take (0 = none touched yet).</summary>
        public readonly NetworkVariable<ulong> CommittedBlockId = new NetworkVariable<ulong>();

        public const float MaxPullForce = 70f;
        public const float MaxDragSpeed = 1.6f;
        public const float NormalLateralFollow = 0.12f;

        NetworkGameManager _game;
        BotBrain _bot;

        // ---- server-only turn context
        int _orderIndex = -1;
        JengaBlock _held, _touched, _lastPlaced, _committed;
        Vector3 _grabStart, _grabOffset, _dragPoint, _carryPoint;
        double _turnStart, _placedTime;
        bool _extracted, _autoPlaced, _turnFailed, _timedOut;
        JengaTower.ExtractInfo _extract;
        float _peakWobble, _calmTimer, _placementRefresh;
        Dictionary<JengaBlock, Vector3> _snapshot;
        CardDefinition _steadyDef, _secondChanceDef;
        readonly Dictionary<ulong, CardDefinition> _pressureQueue = new Dictionary<ulong, CardDefinition>();

        /// <summary>Block that must not count as "fallen" right now (the one being pushed/pulled or carried).</summary>
        public JengaBlock ExcludedBlock => _held;
        /// <summary>Host: the block placed this turn (used by Second Chance).</summary>
        public JengaBlock LastPlacedBlock => _lastPlaced;

        public bool IsLocalPlayersTurn => IsSpawned && ActiveClientId.Value == NetworkManager.LocalClientId;
        public double TimeRemaining => _game == null || !TimerActive.Value ? double.PositiveInfinity : Math.Max(0, TurnEndTime.Value - _game.ServerNow);

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
            _pressureQueue.Clear();
            _game.Stability.ServerReset();
            NextClientId.Value = order.Count > 0 ? order[0] : ulong.MaxValue;
        }

        public void ServerBeginFirstTurn() => NextTurn();

        public void ServerStop()
        {
            _bot?.Stop();
            if (_held != null) _held.ServerEndHold();
            _held = null;
            _touched = null;
            _lastPlaced = null;
            _committed = null;
            HeldBlockId.Value = 0;
            CommittedBlockId.Value = 0;
            Stage.Value = TurnStage.None;
            ActiveClientId.Value = ulong.MaxValue;
            NextClientId.Value = ulong.MaxValue;
            TimerActive.Value = false;
            ClearTurnEffects();
        }

        void ClearTurnEffects()
        {
            SteadyGripActive.Value = false;
            TouchMoveActive.Value = false;
            SecondChanceArmed.Value = false;
            _steadyDef = null;
            _secondChanceDef = null;
        }

        public void ServerOnPlayerDisconnected(ulong clientId)
        {
            _pressureQueue.Remove(clientId);
            if (_game.Phase.Value != GamePhase.Playing) return;
            if (ActiveClientId.Value == clientId)
            {
                // The active player left: finish their turn safely.
                if (Stage.Value == TurnStage.Carrying) PlaceHeld(FirstFreeSlot(), true);
                else if (Stage.Value == TurnStage.Dragging || Stage.Value == TurnStage.WaitingForGrab) FailTurn();
            }
            RefreshNext();
        }

        public void ServerOnPlayerReconnected(ulong oldClientId, ulong newClientId)
        {
            for (int i = 0; i < TurnOrder.Count; i++)
                if (TurnOrder[i] == oldClientId) TurnOrder[i] = newClientId;
            if (ActiveClientId.Value == oldClientId) ActiveClientId.Value = newClientId;
            if (_pressureQueue.TryGetValue(oldClientId, out var def)) { _pressureQueue.Remove(oldClientId); _pressureQueue[newClientId] = def; }
            RefreshNext();
        }

        /// <summary>Connected player after the given turn-order index (wrapping). May be the same player when alone.</summary>
        ulong ConnectedAfter(int index, out int foundIndex)
        {
            foundIndex = index;
            for (int k = 1; k <= TurnOrder.Count; k++)
            {
                int i = (index + k) % TurnOrder.Count;
                if (_game.TryGetPlayer(TurnOrder[i], out var pd) && pd.IsConnected) { foundIndex = i; return TurnOrder[i]; }
            }
            return ulong.MaxValue;
        }

        void RefreshNext()
        {
            if (TurnOrder.Count == 0) { NextClientId.Value = ulong.MaxValue; return; }
            NextClientId.Value = ConnectedAfter(Mathf.Max(0, _orderIndex), out _);
        }

        void NextTurn()
        {
            _touched = null;
            _committed = null;
            _lastPlaced = null;
            _snapshot = null;
            _extract = new JengaTower.ExtractInfo { Layer = -1 };
            _held = null;
            HeldBlockId.Value = 0;
            CommittedBlockId.Value = 0;
            ClearTurnEffects();

            int idx = -1;
            ulong next = TurnOrder.Count == 0 ? ulong.MaxValue : ConnectedAfter(_orderIndex < 0 ? TurnOrder.Count - 1 : _orderIndex, out idx);
            if (next == ulong.MaxValue)
            {
                Stage.Value = TurnStage.None;
                return;
            }
            _orderIndex = idx;

            _turnStart = _game.ServerNow;
            _extracted = _autoPlaced = _turnFailed = _timedOut = false;
            _peakWobble = 0f;
            TurnNumber.Value++;
            int secs = _game.TurnSeconds.Value;
            TimerActive.Value = secs > 0;
            TurnEndTime.Value = secs > 0 ? _turnStart + secs : 0;

            // Pressure played on this player during an earlier turn applies now.
            if (_pressureQueue.Remove(next)) TouchMoveActive.Value = true;

            Stage.Value = TurnStage.WaitingForGrab;
            if (ActiveClientId.Value == next) ActiveClientId.Value = ulong.MaxValue; // force change event (solo play)
            ActiveClientId.Value = next;
            RefreshNext();
            TurnStartedClientRpc(next, TouchMoveActive.Value);
            _game.Tasks.ServerOnTurnStarted(next);
            if (_game.IsBot(next))
            {
                if (_bot == null) _bot = new BotBrain(this, _game);
                _bot.Begin(next);
            }
            else _bot?.Stop();
        }

        [ClientRpc]
        void TurnStartedClientRpc(ulong clientId, bool pressured)
        {
            if (clientId == NetworkManager.LocalClientId) AudioManager.Instance?.Play(AudioManager.Sfx.TurnStart);
            if (pressured && clientId == NetworkManager.LocalClientId)
                PrivateMessage?.Invoke(L.Resolve(L.Msg("pressure_on_you")));
        }

        // ------------------------------------------------------------------ Card effects (called by CardManager after validation)
        public bool CanPlayCardNow(ulong clientId, CardTiming timing)
        {
            if (_game.Phase.Value != GamePhase.Playing || ActiveClientId.Value != clientId) return false;
            var st = Stage.Value;
            if (timing == CardTiming.OwnTurnBeforeExtraction) return st == TurnStage.WaitingForGrab || st == TurnStage.Dragging;
            return st == TurnStage.WaitingForGrab || st == TurnStage.Dragging || st == TurnStage.Carrying;
        }

        public void ServerApplySteadyGrip(CardDefinition def)
        {
            _steadyDef = def;
            SteadyGripActive.Value = true;
            if (_held != null && Stage.Value == TurnStage.Dragging) _held.Col.sharedMaterial = JengaBlock.HeldMaterial(def.heldFriction);
        }

        /// <summary>Returns the pressured player, or ulong.MaxValue when there is nobody else to pressure.</summary>
        public ulong ServerQueuePressure(ulong fromClientId, CardDefinition def)
        {
            ulong target = ConnectedAfter(_orderIndex, out _);
            if (target == ulong.MaxValue || target == fromClientId) return ulong.MaxValue;
            _pressureQueue[target] = def;
            return target;
        }

        public void ServerArmSecondChance(CardDefinition def)
        {
            _secondChanceDef = def;
            SecondChanceArmed.Value = true;
        }

        // ------------------------------------------------------------------ Client requests
        bool IsActive(ulong sender) =>
            _game.Phase.Value == GamePhase.Playing && sender == ActiveClientId.Value;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestGrabServerRpc(ulong blockId, Vector3 hitPoint, RpcParams rpc = default)
            => ServerTryGrab(rpc.Receive.SenderClientId, JengaBlock.Find(blockId), hitPoint);

        /// <summary>Host: the active player (human via RPC, or bot) touches a block.</summary>
        public bool ServerTryGrab(ulong sender, JengaBlock block, Vector3 hitPoint)
        {
            if (!IsActive(sender) || Stage.Value != TurnStage.WaitingForGrab) return false;
            if (block == null) return false;
            if (!_game.Tower.CanExtract(block, out var reason))
            {
                SendPrivateMessage(sender, reason);
                return false;
            }
            if (TouchMoveActive.Value)
            {
                if (_committed == null) { _committed = block; CommittedBlockId.Value = block.NetworkObjectId; }
                else if (_committed != block)
                {
                    SendPrivateMessage(sender, L.Msg("pressure_touched"));
                    return false;
                }
            }
            if (_snapshot == null) _snapshot = _game.Tower.SnapshotPositions(block);
            // Without Pressure a player may test several blocks; released blocks stay where they are.
            _held = block;
            _touched = block;
            _grabStart = block.Rb.position;
            _grabOffset = hitPoint - _grabStart;
            _grabOffset.y = 0;
            _dragPoint = hitPoint;
            _game.TryGetPlayer(sender, out var pd);
            block.ServerBeginHold(pd.ColorIndex, SteadyGripActive.Value && _steadyDef != null ? JengaBlock.HeldMaterial(_steadyDef.heldFriction) : null);
            HeldBlockId.Value = block.NetworkObjectId;
            CursorPoint.Value = hitPoint;
            Stage.Value = TurnStage.Dragging;
            return true;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        public void DragInputServerRpc(Vector3 worldPoint, RpcParams rpc = default) => ServerSetInput(rpc.Receive.SenderClientId, worldPoint);

        /// <summary>Host: where the active player's hand is pointing (drag target / carry target).</summary>
        public void ServerSetInput(ulong sender, Vector3 worldPoint)
        {
            if (!IsActive(sender) || _held == null) return;
            if (!IsFinite(worldPoint)) return;
            worldPoint = Vector3.ClampMagnitude(worldPoint - JengaTower.BaseCenter, 12f) + JengaTower.BaseCenter;
            CursorPoint.Value = worldPoint;
            if (Stage.Value == TurnStage.Dragging) _dragPoint = worldPoint;
            else if (Stage.Value == TurnStage.Carrying) _carryPoint = worldPoint;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseServerRpc(RpcParams rpc = default) => ServerRelease(rpc.Receive.SenderClientId);

        public void ServerRelease(ulong sender)
        {
            if (!IsActive(sender) || Stage.Value != TurnStage.Dragging || _held == null) return;
            _held.ServerEndHold();
            _held = null;
            HeldBlockId.Value = 0;
            Stage.Value = TurnStage.WaitingForGrab;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceServerRpc(int slot, RpcParams rpc = default) => ServerTryPlace(rpc.Receive.SenderClientId, slot);

        public bool ServerTryPlace(ulong sender, int slot)
        {
            if (!IsActive(sender) || Stage.Value != TurnStage.Carrying || _held == null) return false;
            if (slot < 0 || slot > 2 || (FreeSlotMask.Value & (1 << slot)) == 0)
            {
                SendPrivateMessage(sender, L.Msg("slot_taken"));
                return false;
            }
            PlaceHeld(slot, false);
            return true;
        }

        static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        public void SendPrivateMessage(ulong clientId, string msg)
        {
            if (string.IsNullOrEmpty(msg) || _game.IsBot(clientId)) return;
            var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            PrivateMessageClientRpc(new FixedString128Bytes(msg.Length > 120 ? msg.Substring(0, 120) : msg), p);
        }

        [ClientRpc]
        void PrivateMessageClientRpc(FixedString128Bytes msg, ClientRpcParams rpcParams = default)
        {
            PrivateMessage?.Invoke(L.Resolve(msg.ToString()));
            AudioManager.Instance?.Play(AudioManager.Sfx.Error);
        }

        // ------------------------------------------------------------------ Server simulation
        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _game == null || _game.Phase.Value != GamePhase.Playing) return;
            float dt = Time.fixedDeltaTime;
            var stage = Stage.Value;

            if (stage == TurnStage.Dragging || stage == TurnStage.Carrying || stage == TurnStage.Settling)
                _peakWobble = Mathf.Max(_peakWobble, _game.Stability.RawWobble);

            if (_bot != null && _bot.Active) _bot.Tick(dt);

            switch (stage)
            {
                case TurnStage.Dragging: TickDragging(dt); break;
                case TurnStage.Carrying: TickCarrying(dt); break;
                case TurnStage.Settling: TickSettling(dt); break;
            }

            if (TimerActive.Value && (stage == TurnStage.WaitingForGrab || stage == TurnStage.Dragging || stage == TurnStage.Carrying) && _game.ServerNow >= TurnEndTime.Value)
                OnTimeout();
        }

        void TickDragging(float dt)
        {
            if (_held == null || !_held.IsSpawned) { Stage.Value = TurnStage.WaitingForGrab; _held = null; HeldBlockId.Value = 0; return; }
            var rb = _held.Rb;
            var axis = _held.LongAxis;
            bool steady = SteadyGripActive.Value && _steadyDef != null;
            float maxSpeed = MaxDragSpeed * (steady ? _steadyDef.pullSpeedMultiplier : 1f);
            float lateralFollow = steady ? _steadyDef.lateralFollow : NormalLateralFollow;

            // Mouse movement projected mostly on the block's long axis (you push/pull a Jenga block, you don't steer it).
            Vector3 delta = (_dragPoint - _grabOffset) - _grabStart;
            delta.y = 0;
            Vector3 along = Vector3.Dot(delta, axis) * axis;
            Vector3 lateral = delta - along;
            Vector3 target = _grabStart + along + lateral * lateralFollow;

            Vector3 toTarget = target - rb.position;
            toTarget.y = 0;
            Vector3 desiredVel = Vector3.ClampMagnitude(toTarget * 9f, maxSpeed);
            Vector3 v = rb.linearVelocity;
            Vector3 dv = desiredVel - new Vector3(v.x, 0, v.z);
            Vector3 force = Vector3.ClampMagnitude(dv * rb.mass / dt * 0.5f, MaxPullForce);
            rb.AddForce(force, ForceMode.Force);
            rb.angularVelocity *= steady ? 0.7f : 0.85f;

            if (_game.Tower.IsOutOfTower(_held, _held.Layer.Value)) BeginCarry();
        }

        void BeginCarry()
        {
            _extract = _game.Tower.ServerDetach(_held);
            _held.ServerBeginCarry();
            _extracted = true;
            SteadyGripActive.Value = false; // Steady Grip lasts until the block is out
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

        public int FirstFreeSlot()
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
            _lastPlaced = _held;
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
            var who = _game.PlayerName(ActiveClientId.Value);
            if (!_extracted && SecondChanceArmed.Value && _secondChanceDef != null)
            {
                // Second Chance, case 1: the turn would time out before the block is out -> extra time once.
                SecondChanceArmed.Value = false;
                double extra = Math.Max(5, _game.TurnSeconds.Value * _secondChanceDef.extraTimeFraction);
                TurnEndTime.Value = _game.ServerNow + extra;
                _game.Cards.ServerAnnounceEffect(ActiveClientId.Value, CardKind.SecondChance, L.Msg("sc_time", who, extra.ToString("0")));
                return;
            }
            _timedOut = true;
            if (Stage.Value == TurnStage.Carrying) PlaceHeld(FirstFreeSlot(), true);
            else FailTurn();
            _game.ServerNotify(L.Msg("ran_out", who));
        }

        /// <summary>
        /// Second Chance, case 2 (called by TowerStabilityChecker): the only fallen block is the one placed this turn
        /// and the rest of the tower stands. The player picks it up again and gets to place it once more.
        /// Returns false when the card is not armed (then the fall counts as a collapse).
        /// </summary>
        public bool ServerTryRecoverPlacedBlock()
        {
            if (!SecondChanceArmed.Value || _secondChanceDef == null || Stage.Value != TurnStage.Settling || _lastPlaced == null || _turnFailed) return false;
            SecondChanceArmed.Value = false;
            var b = _lastPlaced;
            _lastPlaced = null;
            _game.Tower.ServerDetach(b);
            _held = b;
            b.ServerBeginHold(_game.TryGetPlayer(ActiveClientId.Value, out var pd) ? pd.ColorIndex : (byte)0);
            b.ServerBeginCarry();
            RefreshPlacement();
            var above = PlaceCenter.Value + Vector3.up * (JengaDims.BlockHeight * 3f);
            b.Rb.position = above;
            b.transform.position = above;
            _carryPoint = above;
            HeldBlockId.Value = b.NetworkObjectId;
            if (TimerActive.Value) TurnEndTime.Value = Math.Max(TurnEndTime.Value, _game.ServerNow + 10);
            Stage.Value = TurnStage.Carrying;
            _game.Cards.ServerAnnounceEffect(ActiveClientId.Value, CardKind.SecondChance, L.Msg("sc_catch", _game.PlayerName(ActiveClientId.Value)));
            return true;
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
            if (_game.Phase.Value != GamePhase.Playing) return; // collapse already ended the round

            bool success = _extracted && !_turnFailed;
            var report = new MoveReport
            {
                ClientId = ActiveClientId.Value,
                Success = success,
                AutoPlaced = _autoPlaced,
                TimedOut = _timedOut,
                Seconds = (float)(_placedTime - _turnStart),
                SourceLayer = _extract.Layer,
                PeakWobble = _peakWobble,
                MaxDisplacement = JengaTower.MaxDisplacement(_snapshot, _lastPlaced),
            };
            if (success) MovesCompleted.Value++;
            else _game.ServerNotify(L.Msg("not_count", _game.PlayerName(report.ClientId)));
            _game.Tasks.ServerOnMove(report);

            // No turn limit: the tower keeps growing until somebody drops it.
            NextTurn();
        }
    }
}
