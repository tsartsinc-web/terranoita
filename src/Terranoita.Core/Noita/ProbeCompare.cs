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
        /// <summary>The differences in what the cast does (design/tasks.md PC-30, the author's priority): it fires, the shots
        /// and payloads it makes, the kinds of harm it does, a flight that goes elsewhere. Also in Differences; the rest
        /// there are numbers (speed, amounts, mana, a flight a little off).</summary>
        public readonly List<string> Behaviour = new List<string>();
        public bool Matches => Differences.Count == 0;
        public bool BehavesLike => Behaviour.Count == 0;
    }

    public static class ProbeCompare
    {
        // tolerances (ours, for one random sample each side): Noita picks speed in speed_min..max and spreads shots
        public const double SpeedTolerance = 0.25, DamageTolerance = 0.30, ManaTolerance = 0.5;
        // flight: px off allowed, plus this share of the distance Noita's shot has flown, over its first PathFrames frames
        public const double PathTolerance = 4, PathShare = 0.1;
        public const int PathFrames = 30;
        // a flight that goes elsewhere (behaviour): px off plus this share of the distance flown
        public const double FlightTolerance = 10, FlightShare = 0.3;
        // below this a hit is no hit (Noita damage units; 0.04 = 1 hp)
        public const double NoHarm = 0.04;

        /// <summary>A shot's flight as (frame, along, across): where it is at each sampled frame relative to where it was
        /// first seen, along and across the way it first flew (along its axes when it started still). Both sides first see
        /// a shot after its first move and sample every 5 frames from there (Noita: x0 - vx0/60 is one spawn point for
        /// every speed; SpellRecorder samples after the move), so the frames line up; measuring from each side's own
        /// direction keeps the random spread of one sample each from counting.</summary>
        static IEnumerable<(int frame, double along, double across)> Flight(ProbeShot s)
        {
            if (s.X0 == null || s.Y0 == null)
                yield break;
            double vx = s.Vx0 ?? 0, vy = s.Vy0 ?? 0, len = Math.Sqrt(vx * vx + vy * vy);
            double ux = len < 1 ? 1 : vx / len, uy = len < 1 ? 0 : vy / len;
            foreach (var p in s.Path)
            {
                if (p.Length < 3 || p[0] > PathFrames)
                    continue;
                double dx = p[1] - s.X0.Value, dy = p[2] - s.Y0.Value;
                yield return ((int)p[0], dx * ux + dy * uy, ux * dy - uy * dx);
            }
        }

        /// <summary>Shots are projectile files; the probe also records Noita's own effect entities (particles, misc/crack,
        /// misc/electricity: run 2026-10-09), which are not shots of our runtime.</summary>
        public static bool IsShot(ProbeShot s) => (s.File ?? "").StartsWith("data/entities/projectiles/", StringComparison.Ordinal);

        static Dictionary<string, int> Count(IEnumerable<ProbeShot> shots, Func<ProbeShot, string> key) =>
            shots.GroupBy(key).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        static string Short(string file) => System.IO.Path.GetFileNameWithoutExtension(file ?? "");

        static string F(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

        public static ProbeVerdict Compare(ProbeRow noita, ProbeRow ours)
        {
            var v = new ProbeVerdict { Name = noita.Name };
            void Does(string d) { v.Behaviour.Add(d); v.Differences.Add(d); }
            if (ours == null)
            {
                Does("not run in Terraria");
                return v;
            }
            if (noita.Fired != ours.Fired)
            {
                Does(noita.Fired ? "nothing fired (Noita fired)" : "fired (Noita fired nothing)");
                return v;
            }
            // what the cast released, by file, whoever shot it: the Noita probe sees the player as the shooter of nearly
            // every shot (a trigger's payload too: 927 of 930 rows have no projectile parent), ours records the shot
            // that released it
            var nShots = noita.Shots.Where(IsShot).ToList();
            var oShots = ours.Shots.Where(IsShot).ToList();
            var nCount = Count(nShots, s => s.File);
            var oCount = Count(oShots, s => s.File);
            foreach (var f in nCount.Keys.Union(oCount.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                nCount.TryGetValue(f, out int a);
                oCount.TryGetValue(f, out int b);
                if (a != b)
                    Does("shots " + Short(f) + ": " + b + " (Noita " + a + ")");
            }
            // start speed and flight of the first shot of each file both released
            foreach (var f in nCount.Keys.Intersect(oCount.Keys))
            {
                var nf = nShots.First(s => s.File == f);
                var of = oShots.First(s => s.File == f);
                if (nf.Vx0 == null || of.Vx0 == null)
                    continue;   // gone before it could be measured
                double ns = nf.Speed0, os = of.Speed0;
                if (Math.Abs(os - ns) > SpeedTolerance * Math.Max(ns, 1))
                    v.Differences.Add("speed " + Short(f) + ": " + F(os) + " (Noita " + F(ns) + ")");
                // ours scaled to Noita's start speed: one random speed each side (speed_min..speed_max) is checked above,
                // the path checks how the flight goes on from it
                double scale = ns >= 1 && os >= 1 ? ns / os : 1;
                var mine = new Dictionary<int, (int frame, double along, double across)>();
                foreach (var q in Flight(of))
                    if (!mine.ContainsKey(q.frame))
                        mine[q.frame] = (q.frame, q.along * scale, q.across * scale);
                foreach (var p in Flight(nf))
                {
                    if (!mine.TryGetValue(p.frame, out var q))
                        continue;
                    double off = Math.Sqrt((p.along - q.along) * (p.along - q.along) + (p.across - q.across) * (p.across - q.across));
                    double flown = Math.Sqrt(p.along * p.along + p.across * p.across);
                    if (off > PathTolerance + PathShare * flown)
                    {
                        string d = "path " + Short(f) + ": " + F(Math.Round(off)) + " px off at frame " + p.frame;
                        if (off > FlightTolerance + FlightShare * flown)
                            Does(d);
                        else
                            v.Differences.Add(d);
                        break;
                    }
                }
            }
            // damage on the target by Noita's message
            var nHit = noita.Hits.GroupBy(h => h.Message).ToDictionary(g => g.Key, g => g.Sum(h => h.Damage));
            var oHit = ours.Hits.GroupBy(h => h.Message).ToDictionary(g => g.Key, g => g.Sum(h => h.Damage));
            foreach (var m in nHit.Keys.Union(oHit.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                nHit.TryGetValue(m, out double a);
                oHit.TryGetValue(m, out double b);
                string d = "damage " + m + ": " + F(b) + " (Noita " + F(a) + ")";
                if ((a >= NoHarm) != (b >= NoHarm))
                    Does(d);   // one harms this way, the other does not
                else if (Math.Abs(a - b) > DamageTolerance * Math.Max(a, NoHarm))
                    v.Differences.Add(d);
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

        /// <summary>"behaviour: N of M (...); matching Noita: N of M (...)": casts that do what Noita's do (PC-30, the
        /// coverage number), then casts that match in the numbers too; per suite (single, mod, combo).</summary>
        public static string Summary(IList<ProbeVerdict> verdicts)
        {
            string Count(Func<ProbeVerdict, bool> ok)
            {
                string Part(IEnumerable<ProbeVerdict> vs) { var l = vs.ToList(); return l.Count(ok) + " of " + l.Count; }
                var suites = verdicts.GroupBy(x => x.Name.Split(':')[0]).Select(g => g.Key + " " + Part(g));
                return Part(verdicts) + " (" + string.Join(", ", suites) + ")";
            }
            return "behaviour: " + Count(x => x.BehavesLike) + "; matching Noita: " + Count(x => x.Matches);
        }
    }
}
