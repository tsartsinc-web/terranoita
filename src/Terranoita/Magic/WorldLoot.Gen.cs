using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;
using Terraria.GameContent.Generation;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita's loot inside Terraria's own world generation (design/worldgen_plan.md sections 1 and 4): a pass after
    /// "Final Cleanup" (every chest exists by then) puts the cave wand and potion altars and fills each chest by Noita's
    /// own chest script (chest_random.lua, chest_random_super.lua for the deepest chests: drop_random_reward), keeping
    /// Terraria's items. The world's first save writes .wld.magic, so the load-time fill never runs for it.
    /// </summary>
    public static partial class WorldLoot
    {
        static bool _generated;

        [Hook("worldgen_passes")]
        [HarmonyPatch(typeof(WorldGen), "AddPasses")]
        static class PassesPatch
        {
            static void Postfix()
            {
                try
                {
                    var gen = Traverse.Create(typeof(WorldGen)).Field("_generator").GetValue<WorldGenerator>();
                    var passes = gen == null ? null : Traverse.Create(gen).Field("_passes").GetValue<List<GenPass>>();
                    if (passes == null)
                    {
                        Entry.Log("worldgen: no pass list, Noita's loot comes when the world is first loaded");
                        return;
                    }
                    int at = passes.FindIndex(p => p.Name == GenPassNameID.FinalCleanup);
                    passes.Insert(at < 0 ? passes.Count : at + 1, new PassLegacy("Terranoita: loot", (progress, config) => Generate(progress)));
                }
                catch (Exception ex) { Entry.Error("worldgen passes", ex); }
            }
        }

        static void Generate(GenerationProgress progress)
        {
            _generated = false;
            if (!NoitaArt.Ready)
            {
                Entry.Log("worldgen: Noita's files not read yet, its loot comes when the world is first loaded");
                return;
            }
            try
            {
                progress.Message = "Noita's loot";
                Spots.Clear();
                PlaceWands();
                PlaceFlaskAltars();
                PlaceMoreChests();
                FillChestsByNoita();
                _generated = true;
            }
            catch (Exception ex)
            {
                // the world is still made; WorldLoot.Load fills it on first load
                Spots.Clear();
                Entry.Error("worldgen loot", ex);
            }
        }

        /// <summary>More chests than Terraria makes (author 2026-10-10): half as many again (at least 30), buried in the
        /// caves between the surface and the underworld, not next to other chests; wooden above the cavern layer, gold
        /// below. Noita's chest script fills them with the rest (FillChestsByNoita).</summary>
        static void PlaceMoreChests()
        {
            int before = 0;
            for (int i = 0; i < Main.maxChests; i++)
            {
                var c = Main.chest[i];
                if (c != null && Physics.Mats.InWorld(c.x, c.y) && Main.tile[c.x, c.y].active() &&
                    (Main.tile[c.x, c.y].type == TileID.Containers || Main.tile[c.x, c.y].type == TileID.Containers2))
                    before++;
            }
            int want = Math.Max(30, before / 2), made = 0;
            int top = (int)Main.worldSurface + 20, bottom = Main.maxTilesY - 250;
            for (int tries = 0; made < want && tries < want * 40 && bottom > top; tries++)
            {
                int x = WorldGen.genRand.Next(100, Main.maxTilesX - 100), y = WorldGen.genRand.Next(top, bottom);
                if (Main.tile[x, y].active())
                    continue;   // a cave, not inside the rock
                int style = y < Main.rockLayer ? 0 : 1;
                if (WorldGen.AddBuriedChest(x, y, 0, true, style, false, TileID.Containers))
                    made++;
            }
            Entry.Log("worldgen: " + made + " more chests (Terraria made " + before + ")");
        }

        const double EmptyWandChance = 0.15;

        const string ChestScript = "data/scripts/items/chest_random.lua", SuperChestScript = "data/scripts/items/chest_random_super.lua";

        /// <summary>Every Terraria chest gets what Noita's chest drops (drop_random_reward) in its empty slots.</summary>
        static void FillChestsByNoita()
        {
            var normal = new NoitaBiomeSpawns(NoitaArt.ReadText, WorldGen.genRand.Next());
            normal.Load(ChestScript);
            var super = new NoitaBiomeSpawns(NoitaArt.ReadText, WorldGen.genRand.Next());
            super.Load(SuperChestScript);
            int chests = 0, wands = 0, spells = 0, flasks = 0, other = 0, emptyWands = 0;
            var skipped = new Dictionary<string, int>();
            var deep = new List<Chest>();
            for (int i = 0; i < Main.maxChests; i++)
            {
                var c = Main.chest[i];
                if (c == null || !Physics.Mats.InWorld(c.x, c.y))
                    continue;
                var t = Main.tile[c.x, c.y];
                if (!t.active() || (t.type != TileID.Containers && t.type != TileID.Containers2))
                    continue;
                int level = ChestLevel(t, c.y);
                if (level >= 4)
                    deep.Add(c);
                // Noita's better chest (chest_random_super) for shadow, lihzahrd and biome chests
                var b = level >= 5 ? super : normal;
                float nx = c.x * 16 / Px, ny = c.y * 16 / Px;
                var at = new Vector2(c.x * 16 + 16, c.y * 16);
                chests++;
                foreach (var p in b.Call("drop_random_reward", nx, ny, b.NewEntity(nx, ny), nx, ny))
                {
                    // a full chest makes nothing more (a wand made here would stay in WandStore unused)
                    if (Array.FindIndex(c.item, it => it == null || it.IsAir) < 0)
                        break;
                    foreach (var item in Reward(p, at, skipped))
                    {
                        int slot = Array.FindIndex(c.item, it => it == null || it.IsAir);
                        if (slot < 0)
                            break;
                        c.item[slot] = item;
                        if (MagicItems.IsWand(item)) wands++;
                        else if (MagicItems.IsSpell(item)) spells++;
                        else if (MagicItems.IsFlask(item)) flasks++;
                        else other++;
                    }
                }
                // a chance of an empty wand (author 2026-10-10): one Noita's wand script of the chest's level made, its
                // spells taken out (all its slots free)
                int free = Array.FindIndex(c.item, it => it == null || it.IsAir);
                if (free >= 0 && WorldGen.genRand.NextDouble() < EmptyWandChance)
                {
                    var made = Maker.MakeEntity("data/entities/items/wand_level_0" + Math.Max(1, Math.Min(6, level)) + ".xml", nx, ny);
                    made.Spells.Clear();
                    made.AlwaysCast.Clear();
                    c.item[free] = MagicItems.MakeWand(WandWindow.Store(made));
                    emptyWands++;
                }
            }
            foreach (var e in normal.Errors.Concat(super.Errors).Distinct().Take(5))
                Entry.Log("worldgen chest script: " + e);
            // spells Noita never spawns by level: each once in a deep chest, as the load-time fill does
            int rare = 0;
            if (deep.Count > 0)
                foreach (var a in Maker.Actions())
                {
                    if (a.levels.Select((l, k) => k < a.probs.Length && a.probs[k] > 0).Any(x => x))
                        continue;
                    var c = deep[WorldGen.genRand.Next(deep.Count)];
                    int slot = Array.FindIndex(c.item, it => it == null || it.IsAir);
                    if (slot < 0)
                        continue;
                    c.item[slot] = MagicItems.MakeSpell(a.id);
                    rare++;
                }
            Entry.Log("worldgen: Noita's chests: " + chests + " chests, " + wands + " wands, " + emptyWands + " empty wands, " + spells + " spells, " + flasks +
                      " flasks, " + other + " other items, " + rare + " rare spells in deep chests" +
                      (skipped.Count > 0 ? "; not made yet: " + string.Join(", ", skipped.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " x" + kv.Value)) : ""));
        }

        static readonly Dictionary<string, XmlEntity> Entities = new Dictionary<string, XmlEntity>(StringComparer.OrdinalIgnoreCase);

        static XmlEntity EntityOf(string file)
        {
            if (!Entities.TryGetValue(file, out var e))
            {
                try { e = NoitaArt.ReadText(file) == null ? null : NoitaEntityXml.Load(file, NoitaArt.ReadText); }
                catch (Exception ex) { Entry.Error("chest reward " + file, ex); }
                Entities[file] = e;
            }
            return e;
        }

        /// <summary>A Terraria item for what Noita's chest script placed; nothing (and counted) for what we do not make.</summary>
        static IEnumerable<Item> Reward(Placement p, Vector2 at, Dictionary<string, int> skipped)
        {
            if (p.Kind == "spell")
                return new[] { MagicItems.MakeSpell(p.File.ToUpperInvariant()) };
            string file = p.File ?? "";
            string name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (p.Kind == "entity" && file.StartsWith("data/entities/items/wand", StringComparison.OrdinalIgnoreCase))
            {
                var w = WandWindow.Store(Maker.MakeEntity(file, p.X, p.Y));
                return new[] { MagicItems.MakeWand(w) };
            }
            var e = p.Kind == "entity" ? EntityOf(file) : null;
            // gold nuggets: their gold_value (VariableStorageComponent); ours: 1 Noita gold = 1 silver coin
            string gold = e?.Components.FirstOrDefault(c => c.Type == "VariableStorageComponent" && c.Get("name") == "gold_value")?.Get("value_int");
            if (gold != null && int.TryParse(gold, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
                return Coins(value);
            // flasks and pouches: Noita's own potion script of that file (potion.lua, potion_secret.lua...)
            string script = e?.Components.Where(c => c.Type == "LuaComponent").Select(c => c.Get("script_source_file"))
                .FirstOrDefault(s => s != null && s.StartsWith("data/scripts/items/", StringComparison.Ordinal) &&
                                     (s.Contains("potion") || s.Contains("powder")));
            if (script != null || name == "potion")
                return new[] { Flasks.Make(at, script ?? "data/scripts/items/potion.lua") };
            // ours: Noita's max-hp hearts are Terraria's Life Crystal; the full-hp heart a healing potion; its bomb a bomb
            switch (name)
            {
                case "heart":
                case "heart_better":
                    return new[] { new Item(ItemID.LifeCrystal) };
                case "heart_fullhp":
                    return new[] { new Item(ItemID.HealingPotion) };
                case "bomb_small":
                    return new[] { new Item(ItemID.Bomb) };
            }
            string key = p.Kind == "material" ? "material " + file : name;
            skipped[key] = skipped.TryGetValue(key, out int n) ? n + 1 : 1;
            return Enumerable.Empty<Item>();
        }

        /// <summary>Silver coins (gold coins for every 100).</summary>
        static IEnumerable<Item> Coins(int silver)
        {
            if (silver >= 100)
                yield return Stack(ItemID.GoldCoin, silver / 100);
            if (silver % 100 > 0)
                yield return Stack(ItemID.SilverCoin, silver % 100);
        }

        static Item Stack(int type, int stack)
        {
            var i = new Item(type);
            i.stack = stack;
            return i;
        }
    }
}
