using NUnit.Framework;
using Eliminated.Sim.Core;

namespace Eliminated.Game.Tests
{
    /// <summary>
    /// EditMode test proving the Unity project can reference and exercise the
    /// pure-C# simulation. Mirrors the headless xUnit smoke test so a regression
    /// in the package wiring is caught inside the Editor too.
    /// </summary>
    public class SimLinkTests
    {
        [Test]
        public void Simulation_constants_are_visible_from_unity()
        {
            Assert.AreEqual(20, Constants.TickHz);
            Assert.AreEqual(1280f, Constants.ArenaW);
            Assert.AreEqual(720f, Constants.ArenaH);
        }
    }
}
