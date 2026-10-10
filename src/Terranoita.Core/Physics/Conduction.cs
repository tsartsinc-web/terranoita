using System;
using System.Collections.Generic;

namespace Terranoita.Physics
{
    /// <summary>What conducts electricity at a tile (the game: our liquids' and Terraria water's `conducts`, metal tiles).</summary>
    public interface IConductGrid
    {
        bool Conducts(int x, int y);
    }

    /// <summary>
    /// Electricity through connected conducting tiles (design/effect_interactions.md section 3): an emission at a spot
    /// charges every conducting tile within the radius and spreads from them through conducting neighbours (4-way),
    /// as far as the energy goes (Noita's ElectricityComponent energy: 1 energy = 1 tile here, to tune on the PC) and
    /// never past MaxSpread tiles per emission or MaxCharged in all. Charged tiles count down and go out; an emission
    /// refreshes them. No allocations after the first calls (buffers are reused).
    /// </summary>
    public sealed class Conduction
    {
        public readonly int Width, MaxSpread, MaxCharged;
        readonly Dictionary<int, int> _charged = new Dictionary<int, int>();   // tile key -> frames left
        readonly Queue<int> _queue = new Queue<int>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _keys = new List<int>();

        public Conduction(int worldWidth, int maxSpread = 400, int maxCharged = 3000)
        {
            Width = worldWidth;
            MaxSpread = maxSpread;
            MaxCharged = maxCharged;
        }

        public int Count => _charged.Count;
        int Key(int x, int y) => x + y * Width;
        public bool Charged(int x, int y) => _charged.ContainsKey(Key(x, y));
        public int FramesLeft(int x, int y) => _charged.TryGetValue(Key(x, y), out int f) ? f : 0;

        /// <summary>Calls back for every charged tile (no list is made).</summary>
        public void ForEach(Action<int, int> tile)
        {
            foreach (int k in _charged.Keys)
                tile(k % Width, k / Width);
        }

        public void Clear() => _charged.Clear();

        /// <summary>Electricity at x,y: how many tiles it charged or refreshed.</summary>
        public int Emit(IConductGrid grid, int x, int y, int radius, int energy, int chargeFrames)
        {
            int budget = Math.Min(Math.Max(0, energy), MaxSpread);
            if (budget == 0 || chargeFrames <= 0)
                return 0;
            _queue.Clear();
            _seen.Clear();
            int r = Math.Max(0, radius);
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r && x + dx >= 0 && x + dx < Width && grid.Conducts(x + dx, y + dy) &&
                        _seen.Add(Key(x + dx, y + dy)))
                        _queue.Enqueue(Key(x + dx, y + dy));
            int done = 0;
            while (_queue.Count > 0 && done < budget)
            {
                int k = _queue.Dequeue();
                if (!Charge(k, chargeFrames))
                    break;   // the world-wide cap is full
                done++;
                int cx = k % Width, cy = k / Width;
                Next(grid, cx, cy - 1);
                Next(grid, cx - 1, cy);
                Next(grid, cx + 1, cy);
                Next(grid, cx, cy + 1);
            }
            return done;
        }

        void Next(IConductGrid grid, int x, int y)
        {
            if (x < 0 || x >= Width || y < 0)
                return;
            int k = Key(x, y);
            if (!_seen.Contains(k) && grid.Conducts(x, y))
            {
                _seen.Add(k);
                _queue.Enqueue(k);
            }
        }

        bool Charge(int k, int frames)
        {
            if (_charged.TryGetValue(k, out int old))
            {
                if (frames > old)
                    _charged[k] = frames;
                return true;
            }
            if (_charged.Count >= MaxCharged)
                return false;
            _charged[k] = frames;
            return true;
        }

        /// <summary>Time passes: charges count down; a tile that stopped conducting (liquid gone) goes out.</summary>
        public void Tick(IConductGrid grid, int frames)
        {
            if (_charged.Count == 0)
                return;
            _keys.Clear();
            _keys.AddRange(_charged.Keys);
            foreach (int k in _keys)
            {
                int left = _charged[k] - frames;
                if (left <= 0 || !grid.Conducts(k % Width, k / Width))
                    _charged.Remove(k);
                else
                    _charged[k] = left;
            }
        }
    }
}
