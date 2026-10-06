using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>A node of Noita's XML. Noita's own parser is lenient, so this one is too.</summary>
    public sealed class NxmlNode
    {
        public string Name;
        public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<NxmlNode> Children = new List<NxmlNode>();

        public string Attr(string name, string fallback = null) =>
            Attributes.TryGetValue(name, out var v) ? v : fallback;

        public float? Float(string name)
        {
            var v = Attr(name);
            if (v == null)
                return null;
            return float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null;
        }

        public int? Int(string name)
        {
            var f = Float(name);
            return f.HasValue ? (int)Math.Round(f.Value) : (int?)null;
        }

        public NxmlNode Child(string name) => Children.Find(c => c.Name == name);

        public NxmlNode Clone()
        {
            var n = new NxmlNode { Name = Name };
            foreach (var kv in Attributes)
                n.Attributes[kv.Key] = kv.Value;
            foreach (var c in Children)
                n.Children.Add(c.Clone());
            return n;
        }

        /// <summary>Apply an override element (attributes and same-named child elements) onto this node.</summary>
        public void MergeFrom(NxmlNode over)
        {
            foreach (var kv in over.Attributes)
                Attributes[kv.Key] = kv.Value;
            foreach (var oc in over.Children)
            {
                var mine = Child(oc.Name);
                if (mine != null)
                    mine.MergeFrom(oc);
                else
                    Children.Add(oc.Clone());
            }
        }
    }

    /// <summary>Lenient XML reader: comments, processing instructions, unquoted attributes and stray text are tolerated.</summary>
    public static class Nxml
    {
        public static NxmlNode Parse(string text)
        {
            var root = new NxmlNode { Name = "#document" };
            var stack = new Stack<NxmlNode>();
            stack.Push(root);
            int i = 0, n = text.Length;
            while (i < n)
            {
                int lt = text.IndexOf('<', i);
                if (lt < 0)
                    break;
                i = lt;
                if (Starts(text, i, "<!--"))
                {
                    int end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }
                if (Starts(text, i, "<![CDATA["))
                {
                    int end = text.IndexOf("]]>", i, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }
                if (Starts(text, i, "<?") || Starts(text, i, "<!"))
                {
                    int end = text.IndexOf('>', i);
                    i = end < 0 ? n : end + 1;
                    continue;
                }
                if (Starts(text, i, "</"))
                {
                    int end = text.IndexOf('>', i);
                    string name = text.Substring(i + 2, (end < 0 ? n : end) - i - 2).Trim();
                    // pop to the matching element; ignore stray closers
                    foreach (var node in stack)
                    {
                        if (node.Name == name)
                        {
                            while (stack.Peek() != node)
                                stack.Pop();
                            stack.Pop();
                            break;
                        }
                    }
                    if (stack.Count == 0)
                        stack.Push(root);
                    i = end < 0 ? n : end + 1;
                    continue;
                }
                i++;
                var el = new NxmlNode { Name = ReadName(text, ref i) };
                if (el.Name.Length == 0)
                    continue;
                bool selfClosing = false;
                while (i < n)
                {
                    SkipSpace(text, ref i);
                    if (i >= n)
                        break;
                    if (text[i] == '>')
                    {
                        i++;
                        break;
                    }
                    if (text[i] == '/' && i + 1 < n && text[i + 1] == '>')
                    {
                        selfClosing = true;
                        i += 2;
                        break;
                    }
                    string an = ReadName(text, ref i);
                    if (an.Length == 0)
                    {
                        i++;
                        continue;
                    }
                    SkipSpace(text, ref i);
                    string av = "";
                    if (i < n && text[i] == '=')
                    {
                        i++;
                        SkipSpace(text, ref i);
                        av = ReadValue(text, ref i);
                    }
                    el.Attributes[an] = Unescape(av);
                }
                stack.Peek().Children.Add(el);
                if (!selfClosing)
                    stack.Push(el);
            }
            return root;
        }

        /// <summary>The first element of a document (e.g. &lt;Entity&gt; or &lt;Sprite&gt;).</summary>
        public static NxmlNode ParseRoot(string text)
        {
            var doc = Parse(text);
            return doc.Children.Count > 0 ? doc.Children[0] : null;
        }

        static bool Starts(string s, int i, string p) => string.CompareOrdinal(s, i, p, 0, p.Length) == 0;

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        static string ReadName(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '-' || s[i] == ':' || s[i] == '.'))
                i++;
            return s.Substring(start, i - start);
        }

        static string ReadValue(string s, ref int i)
        {
            if (i >= s.Length)
                return "";
            char q = s[i];
            if (q == '"' || q == '\'')
            {
                int end = s.IndexOf(q, i + 1);
                if (end < 0)
                    end = s.Length;
                string v = s.Substring(i + 1, end - i - 1);
                i = Math.Min(s.Length, end + 1);
                return v;
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '>' && !(s[i] == '/' && i + 1 < s.Length && s[i + 1] == '>'))
                i++;
            return s.Substring(start, i - start);
        }

        static string Unescape(string v)
        {
            if (v.IndexOf('&') < 0)
                return v;
            var sb = new StringBuilder(v);
            sb.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
            return sb.ToString();
        }
    }
}
