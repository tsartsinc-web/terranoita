using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Noita liquids and gases (design/sheets/liquids.json) on top of Terraria's tiles: our own layer, since Terraria
    /// has room for only its four liquids. A cell holds one material, 0-255 like Terraria's liquid amount. Liquids
    /// fall and level out (heavier ones sink under lighter), gases rise and fade, burnable ones burn, reactions
    /// (reactions.json) run between neighbours, Terraria's water and lava, and blocks (as their Noita material,
    /// noita_solids.json). Touching one gives its Noita status effects (status_effects.json).
    /// </summary>
    public static class Fluids
    {
        const int MaxCells = 40000;
        // Noita's reaction probability (0..100) is per touching pixel pair and frame; a tile is ~5x5 Noita px and a cell
        // looks for reactions every ReactEvery-th pass (8 frames). Ours: a check reacts with chance probability x
        // ReactRate (capped at 1) and changes Portion x max(1, that) of the cell: water + lava (80) turns a tile in ~0.4 s,
        // toxic sludge + water (13) in ~2.6 s, evaporation (15) ~2 s (was 15-30 times slower: author "it does not work")
        const float ReactRate = 2f;
        const float TileReaction = 0.25f;   // eating or changing a whole block: a quarter of that
        const int Portion = 48;             // a reaction changes at least this much of a cell at a time
        const int TileCost = 24;            // what eating a block uses up of an unchanged liquid (acid)
        const int TouchAmount = 24;

        struct Cell
        {
            public byte Kind;     // index into Liquids.All + 1
            public byte Amount;
            public byte Burn;     // > 0: on fire
            public byte Stamp;    // the tick it last moved in: a cell moves once per tick
        }

        static readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();
        static LiquidDef[] _defs;
        static Dictionary<string, int> _index;
        static HashSet<string>[] _tags;
        static HashSet<string>[] _burningTags;
        static bool[] _fading;
        static readonly Dictionary<string, HashSet<string>> SolidTagSets = new Dictionary<string, HashSet<string>>();
        static Color[] _colors;
        static Dictionary<string, NoitaSolidDef> _solids;
        static Dictionary<string, ushort> _tileOfSolid;
        static List<ReactionDef>[] _reactions;
        static int _frame;
        static readonly List<int> KeysBuffer = new List<int>();
        static readonly Comparison<int> Descending = (a, b) => b.CompareTo(a);
        static byte _stamp;
        static int _pass;
        const int FarX = 160, FarY = 100, ReactEvery = 4, FarEvery = 32;
        static bool _far;   // the cell being stepped is far from the player
        static Texture2D _pixel;

        public static int Count => Cells.Count;
        public static void Clear() { lock (SaveSync.Gate) Cells.Clear(); }

        /// <summary>Tests: our liquids and gases in a box of tiles go (they are saved with the world: a test scene built
        /// again on the same spot found the last run's liquids still there).</summary>
        public static void ClearArea(int x1, int y1, int x2, int y2)
        {
            lock (SaveSync.Gate)
                for (int x = x1; x <= x2; x++)
                    for (int y = y1; y <= y2; y++)
                        Cells.Remove(Key(x, y));
        }

        static void Build()
        {
            _reacts = null;   // built again from the new tables when first asked
            _defs = Liquids.All;
            _index = new Dictionary<string, int>();
            _tags = new HashSet<string>[_defs.Length + 1];
            _burningTags = new HashSet<string>[_defs.Length + 1];
            _fading = new bool[_defs.Length + 1];
            _colors = new Color[_defs.Length + 1];
            for (int i = 0; i < _defs.Length; i++)
            {
                _index[_defs[i].Id] = i + 1;
                _tags[i + 1] = new HashSet<string>(_defs[i].Tags ?? new string[0]) { "=" + _defs[i].Id };
                // Noita's _inherit_reactions: it takes part in the reactions that name its parent too
                foreach (var parent in _defs[i].ReactsAs ?? new string[0])
                    _tags[i + 1].Add("=" + parent);
                _fading[i + 1] = _defs[i].Id.EndsWith("_fading", StringComparison.Ordinal);
                _burningTags[i + 1] = new HashSet<string>(_tags[i + 1]) { "fire", "=fire" };
                _colors[i + 1] = ParseColor(_defs[i].Color);
            }
            _solids = NoitaSolids.All.ToDictionary(s => s.Id);
            _tileOfSolid = new Dictionary<string, ushort>();
            foreach (var s in NoitaSolids.All)
            {
                var f = s.TerrariaTile == "none" ? null : typeof(TileID).GetField(s.TerrariaTile);
                if (f != null)
                    _tileOfSolid[s.Id] = Convert.ToUInt16(f.GetValue(null));
            }
            _reactions = new List<ReactionDef>[_defs.Length + 1];
            for (int k = 1; k <= _defs.Length; k++)
                _reactions[k] = Reactions.All.Where(r => Matches(r.Input1, _tags[k]) || Matches(r.Input2, _tags[k])).ToList();
        }

        static Color ParseColor(string argb)
        {
            uint v = uint.TryParse(argb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0xff808080;
            byte a = (byte)(v >> 24), r = (byte)(v >> 16), g = (byte)(v >> 8), b = (byte)v;
            return new Color(r, g, b, Math.Max(a, (byte)140));
        }

        static bool Matches(string input, HashSet<string> tags)
        {
            if (string.IsNullOrEmpty(input) || tags == null)
                return false;
            // [tag] matches a tag; a bare name only that material ("=name"): magic liquids carry the [water] tag
            // and must not pass for the material water
            return tags.Contains(MatchKey(input));
        }

        // the tag or "=name" an input looks up, made once (author: big FPS drops near cave pools from new strings every tick)
        static readonly Dictionary<string, string> MatchKeys = new Dictionary<string, string>();

        static string MatchKey(string input)
        {
            if (!MatchKeys.TryGetValue(input, out var key))
                MatchKeys[input] = key = input[0] == '[' ? input.Trim('[', ']') : "=" + input;
            return key;
        }

        public static int KindOf(string material)
        {
            if (_defs == null)
                Build();
            return material != null && _index.TryGetValue(material, out int k) ? k : 0;
        }

        static int Key(int x, int y) => x + y * Main.maxTilesX;

        /// <summary>Pour amount of a Noita liquid or gas into the tile at x,y (spreads into free neighbours).
        /// Returns how much went in (the rest did not fit: a flask keeps it).</summary>
        public static int Add(int x, int y, string material, int amount)
        {
            lock (SaveSync.Gate)
                return AddLocked(x, y, material, amount);
        }

        static int AddLocked(int x, int y, string material, int amount)
        {
            // Terraria's own liquids are Terraria's
            if (material == "lava" || material == "water")
                return AddTerraria(x, y, material == "lava" ? LiquidID.Lava : LiquidID.Water, amount);
            int kind = KindOf(material);
            if (kind == 0 || amount <= 0)
                return 0;
            int wanted = amount;
            // spread over the open tiles connected to x,y (never through a wall)
            var open = new Queue<(int, int)>();
            var seen = new HashSet<int>();
            open.Enqueue((x, y));
            seen.Add(Key(x, y));
            while (open.Count > 0 && amount > 0 && seen.Count < 64)
            {
                var (cx, cy) = open.Dequeue();
                if (!Open(cx, cy))
                    continue;
                int k = Key(cx, cy);
                Cells.TryGetValue(k, out var c);
                if (c.Amount == 0 || c.Kind == kind)
                {
                    int put = Math.Min(255 - c.Amount, amount);
                    if (put > 0 && (c.Amount > 0 || Cells.Count < MaxCells))
                    {
                        Cells[k] = new Cell { Kind = (byte)kind, Amount = (byte)(c.Amount + put), Burn = c.Burn };
                        amount -= put;
                    }
                }
                foreach (var (nx, ny) in new[] { (cx, cy - 1), (cx - 1, cy), (cx + 1, cy), (cx, cy + 1) })
                    if (seen.Add(Key(nx, ny)) && Open(nx, ny))
                        open.Enqueue((nx, ny));
            }
            return wanted - amount;
        }

        /// <summary>
        /// Terraria's oceans (the beach strips at both world edges, above the caverns) and the Underworld's lava: Noita's
        /// liquids and reactions never change Terraria's liquid there, so a player cannot spoil the world for good (author).
        /// </summary>
        public static bool Protected(int x, int y) =>
            y >= Main.UnderworldLayer || (x < OceanWidth || x >= Main.maxTilesX - OceanWidth) && y < Main.rockLayer;
        const int OceanWidth = 380;   // Terraria's beach zone (Player.ZoneBeach: 380 tiles from either edge)

        static int AddTerraria(int x, int y, int type, int amount)
        {
            if (!Mats.InWorld(x, y) || Mats.Solid(x, y) || Protected(x, y))
                return 0;
            var t = Main.tile[x, y];
            if (t.liquid > 0 && t.liquidType() != type)
                return 0;
            int put = Math.Min(255 - t.liquid, amount);
            if (put <= 0)
                return 0;
            t.liquidType(type);
            t.liquid = (byte)(t.liquid + put);
            Liquid.AddWater(x, y);
            return put;
        }

        /// <summary>Tests: cells that swapped places with another liquid (settled liquids stop swapping).</summary>
        public static int Swaps, Bobs;

        /// <summary>A liquid (ours, not a gas, or Terraria's) fills a good part of the tile: spell shots slow down in it.</summary>
        public static bool LiquidAt(int x, int y)
        {
            if (!Mats.InWorld(x, y))
                return false;
            if (Main.tile[x, y].liquid > 64)
                return true;
            return _defs != null && Cells.TryGetValue(Key(x, y), out var c) && c.Amount > 64 && !Gas(c.Kind);
        }

        /// <summary>Electricity goes through this tile: a liquid that conducts in Noita (liquids.json conducts) fills a
        /// good part of it, ours or Terraria's water/lava (Electricity.cs).</summary>
        public static bool Conducts(int x, int y)
        {
            if (!Mats.InWorld(x, y) || _defs == null)
                return false;
            if (Cells.TryGetValue(Key(x, y), out var c) && c.Amount > 32)
                return _defs[c.Kind - 1].Conducts && !Gas(c.Kind);
            var t = Main.tile[x, y];
            if (t.liquid <= 32 || (t.liquidType() != LiquidID.Water && t.liquidType() != LiquidID.Lava))
                return false;
            return _index.TryGetValue(t.liquidType() == LiquidID.Water ? "water" : "lava", out int k) && _defs[k - 1].Conducts;
        }

        /// <summary>Not a block: liquids and gases can be here.</summary>
        static bool Open(int x, int y)
        {
            if (!Mats.InWorld(x, y))
                return false;
            var t = Main.tile[x, y];
            return !(t.active() && !t.inActive() && Main.tileSolid[t.type] && !Main.tileSolidTop[t.type]);
        }

        static bool Gas(int kind) => _defs[kind - 1].Kind == "gas";

        // ---- simulation ----

        /// <summary>World updates run so far (Terraria pauses the world when its window is not active).</summary>
        public static int Ticks => _frame;

        public static void Update()
        {
            if (_defs == null)
                Build();
            _frame++;
            if (Cells.Count == 0)
                return;
            if (_frame % 2 == 0)
            {
                // one list kept and reused (author: no garbage every tick)
                _stamp = (byte)(_stamp % 255 + 1);
                _pass++;
                // far from the player (two screens and more) a cell only falls and fades, every FarEvery-th pass:
                // no reactions there, so far pools do not boil off into gas nobody sees (author: FPS by the caves)
                var p = Main.LocalPlayer.Center;
                int px = (int)(p.X / 16), py = (int)(p.Y / 16), W = Main.maxTilesX;
                var keys = KeysBuffer;
                keys.Clear();
                foreach (int k in Cells.Keys)
                    if (Math.Abs(k % W - px) <= FarX && Math.Abs(k / W - py) <= FarY || (k + _pass) % FarEvery == 0)
                        keys.Add(k);
                // bottom cells first for liquids, so a column falls together
                keys.Sort(Descending);
                foreach (int k in keys)
                    if (Cells.ContainsKey(k))
                    {
                        _far = Math.Abs(k % W - px) > FarX || Math.Abs(k / W - py) > FarY;
                        Step(k);
                    }
                _far = false;
            }
            Touch();
        }

        static void Step(int k)
        {
            var c = Cells[k];
            if (c.Stamp == _stamp && !_far && (k + _pass) % ReactEvery == 0)
                React(k % Main.maxTilesX, k / Main.maxTilesX);   // it moved this tick: no second move, but it reacts
            if (!Cells.TryGetValue(k, out c) || c.Stamp == _stamp)
                return;   // arrived here this tick
            int x = k % Main.maxTilesX, y = k / Main.maxTilesX;
            var d = _defs[c.Kind - 1];
            bool gas = d.Kind == "gas";
            if (!Open(x, y))
            {
                Cells.Remove(k);   // a block was placed here
                return;
            }
            // author: gases that reach space and liquids that reach the underworld are gone (Terraria's lava stays)
            if (gas ? y < Main.worldSurface * 0.35 : y > Main.UnderworldLayer)
            {
                Cells.Remove(k);
                return;
            }
            // gases fade (Noita lifetime in frames), fading liquids too
            if (d.Lifetime > 0 || _fading[c.Kind])
            {
                // a share of what is there, so a gas spread thin over many tiles lasts as long as a thick one
                float share = c.Amount * 2f / (d.Lifetime > 0 ? d.Lifetime : 900f) * (_far ? FarEvery : 1);
                int fade = (int)share + (Main.rand.NextFloat() < share - (int)share ? 1 : 0);
                if (fade >= c.Amount)
                {
                    Cells.Remove(k);
                    return;
                }
                c.Amount -= (byte)fade;
            }
            if (d.OnFire)
                c.Burn = 1;   // Noita's "fire" (oil's burning child) is always alight
            if (c.Burn > 0 && !Burn(x, y, ref c, d))
            {
                Cells.Remove(k);
                return;
            }
            Cells[k] = c;
            // reactions are looked up every 4th pass, 4 times as likely: the same rate for a quarter of the work
            if (!_far && (k + _pass) % ReactEvery == 0)
                React(x, y);
            if (!Cells.TryGetValue(k, out c))
                return;
            // Terraria's own liquid came in: ours goes on top of it when lighter (Noita densities: blood 4.1 sinks in water 4.0)
            var t = Main.tile[x, y];
            if (!gas && (t.liquid > 32 && Protected(x, y) || Mats.InWorld(x, y + 1) && Main.tile[x, y + 1].liquid > 32 && Protected(x, y + 1)))
            {
                Cells.Remove(k);   // it reached the ocean or the Underworld's lava: lost in it, nothing changes there
                return;
            }
            // Terraria's liquid filled this tile (more than half: its surface wobbles, a lower mark made ours bob up and
            // down for ever, author: "liquids jump endlessly"): ours, lighter, goes up on top of it
            if (!gas && t.liquid > PushUp && !HeavierThanTerraria(c.Kind, t.liquidType()))
            {
                if (MoveAll(k, x, y - 1, c))
                    return;
            }
            int dy = gas ? -1 : 1;
            // gases rise about 7 tiles a second, liquids fall like Terraria's
            if ((!gas || Main.rand.Next(4) == 0) && Flow(k, x, y, x, y + dy, ref c, 255))
                return;
            // gases drift; liquids level out sideways, thick ones slowly
            int first = Main.rand.Next(2) == 0 ? -1 : 1;
            float div = gas ? 2.5f : 3f + d.Viscosity / 20f;
            // a gas spreads sideways only while it is thick, and lazily: it should hang as a cloud
            bool spread = !gas || (c.Amount >= 24 && Main.rand.Next(3) == 0);
            for (int s = 0; s < 2 && spread; s++)
            {
                int dx = s == 0 ? first : -first;
                int nk = Key(x + dx, y);
                Cells.TryGetValue(nk, out var n);
                if (!Open(x + dx, y))
                    continue;
                var side = Main.tile[x + dx, y];
                if (!gas && side.liquid > 32)
                {
                    // Terraria's water or lava beside: ours goes under it sideways when heavier (Noita: the heavier liquid
                    // creeps under the lighter one) and Terraria's liquid takes our place; no wall between them (author)
                    var here = Main.tile[x, y];
                    if (n.Amount == 0 && here.liquid <= 32 && !Protected(x, y) && !Protected(x + dx, y) &&
                        HeavierThanTerraria(c.Kind, side.liquidType()) && CanSink(c.Kind, x + dx, y) && Main.rand.Next(3) == 0)
                    {
                        here.liquidType(side.liquidType());
                        here.liquid = side.liquid;
                        side.liquid = 0;
                        Liquid.AddWater(x, y);
                        Liquid.AddWater(x + dx, y);
                        c.Stamp = _stamp;
                        Cells.Remove(k);
                        Cells[nk] = c;
                        Swaps++;
                        return;
                    }
                    continue;
                }
                if (n.Amount > 0 && n.Kind != c.Kind)
                {
                    // two different liquids side by side: about the same density and reacting, they stir together (Noita:
                    // blood and water); otherwise the heavier creeps under the lighter (takes its place; the lighter then
                    // floats up over it): no invisible wall between two liquids (author). A sideways swap at one height
                    // changes nothing by itself, so the heavier only moves where it can sink next (else two liquids in one
                    // row swapped back and forth for ever, author: "liquids jump endlessly"). Gases drift through each other.
                    bool swap = gas ? Gas(n.Kind) && Main.rand.Next(12) == 0
                                    : !Gas(n.Kind) && (Mixes(c.Kind, n.Kind) || Heavier(c.Kind, n.Kind) && CanSink(c.Kind, x + dx, y) && Main.rand.Next(3) == 0);
                    if (swap)
                    {
                        c.Stamp = n.Stamp = _stamp;
                        Cells[nk] = c;
                        Cells[k] = n;
                        Swaps++;
                        return;
                    }
                    continue;
                }
                int diff = c.Amount - n.Amount;
                if (diff < 2)
                    continue;
                int move = Math.Max(1, (int)(diff / div));
                Flow(k, x, y, x + dx, y, ref c, move);
                if (!Cells.TryGetValue(k, out c))
                    return;
            }
            if (gas && Main.rand.Next(12) == 0)
                Flow(k, x, y, x + (Main.rand.Next(2) == 0 ? -1 : 1), y + Main.rand.Next(-1, 2), ref c, c.Amount / 2 + 1);
            if (Cells.TryGetValue(k, out c) && c.Amount < (gas ? 2 : 3))
                Cells.Remove(k);
        }

        /// <summary>Move up to max from k into x2,y2 (same material or empty), or swap with a lighter one below. True if all moved.</summary>
        static bool Flow(int k, int x, int y, int x2, int y2, ref Cell c, int max)
        {
            if (!Open(x2, y2))
                return false;
            bool gas = Gas(c.Kind);
            int nk = Key(x2, y2);
            Cells.TryGetValue(nk, out var n);
            var below = Main.tile[x2, y2];
            if (!gas && below.liquid > 0)
            {
                // ours sinks through Terraria's water or lava when heavier: Terraria's liquid takes our place above
                var here = Main.tile[x, y];
                if (below.liquid <= 32 && y2 > y && HeavierThanTerraria(c.Kind, below.liquidType()) == false)
                    return false;   // a lighter one rests on Terraria's liquid, even a thin film of it
                if (y2 <= y || n.Amount > 0 || here.liquid > 32 || Protected(x, y) || Protected(x2, y2) || !HeavierThanTerraria(c.Kind, below.liquidType()) || Main.rand.Next(2) != 0)
                    return false;
                here.liquidType(below.liquidType());
                here.liquid = below.liquid;
                below.liquid = 0;
                Liquid.AddWater(x, y);
                Liquid.AddWater(x2, y2);
                c.Stamp = _stamp;
                Cells.Remove(k);
                Cells[nk] = c;
                return true;
            }
            if (n.Amount > 0 && n.Kind != c.Kind)
            {
                // heavier sinks: a liquid falls through a lighter liquid or any gas, a gas rises through liquids
                bool swap = y2 > y ? Heavier(c.Kind, n.Kind) : y2 < y && Heavier(n.Kind, c.Kind);
                if (swap && Main.rand.Next(2) == 0 || !gas && Mixes(c.Kind, n.Kind))
                {
                    c.Stamp = n.Stamp = _stamp;
                    Cells[nk] = c;
                    Cells[k] = n;
                    Swaps++;
                    return true;
                }
                return false;
            }
            int move = Math.Min(Math.Min(max, c.Amount), 255 - n.Amount);
            if (move <= 0)
                return false;
            if (n.Amount == 0 && Cells.Count >= MaxCells)
                return false;
            Cells[nk] = new Cell { Kind = c.Kind, Amount = (byte)(n.Amount + move), Burn = (byte)Math.Max(n.Burn, c.Burn), Stamp = _stamp };
            c.Amount -= (byte)move;
            if (c.Amount == 0)
            {
                Cells.Remove(k);
                return true;
            }
            Cells[k] = c;
            return false;
        }

        /// <summary>The whole cell moves to x,y (up out of Terraria's liquid); false (and it stays) when there is no room:
        /// a cell above full of Terraria's liquid, another liquid or too little space (it used to vanish then).</summary>
        static bool MoveAll(int k, int x, int y, Cell c)
        {
            if (!Open(x, y) || Main.tile[x, y].liquid > PushUp)
                return false;
            int nk = Key(x, y);
            Cells.TryGetValue(nk, out var n);
            if (n.Amount > 0 && (n.Kind != c.Kind || n.Amount + c.Amount > 255))
                return false;
            Cells.Remove(k);
            Cells[nk] = new Cell { Kind = c.Kind, Amount = (byte)(n.Amount + c.Amount), Burn = c.Burn, Stamp = _stamp };
            Bobs++;
            return true;
        }

        // ours is pushed up out of a tile once Terraria's liquid fills more than half of it, and falls only into a tile
        // with none at all: between the two it stays put (a dead band, so Terraria's wobbling surface does not toss it)
        const int PushUp = 128;

        /// <summary>Two liquids of about the same density that react swap now and then, so they mix and react all through.
        /// Ones that do not react settle by density instead: stirring them only made them jump about for ever (author).</summary>
        static bool Mixes(int a, int b) =>
            !Gas(a) && !Gas(b) && Math.Abs(_defs[a - 1].Density - _defs[b - 1].Density) < MixDensity && Reacts(a, b) && Main.rand.Next(MixEvery) == 0;

        static bool[,] _reacts;

        /// <summary>reactions.json has a reaction between these two kinds (either way round).</summary>
        static bool Reacts(int a, int b)
        {
            if (_reacts == null)
            {
                int n = _defs.Length + 1;
                _reacts = new bool[n, n];
                for (int i = 1; i < n; i++)
                    foreach (var r in _reactions[i])
                        for (int j = 1; j < n; j++)
                            if (Matches(r.Input1, _tags[i]) && Matches(r.Input2, _tags[j]) || Matches(r.Input2, _tags[i]) && Matches(r.Input1, _tags[j]))
                                _reacts[i, j] = _reacts[j, i] = true;
            }
            return _reacts[a, b];
        }

        /// <summary>A liquid of this kind put at x,y could go down from there: the cell below is open and empty, or holds a
        /// lighter liquid of ours or of Terraria's.</summary>
        static bool CanSink(int kind, int x, int y)
        {
            if (!Open(x, y + 1))
                return false;
            Cells.TryGetValue(Key(x, y + 1), out var b);
            if (b.Amount > 0)
                return Heavier(kind, b.Kind);
            var t = Main.tile[x, y + 1];
            return t.liquid <= 32 || !Protected(x, y + 1) && HeavierThanTerraria(kind, t.liquidType());
        }
        const float MixDensity = 0.6f;   // ours: water 4.0, blood 4.1, swamp 3.5 mix; oil 1.0 stays on top
        const int MixEvery = 6;

        /// <summary>Ours against Terraria's own water/lava/honey, by Noita's densities (honey: ours, 5).</summary>
        static bool HeavierThanTerraria(int kind, int liquidType)
        {
            if (Gas(kind))
                return false;
            string id = liquidType == LiquidID.Lava ? "lava" : liquidType == LiquidID.Water ? "water" : null;
            int k = id == null ? 0 : KindOf(id);
            float d = k > 0 ? _defs[k - 1].Density : 5f;
            return _defs[kind - 1].Density > d;
        }

        static bool Heavier(int a, int b)
        {
            bool ga = Gas(a), gb = Gas(b);
            if (ga != gb)
                return !ga;
            return _defs[a - 1].Density > _defs[b - 1].Density;
        }

        // ---- fire ----

        static bool Burn(int x, int y, ref Cell c, LiquidDef d)
        {
            // burns down by fire_hp (Noita: oil 1000ish frames per pixel): a full tile of oil burns ~8 s
            int loss = d.FireHp >= 100000 ? 0 : Math.Max(1, (int)(255 * 2 * 5 / Math.Max(60f, d.FireHp)));
            if (loss >= c.Amount)
                return false;
            c.Amount -= (byte)loss;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (Main.rand.Next(6) == 0)
                        Fire.Ignite(x + dx, y + dy);
                    int nk = Key(x + dx, y + dy);
                    if (Cells.TryGetValue(nk, out var n) && n.Burn == 0 && _defs[n.Kind - 1].Burnable && Main.rand.Next(3) == 0)
                    {
                        n.Burn = 1;
                        Cells[nk] = n;
                    }
                }
            return true;
        }

        /// <summary>Noita's MagicConvertMaterialComponent: the material "from" within r tiles of cx,cy becomes "to"
        /// (Noita's liquids here, Terraria's water and lava too; "air" removes it). How many tiles changed.
        /// A "to" we do not have (a solid, an unknown material) changes nothing, so nothing vanishes.</summary>
        public static int ConvertMaterial(int cx, int cy, int r, string from, string to)
        {
            lock (SaveSync.Gate)
                return ConvertLocked(cx, cy, r, from, to);
        }

        static int ConvertLocked(int cx, int cy, int r, string from, string to)
        {
            int kf = KindOf(from), kt = KindOf(to), n = 0;
            int terrariaFrom = from == "water" ? LiquidID.Water : from == "lava" ? LiquidID.Lava : -1;
            if (kf == 0 && terrariaFrom < 0)
                return 0;
            if (kt == 0 && to != "water" && to != "lava" && to != "air")
                return 0;
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r || !Mats.InWorld(x, y))
                        continue;
                    int k = Key(x, y);
                    int amount = 0;
                    if (kf > 0 && Cells.TryGetValue(k, out var c) && c.Kind == kf)
                    {
                        amount = c.Amount;
                        Cells.Remove(k);
                    }
                    else if (terrariaFrom >= 0 && Main.tile[x, y].liquid > 0 && Main.tile[x, y].liquidType() == terrariaFrom && !Protected(x, y))
                    {
                        amount = Main.tile[x, y].liquid;
                        Main.tile[x, y].liquid = 0;
                        Liquid.AddWater(x, y);
                    }
                    if (amount == 0)
                        continue;
                    n++;
                    if (kt > 0 || to == "water" || to == "lava")
                        Add(x, y, to, amount);
                }
            return n;
        }

        /// <summary>Set the liquid at x,y on fire if it burns.</summary>
        public static void Ignite(int x, int y)
        {
            int k = Key(x, y);
            if (Cells.TryGetValue(k, out var c) && _defs[c.Kind - 1].Burnable)
            {
                c.Burn = 1;
                lock (SaveSync.Gate)
                    Cells[k] = c;
            }
        }

        /// <summary>Where a material is, relative to x0,y0 (tests).</summary>
        public static string Where(string material, int x0, int y0)
        {
            int kind = KindOf(material);
            return string.Join(" ", Cells.Where(kv => kv.Value.Kind == kind).Take(12)
                .Select(kv => (kv.Key % Main.maxTilesX - x0) + "," + (kv.Key / Main.maxTilesX - y0) + ":" + kv.Value.Amount));
        }

        // ---- names the player knows (author: shown under the mouse once touched, as in Noita) ----

        static HashSet<string> _known;
        static bool _hoverLogged;
        static string _hoverText;

        /// <summary>After the cursor is drawn: the hover name next to it, so nothing draws over it.</summary>
        public static void DrawHoverText()
        {
            if (_hoverText == null)
                return;
            Utils.DrawBorderString(Main.spriteBatch, _hoverText, new Vector2(Main.mouseX + 20, Main.mouseY + 20), Color.White);
            _hoverText = null;
        }
        static string _knownFor;

        static string KnownFile(string player) =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita",
                                   "known_" + string.Concat((player ?? "player").Split(System.IO.Path.GetInvalidFileNameChars())) + ".txt");

        static HashSet<string> Known()
        {
            string who = Main.LocalPlayer?.name;
            if (_known != null && _knownFor == who)
                return _known;
            _knownFor = who;
            _known = new HashSet<string>();
            try
            {
                if (System.IO.File.Exists(KnownFile(who)))
                    _known.UnionWith(System.IO.File.ReadAllLines(KnownFile(who)));
            }
            catch (Exception ex) { Entry.Error("known load", ex); }
            return _known;
        }

        public static int KnownCount => Known().Count;

        /// <summary>The liquids and gases this character has touched (moved into the progress book once).</summary>
        public static IEnumerable<string> KnownIds => Known().ToList();

        /// <summary>Forget every name (the audit's character must not leave the author's tester knowing all).</summary>
        public static void ForgetAll()
        {
            Known().Clear();
            try { System.IO.File.Delete(KnownFile(_knownFor)); } catch (Exception ex) { Entry.Error("known forget", ex); }
        }

        /// <summary>The player touched (or drank) this material: from now on its name shows under the mouse.</summary>
        public static void Learn(string id)
        {
            ProgressWindow.Touch(id);
            if (!Known().Add(id))
                return;
            try
            {
                var f = KnownFile(_knownFor);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                System.IO.File.WriteAllLines(f, _known);
            }
            catch (Exception ex) { Entry.Error("known save", ex); }
        }

        /// <summary>The name of the liquid or gas under the mouse, if the player knows it (interface drawing).</summary>
        public static void HoverName()
        {
            if (Cells.Count == 0 || Main.gameMenu)
                return;
            int x = (int)(Main.MouseWorld.X / 16), y = (int)(Main.MouseWorld.Y / 16);
            if (!Cells.TryGetValue(Key(x, y), out var c) || c.Amount < 8)
                return;
            var d = _defs[c.Kind - 1];
            // author: an unknown one shows as ???; shown the way Terraria shows a sign's text (Main.DrawMouseOver)
            string text = !Known().Contains(d.Id) ? "???" :
                NoitaArt.Text(d.NameKey, d.Id) + (c.Burn > 0 && !d.OnFire ? " (" + NoitaArt.Text("mat_fire", "fire") + ")" : "");
            _hoverText = text;   // drawn on top of everything with the cursor (DrawHoverText)
            if (!_hoverLogged)
            {
                _hoverLogged = true;
                Entry.Log("hover name shown: " + text + " (" + d.Id + ")");
            }
        }

        /// <summary>How many liquids or gases the box touches (tests).</summary>
        public static int UnderCount(Rectangle box) => Under(box).Count();

        public static bool Has(int x, int y) => Cells.ContainsKey(Key(x, y));

        /// <summary>The liquid at a tile: ours, else Terraria's water or lava (honey is no Noita material: null).</summary>
        public static string MaterialAt(int x, int y)
        {
            if (!Mats.InWorld(x, y))
                return null;
            if (Cells.TryGetValue(Key(x, y), out var c) && c.Amount > 8)
                return _defs[c.Kind - 1].Id;
            var t = Main.tile[x, y];
            if (t.liquid > 32)
                return t.liquidType() == LiquidID.Lava ? "lava" : t.liquidType() == LiquidID.Water ? "water" : null;
            return null;
        }

        /// <summary>The player drinks from the tile (author: hold down in a liquid, as in Noita): takes up to amount of the
        /// liquid there (ours, or Terraria's water/lava/honey) and says which; null if nothing to drink.</summary>
        public static string Drink(int x, int y, int amount)
        {
            if (!Mats.InWorld(x, y))
                return null;
            lock (SaveSync.Gate)
            {
                int k = Key(x, y);
                if (Cells.TryGetValue(k, out var c) && c.Amount > 0)
                {
                    var d = _defs[c.Kind - 1];
                    if (d.Kind != "liquid")
                        return null;
                    int take = Math.Min(amount, (int)c.Amount);
                    if (c.Amount - take <= 0)
                        Cells.Remove(k);
                    else
                    {
                        c.Amount = (byte)(c.Amount - take);
                        Cells[k] = c;
                    }
                    return d.Id;
                }
            }
            var t = Main.tile[x, y];
            if (t.liquid == 0)
                return null;
            if (!Protected(x, y))   // the ocean is drunk from, never drained
                t.liquid = (byte)Math.Max(0, t.liquid - amount);
            Liquid.AddWater(x, y);
            int type = t.liquidType();
            return type == LiquidID.Lava ? "lava" : type == LiquidID.Honey ? "honey" : "water";
        }

        // ---- kept with the world: <world>.wld.fluids (material names, so the sheet may change) ----

        static string FluidsFile => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".fluids";

        /// <summary>Which cave pools this world has had (CavePools.Version); kept in the liquids file.</summary>
        public static int PoolsVersion;

        /// <summary>Read the world's liquids; false when the world has none of ours yet (then the caves get pools).</summary>
        public static bool Load()
        {
            if (_defs == null)
                Build();
            lock (SaveSync.Gate)
            {
                Cells.Clear();
                ToxicGround.Clear();
                PoolsVersion = 0;
                var path = FluidsFile;
                if (path == null || !System.IO.File.Exists(path))
                    return false;
                int version = 0;
                try
                {
                    using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                    {
                        int w = r.ReadInt32(), kinds = r.ReadInt32();
                        var map = new byte[kinds + 1];
                        for (int i = 1; i <= kinds; i++)
                            map[i] = (byte)KindOf(r.ReadString());
                        int n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                        {
                            int k = r.ReadInt32();
                            byte kind = r.ReadByte(), amount = r.ReadByte(), burn = r.ReadByte();
                            if (kind <= kinds && map[kind] > 0)
                                Cells[k % w + k / w * Main.maxTilesX] = new Cell { Kind = map[kind], Amount = amount, Burn = burn };
                        }
                        // 0.3.0 files end here: their caves have the first, sparse pools
                        version = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 1;
                        if (r.BaseStream.Position < r.BaseStream.Length)
                            ToxicGround.Read(r, w);
                    }
                    PoolsVersion = version;   // only after the whole file was read
                    Entry.Log("fluids: " + Cells.Count + " cells read from " + System.IO.Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    // a cut or broken file: keep what was read, set the file aside, and never pour the cave pools
                    // again on a world that already has them
                    Entry.Error("fluids load", ex);
                    SaveSync.SetAside(path);
                    PoolsVersion = CavePools.Version;
                }
                return true;
            }
        }

        public static void Save()
        {
            var path = FluidsFile;
            if (path == null || _defs == null)
                return;
            try
            {
                // may run on Terraria's autosave thread: copy under the lock, write outside it
                KeyValuePair<int, Cell>[] cells;
                KeyValuePair<int, string>[] toxic;
                int pools, width = Main.maxTilesX;
                lock (SaveSync.Gate)
                {
                    cells = new KeyValuePair<int, Cell>[Cells.Count];
                    ((ICollection<KeyValuePair<int, Cell>>)Cells).CopyTo(cells, 0);
                    toxic = ToxicGround.Snapshot();
                    pools = PoolsVersion;
                }
                var defs = _defs;
                SaveSync.WriteAtomic(path, w =>
                {
                    w.Write(width);
                    w.Write(defs.Length);
                    foreach (var d in defs)
                        w.Write(d.Id);
                    w.Write(cells.Length);
                    foreach (var kv in cells)
                    {
                        w.Write(kv.Key);
                        w.Write(kv.Value.Kind);
                        w.Write(kv.Value.Amount);
                        w.Write(kv.Value.Burn);
                    }
                    w.Write(pools);
                    ToxicGround.Write(w, toxic);
                });
            }
            catch (Exception ex) { Entry.Error("fluids save", ex); }
        }

        /// <summary>Cells sitting inside solid blocks: should be none (tests).</summary>
        public static int InsideBlocks() => Cells.Keys.Count(k => !Open(k % Main.maxTilesX, k / Main.maxTilesX));

        /// <summary>How much of a material is in the area (tests).</summary>
        public static int Total(int x1, int x2, int y1, int y2, string material)
        {
            int kind = KindOf(material), sum = 0;
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    if (Cells.TryGetValue(Key(x, y), out var c) && c.Kind == kind)
                        sum += c.Amount;
            return sum;
        }

        /// <summary>Cells per material, the most first, with how many are thin (tests).</summary>
        public static string Census() => string.Join(", ", Cells.Values.GroupBy(c => c.Kind).OrderByDescending(g => g.Count()).Take(8)
            .Select(g => _defs[g.Key - 1].Id + " " + g.Count() + " (thin " + g.Count(c => c.Amount < 64) + ")"));

        /// <summary>Cells of a material (cave pool banks).</summary>
        public static List<int> CellsOf(string material)
        {
            int kind = KindOf(material);
            var list = new List<int>();
            foreach (var kv in Cells)
                if (kv.Value.Kind == kind)
                    list.Add(kv.Key);
            return list;
        }

        /// <summary>Up to n full liquid cells under the surface, far apart (tests).</summary>
        public static (int x, int y)[] PoolSpots(int n)
        {
            var spots = new List<(int x, int y)>();
            foreach (var kv in Cells)
            {
                int x = kv.Key % Main.maxTilesX, y = kv.Key / Main.maxTilesX;
                if (Gas(kv.Value.Kind) || kv.Value.Amount < 200 || y < Main.worldSurface || spots.Any(s => Math.Abs(s.x - x) < 200))
                    continue;
                spots.Add((x, y));
                if (spots.Count == n)
                    break;
            }
            return spots.ToArray();
        }

        public static bool BurningAt(int x, int y) => Cells.TryGetValue(Key(x, y), out var c) && c.Burn > 0;

        // ---- reactions ----

        static void React(int x, int y)
        {
            int k = Key(x, y);
            var c = Cells[k];
            var list = _reactions[c.Kind];
            var me = _tags[c.Kind];
            var d = _defs[c.Kind - 1];
            // the four neighbours, and Terraria's water or lava in this very tile: Terraria's liquid flows into our
            // cells' tiles (it does not see them), so a heavier liquid of ours under lava never touched it otherwise
            bool shared = Main.tile[x, y].liquid > 32;
            for (int s = 0; s < (shared ? 5 : 4); s++)
            {
                int nx = x + (s == 0 ? 1 : s == 1 ? -1 : 0), ny = y + (s == 2 ? 1 : s == 3 ? -1 : 0);
                if (!Mats.InWorld(nx, ny))
                    continue;
                // a burnable liquid next to fire catches it
                if (d.Burnable && c.Burn == 0 && IsFire(nx, ny))
                {
                    c.Burn = 1;
                    Cells[k] = c;
                }
                if (list.Count == 0)
                    continue;
                What what;
                var other = s == 4 ? TerrariaTags(x, y, out what) : TagsAt(nx, ny, out what);
                if (other == null)
                    continue;
                foreach (var r in list)
                {
                    bool first = Matches(r.Input1, me) && Matches(r.Input2, other);
                    bool second = !first && Matches(r.Input2, me) && Matches(r.Input1, other);
                    if (!first && !second)
                        continue;
                    // Noita's direction: where input2 is from input1 (fungi grow up into the air above them)
                    if (!DirectionOk(r.Direction, nx - x, ny - y, first))
                        continue;
                    // the share of a check this reaction happens in, and how much it changes then
                    float q = r.Probability / 100f * ReactRate * (what == What.Tile ? TileReaction : 1f);
                    if (Main.rand.NextFloat() >= q)
                        continue;
                    // Noita's third cell (input_cell3): it must touch too, and becomes output_cell3
                    int tx = 0, ty = 0;
                    What tw = What.Air;
                    if (r.Input3 != "none" && !FindThird(x, y, nx, ny, r.Input3, out tx, out ty, out tw))
                        continue;
                    int amount = (int)Math.Min(255f, Portion * Math.Max(1f, q));
                    string mine = first ? r.Output1 : r.Output2, theirs = first ? r.Output2 : r.Output1;
                    string myIn = first ? r.Input1 : r.Input2, theirIn = first ? r.Input2 : r.Input1;
                    // outputs written with a tag stand for the material that matched it: [evaporable_custom]_vapour of
                    // blood_cold is blood_cold_vapour, [lava] stays what it was
                    mine = Resolve(myIn, mine, _defs[c.Kind - 1].Id);
                    theirs = Resolve(theirIn, theirs, NameAt(nx, ny, what));
                    if (PhysicsTest.Enabled || ReactionTest.Enabled)
                    {
                        Fired[r.Id + " " + r.Input1 + "+" + r.Input2] = (Fired.TryGetValue(r.Id + " " + r.Input1 + "+" + r.Input2, out int fc) ? fc : 0) + 1;
                        if (!FiredAt.TryGetValue(k, out var at))
                            FiredAt[k] = at = new Dictionary<string, int>();
                        at[r.Id] = (at.TryGetValue(r.Id, out int ac) ? ac : 0) + 1;
                    }
                    bool iChange = Changes(myIn, mine);
                    Change(nx, ny, what, theirIn, theirs, x, y, amount);
                    if (r.Input3 != "none")
                        Change(tx, ty, tw, r.Input3, Resolve(r.Input3, r.Output3, NameAt(tx, ty, tw)), x, y, amount);
                    if (iChange)
                        Change(x, y, What.Cell, myIn, mine, nx, ny, amount);
                    else if (what == What.Tile && Cells.TryGetValue(k, out c))
                    {
                        // eating a block uses some up (Noita keeps the acid; here a pool would eat half the world)
                        if (c.Amount <= TileCost) Cells.Remove(k);
                        else { c.Amount -= TileCost; Cells[k] = c; }
                    }
                    if (r.Explosion > 0)
                        Blast.Explode(new Vector2(x * 16 + 8, y * 16 + 8), r.Explosion * 3, true);
                    return;
                }
            }
        }

        /// <summary>Reactions that happened (tests), and where (tile key -> rule id -> times).</summary>
        public static readonly Dictionary<string, int> Fired = new Dictionary<string, int>();
        public static readonly Dictionary<int, Dictionary<string, int>> FiredAt = new Dictionary<int, Dictionary<string, int>>();

        enum What { Cell, Terraria, Tile, Air }

        static readonly HashSet<string> Air = new HashSet<string> { "=air" };
        static readonly HashSet<string> FireTags = new HashSet<string> { "fire", "=fire" };

        /// <summary>Terraria's water or lava in a tile (not the ocean's or the Underworld's), as Noita tags.</summary>
        static HashSet<string> TerrariaTags(int x, int y, out What what)
        {
            what = What.Terraria;
            var t = Main.tile[x, y];
            if (t.liquid <= 32 || Protected(x, y))
                return null;
            int lt = t.liquidType();
            return lt == LiquidID.Lava ? _tags[KindOf("lava")] : lt == LiquidID.Water ? _tags[KindOf("water")] : null;
        }

        /// <summary>What is at x,y as Noita sees it: our cell, Terraria's water or lava, a block's material, or air.</summary>
        static HashSet<string> TagsAt(int x, int y, out What what)
        {
            if (Cells.TryGetValue(Key(x, y), out var c))
            {
                what = What.Cell;
                return c.Burn > 0 ? _burningTags[c.Kind] : _tags[c.Kind];
            }
            var t = Main.tile[x, y];
            if (t.active() && Main.tileSolid[t.type] && !t.inActive())
            {
                what = What.Tile;
                if (Fire.BurningAt(x, y))
                    return FireTags;
                return SolidTags(x, y, t);
            }
            if (t.liquid > 32 && Protected(x, y))
            {
                what = What.Terraria;
                return null;   // the ocean and the Underworld's lava take part in no reaction
            }
            if (t.liquid > 32)
            {
                what = What.Terraria;
                int lt = t.liquidType();
                return lt == LiquidID.Lava ? _tags[KindOf("lava")] : lt == LiquidID.Water ? _tags[KindOf("water")] : null;
            }
            what = What.Air;
            return Fire.BurningAt(x, y) ? FireTags : Air;
        }

        static HashSet<string> SolidTags(int x, int y, Tile t)
        {
            if (!Blast.Breakable(x, y))
                return null;   // dungeon, temple, chests...: Noita's indestructible
            var m = Mats.Of(t);
            string name = m != null && m.NoitaMaterial != "-" ? m.NoitaMaterial : "rock_static";
            if (SolidTagSets.TryGetValue(name, out var tags))
                return tags;
            if (_solids.TryGetValue(name, out var s))
                tags = new HashSet<string>(s.Tags ?? new string[0]) { "=" + s.Id };
            SolidTagSets[name] = tags;
            return tags;
        }

        static bool IsFire(int x, int y) =>
            Fire.BurningAt(x, y) || BurningAt(x, y) || (Main.tile[x, y].liquid > 32 && Main.tile[x, y].liquidType() == LiquidID.Lava);

        static bool Changes(string input, string output) =>
            !string.IsNullOrEmpty(output) && output != input && output != "none" && !output.StartsWith("[");   // [fire] stays fire, [acid] stays acid

        /// <summary>An output written with a tag means the material that matched that tag: "[evaporable_custom]_vapour"
        /// for blood_cold is blood_cold_vapour (when Noita has that material), "[lava]" for lava stays lava. A tag output
        /// that cannot be made concrete leaves it as it was.</summary>
        static string Resolve(string input, string output, string actual)
        {
            if (string.IsNullOrEmpty(output) || output[0] != '[' || actual == null)
                return output;
            int end = output.IndexOf(']');
            if (end < 0)
                return input;
            if (output.Substring(0, end + 1) != input)
                return output == "[fire]" ? "fire" : input;   // alcohol + lava -> [fire]: it burns; another tag: unknown material, no change
            string made = actual + output.Substring(end + 1);
            return made == actual ? input : KindOf(made) > 0 || made == "water" || made == "lava" || _solids.ContainsKey(made) ? made : input;
        }

        /// <summary>Noita material at x,y as the reaction saw it (our cell, Terraria's water or lava, a block's material).</summary>
        static string NameAt(int x, int y, What what)
        {
            switch (what)
            {
                case What.Cell: return Cells.TryGetValue(Key(x, y), out var c) ? _defs[c.Kind - 1].Id : null;
                case What.Terraria: return Main.tile[x, y].liquidType() == LiquidID.Lava ? "lava" : "water";
                case What.Tile: var m = Mats.Of(Main.tile[x, y]); return m != null && m.NoitaMaterial != "-" ? m.NoitaMaterial : "rock_static";
                default: return "air";
            }
        }

        /// <summary>Noita's reaction direction: input2 must be on that side of input1 (dx, dy: the other cell from this
        /// one; meFirst: this cell is input1).</summary>
        static bool DirectionOk(string direction, int dx, int dy, bool meFirst)
        {
            if (string.IsNullOrEmpty(direction) || direction == "none")
                return true;
            if (!meFirst)
            {
                dx = -dx;
                dy = -dy;
            }
            switch (direction)
            {
                case "top": return dy == -1;
                case "bottom": return dy == 1;
                case "left": return dx == -1;
                case "right": return dx == 1;
            }
            return true;
        }

        /// <summary>A cell of input3 touching this one (not the other input's cell).</summary>
        static bool FindThird(int x, int y, int ox, int oy, string input3, out int tx, out int ty, out What what)
        {
            for (int s = 0; s < 4; s++)
            {
                tx = x + (s == 0 ? 1 : s == 1 ? -1 : 0);
                ty = y + (s == 2 ? 1 : s == 3 ? -1 : 0);
                if ((tx == ox && ty == oy) || !Mats.InWorld(tx, ty))
                    continue;
                var tags = TagsAt(tx, ty, out what);
                if (Matches(input3, tags))
                    return true;
            }
            tx = ty = 0;
            what = What.Air;
            return false;
        }

        static bool IsSolidOutput(string output) =>
            output != "air" && output != "fire" && KindOf(output) == 0;

        /// <summary>
        /// A reaction turns what is at x,y into output. Liquids and gases change a portion at a time (Noita reacts pixel
        /// by pixel); the new material goes into the same cell if it emptied, else next door (sx,sy) or above.
        /// </summary>
        static void Change(int x, int y, What what, string input, string output, int sx, int sy, int portion = Portion)
        {
            if (!Changes(input, output))
                return;
            int k = Key(x, y);
            var t = Main.tile[x, y];
            int amount = portion;
            switch (what)
            {
                case What.Cell:
                    if (!Cells.TryGetValue(k, out var c))
                        return;
                    if (IsSolidOutput(output))
                    {
                        Cells.Remove(k);
                        PlaceSolid(x, y, output);
                        return;
                    }
                    amount = Math.Min((int)c.Amount, portion);
                    if (c.Amount <= amount) Cells.Remove(k);
                    else { c.Amount -= (byte)amount; Cells[k] = c; }
                    break;
                case What.Terraria:
                    if (Protected(x, y))
                        return;
                    amount = Math.Min((int)t.liquid, portion);
                    t.liquid -= (byte)amount;
                    if (IsSolidOutput(output))
                    {
                        if (t.liquid < 32)
                        {
                            t.liquid = 0;
                            PlaceSolid(x, y, output);
                        }
                        return;
                    }
                    Liquid.AddWater(x, y);
                    break;
                case What.Tile:
                    Placed.Remove(x, y);
                    WorldGen.KillTile(x, y, false, false, true);
                    if (t.active())
                        return;
                    if (IsSolidOutput(output))
                    {
                        PlaceSolid(x, y, output);
                        return;
                    }
                    amount = 160;
                    break;
            }
            Put(output, amount, x, y, sx, sy);
        }

        static void Put(string output, int amount, int x, int y, int sx, int sy)
        {
            if (output == "air" || amount <= 0)
                return;
            if (output == "fire")
            {
                Fire.Ignite(x, y);
                Ignite(x, y);
                return;
            }
            if (output == "water" || output == "lava")
            {
                AddTerraria(x, y, output == "lava" ? LiquidID.Lava : LiquidID.Water, amount);
                return;
            }
            int kind = KindOf(output);
            foreach (var (px, py) in new[] { (x, y), (sx, sy), (x, y - 1) })
            {
                if (!Open(px, py))
                    continue;
                int pk = Key(px, py);
                Cells.TryGetValue(pk, out var n);
                if (n.Amount > 0 && n.Kind != kind)
                    continue;
                if (n.Amount == 0 && Cells.Count >= MaxCells)
                    return;
                Cells[pk] = new Cell { Kind = (byte)kind, Amount = (byte)Math.Min(255, n.Amount + amount), Burn = n.Burn };
                return;
            }
        }

        static void PlaceSolid(int x, int y, string output)
        {
            if (_tileOfSolid.TryGetValue(output, out ushort tile) && !Main.tile[x, y].active())
            {
                WorldGen.PlaceTile(x, y, tile, true, true);
                ToxicGround.Mark(x, y, output);   // lava + toxic sludge: toxic rock
                Falling.Disturb(x, y);
            }
        }

        // ---- touching ----

        static void Touch()
        {
            var me = Main.LocalPlayer;
            if (me.active && !me.dead && me.wet && !me.lavaWet && !me.honeyWet && !me.shimmerWet)
                Status.Stain(new[] { "WET" });   // Terraria's water is Noita's water
            if (me.active && !me.dead)
                foreach (var (d, burning) in Under(me.Hitbox))
                {
                    Learn(d.Id);
                    if (d.TouchEffects != null && d.TouchEffects.Length > 0)
                    {
                        Status.Stain(d.TouchEffects);
                        // Noita remove_cells_that_cause_when_activated (polymorphine): the liquid is used up
                        if (d.TouchEffects.Any(Status.RemovesCause))
                            RemoveUnder(me.Hitbox, d);
                    }
                    if (burning)
                        Status.Apply("ON_FIRE");
                    // Noita's player_base.xml materials_that_damage: hp units per frame, 1 unit = 25 hp
                    if (d.TouchDamage >= 100)
                        me.KillMe(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(me.name + " touched " + NoitaArt.Text(d.NameKey, d.Id) + "."), 9999, 0);
                    else if (d.TouchDamage != 0)
                        Status.TouchHurt(d.TouchDamage * 25f * 60f);
                    if (d.Kind == "liquid" && d.Viscosity > 0 && _frame % 2 == 0)
                        me.velocity *= 1f - Math.Min(0.15f, d.Viscosity / 400f);
                }
            if (_frame % 10 != 0)
                return;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly)
                    continue;
                foreach (var (d, burning) in Under(n.Hitbox))
                {
                    if (burning)
                        n.AddBuff(BuffID.OnFire, 120);
                    // what hurts the player hurts creatures too (acid, lava-like, cursed...): Noita's per-frame damage, every 10 frames
                    if (d.TouchDamage > 0 && !n.dontTakeDamage)
                        n.StrikeNPCNoInteraction(Math.Max(1, (int)Math.Round(d.TouchDamage * 25f * 10f)), 0f, 0);
                    foreach (var e in d.TouchEffects ?? new string[0])
                        switch (e)
                        {
                            case "ON_FIRE": n.AddBuff(BuffID.OnFire, 120); break;
                            case "POISONED": n.AddBuff(BuffID.Poisoned, 120); break;
                            case "RADIOACTIVE": n.AddBuff(BuffID.Venom, 120); break;
                            case "SLIMY": n.AddBuff(BuffID.Slimed, 120); break;
                            case "WET": n.AddBuff(BuffID.Wet, 120); break;
                            case "OILED": n.AddBuff(BuffID.Oiled, 120); break;
                        }
                }
            }
        }

        static void RemoveUnder(Rectangle box, LiquidDef d)
        {
            int kind = KindOf(d.Id);
            for (int x = box.Left / 16 - 1; x <= (box.Right - 1) / 16 + 1; x++)
                for (int y = box.Top / 16 - 1; y <= (box.Bottom - 1) / 16 + 1; y++)
                    if (Cells.TryGetValue(Key(x, y), out var c) && c.Kind == kind)
                        Cells.Remove(Key(x, y));
        }

        static IEnumerable<(LiquidDef, bool)> Under(Rectangle box)
        {
            var seen = new HashSet<int>();
            for (int x = box.Left / 16; x <= (box.Right - 1) / 16; x++)
                for (int y = box.Top / 16; y <= (box.Bottom - 1) / 16; y++)
                    if (Cells.TryGetValue(Key(x, y), out var c) && c.Amount >= TouchAmount)
                    {
                        // the liquid's surface: only the part of the tile it fills
                        if (!Gas(c.Kind) && box.Bottom <= y * 16 + 16 - c.Amount / 16)
                            continue;
                        if (seen.Add(c.Kind * 2 + (c.Burn > 0 ? 1 : 0)))
                            yield return (_defs[c.Kind - 1], c.Burn > 0);
                    }
        }

        // ---- drawing ----

        public static void Draw(SpriteBatch sb)
        {
            if (Cells.Count == 0)
                return;
            if (_pixel == null)
            {
                _pixel = new Texture2D(Main.instance.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            int x0 = (int)(Main.screenPosition.X / 16) - 1, y0 = (int)(Main.screenPosition.Y / 16) - 1;
            int x1 = x0 + Main.screenWidth / 16 + 3, y1 = y0 + Main.screenHeight / 16 + 3;
            foreach (var kv in Cells)
            {
                int x = kv.Key % Main.maxTilesX, y = kv.Key / Main.maxTilesX;
                if (x < x0 || x > x1 || y < y0 || y > y1)
                    continue;
                var c = kv.Value;
                var d = _defs[c.Kind - 1];
                var col = _colors[c.Kind];
                bool gas = d.Kind == "gas";
                if (d.Glow > 0 || c.Burn > 0)
                {
                    float g = c.Burn > 0 ? 1f : Math.Min(1f, d.Glow / 200f);
                    Lighting.AddLight(x, y, col.R / 255f * g, col.G / 255f * g, col.B / 255f * g);
                    if (c.Burn > 0)
                        Lighting.AddLight(x, y, 0.9f, 0.45f, 0.1f);
                }
                else
                {
                    var light = Lighting.GetColor(x, y);
                    col = new Color(col.R * light.R / 255, col.G * light.G / 255, col.B * light.B / 255, col.A);
                }
                Rectangle r;
                if (gas)
                {
                    float a = c.Amount / 255f * 0.8f;
                    col *= a;
                    r = new Rectangle(x * 16, y * 16, 16, 16);
                }
                else
                {
                    // liquids sit at the bottom of their tile, unless more of the same is above
                    int h = Math.Max(1, c.Amount * 16 / 255);
                    if (Cells.TryGetValue(kv.Key - Main.maxTilesX, out var up) && up.Kind == c.Kind && !Gas(up.Kind))
                        h = 16;
                    r = new Rectangle(x * 16, y * 16 + 16 - h, 16, h);
                    // see-through like Terraria's water (author), whatever Noita's alpha
                    col *= Math.Min(col.A, (byte)150) / 255f;
                }
                r.Offset((int)-Main.screenPosition.X, (int)-Main.screenPosition.Y);
                sb.Draw(_pixel, r, col);
                if (c.Burn > 0 && Main.rand.Next(5) == 0)
                    Dust.NewDustDirect(new Vector2(x * 16, y * 16), 16, 16, DustID.Torch, 0f, -2f, 100, default(Color), 1.5f).noGravity = true;
            }
        }
    }
}
