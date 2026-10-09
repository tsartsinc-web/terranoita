using System;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class LaserBeamTests
    {
        [Fact]
        public void BeamRunsToMaxLengthOrTheGround()
        {
            // a wall from x = 50 (Noita px); the beam from (10, 0)
            Func<float, float, bool> wall = (x, y) => x >= 50;
            var free = LaserBeam.End(10, 0, (float)Math.PI, 96, wall);   // to the left: nothing in the way
            Assert.Equal(-86, free.x, 0);
            Assert.Equal(0, free.y, 0);
            var stopped = LaserBeam.End(10, 0, 0, 96, wall);             // to the right: stops before the wall
            Assert.Equal(49, stopped.x, 0);
            var inside = LaserBeam.End(60, 0, 0, 96, wall);              // starting in the ground: no beam
            Assert.Equal((60f, 0f), inside);
        }
    }
}
