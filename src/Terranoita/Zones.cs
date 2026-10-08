using Terraria;

namespace Terranoita.Game
{
    /// <summary>Helpers used by the generated zone checks (design/sheets/terraria_zones.json, terraria_check).</summary>
    public static class Zones
    {
        /// <summary>No biome that has its own Noita locations (snow, jungle, desert, evil, hallow, mushroom, dungeon...).</summary>
        public static bool NoBiome(Player p) =>
            !p.ZoneSnow && !p.ZoneJungle && !p.ZoneDesert && !p.ZoneUndergroundDesert && !p.ZoneGlowshroom &&
            !p.ZoneDungeon && !p.ZoneCorrupt && !p.ZoneCrimson && !p.ZoneHallow && !p.ZoneMarble && !p.ZoneGranite &&
            !p.ZoneLihzhardTemple && !p.ZoneHive && !p.ZoneMeteor && !p.ZoneShimmer;

        static Tile At(int x, int y) =>
            x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY ? Main.tile[x, y] : null;

        public static bool Water(int x, int y)
        {
            var t = At(x, y);
            return t != null && t.liquid > 0 && t.liquidType() == 0;
        }

        /// <summary>Surface water within r tiles (a lake's walkers stand on its shore; swimmers still need the water).</summary>
        public static bool NearWater(int x, int y, int r)
        {
            for (int dy = -r; dy <= r; dy += 2)
                for (int dx = -r; dx <= r; dx += 2)
                    if (Water(x + dx, y + dy))
                        return true;
            return false;
        }

        public static bool Wall(int x, int y, ushort wall)
        {
            var t = At(x, y);
            return t != null && t.wall == wall;
        }
    }
}
