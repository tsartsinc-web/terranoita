using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Disturbed tiles that fall (systems.json block_physics): loose ones one by one like Noita powders, the player's
    /// unsupported buildings as one body. Untouched tiles are never looked at.
    /// </summary>
    public static class Falling
    {
        const float Gravity = 0.25f, MaxFall = 10f;
        const int MaxGroup = 4000;        // a bigger building is taken as supported (and never searched further)
        const int PerFrame = 400;         // disturbed tiles looked at per frame
        const int Reach = 3;              // author: loose soil caves in only this close to the broken block, not the whole hill

        sealed class Part
        {
            public int Dx, Dy;
            public Tile Tile;
            public bool Placed;
        }

        sealed class Body
        {
            public int X, Y;              // tile of the body's origin when it started
            public float Off, Vy;         // fallen so far (px) and speed
            public List<Part> Parts = new List<Part>();
            public bool Powder;
            public int HurtCooldown;
            public int Origin;            // the break that set it falling
        }

        static readonly List<Body> Bodies = new List<Body>();
        static readonly Queue<int> Queue = new Queue<int>();
        static readonly Dictionary<int, int> Queued = new Dictionary<int, int>();   // tile -> the break it came from
        static bool _busy;                // our own tile changes do not disturb through the KillTile hook
        // placed tiles already found held this frame: a landed building disturbs every one of its tiles, and each would
        // flood-fill the whole building again (up to MaxGroup steps each). Any tile change clears it.
        static readonly HashSet<int> Checked = new HashSet<int>();

        public static int Active => Bodies.Count;

        public static void Clear()
        {
            Bodies.Clear();
            Queue.Clear();
            Queued.Clear();
            Checked.Clear();
        }

        /// <summary>The tile at x,y changed or went away: the tiles around it look again.</summary>
        public static void Disturb(int x, int y) => Disturb(x, y, x + y * Main.maxTilesX);

        static void Disturb(int x, int y, int origin)
        {
            if (_busy)
                return;
            Enqueue(x, y - 1, origin);
            Enqueue(x - 1, y, origin);
            Enqueue(x + 1, y, origin);
            Enqueue(x, y + 1, origin);
            Enqueue(x, y, origin);
        }

        static void Enqueue(int x, int y, int origin)
        {
            if (!Mats.InWorld(x, y))
                return;
            int k = x + y * Main.maxTilesX;
            if (!Queued.ContainsKey(k))
            {
                Queued[k] = origin;
                Queue.Enqueue(k);
            }
        }

        /// <summary>
        /// Inside the cave-in of the break at origin: Reach tiles up over the break, and each column further out a
        /// random 0 or 1 lower, so the hole is a ragged staircase and not a square (author).
        /// </summary>
        static bool Near(int x, int y, int origin)
        {
            int dx = x - origin % Main.maxTilesX, dy = y - origin / Main.maxTilesX;
            if (Math.Abs(dx) > Reach || dy > Reach)
                return false;
            int height = Reach;
            for (int i = Math.Sign(dx); i != 0 && Math.Abs(i) <= Math.Abs(dx); i += Math.Sign(dx))
                height -= Step(origin, i);
            return -dy <= height;
        }

        // the same random step for the same break and column, every time it is asked
        static int Step(int origin, int column) => (int)((uint)(origin * 73856093 ^ column * 19349663) >> 13) & 1;

        public static void Update()
        {
            Checked.Clear();
            for (int n = 0; n < PerFrame && Queue.Count > 0; n++)
            {
                int k = Queue.Dequeue();
                int origin = Queued[k];
                Queued.Remove(k);
                Look(k % Main.maxTilesX, k / Main.maxTilesX, origin);
            }
            for (int i = Bodies.Count - 1; i >= 0; i--)
                if (Step(Bodies[i]))
                    Bodies.RemoveAt(i);
        }

        static void Look(int x, int y, int origin)
        {
            var t = Main.tile[x, y];
            if (t == null || !t.active())
                return;
            if (Mats.Powder(t))
            {
                if (!Mats.Solid(x, y + 1) && Near(x, y, origin))
                    Start(new List<(int, int)> { (x, y) }, true, origin);
                return;
            }
            if (Placed.Has(x, y) && IsBlock(t) && !Mats.Weightless(t) && !Checked.Contains(x + y * Main.maxTilesX))
                CheckSupport(x, y, origin);
        }

        static bool IsBlock(Tile t) => Main.tileSolid[t.type] || TileID.Sets.Platforms[t.type];

        /// <summary>A connected group of placed blocks holds if it touches a natural solid tile or a weightless one, or
        /// has a platform hanging on a background wall (Terraria lets platforms be placed on walls; author: they hold).</summary>
        static void CheckSupport(int x0, int y0, int origin)
        {
            var group = new List<(int, int)>();
            var seen = new HashSet<int> { x0 + y0 * Main.maxTilesX };
            var open = new Stack<(int, int)>();
            open.Push((x0, y0));
            while (open.Count > 0)
            {
                var (x, y) = open.Pop();
                group.Add((x, y));
                var here = Main.tile[x, y];
                if (here.wall > 0 && TileID.Sets.Platforms[here.type])
                {
                    Checked.UnionWith(seen);   // a platform on a wall: held, and all that hangs on it
                    return;
                }
                if (group.Count > MaxGroup)
                {
                    Checked.UnionWith(seen);   // taken as held
                    return;
                }
                foreach (var (nx, ny) in new[] { (x, y + 1), (x - 1, y), (x + 1, y), (x, y - 1) })
                {
                    if (!Mats.InWorld(nx, ny))
                    {
                        Checked.UnionWith(seen);
                        return;
                    }
                    var t = Main.tile[nx, ny];
                    if (t == null || !t.active() || !IsBlock(t) || t.inActive())
                        continue;
                    if (Mats.Weightless(t) || !Placed.Has(nx, ny))
                    {
                        Checked.UnionWith(seen);   // anchored: every tile reached so far hangs on it
                        return;
                    }
                    if (seen.Add(nx + ny * Main.maxTilesX))
                        open.Push((nx, ny));
                }
            }
            Start(group, false, origin);
        }

        static void Start(List<(int x, int y)> tiles, bool powder, int origin)
        {
            Checked.Clear();   // tiles go away: what held a moment ago may not now
            var b = new Body { X = tiles[0].x, Y = tiles[0].y, Powder = powder, Origin = origin };
            _busy = true;
            try
            {
                foreach (var (x, y) in tiles)
                {
                    var t = Main.tile[x, y];
                    var copy = new Tile();
                    copy.CopyFrom(t);
                    b.Parts.Add(new Part { Dx = x - b.X, Dy = y - b.Y, Tile = copy, Placed = Placed.Has(x, y) });
                    Placed.Remove(x, y);
                    t.ClearTile();
                    NetSync.Tile(x, y);
                }
                foreach (var (x, y) in tiles)
                    WorldGen.SquareTileFrame(x, y, true);
            }
            finally { _busy = false; }
            foreach (var (x, y) in tiles)
                Disturb(x, y, origin);
            Bodies.Add(b);
            if (!powder)
                Entry.Log("physics: building of " + tiles.Count + " tiles falls at " + b.X + "," + b.Y);
        }

        /// <summary>One frame of fall; true when it has landed.</summary>
        static bool Step(Body b)
        {
            b.Vy = Math.Min(MaxFall, b.Vy + Gravity);
            float next = b.Off + b.Vy;
            int rowsNow = (int)Math.Ceiling(b.Off / 16f), rowsNext = (int)Math.Ceiling(next / 16f);
            for (int r = rowsNow + 1; r <= rowsNext; r++)
                if (Blocked(b, 0, r))
                {
                    if (b.Powder && Slide(b, r - 1))
                        return false;
                    Land(b, r - 1);
                    return true;
                }
            b.Off = next;
            if (b.Y + b.Off / 16f > Main.maxTilesY - 10)
            {
                // fell out of the world: the player's blocks come back as items where it left
                foreach (var p in b.Parts)
                    if (p.Placed)
                        DropAsItem(p.Tile, b.X + p.Dx, Math.Min(Main.maxTilesY - 12, (int)(b.Y + p.Dy + b.Off / 16f)));
                return true;
            }
            Crush(b);
            return false;
        }

        static bool Blocked(Body b, int dx, int rows)
        {
            foreach (var p in b.Parts)
                if (Mats.Solid(b.X + p.Dx + dx, b.Y + p.Dy + rows))
                    return true;
            return false;
        }

        /// <summary>Powders pile up: a grain that lands next to a drop rolls down the free diagonal.</summary>
        static bool Slide(Body b, int rows)
        {
            int x = b.X, y = b.Y + rows;
            int first = Main.rand.Next(2) == 0 ? -1 : 1;
            for (int s = 0; s < 2; s++)
            {
                int dir = s == 0 ? first : -first;
                if (!Mats.Solid(x + dir, y) && !Mats.Solid(x + dir, y + 1))
                {
                    b.X += dir;
                    b.Y += rows;
                    b.Off = 0;
                    b.Vy = Math.Min(b.Vy, 2f);
                    return true;
                }
            }
            return false;
        }

        static void Land(Body b, int rows)
        {
            Checked.Clear();
            // another grain may have settled where this one lands: it stacks on top instead of vanishing
            for (int up = 0; up < 6 && Blocked(b, 0, rows); up++)
                rows--;
            _busy = true;
            var landed = new List<(int, int)>();
            try
            {
                foreach (var p in b.Parts)
                {
                    int x = b.X + p.Dx, y = b.Y + p.Dy + rows;
                    if (!Mats.InWorld(x, y))
                        continue;
                    var t = Main.tile[x, y];
                    if (t.active())
                    {
                        if (Main.tileSolid[t.type] || Mats.HoldsItems(x, y))
                        {
                            if (p.Placed)
                                DropAsItem(p.Tile, x, y);   // taken meanwhile: the player's block comes back as an item
                            continue;
                        }
                        _busy = false;
                        WorldGen.KillTile(x, y);   // grass, plants, furniture under it break
                        NetSync.Tile(x, y);
                        _busy = true;
                        if (t.active())
                        {
                            if (p.Placed)
                                DropAsItem(p.Tile, x, y);
                            continue;
                        }
                    }
                    ushort wall = t.wall;
                    byte liquid = t.liquid;
                    int liquidType = t.liquidType();
                    t.CopyFrom(p.Tile);
                    t.wall = wall;
                    t.liquid = 0;
                    if (liquid > 0)
                        PushLiquidUp(x, y, liquid, liquidType);   // Terraria's water there is moved up, not deleted
                    if (b.Powder)
                        t.type = Mats.FallsAs(t.type);
                    NetSync.Tile(x, y);
                    if (p.Placed)
                        Placed.Add(x, y);
                    landed.Add((x, y));
                }
                foreach (var (x, y) in landed)
                    WorldGen.SquareTileFrame(x, y, true);
            }
            finally { _busy = false; }
            if (!b.Powder || Main.rand.Next(4) == 0)
                Terraria.Audio.SoundEngine.PlaySound(SoundID.Dig, new Vector2(b.X * 16, (b.Y + rows) * 16));
            foreach (var (x, y) in landed)
                Disturb(x, y, b.Origin);
        }

        /// <summary>The liquid of a tile a block lands in goes to the first free tile above (up to 4 tiles), else it is lost.</summary>
        static void PushLiquidUp(int x, int y, byte amount, int type)
        {
            for (int up = 1; up <= 4; up++)
            {
                int ty = y - up;
                if (!Mats.InWorld(x, ty) || Mats.Solid(x, ty))
                    return;
                var a = Main.tile[x, ty];
                if (a.liquid > 0 && a.liquidType() != type || a.liquid == 255)
                    continue;
                int put = Math.Min(255 - a.liquid, (int)amount);
                a.liquidType(type);
                a.liquid = (byte)(a.liquid + put);
                NetSync.AddWater(x, ty);
                amount = (byte)(amount - put);
                if (amount == 0)
                    return;
            }
        }

        // tile type (and style for platforms) -> the item that places it, from every item's createTile/placeStyle
        static Dictionary<int, int> _itemOfTile;

        static int ItemOfTile(Tile tile)
        {
            if (_itemOfTile == null)
            {
                _itemOfTile = new Dictionary<int, int>();
                var it = new Item();
                for (int i = 1; i < ItemID.Count; i++)
                {
                    it.SetDefaults(i);
                    if (it.createTile < 0)
                        continue;
                    int key = it.createTile * 1000 + it.placeStyle;
                    if (!_itemOfTile.ContainsKey(key))
                        _itemOfTile[key] = i;
                }
            }
            int style = TileID.Sets.Platforms[tile.type] ? tile.frameY / 18 : 0;
            return _itemOfTile.TryGetValue(tile.type * 1000 + style, out int item) ? item :
                   _itemOfTile.TryGetValue(tile.type * 1000, out item) ? item : 0;
        }

        /// <summary>A placed block that cannot land comes back as its item (none known: lost, as before).</summary>
        static void DropAsItem(Tile tile, int x, int y)
        {
            try
            {
                int item = ItemOfTile(tile);
                if (item > 0)
                    Item.NewItem(new EntitySource_WorldEvent(), x * 16, y * 16, 16, 16, item);
            }
            catch (Exception ex) { Entry.Error("falling drop", ex); }
        }

        /// <summary>Falling blocks hurt what they fall on.</summary>
        static void Crush(Body b)
        {
            if (b.HurtCooldown > 0)
            {
                b.HurtCooldown--;
                return;
            }
            if (b.Vy < 3f)
                return;
            var me = Main.LocalPlayer;
            if (!me.active || me.dead)
                return;
            foreach (var p in b.Parts)
            {
                var r = new Rectangle((b.X + p.Dx) * 16, (int)((b.Y + p.Dy) * 16 + b.Off), 16, 16);
                if (!r.Intersects(me.Hitbox))
                    continue;
                int dmg = b.Powder ? 6 : (int)Math.Min(60, 15 + 2 * Math.Sqrt(b.Parts.Count));
                me.Hurt(PlayerDeathReason.ByCustomReason(me.name + " was buried."), dmg, me.Center.X < r.Center.X ? -1 : 1);
                b.HurtCooldown = 30;
                return;
            }
        }

        // TextureAssets.Tile holds ReLogic Asset<Texture2D>s; ReLogic is embedded in Terraria.exe, so through reflection
        static Array _tileAssets;
        static System.Reflection.PropertyInfo _value;

        static Texture2D TileTexture(int type)
        {
            Main.instance.LoadTiles(type);
            if (_tileAssets == null)
                _tileAssets = (Array)typeof(TextureAssets).GetField("Tile").GetValue(null);
            object asset = _tileAssets.GetValue(type);
            if (asset == null)
                return null;
            if (_value == null)
                _value = asset.GetType().GetProperty("Value");
            return _value.GetValue(asset) as Texture2D;
        }

        public static void Draw(SpriteBatch sb)
        {
            foreach (var b in Bodies)
                foreach (var p in b.Parts)
                {
                    int type = p.Tile.type;
                    var tex = TileTexture(type);
                    if (tex == null)
                        continue;
                    var pos = new Vector2((b.X + p.Dx) * 16, (b.Y + p.Dy) * 16 + b.Off);
                    var light = Lighting.GetColor((int)(pos.X / 16), (int)(pos.Y / 16));
                    // powders tumble: drawn as a full block whatever their frame was
                    var src = b.Powder ? new Rectangle(18, 18, 16, 16) : new Rectangle(p.Tile.frameX, p.Tile.frameY, 16, 16);
                    sb.Draw(tex, pos - Main.screenPosition, src, light);
                }
        }
    }
}
