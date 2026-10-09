using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// One test of the probe compared field by field (design/magic_plan.md PC-23): Noita's row (the probe mod in Noita)
    /// against ours (SpellProbeTest in Terraria), both in Noita's units. Differences are written as short reasons;
    /// a test with none matches Noita.
    /// </summary>
    public sealed class ProbeVerdict
    {
        public string Name;
        public readonly List<string> Differences = new List<string>();
        public bool Matches => Differences.Count == 0;
    }

    public static class ProbeCompare
    {
        // tolerances (ours, for one random sample each side): Noita picks speed in speed_min..max and spreads shots
        public const double SpeedTolerance = 0.25, DamageTolerance = 0.30, ManaTolerance = 0.5;

        static bool Root(ProbeShot s) => string.IsNullOrEmpty(s.Parent) || s.Parent.EndsWith("/player.xml", StringComparison.Ordinal);

        static Dictionary<string, int> Count(IEnumerable<ProbeShot> shots, Func<ProbeShot, string> key) =>
            shots.GroupBy(key).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        static string Short(string file) => System.IO.Path.GetFileNameWithoutExtension(file ?? "");

        static string F(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

        public static ProbeVerdict Compare(ProbeRow noita, ProbeRow ours)
        {
            var v = new ProbeVerdict { Name = noita.Name };
            if (ours == null)
            {
                v.Differences.Add("not run in Terraria");
                return v;
            }
            if (noita.Fired != ours.Fired)
            {
                v.Differences.Add(noita.Fired ? "nothing fired (Noita fired)" : "fired (Noita fired nothing)");
                return v;
            }
            // what the cast released, and what those released (by file, and by file + parent)
            var nRoot = Count(noita.Shots.Where(Root), s => s.File);
            var oRoot = Count(ours.Shots.Where(Root), s => s.File);
            foreach (var f in nRoot.Keys.Union(oRoot.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                nRoot.TryGetValue(f, out int a);
                oRoot.TryGetValue(f, out int b);
                if (a != b)
                    v.Differences.Add("shots " + Short(f) + ": " + b + " (Noita " + a + ")");
            }
            var nKids = Count(noita.Shots.Where(s => !Root(s)), s => s.File + " <- " + s.Parent);
            var oKids = Count(ours.Shots.Where(s => !Root(s)), s => s.File + " <- " + s.Parent);
            foreach (var k in nKids.Keys.Union(oKids.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                nKids.TryGetValue(k, out int a);
                oKids.TryGetValue(k, out int b);
                if (a != b)
                {
                    var parts = k.Split(new[] { " <- " }, StringSplitOptions.None);
                    v.Differences.Add("children " + Short(parts[0]) + " from " + Short(parts.Length > 1 ? parts[1] : "") + ": " + b + " (Noita " + a + ")");
                }
            }
            // start speed of each file both released at the root
            foreach (var f in nRoot.Keys.Intersect(oRoot.Keys))
            {
                double ns = noita.Shots.First(s => Root(s) && s.File == f).Speed0, os = ours.Shots.First(s => Root(s) && s.File == f).Speed0;
                if (Math.Abs(os - ns) > SpeedTolerance * Math.Max(ns, 1))
                    v.Differences.Add("speed " + Short(f) + ": " + F(os) + " (Noita " + F(ns) + ")");
            }
            // damage on the target by Noita's message
            var nHit = noita.Hits.GroupBy(h => h.Message).ToDictionary(g => g.Key, g => g.Sum(h => h.Damage));
            var oHit = ours.Hits.GroupBy(h => h.Message).ToDictionary(g => g.Key, g => g.Sum(h => h.Damage));
            foreach (var m in nHit.Keys.Union(oHit.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                nHit.TryGetValue(m, out double a);
                oHit.TryGetValue(m, out double b);
                if (Math.Abs(a - b) > DamageTolerance * Math.Max(a, 0.04))
                    v.Differences.Add("damage " + m + ": " + F(b) + " (Noita " + F(a) + ")");
            }
            if (noita.ManaUsed.HasValue && ours.ManaUsed.HasValue && Math.Abs(noita.ManaUsed.Value - ours.ManaUsed.Value) > ManaTolerance)
                v.Differences.Add("mana " + F(ours.ManaUsed.Value) + " (Noita " + F(noita.ManaUsed.Value) + ")");
            return v;
        }

        /// <summary>Every test Noita measured, against ours (missing ones count as not matching).</summary>
        public static List<ProbeVerdict> CompareAll(IEnumerable<ProbeRow> noita, IEnumerable<ProbeRow> ours)
        {
            var mine = new Dictionary<string, ProbeRow>(StringComparer.Ordinal);
            foreach (var r in ours)
                mine[r.Name] = r;
            return noita.Select(n => Compare(n, mine.TryGetValue(n.Name, out var o) ? o : null)).ToList();
        }

        /// <summary>"matching Noita: N of M" plus per suite (single, mod, combo).</summary>
        public static string Summary(IList<ProbeVerdict> verdicts)
        {
            string Part(IEnumerable<ProbeVerdict> vs) { var l = vs.ToList(); return l.Count(x => x.Matches) + " of " + l.Count; }
            var suites = verdicts.GroupBy(x => x.Name.Split(':')[0]).Select(g => g.Key + " " + Part(g));
            return "matching Noita: " + Part(verdicts) + " (" + string.Join(", ", suites) + ")";
        }
    }
}
