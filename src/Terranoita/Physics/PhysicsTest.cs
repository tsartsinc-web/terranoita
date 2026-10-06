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
            for (int x = _x0 - 2; x <= _x0 + 44; x++)
            {
                for (int y = gy - 25; y < gy; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearEverything();
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
                    WorldGen.PlaceWall(x, y, WallID.Wood, true);
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
            // the two loose columns (5 + 9 grains) end up as piles somewhere around x0-4..x0+10
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
            Log(when + ": loose dirt " + col + "/14, " + colLanded + " low on the floor (want 14 and 14); pile " + pile + "/9 dirt, " +
                pileWide + " wide on the floor (want > 1); hut " + hut + "/15 wood, " + hutHigh + " still at the old height (want 0); " +
                "box " + box + "/16 wood, wall " + walls + "/49, burning " + Fire.Count + ";ice+snow " + ice + "/2, water " + water + "; blasted " + blasted +
                "/36; falling " + Falling.Active + ", placed " + Placed.Count);
        }
    }
}
