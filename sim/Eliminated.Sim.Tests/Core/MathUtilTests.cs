using Eliminated.Sim.Core;
using Xunit;

namespace Eliminated.Sim.Tests.Core
{
    public class MathUtilTests
    {
        [Fact]
        public void Clamp_bounds_value()
        {
            Assert.Equal(0f, MathUtil.Clamp(-3f, 0f, 10f));
            Assert.Equal(10f, MathUtil.Clamp(99f, 0f, 10f));
            Assert.Equal(5f, MathUtil.Clamp(5f, 0f, 10f));
        }

        [Fact]
        public void Clamp01()
        {
            Assert.Equal(0f, MathUtil.Clamp01(-1f));
            Assert.Equal(1f, MathUtil.Clamp01(2f));
            Assert.Equal(0.5f, MathUtil.Clamp01(0.5f));
        }

        [Fact]
        public void Lerp_interpolates()
        {
            Assert.Equal(0f, MathUtil.Lerp(0f, 10f, 0f), 4);
            Assert.Equal(10f, MathUtil.Lerp(0f, 10f, 1f), 4);
            Assert.Equal(2.5f, MathUtil.Lerp(0f, 10f, 0.25f), 4);
        }
    }
}
