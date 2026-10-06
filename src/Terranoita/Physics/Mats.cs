using System;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>Terraria tile type -> its Noita material (design/sheets/materials.json).</summary>
    public static class Mats
    {
        static MaterialDef[] _byTile;
        static ushort[] _fallsAs, _burnsTo;

        static void Build()
        {
            _byTile = new MaterialDef[TileID.Count];
            _fallsAs = new ushort[TileID.Count];
            _burnsTo = new ushort[TileID.Count];
            foreach (var m in Materials.All)
                foreach (var name in m.TerrariaTiles ?? new string[0])
                {
                    int t = TileType(name);
                    if (t < 0)
                        continue;
                    _byTile[t] = m;
                    _fallsAs[t] = (ushort)(m.FallsAs == "same" ? t : Math.Max(0, TileType(m.FallsAs)));
                    _burnsTo[t] = (ushort)(m.BurnsTo == "none" ? 0 : Math.Max(0, TileType(m.BurnsTo)));
                }
        }

        static int TileType(string name)
        {
            var f = typeof(TileID).GetField(name);
            if (f == null)
            {
                Entry.Warn("materials: no TileID." + name);
                return -1;
            }
            return Convert.ToInt32(f.GetValue(null));
        }

        public static MaterialDef Of(int type)
        {
            if (_byTile == null)
                Build();
            return type >= 0 && type < _byTile.Length ? _byTile[type] : null;
        }

        public static MaterialDef Of(Tile t) => t != null && t.active() ? Of(t.type) : null;

        /// <summary>Loose and not already a Terraria falling tile (sand, silt and slush fall by themselves).</summary>
        public static bool Powder(Tile t)
        {
            var m = Of(t);
            return m != null && m.Falls == "powder" && !TileID.Sets.Falling[t.type];
        }

        public static bool Weightless(Tile t) => Of(t)?.Falls == "weightless";
        public static bool Burns(Tile t) => Of(t)?.Burns == true;
        public static bool Melts(Tile t) => Of(t) != null && Of(t).MeltsTo != "none";
        public static ushort FallsAs(int type) { Of(type); return _fallsAs[type]; }
        /// <summary>0 = nothing is left.</summary>
        public static ushort BurnsTo(int type) { Of(type); return _burnsTo[type]; }

        /// <summary>Blocks things stand on: solid tiles and platforms (not actuated ones).</summary>
        public static bool Solid(int x, int y)
        {
            if (x < 1 || y < 1 || x >= Main.maxTilesX - 1 || y >= Main.maxTilesY - 1)
                return true;
            var t = Main.tile[x, y];
            return t != null && t.active() && !t.inActive() && (Main.tileSolid[t.type] || Main.tileSolidTop[t.type]);
        }

        public static bool InWorld(int x, int y) => x >= 5 && y >= 5 && x < Main.maxTilesX - 5 && y < Main.maxTilesY - 5;
    }
}
