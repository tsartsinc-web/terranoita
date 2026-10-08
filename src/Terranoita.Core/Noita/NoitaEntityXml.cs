using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>A component of a Noita entity file, flattened: object fields become "object.field".</summary>
    public sealed class XmlComponent
    {
        public string Type;
        public string Tags = "";
        public bool Enabled = true;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string field) => Fields.TryGetValue(field, out var v) ? v : null;
    }

    /// <summary>A Noita entity file after its &lt;Base&gt; chain: name, tags, transform, components, child entities.</summary>
    public sealed class XmlEntity
    {
        public string Path, Name = "", Tags = "";
        public float X, Y, Rotation, ScaleX = 1, ScaleY = 1;
        public readonly List<XmlComponent> Components = new List<XmlComponent>();
        public readonly List<XmlEntity> Children = new List<XmlEntity>();
    }

    /// <summary>
    /// Loads a Noita entity XML (data/entities/...) as Noita builds it: &lt;Base file&gt; first, a component inside
    /// &lt;Base&gt; overrides the base's same-type component, the file's own components and children follow
    /// (NoitaEntity does the merge; this turns nodes into flat components). Comments and sloppy XML are tolerated.
    /// </summary>
    public static class NoitaEntityXml
    {
        public static XmlEntity Load(string path, Func<string, string> read) => From(NoitaEntity.Load(read, path));

        public static XmlEntity From(NoitaEntity e)
        {
            var x = new XmlEntity { Path = e.Path, Name = e.Name ?? "", Tags = e.Tags ?? "" };
            foreach (var n in e.Components)
            {
                if (n.Name == "_Transform")
                {
                    x.X = F(n.Attr("position.x"), 0); x.Y = F(n.Attr("position.y"), 0);
                    x.Rotation = F(n.Attr("rotation"), 0);
                    x.ScaleX = F(n.Attr("scale.x"), 1); x.ScaleY = F(n.Attr("scale.y"), 1);
                    continue;
                }
                x.Components.Add(Component(n));
            }
            foreach (var c in e.Children)
                x.Children.Add(From(c));
            return x;
        }

        public static XmlComponent Component(NxmlNode n)
        {
            var c = new XmlComponent { Type = n.Name };
            foreach (var kv in n.Attributes)
            {
                if (kv.Key == "_tags") c.Tags = kv.Value ?? "";
                else if (kv.Key == "_enabled") c.Enabled = kv.Value != "0" && kv.Value != "false";
                else c.Fields[kv.Key] = kv.Value ?? "";
            }
            foreach (var child in n.Children)
                Flatten(child, child.Name + ".", c.Fields);
            return c;
        }

        static void Flatten(NxmlNode n, string prefix, Dictionary<string, string> into)
        {
            foreach (var kv in n.Attributes)
                into[prefix + kv.Key] = kv.Value ?? "";
            foreach (var child in n.Children)
                Flatten(child, prefix + child.Name + ".", into);
        }

        static float F(string s, float d) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
    }

    /// <summary>Field types per component from Noita's tools_modding/component_documentation.txt (bool, float, vec2...).</summary>
    public sealed class ComponentFieldTypes
    {
        readonly Dictionary<string, Dictionary<string, string>> _types = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public static ComponentFieldTypes Parse(string documentation)
        {
            var t = new ComponentFieldTypes();
            if (string.IsNullOrEmpty(documentation))
                return t;
            foreach (var block in ComponentDocs.Split(documentation))
            {
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var line in block.Value.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s{2,}(\S.*?)\s{2,}(\w+)\s");
                    if (m.Success && !fields.ContainsKey(m.Groups[2].Value))
                        fields[m.Groups[2].Value] = m.Groups[1].Value.Trim();
                }
                t._types[block.Key] = fields;
            }
            return t;
        }

        /// <summary>The documented C++ type, or null.</summary>
        public string Of(string component, string field) =>
            component != null && _types.TryGetValue(component, out var f) && f.TryGetValue(field, out var s) ? s : null;

        /// <summary>bool, number, string, vec2 or null (unknown).</summary>
        public string Kind(string component, string field)
        {
            string t = Of(component, field);
            if (t == null) return null;
            if (t == "bool" || t == "LensValue<bool>") return "bool";
            if (t == "vec2" || t == "ivec2") return "vec2";
            if (t == "std::string" || t.EndsWith("::Enum")) return "string";
            if (t.Contains("float") || t.Contains("int") || t == "double" || t == "EntityID" || t == "EntityTypeID") return "number";
            return null;
        }
    }
}
