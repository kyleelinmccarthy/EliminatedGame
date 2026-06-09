using Eliminated.Sim.Core;

namespace Eliminated.Sim.Model
{
    /// <summary>Circle-circle collision helpers. The whole sim uses circle
    /// collision (no physics engine), matching the reference game.</summary>
    public static class Collision
    {
        /// <summary>True if two circles touch or overlap.</summary>
        public static bool Circle(Vec2 pa, float ra, Vec2 pb, float rb)
        {
            float rr = ra + rb;
            return Vec2.SqrDistance(pa, pb) <= rr * rr;
        }

        /// <summary>True if two actors' (scaled) bodies overlap.</summary>
        public static bool Overlap(Actor a, Actor b)
            => Circle(a.Pos, a.Radius, b.Pos, b.Radius);

        /// <summary>True if a point is within <paramref name="r"/> of a center.</summary>
        public static bool PointInCircle(Vec2 p, Vec2 center, float r)
            => Vec2.SqrDistance(p, center) <= r * r;
    }
}
