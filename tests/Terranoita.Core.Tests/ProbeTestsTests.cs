using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class ProbeTestsTests
    {
        [Fact]
        public void FitRowsForTheHomingFormula()
        {
            // homing measured on a shot with no friction and no gravity (disc_bullet_big.xml), so its path shows the homing
            // alone; only the spells the player's Noita has
            var actions = new[] { ("HOMING", 2), ("HOMING_SHOOTER", 2), ("DISC_BULLET_BIG", 0) };
            var fits = ProbeTests.Build(actions).Where(t => t.Name.StartsWith("fit:")).ToList();
            Assert.Equal(new[] { "fit:HOMING+DISC_BULLET_BIG", "fit:HOMING_SHOOTER+DISC_BULLET_BIG" }, fits.Select(t => t.Name));
            Assert.Equal(new[] { "HOMING", "DISC_BULLET_BIG" }, fits[0].Deck);
        }

        [Fact]
        public void RandomWandsKeepTheirStats()
        {
            // PC-30 builds: a wand Noita's wand_level_0N.lua made, cast as it is (its spells per cast, spread, speed,
            // always-cast spells); a wand the script left empty casts nothing and is left out
            var made = new MadeWand { SpellsPerCast = 2, Spread = 4.5f, SpeedMultiplier = 1.2f };
            made.Spells.AddRange(new[] { "LIGHT_BULLET", "BOUNCY_ORB" });
            made.AlwaysCast.Add("HOMING");
            var tests = ProbeTests.RandomWands(new[] { (3, 7, made), (1, 0, new MadeWand()) });
            var t = tests.Single();
            Assert.Equal("wand:3-07", t.Name);
            Assert.Equal(new[] { "LIGHT_BULLET", "BOUNCY_ORB" }, t.Deck);
            Assert.Equal((2, 4.5f, 1.2f), (t.SpellsPerCast, t.Spread, t.SpeedMultiplier));
            Assert.Equal(new[] { "HOMING" }, t.AlwaysCast);
            Assert.Contains("  { name = \"wand:3-07\", deck = { \"LIGHT_BULLET\", \"BOUNCY_ORB\" }, spells_per_cast = 2, spread = 4.5, " +
                            "speed_multiplier = 1.2, always_cast = { \"HOMING\" } },\n", ProbeTests.ToLua(tests));
            // the other tests keep their line (the probe wand's own stats)
            Assert.Contains("  { name = \"single:X\", deck = { \"X\" } },\n", ProbeTests.ToLua(new[] { new ProbeTest { Name = "single:X", Deck = new[] { "X" } } }));
        }
    }
}
