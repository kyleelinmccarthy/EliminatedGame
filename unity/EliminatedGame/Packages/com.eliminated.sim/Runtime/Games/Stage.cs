using System;
using Eliminated.Sim.Core;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Placement helpers for the "non-arena" games — TugOfWar, RpsMinusOne,
    /// JumpRope, GlassBridge, PresentSwap, SimonSays and ChutesAndLadders. These
    /// games have no free movement, so historically they never wrote
    /// <see cref="Model.Actor.Pos"/> at all: the shared top-down view then drew
    /// every player at logical (0,0), i.e. piled in the upper-left corner. Each
    /// game now calls these helpers from <c>BuildSnapshot</c> to arrange its
    /// actors as a readable diagram of the current state.
    ///
    /// All coordinates are logical arena space: 0..<see cref="Constants.ArenaW"/>
    /// in x and 0..<see cref="Constants.ArenaH"/> in y (y points down).
    /// </summary>
    internal static class Stage
    {
        /// <summary>Keep players this far off the arena walls.</summary>
        public const float Margin = 110f;

        public static float CenterX => Constants.ArenaW * 0.5f;
        public static float CenterY => Constants.ArenaH * 0.5f;
        public static float MinX => Margin;
        public static float MaxX => Constants.ArenaW - Margin;
        public static float MinY => Margin;
        public static float MaxY => Constants.ArenaH - Margin;

        /// <summary>Clamp a point inside the playable margin so a player never
        /// renders inside or beyond a wall.</summary>
        public static Vec2 Clamp(float x, float y) => new Vec2(
            MathUtil.Clamp(x, Margin, Constants.ArenaW - Margin),
            MathUtil.Clamp(y, Margin, Constants.ArenaH - Margin));

        /// <summary>The i-th of <paramref name="count"/> evenly spaced values
        /// between <paramref name="a"/> and <paramref name="b"/>. A single item
        /// lands in the middle.</summary>
        public static float Spread(int index, int count, float a, float b)
        {
            if (count <= 1) return (a + b) * 0.5f;
            return a + (b - a) * (index / (float)(count - 1));
        }

        /// <summary>Place item <paramref name="index"/> of <paramref name="count"/>
        /// on a left-to-right, top-to-bottom grid filling the rect
        /// (<paramref name="x0"/>,<paramref name="y0"/>)..(<paramref name="x1"/>,<paramref name="y1"/>).</summary>
        public static Vec2 Grid(int index, int count, int cols, float x0, float y0, float x1, float y1)
        {
            cols = Math.Max(1, cols);
            int rows = Math.Max(1, (count + cols - 1) / cols);
            int r = index / cols;
            int c = index % cols;
            int inThisRow = (r == rows - 1) ? count - r * cols : cols; // short final row stays centered
            // CELL-CENTER placement ((i+0.5)/n), not edge-to-edge: keeps the first/last column
            // off the walls (edge interpolation parked corner players on x0/x1, hiding them).
            float fx = (c + 0.5f) / Math.Max(1, inThisRow);
            float fy = (r + 0.5f) / rows;
            return new Vec2(x0 + (x1 - x0) * fx, y0 + (y1 - y0) * fy);
        }
    }
}
