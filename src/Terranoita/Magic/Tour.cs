using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_TOUR=1 (game_test -Mode tour, the author watches; the game stays open): in a freshly made world the
    /// test character gets Shine, is taken to 10 underground chests (10 s each) to see what world loot put in them,
    /// then dies, and after the respawn a chest with a flask of every potion material is put next to them.
    /// </summary>
    public static class Tour
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_TOUR") == "1";
        const int Start = 120, Every = 600, Stops = 10;
        static int[] _chests;
        static bool _killed, _done;

        static void Log(string s) => Entry.Log("TOUR " + s);

        public static void Frame(Player p, int frame)
        {
            if (_done)
                return;
            if (frame == 60)
            {
                p.AddBuff(BuffID.Shine, 60 * 60 * 30);
                var under = Enumerable.Range(0, Main.maxChests)
                    .Where(i => Main.chest[i] != null && Main.chest[i].y > Main.worldSurface + 5 && Main.chest[i].y < Main.UnderworldLayer &&
                                Main.tile[Main.chest[i].x, Main.chest[i].y].active())
                    .OrderBy(_ => Main.rand.Next()).Take(Stops).ToArray();
                _chests = under;
                Log("shine on; " + under.Length + " underground chests picked of " + Main.chest.Count(c => c != null));
            }
            if (_chests == null)
                return;
            int k = (frame - Start) / Every;
            if (frame >= Start && (frame - Start) % Every == 0 && k < _chests.Length)
            {
                var c = Main.chest[_chests[k]];
                // stand on the floor right of the chest (a chest is 2 x 2 tiles from its top-left)
                var at = new Vector2((c.x + 2) * 16, (c.y + 2) * 16 - p.height);
                p.Teleport(at, -1);
                p.velocity = Vector2.Zero;
                var items = c.item.Where(it => it != null && !it.IsAir).Select(it => it.Name).ToList();
                Log("stop " + (k + 1) + " at chest " + c.x + "," + c.y + ": " + string.Join(", ", items));
                Main.NewText("Tour " + (k + 1) + "/" + _chests.Length + ": " + string.Join(", ", items.Take(6)), new Color(200, 160, 255));
            }
            if (!_killed && frame >= Start + _chests.Length * Every)
            {
                _killed = true;
                Log("killing the character");
                p.KillMe(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(p.name + " ended the tour."), 99999, 0);
                return;
            }
            if (_killed && !p.dead && p.active)
            {
                PlaceFlasks(p);
                _done = true;
            }
        }

        /// <summary>A chest of flasks (every material of Noita's potion.lua) on the ground next to the player.</summary>
        static void PlaceFlasks(Player p)
        {
            int px = (int)(p.Center.X / 16), feet = (int)((p.position.Y + p.height) / 16);
            for (int dx = 3; dx < 30; dx++)
                foreach (int sx in new[] { px + dx, px - dx })
                {
                    // ground under two free tiles, two tiles of air above them
                    int floor = feet - 4;
                    while (floor < feet + 8 && !(Physics.Mats.Solid(sx, floor) && Physics.Mats.Solid(sx + 1, floor)))
                        floor++;
                    if (!Physics.Mats.Solid(sx, floor))
                        continue;
                    bool room = true;
                    for (int y = floor - 2; y < floor && room; y++)
                        for (int x = sx; x <= sx + 1 && room; x++)
                            room = !Main.tile[x, y].active() && Main.tile[x, y].liquid == 0;
                    if (!room)
                        continue;
                    int at = WorldGen.PlaceChest(sx, floor - 1, TileID.Containers, false, 0);
                    if (at < 0)
                        continue;
                    var materials = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next()).PotionMaterials();
                    var c = Main.chest[at];
                    for (int i = 0; i < materials.Count && i < c.item.Length; i++)
                        c.item[i] = MagicItems.MakeFlask(materials[i], Flasks.Capacity);
                    Log("respawned; chest of " + Math.Min(materials.Count, c.item.Length) + " flasks at " + sx + "," + (floor - 2));
                    Main.NewText("A chest of flasks is next to you.", new Color(120, 200, 255));
                    return;
                }
            Log("respawned; no room for the flask chest");
        }
    }
}
