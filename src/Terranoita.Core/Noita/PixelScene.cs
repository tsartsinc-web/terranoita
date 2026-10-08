using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace Terranoita.Noita
{
    /// <summary>
    /// Noita's pixel scenes (LoadPixelScene's materials png): each pixel's colour is a material's wang_color in
    /// materials.xml. Decode gives a grid of material names (null = air or a colour no material has); Downscale turns
    /// it into Terraria tiles: 1 Noita pixel = 3 Terraria pixels, so a tile is 16/3 Noita pixels and takes the most
    /// common material of its block, air when most of the block is air (design/worldgen_plan.md, section 2).
    /// </summary>
    public static class PixelScene
    {
        public const float PixelsPerTile = 16f / 3f;

        /// <summary>wang_color (RGB, alpha ignored) -> material name, from materials.xml text.</summary>
        public static Dictionary<uint, string> WangColors(string materialsXml)
        {
            var map = new Dictionary<uint, string>();
            if (string.IsNullOrEmpty(materialsXml))
                return map;
            var doc = new XmlDocument();
            doc.LoadXml("<Root>" + Regex.Replace(materialsXml, @"<\?xml[^>]*\?>", "") + "</Root>");
            foreach (XmlElement e in doc.GetElementsByTagName("*").OfType<XmlElement>())
            {
                if (e.Name != "CellData" && e.Name != "CellDataChild")
                    continue;
                string name = e.GetAttribute("name"), wang = e.GetAttribute("wang_color");
                if (name.Length > 0 && uint.TryParse(wang, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c) && !map.ContainsKey(c & 0xFFFFFF))
                    map[c & 0xFFFFFF] = name;
            }
            return map;
        }

        public static string[,] Decode(NoitaPng png, Dictionary<uint, string> colors)
        {
            var grid = new string[png.Width, png.Height];
            for (int y = 0; y < png.Height; y++)
                for (int x = 0; x < png.Width; x++)
                {
                    uint c = png.At(x, y);
                    if ((c >> 24) == 0)
                        continue;   // transparent: nothing placed
                    if (colors.TryGetValue(c & 0xFFFFFF, out var m) && m != "air")
                        grid[x, y] = m;
                }
            return grid;
        }

        public static string[,] Downscale(string[,] grid, float pixelsPerTile = PixelsPerTile)
        {
            int w = grid.GetLength(0), h = grid.GetLength(1);
            int tw = Math.Max(1, (int)Math.Ceiling(w / pixelsPerTile)), th = Math.Max(1, (int)Math.Ceiling(h / pixelsPerTile));
            var tiles = new string[tw, th];
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int ty = 0; ty < th; ty++)
                for (int tx = 0; tx < tw; tx++)
                {
                    int x0 = (int)(tx * pixelsPerTile), x1 = Math.Min(w, (int)((tx + 1) * pixelsPerTile));
                    int y0 = (int)(ty * pixelsPerTile), y1 = Math.Min(h, (int)((ty + 1) * pixelsPerTile));
                    count.Clear();
                    int air = 0, all = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            all++;
                            var m = grid[x, y];
                            if (m == null) { air++; continue; }
                            count[m] = count.TryGetValue(m, out var n) ? n + 1 : 1;
                        }
                    if (all == 0 || air * 2 > all || count.Count == 0)
                        continue;
                    // the most common; a tie goes to the name first in order, so the result does not depend on the dictionary
                    tiles[tx, ty] = count.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
                }
            return tiles;
        }
    }
}
