using Eliminated.Sim.Core;
using Xunit;

namespace Eliminated.Sim.Tests.Core
{
    public class Vec2Tests
    {
        [Fact]
        public void Add_sub_scale_behave()
        {
            var a = new Vec2(1, 2);
            var b = new Vec2(3, -1);
            Assert.Equal(new Vec2(4, 1), a + b);
            Assert.Equal(new Vec2(-2, 3), a - b);
            Assert.Equal(new Vec2(2, 4), a * 2f);
        }

        [Fact]
        public void Length_and_distance()
        {
            var a = new Vec2(3, 4);
            Assert.Equal(25f, a.SqrLength, 4);
            Assert.Equal(5f, a.Length, 4);
            Assert.Equal(5f, Vec2.Distance(new Vec2(0, 0), new Vec2(3, 4)), 4);
            Assert.Equal(25f, Vec2.SqrDistance(new Vec2(0, 0), new Vec2(3, 4)), 4);
        }

        [Fact]
        public void Normalized_unit_or_zero()
        {
            var n = new Vec2(0, 5).Normalized;
            Assert.Equal(0f, n.X, 4);
            Assert.Equal(1f, n.Y, 4);
            Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized); // zero stays zero (no NaN)
        }

        [Fact]
        public void ClampMagnitude_only_shrinks()
        {
            var big = new Vec2(6, 8); // length 10
            var c = big.ClampMagnitude(5f);
            Assert.Equal(5f, c.Length, 3);
            var small = new Vec2(0.3f, 0.4f); // length 0.5
            Assert.Equal(small, small.ClampMagnitude(5f)); // unchanged
        }
    }
}
