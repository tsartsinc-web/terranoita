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
    /// The world around the enemy, for movers that need more than their own collisions: climbers (lukki) hold on to
    /// any solid tile, burrowers (worms) know when they are in the ground, swimmers when they are in liquid.
    /// Units: Terraria world pixels. Without it those movers fall back to walking (climbers) or act as if they were
    /// always in their element (burrowers in ground, swimmers on land).
    /// </summary>
    public interface ITerrain
    {
        bool Solid(V2 p);
        bool Liquid(V2 p);
    }

    /// <summary>
    /// The attacks that are not a hit or a shot: auras, summons, heals, support buffs and death explosions. The game
    /// side carries them out by the attack's kind and sheet row; a sink without this interface skips them.
    /// </summary>
    public interface ISpecialAttackSink
    {
        void Special(AttackDef attack);
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
        const float GripMargin = 6f;          // climbers hold on to tiles this close to their body
        const float WormTurn = 0.05f;         // fallback when a burrower's row has no turn_rate
        const float WormSpeedMult = 0.5f;     // author: worms at half Noita's speed
        const int TrailLength = 256;

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
        V2 _dashFrom;
        int _pose;
        int _hopTimer;
        int _range = 1;     // ground shooters: 1 approach, 0 hold, -1 back off
        int _wanderTimer;
        float _wobble;
        int _age;
        bool _awake;
        V2 _heading;
        float _speed;
        readonly float _notice;
        readonly List<V2> _trail = new List<V2>();

        public bool Dashing => _dashLeft > 0;
        /// <summary>Frames this brain has run.</summary>
        public int Age => _age;
        /// <summary>Ghosts and worms move through tiles: the game turns tile collision off for them.</summary>
        public bool PassesTiles => Move == "phase" || Move == "burrow";
        public bool Burrows => Move == "burrow" || Move == "burrow_liquid";
        /// <summary>Worms: drawn as a head with its body along Trail, turned to its velocity.</summary>
        public bool Burrowing => Burrows;
        /// <summary>A disguised creature (mimic) that has not noticed the player yet.</summary>
        public bool Dormant => !_awake;
        /// <summary>Burrowers: where the head has been, newest first, one point per frame (for drawing the body).</summary>
        public IReadOnlyList<V2> Trail => _trail;

        public Brain(EnemyDef enemy)
            : this(enemy, enemy.Ai != null && Defs.Archetype.TryGetValue(enemy.Ai, out var a) ? a : null, Defs.AttacksOf(enemy))
        {
        }

        public Brain(EnemyDef enemy, AiArchetypeDef archetype, IEnumerable<AttackDef> attacks)
        {
            Enemy = enemy;
            Archetype = archetype;
            _attacks = attacks.ToList();
            foreach (var at in _attacks)
                _cooldown[at.Id] = at.CooldownFrames / 2;   // first attack comes a little sooner than the full wait
            _awake = (archetype?.WakeTiles ?? 0) <= 0;
            // it notices the player at least as far as its own attacks reach (turrets, nests)
            float reach = _attacks.Where(a => a.Kind != "death_explosion" && a.Kind != "retaliate").Select(a => a.RangeTiles).DefaultIfEmpty(0).Max();
            _notice = Math.Max(Math.Max(enemy.SightTiles, reach), 4f) * Tile;
        }

        string Move => Archetype?.Move ?? "ground";
        bool Flying => Move == "phase" || ((Move == "fly" || Move == "hover") && Enemy.Flies);
        bool Flees => Archetype?.Flees ?? false;
        float KeepDistance => (Archetype?.KeepDistanceTiles ?? 0) * Tile;

        IAttackSink _sink;

        public void Update(IBody body, Target target, IAttackSink sink, Random rng, ITerrain terrain = null)
        {
            _sink = sink;
            _age++;
            foreach (var k in _cooldown.Keys.ToList())
                if (_cooldown[k] > 0)
                    _cooldown[k]--;
            if (_pose > 0)
                _pose--;

            if (!_awake)
            {
                if (target.Has && Distance(body.Center, target.Center) <= Archetype.WakeTiles * Tile)
                    _awake = true;
                else
                {
                    // disguised: lies still where it is (a potion, a chest) until the player comes close or hurts it
                    body.Velocity = new V2(0, body.Velocity.Y);
                    if (Move != "static")
                        Fall(body);
                    SetAnim("stand");
                    return;
                }
            }

            bool engaged = target.Has && Distance(body.Center, target.Center) <= _notice;
            if (engaged)
                Direction = (target.Center.X >= body.Center.X ? 1 : -1) * (Flees ? -1 : 1);

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
                    _dashLeft = Math.Min(_dashLeft, 4);     // a few frames on, then it is over
                }
                // Noita's dash covers attack_dash_distance (the attack's range) and stops
                if ((body.Center - _dashFrom).Length >= _dash.RangeTiles * Tile + body.Width)
                    _dashLeft = 0;
                if (_dashLeft == 0)
                {
                    var v = body.Velocity;
                    body.Velocity = new V2(v.X * 0.2f, Flying ? v.Y * 0.2f : Math.Max(v.Y, 0));
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
            else if (Move == "climb" && terrain != null)
                ClimbMove(body, target, engaged, rng, terrain);
            else if (Burrows)
                BurrowMove(body, target, engaged, rng, terrain);
            else if (Move == "swim")
                SwimMove(body, target, engaged, rng, terrain);
            else
                GroundMove(body, target, engaged, rng);

            if (Burrows)
            {
                _trail.Insert(0, body.Center);
                if (_trail.Count > TrailLength)
                    _trail.RemoveAt(_trail.Count - 1);
            }

            if (engaged)
                TryAttacks(body, target, sink, rng);

            if (_pose > 0)
                SetAnim("attack");
            else if (Flying)
                SetAnim("fly");
            else if (Burrows || Move == "swim" || (Move == "climb" && terrain != null))
                SetAnim(body.Velocity.Length > 0.1f ? "walk" : "stand");
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
                if (Flees)
                    want = -(dx >= 0 ? 1 : -1) * Enemy.RunSpeed;     // harmless animals run from the player
                else if (keep <= 0)
                    want = Math.Sign(dx) * Enemy.RunSpeed;
                else
                {
                    // approach / hold / back off, with hysteresis so it does not stutter at the edges
                    if (adx < keep * 0.5f) _range = -1;
                    else if (_range == -1 && adx > keep * 0.8f) _range = 0;
                    else if (_range == 1 && adx < keep && target.Visible) _range = 0;
                    else if (_range == 0 && (adx > keep * 1.3f || !target.Visible)) _range = 1;
                    want = _range * Math.Sign(dx) * Enemy.RunSpeed;
                }
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
                bool climb = engaged && !Flees && target.Center.Y < body.Center.Y - 3 * Tile &&
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

        /// <summary>Wander goal for movers that roam freely: ahead in the current direction, bobbing up and down.</summary>
        V2 WanderGoal(IBody body, Random rng, int minFrames, int spread, float bob)
        {
            _wobble += 0.05f;
            if (--_wanderTimer <= 0)
            {
                _wanderTimer = minFrames + rng.Next(spread);
                Direction = rng.Next(2) == 0 ? -1 : 1;
            }
            return body.Center + new V2(Direction * 64, (float)Math.Sin(_wobble) * bob);
        }

        bool Grips(IBody body, ITerrain terrain, V2 at)
        {
            float hw = body.Width / 2f + GripMargin, hh = body.Height / 2f + GripMargin;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if ((i != 0 || j != 0) && terrain.Solid(at + new V2(i * hw, j * hh)))
                        return true;
            return false;
        }

        /// <summary>
        /// Lukki: walks on any solid surface, walls and ceilings included, straight toward the target as long as a
        /// leg can reach a tile; turns along the surface when the straight way would leave it; falls when it lets go.
        /// </summary>
        void ClimbMove(IBody body, Target target, bool engaged, Random rng, ITerrain terrain)
        {
            bool levitates = Enemy.Gravity <= 0f;
            bool grip = Grips(body, terrain, body.Center);
            if (!grip && !levitates)
            {
                Fall(body);
                return;
            }
            var goal = engaged ? target.Center : WanderGoal(body, rng, 120, 180, 48);
            var to = goal - body.Center;
            float speed = Math.Max(Enemy.RunSpeed, 0.5f);
            if (!grip)
            {
                // nothing to hold on to: a levitating body is pushed straight at its goal (PhysicsAIComponent)
                var w = to.Length > 4 && _pose <= 0 ? to.Normalized * speed : new V2(0, 0);
                var vv = body.Velocity;
                vv.X += (w.X - vv.X) * Accel;
                vv.Y += (w.Y - vv.Y) * Accel;
                body.Velocity = vv;
                return;
            }
            var want = new V2(0, 0);
            if (to.Length > 4 && _pose <= 0)
            {
                var dir = to.Normalized;
                foreach (float turn in new[] { 0f, 0.8f, -0.8f, 1.57f, -1.57f, 2.4f, -2.4f })
                {
                    var d = dir.Rotated(turn);
                    if (Grips(body, terrain, body.Center + d * (speed * 4 + 2)))
                    {
                        want = d * speed;
                        break;
                    }
                }
            }
            var v = body.Velocity;
            v.X += (want.X - v.X) * Accel;
            v.Y += (want.Y - v.Y) * Accel;
            body.Velocity = v;
            if (Math.Abs(v.X) > 0.05f)
                Direction = v.X > 0 ? 1 : -1;
        }

        /// <summary>
        /// Worm: inside the ground (or liquid) the head steers toward the target with a limited turn, at its speed;
        /// out in the open it keeps its momentum and falls back in an arc, which is how Noita's worms leap out at you.
        /// </summary>
        void BurrowMove(IBody body, Target target, bool engaged, Random rng, ITerrain terrain)
        {
            // Noita's WormComponent: the speed steps toward the target speed by `accel` each frame (hunting or roaming
            // speed from WormAIComponent), the head turns by turn_rate / roam_turn_rate radians per frame. Water worms
            // (burrow_liquid) move only inside liquid.
            bool water = Move == "burrow_liquid";
            bool inside = terrain == null || terrain.Liquid(body.Center) || (!water && terrain.Solid(body.Center));
            bool hunting = engaged;
            float top = hunting ? Enemy.RunSpeed : (Enemy.RoamSpeed > 0 ? Enemy.RoamSpeed : Enemy.RunSpeed * 0.5f);
            top = Math.Max(water ? top : top * WormSpeedMult, 0.5f);
            float turn = hunting ? Enemy.TurnRate : Enemy.RoamTurnRate;
            if (turn <= 0)
                turn = WormTurn;
            if (_heading.Length < 0.5f)
                _heading = new V2(Direction, 0);
            if (inside)
            {
                var goal = engaged ? target.Center : WanderGoal(body, rng, 90, 120, 64);
                _heading = Turn(_heading, (goal - body.Center).Normalized, turn);
                float step = Enemy.Accel > 0 ? Enemy.Accel : 0.05f;
                _speed = _speed < top ? Math.Min(top, _speed + step) : Math.Max(top, _speed - step);
                body.Velocity = _heading * _speed;
            }
            else
            {
                Fall(body);
                if (body.Velocity.Length > 0.1f)
                    _heading = body.Velocity.Normalized;
                _speed = body.Velocity.Length;
            }
            if (Math.Abs(body.Velocity.X) > 0.05f)
                Direction = body.Velocity.X > 0 ? 1 : -1;
        }

        static V2 Turn(V2 from, V2 to, float maxRad)
        {
            if (to.Length < 1e-3f)
                return from;
            double a = Math.Atan2(from.Y, from.X), d = Math.Atan2(to.Y, to.X) - a;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            a += Math.Max(-maxRad, Math.Min(maxRad, d));
            return new V2((float)Math.Cos(a), (float)Math.Sin(a));
        }

        /// <summary>Fish: swims around in liquid without gravity and turns back at its edge; flops about on land.</summary>
        void SwimMove(IBody body, Target target, bool engaged, Random rng, ITerrain terrain)
        {
            bool wet = terrain != null && terrain.Liquid(body.Center);
            var v = body.Velocity;
            if (!wet)
            {
                if (body.OnGround)
                {
                    v.X *= 0.8f;
                    if (--_hopTimer <= 0)
                    {
                        _hopTimer = 30 + rng.Next(40);
                        Direction = rng.Next(2) == 0 ? -1 : 1;
                        v = new V2(Direction * 1.5f, -Math.Max(Enemy.JumpSpeed * 0.4f, 2f));
                        _sink?.Jumped();
                    }
                }
                body.Velocity = v;
                Fall(body);
                return;
            }
            var goal = engaged && Flees ? body.Center - (target.Center - body.Center).Normalized * 64
                                        : WanderGoal(body, rng, 90, 150, 24);
            var want = (goal - body.Center).Normalized * Math.Max(Enemy.RunSpeed, 0.5f);
            if (!terrain.Liquid(body.Center + want.Normalized * (body.Width / 2f + 4)))
            {
                want = new V2(-want.X, -want.Y * 0.5f);
                Direction = want.X >= 0 ? 1 : -1;
                _wanderTimer = 60;
            }
            float a = Math.Max(0.04f, Accel * 0.25f);
            v.X += (want.X - v.X) * a;
            v.Y += (want.Y - v.Y) * a;
            body.Velocity = v;
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
            float dist = Distance(body.Center, target.Center);
            var special = sink as ISpecialAttackSink;
            // auras work on their own clock, next to whatever else the creature does
            foreach (var a in _attacks)
                if (a.Kind == "aura" && _cooldown[a.Id] <= 0 && dist <= a.RangeTiles * Tile)
                {
                    special?.Special(a);
                    _cooldown[a.Id] = AuraInterval(a);
                }
            foreach (var a in _attacks)
            {
                if (_cooldown[a.Id] > 0)
                    continue;
                float range = a.RangeTiles * Tile;
                switch (a.Kind)
                {
                    case "summon":
                    case "heal":
                    case "support":
                        if (dist <= range)
                        {
                            sink.Started(a);
                            special?.Special(a);
                            _cooldown[a.Id] = Math.Max(1, a.CooldownFrames);
                            _pose = AttackPoseFrames;
                            return;
                        }
                        break;
                    case "melee":
                        // Noita measures melee reach between the two creatures' positions, not their edges;
                        // a worm bites whatever its head touches
                        if (dist <= range || (Burrows && Overlaps(body, target)))
                        {
                            sink.Started(a);
                            sink.Melee(a);
                            _cooldown[a.Id] = a.CooldownFrames;
                            _pose = AttackPoseFrames;
                            return;
                        }
                        break;
                    case "lunge":
                        if (target.Visible && dist <= range && dist > MeleeRange() && (Flying || body.OnGround || Move == "climb"))
                        {
                            // Noita's dash: flyers dart straight at the target; walkers leap at it in an arc
                            body.Velocity = Flying
                                ? (target.Center - body.Center).Normalized * a.LungeSpeed
                                : Aim(body.Center, target.Center, a.LungeSpeed, Enemy.Gravity);
                            _dashAge = 0;
                            _dashFrom = body.Center;
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

        /// <summary>Frames between aura ticks: the sheet's "NF" (every N frames), else its cooldown.</summary>
        static int AuraInterval(AttackDef a)
        {
            var pf = a.PerFrames ?? "";
            if (pf.EndsWith("F") && int.TryParse(pf.Substring(0, pf.Length - 1), out int n) && n > 0)
                return n;
            return Math.Max(1, a.CooldownFrames);
        }

        /// <summary>The enemy was hurt by the attacker: a disguised one wakes, retaliating attacks fire back.</summary>
        public void Hurt(IBody body, Target attacker, IAttackSink sink, Random rng)
        {
            _awake = true;
            if (!attacker.Has)
                return;
            var special = sink as ISpecialAttackSink;
            foreach (var a in _attacks)
                if (a.Kind == "retaliate" && _cooldown[a.Id] <= 0 && a.Summons != null && a.Summons.Length > 0)
                {
                    // it splits or calls for help when hurt (giantshooter -> slimeshooters)
                    sink.Started(a);
                    special?.Special(a);
                    _cooldown[a.Id] = Math.Max(1, a.CooldownFrames);
                }
                else if (a.Kind == "retaliate" && _cooldown[a.Id] <= 0 && a.Projectile != null &&
                    Defs.Projectile.TryGetValue(a.Projectile, out var p))
                {
                    sink.Started(a);
                    Fire(body, attacker, a, p, sink, rng);
                    _cooldown[a.Id] = a.CooldownFrames;
                }
        }

        /// <summary>The enemy died: its death attacks (explosions, acid bursts) go off.</summary>
        public void Died(IAttackSink sink)
        {
            var special = sink as ISpecialAttackSink;
            foreach (var a in _attacks)
                if (a.Kind == "death_explosion")
                    special?.Special(a);
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
            // thrown things (Noita's physics throws) get the speed they need to reach: the 45-degree minimum, plus a margin
            float needed = (float)Math.Sqrt(gravity * (y + Math.Sqrt(x * x + y * y))) * 1.1f;
            if (needed > speed)
                speed = needed;
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
