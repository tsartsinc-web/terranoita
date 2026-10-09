using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>One projectile the Noita probe saw (tools/noita_probe/terranoita_probe/init.lua): Noita pixels and
    /// frames, positions relative to the caster, born/end in frames after the probe pressed fire.</summary>
    public sealed class ProbeShot
    {
        public string File, Parent;
        public int Born;
        public int? End;
        public double? Lifetime, X0, Y0, Vx0, Vy0, Vx1, Vy1, X1, Y1;
        public double Speed0 => Math.Sqrt((Vx0 ?? 0) * (Vx0 ?? 0) + (Vy0 ?? 0) * (Vy0 ?? 0));
    }

    public sealed class ProbeHit
    {
        public double Damage;   // Noita units (x25 = hp)
        public string Message, By;
    }

    /// <summary>One cast of the probe: what really happened in Noita (design/magic_plan.md PC-22/23 ground truth).</summary>
    public sealed class ProbeRow
    {
        public string Name, Note;
        public string[] Deck;
        public double? ManaUsed;
        public int Frames;
        public List<ProbeShot> Shots = new List<ProbeShot>();
        public List<ProbeHit> Hits = new List<ProbeHit>();
        public bool Fired => Shots.Count > 0 || (ManaUsed ?? 0) > 0;
    }

    /// <summary>Reads the probe's probe_out.jsonl (one JSON object per line; the "done" line and diag rows skipped).</summary>
    public static class NoitaProbe
    {
        public static List<ProbeRow> Read(string jsonl)
        {
            var rows = new List<ProbeRow>();
            foreach (var raw in (jsonl ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                var o = MiniJson.Parse(line) as Dictionary<string, object>;
                if (o == null || !o.TryGetValue("name", out var name) || !(name is string n) || n.StartsWith("diag:", StringComparison.Ordinal))
                    continue;
                var row = new ProbeRow
                {
                    Name = n,
                    Note = o.TryGetValue("note", out var note) ? note as string : null,
                    Deck = (o.TryGetValue("deck", out var d) ? d as List<object> : null)?.Select(x => x as string).ToArray() ?? new string[0],
                    ManaUsed = Num(o, "mana_used"),
                    Frames = (int)(Num(o, "frames") ?? 0),
                };
                foreach (var p in (o.TryGetValue("projectiles", out var ps) ? ps as List<object> : null) ?? new List<object>())
                    if (p is Dictionary<string, object> s)
                        row.Shots.Add(new ProbeShot
                        {
                            File = Str(s, "file"), Parent = Str(s, "parent"), Born = (int)(Num(s, "born") ?? 0),
                            End = Num(s, "end") is double e ? (int?)e : null, Lifetime = Num(s, "lifetime"),
                            X0 = Num(s, "x0"), Y0 = Num(s, "y0"), Vx0 = Num(s, "vx0"), Vy0 = Num(s, "vy0"),
                            Vx1 = Num(s, "vx1"), Vy1 = Num(s, "vy1"), X1 = Num(s, "x1"), Y1 = Num(s, "y1"),
                        });
                foreach (var h in (o.TryGetValue("hits", out var hs) ? hs as List<object> : null) ?? new List<object>())
                    if (h is Dictionary<string, object> x)
                        row.Hits.Add(new ProbeHit { Damage = Num(x, "damage") ?? 0, Message = Str(x, "message"), By = Str(x, "by") });
                rows.Add(row);
            }
            return rows;
        }

        static double? Num(Dictionary<string, object> o, string k) => o.TryGetValue(k, out var v) && v is double d ? d : (double?)null;
        static string Str(Dictionary<string, object> o, string k) => o.TryGetValue(k, out var v) ? v as string ?? "" : "";
    }

    /// <summary>A small JSON reader (objects -> Dictionary, arrays -> List, numbers -> double, true/false, null):
    /// Core targets netstandard2.0/net48 with no JSON library, and the game reads the probe at test time.</summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length)
                throw new FormatException("JSON: text after the value at " + i);
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length)
                throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    var key = Str(s, ref i);
                    Skip(s, ref i);
                    Expect(s, ref i, ':');
                    o[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return o;
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return a;
                }
            }
            if (c == '"')
                return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                i++;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw new FormatException("JSON: bad value at " + start);
            return d;
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0)
                return false;
            i += w.Length;
            return true;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c)
                throw new FormatException("JSON: '" + c + "' expected at " + i);
            i++;
        }

        static string Str(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }
            Expect(s, ref i, '"');
            return sb.ToString();
        }
    }
}
