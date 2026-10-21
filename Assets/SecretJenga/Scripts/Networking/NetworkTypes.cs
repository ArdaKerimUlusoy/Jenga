using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>Public per-player state replicated to everyone (no secret data in here).</summary>
    public struct PlayerData : INetworkSerializable, IEquatable<PlayerData>
    {
        public ulong ClientId;
        public FixedString64Bytes AuthId;
        public FixedString32Bytes Name;
        public byte ColorIndex;
        /// <summary>Chair around the table (0..SeatLayout.SeatCount-1).</summary>
        public byte SeatIndex;
        public bool IsReady;
        public bool IsConnected;
        public bool IsHost;
        public int JoinOrder;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref AuthId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ColorIndex);
            serializer.SerializeValue(ref SeatIndex);
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref IsConnected);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref JoinOrder);
        }

        public bool Equals(PlayerData o) =>
            ClientId == o.ClientId && AuthId.Equals(o.AuthId) && Name.Equals(o.Name) && ColorIndex == o.ColorIndex &&
            SeatIndex == o.SeatIndex && IsReady == o.IsReady && IsConnected == o.IsConnected && IsHost == o.IsHost && JoinOrder == o.JoinOrder;

        public override bool Equals(object obj) => obj is PlayerData p && Equals(p);
        public override int GetHashCode() => ClientId.GetHashCode();
        public Color Color => Palette.Player(ColorIndex);
    }

    /// <summary>
    /// One player's secret personal task. Sent privately to its owner (targeted RPC) and to everyone only when the
    /// round ends. The wording lives in the TaskDefinition asset referenced by DefinitionIndex.
    /// </summary>
    public struct TaskInfo : INetworkSerializable
    {
        public TaskKind Kind;
        public byte DefinitionIndex;
        public int Progress;
        public int Target;
        public bool Completed;
        /// <summary>The task can no longer be completed this round (e.g. collapse during your own turn).</summary>
        public bool Failed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref DefinitionIndex);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref Completed);
            serializer.SerializeValue(ref Failed);
        }
    }

    /// <summary>The special card a player holds this round. Private until it is played or the round ends.</summary>
    public struct CardInfo : INetworkSerializable
    {
        public CardKind Kind;
        public byte DefinitionIndex;
        public bool Used;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref DefinitionIndex);
            serializer.SerializeValue(ref Used);
        }
    }

    /// <summary>One row of the end-of-round reveal.</summary>
    public struct PlayerRoundResult : INetworkSerializable
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public byte ColorIndex;
        public TaskInfo Task;
        public CardInfo Card;
        public int SuccessfulTurns;
        public bool CausedCollapse;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ColorIndex);
            serializer.SerializeNetworkSerializable(ref Task);
            serializer.SerializeNetworkSerializable(ref Card);
            serializer.SerializeValue(ref SuccessfulTurns);
            serializer.SerializeValue(ref CausedCollapse);
        }
    }

    /// <summary>JSON payload sent with the NGO connection request.</summary>
    [Serializable]
    public class ConnectionPayload
    {
        public string authId;
        public string playerName;
        public string version;
        public int colorPref = -1;
    }

    [Serializable]
    public class RoomSettings
    {
        public string roomName = "The Last Block";
        public int maxPlayers = GameRules.MaxPlayers;
        public int requiredTurns = GameRules.DefaultRequiredTurns;
        public int startLayers = GameRules.DefaultStartLayers;
        public int turnSeconds = GameRules.DefaultTurnSeconds;
        public bool isPrivate;
        /// <summary>Direct IP hosting (LAN / port forwarding) instead of Unity Relay + Lobby.</summary>
        public bool direct;
        public ushort port = GameSettings.DefaultPort;
    }
}
