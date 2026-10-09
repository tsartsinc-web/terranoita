using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>One cast to measure: a wand deck, the same in Noita (the probe mod) and in Terraria (SpellsTest).</summary>
    public sealed class ProbeTest
    {
        public string Name;       // suite:what, e.g. "single:TENTACLE", "mod:HOMING+BOUNCY_ORB", "combo:BURST_2"
        public string[] Deck;
        // a random wand's own stats ("wand:" tests); the other tests use the probe wand's (one spell a cast, no spread)
        public int SpellsPerCast = 1;
        public float Spread, SpeedMultiplier = 1;
        public string[] AlwaysCast = new string[0];
    }

    /// <summary>
    /// The casts design/magic_plan.md Phase 1 compares (PC-22/23): every spell alone (a modifier, draw, utility or other
    /// spell with LIGHT_BULLET after it, so it has something to act on), every modifier on LIGHT_BULLET, BOUNCY_ORB
    /// and GRENADE, and the key draw/trigger combos. Spell types from gun_actions.lua (gun_enums.lua: 0 projectile,
    /// 1 static projectile, 2 modifier, 3 draw many, 4 material, 5 other, 6 utility, 7 passive).
    /// </summary>
    public static class ProbeTests
    {
        public const string Base = "LIGHT_BULLET";
        static readonly string[] ModifierBases = { "LIGHT_BULLET", "BOUNCY_ORB", "GRENADE" };

        // decks that exercise how spells act on each other in a wand (ids checked in gun_actions.lua 2026-10-09)
        static readonly string[][] Combos =
        {
            new[] { "BURST_2", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "BURST_3", "LIGHT_BULLET", "BOUNCY_ORB", "GRENADE" },
            new[] { "BURST_4", "LIGHT_BULLET", "LIGHT_BULLET", "BOUNCY_ORB", "GRENADE" },
            new[] { "SCATTER_2", "LIGHT_BULLET", "LIGHT_BULLET" },
            new[] { "SCATTER_3", "LIGHT_BULLET", "LIGHT_BULLET", "LIGHT_BULLET" },
            new[] { "SCATTER_4", "LIGHT_BULLET", "LIGHT_BULLET", "LIGHT_BULLET", "LIGHT_BULLET" },
            new[] { "ADD_TRIGGER", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "ADD_TIMER", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "ADD_DEATH_TRIGGER", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "ADD_TRIGGER", "LIGHT_BULLET", "BURST_2", "BOUNCY_ORB", "GRENADE" },
            new[] { "DIVIDE_2", "LIGHT_BULLET" },
            new[] { "DIVIDE_3", "LIGHT_BULLET" },
            new[] { "DIVIDE_4", "LIGHT_BULLET" },
            new[] { "DIVIDE_10", "LIGHT_BULLET" },
            new[] { "HOMING", "BURST_2", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "DAMAGE", "HEAVY_SHOT", "LIGHT_BULLET" },
            new[] { "ALPHA", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "GAMMA", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "TAU", "LIGHT_BULLET", "BOUNCY_ORB", "GRENADE" },
            new[] { "OMEGA", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "MU", "HOMING", "DAMAGE", "LIGHT_BULLET" },
            new[] { "PHI", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "SIGMA", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "ZETA", "LIGHT_BULLET" },
        };

        /// <param name="actions">(id, type) of every spell in gun_actions.lua order.</param>
        public static List<ProbeTest> Build(IEnumerable<(string id, int type)> actions)
        {
            var list = actions.ToList();
            var known = new HashSet<string>(list.Select(a => a.id), StringComparer.Ordinal);
            var tests = new List<ProbeTest>();
            foreach (var (id, type) in list)
                tests.Add(new ProbeTest
                {
                    Name = "single:" + id,
                    Deck = type == 0 || type == 1 || type == 4 ? new[] { id } : new[] { id, Base },
                });
            foreach (var (id, type) in list.Where(a => a.type == 2))
                foreach (var b in ModifierBases)
                    tests.Add(new ProbeTest { Name = "mod:" + id + "+" + b, Deck = new[] { id, b } });
            foreach (var deck in Combos)
                if (deck.All(known.Contains))   // a Noita without one of them (older/newer version): left out
                    tests.Add(new ProbeTest { Name = "combo:" + string.Join("+", deck), Deck = deck });
            return tests;
        }

        /// <summary>Noita's own random wands (design/tasks.md PC-30: builds), one test each: the spells in the order its
        /// wand_level_0N.lua added them, not shuffled (a shuffle would differ between the two games), and its spells per
        /// cast, spread, speed and always-cast spells. A wand the script left without spells casts nothing: left out.</summary>
        public static List<ProbeTest> RandomWands(IEnumerable<(int level, int index, MadeWand wand)> wands) =>
            wands.Where(w => w.wand.Spells.Count > 0).Select(w => new ProbeTest
            {
                Name = "wand:" + w.level + "-" + w.index.ToString("00", CultureInfo.InvariantCulture),
                Deck = w.wand.Spells.ToArray(),
                SpellsPerCast = Math.Max(1, w.wand.SpellsPerCast),
                Spread = w.wand.Spread,
                SpeedMultiplier = w.wand.SpeedMultiplier,
                AlwaysCast = w.wand.AlwaysCast.ToArray(),
            }).ToList();

        static string Lua(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The tests as a Lua file for the probe mod: TESTS = { { name = "...", deck = { "...", ... } }, ... }.</summary>
        public static string ToLua(IEnumerable<ProbeTest> tests)
        {
            var sb = new StringBuilder("-- made by tncli probe-tests from the player's gun_actions.lua; do not edit\nTESTS = {\n");
            foreach (var t in tests)
            {
                sb.Append("  { name = \"").Append(t.Name).Append("\", deck = { ")
                  .Append(string.Join(", ", t.Deck.Select(d => "\"" + d + "\""))).Append(" }");
                if (t.SpellsPerCast != 1 || t.Spread != 0 || t.SpeedMultiplier != 1 || t.AlwaysCast.Length > 0)
                    sb.Append(", spells_per_cast = ").Append(t.SpellsPerCast).Append(", spread = ").Append(Lua(t.Spread))
                      .Append(", speed_multiplier = ").Append(Lua(t.SpeedMultiplier)).Append(", always_cast = { ")
                      .Append(string.Join(", ", t.AlwaysCast.Select(d => "\"" + d + "\""))).Append(t.AlwaysCast.Length > 0 ? " }" : "}");
                sb.Append(" },\n");
            }
            return sb.Append("}\n").ToString();
        }
    }
}
