using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terraria;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Noita's toxic rock (noita_solids rows that hurt on touch: rock_static_radioactive, rock_static_poison): a
    /// Terraria block marked as that material. It glows green along its open sides and poisons and hurts whoever
    /// touches it (author 2026-10-07, screenshot of a sludge pool's banks in Noita). Kept in the liquids file.
    /// </summary>
    public static class ToxicGround
    {
        // tile key -> index + 1 into NoitaSolids.All
        static readonly Dictionary<int, byte> Cells = new Dictionary<int, byte>();
        static NoitaSolidDef[] _defs;
        static Texture2D _pixel;
        static readonly bool[] Seen = new bool[256];

        public static int Count => Cells.Count;
        public static void Clear() => Cells.Clear();

        static int Key(int x, int y) => x + y * Main.maxTilesX;

        static int Kind(string solid)
        {
            if (_defs == null)
                _defs = NoitaSolids.All;
            for (int i = 0; i < _defs.Length; i++)
                if (_defs[i].Id == solid)
                    return _defs[i].TouchDamage != 0 || (_defs[i].TouchEffects?.Length ?? 0) > 0 ? i + 1 : 0;
            return 0;
        }

        /// <summary>The block at x,y is now this Noita material (only those that hurt on touch are kept).</summary>
        public static void Mark(int x, int y, string solid)
        {
            int k = Kind(solid);
            if (k > 0 && Mats.InWorld(x, y) && Main.tile[x, y].active() && Main.tileSolid[Main.tile[x, y].type])
                Cells[Key(x, y)] = (byte)k;
        }

        public static void Remove(int x, int y)
        {
            if (Cells.Count > 0)
                Cells.Remove(Key(x, y));
        }

        public static void Write(System.IO.BinaryWriter w)
        {
            w.Write(Cells.Count);
            foreach (var kv in Cells)
            {
                w.Write(kv.Key);
                w.Write(_defs[kv.Value - 1].Id);
            }
        }

        public static void Read(System.IO.BinaryReader r, int savedWidth)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int k = r.ReadInt32();
                string id = r.ReadString();
                Mark(k % savedWidth, k / savedWidth, id);
            }
        }

        /// <summary>The player touching toxic rock: its effects and Noita's touch damage, once per material.</summary>
        public static void Touch(Player me)
        {
            if (Cells.Count == 0 || !me.active || me.dead)
                return;
            Array.Clear(Seen, 0, Seen.Length);
            var box = me.Hitbox;
            box.Inflate(2, 2);
            for (int x = box.Left / 16; x <= (box.Right - 1) / 16; x++)
                for (int y = box.Top / 16; y <= (box.Bottom - 1) / 16; y++)
                {
                    if (!Cells.TryGetValue(Key(x, y), out byte k) || Seen[k])
                        continue;
                    if (!Main.tile[x, y].active())
                    {
                        Cells.Remove(Key(x, y));
                        continue;
                    }
                    Seen[k] = true;
                    var d = _defs[k - 1];
                    if (d.TouchEffects != null && d.TouchEffects.Length > 0)
                        Status.Stain(d.TouchEffects);
                    if (d.TouchDamage != 0)
                        Status.TouchHurt(d.TouchDamage * 25f * 60f);   // as Fluids: 1 Noita hp unit = 25 hp, per frame
                }
        }

        /// <summary>A green glow on the sides of toxic blocks that face open space.</summary>
        public static void Draw(SpriteBatch sb)
        {
            if (Cells.Count == 0)
                return;
            if (_pixel == null)
            {
                _pixel = new Texture2D(Main.instance.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            int x0 = Math.Max(1, (int)(Main.screenPosition.X / 16) - 1), y0 = Math.Max(1, (int)(Main.screenPosition.Y / 16) - 1);
            int x1 = Math.Min(Main.maxTilesX - 2, x0 + Main.screenWidth / 16 + 3), y1 = Math.Min(Main.maxTilesY - 2, y0 + Main.screenHeight / 16 + 3);
            var glow = new Color(140, 255, 60) * 0.75f;
            const int T = 3;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!Cells.ContainsKey(Key(x, y)))
                        continue;
                    int px = x * 16 - (int)Main.screenPosition.X, py = y * 16 - (int)Main.screenPosition.Y;
                    bool lit = false;
                    if (Open(x, y - 1)) { sb.Draw(_pixel, new Rectangle(px, py, 16, T), glow); lit = true; }
                    if (Open(x, y + 1)) { sb.Draw(_pixel, new Rectangle(px, py + 16 - T, 16, T), glow); lit = true; }
                    if (Open(x - 1, y)) { sb.Draw(_pixel, new Rectangle(px, py, T, 16), glow); lit = true; }
                    if (Open(x + 1, y)) { sb.Draw(_pixel, new Rectangle(px + 16 - T, py, T, 16), glow); lit = true; }
                    if (lit)
                        Lighting.AddLight(x, y, 0.12f, 0.35f, 0.05f);
                }
        }

        static bool Open(int x, int y)
        {
            var t = Main.tile[x, y];
            return !(t.active() && Main.tileSolid[t.type] && !Main.tileSolidTop[t.type]);
        }
    }
}
