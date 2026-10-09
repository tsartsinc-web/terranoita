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

        static ProbeShot Flying(double x0, double y0, double vx, double vy, params double[][] path)
        {
            var s = new ProbeShot { File = Spark, Parent = "", X0 = x0, Y0 = y0, Vx0 = vx, Vy0 = vy };
            s.Path.AddRange(path);
            return s;
        }

        static ProbeRow With(ProbeShot s)
        {
            var r = new ProbeRow { Name = "single:LIGHT_BULLET", ManaUsed = 5 };
            r.Shots.Add(s);
            return r;
        }

        [Fact]
        public void FlightIsComparedFromItsStartAlongItsFirstDirection()
        {
            // Noita: from (20,-6) to the right, a pixel of drop by frame 10
            var noita = With(Flying(20, -6, 600, 0, new double[] { 0, 20, -6, 600, 0 }, new double[] { 5, 70, -6, 600, 0 }, new double[] { 10, 120, -5, 600, 6 }));
            // ours from another start, straight down: the same flight turned 90 degrees (random spread) matches
            var turned = With(Flying(8, 0, 0, 600, new double[] { 0, 8, 0, 0, 600 }, new double[] { 5, 8, 50, 0, 600 }, new double[] { 10, 7, 100, -6, 600 }));
            Assert.True(ProbeCompare.Compare(noita, turned).Matches);
            // ours drew a slower random speed (rocket_tier_2: speed_min 70, speed_max 100): the same flight at 3/4 speed matches
            var slower = With(Flying(8, 0, 450, 0, new double[] { 0, 8, 0, 450, 0 }, new double[] { 5, 45.5, 0, 450, 0 }, new double[] { 10, 83, 0.75, 450, 4.5 }));
            Assert.True(ProbeCompare.Compare(noita, slower).Matches);
            // ours falls 20 px more by frame 10 (more than 4 px + 10% of the 100 px flown)
            var falls = With(Flying(8, 0, 600, 0, new double[] { 0, 8, 0, 600, 0 }, new double[] { 5, 58, 0, 600, 0 }, new double[] { 10, 108, 21, 600, 60 }));
            Assert.Equal("path light_bullet: 20 px off at frame 10", ProbeCompare.Compare(noita, falls).Differences.Single());
            Assert.True(ProbeCompare.Compare(noita, falls).BehavesLike);   // a number: within 10 px + 30% of the flight
            // ours stops where it started: it flies differently (behaviour)
            var stays = With(Flying(8, 0, 600, 0, new double[] { 0, 8, 0, 600, 0 }, new double[] { 5, 8, 0, 0, 0 }, new double[] { 10, 8, 0, 0, 0 }));
            Assert.Equal(new[] { "path light_bullet: 50 px off at frame 5" }, ProbeCompare.Compare(noita, stays).Behaviour);
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
            // PC-30: what the cast does (its payload, its blast) is behaviour; speed and mana are numbers
            Assert.Equal(new[] { "children bouncy_orb from light_bullet: 0 (Noita 1)", "damage $damage_explosion: 0 (Noita 5)" }, v.Behaviour);
        }

        [Fact]
        public void NumbersAloneKeepTheBehaviour()
        {
            var noita = Row("single:LIGHT_BULLET", 5, (Spark, "", 730));
            noita.Hits.Add(new ProbeHit { Damage = 0.12, Message = "$damage_projectile" });
            var ours = Row(noita.Name, 7, (Spark, "", 500));
            ours.Hits.Add(new ProbeHit { Damage = 0.3, Message = "$damage_projectile" });
            var v = ProbeCompare.Compare(noita, ours);
            Assert.False(v.Matches);
            Assert.True(v.BehavesLike);
        }

        [Fact]
        public void NoitasEffectEntitiesAreNotShots()
        {
            var noita = Row("single:BOMB", 25, ("data/entities/projectiles/bomb.xml", "", 120),
                            ("data/entities/particles/muzzle_flashes/muzzle_flash_launcher_large.xml", "", 0), ("data/entities/misc/crack.xml", "", 0));
            var ours = Row("single:BOMB", 25, ("data/entities/projectiles/bomb.xml", "", 110));
            Assert.True(ProbeCompare.Compare(noita, ours).Matches);
        }

        [Fact]
        public void NothingFiredAndMissingRows()
        {
            var noita = Row("single:TENTACLE", 20, ("data/entities/projectiles/deck/tentacle.xml", "", 8));
            Assert.Equal("nothing fired (Noita fired)", ProbeCompare.Compare(noita, Row(noita.Name, 0)).Differences.Single());
            var all = ProbeCompare.CompareAll(new[] { noita, Row("mod:HOMING+LIGHT_BULLET", 5, (Spark, "", 700)) },
                                              new[] { Row("mod:HOMING+LIGHT_BULLET", 5, (Spark, "", 690)) });
            Assert.Equal("not run in Terraria", all[0].Differences.Single());
            Assert.Equal("behaviour: 1 of 2 (single 0 of 1, mod 1 of 1); matching Noita: 1 of 2 (single 0 of 1, mod 1 of 1)", ProbeCompare.Summary(all));
        }
    }
}
