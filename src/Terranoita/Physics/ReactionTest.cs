using System;
using System.Collections.Generic;
using System.Linq;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_REACTIONS=1 (game_test -Mode reactions): every Noita reaction (reactions.json, from
    /// materials.xml) whose two inputs we can set up from our liquids and gases, Terraria's water and lava, and air gets a
    /// small closed box of Lihzahrd brick (it takes part in no reaction), 3 x 2 inside: input1 in the bottom row, input2
    /// in the top row (or air). A [tag] input gets one of our liquids that carries the tag. After a while the log says,
    /// per rule, how often it happened and whether its outputs are there, and lists the ones that never happened.
    /// Lines start with "REACTIONS".
    /// </summary>
    public static class ReactionTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_REACTIONS") == "1";
        const int Start = 120, Run = 60 * 12;
        public const int Length = Start + Run + 30;
        const int PerRow = 32;
        static readonly List<(ReactionDef r, string a, string b, int x, int y)> Boxes = new List<(ReactionDef, string, string, int, int)>();

        static void Log(string s) => Entry.Log("REACTIONS " + s);
        static int _ticks0;

        /// <summary>A material we can pour for a reaction input: ours by name, Terraria's water or lava, air, or one of
        /// ours that carries the [tag] (the common ones first).</summary>
        static string Material(string input)
        {
            if (input == "air" || input == "water" || input == "lava" || Fluids.KindOf(input) > 0)
                return input;
            if (!input.StartsWith("[") || !input.EndsWith("]"))
                return null;
            string tag = input.Trim('[', ']');
            foreach (var pref in new[] { "water", "lava", "acid", "oil", "blood", "poison", "alcohol", "slime" })
            {
                var l = Liquids.All.FirstOrDefault(x => x.Id == pref);
                if (l != null && (l.Tags ?? new string[0]).Contains(tag))
                    return pref;
            }
            return Liquids.All.FirstOrDefault(x => (x.Tags ?? new string[0]).Contains(tag))?.Id;
        }

        public static void Frame(Player p, int frame)
        {
            if (frame == Start)
                Build(p);
            if (frame == Start + Run)
                Report();
        }

        static void Build(Player p)
        {
            int x0 = (int)(p.Center.X / 16) + 4, floor0 = (int)((p.position.Y + p.height) / 16);
            foreach (var r in Reactions.All)
            {
                if (r.Input3 != "none" || r.Direction != "none")
                    continue;   // a third cell, growth in one direction: not set up here
                string a = Material(r.Input1), b = Material(r.Input2);
                if (a == null || b == null || a == "air" && b == "air")
                    continue;
                int i = Boxes.Count, col = i % PerRow, row = i / PerRow;
                Boxes.Add((r, a, b, x0 + col * 4, floor0 - row * 3));
            }
            int rows = (Boxes.Count + PerRow - 1) / PerRow;
            // clear the place (blocks, Terraria's liquid and ours from earlier runs), then the brick: floors, walls, lids
            Fluids.ClearArea(x0 - 1, floor0 - rows * 3 - 2, x0 + PerRow * 4 + 1, floor0);
            for (int x = x0 - 1; x <= x0 + PerRow * 4 + 1; x++)
                for (int y = floor0 - rows * 3 - 2; y <= floor0; y++)
                {
                    var t = Main.tile[x, y];
                    t.ClearEverything();
                }
            foreach (var bx in Boxes)
                for (int dx = 0; dx <= 4; dx++)
                    for (int dy = 0; dy <= 3; dy++)
                        if (dx == 0 || dx == 4 || dy == 0 || dy == 3)
                            WorldGen.PlaceTile(bx.x + dx, bx.y - dy, TileID.LihzahrdBrick, true, true);
            Fluids.Fired.Clear();
            Fluids.FiredAt.Clear();
            foreach (var bx in Boxes)
                for (int dx = 1; dx <= 3; dx++)
                {
                    if (bx.a != "air")
                        Fluids.Add(bx.x + dx, bx.y - 1, bx.a, 255);
                    if (bx.b != "air")
                        Fluids.Add(bx.x + dx, bx.y - 2, bx.b, 255);
                }
            int gaps = 0;
            foreach (var bx in Boxes)
                for (int dx = 0; dx <= 4; dx++)
                    for (int dy = 0; dy <= 3; dy++)
                        if ((dx == 0 || dx == 4 || dy == 0 || dy == 3) && !(Main.tile[bx.x + dx, bx.y - dy].active() && Main.tile[bx.x + dx, bx.y - dy].type == TileID.LihzahrdBrick))
                            gaps++;
            Log("walls placed, gaps " + gaps);
            _ticks0 = Fluids.Ticks;
            Log(Boxes.Count + " rules set up in boxes at " + x0 + "," + floor0 + " (" + Reactions.All.Length + " rules, the rest need a third cell, a direction or a solid)");
        }

        static void Report()
        {
            int fired = 0;
            var never = new List<string>();
            foreach (var bx in Boxes)
            {
                var r = bx.r;
                int n = Fluids.Fired.Where(kv => kv.Key.StartsWith(r.Id + " ")).Sum(kv => kv.Value);
                string what = r.Id + " " + r.Input1 + "+" + r.Input2 + " (" + bx.a + "+" + bx.b + ", p " + r.Probability + ")" +
                              " -> " + r.Output1 + "+" + r.Output2;
                if (n > 0)
                {
                    fired++;
                    Log("ok " + what + ": x" + n + Outputs(bx));
                }
                else
                    never.Add(what + Outputs(bx) + Probe(bx));
            }
            Log("summary: " + fired + " of " + Boxes.Count + " rules happened in " + Run / 60 + " s; never: " + never.Count +
                "; liquid updates meanwhile " + (Fluids.Ticks - _ticks0) + " (0 = the world was paused: the game window lost focus)");
            foreach (var w in never)
                Log("never " + w);
        }

        static string Rule(string id)
        {
            var r = Reactions.All.FirstOrDefault(x => x.Id == id);
            return r == null ? "?" : r.Input1 + "+" + r.Input2 + "->" + r.Output1 + "+" + r.Output2;
        }

        /// <summary>Tests: each inside cell of the box as "material/Terraria liquid amount".</summary>
        static string Probe((ReactionDef r, string a, string b, int x, int y) bx)
        {
            var cells = new List<string>();
            for (int dy = 2; dy >= 1; dy--)
                for (int dx = 1; dx <= 3; dx++)
                {
                    int x = bx.x + dx, y = bx.y - dy;
                    var t = Main.tile[x, y];
                    cells.Add((Fluids.MaterialAt(x, y) ?? "-") + "/" + t.liquid + (t.liquid > 0 ? (t.liquidType() == LiquidID.Lava ? "L" : "W") : ""));
                }
            int missing = 0;
            for (int dx = 0; dx <= 4; dx++)
                for (int dy = 0; dy <= 3; dy++)
                    if ((dx == 0 || dx == 4 || dy == 0 || dy == 3) && !(Main.tile[bx.x + dx, bx.y - dy].active() && Main.tile[bx.x + dx, bx.y - dy].type == TileID.LihzahrdBrick))
                        missing++;
            var here = new Dictionary<string, int>();
            for (int dy = 1; dy <= 2; dy++)
                for (int dx = 1; dx <= 3; dx++)
                    if (Fluids.FiredAt.TryGetValue(bx.x + dx + (bx.y - dy) * Main.maxTilesX, out var at))
                        foreach (var kv in at)
                            here[kv.Key] = (here.TryGetValue(kv.Key, out int v) ? v : 0) + kv.Value;
            string rules = here.Count == 0 ? "" : " fired here: " + string.Join(" ", here.Select(kv => kv.Key + " " + Rule(kv.Key) + " x" + kv.Value));
            return " [" + string.Join(" ", cells) + "]" + (missing > 0 ? " WALL GAPS " + missing : "") + rules;
        }

        /// <summary>What is in the box now: our materials and Terraria's water/lava, and blocks a reaction left.</summary>
        static string Outputs(( ReactionDef r, string a, string b, int x, int y) bx)
        {
            var have = new Dictionary<string, int>();
            int blocks = 0;
            for (int dx = 1; dx <= 3; dx++)
                for (int dy = 1; dy <= 2; dy++)
                {
                    int x = bx.x + dx, y = bx.y - dy;
                    string m = Fluids.MaterialAt(x, y);   // ours, or Terraria's water / lava
                    if (m != null)
                        have[m] = (have.TryGetValue(m, out int v) ? v : 0) + 1;
                    var t = Main.tile[x, y];
                    if (t.active() && t.type != TileID.LihzahrdBrick)
                        blocks++;
                }
            return "; now " + (have.Count == 0 ? "no liquid" : string.Join(" ", have.Select(kv => kv.Key + ":" + kv.Value))) + (blocks > 0 ? ", " + blocks + " blocks made" : "");
        }
    }
}
