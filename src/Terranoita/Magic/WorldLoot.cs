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
    public static partial class WorldLoot
    {
        sealed class Spot
        {
            public int X, Y, Level, Wand = -1;   // the air tile on the floor; wand (or flask) number once made, -3 taken
            public bool Flask;                   // Noita's potion altar (potion_altar.png) instead of the wand altar
        }

        static readonly List<Spot> Spots = new List<Spot>();
        static string FileOf => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".magic";
        static LuaWandMaker _maker;
        static LuaWandMaker Maker => _maker ?? (_maker = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next()));

        // the world whose loot is loaded: a world is saved once while Terraria makes it, before it is ever played, and
        // that save must not write an empty file (it made every new world's chests stay empty, 2026-10-08)
        static string _loadedFor;
        const string Version = "3";   // 3: flask pedestals (older files get them once)

        public static void Load()
        {
            Spots.Clear();
            _loadedFor = null;
            var path = FileOf;
            if (path == null || !NoitaArt.Ready)
                return;
            _loadedFor = path;
            var lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            // version 1 files with no wand spot were written by that save: filled now, once
            if (lines.Length > 0 && (lines[0].Trim() == Version || lines.Length > 1))
            {
                foreach (var line in lines.Skip(1))
                {
                    var p = line.Split(' ');
                    if (p.Length >= 4)
                        Spots.Add(new Spot { X = int.Parse(p[0]), Y = int.Parse(p[1]), Level = int.Parse(p[2]), Wand = int.Parse(p[3]), Flask = p.Length > 4 && p[4] == "f" });
                }
                if (lines[0].Trim() != Version)
                {
                    PlaceFlaskAltars();
                    Save();
                }
                return;
            }
            PlaceWands();
            PlaceFlaskAltars();
            FillChests();
            Save();
        }

        public static void Save()
        {
            var path = FileOf;
            // a world just made by Terraria's worldgen (our loot pass): its first save writes what the pass placed
            if (_generated && path != null)
            {
                _loadedFor = path;
                _generated = false;
            }
            if (path == null || !NoitaArt.Ready || path != _loadedFor)
                return;
            try
            {
                File.WriteAllLines(path, new[] { Version }.Concat(Spots.Select(s => s.X + " " + s.Y + " " + s.Level + " " + s.Wand + (s.Flask ? " f" : ""))));
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
            PlaceAltars(want, false);
            Entry.Log("world loot: " + Spots.Count(s => !s.Flask) + " wand altars in the caves (levels " + string.Join(",", Spots.Where(s => !s.Flask).GroupBy(s => s.Level).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())) + ")");
        }

        /// <summary>Noita's potion altars: half as many as the wand altars, a flask filled by potion.lua on each.</summary>
        static void PlaceFlaskAltars()
        {
            int want = Math.Max(4, Main.maxTilesX * 3 / 1000);
            PlaceAltars(want, true);
            Entry.Log("world loot: " + Spots.Count(s => s.Flask) + " potion altars in the caves");
        }

        static void PlaceAltars(int want, bool flask)
        {
            int placed = 0;
            for (int tries = 0; tries < want * 400 && placed < want; tries++)
            {
                int x = WorldGen.genRand.Next(100, Main.maxTilesX - 100);
                int y = WorldGen.genRand.Next((int)Main.worldSurface + 20, Main.UnderworldLayer - 20);
                if (!Air(x, y))
                    continue;
                while (y < Main.UnderworldLayer - 10 && Air(x, y + 1))
                    y++;
                var floor = Main.tile[x, y + 1];
                // room for the pedestal (2 tiles above the floor, 4 wide) and the item on it
                bool room = floor.active() && Main.tileSolid[floor.type];
                for (int dy = 0; dy <= 4 && room; dy++)
                    for (int dx = -2; dx <= 2 && room; dx++)
                        room = Air(x + dx, y - dy);
                if (!room)
                    continue;
                if (Spots.Any(s => Math.Abs(s.X - x) < 60 && Math.Abs(s.Y - y) < 40))
                    continue;
                Spots.Add(new Spot { X = x, Y = y, Level = LevelAt(y), Flask = flask });
                placed++;
            }
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
            int filled = 0, spells = 0, flasks = 0;
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
                // Noita's level 10 (giga holes, nukes...) sometimes in the best chests
                if (level >= 6 && WorldGen.genRand.Next(100) < 15)
                    level = 10;
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
                // Noita's chests hold potions too (chest_random.lua): one in four, filled by Noita's potion.lua
                int free = Array.FindIndex(c.item, it => it == null || it.IsAir);
                if (free >= 0 && WorldGen.genRand.Next(4) == 0)
                {
                    c.item[free] = Flasks.Make(new Microsoft.Xna.Framework.Vector2(c.x * 16, c.y * 16));
                    flasks++;
                    any = true;
                }
                if (any)
                    filled++;
            }
            // spells Noita never spawns by level (instrument notes, IF/ELSE, ALL_SPELLS, DIVIDE_10...): each once in a
            // deep chest (dungeon, shadow, lihzahrd, biome), so every spell of Noita can be found
            int rare = 0;
            if (deep.Count > 0)
                foreach (var a in Maker.Actions())
                {
                    bool spawns = a.levels.Select((l, i) => i < a.probs.Length && a.probs[i] > 0).Any(x => x);
                    if (spawns)
                        continue;
                    var c = deep[WorldGen.genRand.Next(deep.Count)];
                    int slot = Array.FindIndex(c.item, it => it == null || it.IsAir);
                    if (slot < 0)
                        continue;
                    c.item[slot] = MagicItems.MakeSpell(a.id);
                    rare++;
                }
            Entry.Log("world loot: " + spells + " spells, " + flasks + " flasks in " + filled + " chests, " + rare + " rare ones in deep chests");
        }

        // ---- the wands in the caves: made when the player comes near, picked up by touching ----

        // Noita's unique wands (their entity files are a level wand plus own name and picture) and the level they build on
        static readonly (string file, int level)[] Unique =
        {
            ("wand_ruusu", 2), ("wand_kiekurakeppi", 2), ("wand_leukaluu", 3), ("wand_valtikka", 3), ("wand_vasta", 3),
            ("wand_vihta", 3), ("wand_petri", 4), ("wand_arpaluu", 5), ("wand_varpuluuta", 5),
        };

        /// <summary>Which of Noita's wand files a cave wand is: level by depth, unshuffle or better ones sometimes,
        /// a rare unique wand, level 10 wands in the underworld.</summary>
        static string WandFile(Spot s)
        {
            int roll = WorldGen.genRand.Next(100);
            string name;
            if (s.Level >= 6 && s.Y > Main.UnderworldLayer && roll < 30)
                name = roll < 15 ? "wand_level_10" : "wand_unshuffle_10";
            else if (roll < 4 && Unique.Any(u => u.level <= s.Level + 1))
            {
                var pick = Unique.Where(u => u.level <= s.Level + 1).ToArray();
                name = pick[WorldGen.genRand.Next(pick.Length)].file;
            }
            else if (roll < 14)
                name = "wand_level_0" + s.Level + "_better";
            else if (roll < 44)
                name = "wand_unshuffle_0" + s.Level;
            else
                name = "wand_level_0" + s.Level;
            string file = "data/entities/items/" + name + ".xml";
            // a file this Noita lacks (older or newer version): the plain level wand
            return NoitaArt.ReadText(file) != null ? file : "data/entities/items/wand_level_0" + Math.Min(6, Math.Max(1, s.Level)) + ".xml";
        }

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
                if (s.Wand == -1 && dist < 1600)
                {
                    try
                    {
                        s.Wand = s.Flask ? MagicItems.FlaskOf(Flasks.Make(at)).Id : WandWindow.Store(Maker.MakeEntity(WandFile(s), s.X * 16 / 3f, s.Y * 16 / 3f)).Id;
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
                    Main.NewText(p.inventory[free].Name, new Color(200, 160, 255));
                    s.Wand = -3;   // taken: the altar stays, as in Noita
                    changed = true;
                }
            }
            if (changed)
                Save();
        }

        // Noita's altars (data/biome_impl/*_altar_visual.png, 20 x 30 px) at Noita's scale (1 px = 3 Terraria px, as the
        // creatures): the pedestal's top (row 10 of the wand altar, row 15 of the potion altar) 2 tiles above the floor,
        // its foot in the ground; the item rests above it
        const string WandAltar = "data/biome_impl/wand_altar_visual.png", PotionAltar = "data/biome_impl/potion_altar_visual.png";
        const float Px = Noita.Units.PixelScale;

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
                    var screen = new Rectangle((int)Main.screenPosition.X - 128, (int)Main.screenPosition.Y - 128, Main.screenWidth + 256, Main.screenHeight + 256);
                    foreach (var s in Spots)
                    {
                        float floorY = (s.Y + 1) * 16, cx = s.X * 16 + 8;
                        if (!screen.Contains((int)cx, (int)floorY))
                            continue;
                        var altar = NoitaArt.Get(s.Flask ? PotionAltar : WandAltar)?.Texture;
                        int topRow = s.Flask ? 15 : 10;
                        float imageTop = floorY - 32 - topRow * Px;
                        var light = Lighting.GetColor(s.X, s.Y);
                        if (altar != null)
                            sb.Draw(altar, new Vector2(cx - altar.Width * Px / 2f, imageTop) - Main.screenPosition, null, light, 0f, Vector2.Zero, Px, SpriteEffects.None, 0f);
                        var w = s.Wand >= 0 ? WandStore.Wand(s.Wand) : null;
                        var art = w == null ? null : NoitaArt.Get(w.Flask != null ? Flasks.Sprite : w.Sprite);
                        if (art?.Texture == null)
                            continue;
                        var frame = MagicItems.Frame(art);
                        float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2 + s.X) * 3;
                        var at = new Vector2(cx, floorY - 32 - frame.Height - 6 + bob);
                        Lighting.AddLight(at, 0.35f, 0.25f, 0.5f);
                        sb.Draw(art.Texture, at - Main.screenPosition, frame, Color.White, w.Flask != null ? 0f : -0.5f,
                                new Vector2(frame.Width / 2f, frame.Height / 2f), 2f, SpriteEffects.None, 0f);
                    }
                }
                catch (Exception ex) { Entry.Error("cave altars draw", ex); }
            }
        }
    }
}
