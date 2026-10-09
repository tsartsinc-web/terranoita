using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terranoita.Generated;

namespace Terranoita.Noita
{
    /// <summary>
    /// A spell projectile row made at runtime from any Noita entity file (EntityLoad of files the sheet does not have).
    /// The same rules as tools/apply_spells.py shot_row over tncli's facts (ProjectileFacts + EntityDump), column by
    /// column: first component of a type wins (the entity, then its children depth first, 3 levels deep), the first
    /// nested config_explosion / damage_by_type wins, missing attributes fall back as the facts did (speed 60, gravity
    /// 400 when the entity itself has a VelocityComponent, lifetime -1). No ProjectileComponent: null.
    /// </summary>
    public static class SpellProjectileFromEntity
    {
        // EntityDump leaves these out, so they never win a "first of its type"
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "SpriteAnimatorComponent", "SpriteParticleEmitterComponent", "SpriteOffsetAnimatorComponent",
            "SpriteStainsComponent", "InheritTransformComponent", "HotspotComponent", "AudioLoopComponent",
            "AudioListenerComponent", "UIIconComponent", "UIInfoComponent", "CameraBoundComponent",
            "StatusEffectDataComponent", "ItemComponent", "InventoryComponent", "Inventory2Component",
            "VerletWorldJointComponent", "PhysicsJointComponent", "PhysicsJoint2Component",
        };

        /// <summary>A shot's hit damage as Noita reports it, one hit per kind: ProjectileComponent damage as
        /// "$damage_projectile", each damage_by_type field as "$damage_&lt;type&gt;" (the Noita probe: an arrow hits with
        /// $damage_slice only); kinds with no damage left out. Noita damage units.</summary>
        /// <summary>What a shot's blast leaves (config_explosion): material cells (create_cell_probability, percent;
        /// create_cell_material, "" when the file sets none) and the projectile files of its load_this_entity.</summary>
        public sealed class BlastInfo
        {
            public float CellProbability;
            public string CellMaterial = "";
            public string[] LoadsShots = new string[0];
        }

        public static BlastInfo Blast(XmlEntity e)
        {
            var b = new BlastInfo();
            if (e == null)
                return b;
            var comps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Walk(e, comps, 0);
            if (!comps.TryGetValue("config_explosion", out var ex))
                return b;
            b.CellProbability = Num(Get(ex, "create_cell_probability"));
            b.CellMaterial = Get(ex, "create_cell_material") ?? "";
            b.LoadsShots = (Get(ex, "load_this_entity") ?? "").Split(',').Select(x => x.Trim())
                .Where(x => x.StartsWith("data/entities/projectiles/", StringComparison.Ordinal)).ToArray();
            return b;
        }

        public static Dictionary<string, float> DamageByMessage(XmlEntity e)
        {
            var by = new Dictionary<string, float>(StringComparer.Ordinal);
            if (e == null)
                return by;
            var comps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Walk(e, comps, 0);
            if (!comps.TryGetValue("ProjectileComponent", out var pc))
                return by;
            float d = Num(Get(pc, "damage"));
            if (d != 0)
                by["$damage_projectile"] = d;
            if (comps.TryGetValue("damage_by_type", out var dbt))
                foreach (var kv in dbt)
                    if (!kv.Key.StartsWith("_", StringComparison.Ordinal) && Num(kv.Value) != 0)
                        by["$damage_" + kv.Key] = Num(kv.Value);
            return by;
        }

        public static SpellProjectileDef From(XmlEntity e)
        {
            if (e == null)
                return null;
            var comps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Walk(e, comps, 0);
            if (!comps.TryGetValue("ProjectileComponent", out var pc))
                return null;
            comps.TryGetValue("VelocityComponent", out var vc);
            comps.TryGetValue("config_explosion", out var ex);
            comps.TryGetValue("damage_by_type", out var dbt);
            comps.TryGetValue("ParticleEmitterComponent", out var emit);
            comps.TryGetValue("CellEaterComponent", out var eater);
            comps.TryGetValue("AreaDamageComponent", out var area);
            pc = pc ?? new Dictionary<string, string>();
            var none = new Dictionary<string, string>();
            vc = vc ?? none; ex = ex ?? none; dbt = dbt ?? none; emit = emit ?? none; eater = eater ?? none; area = area ?? none;

            // the facts' values (ProjectileFacts: the entity's own components only)
            var rootPc = e.Components.FirstOrDefault(c => c.Type == "ProjectileComponent");
            bool rootVelocity = e.Components.Any(c => c.Type == "VelocityComponent");
            float? factSpeed = rootPc == null ? (float?)null : Float(rootPc.Get("speed_min")) ?? ProjectileDefaults.Speed;
            float? factSpeedMax = rootPc == null ? (float?)null : Float(rootPc.Get("speed_max")) ?? ProjectileDefaults.Speed;
            float? factGravity = rootVelocity ? Float(e.Components.First(c => c.Type == "VelocityComponent").Get("gravity_y")) ?? ProjectileDefaults.GravityY : (float?)null;
            int? factLifetime = rootPc == null ? (int?)null : Int(rootPc.Get("lifetime"));
            float? factDamage = rootPc == null ? (float?)null : Float(rootPc.Get("damage"));
            float? factRadius = rootPc == null ? (float?)null : Float(rootPc.Get("config_explosion.explosion_radius"));
            string factSound = rootPc?.Get("config_explosion.audio_event_name");
            // a LightningComponent that moves as a projectile ends in its own blast (its config_explosion)
            var lightning = FirstLightning(e, 0);

            return new SpellProjectileDef
            {
                Id = e.Path,
                Sprite = Or(SpriteOf(e), "none"),
                SpeedMin = pc.TryGetValue("speed_min", out var s1) ? Num(s1) : factSpeed ?? 0,
                SpeedMax = pc.TryGetValue("speed_max", out var s2) ? Num(s2) : factSpeedMax ?? 0,
                SpreadRad = Num(Get(pc, "direction_random_rad")),
                Gravity = vc.TryGetValue("gravity_y", out var g) ? Num(g) : factGravity ?? 0,
                AirFriction = Num(Get(vc, "air_friction", Doc.AirFriction)),
                LiquidDrag = Num(Get(vc, "liquid_drag", Doc.LiquidDrag), 1),
                TerminalVelocity = Flag(Get(vc, "apply_terminal_velocity", Doc.ApplyTerminal)) ? Num(Get(vc, "terminal_velocity", Doc.TerminalVelocity), 1000) : -1,
                Lifetime = (int)(pc.TryGetValue("lifetime", out var lt) ? Num(lt, -1) : factLifetime ?? -1),
                LifetimeRandom = (int)Num(Get(pc, "lifetime_randomness")),
                Damage = pc.TryGetValue("damage", out var d) ? Num(d) : factDamage ?? 0,
                TypedDamage = dbt.Where(kv => !kv.Key.StartsWith("_", StringComparison.Ordinal)).Sum(kv => Num(kv.Value)),
                FireDamage = Num(Get(dbt, "fire")),
                HurtsShooter = Get(pc, "explosion_dont_damage_shooter", "1") == "0",
                DamageEveryFrames = (int)Num(Get(pc, "damage_every_x_frames")),
                ExplosionRadius = ex.TryGetValue("explosion_radius", out var er) ? Num(er) : factRadius ?? 0,
                ExplosionDamage = Num(Get(ex, "damage")),
                ExplodeOnDeath = Get(pc, "on_death_explode", "0") == "1" || Get(pc, "on_lifetime_out_explode", "0") == "1",
                DieOnHit = Get(pc, "on_collision_die", "1") == "1",
                DieOnLiquid = Flag(Get(pc, "die_on_liquid_collision", "0")),
                DieOnLowVelocity = Flag(Get(pc, "die_on_low_velocity", "0")),
                LowVelocityLimit = Num(Get(pc, "die_on_low_velocity_limit", Doc.LowVelocityLimit), 50),
                BounceEnergy = Num(Get(pc, "bounce_energy", Doc.BounceEnergy), 0.5f),
                PenetrateWorld = Flag(Get(pc, "penetrate_world", "0")),
                Penetrate = Get(pc, "penetrate_entities", "0") == "1",
                Bounces = (int)Num(Get(pc, "bounces_left")),
                CollideWithWorld = Get(pc, "collide_with_world", "1") == "1",
                Knockback = Num(Get(pc, "knockback_force")),
                Material = Or(Get(emit, "emitted_material_name"), "none"),
                EatRadius = Num(Get(eater, "radius")),
                EatProbability = Num(Get(eater, "eat_probability"), 100),
                AreaDamage = Num(Get(area, "damage_per_frame")),
                AreaHalf = Num(Get(area, "aabb_max.x")),
                ExplosionMaterial = Or(Get(ex, "create_cell_material"), "none"),
                Audio = Or(AudioOf(e), "none"),
                ExplosionSound = Or(factSound, "none"),
                LightningRadius = lightning == null ? 0 : Num(lightning.Get("config_explosion.explosion_radius")),
                LightningDamage = lightning == null ? 0 : Num(lightning.Get("config_explosion.damage"), LightningDamageDefault),
                Stage = "3",
            };
        }

        /// <summary>Components and their nested objects by type, the first one of each winning (comps_of).</summary>
        static void Walk(XmlEntity e, Dictionary<string, Dictionary<string, string>> into, int depth)
        {
            foreach (var c in e.Components)
            {
                if (Skip.Contains(c.Type))
                    continue;
                var top = new Dictionary<string, string>(StringComparer.Ordinal);
                var nested = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (var kv in c.Fields)
                {
                    // "config_explosion.damage" (a nested element) -> object config_explosion, field damage;
                    // a dotted attribute ("aabb_max.x") also stays on the component under its full name
                    top[kv.Key] = kv.Value;
                    int dot = kv.Key.LastIndexOf('.');
                    if (dot <= 0)
                        continue;
                    string path = kv.Key.Substring(0, dot);
                    string obj = path.Substring(path.LastIndexOf('.') + 1);
                    if (!nested.TryGetValue(obj, out var f))
                    {
                        nested[obj] = f = new Dictionary<string, string>(StringComparer.Ordinal);
                        order.Add(obj);
                    }
                    f[kv.Key.Substring(dot + 1)] = kv.Value;
                }
                if (!into.ContainsKey(c.Type))
                    into[c.Type] = top;
                foreach (var obj in order)
                    if (!into.ContainsKey(obj))
                        into[obj] = nested[obj];
            }
            if (depth >= 3)
                return;
            foreach (var child in e.Children)
                Walk(child, into, depth + 1);
        }

        /// <summary>Noita's documented defaults (component_documentation.txt) for fields a file leaves unset, as
        /// apply_spells.py FALLBACK_DOCS.</summary>
        static class Doc
        {
            public const string AirFriction = "0.55", LiquidDrag = "1", TerminalVelocity = "1000", ApplyTerminal = "1",
                LowVelocityLimit = "50", BounceEnergy = "0.5";
        }

        /// <summary>ConfigExplosion.damage is not documented; the runtime takes 5 for a lightning blast (CHECK in Noita).</summary>
        public const float LightningDamageDefault = 5f;

        static XmlComponent FirstLightning(XmlEntity e, int depth)
        {
            foreach (var c in e.Components)
                if (c.Type == "LightningComponent" && Flag(c.Get("is_projectile")))
                    return c;
            if (depth >= 3)
                return null;
            foreach (var child in e.Children)
            {
                var l = FirstLightning(child, depth + 1);
                if (l != null)
                    return l;
            }
            return null;
        }

        static bool Flag(string s) => s != null && (s.Trim() == "1" || s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

        static string SpriteOf(XmlEntity e)
        {
            foreach (var c in e.Components)
                if (c.Type == "SpriteComponent" && !string.IsNullOrEmpty(c.Get("image_file")))
                    return c.Get("image_file");
            // thrown physics objects (tnt): the physics body's image
            foreach (var c in e.Components)
                if (c.Type == "PhysicsImageShapeComponent" && !string.IsNullOrEmpty(c.Get("image_file")))
                    return c.Get("image_file");
            return null;
        }

        static string AudioOf(XmlEntity e)
        {
            string best = null;
            foreach (var c in e.Components)
            {
                string root = c.Type == "AudioComponent" ? c.Get("event_root") : null;
                if (!string.IsNullOrEmpty(root) && (best == null || root.Length > best.Length))   // the most specific folder
                    best = root;
            }
            return best;
        }

        static string Get(Dictionary<string, string> d, string k, string fallback = null) => d.TryGetValue(k, out var v) ? v : fallback;
        static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        // Python's float(): a bad or missing value is the default
        static float Num(string s, float fallback = 0) =>
            s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static float? Float(string s) =>
            s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (float?)null;
        static int? Int(string s) =>
            s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
    }
}
