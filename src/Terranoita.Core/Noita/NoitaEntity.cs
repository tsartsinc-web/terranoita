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
        public bool DashEnabled;
        public int? DashFramesBetween;
        public float? DashDistance, DashSpeed, DashDamage;
        public MovementFacts Movement;
        public readonly List<string> AudioRoots = new List<string>();   // AudioComponent event_root, e.g. animals/zombie
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
            // physics bodies (crystals, lukki, chests) look like their body's image, sometimes on a child entity
            if (f.Sprite == null)
                f.Sprite = PhysicsImage(e) ?? e.Children.Select(c => PickSprite(c.ComponentsNamed("SpriteComponent")) ?? PhysicsImage(c))
                                                        .FirstOrDefault(s => s != null);

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
                f.MeleeFramesBetween = ai.Int("attack_melee_frames_between") ?? AnimalAiDefaults.MeleeFramesBetween;
                f.MeleeRange = ai.Float("attack_melee_max_distance") ?? AnimalAiDefaults.MeleeMaxDistance;
                f.DashEnabled = ai.Attr("attack_dash_enabled", "0") == "1";
                if (f.DashEnabled)
                {
                    f.DashFramesBetween = ai.Int("attack_dash_frames_between") ?? AnimalAiDefaults.DashFramesBetween;
                    f.DashDistance = ai.Float("attack_dash_distance") ?? AnimalAiDefaults.DashDistance;
                    f.DashSpeed = ai.Float("attack_dash_speed") ?? AnimalAiDefaults.DashSpeed;
                    f.DashDamage = ai.Float("attack_dash_damage") ?? AnimalAiDefaults.DashDamage;
                }
            }
            foreach (var atk in e.ComponentsNamed("AIAttackComponent"))
                if (!string.IsNullOrEmpty(atk.Attr("attack_ranged_entity_file")))
                    f.Ranged.Add(RangedAttackFacts.From(atk, "ai_attack"));
            f.Movement = MovementFacts.From(e);
            f.AudioRoots.AddRange(AudioRootsOf(e));
            return f;
        }

        public static IEnumerable<string> AudioRootsOf(NoitaEntity e)
        {
            foreach (var a in e.ComponentsNamed("AudioComponent"))
                if (!string.IsNullOrEmpty(a.Attr("event_root")))
                    yield return a.Attr("event_root");
        }

        static int[] Box(NxmlNode n, string minX, string maxX, string minY, string maxY) => new[]
        {
            (int)Math.Round((n.Float(maxX) ?? 0) - (n.Float(minX) ?? 0)),
            (int)Math.Round((n.Float(maxY) ?? 0) - (n.Float(minY) ?? 0)),
        };

        static string PhysicsImage(NoitaEntity e) =>
            e.ComponentsNamed("PhysicsImageShapeComponent").Select(c => c.Attr("image_file")).FirstOrDefault(f => !string.IsNullOrEmpty(f));

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

    /// <summary>
    /// How an enemy moves, in Noita units (px/s, px/sВІ): AnimalAIComponent says whether it may walk or fly and how far
    /// it sees, CharacterPlatformingComponent its speeds and gravity, PathFindingComponent whether and how it jumps.
    /// </summary>
    public sealed class MovementFacts
    {
        public bool CanWalk, CanFly, CanJump;
        public float? RunVelocity, FlyVelocityX, FlySpeedMaxUp;
        public float AccelX, PixelGravity, JumpSpeed;
        public float? DetectionRange;

        public static MovementFacts From(NoitaEntity e)
        {
            var ai = e.Component("AnimalAIComponent");
            var cp = e.Component("CharacterPlatformingComponent");
            var pf = e.Component("PathFindingComponent");
            if (cp == null)
                return null;
            return new MovementFacts
            {
                CanWalk = (ai?.Attr("can_walk") ?? "1") == "1",
                CanFly = (ai?.Attr("can_fly") ?? "1") == "1",
                CanJump = (pf?.Attr("can_jump") ?? "0") == "1",
                RunVelocity = cp.Float("run_velocity"),
                FlyVelocityX = cp.Float("fly_velocity_x"),
                FlySpeedMaxUp = cp.Float("fly_speed_max_up"),
                AccelX = cp.Float("accel_x") ?? MovementDefaults.AccelX,
                PixelGravity = cp.Float("pixel_gravity") ?? MovementDefaults.PixelGravity,
                JumpSpeed = pf?.Float("jump_speed") ?? MovementDefaults.JumpSpeed,
                DetectionRange = ai?.Float("creature_detection_range_x"),
            };
        }
    }

    public sealed class RangedAttackFacts
    {
        public string Source;               // animal_ai | ai_attack
        public string EntityFile;
        public int? FramesBetween;
        public float? MinDistance, MaxDistance;
        public int? CountMin, CountMax;
        public int StateFrames;             // frames the creature stays in its attack state after shooting

        public static RangedAttackFacts From(NxmlNode n, string source)
        {
            // AnimalAIComponent prefixes its ranged attributes with attack_ranged_; AIAttackComponent does not.
            bool animal = source == "animal_ai";
            return new RangedAttackFacts
            {
                Source = source,
                EntityFile = n.Attr("attack_ranged_entity_file"),
                FramesBetween = animal ? n.Int("attack_ranged_frames_between")
                                       : n.Int("frames_between") ?? AnimalAiDefaults.AttackFramesBetween,
                MinDistance = n.Float(animal ? "attack_ranged_min_distance" : "min_distance") ?? AnimalAiDefaults.RangedMinDistance,
                MaxDistance = n.Float(animal ? "attack_ranged_max_distance" : "max_distance") ?? AnimalAiDefaults.RangedMaxDistance,
                CountMin = n.Int("attack_ranged_entity_count_min") ?? AnimalAiDefaults.RangedCount,
                CountMax = n.Int("attack_ranged_entity_count_max") ?? AnimalAiDefaults.RangedCount,
                StateFrames = n.Int(animal ? "attack_ranged_state_duration_frames" : "state_duration_frames") ?? AnimalAiDefaults.RangedStateFrames,
            };
        }
    }

    /// <summary>
    /// Values Noita uses when an AnimalAIComponent / AIAttackComponent leaves an attribute out, as listed in
    /// Noita's tools_modding/component_documentation.txt.
    /// </summary>
    public static class AnimalAiDefaults
    {
        public const int MeleeFramesBetween = 10;
        public const float MeleeMaxDistance = 20f;
        public const int DashFramesBetween = 120;
        public const float DashDistance = 50f;
        public const float DashSpeed = 200f;
        public const float DashDamage = 0.25f;
        public const float RangedMinDistance = 10f;
        public const float RangedMaxDistance = 160f;
        public const int RangedCount = 1;
        public const int AttackFramesBetween = 180;     // AIAttackComponent.frames_between
        public const int RangedStateFrames = 45;        // attack_ranged_state_duration_frames / state_duration_frames
    }

    /// <summary>CharacterPlatformingComponent / PathFindingComponent defaults from component_documentation.txt.</summary>
    public static class MovementDefaults
    {
        public const float AccelX = 1f;
        public const float PixelGravity = 600f;
        public const float JumpSpeed = 200f;
    }

    /// <summary>ProjectileComponent / VelocityComponent defaults from component_documentation.txt.</summary>
    public static class ProjectileDefaults
    {
        public const float Speed = 60f;                 // ProjectileComponent.speed_min / speed_max
        public const float GravityY = 400f;             // VelocityComponent.gravity_y
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
        public string AudioRoot;            // AudioComponent event_root, e.g. projectiles/acid
        public string ExplosionSound;       // config_explosion audio_event_name, e.g. explosions/tnt

        public static ProjectileFacts From(NoitaEntity e)
        {
            var f = new ProjectileFacts { Entity = e.Path };
            var p = e.Component("ProjectileComponent");
            if (p != null)
            {
                f.SpeedMin = p.Float("speed_min") ?? ProjectileDefaults.Speed;
                f.SpeedMax = p.Float("speed_max") ?? ProjectileDefaults.Speed;
                f.LifetimeFrames = p.Int("lifetime");
                f.Damage = p.Float("damage");
                f.ExplosionRadius = p.Child("config_explosion")?.Float("explosion_radius");
                f.ExplosionSound = p.Child("config_explosion")?.Attr("audio_event_name");
            }
            foreach (var root in EnemyFacts.AudioRootsOf(e))
                if (f.AudioRoot == null || root.Length > f.AudioRoot.Length)   // the most specific folder
                    f.AudioRoot = root;
            var v = e.Component("VelocityComponent");
            if (v != null)
                f.GravityY = v.Float("gravity_y") ?? ProjectileDefaults.GravityY;
            foreach (var s in e.ComponentsNamed("SpriteComponent"))
                if (!string.IsNullOrEmpty(s.Attr("image_file")))
                {
                    f.Sprite = s.Attr("image_file");
                    break;
                }
            // Thrown physics objects (tnt) have no SpriteComponent: their look is the physics body's image.
            if (f.Sprite == null)
                foreach (var s in e.ComponentsNamed("PhysicsImageShapeComponent"))
                    if (!string.IsNullOrEmpty(s.Attr("image_file")))
                    {
                        f.Sprite = s.Attr("image_file");
                        break;
                    }
            return f;
        }
    }

    /// <summary>Unit conversions between Noita and Terraria (Noita art is drawn at 3x).</summary>
    public static class Units
    {
        public const float PixelScale = 3f;          // 1 Noita pixel = 3 Terraria world pixels (author's choice: normal size)
        public const float TerrariaTile = 16f;       // Terraria world pixels per tile
        public const float FramesPerSecond = 60f;

        public static float SpeedToTerraria(float noitaPxPerSecond) => noitaPxPerSecond * PixelScale / FramesPerSecond;
        public static float GravityToTerraria(float noitaPxPerSecond2) => noitaPxPerSecond2 * PixelScale / (FramesPerSecond * FramesPerSecond);
        public static float PxToTiles(float noitaPx) => noitaPx * PixelScale / TerrariaTile;
    }
}
