using System;
using System.Collections.Generic;
using System.Linq;
using Terranoita.Generated;

namespace Terranoita.Ai
{
    public struct V2
    {
        public float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);
        public static V2 operator *(V2 a, float k) => new V2(a.X * k, a.Y * k);
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public V2 Normalized { get { float l = Length; return l < 1e-4f ? new V2(0, 0) : new V2(X / l, Y / l); } }
        public V2 Rotated(float rad)
        {
            float c = (float)Math.Cos(rad), s = (float)Math.Sin(rad);
            return new V2(X * c - Y * s, X * s + Y * c);
        }
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    /// <summary>The enemy's body as the game moves it. Units: Terraria world pixels and pixels/frame.</summary>
    public interface IBody
    {
        V2 Center { get; }
        V2 Velocity { get; set; }
        int Width { get; }
        int Height { get; }
        bool OnGround { get; }
        /// <summary>Blocked sideways by tiles last frame.</summary>
        bool HitWall { get; }
    }

    /// <summary>What the enemy knows about the player it is after.</summary>
    public struct Target
    {
        public bool Has;
        public V2 Center;
        public int Width, Height;
        /// <summary>A straight line between the two is free of tiles.</summary>
        public bool Visible;
    }

    /// <summary>The game side carries out attacks.</summary>
    public interface IAttackSink
    {
        void Melee(AttackDef attack);
        void DashHit(AttackDef attack);
        void Shoot(AttackDef attack, ProjectileDef projectile, V2 from, V2 velocity);
        /// <summary>An attack begins (for its sound).</summary>
        void Started(AttackDef attack);
        /// <summary>The enemy jumped (for its sound).</summary>
        void Jumped();
    }

    /// <summary>
    /// One enemy's behaviour, driven only by its rows in the sheets: movement numbers from the enemies sheet (read from
    /// Noita), the archetype's movement mode and preferred distance, and its attacks. Called once per frame.
    /// </summary>
    public sealed class Brain
    {
        public const float Tile = 16f;
        const float MaxFall = 10f;
        const int AttackPoseFrames = 18;

        public readonly EnemyDef Enemy;
        public readonly AiArchetypeDef Archetype;
        readonly List<AttackDef> _attacks;
        readonly Dictionary<string, int> _cooldown = new Dictionary<string, int>();

        public int Direction = 1;
        public string Anim = "stand";
        public int AnimTicks;
        int _dashLeft;
        AttackDef _dash;
        bool _dashHit;
        int _dashAge;
        int _pose;
        int _hopTimer;
        int _wanderTimer;
        float _wobble;
        int _age;

        public bool Dashing => _dashLeft > 0;
        /// <summary>Frames this brain has run.</summary>
        public int Age => _age;

        public Brain(EnemyDef enemy)
        {
            Enemy = enemy;
            Archetype = enemy.Ai != null && Defs.Archetype.TryGetValue(enemy.Ai, out var a) ? a : null;
            _attacks = Defs.AttacksOf(enemy).ToList();
            foreach (var at in _attacks)
                _cooldown[at.Id] = at.CooldownFrames / 2;   // first attack comes a little sooner than the full wait
        }

        string Move => Archetype?.Move ?? "ground";
        bool Flying => (Move == "fly" || Move == "hover") && Enemy.Flies;
        float KeepDistance => (Archetype?.KeepDistanceTiles ?? 0) * Tile;

        IAttackSink _sink;

        public void Update(IBody body, Target target, IAttackSink sink, Random rng)
        {
            _sink = sink;
            _age++;
            foreach (var k in _cooldown.Keys.ToList())
                if (_cooldown[k] > 0)
                    _cooldown[k]--;
            if (_pose > 0)
                _pose--;

            bool engaged = target.Has && Distance(body.Center, target.Center) <= Math.Max(Enemy.SightTiles, 4f) * Tile;
            if (engaged)
                Direction = target.Center.X >= body.Center.X ? 1 : -1;

            if (_dashLeft > 0)
            {
                _dashLeft--;
                _dashAge++;
                if (!Flying && _dashAge > 3 && body.OnGround)
                    _dashLeft = 0;          // a ground lunge is a leap: it ends when it lands
                if (!_dashHit && target.Has && Overlaps(body, target))
                {
                    _dashHit = true;
                    sink.DashHit(_dash);
                }
                if (!Flying)
                    Fall(body);
                SetAnim("attack");
                return;
            }

            if (Flying)
                FlyMove(body, target, engaged, rng);
            else if (Move == "hop")
                HopMove(body, target, engaged, rng);
            else if (Move == "static")
                body.Velocity = new V2(0, 0);
            else
                GroundMove(body, target, engaged, rng);

            if (engaged)
                TryAttacks(body, target, sink, rng);

            if (_pose > 0)
                SetAnim("attack");
            else if (Flying)
                SetAnim("fly");
            else if (!body.OnGround)
                SetAnim(body.Velocity.Y < 0 ? "jump_up" : "jump_fall");
            else
                SetAnim(Math.Abs(body.Velocity.X) > 0.1f ? "walk" : "stand");
        }

        void SetAnim(string name)
        {
            if (Anim != name)
            {
                Anim = name;
                AnimTicks = 0;
            }
            else
                AnimTicks++;
        }

        // ---- movement -------------------------------------------------------------------------------------------

        void Fall(IBody body)
        {
            var v = body.Velocity;
            v.Y = Math.Min(v.Y + Enemy.Gravity, MaxFall);
            body.Velocity = v;
        }

        float Accel => Math.Max(0.05f, Math.Min(1f, Enemy.Accel));

        void GroundMove(IBody body, Target target, bool engaged, Random rng)
        {
            float want;
            if (engaged)
            {
                float dx = target.Center.X - body.Center.X;
                float adx = Math.Abs(dx);
                float keep = KeepDistance;
                if (keep > 0 && adx < keep * 0.7f)
                    want = -Math.Sign(dx) * Enemy.RunSpeed;          // too close: back off
                else if (keep > 0 && adx < keep * 1.1f && target.Visible)
                    want = 0;                                       // in range: hold and shoot
                else
                    want = Math.Sign(dx) * Enemy.RunSpeed;
            }
            else
            {
                if (--_wanderTimer <= 0)
                {
                    _wanderTimer = 120 + rng.Next(180);
                    Direction = rng.Next(3) == 0 ? -Direction : Direction;
                }
                want = Direction * Enemy.RunSpeed * 0.5f;
            }
            if (_pose > 0)
                want = 0;

            var v = body.Velocity;
            v.X += (want - v.X) * Accel;
            body.Velocity = v;

            if (body.OnGround && Enemy.Walks)
            {
                bool wall = body.HitWall && Math.Abs(want) > 0.01f;
                bool climb = engaged && target.Center.Y < body.Center.Y - 3 * Tile &&
                             Math.Abs(target.Center.X - body.Center.X) < 8 * Tile;
                if ((wall || climb) && Enemy.Jumps)
                    Jump(body, want);
                else if (wall && !engaged)
                    Direction = -Direction;
            }
            Fall(body);
        }

        void Jump(IBody body, float vx)
        {
            var v = body.Velocity;
            v.Y = -Enemy.JumpSpeed;
            v.X = vx;
            body.Velocity = v;
            _sink?.Jumped();
        }

        void HopMove(IBody body, Target target, bool engaged, Random rng)
        {
            // Hoppers that can walk walk between hops; ones with no run speed (frogs) only move by jumping.
            if (Enemy.RunSpeed > 0.01f)
            {
                GroundMove(body, target, engaged, rng);
                if (body.OnGround && engaged && --_hopTimer <= 0)
                {
                    _hopTimer = 50 + rng.Next(50);
                    Jump(body, Direction * Math.Max(Enemy.RunSpeed, Enemy.JumpSpeed * 0.35f));
                }
                return;
            }
            var v = body.Velocity;
            if (body.OnGround)
            {
                v.X *= 0.8f;
                body.Velocity = v;
                if (--_hopTimer <= 0)
                {
                    _hopTimer = engaged ? 40 + rng.Next(40) : 90 + rng.Next(120);
                    if (!engaged && rng.Next(2) == 0)
                        Direction = -Direction;
                    Jump(body, Direction * Enemy.JumpSpeed * (engaged ? 0.45f : 0.25f));
                }
            }
            Fall(body);
        }

        void FlyMove(IBody body, Target target, bool engaged, Random rng)
        {
            _wobble += 0.07f + (float)rng.NextDouble() * 0.03f;
            V2 goal;
            if (engaged)
            {
                float keep = KeepDistance;
                if (keep > 0)
                {
                    // hover at the preferred distance, a little above, on our side of the target
                    float side = body.Center.X < target.Center.X ? -1 : 1;
                    goal = target.Center + new V2(side * keep * 0.8f, -keep * 0.4f);
                }
                else
                    goal = target.Center;
            }
            else
            {
                if (--_wanderTimer <= 0)
                {
                    _wanderTimer = 90 + rng.Next(120);
                    Direction = rng.Next(2) == 0 ? -1 : 1;
                }
                goal = body.Center + new V2(Direction * 64, (float)Math.Sin(_wobble * 0.5f) * 32);
            }
            // erratic, like Noita flyers
            goal += new V2((float)Math.Sin(_wobble * 1.7f) * 24, (float)Math.Cos(_wobble * 1.3f) * 24);

            var to = goal - body.Center;
            float speed = Math.Max(Enemy.FlySpeed, 0.5f);
            var want = to.Length < 8 ? new V2(0, 0) : to.Normalized * speed;
            if (want.Y < -Enemy.FlyUpSpeed)
                want.Y = -Enemy.FlyUpSpeed;
            var v = body.Velocity;
            float a = Math.Max(0.04f, Accel * 0.25f);
            v.X += (want.X - v.X) * a;
            v.Y += (want.Y - v.Y) * a;
            if (body.HitWall)
                v.Y -= 0.1f;
            body.Velocity = v;
        }

        // ---- attacks --------------------------------------------------------------------------------------------

        void TryAttacks(IBody body, Target target, IAttackSink sink, Random rng)
        {
            float gap = EdgeGap(body, target);
            float dist = Distance(body.Center, target.Center);
            foreach (var a in _attacks)
            {
                if (_cooldown[a.Id] > 0)
                    continue;
                float range = a.RangeTiles * Tile;
                switch (a.Kind)
                {
                    case "melee":
                        if (gap <= range)
                        {
                            sink.Started(a);
                            sink.Melee(a);
                            _cooldown[a.Id] = a.CooldownFrames;
                            _pose = AttackPoseFrames;
                            return;
                        }
                        break;
                    case "lunge":
                        if (target.Visible && dist <= range && gap > MeleeRange() && (Flying || body.OnGround))
                        {
                            // Noita's dash: flyers dart straight at the target; walkers leap at it in an arc
                            body.Velocity = Flying
                                ? (target.Center - body.Center).Normalized * a.LungeSpeed
                                : Aim(body.Center, target.Center, a.LungeSpeed, Enemy.Gravity);
                            _dashAge = 0;
                            sink.Started(a);
                            _dash = a;
                            _dashLeft = Flying ? (int)(dist / Math.Max(a.LungeSpeed, 0.5f)) + 6 : 90;
                            _dashHit = false;
                            _cooldown[a.Id] = a.CooldownFrames;
                            return;
                        }
                        break;
                    case "projectile":
                        if (target.Visible && dist <= range && Defs.Projectile.TryGetValue(a.Projectile, out var p))
                        {
                            sink.Started(a);
                            Fire(body, target, a, p, sink, rng);
                            _cooldown[a.Id] = a.CooldownFrames;
                            _pose = AttackPoseFrames;
                            return;
                        }
                        break;
                }
            }
        }

        float MeleeRange()
        {
            float r = 0;
            foreach (var a in _attacks)
                if (a.Kind == "melee")
                    r = Math.Max(r, a.RangeTiles * Tile);
            return r;
        }

        void Fire(IBody body, Target target, AttackDef a, ProjectileDef p, IAttackSink sink, Random rng)
        {
            var from = body.Center;
            var aim = Aim(from, target.Center, p.Speed, p.Gravity);
            int min = a.Count != null && a.Count.Length > 0 ? a.Count[0] : 1;
            int max = a.Count != null && a.Count.Length > 1 ? a.Count[1] : min;
            int n = Math.Max(1, min + rng.Next(Math.Max(1, max - min + 1)));
            for (int i = 0; i < n; i++)
            {
                float spread = n > 1 ? (float)(rng.NextDouble() - 0.5) * 0.25f : 0f;
                sink.Shoot(a, p, from, aim.Rotated(spread));
            }
        }

        /// <summary>
        /// Launch velocity of the given speed that reaches the target under per-frame gravity: the flatter of the two
        /// ballistic solutions, or 45 degrees when the target is out of reach. Straight at it when there is no gravity.
        /// </summary>
        public static V2 Aim(V2 from, V2 to, float speed, float gravity)
        {
            var d = to - from;
            if (gravity <= 1e-5f || speed <= 0)
                return d.Normalized * speed;
            float x = Math.Abs(d.X), y = -d.Y;            // y up
            float v2 = speed * speed;
            float disc = v2 * v2 - gravity * (gravity * x * x + 2 * y * v2);
            double angle = disc < 0 ? Math.PI / 4 : Math.Atan((v2 - Math.Sqrt(disc)) / (gravity * Math.Max(x, 1e-3f)));
            float sx = (float)Math.Cos(angle) * speed * Math.Sign(d.X == 0 ? 1 : d.X);
            float sy = -(float)Math.Sin(angle) * speed;
            return new V2(sx, sy);
        }

        static float Distance(V2 a, V2 b) => (a - b).Length;

        static float EdgeGap(IBody b, Target t)
        {
            float gx = Math.Max(0, Math.Abs(b.Center.X - t.Center.X) - (b.Width + t.Width) / 2f);
            float gy = Math.Max(0, Math.Abs(b.Center.Y - t.Center.Y) - (b.Height + t.Height) / 2f);
            return (float)Math.Sqrt(gx * gx + gy * gy);
        }

        static bool Overlaps(IBody b, Target t) => EdgeGap(b, t) <= 2f;
    }
}
