using System;
using System.Collections.Generic;
using System.Globalization;

namespace Terranoita.Noita
{
    /// <summary>
    /// A beam or lightning spell entity (no ProjectileComponent, or a lightning one): LaserEmitterComponent with its
    /// ConfigLaser `laser`, and LightningComponent with its `config_explosion`. Field names as Noita's; units as Noita's
    /// (pixels, frames, Noita damage). A value the file does not set comes from component_documentation.txt
    /// (ComponentFieldTypes when given, else the documented defaults below); ConfigLaser fields are not in the
    /// documentation we have, so an unset one stays null (the game decides, never a made-up number).
    /// </summary>
    public sealed class BeamDef
    {
        public string Id;

        // LaserEmitterComponent
        public bool HasLaser;
        public bool IsEmitting;
        public int EmitUntilFrame;
        public float LaserAngleAddRad;
        public float? MaxLength, BeamRadius, DamageToEntities, DamageToCells, MaxCellDurabilityToDestroy;
        public string BeamParticleType;
        public readonly Dictionary<string, string> Laser = new Dictionary<string, string>(StringComparer.Ordinal);   // every laser.* field as set

        // LightningComponent
        public bool HasLightning;
        public string SpriteLightningFile;
        public bool IsProjectile;
        public int ExplosionType;
        public int ArcLifetime;
        public float? ExplosionRadius, ExplosionDamage;
        public readonly Dictionary<string, string> Explosion = new Dictionary<string, string>(StringComparer.Ordinal); // config_explosion.* as set
    }

    public static class BeamFromEntity
    {
        // component_documentation.txt (the player's Noita, in design/sources/noita_facts.json _component_docs)
        static readonly Dictionary<string, string> Documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LaserEmitterComponent.is_emitting"] = "1",
            ["LaserEmitterComponent.emit_until_frame"] = "-1",
            ["LaserEmitterComponent.laser_angle_add_rad"] = "0",
            ["LightningComponent.sprite_lightning_file"] = "data/particles/lightning_ray.png",
            ["LightningComponent.is_projectile"] = "0",
            ["LightningComponent.explosion_type"] = "1",
            ["LightningComponent.arc_lifetime"] = "60",
        };

        /// <summary>The beam/lightning parts of an entity (it, then its children depth first; the first of each wins); null if none.</summary>
        public static BeamDef From(XmlEntity e, ComponentFieldTypes docs = null)
        {
            if (e == null)
                return null;
            var laser = Find(e, "LaserEmitterComponent", 0);
            var lightning = Find(e, "LightningComponent", 0);
            if (laser == null && lightning == null)
                return null;
            var b = new BeamDef { Id = e.Path };
            if (laser != null)
            {
                string V(string f) => laser.Get(f) ?? docs?.Default("LaserEmitterComponent", f) ?? Doc("LaserEmitterComponent", f);
                b.HasLaser = true;
                b.IsEmitting = Bool(V("is_emitting"));
                b.EmitUntilFrame = (int)(Float(V("emit_until_frame")) ?? -1);
                b.LaserAngleAddRad = Float(V("laser_angle_add_rad")) ?? 0;
                foreach (var kv in laser.Fields)
                    if (kv.Key.StartsWith("laser.", StringComparison.Ordinal))
                        b.Laser[kv.Key.Substring(6)] = kv.Value;
                b.MaxLength = Float(Get(b.Laser, "max_length"));
                b.BeamRadius = Float(Get(b.Laser, "beam_radius"));
                b.DamageToEntities = Float(Get(b.Laser, "damage_to_entities"));
                b.DamageToCells = Float(Get(b.Laser, "damage_to_cells"));
                b.MaxCellDurabilityToDestroy = Float(Get(b.Laser, "max_cell_durability_to_destroy"));
                b.BeamParticleType = Get(b.Laser, "beam_particle_type");
            }
            if (lightning != null)
            {
                string V(string f) => lightning.Get(f) ?? docs?.Default("LightningComponent", f) ?? Doc("LightningComponent", f);
                b.HasLightning = true;
                b.SpriteLightningFile = V("sprite_lightning_file");
                b.IsProjectile = Bool(V("is_projectile"));
                b.ExplosionType = (int)(Float(V("explosion_type")) ?? 1);
                b.ArcLifetime = (int)(Float(V("arc_lifetime")) ?? 60);
                foreach (var kv in lightning.Fields)
                    if (kv.Key.StartsWith("config_explosion.", StringComparison.Ordinal))
                        b.Explosion[kv.Key.Substring(17)] = kv.Value;
                b.ExplosionRadius = Float(Get(b.Explosion, "explosion_radius"));
                b.ExplosionDamage = Float(Get(b.Explosion, "damage"));
            }
            return b;
        }

        static XmlComponent Find(XmlEntity e, string type, int depth)
        {
            foreach (var c in e.Components)
                if (c.Type == type)
                    return c;
            if (depth >= 3)
                return null;
            foreach (var ch in e.Children)
            {
                var f = Find(ch, type, depth + 1);
                if (f != null)
                    return f;
            }
            return null;
        }

        static string Doc(string comp, string field) => Documented.TryGetValue(comp + "." + field, out var v) ? v : null;
        static string Get(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;
        static bool Bool(string s) => s == "1" || s == "true";
        static float? Float(string s) =>
            s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (float?)null;
    }

    /// <summary>Solid ground for a lightning path (the game: Terraria tiles, in the path's units).</summary>
    public interface ISolidGrid
    {
        bool Solid(int x, int y);
    }

    /// <summary>
    /// A jagged lightning path from a to b like Noita's arcs: one point every SegmentLength, each pushed sideways by a
    /// random amount up to Jitter x SegmentLength (the ends stay put); the same seed gives the same path. The path stops
    /// at the first solid point on it (checked every unit along each segment). Points go into a list the caller keeps.
    /// SegmentLength and Jitter are this mod's look, not Noita numbers.
    /// </summary>
    public static class LightningPath
    {
        public static bool Build(float ax, float ay, float bx, float by, int seed, List<(float x, float y)> into,
                                 ISolidGrid solid = null, float segmentLength = 8f, float jitter = 0.5f)
        {
            into.Clear();
            into.Add((ax, ay));
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f)
                return false;
            int segments = Math.Max(1, (int)Math.Ceiling(len / Math.Max(1f, segmentLength)));
            float nx = -dy / len, ny = dx / len;   // sideways
            uint state = (uint)seed * 2654435761u + 1u;
            float px = ax, py = ay;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float off = 0;
                if (i < segments)
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;   // xorshift32
                    off = ((state & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f) * jitter * (len / segments);
                }
                float qx = ax + dx * t + nx * off, qy = ay + dy * t + ny * off;
                if (solid != null && HitAlong(px, py, qx, qy, solid, out float hx, out float hy))
                {
                    into.Add((hx, hy));
                    return true;
                }
                into.Add((qx, qy));
                px = qx; py = qy;
            }
            return false;
        }

        static bool HitAlong(float x1, float y1, float x2, float y2, ISolidGrid solid, out float hx, out float hy)
        {
            float dx = x2 - x1, dy = y2 - y1;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy))));
            for (int s = 1; s <= steps; s++)
            {
                float x = x1 + dx * s / steps, y = y1 + dy * s / steps;
                if (solid.Solid((int)Math.Floor(x), (int)Math.Floor(y)))
                {
                    hx = x; hy = y;
                    return true;
                }
            }
            hx = x2; hy = y2;
            return false;
        }
    }
}
