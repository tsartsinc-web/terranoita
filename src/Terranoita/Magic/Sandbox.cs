using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_SANDBOX=1 (with TERRANOITA_AUTOTEST=1, no exit; tools/game_test.ps1 -Mode sandbox): a place for the
    /// author to try magic by hand. Plain grassy ground with trees at the player, chests holding every spell of the player's Noita and
    /// every wand Noita's wand files make; no creatures (author).
    /// </summary>
    public static class Sandbox
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_SANDBOX") == "1";
        const int Width = 140, Height = 28;
        static int _x0, _floor;

        static void Log(string s) => Entry.Log("SANDBOX " + s);

        public static void Frame(Player p, int frame)
        {
            if (frame == 60)
            {
                try { Build(p); }
                catch (Exception ex) { Entry.Error("sandbox", ex); }
            }
            NoCreatures();
        }

        static void Build(Player p)
        {
            Main.dayTime = true;
            Main.time = 27000;
            // the most mana a player can have (ManaCap, 15 stars; author: try every spell, the giga ones cost 500-600)
            p.statManaMax = p.statManaMax2 = ManaCap.TotalCap;
            p.statMana = ManaCap.TotalCap;
            _x0 = (int)(p.Center.X / 16) - 12;
            _floor = (int)((p.position.Y + p.height) / 16) + 1;
            // plain ground like a normal forest (author): dirt with grass, a few trees, open air above; the pits and
            // liquids earlier tests left are filled in or removed
            for (int x = _x0 - 1; x <= _x0 + Width; x++)
                for (int y = _floor - Height; y <= _floor + 20; y++)
                {
                    if (!WorldGen.InWorld(x, y, 10))
                        continue;
                    var t = Main.tile[x, y];
                    t.liquid = 0;
                    if (y >= _floor)
                    {
                        t.ClearEverything();
                        t.active(true);
                        t.type = y == _floor ? TileID.Grass : TileID.Dirt;
                        t.wall = y > _floor + 1 ? WallID.DirtUnsafe : (ushort)0;
                    }
                    else
                        t.ClearEverything();
                }
            if (Physics.Patches.On)
                Physics.Fluids.Clear();
            for (int i = 0; i < Main.maxItems; i++)
                Main.item[i].inner.TurnToAir();   // the items earlier tests dropped
            WorldGen.RangeFrame(_x0 - 2, _floor - Height - 1, _x0 + Width + 2, _floor + 21);
            for (int x = _x0 + 30; x < _x0 + Width - 4; x += 9 + Main.rand.Next(6))
                Tree(x, _floor - 1);
            p.position = new Vector2((_x0 + 12) * 16, (_floor - 3) * 16);
            p.velocity = Vector2.Zero;

            // chests: every spell (gun_actions order), then every wand
            var maker = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next());
            var spells = maker.Actions().Select(a => a.id).ToList();
            var wands = new List<Item>();
            foreach (var file in WandFiles())
            {
                try
                {
                    var w = WandWindow.Store(maker.MakeEntity(file, (_x0 + 20) * 16 / 3f, (_floor - 2) * 16 / 3f));
                    wands.Add(MagicItems.MakeWand(w));
                }
                catch (Exception ex) { Log("wand " + file + " not made: " + ex.Message); }
            }
            int cx = _x0 + 4, made = 0;
            for (int i = 0; i < spells.Count; i += 40)
                made += Fill(ref cx, "Spells " + (i / 40 + 1), spells.Skip(i).Take(40).Select(id => MagicItems.MakeSpell(id)).ToList());
            for (int i = 0; i < wands.Count; i += 40)
                made += Fill(ref cx, "Wands " + (i / 40 + 1), wands.Skip(i).Take(40).ToList());
            // a flask of every material Noita's potion.lua can fill one with
            var flasks = maker.PotionMaterials().Select(m => MagicItems.MakeFlask(m, Flasks.Capacity)).ToList();
            made += Fill(ref cx, "Flasks", flasks.Take(40).ToList());
            Log("arena at " + _x0 + "," + _floor + ": " + spells.Count + " spells, " + wands.Count + " wands, " + flasks.Count + " flasks in " + made + " chests");
        }

        /// <summary>A tree as the forest grows them: a sapling on the grass, grown at once.</summary>
        static void Tree(int x, int y)
        {
            if (!WorldGen.PlaceTile(x, y, TileID.Saplings, true))
                return;
            if (!WorldGen.GrowTree(x, y) && Main.tile[x, y].type == TileID.Saplings)
                WorldGen.KillTile(x, y, false, false, true);
        }

        /// <summary>Noita's wand entity files: levels, better, unshuffle, level 10, daily, unique, custom.</summary>
        internal static IEnumerable<string> WandFiles()
        {
            var files = NoitaArt.List("data/entities/items/")
                .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Where(f =>
                {
                    string name = System.IO.Path.GetFileName(f);
                    return (name.StartsWith("wand_") && f.Count(c => c == '/') == 3) || f.Contains("/wands/custom/");
                })
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            return files;
        }

        static int Fill(ref int cx, string name, List<Item> items)
        {
            int at = -1;
            for (int tries = 0; tries < 6 && at < 0; tries++, cx += 3)
                at = WorldGen.PlaceChest(cx, _floor - 1, TileID.Containers, false, 0);
            cx += 3;
            if (at < 0)
            {
                Log("no room for chest " + name);
                return 0;
            }
            var c = Main.chest[at];
            c.name = name;
            for (int k = 0; k < items.Count && k < c.item.Length; k++)
                c.item[k] = items[k];
            return 1;
        }

        /// <summary>No creatures in the sandbox (author): every hostile one is removed.</summary>
        static void NoCreatures()
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && !n.friendly && !n.townNPC)
                    n.active = false;
            }
        }
    }
}
