using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// A Noita entity XML with its &lt;Base file="..."&gt; includes resolved: the base's components come first,
    /// with the override elements written inside &lt;Base&gt; merged onto the first component of the same name.
    /// </summary>
    public sealed class NoitaEntity
    {
        public string Path;
        public string Name;
        public string Tags;
        public readonly List<NxmlNode> Components = new List<NxmlNode>();
        public readonly List<NoitaEntity> Children = new List<NoitaEntity>();

        public NxmlNode Component(string name) => Components.FirstOrDefault(c => c.Name == name);
        public IEnumerable<NxmlNode> ComponentsNamed(string name) => Components.Where(c => c.Name == name);

        public static NoitaEntity Load(Func<string, string> readText, string path) => Load(readText, path, 0);

        static NoitaEntity Load(Func<string, string> readText, string path, int depth)
        {
            if (depth > 16)
                throw new InvalidOperationException("Base include loop at " + path);
            string text = readText(path) ?? throw new System.IO.FileNotFoundException("Not found: " + path, path);
            var root = Nxml.ParseRoot(text) ?? throw new System.IO.InvalidDataException("Empty entity file: " + path);
            return FromNode(readText, root, path, depth);
        }

        static NoitaEntity FromNode(Func<string, string> readText, NxmlNode root, string path, int depth)
        {
            var e = new NoitaEntity { Path = path, Name = root.Attr("name"), Tags = root.Attr("tags") };
            foreach (var child in root.Children)
            {
                if (child.Name == "Base")
                {
                    string file = child.Attr("file");
                    if (string.IsNullOrEmpty(file))
                        continue;
                    var b = Load(readText, file, depth + 1);
                    var baseComponents = b.Components.Select(c => c.Clone()).ToList();
                    var used = new HashSet<NxmlNode>();
                    foreach (var over in child.Children)
                    {
                        var target = baseComponents.FirstOrDefault(c => c.Name == over.Name && !used.Contains(c));
                        if (target != null)
                        {
                            target.MergeFrom(over);
                            used.Add(target);
                        }
                        else
                        {
                            baseComponents.Add(over.Clone());
                        }
                    }
                    e.Components.AddRange(baseComponents);
                    e.Children.AddRange(b.Children);
                    if (e.Name == null)
                        e.Name = b.Name;
                    if (string.IsNullOrEmpty(e.Tags))
                        e.Tags = b.Tags;
                    else if (!string.IsNullOrEmpty(b.Tags))
                        e.Tags = b.Tags + "," + e.Tags;
                }
                else if (child.Name == "Entity")
                {
                    e.Children.Add(FromNode(readText, child, path, depth + 1));
                }
                else
                {
                    e.Components.Add(child);
                }
            }
            return e;
        }
    }

    /// <summary>The facts the design sheets need from an enemy's entity XML.</summary>
    public sealed class EnemyFacts
    {
        public string Entity;
        public float? DisplayHp;            // DamageModelComponent hp x 25
        public string Sprite;               // SpriteComponent image_file
        public int[] HitboxNoitaPx;         // [w, h] in Noita pixels
        public string NameKey;              // $animal_...
        public readonly List<RangedAttackFacts> Ranged = new List<RangedAttackFacts>();
        public int? MeleeFramesBetween;
        public float? MeleeRange;
        public readonly Dictionary<string, float> DamageMultipliers = new Dictionary<string, float>();
        public readonly Dictionary<string, string> AnimalAi = new Dictionary<string, string>();

        public static EnemyFacts From(NoitaEntity e)
        {
            var f = new EnemyFacts { Entity = e.Path, NameKey = e.Name };
            var dm = e.Component("DamageModelComponent");
            if (dm?.Float("hp") is float hp)
                f.DisplayHp = (float)Math.Round(hp * 25f, 2);
            var dmt = dm?.Child("damage_multipliers");
            if (dmt != null)
                foreach (var kv in dmt.Attributes)
                    if (float.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m))
                        f.DamageMultipliers[kv.Key] = m;

            f.Sprite = PickSprite(e.ComponentsNamed("SpriteComponent"));

            var hb = e.Component("HitboxComponent");
            if (hb != null && hb.Float("aabb_max_x").HasValue)
                f.HitboxNoitaPx = Box(hb, "aabb_min_x", "aabb_max_x", "aabb_min_y", "aabb_max_y");
            else
            {
                var cd = e.Component("CharacterDataComponent");
                if (cd != null && cd.Float("collision_aabb_max_x").HasValue)
                    f.HitboxNoitaPx = Box(cd, "collision_aabb_min_x", "collision_aabb_max_x", "collision_aabb_min_y", "collision_aabb_max_y");
            }

            var ai = e.Component("AnimalAIComponent");
            if (ai != null)
            {
                foreach (var kv in ai.Attributes)
                    f.AnimalAi[kv.Key] = kv.Value;
                if (ai.Attr("attack_ranged_enabled", "0") == "1" && !string.IsNullOrEmpty(ai.Attr("attack_ranged_entity_file")))
                    f.Ranged.Add(RangedAttackFacts.From(ai, "animal_ai"));
                f.MeleeFramesBetween = ai.Int("attack_melee_frames_between");
                f.MeleeRange = ai.Float("attack_melee_max_distance");
            }
            foreach (var atk in e.ComponentsNamed("AIAttackComponent"))
                if (!string.IsNullOrEmpty(atk.Attr("attack_ranged_entity_file")))
                    f.Ranged.Add(RangedAttackFacts.From(atk, "ai_attack"));
            return f;
        }

        static int[] Box(NxmlNode n, string minX, string maxX, string minY, string maxY) => new[]
        {
            (int)Math.Round((n.Float(maxX) ?? 0) - (n.Float(minX) ?? 0)),
            (int)Math.Round((n.Float(maxY) ?? 0) - (n.Float(minY) ?? 0)),
        };

        static string PickSprite(IEnumerable<NxmlNode> sprites)
        {
            string fallback = null;
            foreach (var s in sprites)
            {
                string file = s.Attr("image_file");
                if (string.IsNullOrEmpty(file))
                    continue;
                fallback = fallback ?? file;
                bool effect = s.Attr("emissive") == "1" || s.Attr("additive") == "1" ||
                              (s.Attr("_tags") ?? "").Contains("light") || file.Contains("_emissive") || file.Contains("light");
                if (!effect)
                    return file;
            }
            return fallback;
        }
    }

    public sealed class RangedAttackFacts
    {
        public string Source;               // animal_ai | ai_attack
        public string EntityFile;
        public int? FramesBetween;
        public float? MinDistance, MaxDistance;
        public int? CountMin, CountMax;

        public static RangedAttackFacts From(NxmlNode n, string source) => new RangedAttackFacts
        {
            Source = source,
            EntityFile = n.Attr("attack_ranged_entity_file"),
            FramesBetween = n.Int("attack_ranged_frames_between") ?? n.Int("frames_between"),
            MinDistance = n.Float("attack_ranged_min_distance"),
            MaxDistance = n.Float("attack_ranged_max_distance"),
            CountMin = n.Int("attack_ranged_entity_count_min"),
            CountMax = n.Int("attack_ranged_entity_count_max"),
        };
    }

    /// <summary>The facts the projectiles sheet needs from a projectile entity XML.</summary>
    public sealed class ProjectileFacts
    {
        public string Entity;
        public string Sprite;
        public float? SpeedMin, SpeedMax;   // Noita px per second
        public float? GravityY;             // Noita px per second^2
        public int? LifetimeFrames;
        public float? ExplosionRadius;      // Noita px
        public float? Damage;               // Noita internal (x25 = displayed)

        public static ProjectileFacts From(NoitaEntity e)
        {
            var f = new ProjectileFacts { Entity = e.Path };
            var p = e.Component("ProjectileComponent");
            if (p != null)
            {
                f.SpeedMin = p.Float("speed_min");
                f.SpeedMax = p.Float("speed_max");
                f.LifetimeFrames = p.Int("lifetime");
                f.Damage = p.Float("damage");
                f.ExplosionRadius = p.Child("config_explosion")?.Float("explosion_radius");
            }
            f.GravityY = e.Component("VelocityComponent")?.Float("gravity_y");
            foreach (var s in e.ComponentsNamed("SpriteComponent"))
                if (!string.IsNullOrEmpty(s.Attr("image_file")))
                {
                    f.Sprite = s.Attr("image_file");
                    break;
                }
            return f;
        }
    }

    /// <summary>Unit conversions between Noita and Terraria (Noita art is drawn at 2x, like Terraria's).</summary>
    public static class Units
    {
        public const float PixelScale = 2f;          // 1 Noita pixel = 2 Terraria world pixels
        public const float TerrariaTile = 16f;       // Terraria world pixels per tile
        public const float FramesPerSecond = 60f;

        public static float SpeedToTerraria(float noitaPxPerSecond) => noitaPxPerSecond * PixelScale / FramesPerSecond;
        public static float GravityToTerraria(float noitaPxPerSecond2) => noitaPxPerSecond2 * PixelScale / (FramesPerSecond * FramesPerSecond);
        public static float PxToTiles(float noitaPx) => noitaPx * PixelScale / TerrariaTile;
    }
}
