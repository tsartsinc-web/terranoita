using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace Terranoita.Noita
{
    /// <summary>One of Noita's perks as data/scripts/perks/perk_list.lua declares it (design/perks.md, PC-34).</summary>
    public sealed class NoitaPerk
    {
        public string Id, UiName, UiDescription, UiIcon, PerkIcon;
        public bool Stackable, StackableIsRare, OneOff, NotInDefaultPool, UsableByEnemies, RemoveOtherPerks, HasFunc;
        /// <summary>stackable_maximum and max_in_perk_pool (0 = not set).</summary>
        public int StackableMaximum, MaxInPool;
        /// <summary>game_effect and game_effect2 (GameEffectComponent effects the perk gives).</summary>
        public readonly List<string> GameEffects = new List<string>();
    }

    /// <summary>Noita's perk list, read by running the author's perk_list.lua (stage-3 rule: Noita's own data).</summary>
    public static class NoitaPerks
    {
        public const string ListFile = "data/scripts/perks/perk_list.lua";

        public static List<NoitaPerk> Read(Func<string, string> read)
        {
            using (LuaCulture.Enter())
            {
                var lua = new Script(CoreModules.Preset_SoftSandbox);
                var once = new HashSet<string>();
                lua.Globals["dofile_once"] = (Func<string, DynValue>)(p => once.Add(p) ? lua.DoString(read(p) ?? "", null, p) : DynValue.Nil);
                lua.Globals["dofile"] = (Func<string, DynValue>)(p => lua.DoString(read(p) ?? "", null, p));
                lua.DoString(LuaCulture.Prelude);
                // engine functions the list's helpers call at load time do nothing here: only the table is read
                var meta = new Table(lua);
                meta["__index"] = DynValue.NewCallback((c, a) =>
                    a[1].Type == DataType.String && a[1].String.Length > 0 && char.IsUpper(a[1].String[0])
                        ? DynValue.NewCallback((c2, a2) => DynValue.Nil) : DynValue.Nil);
                lua.Globals.MetaTable = meta;
                string text = read(ListFile);
                if (text == null)
                    throw new InvalidOperationException(ListFile + " not found in Noita's data");
                lua.DoString(text, null, ListFile);
                var list = lua.Globals.Get("perk_list").Table;
                if (list == null)
                    throw new InvalidOperationException(ListFile + " has no perk_list table");
                var perks = new List<NoitaPerk>();
                for (int i = 1; i <= list.Length; i++)
                {
                    var t = list.Get(i).Table;
                    if (t == null || t.Get("id").Type != DataType.String)
                        continue;
                    var p = new NoitaPerk
                    {
                        Id = t.Get("id").String,
                        UiName = Str(t, "ui_name"),
                        UiDescription = Str(t, "ui_description"),
                        UiIcon = Str(t, "ui_icon"),
                        PerkIcon = Str(t, "perk_icon"),
                        Stackable = t.Get("stackable").CastToBool(),
                        StackableIsRare = t.Get("stackable_is_rare").CastToBool(),
                        OneOff = t.Get("one_off_effect").CastToBool(),
                        NotInDefaultPool = t.Get("not_in_default_perk_pool").CastToBool(),
                        UsableByEnemies = t.Get("usable_by_enemies").CastToBool(),
                        RemoveOtherPerks = t.Get("remove_other_perks").CastToBool(),
                        HasFunc = t.Get("func").Type == DataType.Function,
                        StackableMaximum = (int)(t.Get("stackable_maximum").CastToNumber() ?? 0),
                        MaxInPool = (int)(t.Get("max_in_perk_pool").CastToNumber() ?? 0),
                    };
                    foreach (var k in new[] { "game_effect", "game_effect2" })
                        if (t.Get(k).Type == DataType.String && t.Get(k).String.Length > 0)
                            p.GameEffects.Add(t.Get(k).String);
                    perks.Add(p);
                }
                return perks;
            }
        }

        static string Str(Table t, string key) => t.Get(key).Type == DataType.String ? t.Get(key).String : "";
    }
}
