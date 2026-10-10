using Terranoita.Physics;
using Xunit;

namespace Terranoita.Tests
{
    public class ConductionTests
    {
        /// <summary>'~' conducts (water), '.' does not; rows top to bottom.</summary>
        sealed class Map : IConductGrid
        {
            public readonly char[][] Rows;
            public Map(params string[] rows) { Rows = System.Array.ConvertAll(rows, r => r.ToCharArray()); }
            public bool Conducts(int x, int y) => y >= 0 && y < Rows.Length && x >= 0 && x < Rows[y].Length && Rows[y][x] == '~';
        }

        static int Charged(Conduction c, Map m)
        {
            int n = 0;
            for (int y = 0; y < m.Rows.Length; y++)
                for (int x = 0; x < m.Rows[y].Length; x++)
                    if (c.Charged(x, y)) { Assert.True(m.Conducts(x, y)); n++; }
            Assert.Equal(c.Count, n);
            return n;
        }

        [Fact]
        public void OnePoolGetsChargedTwoPoolsOnlyTheTouchedOne()
        {
            var m = new Map(
                "..........",
                ".~~~..~~~.",
                ".~~~..~~~.",
                "..........");
            var c = new Conduction(10);
            Assert.Equal(6, c.Emit(m, 2, 1, 0, 1000, 40));
            Assert.Equal(6, Charged(c, m));
            Assert.False(c.Charged(7, 1));
        }

        [Fact]
        public void RadiusReachesWaterFromOutside()
        {
            var m = new Map("~~~....", "~~~....");
            var c = new Conduction(7);
            Assert.Equal(0, c.Emit(m, 5, 0, 1, 1000, 40));   // dry within 1 tile
            Assert.Equal(6, c.Emit(m, 4, 0, 2, 1000, 40));   // the pool is 2 tiles away
        }

        [Fact]
        public void EnergyAndCapsLimitTheSpread()
        {
            var m = new Map("~~~~~~~~~~~~~~~~~~~~", "~~~~~~~~~~~~~~~~~~~~");
            Assert.Equal(5, new Conduction(20).Emit(m, 0, 0, 0, 5, 40));                  // energy 5 (weak electricity 50 -> 50)
            Assert.Equal(10, new Conduction(20, maxSpread: 10).Emit(m, 0, 0, 0, 1000, 40)); // per emission
            var c = new Conduction(20, maxSpread: 400, maxCharged: 7);
            Assert.Equal(7, c.Emit(m, 0, 0, 0, 1000, 40));                                // in all
            Assert.Equal(7, c.Count);
        }

        [Fact]
        public void ChargesGoOutRefreshAndDropWhereTheLiquidLeft()
        {
            var m = new Map("~~~~");
            var c = new Conduction(4);
            c.Emit(m, 0, 0, 0, 1000, 12);
            c.Tick(m, 6);
            Assert.Equal(6, c.FramesLeft(1, 0));
            c.Emit(m, 3, 0, 0, 1000, 12);                    // refreshed
            Assert.Equal(12, c.FramesLeft(1, 0));
            m.Rows[0][3] = '.';                              // dried up
            c.Tick(m, 6);
            Assert.Equal(3, c.Count);
            c.Tick(m, 6);
            Assert.Equal(0, c.Count);
            Assert.Equal(0, c.Emit(m, 0, 0, 0, 0, 12));     // no energy
        }
    }
}
