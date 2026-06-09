using NUnit.Framework;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Game.SimBridge;

namespace Eliminated.Game.Tests
{
    /// <summary>
    /// EditMode tests for the sim↔world coordinate mapping used by the 2.5D view.
    /// Runs in the Unity Test Runner (uses UnityEngine math).
    /// </summary>
    public class LogicalSpaceTests
    {
        [Test]
        public void World_round_trips_back_to_logical()
        {
            var p = new Vec2(345f, 210f);
            var world = LogicalSpace.ToWorld(p);
            var back = LogicalSpace.ToLogical(world);
            Assert.AreEqual(p.X, back.X, 0.001f);
            Assert.AreEqual(p.Y, back.Y, 0.001f);
        }

        [Test]
        public void Arena_center_maps_to_world_origin()
        {
            var world = LogicalSpace.ToWorld(new Vec2(Constants.ArenaW * 0.5f, Constants.ArenaH * 0.5f));
            Assert.Less(world.magnitude, 0.0001f);
        }

        [Test]
        public void Logical_up_is_world_plus_z()
        {
            // Decreasing logical Y (moving "up") should increase world Z.
            var lower = LogicalSpace.ToWorld(new Vec2(640f, 400f));
            var upper = LogicalSpace.ToWorld(new Vec2(640f, 300f)); // smaller y = up
            Assert.Greater(upper.z, lower.z);
        }
    }
}
