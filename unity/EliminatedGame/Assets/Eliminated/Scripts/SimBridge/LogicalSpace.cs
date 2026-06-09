using UnityEngine;
using Eliminated.Sim.Core;

namespace Eliminated.Game.SimBridge
{
    /// <summary>
    /// Maps the simulation's logical arena space (0..1280 × 0..720, y-down) to
    /// Unity world space on the XZ plane (centered at the origin, y-up), for the
    /// 2.5D top-down view. One place owns the conversion so the rest of the
    /// client never hard-codes the scale.
    /// </summary>
    public static class LogicalSpace
    {
        /// <summary>World units per logical unit.</summary>
        public const float Scale = 0.02f;

        public static readonly float CenterX = Constants.ArenaW * 0.5f; // 640
        public static readonly float CenterY = Constants.ArenaH * 0.5f; // 360

        /// <summary>Arena half-extent in world units (z), for camera framing.</summary>
        public static float WorldHalfHeight => Constants.ArenaH * 0.5f * Scale; // 7.2
        public static float WorldHalfWidth => Constants.ArenaW * 0.5f * Scale;  // 12.8

        public static Vector3 ToWorld(Vec2 p) => ToWorld(p.X, p.Y);

        public static Vector3 ToWorld(float x, float y)
            => new Vector3((x - CenterX) * Scale, 0f, -(y - CenterY) * Scale);

        public static Vec2 ToLogical(Vector3 w)
            => new Vec2(w.x / Scale + CenterX, -(w.z / Scale) + CenterY);

        public static float WorldRadius(float logicalRadius) => logicalRadius * Scale;

        /// <summary>Facing angle (logical radians) → world Y rotation for a model
        /// laid out on the XZ plane.</summary>
        public static Quaternion FacingToRotation(float logicalAngle)
        {
            // logical angle: cos→+x, sin→+y(down). World forward maps x→x, y→-z.
            float wx = Mathf.Cos(logicalAngle);
            float wz = -Mathf.Sin(logicalAngle);
            float deg = Mathf.Atan2(wx, wz) * Mathf.Rad2Deg; // heading around +Y
            return Quaternion.Euler(0f, deg, 0f);
        }
    }
}
