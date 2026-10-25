using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Host-side AI for bot seats. A bot plays through exactly the same TurnManager entry points as a human
    /// (ServerTryGrab -> ServerSetInput -> ServerTryPlace), so its blocks are moved by the same physics and a bot
    /// can drop the tower - and lose - like anyone else. It is deliberately imperfect: it hesitates, prefers
    /// sensible blocks, pulls with a shaky hand and gives up on blocks that are stuck.
    /// </summary>
    public class BotBrain
    {
        enum State { Idle, Thinking, Pulling, Carrying, Waiting }

        readonly TurnManager _turns;
        readonly NetworkGameManager _game;
        readonly HashSet<JengaBlock> _tried = new HashSet<JengaBlock>();

        State _state = State.Idle;
        ulong _id;
        float _timer, _pull, _pullSpeed, _nerves, _carryTime, _lookTimer;
        int _attempts, _slot;
        Vector3 _start, _dir, _carryFrom;
        JengaBlock _block;

        public bool Active => _state != State.Idle;

        public BotBrain(TurnManager turns, NetworkGameManager game)
        {
            _turns = turns;
            _game = game;
        }

        public void Begin(ulong botId)
        {
            // one brain serves every bot: drop the previous bot's hand, or it stays frozen mid-gesture over the table
            Stop();
            _id = botId;
            _tried.Clear();
            _attempts = 0;
            _nerves = Random.Range(0.7f, 1.35f);
            _state = State.Thinking;
            _timer = Random.Range(1.6f, 3.4f);
        }

        public void Stop()
        {
            if (_state != State.Idle) HandSync.ServerClearHand(_id);
            _state = State.Idle;
        }

        /// <summary>While thinking, the bot's hand hovers over the tower and points at blocks it is considering.</summary>
        void Ponder(float dt)
        {
            _lookTimer -= dt;
            if (_lookTimer > 0f) return;
            _lookTimer = Random.Range(0.5f, 1.1f);
            var layers = _game.Tower.Layers;
            int max = Mathf.Min(layers.Count, _game.Tower.ForbiddenFromLayer.Value);
            if (max <= 0) return;
            var row = layers[Random.Range(0, max)];
            if (row.Count == 0) return;
            var b = row[Random.Range(0, row.Count)];
            if (b == null) return;
            var outward = _game.TryGetPlayer(_id, out var me) ? SeatLayout.Outward(me.SeatIndex) : Vector3.back;
            HandSync.ServerSetHand(_id, b.transform.position + outward * 0.45f, HandPoseId.Point);
        }

        public void Tick(float dt)
        {
            if (_turns.ActiveClientId.Value != _id) { Stop(); return; }
            var stage = _turns.Stage.Value;
            if (stage == TurnStage.Settling) { if (_state != State.Waiting) HandSync.ServerClearHand(_id); _state = State.Waiting; return; }

            switch (_state)
            {
                case State.Thinking:
                    _timer -= dt;
                    Ponder(dt);
                    if (_timer > 0f) return;
                    if (stage == TurnStage.Carrying) { EnterCarry(); return; }   // e.g. Second Chance handed the block back
                    MaybePlayCard();
                    _block = ChooseBlock();
                    if (_block == null) { _state = State.Waiting; return; }      // nothing sensible: let the timer decide
                    _start = _block.Rb.position;
                    // pull towards our own chair, like a person would
                    var outward = _game.TryGetPlayer(_id, out var me) ? SeatLayout.Outward(me.SeatIndex) : Vector3.back;
                    _dir = _block.LongAxis * (Vector3.Dot(_block.LongAxis, outward) >= 0f ? 1f : -1f);
                    if (!_turns.ServerTryGrab(_id, _block, _start))
                    {
                        _tried.Add(_block);
                        _timer = 0.4f;
                        return;
                    }
                    _pull = 0f;
                    _pullSpeed = Random.Range(0.35f, 0.6f) / _nerves;
                    _timer = 0f;
                    _state = State.Pulling;
                    break;

                case State.Pulling:
                    if (stage == TurnStage.Carrying) { EnterCarry(); return; }
                    if (stage != TurnStage.Dragging) { _state = State.Thinking; _timer = 0.6f; return; }
                    _timer += dt;
                    // a careful bot slows down as soon as the tower starts to sway
                    float sway = _game.Stability.RawWobble;
                    float speed = _pullSpeed * (sway > 0.2f ? 0.35f : 1f);
                    _pull = Mathf.Min(_pull + speed * dt, 2.4f);
                    var shake = Random.insideUnitSphere * 0.025f * _nerves;
                    shake.y = 0f;
                    _turns.ServerSetInput(_id, _start + _dir * _pull + shake);

                    // stuck (the block is pinned) or too scary: let go and try another one
                    float moved = Vector3.Dot(_block != null ? _block.Rb.position - _start : Vector3.zero, _dir);
                    bool stuck = _timer > 2.5f && moved < _pull * 0.35f;
                    bool scared = sway > 0.45f && _attempts < 2 && moved < JengaDims.BlockLength * 0.5f;
                    if (_timer > 12f || stuck || scared)
                    {
                        _turns.ServerRelease(_id);
                        if (_block != null) _tried.Add(_block);
                        _attempts++;
                        _state = _attempts < 4 ? State.Thinking : State.Waiting;
                        _timer = Random.Range(0.8f, 1.6f);
                    }
                    break;

                case State.Carrying:
                    if (stage != TurnStage.Carrying) { _state = State.Waiting; return; }
                    _timer += dt;
                    var slotPos = JengaTower.SlotPosition(_turns.PlaceCenter.Value, _turns.PlaceRotY.Value, _slot);
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_timer / (_carryTime * 0.8f)));
                    _turns.ServerSetInput(_id, Vector3.Lerp(_carryFrom, slotPos + Vector3.up * 0.9f, k));
                    if (_timer > _carryTime)
                    {
                        if ((_turns.FreeSlotMask.Value & (1 << _slot)) == 0) _slot = _turns.FirstFreeSlot();
                        _turns.ServerTryPlace(_id, _slot);
                        _state = State.Waiting;
                    }
                    break;
            }
        }

        void EnterCarry()
        {
            _state = State.Carrying;
            _timer = 0f;
            _carryTime = Random.Range(1.4f, 2.4f);
            _carryFrom = _block != null ? _block.Rb.position : _turns.PlaceCenter.Value;
            _slot = PickSlot();
        }

        void MaybePlayCard()
        {
            if (!_game.Cards.ServerCanPlay(_id, out var kind)) return;
            float chance = kind == CardKind.SteadyGrip ? 0.55f : kind == CardKind.Pressure ? 0.45f : kind == CardKind.SecondChance ? 0.35f : 0f;
            if (Random.value < chance) _game.Cards.ServerTryPlay(_id, false);
        }

        JengaBlock ChooseBlock()
        {
            var tower = _game.Tower;
            JengaBlock best = null;
            float bestScore = float.MinValue;
            var layers = tower.Layers;
            for (int l = 0; l < layers.Count && l < tower.ForbiddenFromLayer.Value; l++)
            {
                var row = layers[l];
                foreach (var b in row)
                {
                    if (b == null || _tried.Contains(b) || !tower.CanExtract(b, out _)) continue;
                    float score = l * 0.35f + Random.Range(0f, 3.5f);
                    if (row.Count == 3) score += b.Slot == 1 ? 3.5f : 2.5f;     // full rows are the safe ones
                    else if (row.Count == 2)
                    {
                        // taking a side block is fine only when the centre one stays behind
                        bool centreStays = false;
                        foreach (var o in row) if (o != b && o.Slot == 1) centreStays = true;
                        score += centreStays ? 0.5f : -7f;
                    }
                    else score -= 9f;                                           // the last block of a row
                    if (score > bestScore) { bestScore = score; best = b; }
                }
            }
            return best;
        }

        int PickSlot()
        {
            var free = new List<int>();
            for (int i = 0; i < 3; i++) if ((_turns.FreeSlotMask.Value & (1 << i)) != 0) free.Add(i);
            if (free.Count == 0) return 1;
            // a fresh layer is started from the middle-ish, a partial one is completed
            if (free.Count == 3) return Random.value < 0.6f ? 1 : free[Random.Range(0, 3)];
            return free[Random.Range(0, free.Count)];
        }
    }
}
