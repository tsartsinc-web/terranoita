using System;
using System.IO;
using System.Linq;
using Terranoita.Noita;
using Xunit;
using Xunit.Abstractions;

namespace Terranoita.Tests
{
    public class LuaGoldenTests
    {
        readonly ITestOutputHelper _out;
        public LuaGoldenTests(ITestOutputHelper o) { _out = o; }

        [Fact]
        public void DiffShowsChangedGoneAndNew()
        {
            string golden = "A | shots 1 a | mana 5\nB | shots 1 b | mana 10\r\nC | shots 2 c | mana 1\n";
            var d = LuaGolden.Diff(golden, new[] { "A | shots 1 a | mana 5", "B | shots 1 b | mana 12", "D | shots 0 | mana 0" });
            Assert.Equal(3, d.Count);
            Assert.StartsWith("changed: B | shots 1 b | mana 12", d[0]);
            Assert.Contains("was: B | shots 1 b | mana 10", d[0]);
            Assert.Equal("new: D | shots 0 | mana 0", d[1]);
            Assert.Equal("gone: C | shots 2 c | mana 1", d[2]);
            Assert.Empty(LuaGolden.Diff(golden, golden.Split('\n')));
        }

        [Fact]
        public void TheGoldenFileIsWellFormed()
        {
            string path = Path.Combine(Root(), "design", "sources", "lua_cast_golden.txt");
            var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
            Assert.True(lines.Count > 300);
            Assert.All(lines, l => Assert.True(l.Contains(" | shots ") || l.Contains(" | ERROR "), l));
            Assert.Equal(lines.Count, lines.Select(l => l.Split(new[] { " | " }, StringSplitOptions.None)[0]).Distinct().Count());
        }

        /// <summary>On the PC (TERRANOITA_NOITA_DIR = the Noita folder): today's gun.lua run equals the golden file.</summary>
        [Fact]
        public void GunLuaStillCastsLikeTheGoldenFile()
        {
            string noita = Environment.GetEnvironmentVariable("TERRANOITA_NOITA_DIR");
            if (string.IsNullOrEmpty(noita) || !Directory.Exists(noita))
            {
                _out.WriteLine("skipped: TERRANOITA_NOITA_DIR not set (needs the player's Noita)");
                return;
            }
            using (var files = new NoitaFiles(noita))
            {
                var diff = LuaGolden.Diff(File.ReadAllText(Path.Combine(Root(), "design", "sources", "lua_cast_golden.txt")),
                                          LuaGolden.Lines(p => files.TryReadText(p, out var t) ? t : null));
                Assert.True(diff.Count == 0, string.Join("\n", diff.Take(20)));
            }
        }

        static string Root()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md")))
                d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("repository root");
        }
    }
}
