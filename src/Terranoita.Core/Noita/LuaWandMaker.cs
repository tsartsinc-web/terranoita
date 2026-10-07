using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MoonSharp.Interpreter;

namespace Terranoita.Noita
{
    /// <summary>A wand as Noita's procedural scripts made it.</summary>
    public sealed class MadeWand
    {
        public string Name = "", Sprite = "";
        public int SpellsPerCast = 1, Capacity = 1;
        public bool Shuffle;
        public float CastDelay, RechargeTime, ManaMax, ManaChargeSpeed, Spread, SpeedMultiplier = 1;
        public List<string> Spells = new List<string>();
        public List<string> AlwaysCast = new List<string>();
        public List<string> Missing = new List<string>();     // engine calls the script made that we do not have
        public Dictionary<string, string> Raw = new Dictionary<string, string>();   // AbilityComponent values as set
    }

    /// <summary>
    /// Runs Noita's own wand scripts from the player's Noita (data/scripts/gun/procedural/: starting_wand.lua,
    /// wand_level_01.lua... which call gun_procedural.lua) on a stand-in wand entity, and reads back what they set:
    /// the AbilityComponent values, gun_config / gunaction_config, the sprite and the spells added (AddGunAction,
    /// AddGunActionPermanent). The engine's random spell picks (GetRandomActionWithType, GetRandomAction) use the
    /// spawn_level / spawn_probability of gun_actions.lua, as Noita does.
    /// </summary>
    public sealed class LuaWandMaker
    {
        readonly Func<string, string> _read;
        readonly int _seed;
        Random _rng;
        List<(string id, int type, int[] levels, float[] probs)> _actions;

        public LuaWandMaker(Func<string, string> readText, int seed = 0)
        {
            _read = readText;
            _seed = seed;
            _rng = new Random(seed);
        }

        /// <summary>Every spell with its type and where it spawns (gun_actions.lua), read once.</summary>
        public List<(string id, int type, int[] levels, float[] probs)> Actions()
        {
            if (_actions != null)
                return _actions;
            using (LuaCulture.Enter())
                return ReadActions();
        }

        List<(string id, int type, int[] levels, float[] probs)> ReadActions()
        {
            var lua = new Script(CoreModules.Preset_SoftSandbox);
            var once = new HashSet<string>();
            lua.Globals["dofile_once"] = (Func<string, DynValue>)(p => once.Add(p) ? lua.DoString(_read(p) ?? "", null, p) : DynValue.Nil);
            lua.Globals["dofile"] = (Func<string, DynValue>)(p => lua.DoString(_read(p) ?? "", null, p));
            lua.DoString(LuaCulture.Prelude);
            lua.DoString(_read("data/scripts/gun/gun_enums.lua") ?? "");
            lua.Globals.MetaTable = Stubs(lua, null);
            lua.DoString(_read("data/scripts/gun/gun_actions.lua") ?? "actions = {}");
            _actions = new List<(string, int, int[], float[])>();
            foreach (var v in lua.Globals.Get("actions").Table.Values)
            {
                var t = v.Table;
                if (t == null || t.Get("id").Type != DataType.String)
                    continue;
                int[] levels = Numbers(t.Get("spawn_level").CastToString()).Select(x => (int)x).ToArray();
                float[] probs = Numbers(t.Get("spawn_probability").CastToString()).ToArray();
                _actions.Add((t.Get("id").String, (int)t.Get("type").CastToNumber().GetValueOrDefault(0), levels, probs));
            }
            return _actions;
        }

        static IEnumerable<float> Numbers(string csv) =>
            (csv ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Select(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f);

        /// <summary>A random spell of this level (and type, -1 = any), weighted as Noita's spawn tables say.</summary>
        public string RandomAction(int level, int type)
        {
            var pool = new List<(string id, float w)>();
            foreach (var a in Actions())
            {
                if (type >= 0 && a.type != type)
                    continue;
                int i = Array.IndexOf(a.levels, level);
                if (i >= 0 && i < a.probs.Length && a.probs[i] > 0)
                    pool.Add((a.id, a.probs[i]));
            }
            if (pool.Count == 0)
                return "";
            double r = _rng.NextDouble() * pool.Sum(p => p.w);
            foreach (var p in pool)
                if ((r -= p.w) <= 0)
                    return p.id;
            return pool[pool.Count - 1].id;
        }

        // ---- the stand-in entities: 1 = the wand; action cards get numbers from 1000 ----
        const int WandEntity = 1;
        readonly Dictionary<(int entity, string comp), Dictionary<string, string>> _values = new Dictionary<(int, string), Dictionary<string, string>>();
        readonly List<(int entity, string comp)> _comps = new List<(int, string)>();
        readonly Dictionary<int, string> _cards = new Dictionary<int, string>();
        readonly HashSet<int> _permanent = new HashSet<int>();
        readonly List<int> _children = new List<int>();
        MadeWand _made;

        int Comp(int entity, string name)
        {
            var key = (entity, name);
            int i = _comps.IndexOf(key);
            if (i < 0)
            {
                _comps.Add(key);
                _values[key] = new Dictionary<string, string>(StringComparer.Ordinal);
                i = _comps.Count - 1;
            }
            return i + 1;
        }

        Dictionary<string, string> Values(int comp) => comp >= 1 && comp <= _comps.Count ? _values[_comps[comp - 1]] : null;

        /// <summary>Run a wand script (e.g. data/scripts/gun/procedural/wand_level_03.lua) at a world spot.</summary>
        public MadeWand Make(string script, float x, float y)
        {
            using (LuaCulture.Enter())
                return MakeInner(script, x, y);
        }

        MadeWand MakeInner(string script, float x, float y)
        {
            _values.Clear(); _comps.Clear(); _cards.Clear(); _permanent.Clear(); _children.Clear();
            _made = new MadeWand();
            Actions();
            var lua = new Script(CoreModules.Preset_SoftSandbox);
            var g = lua.Globals;
            var loaded = new HashSet<string>();
            g["dofile"] = (Func<string, DynValue>)(p => lua.DoString(_read(p) ?? "", null, p));
            g["dofile_once"] = (Func<string, DynValue>)(p => loaded.Add(p) ? lua.DoString(_read(p) ?? "", null, p) : DynValue.Nil);
            g["print"] = (Action<DynValue>)(_ => { });
            g["GetUpdatedEntityID"] = (Func<double>)(() => WandEntity);
            g["EntityGetTransform"] = DynValue.NewCallback((c, a) => DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y)));
            g["SetRandomSeed"] = DynValue.NewCallback((c, a) =>
            {
                double s1 = a.Count > 0 ? a[0].CastToNumber() ?? 0 : 0, s2 = a.Count > 1 ? a[1].CastToNumber() ?? 0 : 0;
                _rng = new Random(unchecked(_seed * 7919 + (int)(s1 * 31) + (int)(s2 * 131071)));
                return DynValue.Nil;
            });
            g["Random"] = DynValue.NewCallback((c, a) =>
            {
                if (a.Count >= 2)
                {
                    int lo = (int)a[0].Number, hi = (int)a[1].Number;
                    return DynValue.NewNumber(hi < lo ? lo : _rng.Next(lo, hi + 1));
                }
                if (a.Count == 1)
                    return DynValue.NewNumber(_rng.Next(0, (int)a[0].Number + 1));
                return DynValue.NewNumber(_rng.NextDouble());
            });
            g["Randomf"] = DynValue.NewCallback((c, a) =>
                DynValue.NewNumber(a.Count >= 2 ? a[0].Number + _rng.NextDouble() * (a[1].Number - a[0].Number) : _rng.NextDouble()));
            g["RandomDistribution"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Round(Distribution(a))));
            g["RandomDistributionf"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(Distribution(a)));
            g["EntityGetFirstComponent"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(Comp((int)a[0].Number, a[1].CastToString())));
            g["EntityGetFirstComponentIncludingDisabled"] = g.Get("EntityGetFirstComponent");
            g["ComponentSetValue"] = DynValue.NewCallback((c, a) => Set(a, false));
            g["ComponentSetValue2"] = DynValue.NewCallback((c, a) => Set(a, false));
            g["ComponentObjectSetValue"] = DynValue.NewCallback((c, a) => Set(a, true));
            g["ComponentObjectSetValue2"] = DynValue.NewCallback((c, a) => Set(a, true));
            g["ComponentGetValue"] = DynValue.NewCallback((c, a) => Get(a, false));
            g["ComponentGetValue2"] = DynValue.NewCallback((c, a) => Get(a, false));
            g["ComponentObjectGetValue"] = DynValue.NewCallback((c, a) => Get(a, true));
            g["ComponentObjectGetValue2"] = DynValue.NewCallback((c, a) => Get(a, true));
            g["ComponentSetValueVector2"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["CreateItemActionEntity"] = DynValue.NewCallback((c, a) =>
            {
                int e = 1000 + _cards.Count;
                _cards[e] = a[0].CastToString();
                return DynValue.NewNumber(e);
            });
            g["EntityAddChild"] = DynValue.NewCallback((c, a) =>
            {
                if ((int)a[0].Number == WandEntity)
                    _children.Add((int)a[1].Number);
                return DynValue.Nil;
            });
            g["EntitySetComponentsWithTagEnabled"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["GetRandomActionWithType"] = DynValue.NewCallback((c, a) =>
                DynValue.NewString(RandomAction((int)a[2].Number, (int)a[3].Number)));
            g["GetRandomAction"] = DynValue.NewCallback((c, a) => DynValue.NewString(RandomAction((int)a[2].Number, -1)));
            g["GlobalsGetValue"] = DynValue.NewCallback((c, a) => a.Count > 1 ? a[1] : DynValue.NewString(""));
            g["GlobalsSetValue"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["StatsGlobalGetValue"] = DynValue.NewCallback((c, a) => DynValue.NewString("0"));
            g["HasFlagPersistent"] = DynValue.NewCallback((c, a) => DynValue.True);
            g["GameGetFrameNum"] = (Func<double>)(() => 0);
            g.MetaTable = Stubs(lua, _made.Missing);
            lua.DoString(LuaCulture.Prelude);
            lua.DoString(_read(script) ?? throw new InvalidOperationException("Noita file missing: " + script), null, script);
            return Read();
        }

        double Distribution(CallbackArguments a)
        {
            // Noita's RandomDistribution(min, max, mean, sharpness): values cluster around mean, sharper = closer
            double min = a[0].Number, max = a[1].Number;
            double mean = a.Count > 2 ? a[2].Number : (min + max) / 2;
            int sharp = a.Count > 3 ? Math.Max(1, (int)a[3].Number) : 1;
            double sum = 0;
            for (int i = 0; i < sharp; i++)
                sum += _rng.NextDouble() * 2 - 1;
            double v = mean + sum / sharp * Math.Max(mean - min, max - mean);
            return Math.Max(min, Math.Min(max, v));
        }

        DynValue Set(CallbackArguments a, bool obj)
        {
            var vals = Values((int)a[0].Number);
            if (vals == null)
                return DynValue.Nil;
            string field = obj ? a[1].CastToString() + "." + a[2].CastToString() : a[1].CastToString();
            var v = a[obj ? 3 : 2];
            vals[field] = v.Type == DataType.Boolean ? (v.Boolean ? "1" : "0") :
                          v.Type == DataType.Number ? v.Number.ToString("R", CultureInfo.InvariantCulture) : v.CastToString() ?? "";
            int comp = (int)a[0].Number;
            var key = _comps[comp - 1];
            if (_cards.ContainsKey(key.entity) && field == "permanently_attached" && vals[field] == "1")
                _permanent.Add(key.entity);
            return DynValue.Nil;
        }

        DynValue Get(CallbackArguments a, bool obj)
        {
            var vals = Values((int)a[0].Number);
            string field = obj ? a[1].CastToString() + "." + a[2].CastToString() : a[1].CastToString();
            return vals != null && vals.TryGetValue(field, out var s) ? DynValue.NewString(s) : DynValue.Nil;
        }

        static Table Stubs(Script lua, List<string> missing)
        {
            var meta = new Table(lua);
            meta["__index"] = DynValue.NewCallback((c, a) =>
            {
                string name = a[1].Type == DataType.String ? a[1].String : null;
                if (name == null || name.Length == 0 || !char.IsUpper(name[0]))
                    return DynValue.Nil;
                return DynValue.NewCallback((c2, a2) =>
                {
                    if (missing != null && !missing.Contains(name))
                        missing.Add(name);
                    return DynValue.Nil;
                });
            });
            return meta;
        }

        MadeWand Read()
        {
            var ab = _values.TryGetValue((WandEntity, "AbilityComponent"), out var v) ? v : new Dictionary<string, string>();
            string S(string k) => ab.TryGetValue(k, out var s) ? s : null;
            float F(string k, float d) => float.TryParse(S(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;
            var w = _made;
            w.Raw = new Dictionary<string, string>(ab);
            w.Name = S("ui_name") ?? "";
            w.Sprite = S("sprite_file") ?? "";
            w.SpellsPerCast = (int)F("gun_config.actions_per_round", 1);
            w.RechargeTime = F("gun_config.reload_time", 40);
            w.Capacity = (int)F("gun_config.deck_capacity", 1);
            string sh = S("gun_config.shuffle_deck_when_empty");
            w.Shuffle = sh == "1" || sh == "true";
            w.CastDelay = F("gunaction_config.fire_rate_wait", 0);
            w.Spread = F("gunaction_config.spread_degrees", 0);
            w.SpeedMultiplier = F("gunaction_config.speed_multiplier", 1);
            w.ManaMax = F("mana_max", 0);
            w.ManaChargeSpeed = F("mana_charge_speed", 0);
            foreach (int e in _children)
                if (_cards.TryGetValue(e, out var id) && id.Length > 0)
                    (_permanent.Contains(e) ? w.AlwaysCast : w.Spells).Add(id);
            w.Capacity -= w.AlwaysCast.Count;   // AddGunActionPermanent adds a slot for the always-cast card
            return w;
        }
    }
}
