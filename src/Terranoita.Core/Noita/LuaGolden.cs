using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// The golden file of Noita's gun.lua (design/sources/lua_cast_golden.txt, `tncli lua-golden`): every spell cast
    /// once from a wand [spell, LIGHT_BULLET, BOMB] with a fixed seed, one line per spell. Comparing a new run with it
    /// shows what a change to LuaGun / LuaWorld did to any spell. Needs the player's Noita (PC).
    /// </summary>
    public static class LuaGolden
    {
        static string N(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

        public static IEnumerable<string> Lines(Func<string, string> read)
        {
            foreach (var id in new LuaGun(read).ActionIds())
                yield return Line(read, id);
        }

        public static string Line(Func<string, string> read, string id)
        {
            try
            {
                var gun = new LuaGun(read, null, 1);
                var w = new LuaWand { RechargeTime = 30, CastDelay = 10, Capacity = 4 };
                w.Spells.Add((id, -1)); w.Spells.Add(("LIGHT_BULLET", -1)); w.Spells.Add(("BOMB", -1));
                gun.Load(w);
                var c = gun.Cast(1000);
                var shots = c.Shots.GroupBy(x => System.IO.Path.GetFileNameWithoutExtension(x.File ?? "?")).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key + (g.Count() > 1 ? "x" + g.Count() : "") + (g.Any(x => x.Payload.Count > 0) ? "+payload" : ""));
                return id + " | shots " + c.Shots.Count + " " + string.Join(",", shots) + " | mana " + N(1000 - c.Mana) +
                       " | delay " + N(c.CastDelay) + " | recharge " + N(c.Recharge) +
                       " | played " + string.Join(",", c.Played) + (c.Missing.Count > 0 ? " | missing " + string.Join(",", c.Missing.Distinct()) : "");
            }
            catch (Exception ex) { return id + " | ERROR " + ex.Message.Split('\n')[0]; }
        }

        static string IdOf(string line) { int i = line.IndexOf(" | ", StringComparison.Ordinal); return i < 0 ? line.Trim() : line.Substring(0, i); }

        /// <summary>What differs from the golden text: "changed", "gone" (in golden only), "new" (in the run only).</summary>
        public static List<string> Diff(string golden, IEnumerable<string> run)
        {
            var want = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var raw in (golden ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.TrimEnd();
                if (l.Length == 0) continue;
                string id = IdOf(l);
                if (!want.ContainsKey(id)) order.Add(id);
                want[id] = l;
            }
            var diff = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in run)
            {
                string l = (raw ?? "").TrimEnd();
                if (l.Length == 0) continue;
                string id = IdOf(l);
                seen.Add(id);
                if (!want.TryGetValue(id, out var w))
                    diff.Add("new: " + l);
                else if (w != l)
                    diff.Add("changed: " + l + "\n   was: " + w);
            }
            foreach (var id in order)
                if (!seen.Contains(id))
                    diff.Add("gone: " + want[id]);
            return diff;
        }
    }
}
