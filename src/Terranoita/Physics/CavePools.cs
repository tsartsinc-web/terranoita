using System;
using System.Linq;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Pools of Noita liquids in the caves (design/sheets/cave_pools.json), put in once per world: the first time a
    /// world is loaded without our liquids file. A pool fills a hollow's floor from wall to wall, up to 4 rows deep.
    /// </summary>
    public static class CavePools
    {
        const int MaxWidth = 30, MaxDepth = 4;

        /// <summary>2: three times as many pools as 0.3.0 (author: no pools seen in the caves).</summary>
        public const int Version = 2;

        public static void Generate()
        {
            int made = 0, cells = 0;
            int deepFrom = (int)((Main.rockLayer + Main.UnderworldLayer) / 2);
            foreach (var row in CavePools_All())
            {
                int tries = (int)(row.Per1000Tiles * Main.maxTilesX / 1000f) * 200;   // most random spots are no hollow
                int want = (int)Math.Ceiling(row.Per1000Tiles * Main.maxTilesX / 1000f);
                int got = 0;
                for (int t = 0; t < tries && got < want; t++)
                {
                    int x = WorldGen.genRand.Next(100, Main.maxTilesX - 100);
                    int y = WorldGen.genRand.Next((int)Main.worldSurface + 10, Main.UnderworldLayer - 20);
                    string depth = y < Main.rockLayer ? "dirt" : y < deepFrom ? "cavern" : "deep";
                    if (row.Depth != "any" && row.Depth != depth)
                        continue;
                    int n = TryPool(x, y, row);
                    if (n > 0)
                    {
                        got++;
                        made++;
                        cells += n;
                    }
                }
            }
            Entry.Log("cave pools: " + made + " pools, " + cells + " cells of Noita liquids");
        }

        // ground rows first, so a snow hollow gets the snow row and not the depth one
        static CavePoolDef[] CavePools_All() =>
            Generated.CavePools.All.OrderBy(r => r.Ground == "any" ? 1 : 0).ToArray();

        static bool Air(int x, int y)
        {
            var t = Main.tile[x, y];
            return !(t.active() && Main.tileSolid[t.type]) && t.liquid == 0 && !Fluids.Has(x, y);
        }

        /// <summary>Something to rest on: a block, a liquid of ours or Terraria's.</summary>
        static bool Held(int x, int y) => !Air(x, y);

        static bool Solid(int x, int y)
        {
            var t = Main.tile[x, y];
            return t.active() && Main.tileSolid[t.type] && !Main.tileSolidTop[t.type];
        }

        static string Ground(int x, int y)
        {
            int type = Main.tile[x, y].type;
            if (type == TileID.SnowBlock || type == TileID.IceBlock || type == TileID.Slush) return "snow";
            if (type == TileID.Mud || type == TileID.JungleGrass) return "jungle";
            if (type == TileID.Sand || type == TileID.Sandstone || type == TileID.HardenedSand) return "desert";
            return "any";
        }

        /// <summary>Drop to the floor of the hollow at x,y and fill it; the number of cells, 0 if no pool fits.</summary>
        static int TryPool(int x, int y, CavePoolDef row)
        {
            if (!Air(x, y))
                return 0;
            int floor = y;
            while (floor < Main.UnderworldLayer - 10 && Air(x, floor + 1))
                floor++;
            if (!Solid(x, floor + 1) || Main.tile[x, floor].wall == 0 && floor < Main.worldSurface)
                return 0;
            string ground = Ground(x, floor + 1);
            if (row.Ground != "any" && row.Ground != ground)
                return 0;
            if (row.Ground == "any" && ground != "any" && Generated.CavePools.All.Any(r => r.Ground == ground))
                return 0;   // that biome has its own row
            string liquid = row.Liquids[WorldGen.genRand.Next(row.Liquids.Length)];
            int cells = 0;
            for (int d = 0, yy = floor; d < MaxDepth; d++, yy--)
            {
                // the row must be closed by walls on both sides and lie on something (the floor or the row below)
                int l = x, r = x;
                while (x - l < MaxWidth && Air(l - 1, yy) && Held(l - 1, yy + 1)) l--;
                while (r - x < MaxWidth && Air(r + 1, yy) && Held(r + 1, yy + 1)) r++;
                if (!Solid(l - 1, yy) || !Solid(r + 1, yy) || r - l + 1 < 3)
                    break;
                for (int xx = l; xx <= r; xx++)
                    if (Air(xx, yy) && Held(xx, yy + 1))
                    {
                        Fluids.Add(xx, yy, liquid, 255);
                        cells++;
                    }
            }
            return cells;
        }
    }
}
