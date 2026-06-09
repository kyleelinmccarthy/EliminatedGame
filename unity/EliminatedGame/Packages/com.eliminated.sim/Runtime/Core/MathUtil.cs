using System;

namespace Eliminated.Sim.Core
{
    /// <summary>
    /// Small scalar math helpers used across the sim. Mirrors the helpers in the
    /// reference game (clamp/lerp). Pure C#, no UnityEngine.Mathf.
    /// </summary>
    public static class MathUtil
    {
        public static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);

        public static int Clamp(int v, int lo, int hi)
            => v < lo ? lo : (v > hi ? hi : v);

        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Shortest signed angular difference (radians) from a to b.</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % (2f * (float)Math.PI);
            if (d < -(float)Math.PI) d += 2f * (float)Math.PI;
            if (d > (float)Math.PI) d -= 2f * (float)Math.PI;
            return d;
        }

        public static bool Approximately(float a, float b, float eps = 1e-5f)
            => Math.Abs(a - b) <= eps;
    }
}
