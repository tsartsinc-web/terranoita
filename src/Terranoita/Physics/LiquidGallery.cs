using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_LIQUIDS=1 (with TERRANOITA_AUTOTEST=1): for the author to walk around and touch. In the
    /// caverns, rows of closed obsidian boxes, each full of one Noita liquid or gas (liquids.json, creative ones;
    /// Terraria's water and lava too), a sign with its name above each, torches along the walkways, a stone
    /// background, and a pickaxe that breaks obsidian. Nothing is applied to the player; the game stays open.
    /// </summary>
    public static class LiquidGallery
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_LIQUIDS") == "1";
        const int PerRow = 12, Inner = 4, InnerH = 3;   // box inside 4 x 3, walls 1
        const int PitchX = Inner + 3, PitchY = InnerH + 2 + 5;   // a gap between boxes, a walkway above each row

        /// <summary>Where the gallery starts: Ctrl+Shift+H brings the player back (teleportatium).</summary>
        public static Vector2? Home;
        static readonly System.Collections.Generic.List<(int left, int top, string name)> Boxes =
            new System.Collections.Generic.List<(int, int, string)>();

        public static void Frame(Player p, int frame)
        {
            if (frame == 300)
                Build(p);
            // gases and fading liquids go, as in Noita: top the boxes up every 10 seconds so they can be seen
            else if (frame > 300 && frame % 600 == 0)
                foreach (var (left, top, name) in Boxes)
                    if (Fluids.Total(left + 1, left + Inner, top + 1, top + InnerH, name) < Inner * InnerH * 255 / 3)
                        Fill(left, top, name);
        }

        static void Fill(int left, int top, string name)
        {
            for (int x = left + 1; x <= left + Inner; x++)
                for (int y = top + 1; y <= top + InnerH; y++)
                    Fluids.Add(x, y, name, 255);
        }

        static void Build(Player p)
        {
            var names = new[] { "water", "lava" }.Concat(Liquids.All.Where(l => l.Creative).Select(l => l.Id)).Distinct().ToArray();
            int rows = (names.Length + PerRow - 1) / PerRow;
            int w = PerRow * PitchX + 6, h = rows * PitchY + 6;
            int x0 = Math.Max(100, Math.Min(Main.maxTilesX - w - 100, (int)(p.Center.X / 16) - w / 2));
            int y0 = Math.Min(Main.maxTilesY - h - 250, (int)Main.rockLayer + 40);

            // a hollow in the caverns with a background, floored with stone
            for (int x = x0 - 2; x < x0 + w + 2; x++)
                for (int y = y0 - 2; y < y0 + h + 2; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearEverything();
                    t.wall = WallID.Stone;
                    if (x < x0 || x >= x0 + w || y < y0 || y >= y0 + h)
                    {
                        t.active(true);
                        t.type = TileID.Stone;
                    }
                }
            int i = 0;
            for (int r = 0; r < rows; r++)
            {
                int top = y0 + 5 + r * PitchY;   // box top row; the walkway is the 5 rows above it
                for (int c = 0; c < PerRow && i < names.Length; c++, i++)
                {
                    int left = x0 + 3 + c * PitchX;
                    Box(left, top, names[i]);
                }
                // floor under the row, so each walkway is the box tops plus the gaps between them
                for (int x = x0; x < x0 + w; x++)
                {
                    var t = Main.tile[x, top + InnerH + 2];
                    t.active(true);
                    t.type = TileID.Stone;
                }
            }
            for (int x = x0 - 2; x < x0 + w + 2; x++)
                for (int y = y0 - 2; y < y0 + h + 2; y++)
                    WorldGen.SquareTileFrame(x, y, true);
            // signs and torches after framing, on the box tops
            i = 0;
            for (int r = 0; r < rows; r++)
            {
                int top = y0 + 5 + r * PitchY;
                for (int c = 0; c < PerRow && i < names.Length; c++, i++)
                {
                    int left = x0 + 3 + c * PitchX;
                    Sign(left + 1, top - 1, names[i]);
                    WorldGen.PlaceTile(left + 4, top - 1, TileID.Torches, true, true);
                }
            }
            // fill last: the boxes are closed now
            i = 0;
            for (int r = 0; r < rows; r++)
            {
                int top = y0 + 5 + r * PitchY;
                for (int c = 0; c < PerRow && i < names.Length; c++, i++)
                {
                    int left = x0 + 3 + c * PitchX;
                    Boxes.Add((left, top, names[i]));
                    Fill(left, top, names[i]);
                }
            }
            // a pickaxe that breaks obsidian, torches
            p.inventory[1].SetDefaults(ItemID.NightmarePickaxe);
            p.inventory[3].SetDefaults(ItemID.Torch);
            p.inventory[3].stack = 99;
            Home = new Vector2((x0 + 3) * 16, (y0 + 4) * 16 - p.height);
            p.Teleport(Home.Value, -1);
            p.velocity = Vector2.Zero;
            Entry.Log("LIQUID GALLERY: " + names.Length + " boxes in " + rows + " rows at " + x0 + "," + y0 + "; cells " + Fluids.Count);
            Main.NewText("Terranoita: " + names.Length + " Noita liquids and gases in obsidian boxes. The pickaxe breaks obsidian. Ctrl+Shift+H: back here.", new Color(120, 200, 255));
        }

        static void Box(int left, int top, string name)
        {
            for (int x = left; x <= left + Inner + 1; x++)
                for (int y = top; y <= top + InnerH + 1; y++)
                {
                    bool edge = x == left || x == left + Inner + 1 || y == top || y == top + InnerH + 1;
                    var t = Main.tile[x, y];
                    t.ClearTile();
                    t.liquid = 0;
                    if (edge)
                    {
                        t.active(true);
                        t.type = TileID.Obsidian;
                    }
                }
        }

        static void Sign(int x, int y, string material)
        {
            if (!WorldGen.PlaceTile(x, y, TileID.Signs, true, true))
                return;
            int id = Terraria.Sign.ReadSign(x, y, true);
            var def = Liquids.All.FirstOrDefault(l => l.Id == material);
            string text = def != null ? NoitaArt.Text(def.NameKey, material) + "\n(" + material + ", " + def.Kind + ")" : material;
            if (id >= 0)
                Terraria.Sign.TextSign(id, text);
        }
    }
}
