using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class ProbeCompareTests
    {
        static ProbeRow Row(string name, double mana, params (string file, string parent, double vx)[] shots)
        {
            var r = new ProbeRow { Name = name, ManaUsed = mana };
            foreach (var s in shots)
                r.Shots.Add(new ProbeShot { File = s.file, Parent = s.parent, Vx0 = s.vx, Vy0 = 0 });
            return r;
        }

        const string Spark = "data/entities/projectiles/deck/light_bullet.xml", Orb = "data/entities/projectiles/deck/bouncy_orb.xml";

        [Fact]
        public void SameCastMatches()
        {
            var noita = Row("combo:ADD_TRIGGER+LIGHT_BULLET+BOUNCY_ORB", 15, (Spark, "", 730), (Orb, Spark, 200));
            noita.Hits.Add(new ProbeHit { Damage = 0.12, Message = "$damage_projectile" });
            var ours = Row(noita.Name, 15, (Spark, "data/entities/player.xml", 700), (Orb, Spark, 220));
            ours.Hits.Add(new ProbeHit { Damage = 0.13, Message = "$damage_projectile" });
            Assert.True(ProbeCompare.Compare(noita, ours).Matches);
        }

        [Fact]
        public void DifferencesAreNamed()
        {
            var noita = Row("combo:ADD_TRIGGER+LIGHT_BULLET+BOUNCY_ORB", 15, (Spark, "", 730), (Orb, Spark, 200));
            noita.Hits.Add(new ProbeHit { Damage = 5, Message = "$damage_explosion" });
            var ours = Row(noita.Name, 30, (Spark, "", 300));
            var v = ProbeCompare.Compare(noita, ours);
            Assert.Equal(new[]
            {
                "children bouncy_orb from light_bullet: 0 (Noita 1)",
                "speed light_bullet: 300 (Noita 730)",
                "damage $damage_explosion: 0 (Noita 5)",
                "mana 30 (Noita 15)",
            }, v.Differences);
        }

        [Fact]
        public void NothingFiredAndMissingRows()
        {
            var noita = Row("single:TENTACLE", 20, ("data/entities/projectiles/deck/tentacle.xml", "", 8));
            Assert.Equal("nothing fired (Noita fired)", ProbeCompare.Compare(noita, Row(noita.Name, 0)).Differences.Single());
            var all = ProbeCompare.CompareAll(new[] { noita, Row("mod:HOMING+LIGHT_BULLET", 5, (Spark, "", 700)) },
                                              new[] { Row("mod:HOMING+LIGHT_BULLET", 5, (Spark, "", 690)) });
            Assert.Equal("not run in Terraria", all[0].Differences.Single());
            Assert.Equal("matching Noita: 1 of 2 (single 0 of 1, mod 1 of 1)", ProbeCompare.Summary(all));
        }
    }
}
