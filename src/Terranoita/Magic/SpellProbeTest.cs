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
        // TERRANOITA_PROBE_OUT: another file (a diagnostic run that must not mix with the real rows)
        public static string OutFile => Environment.GetEnvironmentVariable("TERRANOITA_PROBE_OUT") is string o && o.Length > 0 ? o
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "probe_game.jsonl");

        // the probe's timing (tools/noita_probe init.lua): fire for up to 60 frames until something fires, then watch
        // until every shot is gone for 20 frames or 240 frames have passed
        const int Setup = 6, FireMax = 60, MaxFrames = 240, Quiet = 20;
        const float TargetDistance = 160 * Units.PixelScale;   // the probe's target: 160 Noita px to the right
        // the floor and the target against the shot line, Noita px: the probe pins its player 8 px above the floor
        // (init.lua py, arena.png floor rows 188-199), its target's centre is 10 px above the player (ty = py - 4, hitbox
        // -16..4) and Noita's shots start 5.7 px above the player (noita_probe.jsonl: y0 - vy0/60); ours start 0.2 px
        // above the caster's centre (SpellRecorder's x0/y0 when it was the spawn point, probe_game.jsonl before
        // 2026-10-09 evening). Standing, our floor was 7 px under the shot line and shots Noita drops under the target hit
        // ours (HEAVY_BULLET, BOUNCY_ORB, ROCKET, 2026-10-09)
        const float FloorBelowShot = 13.7f, TargetAboveShot = 4.3f, OurShotY = -0.2f;

        static List<ProbeTest> _tests;
        static int _k = -1, _t, _casts0, _mana0, _firedAt, _lastCount, _lastChange, _updates0;
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
            // held in the air at the probe's height (the probe pins its player every frame too)
            p.position = Pinned(p);
            p.velocity = Vector2.Zero;
            p.fallStart = (int)(p.position.Y / 16f);
            if (_target != null && _target.active)
            {
                // the probe's aim: from the player toward the target's origin, 160 px right and 4 px up (init.lua tx, ty);
                // ours is taken from the caster's centre, so the direction is the same
                Casting.TestAim = new Vector2(_target.Center.X, p.Center.Y - 4 * Units.PixelScale);
                // the probe's target (target.xml HitboxComponent -8..8 x -16..4: 16 x 20 Noita px, its centre
                // TargetAboveShot above the shot line), held still there; the zombie's own 9 x 10 let shots Noita counts
                // as hits fly past
                _target.width = (int)(16 * Units.PixelScale);
                _target.height = (int)(20 * Units.PixelScale);
                _target.position = new Vector2(p.Center.X + TargetDistance, p.Center.Y + (OurShotY - TargetAboveShot) * Units.PixelScale)
                                   - new Vector2(_target.width, _target.height) / 2f;
                _target.velocity = Vector2.Zero;
                // damage over time: Terraria's NPC loses -lifeRegen/120 hp a frame to its debuffs (NPC.UpdateNPC_BuffApplyDOTs);
                // burning as Noita reports it ($damage_fire), other debuffs under our own label
                if (_target.lifeRegen < 0)
                    SpellRecorder.Dot(_target, -_target.lifeRegen / 120f,
                        _target.FindBuffIndex(Terraria.ID.BuffID.OnFire) >= 0 || _target.FindBuffIndex(Terraria.ID.BuffID.OnFire3) >= 0 ? "$damage_fire" : "debuff");
            }
            // the probe wand in hand before the cast (2026-10-09: the first test of a run, and keys sent to another
            // window, cast whatever the character held in slot 0)
            if (p.selectedItem != 1)
                p.selectedItemState.Select(1);
            if (t == Setup && MagicItems.WandOf(Casting.HeldWand(p))?.Id != _wand.Id)
            {
                _t = t;   // wait a frame more
                return;
            }
            if (t == Setup)
            {
                _updates0 = SpellShots.Updates;
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
        // of the caster and 188 px above the floor, a floor 12 px thick, a wall from 264 to 280 px to its right
        static int T(float noitaPx) => (int)Math.Round(noitaPx * Units.PixelScale / 16f);

        /// <summary>Where the caster is held: its shot line FloorBelowShot above the arena floor.</summary>
        static Vector2 Pinned(Player p) =>
            new Vector2(_px * 16 + 8 - p.width / 2f, _floor * 16 - (FloorBelowShot + OurShotY) * Units.PixelScale - p.height / 2f);

        /// <summary>The probe's arena, built again before every cast (blasts dig into it).</summary>
        static void Arena()
        {
            int left = _px - T(40), wall = _px + T(264), right = _px + T(280), top = _floor - T(188), bottom = _floor + T(12) - 1;
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
            // TERRANOITA_PROBE_REPEAT: each test cast this many times (one random spread each; probe-compare takes the
            // majority), resuming counts the casts already written
            int repeat = int.TryParse(Environment.GetEnvironmentVariable("TERRANOITA_PROBE_REPEAT"), out int r) && r > 1 ? r : 1;
            var done = new Dictionary<string, int>();
            try
            {
                if (File.Exists(OutFile))
                    foreach (var row in NoitaProbe.Read(File.ReadAllText(OutFile)))
                        done[row.Name] = (done.TryGetValue(row.Name, out int c) ? c : 0) + 1;
            }
            catch (Exception ex) { Entry.Error("probe resume", ex); }
            _tests = new List<ProbeTest>();
            float Num(Dictionary<string, object> d, string k, float fallback) => d.TryGetValue(k, out var v) && v is double x ? (float)x : fallback;
            string[] Ids(Dictionary<string, object> d, string k) => ((d.TryGetValue(k, out var v) ? v as List<object> : null) ?? new List<object>()).Select(x => x as string).ToArray();
            foreach (var o in (MiniJson.Parse(File.ReadAllText(path)) as List<object>) ?? new List<object>())
                if (o is Dictionary<string, object> d && d.TryGetValue("name", out var n) && n is string name &&
                    (string.IsNullOrEmpty(only) || only.Split(',').Any(x => x.Length > 0 && name.Contains(x))))
                    for (int k = done.TryGetValue(name, out int had) ? had : 0; k < repeat; k++)
                        _tests.Add(new ProbeTest
                        {
                            Name = name, Deck = Ids(d, "deck"), AlwaysCast = Ids(d, "always_cast"),
                            SpellsPerCast = (int)Num(d, "spells_per_cast", 1), Spread = Num(d, "spread", 0), SpeedMultiplier = Num(d, "speed_multiplier", 1),
                        });
            _wand = WandStore.NewWand();
            _wand.Name = "probe"; _wand.Sprite = "data/items_gfx/handgun.xml"; _wand.Slots = new[] { "LIGHT_BULLET" }; _wand.Uses = new[] { -1 };
            p.inventory[1] = MagicItems.MakeWand(_wand);
            p.selectedItemState.Select(1);
            SpellRecorder.On = true;
            Entry.Log("PROBE " + _tests.Count + " casts to make (" + repeat + " a test), " + done.Values.Sum() + " done before");
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
            var test = _tests[_k];
            string name = test.Name;
            var deck = test.Deck;
            SpellShots.Clear();
            Arena();
            p.position = Pinned(p);
            p.velocity = Vector2.Zero;
            // every creature near the arena goes (2026-10-09: StrikeNPCNoInteraction left old targets standing in
            // front of the new one; Spark Bolts ended on them and no hit on the target was recorded)
            for (int i = 0; i < Main.maxNPCs; i++)
                if (Main.npc[i].active && !Main.npc[i].townNPC && Vector2.Distance(Main.npc[i].Center, p.Center) < 3000)
                    Main.npc[i].active = false;
            var def = Enemies.All.First(e => e.Id == "zombie_weak");
            int who = Carriers.Spawn(def, (int)(p.Center.X + TargetDistance), _floor * 16);
            _target = who >= 0 ? Main.npc[who] : null;
            if (_target != null)
                _target.lifeMax = _target.life = 100000;
            SpellRecorder.Target = _target;
            Casting.Changed(_wand);
            Casting.TestReady(_wand);
            _wand.CastDelay = 10; _wand.RechargeTime = 30;   // the probe's wand (init.lua make_wand); a "wand:" test's own stats (PC-30)
            _wand.SpellsPerCast = test.SpellsPerCast; _wand.Spread = test.Spread; _wand.SpeedMultiplier = test.SpeedMultiplier;
            _wand.AlwaysCast = test.AlwaysCast.ToList();
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
            var test = _tests[_k];
            var me = Main.LocalPlayer;
            note += "; shot updates " + (SpellShots.Updates - _updates0) + ", live " + SpellShots.LiveCount;
            if (_target != null)
                note += "; target " + (_target.active ? "at " + ((_target.Center - me.Center) / Units.PixelScale).ToString() + " size " + (_target.width / Units.PixelScale) + "x" + (_target.height / Units.PixelScale)
                        + " type " + _target.type + (_target.friendly ? " friendly" : "") + (_target.dontTakeDamage ? " no damage" : "") : "gone");
            try { File.AppendAllText(OutFile, SpellRecorder.Line(test.Name, test.Deck, Casting.TestMana - _mana0, frames, note) + Environment.NewLine); }
            catch (Exception ex) { Entry.Error("probe write", ex); }
            _t = int.MaxValue / 2;   // next frame starts the next test
        }
    }
}
