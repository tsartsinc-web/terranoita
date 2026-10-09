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
                Casting.TestAim = _target.Center;
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

        static void Load(Player p)
        {
            _start = p.position;
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
            p.position = _start;
            p.velocity = Vector2.Zero;
            _target?.StrikeNPCNoInteraction(99999, 0, 0);
            var def = Enemies.All.First(e => e.Id == "zombie_weak");
            int who = Carriers.Spawn(def, (int)(p.Center.X + TargetDistance), (int)(p.position.Y + p.height));
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
