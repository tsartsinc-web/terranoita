using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// The components of an entity (with its child entities) that say how it behaves, attribute by attribute as the
    /// XML sets them. EnemyFacts covers the common walkers and flyers; this covers everything else the sheets need
    /// (worms, lukki, ghosts, crystals, traps, auras, projectile effects), so a session without the game can fill
    /// those columns from the facts file. Pure visuals are left out, and the few visual components that matter are
    /// cut down to the attributes that do.
    /// </summary>
    public static class EntityDump
    {
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "SpriteAnimatorComponent", "SpriteParticleEmitterComponent", "SpriteOffsetAnimatorComponent",
            "SpriteStainsComponent", "InheritTransformComponent", "HotspotComponent", "AudioLoopComponent",
            "AudioListenerComponent", "UIIconComponent", "UIInfoComponent", "CameraBoundComponent",
            "StatusEffectDataComponent", "ItemComponent", "InventoryComponent", "Inventory2Component",
            "VerletWorldJointComponent", "PhysicsJointComponent", "PhysicsJoint2Component",
        };

        static readonly Dictionary<string, string[]> Only = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SpriteComponent"] = new[] { "image_file", "emissive", "additive", "alpha", "visible" },
            ["ParticleEmitterComponent"] = new[] { "emitted_material_name", "is_emitting", "create_real_particles" },
            ["LightComponent"] = new[] { "radius", "r", "g", "b" },
        };

        public sealed class Item
        {
            /// <summary>null for the entity itself, else the child entity's name (or "child") and depth.</summary>
            public string Entity;
            public NxmlNode Component;
        }

        public static List<Item> Of(NoitaEntity e)
        {
            var list = new List<Item>();
            Add(list, e, null, 0);
            return list;
        }

        static void Add(List<Item> list, NoitaEntity e, string label, int depth)
        {
            foreach (var c in e.Components)
            {
                if (Skip.Contains(c.Name))
                    continue;
                list.Add(new Item { Entity = label, Component = Trim(c) });
            }
            if (depth >= 3)
                return;
            int i = 0;
            foreach (var child in e.Children)
                Add(list, child, (label == null ? "" : label + "/") + (string.IsNullOrEmpty(child.Name) ? "child" + i : child.Name), depth + 1);
        }

        static NxmlNode Trim(NxmlNode c)
        {
            if (!Only.TryGetValue(c.Name, out var keep))
                return c.Clone();
            var n = new NxmlNode { Name = c.Name };
            foreach (var k in keep)
                if (c.Attributes.TryGetValue(k, out var v))
                    n.Attributes[k] = v;
            return n;
        }

        // a whole path, or the start of one a script completes at run time ("data/entities/animals/" .. name .. ".xml")
        static readonly System.Text.RegularExpressions.Regex EntityFile =
            new System.Text.RegularExpressions.Regex(@"[""'](data/entities/[^""']*?(?:\.xml|/))[""']");

        /// <summary>Entity XML paths (or folder prefixes a script completes) written as string literals in a script, in
        /// order, without repeats.</summary>
        public static IEnumerable<string> EntityFilesIn(string script) =>
            EntityFile.Matches(script).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct();

        /// <summary>The script files an entity runs (LuaComponent script_* attributes), for reading by hand.</summary>
        public static IEnumerable<string> Scripts(IEnumerable<Item> items) =>
            items.Where(i => i.Component.Name == "LuaComponent")
                 .SelectMany(i => i.Component.Attributes.Where(kv => kv.Key.StartsWith("script_", StringComparison.Ordinal) && kv.Value.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                 .Select(kv => kv.Value).Distinct();
    }

    /// <summary>Noita's tools_modding/component_documentation.txt cut into one text block per component.</summary>
    public static class ComponentDocs
    {
        public static Dictionary<string, string> Split(string text)
        {
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            string current = null;
            var sb = new System.Text.StringBuilder();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                bool header = line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.EndsWith("Component", StringComparison.Ordinal) &&
                              line.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
                if (header)
                {
                    if (current != null)
                        blocks[current] = sb.ToString().TrimEnd();
                    current = line;
                    sb.Clear();
                }
                else if (current != null && line.Length > 0)
                    sb.Append(line).Append('\n');
            }
            if (current != null)
                blocks[current] = sb.ToString().TrimEnd();
            return blocks;
        }
    }
}
