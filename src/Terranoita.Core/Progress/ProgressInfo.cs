using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terranoita.Generated;

namespace Terranoita.Progress
{
    /// <summary>
    /// Tooltip lines of the progress window for a known entry, from the generated tables (no name: the game shows it,
    /// translated from the player's Noita). Numbers as the game uses them: creature life = noita_hp x tier hp_mult,
    /// attack damage = Noita damage x tier dmg_mult (noita_hp and attack damage are in Noita's shown units already),
    /// spell damage = Noita damage x 25, liquid touch damage per second = touch_damage x 25 x 60.
    /// </summary>
    public static class ProgressInfo
    {
        /// <summary>Same as the game's Spawning.PreHardmodeMaxLife: tougher creatures spawn only in hardmode.</summary>
        public const int PreHardmodeMaxLife = 1000;

        public static List<string> Lines(string category, string id)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(id))
                return lines;
            switch (category)
            {
                case ProgressBook.Creatures: Creature(id, lines); break;
                case ProgressBook.Liquids: Liquid(id, lines); break;
                case ProgressBook.Spells: Spell(id, lines); break;
            }
            return lines;
        }

        /// <summary>Longest list in a tooltip (places, reactions); the rest is "...".</summary>
        public const int MaxListLines = 6;

        // a zone id as a short place name: surface_forest -> Surface forest, cavern_hm -> Cavern
        static string Place(string zoneId)
        {
            string s = zoneId.Replace("_post_plantera", "").Replace("_hm", "").Replace('_', ' ');
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static string N(float v) => v == (int)v ? ((int)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

        // ---- creatures ----

        static void Creature(string id, List<string> lines)
        {
            if (!Defs.Enemy.TryGetValue(id, out var e))
                return;
            Defs.Tier.TryGetValue(e.Tier ?? "", out var tier);
            int life = tier == null ? 0 : Math.Max(1, (int)Math.Round(e.NoitaHp * tier.HpMult));
            if (tier != null)
                lines.Add("Life: " + life);
            bool tough = life > PreHardmodeMaxLife;   // the game spawns these only in hardmode (Spawning.cs, author)
            // one short line per Terraria place (author: one long line ran off the screen)
            var where = new List<string>();
            foreach (var loc in e.SpawnIn ?? new string[0])
                if (Defs.Biome.TryGetValue(loc, out var b) && Defs.Zone.TryGetValue(b.Zone ?? "", out var z) && z.Id != "none")
                {
                    string place = "  " + Place(z.Id) + (z.Hardmode || tough ? ", hardmode" : "") +
                                   (z.AfterBoss != null && z.AfterBoss != "none" ? ", after " + z.AfterBoss : "");
                    if (!where.Contains(place))
                        where.Add(place);
                }
            if (where.Count > 0)
            {
                lines.Add("Lives in:");
                lines.AddRange(where.Take(MaxListLines));
                if (where.Count > MaxListLines)
                    lines.Add("  ...");
            }
            else if (e.SpawnRule != null && e.SpawnRule != "natural")
                lines.Add("Found: " + e.SpawnRule.Replace('_', ' '));
            foreach (var a in Defs.AttacksOf(e))
            {
                Defs.DamageRange(a, out float min, out float max);
                string line = "Attack: " + a.Kind.Replace('_', ' ');
                if (max > 0 && tier != null)
                {
                    int lo = Math.Max(1, (int)Math.Round(min * tier.DmgMult)), hi = Math.Max(1, (int)Math.Round(max * tier.DmgMult));
                    line += ", " + (lo == hi ? lo.ToString(CultureInfo.InvariantCulture) : lo + "-" + hi) + " damage";
                    var kinds = a.Damage.Where(kv => kv.Value != null && kv.Value.Length > 0 && kv.Value.Max() > 0).Select(kv => kv.Key).ToList();
                    if (kinds.Count > 0)
                        line += " (" + string.Join(", ", kinds) + ")";
                }
                if (a.Effect != null && a.Effect != "none")
                    line += ", " + a.Effect.Replace('_', ' ').ToLowerInvariant();
                lines.Add(line);
            }
        }

        // ---- liquids ----

        static Dictionary<string, LiquidDef> _liquids;

        static void Liquid(string id, List<string> lines)
        {
            if (_liquids == null)
                _liquids = Liquids.All.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
            if (!_liquids.TryGetValue(id, out var d))
                return;
            lines.Add(d.Kind == "gas" ? "Gas" : "Liquid" + (d.Viscosity >= 100 ? ", thick" : ""));
            var touch = (d.TouchEffects ?? new string[0]).Distinct().Select(Status).ToList();
            if (touch.Count > 0)
                lines.Add("On touch: " + string.Join(", ", touch));
            if (d.TouchDamage >= 100)
                lines.Add("Touching it kills");
            else if (d.TouchDamage > 0)
                lines.Add("Touch: " + N(d.TouchDamage * 25f * 60f) + " damage per second");
            else if (d.TouchDamage < 0)
                lines.Add("Touch: heals " + N(-d.TouchDamage * 25f * 60f) + " per second");
            var drink = (d.Ingestion ?? new string[0]).Select(s => Status(s.Split(':')[0])).Distinct().ToList();
            if (drink.Count > 0)
                lines.Add("If drunk: " + string.Join(", ", drink));
            if (d.OnFire)
                lines.Add("Burning");
            else if (d.Burnable)
                lines.Add("Burns");
            var with = new List<string>();
            var tags = new HashSet<string>(d.Tags ?? new string[0]);
            foreach (var r in Reactions.All)
            {
                string other = Matches(r.Input1, id, tags) ? r.Input2 : Matches(r.Input2, id, tags) ? r.Input1 : null;
                if (other == null || other == "air")
                    continue;
                string text = "with " + Plain(other) + " -> " + Plain(r.Output1) + (r.Output2 != null && r.Output2 != "air" && r.Output2 != r.Output1 ? " + " + Plain(r.Output2) : "");
                if (!with.Contains(text))
                    with.Add(text);
            }
            if (with.Count > 0)
            {
                lines.Add("Reacts:");
                lines.AddRange(with.Take(MaxListLines).Select(w => "  " + w));
                if (with.Count > MaxListLines)
                    lines.Add("  ...");
            }
        }

        static bool Matches(string input, string id, HashSet<string> tags) =>
            !string.IsNullOrEmpty(input) && (input[0] == '[' ? tags.Contains(input.Trim('[', ']')) : input == id);

        static string Plain(string material) => (material ?? "").Trim('[', ']').Replace('_', ' ');

        // a status id as text (the game may show Noita's translated status name instead)
        static string Status(string id) => (id ?? "").Replace('_', ' ').ToLowerInvariant();

        // ---- spells ----

        static Dictionary<string, SpellDef> _spells;
        static Dictionary<string, SpellProjectileDef> _shots;

        static void Spell(string id, List<string> lines)
        {
            if (_spells == null)
                _spells = SpellTable.All.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            if (_shots == null)
                _shots = SpellProjectiles.All.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            if (!_spells.TryGetValue(id, out var s))
                return;
            lines.Add("Type: " + (s.Type ?? "").Replace('_', ' '));
            lines.Add("Mana: " + N(s.Mana));
            if (s.MaxUses >= 0)
                lines.Add("Uses: " + s.MaxUses);
            foreach (var file in (s.Projectiles ?? new string[0]).Concat(s.TriggerFile != null && s.TriggerFile != "none" ? new[] { s.TriggerFile } : new string[0]).Distinct())
            {
                if (!_shots.TryGetValue(file, out var p))
                    continue;
                float dmg = (p.Damage + p.TypedDamage) * 25f;
                if (dmg > 0)
                    lines.Add("Damage: " + N((float)Math.Round(dmg, 1)));
                if (p.ExplosionRadius > 0 && p.ExplosionDamage > 0)
                    lines.Add("Explosion: " + N((float)Math.Round(p.ExplosionDamage * 25f, 1)) + " damage, radius " + N(p.ExplosionRadius));
                break;   // the first projectile is what the spell is known for
            }
            float delay = s.ConfigAdd != null && s.ConfigAdd.TryGetValue("fire_rate_wait", out var fw) ? fw : 0;
            if (delay != 0)
                lines.Add("Cast delay: " + (delay > 0 ? "+" : "") + N((float)Math.Round(delay / 60f, 2)) + " s");
            if (s.ReloadAdd != 0)
                lines.Add("Recharge: " + (s.ReloadAdd > 0 ? "+" : "") + N((float)Math.Round(s.ReloadAdd / 60f, 2)) + " s");
            if (s.Draws > 0 && s.Type == "draw_many")
                lines.Add("Casts " + s.Draws + " spells at once");
        }
    }
}
