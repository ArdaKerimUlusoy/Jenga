using System;
using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Experimental special cards. Each player is dealt one random card per round (held privately, like the tasks).
    /// A client asks to play its card; the host validates ownership, timing and target against the authoritative
    /// game state, applies the effect through TurnManager and announces it to every player. The UI only displays
    /// state - no card logic lives there.
    /// </summary>
    public class CardManager : NetworkBehaviour
    {
        // ---- client side (local player only)
        public static CardInfo LocalCard { get; private set; }
        public static event Action<CardInfo> LocalCardChanged;
        /// <summary>(user, kind, target or ulong.MaxValue, message) - raised on every client when a card is played.</summary>
        public static event Action<ulong, CardKind, ulong, string> CardPlayed;

        readonly Dictionary<string, CardInfo> _hands = new Dictionary<string, CardInfo>();
        readonly List<string> _log = new List<string>();
        NetworkGameManager _game;

        void Awake() => _game = GetComponent<NetworkGameManager>();

        public override void OnNetworkSpawn() => ClearLocal();
        public override void OnNetworkDespawn() => ClearLocal();

        static void ClearLocal()
        {
            LocalCard = default;
            LocalCardChanged?.Invoke(default);
        }

        string AuthOf(ulong clientId) => _game.TryGetPlayer(clientId, out var p) ? p.AuthId.ToString() : null;

        public IReadOnlyList<string> ServerLog => _log;

        // ------------------------------------------------------------------ Server: dealing
        /// <param name="tasks">clientId -> dealt task kind (a Pressure Player is guaranteed an intervention card).</param>
        public void ServerDeal(List<PlayerData> players, Dictionary<ulong, TaskKind> tasks)
        {
            if (!IsServer) return;
            _hands.Clear();
            _log.Clear();
            ClearAllClientRpc();

            var pool = new List<int>();
            var defs = GameContent.Cards;
            for (int i = 0; i < defs.Count; i++) if (defs[i].inPool && defs[i].kind != CardKind.None) pool.Add(i);
            if (pool.Count == 0) return;
            int pressureIndex = GameContent.FirstCardOfKind(CardKind.Pressure);

            foreach (var p in players)
            {
                if (!p.IsConnected) continue;
                int idx = pool[UnityEngine.Random.Range(0, pool.Count)];
                if (tasks != null && tasks.TryGetValue(p.ClientId, out var kind) && kind == TaskKind.PressurePlayer && pressureIndex >= 0) idx = pressureIndex;
                var info = new CardInfo { Kind = defs[idx].kind, DefinitionIndex = (byte)idx, Used = false };
                _hands[p.AuthId.ToString()] = info;
                SendCardTo(p.ClientId, info);
            }
        }

        public void ServerResendTo(ulong clientId)
        {
            var auth = AuthOf(clientId);
            if (auth != null && _hands.TryGetValue(auth, out var c)) SendCardTo(clientId, c);
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            _hands.Clear();
            _log.Clear();
            ClearAllClientRpc();
        }

        public bool ServerTryGetCard(string auth, out CardInfo card) => _hands.TryGetValue(auth, out card);

        void SendCardTo(ulong clientId, CardInfo info)
        {
            if (_game.IsBot(clientId)) return;
            var p = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            CardUpdateClientRpc(info, p);
        }

        // ------------------------------------------------------------------ Client -> host
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlayCardServerRpc(RpcParams rpc = default) => ServerTryPlay(rpc.Receive.SenderClientId, true);

        /// <summary>Host: does this player hold an unused card that may be played right now? (used by bots)</summary>
        public bool ServerCanPlay(ulong clientId, out CardKind kind)
        {
            kind = CardKind.None;
            var auth = AuthOf(clientId);
            if (auth == null || !_hands.TryGetValue(auth, out var card) || card.Kind == CardKind.None || card.Used) return false;
            var def = GameContent.Card(card.DefinitionIndex);
            if (def == null || !_game.Turns.CanPlayCardNow(clientId, def.timing)) return false;
            kind = def.kind;
            return true;
        }

        /// <summary>Host-side validation + effect. Players arrive here through the RPC, bots directly.</summary>
        public bool ServerTryPlay(ulong sender, bool tellSender)
        {
            if (!IsServer) return false;
            var auth = AuthOf(sender);
            if (auth == null || !_hands.TryGetValue(auth, out var card) || card.Kind == CardKind.None) return false;
            if (card.Used) { if (tellSender) _game.Turns.SendPrivateMessage(sender, L.Msg("card_cant")); return false; }
            var def = GameContent.Card(card.DefinitionIndex);
            if (def == null) return false;
            if (!_game.Turns.CanPlayCardNow(sender, def.timing))
            {
                if (tellSender) _game.Turns.SendPrivateMessage(sender, L.Msg("card_cant"));
                return false;
            }

            string who = _game.PlayerName(sender);
            ulong target = ulong.MaxValue;
            string msg;
            switch (def.kind)
            {
                case CardKind.SteadyGrip:
                    _game.Turns.ServerApplySteadyGrip(def);
                    msg = L.Msg("card_steady", who);
                    break;
                case CardKind.Pressure:
                    target = _game.Turns.ServerQueuePressure(sender, def);
                    if (target == ulong.MaxValue) { if (tellSender) _game.Turns.SendPrivateMessage(sender, L.Msg("card_nobody")); return false; }
                    msg = L.Msg("card_pressure", who, _game.PlayerName(target));
                    break;
                case CardKind.SecondChance:
                    _game.Turns.ServerArmSecondChance(def);
                    msg = L.Msg("card_second", who);
                    break;
                default:
                    return false;
            }

            card.Used = true;
            _hands[auth] = card;
            SendCardTo(sender, card);
            _log.Add(msg);
            CardPlayedClientRpc(sender, def.kind, target, new FixedString128Bytes(Trim(msg)));
            _game.Tasks.ServerOnCardPlayed(sender, def.kind, target);
            return true;
        }

        /// <summary>Host: tell everyone that an armed effect actually triggered (e.g. Second Chance).</summary>
        public void ServerAnnounceEffect(ulong user, CardKind kind, string message)
        {
            if (!IsServer) return;
            _log.Add(message);
            CardPlayedClientRpc(user, kind, ulong.MaxValue, new FixedString128Bytes(Trim(message)));
        }

        static string Trim(string s) => s.Length > 120 ? s.Substring(0, 120) : s;

        // ------------------------------------------------------------------ Host -> clients
        [ClientRpc]
        void CardUpdateClientRpc(CardInfo info, ClientRpcParams rpcParams = default)
        {
            LocalCard = info;
            LocalCardChanged?.Invoke(info);
        }

        [ClientRpc]
        void CardPlayedClientRpc(ulong user, CardKind kind, ulong target, FixedString128Bytes message)
        {
            AudioManager.Instance?.Play(AudioManager.Sfx.CardPlayed);
            CardPlayed?.Invoke(user, kind, target, L.Resolve(message.ToString()));
        }

        [ClientRpc]
        void ClearAllClientRpc() => ClearLocal();
    }
}
