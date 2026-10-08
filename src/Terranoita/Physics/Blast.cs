using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>Noita explosions break the ground (what Terraria's own bombs could break), fiery ones light it.</summary>
    public static class Blast
    {
        /// <param name="pickPower">-1: Terraria's explosives rule (creatures); else a player's spell breaks only what
        /// that player's best pickaxe could (author)</param>
        public static void Explode(Vector2 pos, float radius, bool fiery, int pickPower = -1)
        {
            if (!Patches.On || radius < 8)
                return;
            int r = (int)Math.Ceiling(radius / 16f);
            int cx = (int)(pos.X / 16), cy = (int)(pos.Y / 16);
            int drops = 0;   // a few blocks drop, the rest is gone: hundreds of items on the ground slowed the game (author)
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                {
                    float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    // solid inside 60% of the radius, ragged edge beyond
                    if (d > r || (d > r * 0.6f && Main.rand.Next(2) == 0) || !Mats.InWorld(x, y))
                        continue;
                    if (Breakable(x, y, pickPower))
                    {
                        bool drop = drops < 4 && Main.rand.Next(3) == 0;
                        if (drop)
                            drops++;
                        WorldGen.KillTile(x, y, false, false, !drop);   // like Noita, most of it is just gone
                    }
                }
            if (fiery)
                Fire.IgniteArea(pos, radius * 1.3f, 0.5f);
        }

        /// <summary>The best pickaxe power in the player's inventory (0 without one).</summary>
        public static int PickPower(Player p)
        {
            var best = p?.GetBestPickaxe();
            return best == null || best.IsAir ? 0 : best.pick;
        }

        /// <summary>
        /// Pickaxe power a tile needs, as Terraria's Player.GetPickaxeDamage gives it (wiki numbers; PC: compare with
        /// `TN_IL=1 tncli tr-methods Terraria.exe Player GetPickaxeDamage`).
        /// </summary>
        public static int RequiredPick(ushort type)
        {
            switch (type)
            {
                case TileID.Meteorite: return 50;
                case TileID.Demonite: case TileID.Crimtane: case TileID.Obsidian: return 55;
                case TileID.Ebonstone: case TileID.Crimstone: case TileID.Pearlstone: case TileID.Hellstone:
                case TileID.BlueDungeonBrick: case TileID.GreenDungeonBrick: case TileID.PinkDungeonBrick: return 65;
                case TileID.Cobalt: case TileID.Palladium: return 100;
                case TileID.Mythril: case TileID.Orichalcum: return 110;
                case TileID.Adamantite: case TileID.Titanium: return 150;
                case TileID.Chlorophyte: return 200;
                case TileID.LihzahrdBrick: return 210;
                default: return 0;
            }
        }

        /// <summary>The checks Terraria's explosives make (Projectile.CanExplodeTile), kept simple; with a pickaxe
        /// power, what that pickaxe could mine instead of the explosives' list.</summary>
        public static bool Breakable(int x, int y, int pickPower = -1)
        {
            var t = Main.tile[x, y];
            if (t == null || !t.active() || TileID.Sets.BasicChest[t.type] || Main.tileContainer[t.type])
                return false;
            if (pickPower >= 0)
                return t.type != TileID.LihzahrdAltar && t.type != TileID.DemonAltar &&
                       !(t.type == TileID.LihzahrdBrick && !NPC.downedPlantBoss) &&
                       RequiredPick(t.type) <= pickPower && WorldGen.CanKillTile(x, y);
            if (Main.tileDungeon[t.type])
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
