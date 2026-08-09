using System;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Infra
{
    /// <summary>
    /// Pure stand-in for UnityEngine.Vector3, for domain code that needs a
    /// position/direction (customer world positions, save data) without
    /// depending on UnityEngine. `Pho.Core` converts at the boundary.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = MathP.Clamp01(t);
            return new Vec3(
                MathP.Lerp(a.X, b.X, t),
                MathP.Lerp(a.Y, b.Y, t),
                MathP.Lerp(a.Z, b.Z, t));
        }

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
