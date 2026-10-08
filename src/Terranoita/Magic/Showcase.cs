using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_SHOWCASE=1 (game_test -Mode showcase): pictures for the Melty page (author). Noon, Noita's creatures
    /// around the player, then scenes of Noita's magic cast at them and Noita's liquids, each caught at its busiest
    /// moment (%LOCALAPPDATA%/Terranoita/shots/show_*.png); the UI is hidden except for the wand window scene.
    /// </summary>
    public static class Showcase
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_SHOWCASE") == "1";
        const int Start = 180, Each = 360;
        // a wand per scene, the side it aims at (tiles; + right), and when its pictures are taken (frames into the scene)
        static readonly (string name, string[] spells, int aim, int[] shots)[] Scenes =
        {
            ("fireballs", new[] { "DIVIDE_10", "FIREBALL" }, 11, new[] { 40, 75, 120 }),
            ("lightning", new[] { "ARC_ELECTRIC", "BURST_2", "LIGHT_BULLET", "LIGHT_BULLET" }, -10, new[] { 30, 60, 100 }),
            ("bombs", new[] { "BURST_3", "GRENADE", "FIREBALL", "ACIDSHOT" }, 9, new[] { 35, 70, 110 }),
            ("black_hole", new[] { "BLACK_HOLE" }, 7, new[] { 50, 100, 160 }),
            ("liquids", null, -7, new[] { 90, 160, 240 }),
            ("wand_window", null, 0, new[] { 40 }),
        };
        public static int Length => Start + Scenes.Length * Each;

        static void Log(string s) => Entry.Log("SHOWCASE " + s);

        public static void Frame(Player p, int frame)
        {
            p.statLife = p.statLifeMax2;
            p.statManaMax = 400;
            p.statMana = 400;
            Main.GameZoomTarget = 1.8f;   // closer: Noita's creatures and spells fill the picture (the game resets it)
            if (frame == 20)
            {
                FindSpots();
                MoveTo(p, 0);
            }
            if (frame == 60)
            {
                Main.dayTime = true;
                Main.time = 27000;
                Main.hideUI = true;
                Log("noon");
            }
            if (frame == Start - 20)
                Screenshot.Request("show_creatures");
            if (frame < Start || frame >= Length)
            {
                Casting.TestFire = false;
                if (frame == Length)
                    Log("done");
                return;
            }
            int k = (frame - Start) / Each, t = (frame - Start) % Each;
            var s = Scenes[k];
            if (t == 0)
            {
                Log("scene " + s.name);
                Casting.TestFire = false;
                SpellShots.Clear();
                if (k > 0)
                    MoveTo(p, k);   // untouched ground and fresh creatures for every scene
                if (s.spells != null)
                {
                    var w = WandStore.NewWand();
                    w.Name = "showcase " + s.name; w.Sprite = "data/items_gfx/handgun.xml"; w.CastDelay = 12; w.RechargeTime = 40; w.SpellsPerCast = 1;
                    w.Slots = s.spells.ToArray();
                    w.Uses = w.Slots.Select(_ => -1).ToArray();
                    p.inventory[1] = MagicItems.MakeWand(w);
                    p.selectedItemState.Select(1);
                }
                if (s.name == "wand_window")
                {
                    Main.hideUI = false;
                    WandWindow.SpellSlots[0] = MagicItems.MakeSpell("BOMB", 1);
                    WandWindow.SpellSlots[1] = MagicItems.MakeSpell("FIREBALL");
                    WandWindow.SpellSlots[2] = MagicItems.MakeSpell("BLACK_HOLE", 0);
                    WandWindow.SpellSlots[3] = MagicItems.MakeSpell("DIVIDE_10");
                    WandWindow.TestOpen(true, false);
                }
            }
            p.direction = Math.Sign(s.aim == 0 ? 1 : s.aim);
            if (s.spells != null && t < Each - 60)
            {
                Casting.TestFire = true;
                // at the nearest creature on that side, else straight ahead
                var target = Main.npc.Where(n => n.active && !n.townNPC && n.life > 0 && Math.Sign(n.Center.X - p.Center.X) == Math.Sign(s.aim) &&
                                                 Math.Abs(n.Center.X - p.Center.X) < 40 * 16)
                                     .OrderBy(n => Math.Abs(n.Center.X - p.Center.X)).FirstOrDefault();
                Casting.TestAim = s.name != "black_hole" && target != null ? target.Center : p.Center + new Vector2(s.aim * 16, s.name == "black_hole" ? 0 : -8);
            }
            else
                Casting.TestFire = false;
            if (s.name == "liquids" && t < 150)
            {
                // Noita's liquids poured from above meet on the ground: acid, liquid fire, water, blood
                int x = (int)(p.Center.X / 16) + s.aim, y = (int)(p.position.Y / 16) - 6;
                Physics.Fluids.Add(x - 2, y, "acid", 255);
                Physics.Fluids.Add(x + 1, y - 1, "liquid_fire", 255);
                Physics.Fluids.Add(x + 4, y, "water", 255);
                Physics.Fluids.Add(x - 5, y - 1, "blood", 255);
            }
            if (s.shots.Contains(t))
                Screenshot.Request("show_" + s.name + "_" + t);
            if (s.name == "wand_window" && t == Each - 1)
                WandWindow.TestOpen(false, false);
        }

        /// <summary>The test world's spawn holds the test arenas, pits and chests (author: "remove the test zones"): the
        /// pictures are taken on untouched grass far away, on flat ground with open sky.</summary>
        static System.Collections.Generic.List<Point> _spots;

        /// <summary>Flat grass with open sky, at least 250 tiles from spawn, flattest first, 80+ tiles apart: each scene
        /// gets untouched ground (the spells of one scene dig pits).</summary>
        static void FindSpots()
        {
            var all = new System.Collections.Generic.List<(int x, int y, int score)>();
            int seen = 0;
            for (int x = 80; x < Main.maxTilesX - 80; x += 13)
            {
                if (Math.Abs(x - Main.spawnTileX) < 250)
                    continue;
                seen++;
                int[] top = Enumerable.Range(x - 20, 41).Select(Surface).ToArray();
                if (top.Any(y => y < 0) || Main.tile[x, top[20]].type != Terraria.ID.TileID.Grass)
                    continue;
                all.Add((x, top[20], top.Max() - top.Min()));
            }
            _spots = new System.Collections.Generic.List<Point>();
            foreach (var c in all.OrderBy(c => c.score))
                if (_spots.All(sp => Math.Abs(sp.X - c.x) >= 80))
                    _spots.Add(new Point(c.x, c.y));
            Log(_spots.Count + " clean places of " + seen + " looked at");
        }

        static void MoveTo(Player p, int k)
        {
            if (_spots.Count == 0)
                return;
            var sp = _spots[k % _spots.Count];
            p.Teleport(new Vector2(sp.X * 16, sp.Y * 16 - p.height - 2), -1);
            p.velocity = Vector2.Zero;
            for (int i = 0; i < Main.maxNPCs; i++)
                if (Main.npc[i].active && !Main.npc[i].townNPC)
                    Main.npc[i].active = false;
            Spawn(p);
        }

        /// <summary>The first solid tile from the sky down at column x, with no liquid above; -1 when there is none.</summary>
        static int Surface(int x)
        {
            for (int y = 40; y < Main.worldSurface + 30; y++)
            {
                var t = Main.tile[x, y];
                if (t.liquid > 0)
                    return -1;
                if (t.active() && Main.tileSolid[t.type] && !Main.tileSolidTop[t.type])
                    return y;
            }
            return -1;
        }

        static void Spawn(Player p)
        {
            int dir = p.direction;
            foreach (var (id, tiles) in new[] { ("shotgunner_weak", 9), ("miner_weak", 13), ("firemage_weak", 16), ("zombie_weak", -7), ("thundermage", -12), ("acidshooter_weak", -15) })
            {
                var def = Enemies.All.FirstOrDefault(e => e.Id == id);
                if (def == null)
                    continue;
                p.direction = Math.Sign(tiles);
                DebugTools.SpawnInFront(def, Math.Abs(tiles));
            }
            p.direction = dir;
        }
    }
}
