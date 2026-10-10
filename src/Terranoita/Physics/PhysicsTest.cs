using System.Linq;
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_PHYSICS=1 (with TERRANOITA_AUTOTEST=1): next to the player, build small scenes and log
    /// what the block physics does with them: a loose column, a pile, a building on a pillar, a fire with ice next
    /// to it, an explosion. Lines start with "PHYSICS TEST".
    /// </summary>
    public static class PhysicsTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_PHYSICS") == "1";
        public const int Length = 300 + 60 * 30;
        static int _x0, _gy;

        static void Log(string s) => Entry.Log("PHYSICS TEST: " + s);

        static void Place(int x, int y, int type, bool byPlayer) =>
            WorldGen.PlaceTile(x, y, type, true, true, byPlayer ? Main.myPlayer : -1);

        static int Count(int x1, int x2, int y1, int y2, Func<Tile, bool> what)
        {
            int n = 0;
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    if (Main.tile[x, y].active() && what(Main.tile[x, y]))
                        n++;
            return n;
        }

        public static void Frame(Player p, int frame)
        {
            if (frame == 300)
                Setup(p);
            else if (frame == 330)
                Kick();
            else if (frame == 300 + 60 * 5)
                Check("after 5 s");
            else if (frame == 300 + 60 * 25)
                Check("after 25 s");
            else if (frame == 300 + 60 * 10)
            {
                // Terraria's autosave: WorldFile.SaveWorld on a ThreadPool thread while liquids keep flowing
                Log("autosave started with " + Fluids.Count + " liquid cells");
                WorldGen.saveAndPlay();
            }
            else if (frame == 300 + 60 * 16)
            {
                string f = Main.worldPathName + ".fluids";
                Log("after autosave: " + f + " " + (System.IO.File.Exists(f) ? new System.IO.FileInfo(f).Length + " bytes" : "missing") +
                    ", .tmp left " + System.IO.File.Exists(f + ".tmp") + ", .bad " + System.IO.File.Exists(f + ".bad"));
            }
            else if (frame == 300 + 60 * 8)
            {
                foreach (var id in new[] { "WET", "OILED", "SLIMY", "RADIOACTIVE", "POISONED", "BERSERK", "NIGHTVISION", "HP_REGENERATION", "TRIP", "ALCOHOLIC" })
                    Status.Apply(id, 12);
                Log("statuses applied: " + string.Join(", ", Status.Active) + "; hp " + p.statLife);
            }
            else if (frame == 300 + 60 * 14)
                Log("statuses after 6 s: " + string.Join(", ", Status.Active) + "; hp " + p.statLife + "; move speed " + p.moveSpeed.ToString("0.00") + ", on fire " + p.onFire);
            if (frame == 300 + 60 * 18)
                ShockSetup();
            else if (frame == 300 + 60 * 18 + 30)
                Log("electricity: charged " + Electricity.Count + " tiles; in the pool " + Hp(_inPool) + ", outside " + Hp(_outside));
            else if (frame == 300 + 60 * 21)
                Log("electricity after 3 s: charged " + Electricity.Count + " tiles (0 expected); in the pool " + Hp(_inPool) + ", outside " + Hp(_outside));
            if (frame == 340)
                Pour();
            else if (frame == 341 || frame == 346 || frame == 360 || frame == 400)
                Log("smoke at frame " + (frame - 340) + ": " + Fluids.Where("smoke", _x0, _gy));
            else if (frame == 340 + 60 * 3 || frame == 340 + 60 * 12 || frame == 340 + 60 * 24)
                Fluid((frame - 340) / 60 + " s");
        }

        static NPC _inPool, _outside;
        static string Hp(NPC n) => n == null ? "none" : n.TypeName + " hp " + n.life + "/" + n.lifeMax + (n.active ? "" : " (gone)");

        // 15. electricity: a pool of Terraria water with a creature in it and one beside it, lightning into the water
        static void ShockSetup()
        {
            int x1 = _x0 + 166, x2 = _x0 + 174, gy = _gy;
            for (int x = x1 - 4; x <= x2 + 6; x++)
                for (int y = gy - 8; y <= gy + 4; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearEverything();
                    if (y >= gy + 1 || ((x == x1 - 1 || x == x2 + 1) && y >= gy - 4))
                    {
                        t.active(true);
                        t.type = TileID.Stone;
                    }
                    else if (x >= x1 && x <= x2 && y >= gy - 3 && y <= gy)
                    {
                        t.liquid = 255;
                        t.liquidType(LiquidID.Water);
                    }
                }
            WorldGen.RangeFrame(x1 - 4, gy - 8, x2 + 6, gy + 4);
            _inPool = Main.npc[NPC.NewNPC(new Terraria.DataStructures.EntitySource_SpawnNPC(), (x1 + 4) * 16, (gy + 1) * 16, NPCID.Zombie)];
            _outside = Main.npc[NPC.NewNPC(new Terraria.DataStructures.EntitySource_SpawnNPC(), (x2 + 4) * 16, (gy + 1) * 16, NPCID.Zombie)];
            int n = Electricity.Emit(new Microsoft.Xna.Framework.Vector2(x1 * 16 + 8, (gy - 3) * 16 + 8), 1000);
            Log("electricity: misc/electricity.xml (energy 1000) into a 9x4 pool charged " + n + " tiles; in the pool " + Hp(_inPool) + ", outside " + Hp(_outside));
        }

        // 6. acid on a dirt block, 7. oil in a stone basin set alight, 8. smoke, 9. slime poured onto oil (sinks)
        static void Pour()
        {
            for (int x = _x0 + 48; x <= _x0 + 53; x++)
                for (int y = _gy - 3; y <= _gy - 1; y++)
                    Place(x, y, TileID.Dirt, false);
            for (int s = 0; s < 4; s++)
                Fluids.Add(_x0 + 50, _gy - 5 - s, "acid", 255);
            for (int y = _gy - 3; y <= _gy - 1; y++)
            {
                Place(_x0 + 57, y, TileID.Stone, false);
                Place(_x0 + 63, y, TileID.Stone, false);
            }
            for (int s = 0; s < 5; s++)
                Fluids.Add(_x0 + 58 + s, _gy - 1, "oil", 255);
            for (int y = _gy - 3; y <= _gy - 1; y++)
            {
                Place(_x0 + 66, y, TileID.Stone, false);
                Place(_x0 + 70, y, TileID.Stone, false);
            }
            for (int s = 0; s < 3; s++)
                Fluids.Add(_x0 + 67 + s, _gy - 1, "oil", 200);
            for (int s = 0; s < 3; s++)
                Fluids.Add(_x0 + 68, _gy - 6 - s, "slime", 255);
            for (int s = 0; s < 3; s++)
                Fluids.Add(_x0 + 75, _gy - 2 - s, "smoke", 255);
            // liquid + liquid (rows of reactions.json): one stone basin each, the first liquid at the bottom, the second on top
            for (int c = 0; c < Pairs.Length; c++)
            {
                int bx = _x0 + 84 + c * 6;
                for (int y = _gy - 4; y <= _gy - 1; y++)
                {
                    Place(bx, y, TileID.Stone, false);
                    Place(bx + 6, y, TileID.Stone, false);
                }
                for (int s = 1; s <= 5; s++)
                {
                    Fluids.Add(bx + s, _gy - 1, Pairs[c].a, 200);
                    Fluids.Add(bx + s, _gy - 3, Pairs[c].b, 200);
                }
            }
            // 10. two liquids side by side in one basin (slime 5.0 left, oil 1.0 right): no wall between them, the
            // heavier creeps under the lighter (author: "an invisible barrier stays between them")
            int sb = _x0 + 126;
            for (int y = _gy - 4; y <= _gy - 1; y++)
            {
                Place(sb, y, TileID.Stone, false);
                Place(sb + 8, y, TileID.Stone, false);
            }
            for (int y = _gy - 3; y <= _gy - 1; y++)
            {
                for (int x = sb + 1; x <= sb + 3; x++)
                    Fluids.Add(x, y, "slime", 255);
                for (int x = sb + 4; x <= sb + 7; x++)
                    Fluids.Add(x, y, "oil", 255);
            }
            // 11. a full flask shatters in the open: all of it must land (author: less than pouring it out)
            int left = Magic.Flasks.TestShatter("blood", new Vector2((_x0 + 145) * 16 + 8, (_gy - 3) * 16));
            Log("flask of blood shattered: " + left + " units did not land (want 0)");
            // 12. a player's platform in the air with a wooden wall behind it: it holds (author)
            Main.tile[_x0 + 152, _gy - 9].wall = WallID.Wood;
            Place(_x0 + 152, _gy - 9, TileID.Platforms, true);
            Falling.Disturb(_x0 + 152, _gy - 9);
            // and one with no wall behind it: it falls (the scene has a stone wall everywhere: taken away here)
            Main.tile[_x0 + 156, _gy - 9].wall = 0;
            Place(_x0 + 156, _gy - 9, TileID.Platforms, true);
            Falling.Disturb(_x0 + 156, _gy - 9);
            // 13. a flare and a fire arrow shot into a wooden wall: it catches fire where they hit (author)
            for (int y = _gy - 5; y <= _gy - 1; y++)
            {
                Place(_x0 + 159, y, TileID.WoodBlock, true);
                Place(_x0 + 160, y, TileID.WoodBlock, true);
            }
            var src = new Terraria.DataStructures.EntitySource_WorldEvent();
            Projectile.NewProjectile(src, new Vector2((_x0 + 150) * 16, (_gy - 2) * 16 + 8), new Vector2(8, 0), ProjectileID.Flare, 7, 0f, Main.myPlayer);
            Projectile.NewProjectile(src, new Vector2((_x0 + 150) * 16, (_gy - 4) * 16 + 8), new Vector2(10, 0), ProjectileID.FireArrow, 7, 0f, Main.myPlayer);
            Log("poured: cells " + Fluids.Count + ", smoke " + Fluids.Total(_x0 + 70, _x0 + 80, _gy - 10, _gy, "smoke") +
                "; platform on the wall placed " + Main.tile[_x0 + 152, _gy - 9].active());
        }

        static readonly (string a, string b)[] Pairs = { ("water", "radioactive_liquid"), ("blood", "poison"), ("lava", "blood_cold"), ("water", "cement"), ("water", "blood"), ("blood", "water_salt") };

        static void Fluid(string when)
        {
            Log(when + ": liquid pairs: " + string.Join(", ", Pairs.Select(pr => pr.a + "+" + pr.b + " " +
                Fluids.Fired.Where(kv => kv.Key.EndsWith(" " + pr.a + "+" + pr.b) || kv.Key.EndsWith(" " + pr.b + "+" + pr.a)).Sum(kv => kv.Value))) +
                "; cement basin: cement " + Fluids.Total(_x0 + 103, _x0 + 107, _gy - 6, _gy - 1, "cement") + ", water " + Count(_x0 + 103, _x0 + 107, _gy - 6, _gy - 1, tl => tl.liquid > 32) + " tiles, concrete " + Count(_x0 + 103, _x0 + 107, _gy - 6, _gy - 1, tl => tl.active() && tl.type != TileID.Stone) +
                "; blood in the bottom row under Terraria water " + Fluids.Total(_x0 + 109, _x0 + 113, _gy - 1, _gy - 1, "blood") +
                "; blood+water_salt mixed: blood in the top rows " + Fluids.Total(_x0 + 115, _x0 + 119, _gy - 4, _gy - 3, "blood") + ", salt water in the bottom row " + Fluids.Total(_x0 + 115, _x0 + 119, _gy - 1, _gy - 1, "water_salt"));
            int sb = _x0 + 126;
            Log(when + ": side by side basin: bottom row slime " + Fluids.Total(sb + 1, sb + 7, _gy - 1, _gy - 1, "slime") +
                " (right half " + Fluids.Total(sb + 4, sb + 7, _gy - 1, _gy - 1, "slime") + "), oil " + Fluids.Total(sb + 1, sb + 7, _gy - 1, _gy - 1, "oil") +
                "; top row oil " + Fluids.Total(sb + 1, sb + 7, _gy - 3, _gy - 3, "oil") + " (left half " + Fluids.Total(sb + 1, sb + 3, _gy - 3, _gy - 3, "oil") + ")" +
                "; blood from the flask " + Fluids.Total(_x0 + 125, _x0 + 165, _gy - 30, _gy, "blood") +
                "; platform on the wall " + (Main.tile[_x0 + 152, _gy - 9].active() ? "holds" : "FELL") +
                ", without a wall " + (Main.tile[_x0 + 156, _gy - 9].active() ? "still there" : "fell") +
                "; wooden wall hit by a flare and a fire arrow: burning " + Enumerable.Range(_gy - 5, 5).Sum(y => (Fire.BurningAt(_x0 + 159, y) ? 1 : 0) + (Fire.BurningAt(_x0 + 160, y) ? 1 : 0)) +
                ", burnt away " + Enumerable.Range(_gy - 5, 5).Sum(y => (Main.tile[_x0 + 159, y].active() ? 0 : 1) + (Main.tile[_x0 + 160, y].active() ? 0 : 1)) + " of 10" +
                "; liquid swaps since the last check " + Fluids.Swaps + ", lifts out of Terraria's liquid " + Fluids.Bobs + " (settled liquids: few)");
            Fluids.Swaps = 0;
            Fluids.Bobs = 0;
            if (when.StartsWith("3"))
                Fluids.Ignite(_x0 + 60, _gy - 1);
            int dirt = Count(_x0 + 48, _x0 + 53, _gy - 3, _gy - 1, t => t.type == TileID.Dirt);
            Log(when + ": dirt under acid " + dirt + "/18, acid " + Fluids.Total(_x0 + 40, _x0 + 56, _gy - 20, _gy, "acid") +
                ", acid gas " + Fluids.Total(_x0 + 30, _x0 + 66, _gy - 25, _gy, "acid_gas") +
                "; oil in the burning basin " + Fluids.Total(_x0 + 58, _x0 + 62, _gy - 4, _gy - 1, "oil") + " (burning " + Fluids.BurningAt(_x0 + 60, _gy - 1) + ")" +
                "; second basin: bottom row slime " + Fluids.Total(_x0 + 67, _x0 + 69, _gy - 1, _gy - 1, "slime") + ", oil " + Fluids.Total(_x0 + 67, _x0 + 69, _gy - 1, _gy - 1, "oil") +
                "; smoke anywhere near " + Fluids.Total(_x0 - 40, _x0 + 120, _gy - 150, _gy + 5, "smoke") + ", low " + Fluids.Total(_x0 + 72, _x0 + 78, _gy - 5, _gy - 1, "smoke") + ", high " + Fluids.Total(_x0 + 65, _x0 + 85, _gy - 25, _gy - 6, "smoke") +
                "; cells " + Fluids.Count + "; reactions: " + string.Join(", ", Fluids.Fired.OrderByDescending(kv => kv.Value).Take(6).Select(kv => kv.Key + " x" + kv.Value)));
        }

        static void Setup(Player p)
        {
            int px = (int)(p.Center.X / 16);
            _x0 = px + 8;
            // ground under the middle of the test strip
            int gy = (int)(p.position.Y / 16) - 20;
            while (gy < Main.maxTilesY - 50 && !Mats.Solid(_x0 + 20, gy))
                gy++;
            _gy = gy;
            // a flat floor of stone and clear air above, so every scene starts the same
            Fluids.ClearArea(_x0 - 2, gy - 25, _x0 + 162, gy + 2);   // our liquids from earlier runs are saved with the world
            for (int x = _x0 - 2; x <= _x0 + 162; x++)   // to the wooden wall of scene 13
            {
                for (int y = gy - 25; y < gy; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearEverything();
                    t.wall = WallID.Stone;   // author: a background behind the scenes, as in caves
                }
                for (int y = gy; y <= gy + 2; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearTile();
                    t.liquid = 0;
                    Place(x, y, TileID.Stone, false);
                }
            }
            p.Teleport(new Vector2((_x0 + 20) * 16, (gy - 3) * 16 - p.height), -1);
            p.velocity = Vector2.Zero;

            // 1. a column of natural dirt in the air (gy-10..gy-5)
            for (int y = _gy - 10; y <= _gy - 5; y++)
                Place(_x0, y, TileID.Dirt, false);
            // 2. a tall column for a pile (gy-16..gy-7)
            for (int y = _gy - 16; y <= _gy - 7; y++)
                Place(_x0 + 6, y, TileID.Dirt, false);
            // 3. a player's wooden hut on a pillar: pillar x0+14 gy-1..gy-4, floor gy-5 x0+11..x0+17, walls 3 high
            for (int y = _gy - 1; y >= _gy - 4; y--)
                Place(_x0 + 14, y, TileID.WoodBlock, true);
            Place(_x0 + 14, _gy - 5, TileID.WoodBlock, true);
            for (int d = 1; d <= 3; d++)
            {
                Place(_x0 + 14 - d, _gy - 5, TileID.WoodBlock, true);
                Place(_x0 + 14 + d, _gy - 5, TileID.WoodBlock, true);
            }
            for (int y = _gy - 6; y >= _gy - 8; y--)
            {
                Place(_x0 + 11, y, TileID.WoodBlock, true);
                Place(_x0 + 17, y, TileID.WoodBlock, true);
            }
            // 4. a wooden box 4x4 on the floor with ice and snow beside it
            for (int x = _x0 + 24; x <= _x0 + 27; x++)
                for (int y = _gy - 4; y <= _gy - 1; y++)
                    Place(x, y, TileID.WoodBlock, true);
            // with a wooden wall behind it and beyond it
            for (int x = _x0 + 21; x <= _x0 + 27; x++)
                for (int y = _gy - 7; y <= _gy - 1; y++)
                    Main.tile[x, y].wall = WallID.Wood;
            Place(_x0 + 28, _gy - 1, TileID.IceBlock, false);
            Place(_x0 + 28, _gy - 2, TileID.SnowBlock, false);
            // 5. dirt to blow up
            for (int x = _x0 + 34; x <= _x0 + 42; x++)
                for (int y = _gy - 4; y <= _gy - 1; y++)
                    Place(x, y, TileID.Dirt, false);
            Log("set up at " + _x0 + "," + _gy + "; placed tiles " + Placed.Count);
        }

        static void Kick()
        {
            WorldGen.KillTile(_x0, _gy - 5, false, false, true);        // 1: under the dirt column
            WorldGen.KillTile(_x0 + 6, _gy - 7, false, false, true);    // 2: under the tall column
            WorldGen.KillTile(_x0 + 14, _gy - 1, false, false, true);   // 3: the pillar's foot
            Fire.Ignite(_x0 + 24, _gy - 1);                             // 4: the box's corner
            Blast.Explode(new Vector2((_x0 + 38) * 16 + 8, (_gy - 2) * 16), 40, false);   // 5
            Log("kicked: falling " + Falling.Active + ", burning " + Fire.Count);
        }

        static void Check(string when)
        {
            // of the two loose columns (5 + 9 grains) only the 3 grains next to the broken tile fall (author: a local cave-in)
            int col = Count(_x0 - 5, _x0 + 11, _gy - 16, _gy - 1, t => t.type == TileID.Dirt);
            int colLanded = Count(_x0 - 5, _x0 + 11, _gy - 6, _gy - 1, t => t.type == TileID.Dirt);
            int pile = Count(_x0 + 3, _x0 + 9, _gy - 16, _gy - 1, t => t.type == TileID.Dirt);
            int pileWide = 0;
            for (int x = _x0 + 3; x <= _x0 + 9; x++)
                if (Main.tile[x, _gy - 1].active() && Main.tile[x, _gy - 1].type == TileID.Dirt)
                    pileWide++;
            int hutHigh = Count(_x0 + 10, _x0 + 18, _gy - 8, _gy - 8, t => t.type == TileID.WoodBlock);
            int hut = Count(_x0 + 10, _x0 + 18, _gy - 9, _gy - 1, t => t.type == TileID.WoodBlock);
            int box = Count(_x0 + 24, _x0 + 27, _gy - 4, _gy - 1, t => t.type == TileID.WoodBlock);
            int walls = 0;
            for (int x = _x0 + 21; x <= _x0 + 27; x++)
                for (int y = _gy - 7; y <= _gy - 1; y++)
                    if (Main.tile[x, y].wall == WallID.Wood)
                        walls++;
            int ice = Count(_x0 + 28, _x0 + 28, _gy - 2, _gy - 1, t => t.type == TileID.IceBlock || t.type == TileID.SnowBlock);
            int water = 0;
            for (int x = _x0 + 22; x <= _x0 + 32; x++)
                for (int y = _gy - 4; y <= _gy - 1; y++)
                    water += Main.tile[x, y].liquid;
            int blasted = 36 - Count(_x0 + 34, _x0 + 42, _gy - 4, _gy - 1, t => t.type == TileID.Dirt);
            Log(when + ": loose dirt " + col + "/14, " + colLanded + " low on the floor (want 14 and 6: 3 grains of each column fall, Falling.Reach); pile " + pile + "/9 dirt, " +
                pileWide + " wide on the floor (want > 1); hut " + hut + "/15 wood, " + hutHigh + " still at the old height (want 0); " +
                "box " + box + "/16 wood, wall " + walls + "/49, burning " + Fire.Count + ";ice+snow " + ice + "/2, water " + water + "; blasted " + blasted +
                "/36; falling " + Falling.Active + ", placed " + Placed.Count);
        }
    }
}
