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
        /// <summary>Noita perks the caster takes before the cast ("perk:" tests, PC-37 perk x spell).</summary>
        public string[] Perks = new string[0];
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
        const string FitBase = "DISC_BULLET_BIG";
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

        // PC-37 synergy: pairs of the most used modifiers on a Spark Bolt (190), trigger chains, perks that change shots on
        // the three modifier bases (ids checked in gun_actions.lua / perk_list.lua 2026-10-10)
        static readonly string[] PairModifiers =
        {
            "HOMING", "SPEED", "HEAVY_SHOT", "DAMAGE", "CRITICAL_HIT", "PIERCING_SHOT", "BOUNCE", "SPIRALING_SHOT", "ORBIT_SHOT",
            "SINEWAVE", "ACCELERATING_SHOT", "FIRE_TRAIL", "POISON_TRAIL", "ELECTRIC_CHARGE", "EXPLOSIVE_PROJECTILE",
            "ADD_TRIGGER", "ADD_TIMER", "LIFETIME", "CHAOTIC_ARC", "FREEZE",
        };

        static readonly string[][] Chains =
        {
            new[] { "LIGHT_BULLET_TRIGGER", "LIGHT_BULLET_TRIGGER", "BOUNCY_ORB" },
            new[] { "LIGHT_BULLET_TRIGGER", "LIGHT_BULLET_TIMER", "BOUNCY_ORB" },
            new[] { "LIGHT_BULLET_TIMER", "LIGHT_BULLET_TRIGGER", "GRENADE" },
            new[] { "LIGHT_BULLET_TIMER", "LIGHT_BULLET_TIMER", "LIGHT_BULLET_TIMER", "BOUNCY_ORB" },
            new[] { "LIGHT_BULLET_TRIGGER", "BURST_2", "BOUNCY_ORB", "GRENADE" },
            new[] { "LIGHT_BULLET_TRIGGER", "SCATTER_3", "LIGHT_BULLET", "LIGHT_BULLET", "LIGHT_BULLET" },
            new[] { "LIGHT_BULLET_TRIGGER_2", "LIGHT_BULLET", "BOUNCY_ORB" },
            new[] { "LIGHT_BULLET_TRIGGER", "HOMING", "BOUNCY_ORB" },
            new[] { "HOMING", "LIGHT_BULLET_TRIGGER", "BOUNCY_ORB" },
            new[] { "BURST_2", "LIGHT_BULLET_TRIGGER", "BOUNCY_ORB", "LIGHT_BULLET_TRIGGER", "GRENADE" },
            new[] { "ADD_TRIGGER", "LIGHT_BULLET", "ADD_TRIGGER", "BOUNCY_ORB", "GRENADE" },
            new[] { "ADD_DEATH_TRIGGER", "BOUNCY_ORB", "ADD_TIMER", "GRENADE", "LIGHT_BULLET" },
            new[] { "BULLET_TRIGGER", "BULLET_TIMER", "LIGHT_BULLET" },
            new[] { "SPITTER_TIER_2_TIMER", "LIGHT_BULLET_TRIGGER", "BOUNCY_ORB" },
            new[] { "DIVIDE_2", "LIGHT_BULLET_TRIGGER", "BOUNCY_ORB" },
            new[] { "ADD_TRIGGER", "DIVIDE_3", "LIGHT_BULLET", "BOUNCY_ORB" },
        };

        public static readonly string[] ShotPerks =
        {
            "CRITICAL_HIT", "PROJECTILE_HOMING", "PROJECTILE_HOMING_SHOOTER", "EXTRA_KNOCKBACK", "LOWER_SPREAD", "LOW_RECOIL",
            "BOUNCE", "FAST_PROJECTILES", "DUPLICATE_PROJECTILE", "LASER_AIM", "PERSONAL_LASER", "GLASS_CANNON",
            "LOW_HP_DAMAGE_BOOST", "ELECTRICITY",
        };

        /// <summary>PC-37 sets: "pair:" two modifiers on a Spark Bolt, "chain:" trigger chains, "perk:" a perk and a base
        /// spell (perks not in the player's perk_list.lua are left out).</summary>
        public static List<ProbeTest> Synergy(IEnumerable<string> actionIds, IEnumerable<string> perkIds)
        {
            var known = new HashSet<string>(actionIds, StringComparer.Ordinal);
            var perks = new HashSet<string>(perkIds, StringComparer.Ordinal);
            var tests = new List<ProbeTest>();
            var mods = PairModifiers.Where(known.Contains).ToList();
            for (int i = 0; i < mods.Count; i++)
                for (int j = i + 1; j < mods.Count; j++)
                    tests.Add(new ProbeTest { Name = "pair:" + mods[i] + "+" + mods[j], Deck = new[] { mods[i], mods[j], Base } });
            foreach (var deck in Chains)
                if (deck.All(known.Contains))
                    tests.Add(new ProbeTest { Name = "chain:" + string.Join("+", deck), Deck = deck });
            foreach (var perk in ShotPerks.Where(perks.Contains))
                foreach (var b in ModifierBases)
                    tests.Add(new ProbeTest { Name = "perk:" + perk + "+" + b, Deck = new[] { b }, Perks = new[] { perk } });
            return tests;
        }

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
            // rows to fit an engine formula with: each homing modifier on a shot with no friction and no gravity
            // (disc_bullet_big.xml), so its path shows the homing alone (design/magic_plan.md: the homing fit)
            foreach (var (id, type) in list.Where(a => a.type == 2 && a.id.StartsWith("HOMING", StringComparison.Ordinal)))
                if (known.Contains(FitBase))
                    tests.Add(new ProbeTest { Name = "fit:" + id + "+" + FitBase, Deck = new[] { id, FitBase } });
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
                if (t.Perks.Length > 0)
                    sb.Append(", perks = { ").Append(string.Join(", ", t.Perks.Select(d => "\"" + d + "\""))).Append(" }");
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
