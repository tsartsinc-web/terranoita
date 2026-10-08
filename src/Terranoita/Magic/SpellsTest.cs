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
    /// TERRANOITA_AUTOTEST_SPELLS=1 (game_test -Mode spells): every spell of the player's Noita alone in a test wand
    /// (modifiers and the like followed by a spark bolt), cast at a creature 6 tiles away for 0.75 s. What the sheets
    /// promise is checked by the test itself: a spell with a projectile fires, one whose projectile does damage hurts
    /// the target, one with limited uses spends them; no error. One row per spell in test_rows.txt (game_test turns
    /// it into a summary and a diff against design/sources/magic_baseline.txt). TERRANOITA_SPELLS_ONLY=A,B: just these.
    /// </summary>
    public static class SpellsTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_SPELLS") == "1";
        const int Each = 45;
        static List<string> _ids;
        static Vector2 _start;
        static NPC _target;
        static int _casts, _shots, _mana, _errors, _life;
        static WandData _wand;
        public static bool Done { get; private set; }
        public static string RowsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "test_rows.txt");

        static Dictionary<string, SpellProjectileDef> _projectiles;

        /// <summary>What the sheets say the spell must do: fire, hurt, spend uses.</summary>
        static (bool fires, bool hurts, bool uses) Expect(string id)
        {
            if (_projectiles == null)
                _projectiles = SpellProjectiles.All.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var s = MagicItems.Spell(id);
            if (s == null)
                return (false, false, false);
            var files = (s.Projectiles ?? new string[0]).Concat(s.TriggerFile != null && s.TriggerFile != "none" ? new[] { s.TriggerFile } : new string[0]).ToList();
            bool hurts = files.Any(f => _projectiles.TryGetValue(f, out var d) && d.Damage + d.TypedDamage + d.ExplosionDamage > 0);
            return (files.Count > 0, hurts, s.MaxUses > 0);
        }

        public static void Frame(Player p, int frame)
        {
            p.statManaMax = p.statManaMax2 = 1000;
            p.statMana = p.statManaMax2;
            if (frame == 60)
            {
                _start = p.position;
                var only = Environment.GetEnvironmentVariable("TERRANOITA_SPELLS_ONLY");
                _ids = new LuaWandMaker(NoitaArt.ReadText, 1).Actions().Select(a => a.id)
                    .Where(id => string.IsNullOrEmpty(only) || only.Split(',').Contains(id)).ToList();
                try { File.WriteAllText(RowsFile, ""); } catch { }
                Entry.Log("SPELLS " + _ids.Count + " spells");
            }
            if (frame < 120 || _ids == null || Done)
                return;
            int k = (frame - 120) / Each, t = (frame - 120) % Each;
            if (k >= _ids.Count)
            {
                Casting.TestFire = false;
                Casting.TestAim = null;
                Entry.Log("SPELLS done");
                Done = true;
                return;
            }
            string id = _ids[k];
            if (t == 0)
            {
                SpellShots.Clear();
                p.position = _start;
                p.velocity = Vector2.Zero;
                _target?.StrikeNPCNoInteraction(99999, 0, 0);
                var def = Enemies.All.First(e => e.Id == "zombie_weak");
                int who = Carriers.Spawn(def, (int)p.Center.X + 6 * 16, (int)(p.position.Y + p.height));
                _target = who >= 0 ? Main.npc[who] : null;
                if (_target != null)
                {
                    _target.lifeMax = _target.life = 100000;   // survives the spell: the damage is what is read
                    _life = _target.life;
                }
                var type = MagicItems.Spell(id)?.Type ?? "";
                _wand = WandStore.NewWand();
                _wand.Name = "spell test"; _wand.Sprite = "data/items_gfx/handgun.xml"; _wand.CastDelay = 10; _wand.RechargeTime = 20; _wand.SpellsPerCast = 1;
                _wand.Slots = type == "projectile" || type == "static_projectile" || type == "material" ? new[] { id } : new[] { id, "LIGHT_BULLET" };
                _wand.Uses = _wand.Slots.Select(s => MagicItems.Spell(s)?.MaxUses ?? -1).ToArray();
                p.inventory[1] = MagicItems.MakeWand(_wand);
                p.selectedItemState.Select(1);
                _casts = Casting.TestCasts;
                _shots = Casting.TestShots;
                _mana = Casting.TestMana;
                _errors = Entry.Errors;
                Casting.TestFire = true;
            }
            if (_target != null && _target.active)
                Casting.TestAim = _target.Center;
            if (t != Each - 1)
                return;
            int casts = Casting.TestCasts - _casts, shots = Casting.TestShots - _shots, mana = Casting.TestMana - _mana, errors = Entry.Errors - _errors;
            int hurt = _target == null ? 0 : _target.active ? _life - _target.life : _life;
            bool usesSpent = _wand.Uses.Length > 0 && _wand.Uses[0] >= 0 && _wand.Uses[0] < (MagicItems.Spell(id)?.MaxUses ?? 0);
            var (fires, hurts, uses) = Expect(id);
            string status = errors > 0 ? "error" : casts == 0 ? "no cast" : fires && shots == 0 ? "no shot" :
                            hurts && hurt <= 0 ? "no damage" : uses && !usesSpent ? "uses kept" : "OK";
            string row = id + " " + status + " shots " + shots + " mana " + mana + " hurt " + hurt;
            try { File.AppendAllText(RowsFile, row + Environment.NewLine); } catch { }
            if (status != "OK")
                Entry.Log("SPELLS " + row);
        }
    }
}
