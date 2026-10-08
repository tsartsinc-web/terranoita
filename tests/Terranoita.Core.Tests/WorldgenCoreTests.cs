using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>Worldgen groundwork (design/worldgen_plan.md section 2): PNG reading, pixel scenes, Noita's biome scripts recorded.</summary>
    public class WorldgenCoreTests
    {
        // ---- a tiny PNG writer (RGBA or RGB, filter 0 or 1) so the reader is tested on real PNG bytes ----

        static byte[] Png(int w, int h, uint[] argb, bool alpha = true, byte filter = 0)
        {
            int bpp = alpha ? 4 : 3;
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(filter);
                var row = new byte[w * bpp];
                for (int x = 0; x < w; x++)
                {
                    uint c = argb[y * w + x];
                    row[x * bpp] = (byte)(c >> 16); row[x * bpp + 1] = (byte)(c >> 8); row[x * bpp + 2] = (byte)c;
                    if (alpha) row[x * bpp + 3] = (byte)(c >> 24);
                }
                if (filter == 1)   // Sub: each byte minus the one bpp to its left
                    for (int i = row.Length - 1; i >= bpp; i--)
                        row[i] = (byte)(row[i] - row[i - bpp]);
                raw.Write(row, 0, row.Length);
            }
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x01);
            using (var d = new DeflateStream(z, CompressionMode.Compress, true))
                raw.WriteTo(d);
            z.Write(new byte[4], 0, 4);   // Adler-32, not checked by the reader
            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2);
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        static void Be(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        static void Chunk(Stream s, string kind, byte[] data)
        {
            var len = new byte[4];
            Be(len, 0, data.Length);
            s.Write(len, 0, 4);
            s.Write(System.Text.Encoding.ASCII.GetBytes(kind), 0, 4);
            s.Write(data, 0, data.Length);
            s.Write(new byte[4], 0, 4);   // CRC, not checked
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(false, 1)]
        public void PngPixelsComeBack(bool alpha, byte filter)
        {
            var px = new uint[] { 0xFF102030, 0xFFFFFFFF, 0xFF000000, alpha ? 0x00ABCDEFu : 0xFFABCDEF, 0xFF786C42, 0xFF0000FF };
            var png = NoitaPng.Read(Png(3, 2, px, alpha, filter));
            Assert.Equal(3, png.Width);
            Assert.Equal(2, png.Height);
            Assert.Equal(px, png.Pixels);
        }

        [Fact]
        public void NotAPng() => Assert.Throws<InvalidDataException>(() => NoitaPng.Read(new byte[] { 1, 2, 3 }));

        const string Materials = @"<?xml version=""1.0"" ?>
<Materials>
  <CellData name=""air"" wang_color=""00000000"" />
  <CellData name=""rock_static"" wang_color=""ff786c42"" />
  <CellData name=""water"" wang_color=""ff2f554c"" />
  <CellDataChild name=""water_salt"" _parent=""water"" wang_color=""ff2f554d"" />
</Materials>";

        [Fact]
        public void WangColorsName()
        {
            var c = PixelScene.WangColors(Materials);
            Assert.Equal("rock_static", c[0x786C42]);
            Assert.Equal("water_salt", c[0x2F554D]);
            Assert.Equal("air", c[0]);
        }

        [Fact]
        public void SceneDecodesAndShrinksToTiles()
        {
            // 12 x 6 Noita pixels: left half rock, right half water, bottom row an unknown colour, one transparent pixel
            const uint rock = 0xFF786C42, water = 0xFF2F554C, unknown = 0xFF123456;
            var px = new uint[12 * 6];
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 12; x++)
                    px[y * 12 + x] = y == 5 ? unknown : x < 6 ? rock : water;
            px[0] = 0;
            var grid = PixelScene.Decode(NoitaPng.Read(Png(12, 6, px)), PixelScene.WangColors(Materials));
            Assert.Null(grid[0, 0]);                 // transparent
            Assert.Equal("rock_static", grid[1, 0]);
            Assert.Equal("water", grid[11, 4]);
            Assert.Null(grid[3, 5]);                 // no material has that colour
            // 16/3 px per tile: 12 x 6 -> 3 x 2 tiles
            var tiles = PixelScene.Downscale(grid);
            Assert.Equal(3, tiles.GetLength(0));
            Assert.Equal(2, tiles.GetLength(1));
            Assert.Equal("rock_static", tiles[0, 0]);
            Assert.Equal("water", tiles[1, 0]);      // pixels 5..9: water 5 of them, rock 1
            Assert.Null(tiles[0, 1]);                // the last row: 1 pixel high, unknown colour = air
        }

        [Fact]
        public void MostlyAirIsAir()
        {
            var grid = new string[6, 6];
            grid[0, 0] = "rock_static";
            Assert.Null(PixelScene.Downscale(grid)[0, 0]);
            for (int x = 0; x < 6; x++) for (int y = 0; y < 4; y++) grid[x, y] = "rock_static";
            Assert.Equal("rock_static", PixelScene.Downscale(grid)[0, 0]);
        }

        // ---- Noita's scripts with the recording host (stubs shaped like the real files) ----

        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["data/scripts/gun/gun_enums.lua"] = "ACTION_TYPE_PROJECTILE = 0",
            ["data/scripts/gun/gun_actions.lua"] = @"actions = {
  { id = ""LIGHT_BULLET"", type = 0, spawn_level = ""1,2"", spawn_probability = ""1,1"" },
  { id = ""BOMB"", type = 0, spawn_level = ""1"", spawn_probability = ""1"" } }",
            ["data/scripts/director_helpers.lua"] = @"
function spawn(what, x, y)
  SetRandomSeed(x, y)
  local pick = what[Random(1, #what)]
  if pick.entity then EntityLoad(pick.entity, x, y) end
end",
            ["data/scripts/biomes/test.lua"] = @"
dofile_once(""data/scripts/director_helpers.lua"")
RegisterSpawnFunction(0xffffeedd, ""init"")
RegisterSpawnFunction(0xff00ac33, ""spawn_altar"")
g_items = { { entity = ""data/entities/items/wand_level_01.xml"" }, { entity = ""data/entities/items/wand_level_01.xml"" } }
function spawn_wands(x, y) spawn(g_items, x, y) end
function spawn_potions(x, y) EntityLoad(""data/entities/items/pickup/potion.xml"", x + 2, y) end
function spawn_altar(x, y)
  LoadPixelScene(""data/biome_impl/altar.png"", ""data/biome_impl/altar_visual.png"", x - 10, y - 20, """", true)
  local e = EntityLoad(""data/entities/items/pickup/chest_random.xml"", x, y)
  local ex, ey = EntityGetTransform(e)
  CreateItemActionEntity(GetRandomAction(ex, ey, 1, 0), ex, ey - 5)
  GameDoSomethingWeLack(1)
end
function broken(x, y) error(""boom"") end",
            ["data/scripts/items/chest_random.lua"] = @"
function drop_random_reward(x, y, entity_id, rand_x, rand_y, set_rnd)
  SetRandomSeed(rand_x, rand_y)
  local count = 1
  if Random(0, 100) >= 0 then count = 3 end
  for i = 1, count do
    CreateItemActionEntity(GetRandomActionWithType(x, y, 2, 0, i), x + i * 8, y)
  end
  EntityLoad(""data/entities/items/pickup/goldnugget_10.xml"", x, y - 4)
end",
        };

        static NoitaBiomeSpawns Biome(int seed = 1)
        {
            var b = new NoitaBiomeSpawns(p => Files.TryGetValue(p, out var s) ? s : null, seed);
            b.Load("data/scripts/biomes/test.lua");
            return b;
        }

        [Fact]
        public void BiomeScriptPlacementsAreRecorded()
        {
            var b = Biome();
            Assert.Empty(b.Errors);
            Assert.Equal("spawn_altar", b.SpawnFunctions[0xff00ac33]);
            Assert.True(b.Has("spawn_wands") && !b.Has("spawn_nothing"));

            var wand = b.Call("spawn_wands", 100, 200).Single();
            Assert.Equal(("entity", "data/entities/items/wand_level_01.xml", 100f, 200f), (wand.Kind, wand.File, wand.X, wand.Y));
            Assert.Equal(102f, b.Call("spawn_potions", 100, 200).Single().X);

            var altar = b.Call("spawn_altar", 50, 60);
            Assert.Equal(new[] { "scene", "entity", "spell" }, altar.Select(p => p.Kind));
            Assert.Equal(("data/biome_impl/altar.png", "data/biome_impl/altar_visual.png", 40f, 40f),
                         (altar[0].File, altar[0].Extra, altar[0].X, altar[0].Y));
            Assert.Contains(altar[2].File, new[] { "LIGHT_BULLET", "BOMB" });
            Assert.Equal((50f, 55f), (altar[2].X, altar[2].Y));   // EntityGetTransform gave the chest's place back
            Assert.Contains("GameDoSomethingWeLack", b.Missing);
        }

        [Fact]
        public void ErrorsAreKeptNotThrown()
        {
            var b = Biome();
            Assert.Empty(b.Call("broken", 0, 0));
            Assert.Empty(b.Call("no_such_function", 0, 0));
            Assert.Equal(2, b.Errors.Count);
        }

        [Fact]
        public void SameSeedSamePlacements()
        {
            string Run(int seed) => string.Join("|", Enumerable.Range(0, 20).Select(i => Biome(seed).Call("spawn_altar", i * 10, 5)[2].File));
            Assert.Equal(Run(3), Run(3));
        }

        [Fact]
        public void ChestRandomRewards()
        {
            var b = new NoitaBiomeSpawns(p => Files.TryGetValue(p, out var s) ? s : null, 7);
            b.Load("data/scripts/items/chest_random.lua");
            int chest = b.NewEntity(300, 400);
            var loot = b.Call("drop_random_reward", 300, 400, chest, 300, 400, 0);
            Assert.Empty(b.Errors);
            Assert.Equal(3, loot.Count(p => p.Kind == "spell"));   // GetRandomActionWithType of level 2, type 0: LIGHT_BULLET
            Assert.All(loot.Where(p => p.Kind == "spell"), p => Assert.Equal("LIGHT_BULLET", p.File));
            Assert.Contains(loot, p => p.Kind == "entity" && p.File.EndsWith("goldnugget_10.xml"));
        }
    }
}
