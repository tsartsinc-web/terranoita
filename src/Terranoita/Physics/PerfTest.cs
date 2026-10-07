using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_FPS=1 (with TERRANOITA_AUTOTEST=1): the player stands by cave pools, then on the surface,
    /// and every 5 s the log gets how long our physics took (author: FPS drops by the caves). Lines start with "PERF".
    /// </summary>
    public static class PerfTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_FPS") == "1";
        const int Stay = 60 * 25, Report = 60 * 5;
        public const int Length = 120 + Stay * 3;

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double _updateMs, _updateMax, _drawMs;
        static int _updates, _draws, _gc0, _frames0;
        static long _wall0;
        static string _place = "start";
        static (int x, int y)[] _spots;

        /// <summary>Time one physics update (Patches).</summary>
        public static void Update(Action a)
        {
            long t = Clock.ElapsedTicks;
            a();
            double ms = (Clock.ElapsedTicks - t) * 1000.0 / Stopwatch.Frequency;
            _updateMs += ms;
            _updateMax = Math.Max(_updateMax, ms);
            _updates++;
        }

        /// <summary>Time one liquids draw (Patches).</summary>
        public static void Draw(Action a)
        {
            long t = Clock.ElapsedTicks;
            a();
            _drawMs += (Clock.ElapsedTicks - t) * 1000.0 / Stopwatch.Frequency;
            _draws++;
        }

        public static void Frame(Player p, int frame)
        {
            if (frame == 60)
            {
                _spots = Fluids.PoolSpots(2);
                Entry.Log("PERF: " + Fluids.Count + " cells in the world, pools tried: " + _spots.Length);
                Entry.Log("PERF cells by liquid: " + Fluids.Census());
                Reset(frame);
            }
            if (frame == 120 || frame == 120 + Stay || frame == 120 + Stay * 2)
            {
                int i = (frame - 120) / Stay;
                if (i < _spots.Length)
                {
                    var (x, y) = _spots[i];
                    p.Teleport(new Vector2(x * 16 - p.width / 2, (y - 4) * 16), 1);
                    _place = "pool " + (i + 1) + " at " + x + "," + y;
                }
                else
                {
                    p.Teleport(new Vector2(Main.spawnTileX * 16, (Main.spawnTileY - 3) * 16), 1);
                    _place = "surface";
                }
                p.velocity = Vector2.Zero;
                Reset(frame);
            }
            p.immune = true;
            p.immuneTime = 2;
            if (frame > 120 && (frame - 120) % Report == 0)
            {
                double secs = (Clock.ElapsedMilliseconds - _wall0) / 1000.0;
                Entry.Log("PERF " + _place + " | world updates/s " + ((frame - _frames0) / secs).ToString("0") +
                          " | physics avg " + (_updateMs / Math.Max(1, _updates)).ToString("0.00") + " ms, max " + _updateMax.ToString("0.0") + " ms" +
                          " | liquids draw avg " + (_drawMs / Math.Max(1, _draws)).ToString("0.00") + " ms (" + _draws + " draws)" +
                          " | GC " + (GC.CollectionCount(0) - _gc0) + " | cells " + Fluids.Count + ", burning " + Fire.Count);
                Reset(frame);
            }
            if (frame == Length)
            {
                Entry.Log("PERF cells by liquid: " + Fluids.Census());
                Entry.Log("AUTOTEST: done (perf)");
            }
        }

        static void Reset(int frame)
        {
            _updateMs = _updateMax = _drawMs = 0;
            _updates = _draws = 0;
            _gc0 = GC.CollectionCount(0);
            _frames0 = frame;
            _wall0 = Clock.ElapsedMilliseconds;
        }
    }
}
