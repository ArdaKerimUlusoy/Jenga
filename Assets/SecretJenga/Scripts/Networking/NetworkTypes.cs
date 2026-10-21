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
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref IsConnected);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref JoinOrder);
        }

        public bool Equals(PlayerData o) =>
            ClientId == o.ClientId && AuthId.Equals(o.AuthId) && Name.Equals(o.Name) && ColorIndex == o.ColorIndex &&
            IsReady == o.IsReady && IsConnected == o.IsConnected && IsHost == o.IsHost && JoinOrder == o.JoinOrder;

        public override bool Equals(object obj) => obj is PlayerData p && Equals(p);
        public override int GetHashCode() => ClientId.GetHashCode();
        public Color Color => Palette.Player(ColorIndex);
    }

    /// <summary>Mission state sent privately to one client (and to everyone only at the end of the match).</summary>
    public struct MissionInfo : INetworkSerializable
    {
        public MissionType Type;
        public BlockColor TargetColor;
        public int Progress;
        public int Target;
        public bool Completed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref TargetColor);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref Completed);
        }
    }

    /// <summary>One row of the end-of-match reveal.</summary>
    public struct MissionResult : INetworkSerializable
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public byte ColorIndex;
        public MissionInfo Mission;
        public int SuccessfulMoves;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ColorIndex);
            serializer.SerializeNetworkSerializable(ref Mission);
            serializer.SerializeValue(ref SuccessfulMoves);
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
        public string roomName = "Secret Jenga Room";
        public int maxPlayers = 4;
        public int targetHeight = GameRules.DefaultTargetHeight;
        public int startLayers = GameRules.DefaultStartLayers;
        public int turnSeconds = GameRules.DefaultTurnSeconds;
        public bool isPrivate;
    }
}
