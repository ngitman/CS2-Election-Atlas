using System;

namespace ElectionAtlas.Model
{
    /// <summary>A point in political space. Both axes run from -1 to +1 and are clamped.</summary>
    public readonly struct Position : IEquatable<Position>
    {
        public const float Min = -1f;
        public const float Max = 1f;

        /// <summary>The largest possible distance between two positions (corner to corner).</summary>
        public static readonly float MaxDistance = (float)Math.Sqrt(8.0);

        /// <summary>-1 = left (redistribution), +1 = right (low tax).</summary>
        public readonly float Economic;

        /// <summary>-1 = progressive, +1 = traditional.</summary>
        public readonly float Social;

        public Position(float economic, float social)
        {
            Economic = Clamp(economic);
            Social = Clamp(social);
        }

        public float DistanceTo(Position other)
        {
            float de = Economic - other.Economic;
            float ds = Social - other.Social;
            return (float)Math.Sqrt(de * de + ds * ds);
        }

        public bool Equals(Position other) => Economic.Equals(other.Economic) && Social.Equals(other.Social);

        public override bool Equals(object obj) => obj is Position other && Equals(other);

        public override int GetHashCode() => (Economic.GetHashCode() * 397) ^ Social.GetHashCode();

        public override string ToString() => $"({Economic:0.00}, {Social:0.00})";

        private static float Clamp(float v) => v < Min ? Min : v > Max ? Max : v;
    }
}
