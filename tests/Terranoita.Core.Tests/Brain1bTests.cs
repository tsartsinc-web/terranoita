using System;
using System.Collections.Generic;
using System.Linq;
using Terranoita.Ai;
using Terranoita.Generated;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>
    /// Stage 1b movers and attack kinds. Rows come from the sheets; movement numbers that data.wak has not given yet
    /// (lukki, worms, ghosts) are set in the test, so these check the behaviour, not the numbers.
    /// </summary>
    public class Brain1bTests
    {
        /// <summary>A body with no collisions of its own (worms, ghosts, climbers: the terrain is the brain's business).</summary>
        sealed class FreeBody : IBody
        {
            public V2 Pos;
            public V2 Velocity { get; set; }
            public int Width { get; set; } = 30;
            public int Height { get; set; } = 30;
            public bool OnGround { get; set; }
            public bool HitWall => false;
            public V2 Center => Pos;
            public void Step() => Pos += Velocity;
        }

        /// <summary>A box on a floor at y = 0 (y grows downwards).</summary>
        sealed class FloorBody : IBody
        {
            public V2 Pos;
            public V2 Velocity { get; set; }
            public int Width { get; set; } = 20;
            public int Height { get; set; } = 20;
            public bool OnGround { get; private set; }
            public bool HitWall => false;
            public V2 Center => Pos;
            public void Step()
            {
                var next = Pos + Velocity;
                float floor = -Height / 2f;
                OnGround = next.Y >= floor;
                if (OnGround)
                {
                    next.Y = floor;
                    if (Velocity.Y > 0)
                        Velocity = new V2(Velocity.X, 0);
                }
                Pos = next;
            }
        }

        sealed class World : ITerrain
        {
            public Func<V2, bool> IsSolid = _ => false, IsLiquid = _ => false;
            public bool Solid(V2 p) => IsSolid(p);
            public bool Liquid(V2 p) => IsLiquid(p);
        }

        sealed class Sink : IAttackSink, ISpecialAttackSink
        {
            public readonly List<string> Melee = new List<string>(), Special = new List<string>(), Started = new List<string>();
            public readonly List<(string id, V2 vel)> Shots = new List<(string, V2)>();
            public int Jumps;
            void IAttackSink.Melee(AttackDef a) => Melee.Add(a.Id);
            void IAttackSink.DashHit(AttackDef a) => Melee.Add(a.Id);
            void IAttackSink.Shoot(AttackDef a, ProjectileDef p, V2 from, V2 vel) => Shots.Add((a.Id, vel));
            void IAttackSink.Started(AttackDef a) => Started.Add(a.Id);
            void IAttackSink.Jumped() => Jumps++;
            void ISpecialAttackSink.Special(AttackDef a) => Special.Add(a.Id);
        }

        static Target At(float x, float y) => new Target { Has = true, Center = new V2(x, y), Width = 20, Height = 42, Visible = true };

        /// <summary>The sheet row with movement numbers set for the test.</summary>
        static EnemyDef Row(string id, float run = 0, float fly = 0, float gravity = 0.5f, float accel = 0.3f, float sight = 30)
        {
            var e = Defs.Enemy[id];
            return new EnemyDef
            {
                Id = e.Id, Ai = e.Ai, Attacks = e.Attacks, Stage = e.Stage, SpawnRule = e.SpawnRule,
                Walks = run > 0, Flies = fly > 0, RunSpeed = run, FlySpeed = fly, FlyUpSpeed = fly, Gravity = gravity,
                Accel = accel, JumpSpeed = 4, SightTiles = sight, RoamSpeed = run / 2, TurnRate = 0.06f, RoamTurnRate = 0.02f,
            };
        }

        static readonly string[] Moves = { "ground", "ground_levitate", "hover", "phase", "fly", "hop", "climb", "burrow", "burrow_liquid", "swim", "static" };

        [Fact]
        public void Every1bEnemyHasAnArchetypeTheBrainKnows()
        {
            foreach (var e in Enemies.All.Where(e => e.Stage == "1b"))
            {
                var b = new Brain(e);
                Assert.True(b.Archetype != null, e.Id + " has no archetype");
                Assert.Contains(b.Archetype.Move, Moves);
            }
        }

        [Fact]
        public void LukkiClimbsTheWallToReachThePlayer()
        {
            // floor below y = 0, wall right of x = 100; the player clings high up on the wall
            var world = new World { IsSolid = p => p.Y >= 0 || p.X >= 100 };
            var brain = new Brain(Row("lukki", run: 1.5f));
            var body = new FreeBody { Pos = new V2(40, -16) };
            var sink = new Sink();
            var rng = new Random(2);
            float highest = 0;
            for (int i = 0; i < 600; i++)
            {
                brain.Update(body, At(90, -200), sink, rng, world);
                body.Step();
                highest = Math.Min(highest, body.Pos.Y);
                Assert.True(body.Pos.X > -30, "wandered off the surfaces: " + body.Pos);
            }
            Assert.True(highest < -150, "climbed the wall: highest " + highest);
        }

        [Fact]
        public void LukkiLetsGoInTheOpenAndFalls()
        {
            var world = new World { IsSolid = p => p.Y >= 0 };
            var brain = new Brain(Row("lukki", run: 1.5f));
            var body = new FreeBody { Pos = new V2(0, -300) };
            var rng = new Random(2);
            for (int i = 0; i < 20; i++)
            {
                brain.Update(body, At(0, -300), new Sink(), rng, world);
                body.Step();
            }
            Assert.True(body.Velocity.Y > 5, "falling: " + body.Velocity);
        }

        [Fact]
        public void WormHuntsThroughTheGroundAndLeapsOutAtThePlayer()
        {
            var world = new World { IsSolid = p => p.Y >= 0 };
            var brain = new Brain(Row("worm", run: 3f, gravity: 0.2f));
            Assert.True(brain.PassesTiles);
            var body = new FreeBody { Pos = new V2(-300, 120) };
            var rng = new Random(4);
            bool leftGround = false, cameBack = false;
            for (int i = 0; i < 900; i++)
            {
                brain.Update(body, At(0, -30), new Sink(), rng, world);
                body.Step();
                if (body.Pos.Y < -10) leftGround = true;
                else if (leftGround && body.Pos.Y > 20) cameBack = true;
            }
            Assert.True(leftGround, "never came out of the ground");
            Assert.True(cameBack, "never fell back in");
            Assert.Equal(256, brain.Trail.Count);
            Assert.Equal(body.Pos.X - body.Velocity.X, brain.Trail[0].X, 3);
        }

        [Fact]
        public void WormsFromTheSheetHaveTheirNoitaNumbers()
        {
            foreach (var id in new[] { "worm", "worm_big", "worm_tiny", "eel" })
            {
                var e = Defs.Enemy[id];
                Assert.True(e.RunSpeed > e.RoamSpeed && e.RoamSpeed > 0, id + " hunt faster than roam");
                Assert.True(e.TurnRate > 0 && e.RoamTurnRate > 0, id + " turn rates");
                Assert.True(e.Gravity > 0 && e.Accel > 0, id + " gravity/accel");
            }
            Assert.Equal("burrow_liquid", new Brain(Defs.Enemy["eel"]).Archetype.Move);
        }

        [Fact]
        public void WormSpeedsUpStepByStepAndTurnsNoFasterThanItsRate()
        {
            var row = Row("worm", run: 6f, gravity: 0.2f, accel: 0.1f);
            var brain = new Brain(row);
            var body = new FreeBody { Pos = new V2(0, 200) };
            var rng = new Random(1);
            float lastSpeed = 0;
            V2 lastDir = new V2(1, 0);
            for (int i = 0; i < 120; i++)
            {
                brain.Update(body, At(400, 200), new Sink(), rng);   // no terrain: always in its element
                body.Step();
                float sp = body.Velocity.Length;
                Assert.True(sp - lastSpeed <= row.Accel + 1e-3f, "speed step " + (sp - lastSpeed));
                if (sp > 0.01f && lastSpeed > 0.01f)
                {
                    var d = body.Velocity.Normalized;
                    double ang = Math.Acos(Math.Max(-1, Math.Min(1, d.X * lastDir.X + d.Y * lastDir.Y)));
                    Assert.True(ang <= row.TurnRate + 1e-3, "turned " + ang);
                    lastDir = d;
                }
                else if (sp > 0.01f)
                    lastDir = body.Velocity.Normalized;
                lastSpeed = sp;
            }
            Assert.InRange(lastSpeed, row.RunSpeed - 0.01f, row.RunSpeed + 0.01f);
        }

        [Fact]
        public void EelFallsOutOfTheWaterAndCannotDig()
        {
            var world = new World { IsLiquid = p => p.Y > 0 && p.Y < 100, IsSolid = p => p.Y >= 100 };
            var brain = new Brain(Row("eel", run: 3f, gravity: 0.2f, accel: 0.1f));
            Assert.False(brain.PassesTiles);
            var body = new FreeBody { Pos = new V2(0, -40) };
            for (int i = 0; i < 30; i++)
            {
                brain.Update(body, At(0, 60), new Sink(), new Random(1), world);
                body.Step();
            }
            Assert.True(body.Velocity.Y > 0, "out of the water it falls: " + body.Velocity);
        }

        [Fact]
        public void LevitatingLukkiCrossesTheOpenStraightToThePlayer()
        {
            var world = new World { IsSolid = p => p.Y >= 0 };
            var brain = new Brain(Row("lukki", run: 2f, gravity: 0f));
            var body = new FreeBody { Pos = new V2(0, -300) };
            var rng = new Random(2);
            float start = (body.Pos - new V2(200, -300)).Length;
            for (int i = 0; i < 200; i++)
            {
                brain.Update(body, At(200, -300), new Sink(), rng, world);
                body.Step();
                Assert.True(body.Velocity.Y <= 0.5f, "does not fall: " + body.Velocity);
            }
            Assert.True((body.Pos - new V2(200, -300)).Length < start / 2, "got closer: " + body.Pos);
        }

        [Fact]
        public void FishStaysInTheWaterAndFlopsOnLand()
        {
            var pond = new World { IsLiquid = p => Math.Abs(p.X) < 200 && p.Y > -100 && p.Y < 0 };
            var brain = new Brain(Row("fish", run: 1.2f));
            var body = new FreeBody { Pos = new V2(0, -50), Width = 15, Height = 15 };
            var rng = new Random(6);
            float moved = 0;
            var start = body.Pos;
            for (int i = 0; i < 900; i++)
            {
                brain.Update(body, default(Target), new Sink(), rng, pond);
                body.Step();
                Assert.True(pond.Liquid(body.Pos), "left the water at frame " + i + ": " + body.Pos);
                moved = Math.Max(moved, (body.Pos - start).Length);
            }
            Assert.True(moved > 40, "swam around: " + moved);

            var land = new FloorBody { Pos = new V2(500, -10) };
            var fish = new Brain(Row("fish", run: 1.2f));
            var sink = new Sink();
            bool airborne = false;
            for (int i = 0; i < 200; i++)
            {
                fish.Update(land, default(Target), sink, rng, pond);
                land.Step();
                airborne |= !land.OnGround;
            }
            Assert.True(airborne && sink.Jumps > 0, "flopped");
        }

        [Fact]
        public void HarmlessAnimalsRunFromThePlayer()
        {
            var brain = new Brain(Defs.Enemy["duck"]);
            Assert.True(brain.Archetype.Flees);
            var body = new FloorBody { Pos = new V2(0, -10) };
            var rng = new Random(1);
            for (int i = 0; i < 300; i++)
            {
                brain.Update(body, At(60, -21), new Sink(), rng);
                body.Step();
            }
            Assert.True(body.Pos.X < -40, "ran away: " + body.Pos);
            Assert.Equal(-1, brain.Direction);
        }

        [Fact]
        public void MimicLiesStillUntilThePlayerComesCloseOrHurtsIt()
        {
            var brain = new Brain(Row("mimic_potion", run: 1f));
            var body = new FloorBody { Pos = new V2(0, -10) };
            var sink = new Sink();
            var rng = new Random(1);
            for (int i = 0; i < 300; i++)
            {
                brain.Update(body, At(200, -21), sink, rng);
                body.Step();
            }
            Assert.True(brain.Dormant);
            Assert.Equal(0, body.Pos.X, 3);
            Assert.Empty(sink.Started);
            brain.Update(body, At(30, -21), sink, rng);
            Assert.False(brain.Dormant, "woke when the player came close");

            var other = new Brain(Row("mimic_potion", run: 1f));
            other.Hurt(body, At(300, -21), sink, rng);
            Assert.False(other.Dormant, "woke when hurt");
        }

        [Fact]
        public void GhostCursesOnItsOwnClockAndPassesTiles()
        {
            var brain = new Brain(Row("ghost", fly: 1f, gravity: 0));
            Assert.True(brain.PassesTiles);
            var body = new FreeBody { Pos = new V2(0, -40) };
            var sink = new Sink();
            var rng = new Random(1);
            for (int i = 0; i < 60; i++)
            {
                brain.Update(body, At(body.Pos.X + 10, body.Pos.Y), sink, rng);
                body.Step();
            }
            int every = int.Parse(Defs.Attack["ghost.curse"].PerFrames.TrimEnd('F'));
            Assert.InRange(sink.Special.Count(s => s == "ghost.curse"), 60 / every - 1, 60 / every + 1);
        }

        [Fact]
        public void NestReleasesItsCreaturesWhenThePlayerIsNear()
        {
            var brain = new Brain(Row("nest_fly", gravity: 0, sight: 10));
            var body = new FreeBody { Pos = new V2(0, 0) };
            var sink = new Sink();
            var rng = new Random(1);
            for (int i = 0; i < Defs.Attack["nest_fly.release"].CooldownFrames + 10; i++)
            {
                brain.Update(body, At(48, 0), sink, rng);
                body.Step();
            }
            Assert.Equal(new[] { "nest_fly.release" }, sink.Special.ToArray());
            Assert.Equal(new V2(0, 0).X, body.Pos.X);
        }

        [Fact]
        public void NestNoticesThePlayerAsFarAsItReleasesEvenBeyondItsSight()
        {
            var row = Defs.Enemy["nest_fly"];
            var release = Defs.Attack["nest_fly.release"];
            Assert.True(release.RangeTiles > row.SightTiles, "the case the autotest hit: spawned 10 tiles away, sight 9.4");
            var brain = new Brain(row);
            var sink = new Sink();
            var rng = new Random(1);
            float d = (release.RangeTiles - 1) * Brain.Tile;
            for (int i = 0; i < release.CooldownFrames + 10; i++)
                brain.Update(new FreeBody(), At(d, 0), sink, rng);
            Assert.Contains("nest_fly.release", sink.Special);
        }

        [Fact]
        public void WolfLungeStopsAfterItsDistanceInsteadOfFlyingOff()
        {
            // in Terraria the leap at 40 px/frame did not register a landing and the wolf sailed 150 tiles away
            var brain = new Brain(Defs.Enemy["wolf"]);
            var body = new FreeBody { Pos = new V2(0, 0), OnGround = true };
            var rng = new Random(1);
            float far = 0;
            for (int i = 0; i < 300; i++)
            {
                brain.Update(body, At(300, 0), new Sink(), rng);
                body.OnGround = !brain.Dashing;          // never "lands" while dashing
                if (body.OnGround)
                    body.Velocity = new V2(body.Velocity.X, 0);
                body.Step();
                far = Math.Max(far, Math.Abs(body.Pos.X - 300));
            }
            float reach = Defs.Attack["wolf.lunge"].RangeTiles * Brain.Tile;
            Assert.True(far < reach * 2 + 100, "went " + far / Brain.Tile + " tiles");
        }

        [Fact]
        public void WraithRetaliatesWhenHurt()
        {
            var brain = new Brain(Row("wraith_glowing", fly: 1f, gravity: 0));
            var sink = new Sink();
            brain.Hurt(new FreeBody(), At(100, 0), sink, new Random(1));
            Assert.Contains(sink.Shots, s => s.id == "wraith_glowing.pinpoint_of_light");
        }

        [Fact]
        public void FungusExplodesWhenItDies()
        {
            var brain = new Brain(Defs.Enemy["fungus"]);
            var sink = new Sink();
            brain.Died(sink);
            Assert.Equal(new[] { "fungus.explodes_on_death" }, sink.Special.ToArray());
        }
    }
}
