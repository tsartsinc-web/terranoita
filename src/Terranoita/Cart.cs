using System;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// The minecart of Noita's start (data/entities/props/physics_minecart.xml, its pictures data/props_gfx/minecart*.png),
    /// put next to the player the first time a world is entered (author). It falls, rolls when kicked (F) and slows on
    /// the ground; the player can stand in it and ride. Kept with the world: &lt;world&gt;.wld.cart ("x y", or "none").
    /// </summary>
    public static class Cart
    {
        const float Px = Noita.Units.PixelScale;
        static readonly int W = (int)(18 * Px), H = (int)(15 * Px);   // minecart.png is 18 x 15 Noita px
        static Vector2 _pos, _vel;     // top-left in world pixels
        static bool _has;
        static string _world;
        static uint _savedAt;

        static string FileOf => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".cart";
        static Rectangle Box => new Rectangle((int)_pos.X, (int)_pos.Y, W, H);

        static void Load(Player p)
        {
            _world = Main.worldPathName;
            _has = false;
            _vel = Vector2.Zero;
            try
            {
                var f = FileOf;
                if (f != null && File.Exists(f))
                {
                    var parts = File.ReadAllText(f).Trim().Split(' ');
                    if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    {
                        _pos = new Vector2(x, y);
                        _has = true;
                    }
                    return;   // "none": it is gone
                }
                // the first visit: next to the player, on the ground
                _pos = new Vector2(p.Center.X + 4 * 16, p.position.Y + p.height - H - 4);
                _has = true;
                Save();
                Entry.Log("cart: put next to the player at " + (int)(_pos.X / 16) + "," + (int)(_pos.Y / 16));
            }
            catch (Exception ex) { Entry.Error("cart load", ex); }
        }

        static void Save()
        {
            var f = FileOf;
            if (f == null)
                return;
            try
            {
                File.WriteAllText(f + ".tmp", _has ? _pos.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " " +
                                                     _pos.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "none");
                if (File.Exists(f))
                    File.Delete(f);
                File.Move(f + ".tmp", f);
            }
            catch (Exception ex) { Entry.Error("cart save", ex); }
            _savedAt = Main.GameUpdateCount;
        }

        public static void Kick(Vector2 foot, Vector2 push)
        {
            if (!_has)
                return;
            var box = Box;
            box.Inflate(3 * 16, 16);
            if (box.Contains((int)foot.X, (int)foot.Y))
                _vel += push * 0.8f;
        }

        public static void Update(Player p)
        {
            if (_world != Main.worldPathName)
                Load(p);
            if (!_has)
                return;
            // falls and rolls on Terraria's tiles, slows down on the ground
            _vel.Y = Math.Min(_vel.Y + 0.3f, 10f);
            var before = _vel;
            _vel = Collision.TileCollision(_pos, _vel, W, H, true, true);
            bool onGround = before.Y > 0 && _vel.Y == 0;
            if (onGround)
                _vel.X *= 0.97f;
            if (Math.Abs(_vel.X) < 0.05f)
                _vel.X = 0;
            if (before.X != 0 && _vel.X == 0 && Math.Abs(before.X) > 1)
                _vel.Y = -2;   // bumps over a ledge it hits fast
            _pos += _vel;
            // the player stands in it: carried along, as in Noita's physics
            var box = Box;
            bool feetOnTop = p.velocity.Y >= 0 && p.position.Y + p.height >= box.Top - 2 && p.position.Y + p.height <= box.Top + 10 &&
                             p.position.X + p.width > box.Left + 6 && p.position.X < box.Right - 6;
            if (feetOnTop && !p.controlDown)
            {
                p.position.Y = box.Top + 6 - p.height;   // sits a little inside the cart
                p.velocity.Y = 0;
                p.position.X += _vel.X;
                if (p.controlJump && p.releaseJump)
                    p.velocity.Y = -Player.jumpSpeed * p.gravDir;
            }
            if (_pos.Y > Main.maxTilesY * 16)
            {
                _has = false;
                Save();
            }
            if (Main.GameUpdateCount - _savedAt > 600)
                Save();
        }

        static void Draw()
        {
            if (!_has || Main.gameMenu)
                return;
            var sb = Main.spriteBatch;
            var color = Lighting.GetColor((int)((_pos.X + W / 2f) / 16), (int)((_pos.Y + H / 2f) / 16));
            // Noita draws physics props as their material inside the shape: metal_rust = data/materials_gfx/steel.png
            foreach (var file in new[] { "data/props_gfx/minecart_wheel_left.png", "data/props_gfx/minecart_wheel_right.png", "data/props_gfx/minecart.png" })
            {
                var tex = NoitaArt.Masked(file, "data/materials_gfx/steel.png");
                if (tex != null)
                    sb.Draw(tex, new Rectangle((int)(_pos.X - Main.screenPosition.X), (int)(_pos.Y - Main.screenPosition.Y), W, H), color);
            }
        }

        [Hook("cart_draw")]
        [HarmonyPatch(typeof(Main), "DrawProjectiles")]
        static class DrawPatch
        {
            static void Postfix()
            {
                if (Main.gameMenu || !_has && Magic.Flasks.FlyingCount == 0)
                    return;
                var sb = Main.spriteBatch;
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                try { Draw(); Magic.Flasks.DrawThrown(); }
                catch (Exception ex) { Entry.Error("cart draw", ex); }
                finally { sb.End(); }
            }
        }
    }
}
