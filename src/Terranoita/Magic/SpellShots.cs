using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// The player's spell projectiles: the mod's own list (like enemy Shots). Each one is its Noita projectile file
    /// (spell_projectiles.json: speed, gravity, air friction, lifetime, damage, explosion, bounces, penetration) with
    /// the shot config Noita's gun.lua gave it on top (damage and explosion added, speed multiplier, spread, extra
    /// lifetime, bounces, gravity, homing...). Triggers release their payload where they hit or expire.
    /// Units: 1 Noita pixel = 3 Terraria pixels, 1 Noita damage unit = 25 hp.
    /// </summary>
    public static partial class SpellShots
    {
        const float Px = Terranoita.Noita.Units.PixelScale;
        const int Max = 600;
        static bool _capLogged;
        const int FriendlyFireAfter = 10;   // ours: frames before a friendly_fire shot can hit its caster (it starts at the wand)

        sealed class Shot
        {
            public int Id;
            public SpellProjectileDef Def;
            public LuaShot Lua;
            public Vector2 Pos, Vel;
            public int Life, Age, Bounces, TriggerIn;
            public int Script, StartLife;      // its entity in Noita's shot scripts (0 = none), lifetime at start
            public bool Killed;                // a script killed it
            public bool Evicted;               // ended quietly to make room at the cap (no explosion, no payload)
            public bool Triggered;             // its CollisionTriggerComponent went off (a mine: a creature came near)
            public uint Born;                  // the game frame it was fired in (shots of one cast share it)
            public float Damage, ExplosionDamage, Radius, Gravity, Friction, Knockback;
            public bool Fire, Penetrate;
            public List<Extra> Extras;         // components of the modifiers' extra_entities (SpellShots.Extras.cs)
            public string[] Trail;             // trail_material
            public Player Owner;
            public ShotPhys Phys;              // Noita's engine rules for its file (SpellShots.Physics.cs)
            public Vector2 Origin;             // where it was fired (lightning trails start there; the homebringer bolt pulls to it)
            // ProjectileComponent fields Noita's scripts may switch (true_orbit.lua: collide_with_world 0)
            public bool NoWorld, DieOnCollision, PenetrateWorld, DieOnLow, ExplodeOnDeath, NullDamage;
            public bool FriendlyFire, HitOwner;   // friendly_fire (PIERCING_SHOT): it can hit its caster, once
            public readonly HashSet<int> Hit = new HashSet<int>();
            public Dictionary<string, float> Kinds;   // probe only: its hit damage by Noita's damage message (SpellRecorder)
        }

        static readonly List<Shot> Live = new List<Shot>();
        public static int LiveCount => Live.Count;
        static Dictionary<string, SpellProjectileDef> _defs;
        static int _nextId = 1;
        static readonly HashSet<string> Unknown = new HashSet<string>();

        static SpellProjectileDef Def(string file)
        {
            if (_defs == null)
                _defs = SpellProjectiles.All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(file))
                return null;
            if (_defs.TryGetValue(file, out var def))
                return def;
            // not in the sheet (files spell scripts load): built from the entity file by the sheet's own rules, cached
            // per file, a file without a ProjectileComponent too (null)
            try
            {
                def = NoitaArt.ReadText(file) == null ? null : SpellProjectileFromEntity.From(NoitaEntityXml.Load(file, NoitaArt.ReadText));
            }
            catch (Exception ex)
            {
                Entry.Error("spell projectile " + file, ex);
                def = null;
            }
            _defs[file] = def;
            return def;
        }

        public static List<int> Ids() => Live.Select(s => s.Id).ToList();
        public static Vector2 Position(int id) => Live.FirstOrDefault(s => s.Id == id)?.Pos ?? Vector2.Zero;

        /// <summary>The projectiles of a cast (or a trigger's payload), from pos toward dir. Noita's pattern_degrees
        /// (I/Y/T/W/circle/pentagram shapes, the divide spells): the projectiles of one shot (they share its config)
        /// fan out evenly over -P..+P degrees; a whole circle (P 180) spaces them 360/N apart, so the first and last
        /// do not overlap (I_SHAPE: forward and back).</summary>
        public static void FireAll(IList<LuaShot> shots, Vector2 pos, Vector2 dir, Player owner, WandData wand)
        {
            var groups = new Dictionary<object, List<LuaShot>>();
            var order = new List<object>();
            foreach (var ls in shots)
            {
                object key = (object)ls.Config ?? ls;
                if (!groups.TryGetValue(key, out var g))
                {
                    groups[key] = g = new List<LuaShot>();
                    order.Add(key);
                }
                g.Add(ls);
            }
            foreach (var key in order)
            {
                var g = groups[key];
                float pattern = g[0].Get("pattern_degrees");
                int n = g.Count;
                for (int i = 0; i < n; i++)
                {
                    var d = dir;
                    if (pattern > 0 && n > 1)
                    {
                        float step = pattern >= 180 ? 2 * pattern / n : 2 * pattern / (n - 1);
                        d = Vector2.Transform(dir, Matrix.CreateRotationZ(MathHelper.ToRadians(-pattern + step * i)));
                    }
                    Fire(g[i], pos, d, owner, wand);
                }
            }
        }

        /// <summary>A projectile of a cast, from pos toward dir.</summary>
        public static void Fire(LuaShot ls, Vector2 pos, Vector2 dir, Player owner, WandData wand)
        {
            // the cap is ours (speed; Noita has none): the oldest shot makes room, the new one is never refused
            // (refusing silently made every spell after a wall spell do nothing, 2026-10-09)
            if (Live.Count >= Max)
            {
                var oldest = Live.FirstOrDefault(x => !x.Evicted);
                if (oldest != null)
                    oldest.Evicted = true;
                if (!_capLogged)
                {
                    _capLogged = true;
                    Entry.Log("spell shots: " + Max + " alive (our cap): the oldest end quietly to make room");
                }
            }
            var d = Def(ls.File);
            if (d == null)
            {
                if (Unknown.Add(ls.File ?? "?"))
                    Entry.Log("spell projectile not in spell_projectiles.json yet: " + ls.File);
                return;
            }
            ReportRuntime(ls.File);
            foreach (var extra in ls.Text("extra_entities").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                ReportRuntime(extra.Trim());
            var rng = Main.rand;
            var phys = PhysOf(ls.File);
            float speed = (phys.Thrown ? phys.ThrowSpeed : d.SpeedMin + (float)rng.NextDouble() * Math.Max(0, d.SpeedMax - d.SpeedMin)) * Math.Max(0f, ls.Get("speed_multiplier"));
            // spread: the shot's degrees (wand + spells), and the projectile's own randomness
            float spreadDeg = Math.Max(0f, ls.Get("spread_degrees"));
            float angle = (float)Math.Atan2(dir.Y, dir.X) + MathHelper.ToRadians(((float)rng.NextDouble() * 2 - 1) * spreadDeg)
                          + ((float)rng.NextDouble() * 2 - 1) * d.SpreadRad;
            // ZERO_DAMAGE: damage_null_all (c.damage_explosion / c.damage_projectile that HIGH_EXPLOSIVE and BERSERK set
            // are not ConfigGunActionInfo fields, gunaction_generated.lua: Noita's engine never reads them)
            bool nullAll = ls.Get("damage_null_all") > 0;
            var s = new Shot
            {
                Id = _nextId++, Def = d, Lua = ls, Pos = pos, Owner = owner, Phys = phys, Origin = pos,
                NoWorld = !d.CollideWithWorld, DieOnCollision = phys.OnCollisionDie, PenetrateWorld = phys.PenetrateWorld,
                DieOnLow = phys.DieOnLowVelocity, ExplodeOnDeath = d.ExplodeOnDeath, NullDamage = nullAll,
                FriendlyFire = ls.Config != null && ls.Config.TryGetValue("friendly_fire", out var ff) &&
                               (ff.Type == MoonSharp.Interpreter.DataType.Boolean ? ff.Boolean : ff.Type == MoonSharp.Interpreter.DataType.Number && ff.Number != 0),
                Vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed * Px / 60f,
                Life = (d.Lifetime > 0 ? d.Lifetime : 600) + (int)ls.Get("lifetime_add") + rng.Next(-d.LifetimeRandom, d.LifetimeRandom + 1),
                Bounces = d.Bounces + (int)ls.Get("bounces"),
                Damage = nullAll ? 0 : Math.Max(0, d.Damage + d.TypedDamage + ls.Get("damage_projectile_add") +
                                     ls.Get("damage_fire_add") + ls.Get("damage_ice_add") +
                                     ls.Get("damage_electricity_add") + ls.Get("damage_slice_add") + ls.Get("damage_curse_add") +
                                     ls.Get("damage_drill_add") + ls.Get("damage_melee_add")) * 25f,
                ExplosionDamage = nullAll ? 0 : Math.Max(0, d.ExplosionDamage + ls.Get("damage_explosion_add")) * 25f,
                Radius = Math.Max(0, d.ExplosionRadius + ls.Get("explosion_radius")) * Px,
                Gravity = ((phys.Thrown ? ThrownGravity : d.Gravity) + ls.Get("gravity")) * Px / 3600f,
                Friction = phys.AirFriction,   // the file's air_friction, or Noita's default 0.55 when it sets none
                Knockback = d.Knockback + ls.Get("knockback_force"),
                Fire = ls.Get("damage_fire_add") > 0 || d.FireDamage > 0 || (d.Material ?? "").Contains("fire"),
                Extras = ExtrasOf(ls), Born = Main.GameUpdateCount,
                Trail = ls.Text("trail_material").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                Penetrate = d.Penetrate,
                TriggerIn = ls.Trigger == "timer" ? Math.Max(1, ls.TriggerFrames) : -1,
            };
            if (s.Life < 1)
                s.Life = 1;
            s.StartLife = s.Life;
            NoitaSound.PlayFirst(d.Audio, pos, "create");
            SpellRecorder.Born(s.Id, ls.File, s.Pos, s.Vel);
            if (SpellRecorder.On)
                s.Kinds = KindsOf(ls);
            if (Instant(s))
            {
                // Noita's lightning bolt: it strikes at once (its own flight is a frame or two)
                StrikeLightning(s);
                End(s, true);
                SpellRecorder.Gone(s.Id, s.Pos);
                return;
            }
            Live.Add(s);
            if (ComponentRuntime && !_runtimeLogged)
            {
                _runtimeLogged = true;
                Entry.Log("spell shots: component runtime on (TERRANOITA_RUNTIME=components)");
            }
            ScriptsAdd(s, ComponentRuntime);
            if (ComponentRuntime && s.Script != 0 && ls.Get("gravity") != 0)
                AddGravity(s, ls.Get("gravity"));
        }

        /// <summary>The component runtime (design/magic_plan.md PC-24, author's condition 1): with
        /// TERRANOITA_RUNTIME=components every shot is an entity of the script store and its components' fields drive it;
        /// off by default until probe-compare shows it at least as close to Noita as the old path.</summary>
        internal static readonly bool ComponentRuntime = Environment.GetEnvironmentVariable("TERRANOITA_RUNTIME") == "components";
        static bool _runtimeLogged;

        /// <summary>The cast's gravity (GRAVITY, GRAVITY_ANTI...) on the shot's VelocityComponent, added to the file's as the
        /// old path adds it to the sheet's.</summary>
        static void AddGravity(Shot s, float add)
        {
            var vc = _scripts.Components(s.Script, "VelocityComponent", false).FirstOrDefault();
            if (vc == null)
                return;
            float g = ShotFlight.From(vc.Get, Docs).GravityY;   // the file's, or Noita's documented default
            _scripts.SetField(s.Script, "VelocityComponent", "gravity_y", (g + add).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Component runtime: Noita's VelocityComponent from the store this frame (scripts' changes included);
        /// false when the shot has none (physics bodies), and the old path flies it.</summary>
        static bool FlyByComponents(Shot s)
        {
            var vc = _scripts?.Components(s.Script, "VelocityComponent", false).FirstOrDefault();
            if (vc == null)
                return false;
            var v = s.Vel * 60f / Px;
            ShotFlight.From(vc.Get, Docs).Step(ref v.X, ref v.Y);
            s.Vel = v * Px / 60f;
            return true;
        }

        static void Update()
        {
            if (Main.gameMenu)
                return;
            SlowFrames.Time("shot scripts", ScriptsUpdate);
            ForgetMarks();
            HoldElectrocuted();
            SlowFrames.Time("spell shots", StepAll);
        }

        static void StepAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var s = Live[i];
                bool gone = false;
                try { gone = Step(s); }
                catch (Exception ex) { Entry.Error("spell shot " + s.Def.Id, ex); gone = true; }
                if (!gone)
                    SpellRecorder.Sample(s.Id, s.Pos, s.Vel);   // after the move, as the Noita probe sees it
                else
                {
                    SpellRecorder.Gone(s.Id, s.Pos);
                    Live.Remove(s);
                    ScriptsRemove(s);
                }
            }
        }

        /// <summary>One frame; true when the shot is gone.</summary>
        static bool Step(Shot s)
        {
            if (s.Evicted)
                return true;
            if (s.Killed)
            {
                End(s, false);
                return true;
            }
            s.Age++;
            var ph = s.Phys;
            if (!(ComponentRuntime && s.Script != 0 && !ph.Thrown && FlyByComponents(s)))
            {
                s.Vel.Y += s.Gravity;
                // Noita's air_friction, also negative (rockets speed up: rocket_tier_3 -5.0). The probe measured Noita's
                // speed change over a frame: 1 - f/60 exactly (rocket 1.0833, spark bolt 0.9716; 2026-10-09)
                if (s.Friction != 0)
                    s.Vel *= Math.Max(0f, 1f - s.Friction / 60f);
                // Noita's terminal_velocity (px/s)
                if (ph.ApplyTerminal && ph.TerminalVelocity > 0)
                {
                    float max = ph.TerminalVelocity * Px / 60f;
                    if (s.Vel.LengthSquared() > max * max)
                        s.Vel = Vector2.Normalize(s.Vel) * max;
                }
            }
            // liquids: die_on_liquid_collision ends it (the iceball); liquid_drag slows it down (author: shots slow in water)
            if (InLiquid(s.Pos))
            {
                if (ph.DieOnLiquid)
                {
                    End(s, true);
                    return true;
                }
                if (ph.LiquidDrag > 0)
                    s.Vel *= Math.Max(0f, 1f - LiquidSlow * ph.LiquidDrag);
            }
            StepExtras(s);
            // fire spells set burnable blocks and walls they pass on fire, now and then (as Terraria's fire projectiles do)
            if (s.Fire && s.Age % 6 == 0 && Physics.Patches.On)
                Physics.Fire.IgniteArea(s.Pos, 12f, 0.25f);
            // shots cut grass, flowers, vines and pots like a sword does (author)
            NoitaActions.CutTiles(s.Pos, 6, Terraria.Enums.TileCuttingContext.AttackProjectile);
            // the timer of a timer trigger
            if (s.TriggerIn > 0 && --s.TriggerIn == 0)
                Release(s);
            // die_on_low_velocity (limit in px/s)
            if (s.DieOnLow && s.Age > 2 && s.Vel.Length() * 60f / Px < ph.LowVelocityLimit)
            {
                End(s, false);
                return true;
            }
            var next = s.Pos + s.Vel;
            if (!s.NoWorld && Collision.SolidCollision(next - new Vector2(2, 2), 4, 4))
            {
                if (ph.Thrown)
                    next = ThrownHit(s, next);   // a physics body never dies on the ground: its fuse (lifetime) ends it
                else if (s.PenetrateWorld)
                    next = s.Pos + s.Vel * ph.PenetrateCoeff;   // through the ground, slower inside it
                else if (s.Bounces > 0)
                {
                    s.Bounces--;
                    // bounce off the side it hit; bounce_energy: "when bouncing, velocity is multiplied by this"
                    // (component_documentation.txt), all of it: in the Noita probe a grenade's speed along the floor halves
                    // at each bounce too (249 -> 119 -> 104 -> 50 px/s, 2026-10-09)
                    if (Collision.SolidCollision(new Vector2(next.X, s.Pos.Y) - new Vector2(2, 2), 4, 4))
                        s.Vel.X = -s.Vel.X;
                    if (Collision.SolidCollision(new Vector2(s.Pos.X, next.Y) - new Vector2(2, 2), 4, 4))
                        s.Vel.Y = -s.Vel.Y;
                    s.Vel *= ph.BounceEnergy;
                    // bounce_fx_file: "this entity is created at the bounce position" (component_documentation.txt); the
                    // BOUNCE_* modifiers' scripts set it (bounce_explosion.lua), the probe saw Noita make one per bounce
                    var fx = BounceFx(s);
                    if (!string.IsNullOrEmpty(fx))
                        LoadEntity(fx, s.Pos, s.Owner);
                    return false;
                }
                else if (!s.DieOnCollision)
                {
                    // on_collision_die 0 (delayed spellcast, ball lightning...): it lives on against the ground, sliding
                    // along it and stopping where it cannot go
                    bool hitX = Collision.SolidCollision(new Vector2(next.X, s.Pos.Y) - new Vector2(2, 2), 4, 4);
                    bool hitY = Collision.SolidCollision(new Vector2(s.Pos.X, next.Y) - new Vector2(2, 2), 4, 4);
                    if (hitX)
                        s.Vel.X = 0;
                    if (hitY)
                        s.Vel.Y = 0;
                    if (!hitX && !hitY)
                        s.Vel = Vector2.Zero;
                    next = s.Pos + s.Vel;
                    if (Collision.SolidCollision(next - new Vector2(2, 2), 4, 4))
                        next = s.Pos;
                }
                else
                {
                    End(s, true);
                    return true;
                }
            }
            var from = s.Pos;   // the path this frame: fast shots (bullets, lances) must not jump past a creature
            s.Pos = next;
            // Noita's CellEaterComponent (black holes, discs): the ground around it goes; AreaDamageComponent: creatures in its box
            if (s.Def.EatRadius > 0 && s.Age % 3 == 0)
                EatAt(s.Pos, s.Def.EatRadius * Px, s.Def.EatProbability, Physics.Blast.PickPower(s.Owner));
            if (s.Def.AreaDamage > 0)
                AreaDamageAt(s, Math.Max(4f, s.Def.AreaHalf) * Px, s.Def.AreaDamage * 25f);
            if (s.Def.Material != "none" && s.Age % 8 == 0 && Physics.Patches.On)
                Physics.Fluids.Add((int)(s.Pos.X / 16), (int)(s.Pos.Y / 16), s.Def.Material, 12);
            // creatures
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly || n.dontTakeDamage || n.life <= 0 || s.Hit.Contains(i) ||
                    !Collision.CheckAABBvLineCollision(n.position - new Vector2(4, 4), n.Size + new Vector2(8, 8), from, s.Pos))
                    continue;
                Strike(s, n, s.Damage);
                if (ph.PullsToCaster)
                    PullToCaster(s, n);
                if (s.Def.DamageEveryFrames <= 0)
                    s.Hit.Add(i);
                if (!s.Penetrate && s.DieOnCollision)
                {
                    End(s, true);
                    return true;
                }
            }
            // friendly_fire (PIERCING_SHOT): the shot hurts its own caster too, once, after it has left the wand
            var me = s.Owner;
            if (s.FriendlyFire && !s.HitOwner && s.Age > FriendlyFireAfter && s.Damage > 0 && me != null && me.active && !me.dead &&
                Collision.CheckAABBvLineCollision(me.position, me.Size, from, s.Pos))
            {
                s.HitOwner = true;
                me.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(me.name + " was hit by their own spell."), (int)Math.Round(s.Damage), s.Vel.X >= 0 ? 1 : -1);
                if (!s.Penetrate && s.DieOnCollision)
                {
                    End(s, true);
                    return true;
                }
            }
            if (s.Def.DamageEveryFrames > 0 && s.Age % s.Def.DamageEveryFrames == 0)
                s.Hit.Clear();
            if (--s.Life <= 0)
            {
                End(s, false);
                return true;
            }
            return false;
        }

        /// <summary>A thrown physics body against the ground (constants and their probe data in SpellShots.Physics.cs): a
        /// real impact bounces, slower along the ground; a slow one rests and slides.</summary>
        static Vector2 ThrownHit(Shot s, Vector2 next)
        {
            bool hitX = Collision.SolidCollision(new Vector2(next.X, s.Pos.Y) - new Vector2(2, 2), 4, 4);
            bool hitY = Collision.SolidCollision(new Vector2(s.Pos.X, next.Y) - new Vector2(2, 2), 4, 4);
            if (!hitX && !hitY)
                hitY = true;   // a corner: as the ground
            float rest = ThrownRestSpeed * Px / 60f;
            if (hitY)
            {
                if (Math.Abs(s.Vel.Y) > rest)
                {
                    s.Vel.Y = -s.Vel.Y * ThrownBounce;
                    s.Vel.X *= ThrownImpactKeep;
                }
                else
                {
                    s.Vel.Y = 0;
                    s.Vel.X *= ThrownSlide;
                }
            }
            if (hitX)
                s.Vel.X = -s.Vel.X * ThrownBounce;
            var moved = s.Pos + s.Vel;
            return Collision.SolidCollision(moved - new Vector2(2, 2), 4, 4) ? s.Pos : moved;
        }

        static void EatAt(Vector2 pos, float r, float probability, int pickPower)
        {
            int cx = (int)(pos.X / 16), cy = (int)(pos.Y / 16), rt = (int)Math.Ceiling(r / 16f);
            float chance = Math.Min(1f, probability / 100f * 3f);   // every 3rd frame
            for (int x = cx - rt; x <= cx + rt; x++)
                for (int y = cy - rt; y <= cy + rt; y++)
                {
                    if (!Physics.Mats.InWorld(x, y) || Vector2.Distance(new Vector2(x * 16 + 8, y * 16 + 8), pos) > r + 8)
                        continue;
                    if (!Main.tile[x, y].active() || !Physics.Blast.Breakable(x, y, pickPower) || Main.rand.NextFloat() >= chance)
                        continue;
                    WorldGen.KillTile(x, y, false, false, true);   // eaten: nothing drops
                }
        }

        static readonly Dictionary<int, float> Owed = new Dictionary<int, float>();

        static void AreaDamageAt(Shot s, float half, float perFrame)
        {
            var box = new Rectangle((int)(s.Pos.X - half), (int)(s.Pos.Y - half), (int)(half * 2), (int)(half * 2));
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly || n.dontTakeDamage || n.life <= 0 || !n.Hitbox.Intersects(box))
                    continue;
                // per frame in Noita: added up and dealt every 10 frames, so the numbers stay readable
                Owed[i] = (Owed.TryGetValue(i, out float o) ? o : 0) + perFrame;
                if (s.Age % 10 == 0 && Owed[i] >= 1)
                {
                    Strike(s, n, Owed[i]);
                    Owed[i] = 0;
                }
            }
        }

        static readonly Dictionary<string, Dictionary<string, float>> FileKinds = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Probe only: a shot's hit damage by Noita's damage message (its file's damage and damage_by_type, the
        /// cast's damage_*_add on top), so the recorder reports a hit as Noita does: an arrow as $damage_slice. Terraria
        /// has no damage kinds: the damage dealt is the same.</summary>
        static Dictionary<string, float> KindsOf(LuaShot ls)
        {
            if (!FileKinds.TryGetValue(ls.File ?? "", out var file))
            {
                try { file = NoitaArt.ReadText(ls.File) == null ? new Dictionary<string, float>() : SpellProjectileFromEntity.DamageByMessage(NoitaEntityXml.Load(ls.File, NoitaArt.ReadText)); }
                catch (Exception ex) { Entry.Error("damage kinds " + ls.File, ex); file = new Dictionary<string, float>(); }
                FileKinds[ls.File ?? ""] = file;
            }
            var kinds = new Dictionary<string, float>(file, StringComparer.Ordinal);
            foreach (var kind in new[] { "projectile", "fire", "ice", "electricity", "slice", "curse", "drill", "melee" })
            {
                float add = ls.Get("damage_" + kind + "_add");
                if (add != 0)
                    kinds["$damage_" + kind] = (kinds.TryGetValue("$damage_" + kind, out var v) ? v : 0) + add;
            }
            return kinds;
        }

        static void Strike(Shot s, NPC n, float damage, string message = "$damage_projectile")
        {
            if (s.Owner == null)
                return;
            float total = s.Kinds == null || message != "$damage_projectile" ? 0 : s.Kinds.Values.Where(v => v > 0).Sum();
            if (total > 0)
                foreach (var kv in s.Kinds.Where(kv => kv.Value > 0))
                    SpellRecorder.Hit(n, s.Lua.File, damage * kv.Value / total, kv.Key);
            else
                SpellRecorder.Hit(n, s.Lua.File, damage, message);
            // Terraria's magic damage and crit (armour, potions, accessories, mana sickness) on top of Noita's numbers
            int dmg = (int)Math.Round(damage * s.Owner.magicDamage);
            if (dmg <= 0)
                return;
            bool crit = Main.rand.NextFloat() * 100f < s.Lua.Get("damage_critical_chance") + CritBoost(s, n) + s.Owner.magicCrit;
            s.Owner.ApplyDamageToNPC(n, dmg, Math.Max(0f, s.Knockback / 10f), s.Vel.X >= 0 ? 1 : -1, crit, null, 0, -1);
            if (s.Fire)
                n.AddBuff(BuffID.OnFire, 180);
            ApplyStatuses(s, n);
            // Noita's lightning_count (ELECTRIC_CHARGE...): the hit electrocutes (effect_electricity.xml, 40 frames)
            if (s.Lua.Get("lightning_count") > 0)
                Electrocute(n);
        }

        /// <summary>The shot hits something or runs out: its explosion, its payload.</summary>
        static void End(Shot s, bool hit)
        {
            TeleportOwner(s);
            if (s.Lua.Trigger == "hit_world" && hit || s.Lua.Trigger == "death" || s.Lua.Trigger == "timer" && s.TriggerIn > 0)
                Release(s);
            if (DebugTools.Testing)
                Entry.Log("SPELLS shot end " + System.IO.Path.GetFileNameWithoutExtension(s.Lua.File) + ": age " + s.Age + " of " + s.StartLife +
                          ", " + (Vector2.Distance(s.Origin, s.Pos) / 16).ToString("0.0") + " tiles from its start" + (hit ? ", hit" : s.Life <= 0 ? ", life out" : ""));
            // a fire spell that hits a block sets the burnable ones around it alight (author: fire weapons light wood)
            if (hit && s.Fire && Physics.Patches.On)
                Physics.Fire.IgniteArea(s.Pos, 24f, 0.8f);
            // Noita's ELECTRIC_CHARGE (lightning_count): "releases an electric charge on impact" (the engine's electricity,
            // misc/electricity.xml) - into the conducting liquid there
            if (hit && s.Lua.Get("lightning_count") > 0)
                Physics.Electricity.Emit(s.Pos, ElectricEnergy(ElectricityFile));
            if (s.Phys.Lightning != null)
                LightningBurst(s);   // a lightning projectile ends in its lightning trail and blast, whatever ends it
            else if (s.Radius > 0 && (hit || s.ExplodeOnDeath))
                Explode(s);
            else
                NoitaSound.PlayFirst(s.Def.Audio, s.Pos, "destroy");
        }

        static readonly Dictionary<string, bool> HittableFiles = new Dictionary<string, bool>();

        /// <summary>The projectile's entity has the tag "hittable": explosions and damage set it off.</summary>
        static bool Hittable(string file)
        {
            if (string.IsNullOrEmpty(file))
                return false;
            if (!HittableFiles.TryGetValue(file, out bool h))
            {
                var text = NoitaArt.ReadText(file) ?? "";
                var m = System.Text.RegularExpressions.Regex.Match(text, "<Entity[^>]*tags=\"([^\"]*)\"");
                HittableFiles[file] = h = m.Success && m.Groups[1].Value.Split(',').Any(x => x.Trim() == "hittable");
            }
            return h;
        }

        static void Release(Shot s)
        {
            if (s.Lua.Payload.Count == 0)
                return;
            var dir = s.Vel.LengthSquared() > 0.01f ? Vector2.Normalize(s.Vel) : new Vector2(1, 0);
            var payload = s.Lua.Payload.ToList();
            s.Lua.Payload.Clear();   // released once
            SpellRecorder.Parent = s.Lua.File;
            try { FireAll(payload, s.Pos - s.Vel, dir, s.Owner, null); }
            finally { SpellRecorder.Parent = ""; }
        }

        static readonly Dictionary<string, (float probability, string material)> FileCells = new Dictionary<string, (float, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Does the blast leave fire? Noita's config_explosion create_cell_probability leaves material cells (fire
        /// when the file names none, or "fire") where the blast tears up the ground. A model fitted to the probe (assumed,
        /// not in Noita's files): blasts that reached the floor set the target burning (BOMB 40, BOMB_HOLY 40, NUKE 5,
        /// NUKE_GIGA 5, METEOR fire), ROCKET's (10) at the target in the air did not.</summary>
        static bool LeavesFire(Shot s)
        {
            string file = s.Lua.File ?? "";
            if (!FileCells.TryGetValue(file, out var cells))
            {
                try { cells = NoitaArt.ReadText(file) == null ? (0f, "") : SpellProjectileFromEntity.BlastCells(NoitaEntityXml.Load(file, NoitaArt.ReadText)); }
                catch (Exception ex) { Entry.Error("blast cells " + file, ex); cells = (0f, ""); }
                FileCells[file] = cells;
            }
            if (cells.probability <= 0 || (cells.material != "" && cells.material != "fire"))
                return false;
            int r = (int)(s.Radius / 16f) + 1, cx = (int)(s.Pos.X / 16f), cy = (int)(s.Pos.Y / 16f);
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                    if (Physics.Mats.InWorld(x, y) && Physics.Mats.Solid(x, y) &&
                        Vector2.Distance(new Vector2(x * 16 + 8, y * 16 + 8), s.Pos) <= s.Radius + 8)
                        return true;
            return false;
        }

        static void Explode(Shot s)
        {
            float r = s.Radius;
            bool fire = LeavesFire(s);
            // "hittable" shots caught in it go off too (Noita's pipe bomb crystals, mines: DamageModel hp 0.5)
            foreach (var o in Live)
                if (o != s && o.Life > 1 && Hittable(o.Lua.File) && Vector2.Distance(o.Pos, s.Pos) <= r + 8)
                    o.Life = 1;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                // Noita: a blast hurts with its explosion damage only; the projectile's own damage is for a direct hit
                // (probe 2026-10-09: rocket_tier_3 near the target: explosion 5.2, no projectile damage)
                if (n.active && !n.friendly && !n.dontTakeDamage && n.life > 0 && Vector2.Distance(n.Center, s.Pos) <= r + n.width / 2f)
                {
                    Strike(s, n, s.ExplosionDamage, "$damage_explosion");
                    if (fire)
                        n.AddBuff(BuffID.OnFire, 180);
                }
            }
            // Noita: some explosions hurt their caster too (explosion_dont_damage_shooter = 0)
            var me = s.Owner;
            if (s.Def.HurtsShooter && me != null && me.active && !me.dead && s.ExplosionDamage > 0 &&
                Vector2.Distance(me.Center, s.Pos) <= r + me.width / 2f)
                me.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(me.name + " blew up."), (int)Math.Round(s.ExplosionDamage), me.Center.X < s.Pos.X ? -1 : 1);
            int dust = s.Fire || s.Radius >= 24 ? DustID.Torch : DustID.Smoke;
            int count = (int)Math.Min(80, 6 + r / 2);
            for (int k = 0; k < count; k++)
            {
                var v = Main.rand.NextVector2Circular(r / 10f, r / 10f);
                Dust.NewDust(s.Pos - new Vector2(4, 4), 8, 8, dust, v.X, v.Y);
            }
            if (!NoitaSound.Play(s.Def.ExplosionSound, s.Pos) && r >= 24)
                SoundEngine.PlaySound(SoundID.Item14, s.Pos);
            // big Noita explosions dig, as the enemies' do
            if (r >= 16 && Physics.Patches.On)
                Physics.Blast.Explode(s.Pos, r, s.Fire || fire, Physics.Blast.PickPower(s.Owner));
            Lighting.AddLight(s.Pos, 1f, 0.7f, 0.3f);
        }

        static void Draw()
        {
            if (Live.Count == 0)
                return;
            var sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            try
            {
                foreach (var s in Live)
                {
                    var art = NoitaArt.Get(s.Def.Sprite);
                    Lighting.AddLight(s.Pos, 0.35f, 0.3f, 0.5f);
                    if (art?.Texture == null)
                    {
                        if (!Main.gamePaused)
                            Dust.NewDustPerfect(s.Pos, DustID.PurpleTorch, Vector2.Zero, 0, default(Color), 1.1f).noGravity = true;
                        continue;
                    }
                    var anim = art.Sprite.Find("fireball", "default", "stand");
                    int fx = 0, fy = 0, fw = art.Texture.Width, fh = art.Texture.Height;
                    if (anim != null)
                        anim.FrameRect(anim.FrameAt(s.Age), out fx, out fy, out fw, out fh);
                    var origin = anim != null ? new Vector2(art.Sprite.OffsetX, art.Sprite.OffsetY) : new Vector2(fw / 2f, fh / 2f);
                    float rot = (float)Math.Atan2(s.Vel.Y, s.Vel.X);
                    sb.Draw(art.Texture, s.Pos - Main.screenPosition, new Rectangle(fx, fy, fw, fh), Color.White, rot, origin, Px, SpriteEffects.None, 0f);
                }
            }
            catch (Exception ex) { Entry.Error("spell shots draw", ex); }
            finally { sb.End(); }
        }

        public static void Clear()
        {
            Live.Clear();
            ScriptsClear();
        }

        [Hook("spell_shots_update")]
        [HarmonyPatch(typeof(Main), "UpdateWorld_Projectiles")]
        static class UpdatePatch
        {
            static void Postfix() => Update();
        }

        [Hook("spell_shots_draw")]
        [HarmonyPatch(typeof(Main), "DrawProjectiles")]
        static class DrawPatch
        {
            static void Postfix() => Draw();
        }
    }
}
