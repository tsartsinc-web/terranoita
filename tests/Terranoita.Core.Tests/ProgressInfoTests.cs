using System.Linq;
using Terranoita.Generated;
using Terranoita.Progress;
using Xunit;

namespace Terranoita.Tests
{
    public class ProgressInfoTests
    {
        [Fact]
        public void CreatureLines()
        {
            var zombie = Defs.Enemy["zombie"];
            var l = ProgressInfo.Lines(ProgressBook.Creatures, "zombie");
            int life = (int)System.Math.Round(zombie.NoitaHp * Defs.Tier[zombie.Tier].HpMult);
            Assert.Contains("Life: " + life, l);
            Assert.Contains(l, s => s.StartsWith("Lives in: "));
            Assert.Contains(l, s => s.StartsWith("Attack: melee") && s.Contains("damage (melee)"));
            Assert.Contains(l, s => s.StartsWith("Attack: lunge"));
            var acid = ProgressInfo.Lines(ProgressBook.Creatures, "acidshooter");
            Assert.Contains(acid, s => s.StartsWith("Life: "));
            Assert.Empty(ProgressInfo.Lines(ProgressBook.Creatures, "no_such_creature"));
            // over 1000 life: hardmode only, wherever it lives
            var worm = ProgressInfo.Lines(ProgressBook.Creatures, "worm_big").Single(s => s.StartsWith("Lives in: "));
            Assert.All(worm.Substring(10).Split(';'), part => Assert.Contains("hardmode", part));
        }

        [Fact]
        public void LiquidLines()
        {
            var acid = ProgressInfo.Lines(ProgressBook.Liquids, "acid");
            Assert.Equal("Liquid", acid[0]);
            Assert.Contains(acid, s => s.StartsWith("Touch: ") && s.EndsWith("damage per second"));
            Assert.Contains("If drunk: poisoned", acid);
            Assert.Contains(acid, s => s.StartsWith("Reacts: ") && s.Contains("corrodible"));
            var water = ProgressInfo.Lines(ProgressBook.Liquids, "water");
            Assert.Contains("On touch: wet", water);
            var oil = Liquids.All.FirstOrDefault(x => x.Id == "oil");
            if (oil != null)
                Assert.Contains("Burns", ProgressInfo.Lines(ProgressBook.Liquids, "oil"));
        }

        [Fact]
        public void SpellLines()
        {
            var bolt = ProgressInfo.Lines(ProgressBook.Spells, "LIGHT_BULLET");
            Assert.Equal(new[] { "Type: projectile", "Mana: 5", "Damage: 3" }, bolt.Take(3));
            var bomb = ProgressInfo.Lines(ProgressBook.Spells, "BOMB");
            Assert.Contains("Uses: 3", bomb);
            Assert.Contains(bomb, s => s.StartsWith("Explosion: "));
            Assert.Empty(ProgressInfo.Lines(ProgressBook.Wands, "anything"));
        }
    }
}
