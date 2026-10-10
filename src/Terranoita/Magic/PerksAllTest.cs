using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_PERKSALL=1 (game_test -Mode perks_all, PC-36): every Noita perk alone, then a fixed scenario,
    /// compared with the same scenario without a perk. Per perk one line "PERKSALL &lt;ID&gt;: WORKS|NO EFFECT|ERROR: what
    /// changed", and a summary line. Stackable perks are taken twice as well ("ID x2").
    /// Scenario: stand (life, speed, jump, flight, immunities, the perk's Noita components), a hit of 20, fire / poison /
    /// electricity / freeze / bleeding touched, one cast of a Spark Bolt wand at a target, a creature killed nearby.
    /// </summary>
    public static class PerksAllTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_PERKSALL") == "1";
        public static bool Done;
        const int Per = 150;   // frames a case

        static List<(string id, int times)> _cases;
        static int _k = -1, _t;
        static Dictionary<string, string> _base, _now;
        static int _works, _none, _errors;
        static readonly List<string> NoEffect = new List<string>(), Errors = new List<string>();
        static Vector2 _spot;
        static WandData _wand;
        static NPC _target;
        static int _casts0, _items0, _npcs0, _storeErrors0;

        static void Log(string s) => Entry.Log("PERKSALL " + s);
        static string F(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

        public static void Frame(Player p, int frame)
        {
            if (Done)
                return;
            try
            {
                if (_cases == null)
                {
                    if (frame < 60)
                        return;
                    Setup(p);
                }
                Step(p);
            }
            catch (Exception ex)
            {
                Entry.Error("PERKSALL " + (_k >= 0 && _k < _cases.Count ? _cases[_k].id : "setup"), ex);
                if (_k >= 0 && _k < _cases.Count)
                    Errors.Add(_cases[_k].id + " (test: " + ex.Message + ")");
                _t = Per;   // on to the next case
            }
        }

        static void Setup(Player p)
        {
            _cases = new List<(string, int)> { ("", 0) };   // the case without a perk first: the baseline
            var only = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_ONLY");
            foreach (var k in Perks.All)
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(k.Id))
                    continue;
                _cases.Add((k.Id, 1));
                if (k.Stackable)
                    _cases.Add((k.Id, 2));
            }
            _spot = p.position;
            _wand = WandStore.NewWand();
            _wand.Name = "perk test"; _wand.Sprite = "data/items_gfx/handgun.xml";
            _wand.Slots = new[] { "LIGHT_BULLET" }; _wand.Uses = new[] { -1 };
            _wand.CastDelay = 10; _wand.RechargeTime = 30; _wand.ManaMax = 300; _wand.ManaChargeSpeed = 100;
            Log(Perks.All.Count + " perks, " + (_cases.Count - 1) + " cases");
            Next(p);
        }

        static void Next(Player p)
        {
            _k++;
            _t = 0;
            if (_k >= _cases.Count)
            {
                Perks.TestClear(p);
                Log("done: works " + _works + ", no effect " + _none + ", error " + _errors + " of " + (_cases.Count - 1) + " cases");
                Log("no effect: " + string.Join(", ", NoEffect));
                Log("error: " + string.Join(", ", Errors));
                Done = true;
            }
        }

        static void Step(Player p)
        {
            var (id, times) = _cases[_k];
            int t = _t++;
            if (t == 0)
            {
                // a fresh character: no perks, full life, no buffs, at the spot, the test wand in hand
                Perks.TestClear(p);
                for (int b = 0; b < p.buffTime.Length; b++)
                    p.buffTime[b] = 0;
                p.statLife = p.statLifeMax2;
                p.immune = false;
                p.immuneTime = 0;
                p.position = _spot;
                p.velocity = Vector2.Zero;
                SpellShots.Clear();
                foreach (var it in Main.item)
                    if (it.active)
                        it.TurnToAir();
                for (int i = 0; i < Main.maxNPCs; i++)
                    if (Main.npc[i].active && !Main.npc[i].townNPC && Vector2.Distance(Main.npc[i].Center, p.Center) < 2500)
                        Main.npc[i].active = false;
                p.inventory[1] = MagicItems.MakeWand(_wand);
                p.selectedItemState.Select(1);
                Perks.FuncErrors.Clear();
                _storeErrors0 = SpellShots.ScriptStore?.Errors.Count ?? 0;
                for (int n = 0; n < times; n++)
                    Perks.TestTake(p, id);
                _now = new Dictionary<string, string>();
            }
            else if (t == 5)
            {
                // standing: what Terraria computed this frame with the perk
                _now["max life"] = p.statLifeMax2.ToString();
                _now["max mana"] = p.statManaMax2.ToString();
                _now["run speed"] = F(p.maxRunSpeed) + "/" + F(p.runAcceleration);
                _now["move speed"] = F(p.moveSpeed);
                _now["jump"] = F(p.jumpSpeedBoost) + "/" + Player.jumpHeight;
                _now["flight"] = p.wingTimeMax.ToString() + "/" + (p.rocketBoots > 0 ? "rocket" : "");
                _now["gravity"] = F(p.gravity);
                _now["defense"] = p.statDefense.ToString();
                _now["endurance"] = F(p.endurance);
                _now["regen"] = p.lifeRegen.ToString();
                _now["immune"] = string.Join(",", new[] { BuffID.OnFire, BuffID.Burning, BuffID.Poisoned, BuffID.Venom, BuffID.Electrified,
                    BuffID.Frozen, BuffID.Chilled, BuffID.Bleeding, BuffID.Confused, BuffID.Slow }.Where(b => p.buffImmune[b]).Select(b => b.ToString())) +
                    (p.lavaImmune ? " lava" : "") + (p.fireWalk ? " firewalk" : "") + (p.gills ? " gills" : "") + (p.noKnockback ? " noknockback" : "");
                int who = Perks.PlayerEntity(p);
                var store = SpellShots.ScriptStore;
                if (who != 0 && store != null)
                {
                    var comps = store.Components(who, null).GroupBy(c => c.Type).OrderBy(g => g.Key).Select(g => g.Key + (g.Count() > 1 ? "x" + g.Count() : ""));
                    _now["noita parts"] = string.Join(",", comps);
                    _now["noita children"] = store.ChildrenOf(who).Count().ToString();
                    var dm = store.Components(who, "DamageModelComponent", false).FirstOrDefault();
                    _now["noita hp"] = dm == null ? "" : dm.Get("max_hp") + "/" + dm.Get("max_hp_cap");
                }
                _now["shot modifiers"] = string.Join(",", Perks.ShotModifiers(p));
                // a hit: what reaches the life
                int before = p.statLife;
                p.Hurt(PlayerDeathReason.ByCustomReason(p.name + " was tested."), 20, 0);
                _now["hit 20"] = (before - p.statLife).ToString();
                p.statLife = p.statLifeMax2;
                // touched by fire, poison, electricity, ice, bleeding: does the debuff stick
                var tried = new[] { BuffID.OnFire, BuffID.Poisoned, BuffID.Electrified, BuffID.Frozen, BuffID.Bleeding };
                foreach (int b in tried)
                    p.AddBuff(b, 60);
                _now["debuffs taken"] = string.Join(",", tried.Where(b => p.FindBuffIndex(b) >= 0).Select(b => b.ToString()));
                for (int b = 0; b < p.buffTime.Length; b++)
                    p.buffTime[b] = 0;
            }
            else if (t == 10)
            {
                // one cast of the Spark Bolt wand at a target 20 tiles ahead
                var def = Enemies.All.First(e => e.Id == "zombie_weak");
                int who = Carriers.Spawn(def, (int)p.Center.X + 20 * 16, (int)p.Bottom.Y);
                _target = who >= 0 ? Main.npc[who] : null;
                if (_target != null)
                    _target.lifeMax = _target.life = 100000;
                SpellRecorder.Target = _target;
                SpellRecorder.On = true;
                SpellRecorder.Begin(p.Center);
                Casting.TestAim = _target?.Center ?? p.Center + new Vector2(320, 0);
                Casting.Changed(_wand);
                Casting.TestReady(_wand);
                _casts0 = Casting.TestCasts;
                Casting.TestFire = true;
            }
            else if (t > 10 && t < 70 && Casting.TestCasts > _casts0)
                Casting.TestFire = false;   // one cast
            else if (t == 70)
            {
                Casting.TestFire = false;
                Casting.TestAim = null;
                var files = SpellRecorder.ShotFiles.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).GroupBy(f => f).OrderBy(g => g.Key)
                    .Select(g => g.Key + (g.Count() > 1 ? "x" + g.Count() : ""));
                _now["cast"] = string.Join(",", files);
                _now["cast damage"] = F(Math.Round(SpellRecorder.HitDamage, 1));
                SpellRecorder.On = false;
                SpellRecorder.Target = null;
                if (_target != null)
                    _target.active = false;
                SpellShots.Clear();
                // a creature killed next to the character
                _items0 = Main.item.Count(i => i.active);
                _npcs0 = Main.npc.Count(n => n.active);
                var def = Enemies.All.First(e => e.Id == "zombie_weak");
                int k = Carriers.Spawn(def, (int)p.Center.X + 4 * 16, (int)p.Bottom.Y);
                if (k >= 0)
                {
                    Main.npc[k].playerInteraction[p.whoAmI] = true;
                    Main.npc[k].life = 0;
                    Main.npc[k].checkDead();
                }
            }
            else if (t == 110)
            {
                _now["after a kill"] = "items " + (Main.item.Count(i => i.active) - _items0) + ", creatures " + (Main.npc.Count(n => n.active) - _npcs0) +
                                       ", shots " + SpellShots.LiveCount + ", mana " + p.statMana;
                _now["life after"] = p.statLife.ToString();
            }
            else if (t >= Per)
            {
                Judge(id, times);
                Next(p);
            }
        }

        static void Judge(string id, int times)
        {
            if (id == "")
            {
                _base = _now;
                Log("baseline: " + string.Join("; ", _base.Select(kv => kv.Key + " " + kv.Value)));
                return;
            }
            string name = id + (times > 1 ? " x" + times : "");
            var diffs = _now.Where(kv => !_base.TryGetValue(kv.Key, out var b) || b != kv.Value)
                            .Select(kv => kv.Key + " " + (_base.TryGetValue(kv.Key, out var b) ? b : "") + " -> " + kv.Value).ToList();
            int storeErrors = (SpellShots.ScriptStore?.Errors.Count ?? 0) - _storeErrors0;
            string verdict;
            if (Perks.FuncErrors.Contains(id) || storeErrors > 0)
            {
                verdict = "ERROR";
                _errors++;
                Errors.Add(name + (storeErrors > 0 ? " (" + SpellShots.ScriptStore.Errors.Last().Split('\n')[0] + ")" : ""));
            }
            else if (diffs.Count == 0)
            {
                verdict = "NO EFFECT";
                _none++;
                NoEffect.Add(name);
            }
            else
            {
                verdict = "WORKS";
                _works++;
            }
            Log(name + ": " + verdict + (diffs.Count > 0 ? ": " + string.Join("; ", diffs) : ""));
        }
    }
}
