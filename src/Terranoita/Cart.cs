using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// The wooden cart of Noita's start (data/entities/props/physics_cart.xml: cart_top.png and its one small wheel
    /// cart_wheel.png, both 27 x 13 and centered on each other, drawn in their own colors),
    /// put next to the player the first time a world is entered (author). It falls, rolls when kicked (F) and slows on
    /// the ground; the player can stand in it and ride. Kept with the world: &lt;world&gt;.wld.cart ("x y", or "none").
    /// </summary>
    public static class Cart
    {
        const float Px = Noita.Units.PixelScale;
        static readonly int W = (int)(27 * Px), H = (int)(13 * Px);   // cart_top.png is 27 x 13 Noita px
        static Vector2 _pos, _vel;     // top-left in world pixels
        static float _rot, _spin;      // turned (radians, 0 = upright) and how fast: a kick tips it over
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
                    if (parts.Length >= 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    {
                        _pos = new Vector2(x, y);
                        _rot = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 0f;
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
                                                     _pos.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " " +
                                                     _rot.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "none");
                if (File.Exists(f))
                    File.Delete(f);
                File.Move(f + ".tmp", f);
            }
            catch (Exception ex) { Entry.Error("cart save", ex); }
            _savedAt = Main.GameUpdateCount;
        }

        public static readonly bool TestOn = Environment.GetEnvironmentVariable("TERRANOITA_CART_TEST") == "1";
        static int _logFrames;

        static NPC[] _kicked;
        static float[] _from;
        static float _cartFrom;

        /// <summary>TERRANOITA_CART_TEST=1 (game_test -Mode cart): the kick (F) on a bunny, a slime and a zombie left of
        /// the player, then on the cart; how far each flew is logged (author: at most ~5 tiles for small creatures).</summary>
        public static void Test(Player p, int frame)
        {
            if (frame == 60)
            {
                p.direction = -1;
                _kicked = new[] { Terraria.ID.NPCID.Bunny, Terraria.ID.NPCID.BlueSlime, Terraria.ID.NPCID.Zombie }
                    .Select(id => Main.npc[NPC.NewNPC(new Terraria.DataStructures.EntitySource_SpawnNPC(), (int)p.Center.X - 30, (int)(p.position.Y + p.height), id)])
                    .ToArray();
                foreach (var n in _kicked)
                    n.velocity = Vector2.Zero;
                _from = _kicked.Select(n => n.Center.X).ToArray();
                // a plant at the foot: the kick cuts it like a sword (log "cut tile")
                int fx = (int)((p.Center.X - p.width / 2f - 8) / 16), fy = (int)((p.position.Y + p.height) / 16) - 1;
                WorldGen.PlaceTile(fx, fy, Terraria.ID.TileID.Pots, true, true);
                Entry.Log("CART pot placed: " + (Main.tile[fx, fy].active() ? "yes, tile " + Main.tile[fx, fy].type : "no"));
            }
            if (frame == 62)
            {
                p.direction = -1;
                NoitaActions.Kick(p);
            }
            if (frame == 180)
            {
                for (int i = 0; i < _kicked.Length; i++)
                {
                    var n = _kicked[i];
                    Entry.Log("CART kick " + n.TypeName + " (" + n.width + "x" + n.height + ", kb " + n.knockBackResist.ToString("0.00") + "): " +
                              ((_from[i] - n.Center.X) / 16).ToString("0.0") + " tiles");
                    n.active = false;
                }
                if (!_has)
                {
                    Entry.Log("CART none in this world");
                    Main.instance.Exit();
                    return;
                }
                p.Teleport(new Vector2(_pos.X - 40, _pos.Y + H - p.height), -1);
                p.velocity = Vector2.Zero;
                p.direction = 1;
                _cartFrom = _pos.X;
                Entry.Log("CART before: box " + Box + " rot " + _rot.ToString("0.00"));
            }
            if (frame == 210)
            {
                p.direction = 1;   // the game turns the player between frames: set it right before the kick
                NoitaActions.Kick(p);
            }
            if (frame == 390)
            {
                Entry.Log("CART kick cart: " + ((_pos.X - _cartFrom) / 16).ToString("0.0") + " tiles, rot " + _rot.ToString("0.00"));
                Main.instance.Exit();
            }
        }

        public static void Kick(Vector2 foot, Vector2 push)
        {
            if (!_has)
                return;
            var box = Box;
            box.Inflate(3 * 16, 16);
            if (box.Contains((int)foot.X, (int)foot.Y))
            {
                // it flies a little and tips over (ours: Noita does this with its rigid body physics)
                _vel += new Vector2(push.X * 1.1f, Math.Min(push.Y, 0) - 3.5f);
                _spin += Math.Sign(push.X) * (0.18f + Main.rand.NextFloat() * 0.1f);
                _logFrames = 40;
            }
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
            // up/down first, then sideways: together, a kick from the ground lost its sideways part on the first
            // frame (the floor or a slope under it stopped X) and the cart only spun in place (author)
            float vy = Collision.TileCollision(_pos, new Vector2(0, _vel.Y), W, H, true, true).Y;
            float vx = Collision.TileCollision(_pos + new Vector2(0, vy), new Vector2(_vel.X, 0), W, H, true, true).X;
            _vel = new Vector2(vx, vy);
            if (_logFrames > 0 && (_logFrames-- % 5 == 0))
                Entry.Log("cart path: pos " + (int)_pos.X + "," + (int)_pos.Y + " vel " + before.X.ToString("0.0") + "," + before.Y.ToString("0.0") +
                          " -> " + _vel.X.ToString("0.0") + "," + _vel.Y.ToString("0.0") + " rot " + _rot.ToString("0.00"));
            bool onGround = before.Y > 0 && _vel.Y == 0;
            if (onGround)
                _vel.X *= Math.Abs(_rot) > 0.4f && Math.Abs(_rot) < 2.7f ? 0.85f : 0.97f;   // on its side or back it drags
            // turning: in the air it keeps spinning; on the ground it falls onto its wheels or onto its back
            // (rest at 0 and pi, tips away from standing on end at +-pi/2)
            _rot += _spin;
            _rot = MathHelper.WrapAngle(_rot);
            if (onGround)
            {
                _spin = _spin * 0.75f - 0.05f * (float)Math.Sin(2 * _rot);
                if (Math.Abs(_spin) < 0.002f && Math.Abs(Math.Sin(2 * _rot)) < 0.02f)
                {
                    _spin = 0;
                    _rot = Math.Abs(_rot) > MathHelper.PiOver2 ? MathHelper.Pi : 0f;
                }
            }
            else
                _spin *= 0.995f;
            if (Math.Abs(_vel.X) < 0.05f)
                _vel.X = 0;
            if (before.X != 0 && _vel.X == 0 && Math.Abs(before.X) > 1)
                _vel.Y = -2;   // bumps over a ledge it hits fast
            _pos += _vel;
            // the player stands in it: carried along, as in Noita's physics
            var box = Box;
            bool feetOnTop = Math.Abs(_rot) < 0.3f && p.velocity.Y >= 0 && p.position.Y + p.height >= box.Top - 2 && p.position.Y + p.height <= box.Top + 10 &&
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
            var center = _pos + new Vector2(W, H) / 2f - Main.screenPosition;
            // the wheel's picture is the same size as the top, its wheel already in place: both turn about the middle
            foreach (var file in new[] { "data/props_gfx/cart_wheel.png", "data/props_gfx/cart_top.png" })
            {
                var tex = Art(file);
                if (tex != null)
                    sb.Draw(tex, center, null, color, _rot, new Vector2(tex.Width / 2f, tex.Height / 2f), Px, SpriteEffects.None, 0f);
            }
        }

        static bool _artLogged;

        /// <summary>The part filled with its material (Noita's look), else Noita's own picture of it.</summary>
        static Texture2D Art(string file)
        {
            var tex = NoitaArt.Get(file)?.Texture;
            string how = "own colors";
            if (!_artLogged && file.EndsWith("cart_top.png"))
            {
                _artLogged = true;
                Entry.Log("cart art: " + (tex == null ? "NONE" : how + " " + tex.Width + "x" + tex.Height) + ", cart at " + (int)(_pos.X / 16) + "," + (int)(_pos.Y / 16));
            }
            return tex;
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
