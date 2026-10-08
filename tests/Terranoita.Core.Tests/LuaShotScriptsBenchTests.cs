using System.Collections.Generic;
using System.Diagnostics;
using Terranoita.Noita;
using Xunit;
using Xunit.Abstractions;

namespace Terranoita.Tests
{
    /// <summary>300 shots x 2 scripts x 600 frames: the same executions and results whatever the runtime's bookkeeping.</summary>
    public class LuaShotScriptsBenchTests
    {
        readonly ITestOutputHelper _out;
        public LuaShotScriptsBenchTests(ITestOutputHelper output) { _out = output; }

        sealed class CountingHost : ShotHostBase
        {
            public int Frame;
            public readonly Dictionary<int, float[]> Pos = new Dictionary<int, float[]>();
            public long ACalls, BCalls, BSum;
            public override int FrameNum => Frame;
            public override bool GetPosition(int e, out float x, out float y)
            {
                x = y = 0;
                if (!Pos.TryGetValue(e, out var p)) return false;
                x = p[0]; y = p[1];
                return true;
            }
            public override void Kill(int e) => Pos.Remove(e);
            public override void Screenshake(float x, float y, float strength)
            {
                if (strength == 1) ACalls++;
                else { BCalls++; BSum += (long)strength - 1000; }
            }
        }

        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["long.xml"] = @"<Entity tags=""bench_shot""><VelocityComponent /><ProjectileComponent /></Entity>",
            ["short.xml"] = @"<Entity tags=""bench_shot""><VelocityComponent /><ProjectileComponent /><LifetimeComponent lifetime=""300"" /></Entity>",
            ["a.xml"] = @"<Entity><LuaComponent script_source_file=""a.lua"" execute_every_n_frame=""1"" /></Entity>",
            ["a.lua"] = "GameScreenshake(1)",
            ["b.xml"] = @"<Entity tags=""bench_extra""><LuaComponent script_source_file=""b.lua"" execute_every_n_frame=""3"" execute_times=""50"" /></Entity>",
            ["b.lua"] = @"local me = GetUpdatedEntityID()
local x, y = EntityGetTransform(me)
GameScreenshake(1000 + #EntityGetInRadiusWithTag(x, y, 40, ""bench_extra""))",
        };

        [Fact]
        public void ManyShotsRunTheSameScriptsTheSameTimes()
        {
            var host = new CountingHost();
            var lua = new LuaShotScripts(host, p => Files.TryGetValue(p, out var s) ? s : null);
            const int shots = 300, frames = 600;
            for (int i = 0; i < shots; i++)
            {
                int shot = lua.CreateShot(i % 3 == 0 ? "short.xml" : "long.xml");
                host.Pos[shot] = new float[] { i * 10, 0 };
                lua.AttachExtra(shot, "a.xml");
                lua.AttachExtra(shot, "b.xml");
            }
            var sw = Stopwatch.StartNew();
            for (int f = 1; f <= frames; f++)
            {
                host.Frame = f;
                lua.Update(f);
            }
            sw.Stop();
            _out.WriteLine($"{shots} shots x 2 scripts x {frames} frames: {sw.ElapsedMilliseconds} ms, {host.ACalls + host.BCalls} runs");

            // a: every frame; short shots die at frame 300 (their lifetime) before that frame's scripts
            long shortShots = (shots + 2) / 3;
            Assert.Equal(shortShots * 299 + (shots - shortShots) * frames, host.ACalls);
            // b: every 3rd frame, 50 times (frames 3..150, before any shot dies)
            Assert.Equal(shots * 50L, host.BCalls);
            // b counts the bench_extra entities within 40 px: shots 10 px apart -> neighbours i-4..i+4
            long within = 0;
            for (int i = 0; i < shots; i++)
                within += System.Math.Min(shots - 1, i + 4) - System.Math.Max(0, i - 4) + 1;
            Assert.Equal(within * 50, host.BSum);
            Assert.Empty(lua.Errors);
            Assert.Empty(lua.Missing);
            Assert.Equal(shots - shortShots, host.Pos.Count);   // the short ones were killed through the host
        }
    }
}
