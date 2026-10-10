using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Terranoita.Noita
{
    /// <summary>
    /// One spell (Noita "action") read from data/scripts/gun/gun_actions.lua: its static fields and what its action
    /// function does, as far as the common statement shapes go. Anything else in the function is kept in Unparsed so a
    /// person can see which spells need hand work.
    /// </summary>
    public sealed class GunActionFacts
    {
        public string Id, Name, Description, Sprite, Type;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal); // other static fields, raw
        public int[] SpawnLevel;
        public float[] SpawnProbability;
        public float? Price, Mana;
        public int? MaxUses;
        public string[] RelatedProjectiles = new string[0];

        // parsed action function
        public readonly List<string> Projectiles = new List<string>();          // add_projectile(file), in order
        public readonly List<GunTrigger> Triggers = new List<GunTrigger>();     // add_projectile_trigger_*(file, ..., draws)
        public int Draws;                                                       // draw_actions(n) outside ifs/loops
        public readonly Dictionary<string, float> ConfigAdd = new Dictionary<string, float>(StringComparer.Ordinal);   // c.x = c.x + n
        public readonly Dictionary<string, float> ConfigMul = new Dictionary<string, float>(StringComparer.Ordinal);   // c.x = c.x * n
        public readonly Dictionary<string, string> ConfigSet = new Dictionary<string, string>(StringComparer.Ordinal); // c.x = value
        public float ReloadAdd;                                                 // current_reload_time + n
        public readonly Dictionary<string, float> ShotAdd = new Dictionary<string, float>(StringComparer.Ordinal);    // shot_effects.x = shot_effects.x + n
        public readonly Dictionary<string, float> ShotSet = new Dictionary<string, float>(StringComparer.Ordinal);    // shot_effects.x = n
        public readonly List<string> Clamps = new List<string>();                // c.x kept in a range by an if block (Noita's guards)
        public bool Conditional;                                                // the function has if/for/while
        public readonly List<string> Calls = new List<string>();                // other functions it calls
        public readonly List<string> Unparsed = new List<string>();             // statements none of the above matched
    }

    public sealed class GunTrigger
    {
        public string Kind;   // timer, hit_world, death
        public string File;
        public int Draws;
        public int? Frames;   // timer only
    }

    public static class GunActions
    {
        public const string Path = "data/scripts/gun/gun_actions.lua";

        /// <summary>Every entry of the <c>actions = { {...}, ... }</c> table, in file order.</summary>
        public static List<GunActionFacts> Parse(string lua) => Parse(lua, null);

        /// <summary>As Parse, with gun.lua's numeric constants (ACTION_DRAW_RELOAD_TIME_INCREASE = 0...) put in for their names.</summary>
        public static List<GunActionFacts> Parse(string lua, IDictionary<string, float> constants)
        {
            _constants = constants;
            string src = StripComments(lua);
            var list = new List<GunActionFacts>();
            var m = Regex.Match(src, @"(?m)^\s*actions\s*=\s*\{");
            if (!m.Success)
                return list;
            int i = m.Index + m.Length;
            while (true)
            {
                i = SkipSpaceAndCommas(src, i);
                if (i >= src.Length || src[i] == '}')
                    break;
                if (src[i] != '{')
                    throw new FormatException("gun_actions.lua: expected '{' at offset " + i);
                int end = Matching(src, i);
                list.Add(Entry(src.Substring(i + 1, end - i - 1)));
                i = end + 1;
            }
            return list;
        }

        [ThreadStatic] static IDictionary<string, float> _constants;

        /// <summary>NAME = number lines of a Lua file (gun.lua's constants).</summary>
        public static Dictionary<string, float> Constants(string lua)
        {
            var d = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(StripComments(lua ?? ""), @"(?m)^\s*([A-Z][A-Z0-9_]+)\s*=\s*" + Number + @"\s*$"))
                d[m.Groups[1].Value] = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return d;
        }

        // Noita's guards after a multiplier: if ( c.x >= 20 ) then c.x = math.min( c.x, 20 ) elseif ( c.x < 0 ) then c.x = 0 end,
        // and if ( c.x < 0 ) then c.x = 0 end
        static readonly Regex ClampBoth = new Regex(@"if\s*\(\s*c\.(\w+)\s*>=\s*(-?\d+(?:\.\d+)?)\s*\)\s*then\s*c\.\1\s*=\s*math\.min\(\s*c\.\1\s*,\s*\2\s*\)\s*elseif\s*\(\s*c\.\1\s*<\s*0\s*\)\s*then\s*c\.\1\s*=\s*0\s*end");
        static readonly Regex ClampLow = new Regex(@"if\s*\(\s*c\.(\w+)\s*<\s*0\s*\)\s*then\s*c\.\1\s*=\s*0\s*end");

        static string StripClamps(GunActionFacts a, string body)
        {
            body = ClampBoth.Replace(body, m => { a.Clamps.Add(m.Groups[1].Value + " 0.." + m.Groups[2].Value); return ""; });
            return ClampLow.Replace(body, m => { a.Clamps.Add(m.Groups[1].Value + " >= 0"); return ""; });
        }

        static string PutConstants(string body)
        {
            if (_constants == null || _constants.Count == 0)
                return body;
            return Regex.Replace(body, @"\b[A-Z][A-Z0-9_]+\b", m => _constants.TryGetValue(m.Value, out float v) ? v.ToString(CultureInfo.InvariantCulture) : m.Value);
        }

        static GunActionFacts Entry(string body)
        {
            var a = new GunActionFacts();
            foreach (var (key, raw) in Fields(body))
            {
                string v = raw.Trim();
                switch (key)
                {
                    case "id": a.Id = Str(v); break;
                    case "name": a.Name = Str(v); break;
                    case "description": a.Description = Str(v); break;
                    case "sprite": a.Sprite = Str(v); break;
                    case "type": a.Type = Regex.Replace(v, "^ACTION_TYPE_", "").ToLowerInvariant(); break;
                    case "spawn_level": a.SpawnLevel = Str(v)?.Split(',').Where(s => s.Trim() != "").Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray(); break;
                    case "spawn_probability": a.SpawnProbability = Str(v)?.Split(',').Where(s => s.Trim() != "").Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray(); break;
                    case "price": a.Price = Num(v); break;
                    case "mana": a.Mana = Num(v); break;
                    case "max_uses": a.MaxUses = Num(v) is float f ? (int)f : (int?)null; break;
                    case "related_projectiles": a.RelatedProjectiles = Strings(v); break;
                    case "action": Function(a, v); break;
                    default: a.Fields[key] = v; break;
                }
            }
            return a;
        }

        static void Function(GunActionFacts a, string v)
        {
            var fm = Regex.Match(v, @"^function\s*\([^)]*\)", RegexOptions.Singleline);
            if (!fm.Success || !v.EndsWith("end"))
            {
                a.Unparsed.Add(v);
                return;
            }
            string body = StripClamps(a, PutConstants(v.Substring(fm.Length, v.Length - fm.Length - 3)));
            a.Conditional = Regex.IsMatch(body, @"\b(if|for|while|repeat)\b");
            int depth = 0;
            foreach (var st in Statements(body))
            {
                string s = st.Trim();
                if (s.Length == 0)
                    continue;
                // track if/for/while blocks: draws inside them are not counted as fixed
                int opens = Regex.Matches(s, @"\b(if|for|while|function)\b").Count;
                int closes = Regex.Matches(s, @"\bend\b").Count;
                bool top = depth == 0 && opens == 0;
                depth += opens - closes;
                if (!top)
                {
                    foreach (Match c in Regex.Matches(s, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                        AddCall(a, c.Groups[1].Value);
                    a.Unparsed.Add(s);
                    continue;
                }
                if (!Statement(a, s))
                    a.Unparsed.Add(s);
            }
        }

        static readonly string Number = @"(-?\d+(?:\.\d+)?)";

        static bool Statement(GunActionFacts a, string s)
        {
            Match m;
            if ((m = Regex.Match(s, @"^add_projectile\s*\(\s*""([^""]+)""\s*\)$")).Success)
            {
                a.Projectiles.Add(m.Groups[1].Value);
                return true;
            }
            if ((m = Regex.Match(s, @"^add_projectile_trigger_(timer|hit_world|death)\s*\(\s*""([^""]+)""\s*((?:,\s*" + Number + @"\s*)*)\)$")).Success)
            {
                var nums = Regex.Matches(m.Groups[3].Value, Number).Cast<Match>().Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                var t = new GunTrigger { Kind = m.Groups[1].Value, File = m.Groups[2].Value };
                if (t.Kind == "timer" && nums.Count >= 2) { t.Frames = nums[0]; t.Draws = nums[1]; }
                else if (nums.Count >= 1) t.Draws = nums[nums.Count - 1];
                else return false;
                a.Triggers.Add(t);
                return true;
            }
            if ((m = Regex.Match(s, @"^draw_actions\s*\(\s*" + Number + @"\s*,\s*true\s*\)$")).Success)
            {
                a.Draws += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return true;
            }
            if ((m = Regex.Match(s, @"^c\.(\w+)\s*=\s*c\.(\w+)\s*([-+*])\s*" + Number + "$")).Success && m.Groups[1].Value == m.Groups[2].Value)
            {
                float n = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                string k = m.Groups[1].Value;
                if (m.Groups[3].Value == "*")
                    a.ConfigMul[k] = (a.ConfigMul.TryGetValue(k, out var x) ? x : 1) * n;
                else
                    a.ConfigAdd[k] = (a.ConfigAdd.TryGetValue(k, out var x) ? x : 0) + (m.Groups[3].Value == "-" ? -n : n);
                return true;
            }
            // current_reload_time = current_reload_time - 0 - 10 (constants already put in)
            if ((m = Regex.Match(s, @"^current_reload_time\s*=\s*current_reload_time((?:\s*[-+]\s*-?\d+(?:\.\d+)?)+)$")).Success)
            {
                foreach (Match t in Regex.Matches(m.Groups[1].Value, @"([-+])\s*(-?\d+(?:\.\d+)?)"))
                {
                    float n = float.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture);
                    a.ReloadAdd += t.Groups[1].Value == "-" ? -n : n;
                }
                return true;
            }
            if ((m = Regex.Match(s, @"^shot_effects\.(\w+)\s*=\s*shot_effects\.(\w+)\s*([-+])\s*" + Number + "$")).Success && m.Groups[1].Value == m.Groups[2].Value)
            {
                float n = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                string k = m.Groups[1].Value;
                a.ShotAdd[k] = (a.ShotAdd.TryGetValue(k, out var x) ? x : 0) + (m.Groups[3].Value == "-" ? -n : n);
                return true;
            }
            if ((m = Regex.Match(s, @"^shot_effects\.(\w+)\s*=\s*" + Number + "$")).Success)
            {
                a.ShotSet[m.Groups[1].Value] = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return true;
            }
            if ((m = Regex.Match(s, @"^c\.(\w+)\s*=\s*(""[^""]*""|-?\d+(?:\.\d+)?|true|false)$")).Success)
            {
                a.ConfigSet[m.Groups[1].Value] = m.Groups[2].Value.Trim('"');
                return true;
            }
            if ((m = Regex.Match(s, @"^c\.(\w+)\s*=\s*c\.\1\s*\.\.\s*""([^""]*)""$")).Success)
            {
                // c.extra_entities = c.extra_entities .. "file,"  (modifiers that attach an entity to each shot)
                a.ConfigSet[m.Groups[1].Value + "+"] = (a.ConfigSet.TryGetValue(m.Groups[1].Value + "+", out var old) ? old : "") + m.Groups[2].Value;
                return true;
            }
            foreach (Match c in Regex.Matches(s, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                AddCall(a, c.Groups[1].Value);
            return false;
        }

        static void AddCall(GunActionFacts a, string name)
        {
            if (name != "function" && !a.Calls.Contains(name))
                a.Calls.Add(name);
        }

        // ---- small Lua lexing helpers (enough for a table of literals and short functions) ----

        public static string StripComments(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length;)
            {
                char ch = s[i];
                if (ch == '"' || ch == '\'')
                {
                    int j = StringEnd(s, i);
                    sb.Append(s, i, j - i);
                    i = j;
                }
                else if (ch == '-' && i + 1 < s.Length && s[i + 1] == '-')
                {
                    int lvl;
                    if (i + 2 < s.Length && s[i + 2] == '[' && (lvl = LongOpen(s, i + 2)) >= 0)
                    {
                        string close = "]" + new string('=', lvl) + "]";
                        int j = s.IndexOf(close, i, StringComparison.Ordinal);
                        i = j < 0 ? s.Length : j + close.Length;
                    }
                    else
                    {
                        int j = s.IndexOf('\n', i);
                        i = j < 0 ? s.Length : j;
                    }
                }
                else
                {
                    sb.Append(ch);
                    i++;
                }
            }
            return sb.ToString();
        }

        static int LongOpen(string s, int i)
        {
            int j = i + 1, lvl = 0;
            while (j < s.Length && s[j] == '=') { lvl++; j++; }
            return j < s.Length && s[j] == '[' ? lvl : -1;
        }

        static int StringEnd(string s, int i)
        {
            char q = s[i];
            int j = i + 1;
            while (j < s.Length && s[j] != q && s[j] != '\n')
                j += s[j] == '\\' ? 2 : 1;
            return Math.Min(j + 1, s.Length);
        }

        static int SkipSpaceAndCommas(string s, int i)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',' || s[i] == ';'))
                i++;
            return i;
        }

        static bool WordAt(string s, int i, string w) =>
            string.CompareOrdinal(s, i, w, 0, w.Length) == 0 &&
            (i == 0 || !IsWord(s[i - 1])) && (i + w.Length >= s.Length || !IsWord(s[i + w.Length]));

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Index of the bracket closing the one at <paramref name="open"/>; skips strings.</summary>
        static int Matching(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\'') { i = StringEnd(s, i) - 1; continue; }
                if (c == '{' || c == '(' || c == '[') depth++;
                else if (c == '}' || c == ')' || c == ']') { if (--depth == 0) return i; }
            }
            throw new FormatException("unbalanced brackets at offset " + open);
        }

        /// <summary>Top-level <c>key = value</c> pairs of a table body; values end at a top-level comma.</summary>
        static IEnumerable<(string, string)> Fields(string body)
        {
            int i = 0;
            while (true)
            {
                i = SkipSpaceAndCommas(body, i);
                if (i >= body.Length)
                    yield break;
                var m = Regex.Match(body.Substring(i), @"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*");
                if (!m.Success)
                    throw new FormatException("gun_actions.lua: expected 'key =' near: " + body.Substring(i, Math.Min(40, body.Length - i)));
                int start = i + m.Length, j = start, blocks = 0;
                for (; j < body.Length; j++)
                {
                    char c = body[j];
                    if (c == '"' || c == '\'') { j = StringEnd(body, j) - 1; continue; }
                    if (c == '{' || c == '(' || c == '[') { j = Matching(body, j); continue; }
                    if (WordAt(body, j, "function") || WordAt(body, j, "if") || WordAt(body, j, "do")) blocks++;
                    else if (WordAt(body, j, "end")) blocks--;
                    else if (c == ',' && blocks == 0) break;
                }
                yield return (m.Groups[1].Value, body.Substring(start, j - start));
                i = j;
            }
        }

        /// <summary>Splits a function body into statements at line ends that are outside brackets.</summary>
        static IEnumerable<string> Statements(string body)
        {
            int start = 0;
            for (int j = 0; j < body.Length; j++)
            {
                char c = body[j];
                if (c == '"' || c == '\'') { j = StringEnd(body, j) - 1; continue; }
                if (c == '{' || c == '(' || c == '[') { j = Matching(body, j); continue; }
                if (c == '\n' || c == ';')
                {
                    yield return body.Substring(start, j - start);
                    start = j + 1;
                }
            }
            yield return body.Substring(start);
        }

        static string Str(string v)
        {
            v = v.Trim();
            return v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0] ? v.Substring(1, v.Length - 2) : null;
        }

        static float? Num(string v) =>
            float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null;

        static string[] Strings(string v)
        {
            v = v.Trim();
            if (!v.StartsWith("{"))
                return new string[0];
            return Regex.Matches(v, @"""([^""]*)""").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }
    }
}
