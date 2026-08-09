using System;
using Pho.Domain.Multiplayer;
using Unity.Netcode;

namespace Pho.Net.Player
{
    /// <summary>
    /// A player's display name in a form a <see cref="NetworkVariable{T}"/>
    /// can carry: fixed size, unmanaged, no allocation on the wire.
    ///
    /// The encoding itself lives in the pure
    /// <see cref="AsciiNamePacker"/> (tested in the fast loop); this struct
    /// is only the Netcode-facing shell -- the same split as
    /// CarryRegistry/CarryAuthority. See AsciiNamePacker's doc comment for
    /// why this is four ulongs rather than Unity's FixedString32Bytes.
    /// </summary>
    public struct NetPlayerName : INetworkSerializable, IEquatable<NetPlayerName>
    {
        public ulong A, B, C, D;
        public int Length;

        public static NetPlayerName From(string value)
        {
            var name = default(NetPlayerName);
            AsciiNamePacker.Pack(value, out name.A, out name.B, out name.C, out name.D, out name.Length);
            return name;
        }

        public override string ToString() => AsciiNamePacker.Unpack(A, B, C, D, Length);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref A);
            serializer.SerializeValue(ref B);
            serializer.SerializeValue(ref C);
            serializer.SerializeValue(ref D);
            serializer.SerializeValue(ref Length);
        }

        public bool Equals(NetPlayerName other) =>
            A == other.A && B == other.B && C == other.C && D == other.D && Length == other.Length;

        public override bool Equals(object obj) => obj is NetPlayerName other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + A.GetHashCode();
                hash = hash * 31 + B.GetHashCode();
                hash = hash * 31 + C.GetHashCode();
                hash = hash * 31 + D.GetHashCode();
                hash = hash * 31 + Length;
                return hash;
            }
        }
    }
}
