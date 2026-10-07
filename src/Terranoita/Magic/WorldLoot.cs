using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita's loot in a Terraria world, put in once per world (author): wands lying in the caves, better the deeper
    /// (Noita's wand_level_0N.lua scripts make them when the player first comes near), and spells in the chests,
    /// better the rarer the chest (Noita's spawn tables of that level). Kept in &lt;world&gt;.wld.magic.
    /// </summary>
    public static class WorldLoot
    {
        sealed class Spot
        {
            public int X, Y, Level, Wand = -1;   // tile of the floor under it; wand number once made
        }

        static readonly List<Spot> Spots = new List<Spot>();
        static string FileOf => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".magic";
        static LuaWandMaker _maker;
        static LuaWandMaker Maker => _maker ?? (_maker = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next()));

        public static void Load()
        {
            Spots.Clear();
            var path = FileOf;
            if (path == null || !NoitaArt.Ready)
                return;
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path).Skip(1))
                {
                    var p = line.Split(' ');
                    if (p.Length >= 4)
                        Spots.Add(new Spot { X = int.Parse(p[0]), Y = int.Parse(p[1]), Level = int.Parse(p[2]), Wand = int.Parse(p[3]) });
                }
                return;
            }
            PlaceWands();
            FillChests();
            Save();
        }

        public static void Save()
        {
            var path = FileOf;
            if (path == null || !NoitaArt.Ready)
                return;
            try
            {
                File.WriteAllLines(path, new[] { "1" }.Concat(Spots.Select(s => s.X + " " + s.Y + " " + s.Level + " " + s.Wand)));
            }
            catch (Exception ex) { Entry.Error("world loot save", ex); }
        }

        /// <summary>Noita's level for a depth: the dirt layer 1, then down to the underworld 6.</summary>
        static int LevelAt(int y)
        {
            if (y < Main.rockLayer)
                return 1;
            double f = (y - Main.rockLayer) / Math.Max(1.0, Main.UnderworldLayer - Main.rockLayer);
            return Math.Max(2, Math.Min(6, 2 + (int)(f * 5)));
        }

        static bool Air(int x, int y)
        {
            var t = Main.tile[x, y];
            return !(t.active() && Main.tileSolid[t.type]) && t.liquid == 0;
        }

        static void PlaceWands()
        {
            int want = Math.Max(8, Main.maxTilesX * 6 / 1000);
            for (int tries = 0; tries < want * 400 && Spots.Count < want; tries++)
            {
                int x = WorldGen.genRand.Next(100, Main.maxTilesX - 100);
                int y = WorldGen.genRand.Next((int)Main.worldSurface + 20, Main.UnderworldLayer - 20);
                if (!Air(x, y))
                    continue;
                while (y < Main.UnderworldLayer - 10 && Air(x, y + 1))
                    y++;
                var floor = Main.tile[x, y + 1];
                if (!floor.active() || !Main.tileSolid[floor.type] || !Air(x, y - 1) || !Air(x, y - 2) || !Air(x - 1, y) || !Air(x + 1, y))
                    continue;
                if (Spots.Any(s => Math.Abs(s.X - x) < 60 && Math.Abs(s.Y - y) < 40))
                    continue;
                Spots.Add(new Spot { X = x, Y = y, Level = LevelAt(y) });
            }
            Entry.Log("world loot: " + Spots.Count + " wands in the caves (levels " + string.Join(",", Spots.GroupBy(s => s.Level).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())) + ")");
        }

        /// <summary>A chest's Noita level by its kind: wooden 1, gold 2, ivy/sky/mushroom/marble/granite 3, dungeon 4, shadow 5, lihzahrd and biome 6.</summary>
        static int ChestLevel(Tile t, int y)
        {
            int style = t.frameX / 36;
            if (t.type == TileID.Containers2)
                return 2;
            switch (style)
            {
                case 0: case 5: case 6: case 7: case 8: case 9: case 14: return 1;
                case 1: case 11: case 12: case 15: case 17: return 2;
                case 10: case 13: case 32: case 50: case 51: return 3;
                case 2: return 4;
                case 3: case 4: return 5;
                case 16: return 6;
                default: return style >= 18 && style <= 27 ? 6 : Math.Max(1, LevelAt(y) - 1);
            }
        }

        static void FillChests()
        {
            int filled = 0, spells = 0;
            for (int i = 0; i < Main.maxChests; i++)
            {
                var c = Main.chest[i];
                if (c == null || !Physics.Mats.InWorld(c.x, c.y))
                    continue;
                var t = Main.tile[c.x, c.y];
                if (!t.active() || (t.type != TileID.Containers && t.type != TileID.Containers2))
                    continue;
                int level = ChestLevel(t, c.y);
                int n = WorldGen.genRand.Next(100) < 30 ? 2 : 1;
                bool any = false;
                for (int k = 0; k < n; k++)
                {
                    int slot = Array.FindIndex(c.item, it => it == null || it.IsAir);
                    string id = Maker.RandomAction(level, -1);
                    if (slot < 0 || string.IsNullOrEmpty(id))
                        break;
                    c.item[slot] = MagicItems.MakeSpell(id);
                    spells++;
                    any = true;
                }
                if (any)
                    filled++;
            }
            Entry.Log("world loot: " + spells + " spells in " + filled + " chests");
        }

        // ---- the wands in the caves: made when the player comes near, picked up by touching ----

        public static void Update()
        {
            var p = Main.LocalPlayer;
            if (Spots.Count == 0 || p == null || !p.active || p.dead)
                return;
            bool changed = false;
            for (int i = Spots.Count - 1; i >= 0; i--)
            {
                var s = Spots[i];
                var at = new Vector2(s.X * 16 + 8, s.Y * 16 + 8);
                float dist = Vector2.Distance(at, p.Center);
                if (s.Wand < 0 && dist < 1600)
                {
                    try
                    {
                        string script = "data/scripts/gun/procedural/" + (WorldGen.genRand.Next(100) < 30 ? "wand_unshuffle_0" : "wand_level_0") + s.Level + ".lua";
                        s.Wand = WandWindow.Store(Maker.Make(script, s.X * 16 / 3f, s.Y * 16 / 3f)).Id;
                    }
                    catch (Exception ex) { Entry.Error("cave wand", ex); s.Wand = -2; }
                    changed = true;
                }
                if (s.Wand >= 0 && dist < 40)
                {
                    int free = Enumerable.Range(0, 50).FirstOrDefault(k => p.inventory[k].IsAir);
                    if (!p.inventory[free].IsAir)
                        continue;
                    p.inventory[free] = MagicItems.MakeWand(WandStore.Wand(s.Wand));
                    Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab, at);
                    Main.NewText(MagicItems.WandName(WandStore.Wand(s.Wand)), new Color(200, 160, 255));
                    Spots.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed)
                Save();
        }

        [Hook("cave_wands_draw")]
        [HarmonyPatch(typeof(Main), "DrawItems")]
        static class DrawPatch
        {
            static void Postfix()
            {
                if (Spots.Count == 0 || Main.gameMenu)
                    return;
                try
                {
                    var sb = Main.spriteBatch;
                    var screen = new Rectangle((int)Main.screenPosition.X - 64, (int)Main.screenPosition.Y - 64, Main.screenWidth + 128, Main.screenHeight + 128);
                    foreach (var s in Spots)
                    {
                        var w = s.Wand >= 0 ? WandStore.Wand(s.Wand) : null;
                        var art = w == null ? null : NoitaArt.Get(w.Sprite);
                        var at = new Vector2(s.X * 16 + 8, s.Y * 16 + 10 + (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2 + s.X) * 3);
                        if (art?.Texture == null || !screen.Contains((int)at.X, (int)at.Y))
                            continue;
                        var frame = MagicItems.Frame(art);
                        Lighting.AddLight(at, 0.35f, 0.25f, 0.5f);
                        sb.Draw(art.Texture, at - Main.screenPosition, frame, Color.White, -0.5f, new Vector2(frame.Width / 2f, frame.Height / 2f), 2f, SpriteEffects.None, 0f);
                    }
                }
                catch (Exception ex) { Entry.Error("cave wands draw", ex); }
            }
        }
    }
}
