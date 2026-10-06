using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>Noita explosions break the ground (what Terraria's own bombs could break), fiery ones light it.</summary>
    public static class Blast
    {
        public static void Explode(Vector2 pos, float radius, bool fiery)
        {
            if (!Patches.On || radius < 8)
                return;
            int r = (int)Math.Ceiling(radius / 16f);
            int cx = (int)(pos.X / 16), cy = (int)(pos.Y / 16);
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                {
                    float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    // solid inside 60% of the radius, ragged edge beyond
                    if (d > r || (d > r * 0.6f && Main.rand.Next(2) == 0) || !Mats.InWorld(x, y))
                        continue;
                    if (Breakable(x, y))
                        WorldGen.KillTile(x, y, false, false, Main.rand.Next(3) != 0);   // like Noita, most of it is just gone
                }
            if (fiery)
                Fire.IgniteArea(pos, radius * 1.3f, 0.5f);
        }

        /// <summary>The checks Terraria's explosives make (Projectile.CanExplodeTile), kept simple.</summary>
        public static bool Breakable(int x, int y)
        {
            var t = Main.tile[x, y];
            if (t == null || !t.active() || Main.tileDungeon[t.type] || TileID.Sets.BasicChest[t.type] || Main.tileContainer[t.type])
                return false;
            switch (t.type)
            {
                case TileID.LihzahrdBrick:
                case TileID.LihzahrdAltar:
                case TileID.DemonAltar:
                case TileID.Hellstone:
                case TileID.Obsidian:
                case TileID.Chlorophyte:
                case TileID.Cobalt:
                case TileID.Palladium:
                case TileID.Mythril:
                case TileID.Orichalcum:
                case TileID.Adamantite:
                case TileID.Titanium:
                    return false;
            }
            return WorldGen.CanKillTile(x, y);
        }
    }
}
