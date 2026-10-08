using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// How Noita's engine moves a projectile, read from its own entity file (VelocityComponent, ProjectileComponent,
    /// LightningComponent) with the defaults of component_documentation.txt for what the file does not set:
    /// air_friction 0.55, liquid_drag 1 (slows in liquids), die_on_liquid_collision, die_on_low_velocity (limit 50 px/s),
    /// on_collision_die (0: the shot stays alive at a wall), bounce_energy 0.5, penetrate_world (+ velocity coeff 0.6),
    /// terminal_velocity 1000. Lightning projectiles (LightningComponent is_projectile) and the homebringer bolt
    /// (tag teleport_projectile_closer) are engine behaviour in Noita: done here.
    /// </summary>
    public static partial class SpellShots
    {
        sealed class ShotPhys
        {
            public float AirFriction, LiquidDrag, LowVelocityLimit, BounceEnergy, PenetrateCoeff, TerminalVelocity;
            public bool DieOnLiquid, DieOnLowVelocity, OnCollisionDie, PenetrateWorld, ApplyTerminal, PullsToCaster;
            public BeamDef Lightning;   // LightningComponent with is_projectile: a lightning trail and its blast
        }

        static readonly Dictionary<string, ShotPhys> Phys = new Dictionary<string, ShotPhys>(StringComparer.OrdinalIgnoreCase);
        static ComponentFieldTypes _docs;
        static bool _docsTried;

        /// <summary>Noita's component documentation (field types and defaults), from the player's Noita.</summary>
        static ComponentFieldTypes Docs
        {
            get
            {
                if (_docs == null && !_docsTried && NoitaArt.Ready)
                {
                    _docsTried = true;
                    try
                    {
                        string doc = NoitaArt.GameDir == null ? null : Path.Combine(NoitaArt.GameDir, "tools_modding", "component_documentation.txt");
                        if (doc != null && File.Exists(doc))
                            _docs = ComponentFieldTypes.Parse(File.ReadAllText(doc));
                        else
                            Entry.Log("spell shots: no component_documentation.txt, Noita's defaults from this mod's copy");
                    }
                    catch (Exception ex) { Entry.Error("component docs", ex); }
                }
                return _docs;
            }
        }

        // component_documentation.txt (Noita 2024), used when the player's copy is missing
        static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VelocityComponent.air_friction"] = "0.55", ["VelocityComponent.liquid_drag"] = "1",
            ["VelocityComponent.terminal_velocity"] = "1000", ["VelocityComponent.apply_terminal_velocity"] = "1",
            ["ProjectileComponent.die_on_liquid_collision"] = "0", ["ProjectileComponent.die_on_low_velocity"] = "0",
            ["ProjectileComponent.die_on_low_velocity_limit"] = "50", ["ProjectileComponent.on_collision_die"] = "1",
            ["ProjectileComponent.bounce_energy"] = "0.5", ["ProjectileComponent.penetrate_world"] = "0",
            ["ProjectileComponent.penetrate_world_velocity_coeff"] = "0.6",
        };

        static ShotPhys PhysOf(string file)
        {
            file = file ?? "";
            if (Phys.TryGetValue(file, out var p))
                return p;
            XmlEntity x = null;
            try
            {
                if (NoitaArt.ReadText(file) != null)
                    x = NoitaEntityXml.Load(file, NoitaArt.ReadText);
            }
            catch (Exception ex) { Entry.Error("spell projectile physics " + file, ex); }
            var vc = x?.Components.FirstOrDefault(c => c.Type == "VelocityComponent");
            var pc = x?.Components.FirstOrDefault(c => c.Type == "ProjectileComponent");
            string V(XmlComponent c, string type, string field) =>
                c?.Get(field) ?? Docs?.Default(type, field) ?? (Fallback.TryGetValue(type + "." + field, out var d) ? d : null);
            float F(XmlComponent c, string type, string field, float fallback) =>
                float.TryParse(V(c, type, field), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            bool B(XmlComponent c, string type, string field, bool fallback)
            {
                string s = V(c, type, field);
                return s == null ? fallback : s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            p = new ShotPhys
            {
                AirFriction = F(vc, "VelocityComponent", "air_friction", 0.55f),
                LiquidDrag = F(vc, "VelocityComponent", "liquid_drag", 1f),
                TerminalVelocity = F(vc, "VelocityComponent", "terminal_velocity", 1000f),
                ApplyTerminal = B(vc, "VelocityComponent", "apply_terminal_velocity", true),
                DieOnLiquid = B(pc, "ProjectileComponent", "die_on_liquid_collision", false),
                DieOnLowVelocity = B(pc, "ProjectileComponent", "die_on_low_velocity", false),
                LowVelocityLimit = F(pc, "ProjectileComponent", "die_on_low_velocity_limit", 50f),
                OnCollisionDie = B(pc, "ProjectileComponent", "on_collision_die", true),
                BounceEnergy = F(pc, "ProjectileComponent", "bounce_energy", 0.5f),
                PenetrateWorld = B(pc, "ProjectileComponent", "penetrate_world", false),
                PenetrateCoeff = F(pc, "ProjectileComponent", "penetrate_world_velocity_coeff", 0.6f),
                PullsToCaster = x != null && (x.Tags ?? "").Split(',').Any(t => t.Trim() == "teleport_projectile_closer"),
            };
            try
            {
                var beam = x == null ? null : BeamFromEntity.From(x, Docs);
                if (beam != null && beam.HasLightning && beam.IsProjectile)
                    p.Lightning = beam;
            }
            catch (Exception ex) { Entry.Error("spell lightning " + file, ex); }
            Phys[file] = p;
            return p;
        }

        // ---- liquids ----

        const float LiquidSlow = 0.12f;   // ours: share of the speed a shot loses each frame in a liquid at liquid_drag 1

        static bool InLiquid(Vector2 pos) => Physics.Fluids.LiquidAt((int)(pos.X / 16), (int)(pos.Y / 16));

        // ---- lightning ----

        // ours: how far Noita's instant lightning bolt (lightning.xml: speed 60, lifetime 2 frames, LightningComponent
        // is_projectile) strikes - the wraith's lightning attack reaches 300 px (attacks.json range_tiles 56.2)
        const float LightningReach = 300f;
        // Noita's ConfigExplosion damage when a config does not set it (not in component_documentation.txt): most spell
        // files set damage="0" themselves; lightning.xml leaves it out and strikes hard (author: "very weak" before)
        const float ExplosionDefaultDamage = 5f;

        /// <summary>A lightning projectile that ends where it is fired (its own flight is a frame or two): Noita strikes
        /// at once along the aim, to the first wall or creature.</summary>
        static bool Instant(Shot s) => s.Phys.Lightning != null && s.Life <= 5;

        static void StrikeLightning(Shot s)
        {
            var dir = s.Vel.LengthSquared() > 0.0001f ? Vector2.Normalize(s.Vel) : new Vector2(s.Owner?.direction ?? 1, 0);
            float reach = LightningReach * Px;
            var end = s.Pos + dir * reach;
            NPC hit = null;
            for (float k = 0; k <= reach; k += 6)
            {
                var at = s.Pos + dir * k;
                int tx = (int)(at.X / 16), ty = (int)(at.Y / 16);
                if (!Physics.Mats.InWorld(tx, ty) || Physics.Mats.Solid(tx, ty))
                {
                    end = at;
                    break;
                }
                for (int i = 0; i < Main.maxNPCs && hit == null; i++)
                {
                    var n = Main.npc[i];
                    if (n.active && !n.friendly && !n.dontTakeDamage && n.life > 0 && n.Hitbox.Contains((int)at.X, (int)at.Y))
                        hit = n;
                }
                if (hit != null)
                {
                    end = at;
                    break;
                }
            }
            s.Pos = end;
            if (hit != null)
            {
                Strike(s, hit, s.Damage);
                s.Hit.Add(hit.whoAmI);
            }
            if (DebugTools.Testing)
                Entry.Log("SPELLS lightning " + System.IO.Path.GetFileNameWithoutExtension(s.Lua.File) + ": " +
                          (Vector2.Distance(s.Origin, end) / 16).ToString("0.0") + " tiles" + (hit != null ? ", hit " + hit.TypeName : ""));
        }

        /// <summary>The lightning trail from where the shot started to where it ends, and its blast (LightningComponent
        /// explosion_type 1 = lightning trail; its own config_explosion).</summary>
        static void LightningBurst(Shot s)
        {
            var l = s.Phys.Lightning;
            var path = new List<(float x, float y)>();
            LightningPath.Build(s.Origin.X, s.Origin.Y, s.Pos.X, s.Pos.Y, s.Id, path, null, 18f, 0.6f);
            for (int i = 1; i < path.Count; i++)
            {
                var a = new Vector2(path[i - 1].x, path[i - 1].y);
                var b = new Vector2(path[i].x, path[i].y);
                float len = Vector2.Distance(a, b);
                for (float k = 0; k < len; k += 5)
                    Dust.NewDustPerfect(Vector2.Lerp(a, b, k / Math.Max(1f, len)), DustID.Electric, Vector2.Zero, 0, default(Color), 0.9f).noGravity = true;
                Lighting.AddLight(b, 0.4f, 0.7f, 1f);
            }
            if (l.ExplosionRadius.HasValue)
                s.Radius = Math.Max(0, l.ExplosionRadius.Value + s.Lua.Get("explosion_radius")) * Px;
            s.ExplosionDamage = Math.Max(0, (l.ExplosionDamage ?? ExplosionDefaultDamage) + s.Lua.Get("damage_explosion_add")) * 25f;
            if (s.NullDamage)
                s.ExplosionDamage = 0;
            if (s.Radius > 0)
                Explode(s);
        }

        // ---- the homebringer bolt ----

        /// <summary>Noita's homebringer teleport bolt (tag teleport_projectile_closer): the creature it hits is taken to
        /// where the bolt was fired, next to its caster.</summary>
        static void PullToCaster(Shot s, NPC n)
        {
            if (n.boss || !n.active)
                return;
            var to = s.Origin - new Vector2(n.width / 2f, n.height / 2f);
            var step = s.Pos - s.Origin;
            step = step.LengthSquared() > 1 ? Vector2.Normalize(step) * 8f : Vector2.Zero;
            for (int k = 0; k < 20 && Collision.SolidCollision(to, n.width, n.height); k++)
                to += step;
            if (Collision.SolidCollision(to, n.width, n.height))
                return;
            for (int d = 0; d < 12; d++)
                Dust.NewDust(n.position, n.width, n.height, DustID.PurpleTorch);
            n.position = to;
            n.velocity = Vector2.Zero;
            n.netUpdate = true;
            for (int d = 0; d < 12; d++)
                Dust.NewDust(n.position, n.width, n.height, DustID.PurpleTorch);
            if (DebugTools.Testing)
                Entry.Log("SPELLS pulled " + n.TypeName + " to the caster");
        }

        static bool Bool(string value, out bool b)
        {
            b = value == "1" || value != null && value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return b || value == "0" || value != null && value.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
