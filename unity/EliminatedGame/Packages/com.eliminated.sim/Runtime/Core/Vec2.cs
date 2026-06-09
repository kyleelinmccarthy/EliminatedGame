using System;

namespace Eliminated.Sim.Core
{
    /// <summary>
    /// A tiny 2D float vector. The simulation defines its own vector type so it
    /// never depends on <c>UnityEngine.Vector2</c> — that is what lets the same
    /// source compile under the .NET SDK for headless tests AND inside Unity.
    /// Immutable value type (KISS, no aliasing surprises in the deterministic sim).
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float SqrLength => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        /// <summary>Unit vector in the same direction; <see cref="Zero"/> stays
        /// zero (no NaN for a zero-length vector).</summary>
        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        /// <summary>Returns this vector shortened to <paramref name="max"/> if it
        /// is longer; otherwise unchanged.</summary>
        public Vec2 ClampMagnitude(float max)
        {
            float sq = SqrLength;
            if (sq <= max * max) return this;
            float len = (float)Math.Sqrt(sq);
            float s = max / len;
            return new Vec2(X * s, Y * s);
        }

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
