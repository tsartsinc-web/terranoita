using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_WANDS=1 (game_test -Mode wands, author: "take each wand, fire, next"): every wand Noita's
    /// wand files make goes into the hand and fires for 1.5 s; the log gets its spells, casts, shots and mana spent.
    /// Errors of a wand show as ERROR lines right after its WANDS line.
    /// </summary>
    public static class WandsTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_WANDS") == "1";
        const int Each = 90;
        static List<(string file, WandData wand)> _wands;
        static Vector2 _start;
        static int _casts, _shots, _mana, _errors, _skipped;
        static bool _recharged;
        static readonly bool All = Environment.GetEnvironmentVariable("TERRANOITA_WANDS_ALL") == "1";
        // wand files that already passed (cast, shots, no error): not tested again (author); TERRANOITA_WANDS_ALL=1 tests all
        static string PassedFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "wands_passed.txt");
        public static bool Done { get; private set; }

        static void Log(string s) => Entry.Log("WANDS " + s);

        public static void Frame(Player p, int frame)
        {
            p.statManaMax = p.statManaMax2 = 1000;
            if (frame == 60)
            {
                _start = p.position;
                var maker = new LuaWandMaker(NoitaArt.ReadText, 12345);
                _wands = new List<(string, WandData)>();
                var passed = new HashSet<string>();
                try { if (!All && System.IO.File.Exists(PassedFile)) passed.UnionWith(System.IO.File.ReadAllLines(PassedFile)); } catch { }
                foreach (var f in Sandbox.WandFiles())
                {
                    if (passed.Contains(f))
                    {
                        _skipped++;
                        continue;
                    }
                    try { _wands.Add((f, WandWindow.Store(maker.MakeEntity(f, p.Center.X / 3, p.Center.Y / 3)))); }
                    catch (Exception ex) { Log("wand " + f + " not made: " + ex.Message); }
                }
                Log(_wands.Count + " wands made, " + _skipped + " passed before and skipped");
            }
            if (frame < 120 || _wands == null || Done)
                return;
            int k = (frame - 120) / Each, t = (frame - 120) % Each;
            if (k >= _wands.Count)
            {
                Casting.TestFire = false;
                Log("done");
                Done = true;
                return;
            }
            var (file, w) = _wands[k];
            if (t == 0)
            {
                SpellShots.Clear();
                p.position = _start;
                p.velocity = Vector2.Zero;
                p.statMana = p.statManaMax2;
                p.inventory[1] = MagicItems.MakeWand(w);
                p.selectedItemState.Select(1);
                _casts = Casting.TestCasts;
                _shots = Casting.TestShots;
                _mana = Casting.TestMana;
                _errors = Entry.Errors;
                _recharged = false;
                if (k == 0)
                    try { System.IO.File.WriteAllText(SpellsTest.RowsFile, ""); } catch { }
                Casting.TestFire = true;
            }
            p.statMana = p.statManaMax2;   // never runs dry
            _recharged |= Casting.Recharging(w.Id) >= 0;
            if (t == Each - 1)
            {
                int casts = Casting.TestCasts - _casts, shots = Casting.TestShots - _shots, errors = Entry.Errors - _errors;
                int mana = Casting.TestMana - _mana;
                // Noita's promise for a wand: it casts, shoots, spends mana, recharges; no error
                string status = errors > 0 ? "error" : casts == 0 ? "no cast" : shots == 0 ? "no shot" : mana <= 0 ? "no mana" : !_recharged ? "no recharge" : "OK";
                try { System.IO.File.AppendAllText(SpellsTest.RowsFile, "wand:" + System.IO.Path.GetFileNameWithoutExtension(file) + " " + status + " shots " + shots + " mana " + mana + Environment.NewLine); } catch { }
                if (status != "OK")
                    Screenshot.Request("fail_" + System.IO.Path.GetFileNameWithoutExtension(file));
                if (status == "OK")
                    try { System.IO.File.AppendAllText(PassedFile, file + Environment.NewLine); } catch { }
                Log((casts > 0 && errors == 0 ? "ok " : "FAIL ") + (k + 1) + "/" + _wands.Count + " " + System.IO.Path.GetFileNameWithoutExtension(file) + " '" + MagicItems.WandName(w) + "' [" +
                    string.Join(" ", w.Slots.Select(s => s ?? "-")) + (w.AlwaysCast.Count > 0 ? " | always " + string.Join(" ", w.AlwaysCast) : "") +
                    "]: casts " + (Casting.TestCasts - _casts) + ", shots " + (Casting.TestShots - _shots) + ", mana " + (Casting.TestMana - _mana) +
                    ", live " + SpellShots.Ids().Count + (errors > 0 ? ", errors " + errors : ""));
            }
        }
    }
}
