using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_PROBE=1 (game_test -Mode probe; design/magic_plan.md PC-23): the Noita probe's casts
    /// (design/sources/probe_tests.json, the same decks the probe mod cast in Noita) cast once each in Terraria, at a
    /// target as far away as the probe's (160 Noita px), recorded by SpellRecorder in Noita's units into
    /// %LOCALAPPDATA%/Terranoita/probe_game.jsonl (tests already there are skipped). `tncli probe-compare` compares it
    /// with Noita's design/sources/noita_probe.jsonl. TERRANOITA_PROBE_ONLY=substring: just those tests.
    /// </summary>
    public static class SpellProbeTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_PROBE") == "1";
        public static bool Done { get; private set; }
        public static string OutFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "probe_game.jsonl");

        // the probe's timing (tools/noita_probe init.lua): fire for up to 60 frames until something fires, then watch
        // until every shot is gone for 20 frames or 240 frames have passed
        const int Setup = 6, FireMax = 60, MaxFrames = 240, Quiet = 20;
        const float TargetDistance = 160 * Units.PixelScale;   // the probe's target: 160 Noita px to the right

        static List<(string name, string[] deck)> _tests;
        static int _k = -1, _t, _casts0, _mana0, _firedAt, _lastCount, _lastChange;
        static Vector2 _start;
        static int _px, _floor;   // the caster's tile column and the arena floor's top row
        static NPC _target;
        static WandData _wand;

        public static void Frame(Player p, int frame)
        {
            p.statManaMax = p.statManaMax2 = 1000;
            p.statMana = p.statManaMax2;
            if (frame == 60)
                Load(p);
            if (frame < 120 || _tests == null || Done)
                return;
            if (_k < 0 || _t > MaxFrames + Setup + FireMax)
                Next(p);
            if (Done)
                return;
            int t = _t++;
            if (_target != null && _target.active)
            {
                // the probe's aim: its target's centre is 4 Noita px above the wand line (init.lua: target at the
                // caster's height - 4, hitbox -16..4; shots start 6 px up), so the shot's gravity is offset the same way
                Casting.TestAim = new Vector2(_target.Center.X, p.Center.Y - 4 * Units.PixelScale);
                _target.velocity.X = 0;
            }
            if (t == Setup)
            {
                SpellRecorder.Begin(p.Center);
                _casts0 = Casting.TestCasts;
                _mana0 = Casting.TestMana;
                Casting.TestFire = true;
                _firedAt = -1;
            }
            if (t < Setup)
                return;
            int ft = t - Setup;
            bool fired = Casting.TestCasts > _casts0 || SpellRecorder.Count > 0;
            if (fired && _firedAt < 0)
            {
                _firedAt = ft;
                Casting.TestFire = false;   // one cast, as the probe does
            }
            if (_firedAt < 0)
            {
                if (ft >= FireMax)
                    Finish(ft, "nothing fired in " + FireMax + " frames");
                return;
            }
            if (SpellRecorder.Count != _lastCount)
            {
                _lastCount = SpellRecorder.Count;
                _lastChange = ft;
            }
            if (ft >= MaxFrames || SpellRecorder.Alive == 0 && ft - _lastChange >= Quiet && ft - _firedAt >= Quiet)
                Finish(ft, (SpellRecorder.Alive > 0 ? "still flying at the end; " : "") + "fired at frame " + _firedAt);
        }

        // the probe's arena in Noita px (tools/noita_probe/terranoita_probe/files/arena.png, init.lua): air 40 px left
        // and 180 px above the caster, a floor 12 px thick under it, a wall from 264 to 280 px to its right
        static int T(float noitaPx) => (int)Math.Round(noitaPx * Units.PixelScale / 16f);

        /// <summary>The probe's arena, built again before every cast (blasts dig into it).</summary>
        static void Arena()
        {
            int left = _px - T(40), wall = _px + T(264), right = _px + T(280), top = _floor - T(180), bottom = _floor + T(12) - 1;
            for (int x = left - 1; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    if (!WorldGen.InWorld(x, y, 10))
                        continue;
                    var t = Main.tile[x, y];
                    t.ClearEverything();
                    if (y >= _floor || x >= wall)
                    {
                        t.active(true);
                        t.type = Terraria.ID.TileID.Stone;
                    }
                }
            if (Physics.Patches.On)
                Physics.Fluids.Clear();
            for (int i = 0; i < Main.maxItems; i++)
                Main.item[i].inner.TurnToAir();
            WorldGen.RangeFrame(left - 2, top - 1, right + 2, bottom + 1);
        }

        static void Load(Player p)
        {
            _start = p.position;
            _px = (int)(p.Center.X / 16);
            _floor = (int)((p.position.Y + p.height) / 16) + 1;
            string path = Environment.GetEnvironmentVariable("TERRANOITA_PROBE_TESTS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Entry.Log("PROBE no tests file (TERRANOITA_PROBE_TESTS): " + path);
                Done = true;
                return;
            }
            var only = Environment.GetEnvironmentVariable("TERRANOITA_PROBE_ONLY");
            var done = new HashSet<string>();
            try
            {
                if (File.Exists(OutFile))
                    foreach (var row in NoitaProbe.Read(File.ReadAllText(OutFile)))
                        done.Add(row.Name);
            }
            catch (Exception ex) { Entry.Error("probe resume", ex); }
            _tests = new List<(string, string[])>();
            foreach (var o in (MiniJson.Parse(File.ReadAllText(path)) as List<object>) ?? new List<object>())
                if (o is Dictionary<string, object> d && d.TryGetValue("name", out var n) && n is string name &&
                    !done.Contains(name) && (string.IsNullOrEmpty(only) || name.Contains(only)))
                    _tests.Add((name, ((d["deck"] as List<object>) ?? new List<object>()).Select(x => x as string).ToArray()));
            _wand = WandStore.NewWand();
            _wand.Name = "probe"; _wand.Sprite = "data/items_gfx/handgun.xml"; _wand.Slots = new[] { "LIGHT_BULLET" }; _wand.Uses = new[] { -1 };
            p.inventory[1] = MagicItems.MakeWand(_wand);
            p.selectedItemState.Select(1);
            SpellRecorder.On = true;
            Entry.Log("PROBE " + _tests.Count + " tests to cast, " + done.Count + " done before");
        }

        static void Next(Player p)
        {
            _k++;
            _t = 0;
            _lastCount = 0;
            _lastChange = 0;
            if (_k >= _tests.Count)
            {
                Casting.TestFire = false;
                Casting.TestAim = null;
                SpellRecorder.On = false;
                Entry.Log("PROBE done: " + _tests.Count + " casts written to " + OutFile);
                Done = true;
                return;
            }
            var (name, deck) = _tests[_k];
            SpellShots.Clear();
            Arena();
            p.position = new Vector2(_px * 16 + 8 - p.width / 2f, _floor * 16 - p.height);
            p.velocity = Vector2.Zero;
            _target?.StrikeNPCNoInteraction(99999, 0, 0);
            var def = Enemies.All.First(e => e.Id == "zombie_weak");
            int who = Carriers.Spawn(def, (int)(p.Center.X + TargetDistance), _floor * 16);
            _target = who >= 0 ? Main.npc[who] : null;
            if (_target != null)
                _target.lifeMax = _target.life = 100000;
            SpellRecorder.Target = _target;
            Casting.Changed(_wand);
            Casting.TestReady(_wand);
            _wand.CastDelay = 10; _wand.RechargeTime = 30; _wand.SpellsPerCast = 1;   // the probe's wand (init.lua make_wand)
            _wand.Slots = deck;
            _wand.Uses = deck.Select(s => MagicItems.Spell(s)?.MaxUses ?? -1).ToArray();
            p.inventory[1] = MagicItems.MakeWand(_wand);
            p.selectedItemState.Select(1);
            if (_k % 25 == 0)
                Entry.Log("PROBE " + _k + " of " + _tests.Count + ": " + name);
        }

        static void Finish(int frames, string note)
        {
            Casting.TestFire = false;
            var (name, deck) = _tests[_k];
            try { File.AppendAllText(OutFile, SpellRecorder.Line(name, deck, Casting.TestMana - _mana0, frames, note) + Environment.NewLine); }
            catch (Exception ex) { Entry.Error("probe write", ex); }
            _t = int.MaxValue / 2;   // next frame starts the next test
        }
    }
}
