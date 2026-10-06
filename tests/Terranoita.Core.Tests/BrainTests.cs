using System;
using System.Collections.Generic;
using System.Linq;
using Terranoita.Ai;
using Terranoita.Generated;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>Stage 1a enemy behaviour against a flat test floor, using the real sheet rows.</summary>
    public class BrainTests
    {
        /// <summary>A box on a flat floor at y = 0 (y grows downwards, like Terraria), optional wall at WallX.</summary>
        sealed class Box : IBody
        {
            public V2 Pos;                 // center
            public V2 Velocity { get; set; }
            public int Width { get; set; } = 20;
            public int Height { get; set; } = 20;
            public float? WallX;
            public bool OnGround { get; private set; }
            public bool HitWall { get; private set; }
            public V2 Center => Pos;

            public void Step()
            {
                var next = Pos + Velocity;
                HitWall = false;
                if (WallX.HasValue && Math.Sign(next.X + Width / 2f * Math.Sign(Velocity.X) - WallX.Value) != Math.Sign(Pos.X - WallX.Value))
                {
                    next.X = Pos.X;
                    Velocity = new V2(0, Velocity.Y);
                    HitWall = true;
                }
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

        sealed class Sink : IAttackSink
        {
            public readonly List<string> Melee = new List<string>();
            public readonly List<string> Dash = new List<string>();
            public readonly List<(string id, V2 vel)> Shots = new List<(string, V2)>();
            void IAttackSink.Melee(AttackDef a) => Melee.Add(a.Id);
            void IAttackSink.DashHit(AttackDef a) => Dash.Add(a.Id);
            void IAttackSink.Shoot(AttackDef a, ProjectileDef p, V2 from, V2 vel) => Shots.Add((a.Id, vel));
            public readonly List<string> Started = new List<string>();
            public int Jumps;
            void IAttackSink.Started(AttackDef a) => Started.Add(a.Id);
            void IAttackSink.Jumped() => Jumps++;
        }

        static Target PlayerAt(float x, float y = -21) =>
            new Target { Has = true, Center = new V2(x, y), Width = 20, Height = 42, Visible = true };

        static (Box body, Sink sink, Brain brain) Run(string enemy, Target t, int frames, float startX = 0, float startY = -10, float? wall = null)
        {
            var brain = new Brain(Defs.Enemy[enemy]);
            var body = new Box { Pos = new V2(startX, startY), WallX = wall };
            var sink = new Sink();
            var rng = new Random(1);
            for (int i = 0; i < frames; i++)
            {
                brain.Update(body, t, sink, rng);
                body.Step();
            }
            return (body, sink, brain);
        }

        [Fact]
        public void Every1aEnemyHasItsMovementFromNoita()
        {
            foreach (var e in Enemies.All.Where(e => e.Stage == "1a"))
            {
                Assert.True(e.Gravity > 0, e.Id + " gravity");
                Assert.True(e.SightTiles > 0, e.Id + " sight");
                Assert.True(e.Walks || e.Flies, e.Id + " can neither walk nor fly");
                Assert.NotNull(new Brain(e).Archetype);
            }
        }

        [Fact]
        public void WalkerClosesInAndBites()
        {
            var (body, sink, _) = Run("zombie_weak", PlayerAt(160), 600);
            Assert.True(body.Pos.X > 100, "walked toward the player: " + body.Pos);
            Assert.True(sink.Melee.Contains("zombie_weak.melee"), "melee " + sink.Melee.Count + " dash " + sink.Dash.Count + " started " + string.Join(",", sink.Started) + " pos " + body.Pos);
            Assert.Contains("zombie_weak.melee", sink.Started);
        }

        [Fact]
        public void MeleeRespectsItsCooldown()
        {
            var (_, sink, _) = Run("rat", PlayerAt(25), 600);
            int cooldown = Defs.Attack["rat.melee"].CooldownFrames;
            Assert.InRange(sink.Melee.Count, 600 / cooldown - 2, 600 / cooldown + 1);
        }

        [Fact]
        public void ShooterHoldsDistanceAndFiresVolleys()
        {
            var (body, sink, _) = Run("shotgunner_weak", PlayerAt(40), 400);
            Assert.True(body.Pos.X < 0, "backed away from a player standing too close: " + body.Pos);
            var shots = sink.Shots.Where(s => s.id == "shotgunner_weak.projectile").ToList();
            Assert.NotEmpty(shots);
            float speed = Defs.Projectile["shotgunner_weak_shot"].Speed;
            Assert.All(shots, s => Assert.InRange(s.vel.Length, speed * 0.99f, speed * 1.01f));
        }

        [Fact]
        public void ThrowerLobsTntInAnArc()
        {
            var (_, sink, _) = Run("miner_weak", PlayerAt(200), 300);
            var tnt = sink.Shots.Where(s => s.id == "miner_weak.tnt").ToList();
            Assert.NotEmpty(tnt);
            Assert.All(tnt, s => Assert.True(s.vel.Y < 0 && s.vel.X > 0, "thrown up and toward the player: " + s.vel));
        }

        [Fact]
        public void AimHitsTheTargetUnderGravity()
        {
            float g = 0.2222f, speed = 8f;
            var from = new V2(0, 0);
            var to = new V2(150, 30);
            var v = Brain.Aim(from, to, speed, g);
            var p = from;
            float best = float.MaxValue;
            for (int i = 0; i < 300; i++)
            {
                p += v;
                v = new V2(v.X, v.Y + g);
                best = Math.Min(best, (p - to).Length);
            }
            Assert.True(best < 8, "closest approach " + best);
        }

        [Fact]
        public void FlyerIgnoresGravityAndLungesAtThePlayer()
        {
            var t = PlayerAt(120, -100);
            var (body, sink, _) = Run("bat", t, 900, startX: 0, startY: -100);
            Assert.True(body.Pos.Y < -40, "stayed in the air: " + body.Pos);
            Assert.Contains("bat.lunge", sink.Dash);
        }

        [Fact]
        public void FloaterKeepsItsDistance()
        {
            var t = PlayerAt(0, -100);
            var (body, sink, brain) = Run("acidshooter_weak", t, 900, startX: 300, startY: -100);
            float keep = brain.Archetype.KeepDistanceTiles * Brain.Tile;
            float d = (body.Pos - t.Center).Length;
            Assert.InRange(d, keep * 0.4f, keep * 1.6f);
            Assert.Contains(sink.Shots, s => s.id == "acidshooter_weak.acid_ball");
        }

        [Fact]
        public void FrogMovesOnlyByHopping()
        {
            var brain = new Brain(Defs.Enemy["frog"]);
            var body = new Box { Pos = new V2(0, -10) };
            var sink = new Sink();
            var rng = new Random(3);
            bool airborne = false;
            for (int i = 0; i < 400; i++)
            {
                brain.Update(body, PlayerAt(200), sink, rng);
                body.Step();
                airborne |= !body.OnGround;
            }
            Assert.True(airborne, "never left the ground");
            Assert.True(body.Pos.X > 30, "hopped toward the player: " + body.Pos);
        }

        [Fact]
        public void WalkerJumpsAWall()
        {
            var (body, _, _) = Run("zombie_weak", PlayerAt(400), 400, wall: 60);
            Assert.True(body.Pos.Y < -10 || body.Pos.X < 60, "it is stuck or jumped");
        }
    }
}
