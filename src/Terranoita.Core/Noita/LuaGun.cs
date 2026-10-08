using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace Terranoita.Noita
{
    /// <summary>One projectile a cast released, with the shot config Noita gave it and, for triggers, its payload.</summary>
    public sealed class LuaShot
    {
        public string File;
        public Dictionary<string, DynValue> Config;          // ConfigGunActionInfo of the shot it belongs to
        public string Trigger;                               // null, "timer", "hit_world", "death"
        public int TriggerFrames;
        public List<LuaShot> Payload = new List<LuaShot>();
        public float Get(string key) => Config != null && Config.TryGetValue(key, out var v) && v.Type == DataType.Number ? (float)v.Number : 0f;
        public string Text(string key) => Config != null && Config.TryGetValue(key, out var v) && v.Type == DataType.String ? v.String : "";
    }

    /// <summary>What one cast did, as Noita's engine would have seen it.</summary>
    public sealed class LuaCast
    {
        public readonly List<LuaShot> Shots = new List<LuaShot>();
        public readonly List<string> Played = new List<string>();
        public float CastDelay;                              // frames: fire_rate_wait of every shot of the cast, added up
        public float Recharge = -1;                          // frames of StartReload, -1 = no recharge
        public float Mana;                                   // mana left
        public float Recoil;
        public readonly List<string> Missing = new List<string>();   // engine functions the scripts called that we do not have
    }

    /// <summary>A wand as Noita's engine hands it to gun.lua.</summary>
    public sealed class LuaWand
    {
        public int SpellsPerCast = 1;
        public bool Shuffle;
        public float RechargeTime = 40, CastDelay;
        public int Capacity = 2;
        public float Spread, SpeedMultiplier = 1;
        public List<string> AlwaysCast = new List<string>();
        public List<(string id, int uses)> Spells = new List<(string, int)>();
    }

    /// <summary>
    /// Noita's own spell code run as is: data/scripts/gun/gun.lua (with gun_actions.lua and the generated config files)
    /// from the player's Noita, in MoonSharp. This class plays Noita's engine: it sets the wand up the way the engine
    /// does (ConfigGun_ReadToLua, _set_gun, _clear_deck, _add_card_to_deck), shoots (_start_shot, _play_permanent_card,
    /// _draw_actions_for_shot) and records what the scripts ask of the engine (BeginProjectile, triggers,
    /// RegisterGunAction, StartReload...). One instance per wand: the deck lives in the Lua state.
    /// </summary>
    public sealed class LuaGun
    {
        readonly Script _lua;
        readonly Func<string, string> _read;
        readonly HashSet<string> _loaded = new HashSet<string>(StringComparer.Ordinal);
        readonly string[] _actionFields;
        readonly Random _rng;
        int _frame;

        // what the current cast builds
        LuaCast _cast;
        readonly Stack<List<LuaShot>> _scopes = new Stack<List<LuaShot>>();
        readonly Stack<LuaShot> _open = new Stack<LuaShot>();
        Dictionary<string, DynValue> _lastConfig;

        public int Frame { get => _frame; set => _frame = value; }

        public LuaWorld World;
        /// <summary>A limited spell was used: (card number from 1 in Load order, uses left).</summary>
        public Action<int, int> UsesChanged;

        /// <param name="readText">Reads a file of the player's Noita (data/scripts/...), null if missing.</param>
        /// <param name="seed">a fixed seed makes random spells repeat (golden files, tests)</param>
        public LuaGun(Func<string, string> readText, LuaWorld world = null, int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _read = readText;
            World = world ?? new LuaWorld();
            _lua = new Script(CoreModules.Preset_SoftSandbox);
            var g = _lua.Globals;
            g["dofile"] = (Func<string, DynValue>)(p => DoFile(p, false));
            g["dofile_once"] = (Func<string, DynValue>)(p => DoFile(p, true));
            g["print"] = (Action<DynValue>)(_ => { });
            // engine functions gun.lua and the actions call
            g["BeginProjectile"] = (Action<string>)BeginProjectile;
            g["EndProjectile"] = (Action)EndProjectile;
            g["BeginTriggerTimer"] = (Action<double>)(f => BeginTrigger("timer", (int)f));
            g["BeginTriggerHitWorld"] = (Action)(() => BeginTrigger("hit_world", 0));
            g["BeginTriggerDeath"] = (Action)(() => BeginTrigger("death", 0));
            g["EndTrigger"] = (Action)EndTrigger;
            g["RegisterGunAction"] = DynValue.NewCallback((c, a) => { RegisterGunAction(a); return DynValue.Nil; });
            g["RegisterGunShotEffects"] = (Action<double>)(r => { if (_cast != null) _cast.Recoil += (float)r; });
            g["RegisterGun"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["SetProjectileConfigs"] = (Action)SetProjectileConfigs;
            g["StartReload"] = (Action<double>)(t => { if (_cast != null) _cast.Recharge = (float)t; });
            g["OnActionPlayed"] = (Action<string>)(id => _cast?.Played.Add(id));
            g["OnNotEnoughManaForAction"] = (Action)(() => { });
            g["ActionUsed"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["ActionUsesRemainingChanged"] = DynValue.NewCallback((c, a) =>
            {
                UsesChanged?.Invoke((int)a[0].Number, (int)a[1].Number);
                return DynValue.True;
            });
            g["LogAction"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["Reflection_RegisterProjectile"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["BaabInstruction"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["GameGetFrameNum"] = (Func<double>)(() => _frame);
            g["SetRandomSeed"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["Random"] = DynValue.NewCallback((c, a) => LuaRandom(a));
            g["Randomf"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(a.Count >= 2 ? a[0].Number + _rng.NextDouble() * (a[1].Number - a[0].Number) : _rng.NextDouble()));
            // the world the scripts look at (LuaWorld): entities are numbers, components are entity * 16 + kind
            g["GetUpdatedEntityID"] = (Func<double>)(() => LuaWorld.Caster);
            g["EntityGetWithTag"] = DynValue.NewCallback((c, a) => Ids(World.WithTag(a[0].CastToString())));
            g["EntityGetInRadiusWithTag"] = DynValue.NewCallback((c, a) =>
                Ids(World.InRadiusWithTag((float)a[0].Number, (float)a[1].Number, (float)a[2].Number, a[3].CastToString())));
            g["EntityGetTransform"] = DynValue.NewCallback((c, a) =>
            {
                var (x, y) = World.Position((int)a[0].Number);
                return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y), DynValue.NewNumber(0), DynValue.NewNumber(1), DynValue.NewNumber(1));
            });
            g["EntityHasTag"] = DynValue.NewCallback((c, a) => DynValue.NewBoolean(World.HasTag((int)a[0].Number, a[1].CastToString())));
            g["EntityGetName"] = DynValue.NewCallback((c, a) => DynValue.NewString(World.Name((int)a[0].Number)));
            g["EntityGetFirstComponent"] = DynValue.NewCallback((c, a) => Component(a));
            g["EntityGetFirstComponentIncludingDisabled"] = DynValue.NewCallback((c, a) => Component(a));
            g["EntityGetComponent"] = DynValue.NewCallback((c, a) =>
            {
                var comp = Component(a);
                return comp.IsNil() ? DynValue.Nil : Ids(new List<int> { (int)comp.Number });
            });
            g["EntityGetAllChildren"] = DynValue.NewCallback((c, a) =>
            {
                // the caster's children: its inventory; a wand's children: its spells (entities WandEntity + 1...)
                int e = (int)a[0].Number;
                if (e == LuaWorld.Caster)
                    return Ids(new List<int> { InventoryEntity });
                if (e == InventoryEntity)
                    return Ids(new List<int> { WandEntity });
                if (e == WandEntity)
                    return Ids(Enumerable.Range(0, World.WandSpells().Count).Select(i => SpellEntity + i).ToList());
                return DynValue.Nil;
            });
            g["ComponentGetValue2"] = DynValue.NewCallback((c, a) => ComponentGet((int)a[0].Number, a[1].CastToString()));
            g["ComponentGetValue"] = DynValue.NewCallback((c, a) =>
            {
                var v = ComponentGet((int)a[0].Number, a[1].CastToString());   // the old API answers in text
                return v.IsNil() ? DynValue.Nil : DynValue.NewString(v.CastToString());
            });
            g["ComponentSetValue2"] = DynValue.NewCallback((c, a) => { ComponentSet((int)a[0].Number, a[1].CastToString(), a[2]); return DynValue.Nil; });
            g["EntityInflictDamage"] = DynValue.NewCallback((c, a) =>
            {
                World.Damage((int)a[0].Number, (float)a[1].Number, a.Count > 2 ? a[2].CastToString() : "");
                return DynValue.Nil;
            });
            g["EntityLoad"] = DynValue.NewCallback((c, a) =>
                DynValue.NewNumber(World.Load(a[0].CastToString(), a.Count > 1 ? (float)a[1].Number : 0, a.Count > 2 ? (float)a[2].Number : 0)));
            g["HasFlagPersistent"] = DynValue.NewCallback((c, a) => DynValue.NewBoolean(World.Flag(a[0].CastToString())));
            g["GlobalsGetValue"] = DynValue.NewCallback((c, a) =>
                DynValue.NewString(World.Globals.TryGetValue(a[0].CastToString(), out var v) ? v : a.Count > 1 ? a[1].CastToString() : ""));
            g["GlobalsSetValue"] = DynValue.NewCallback((c, a) => { World.Globals[a[0].CastToString()] = a[1].CastToString(); return DynValue.Nil; });
            // anything else the engine would answer: nil, and remembered so it can be added
            var meta = new Table(_lua);
            meta["__index"] = DynValue.NewCallback((c, a) =>
            {
                string name = a[1].Type == DataType.String ? a[1].String : null;
                if (name == null || name.Length == 0 || !char.IsUpper(name[0]))
                    return DynValue.Nil;
                return DynValue.NewCallback((c2, a2) =>
                {
                    if (_cast != null && !_cast.Missing.Contains(name))
                        _cast.Missing.Add(name);
                    return DynValue.Nil;
                });
            });
            g.MetaTable = meta;

            string gunaction = _read("data/scripts/gun/gunaction_generated.lua") ?? throw new InvalidOperationException("no gunaction_generated.lua");
            var m = Regex.Match(gunaction, @"function\s+ConfigGunActionInfo_ReadToLua\s*\(([^)]*)\)");
            _actionFields = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            using (LuaCulture.Enter())
            {
                _lua.DoString(LuaCulture.Prelude);
                DoFile("data/scripts/gun/gun.lua", true);
            }
        }

        DynValue DoFile(string path, bool once)
        {
            if (once && !_loaded.Add(path))
                return DynValue.Nil;
            _loaded.Add(path);
            string code = _read(path) ?? throw new InvalidOperationException("Noita file missing: " + path);
            return _lua.DoString(code, null, path);
        }

        const int InventoryEntity = 2, WandEntity = 3, SpellEntity = 100;
        const int DamageModel = 1, Wallet = 2, Inventory2 = 3, ItemAction = 4;

        DynValue Ids(List<int> ids)
        {
            var t = new Table(_lua);
            foreach (int id in ids)
                t.Append(DynValue.NewNumber(id));
            return DynValue.NewTable(t);
        }

        DynValue Component(CallbackArguments a)
        {
            int e = (int)a[0].Number;
            string name = a[1].CastToString();
            int kind = name == "DamageModelComponent" ? DamageModel : name == "WalletComponent" ? Wallet :
                       name == "Inventory2Component" ? Inventory2 : name == "ItemActionComponent" ? ItemAction : 0;
            return kind == 0 ? DynValue.Nil : DynValue.NewNumber(e * 16 + kind);
        }

        DynValue ComponentGet(int comp, string field)
        {
            int e = comp / 16, kind = comp % 16;
            switch (kind)
            {
                case DamageModel when e == LuaWorld.Caster:
                    return field == "hp" ? DynValue.NewNumber(World.Hp) : field == "max_hp" ? DynValue.NewNumber(World.MaxHp) : DynValue.Nil;
                case Wallet:
                    return field == "money" ? DynValue.NewNumber(World.Money) : field == "money_spent" ? DynValue.NewNumber(World.MoneySpent) : DynValue.Nil;
                case Inventory2:
                    return field == "mActiveItem" ? DynValue.NewNumber(WandEntity) : DynValue.Nil;
                case ItemAction:
                    var spells = World.WandSpells();
                    int i = e - SpellEntity;
                    return field == "action_id" && i >= 0 && i < spells.Count ? DynValue.NewString(spells[i]) : DynValue.Nil;
            }
            return DynValue.Nil;
        }

        void ComponentSet(int comp, string field, DynValue v)
        {
            int e = comp / 16, kind = comp % 16;
            if (v.Type != DataType.Number)
                return;
            if (kind == DamageModel && e == LuaWorld.Caster && field == "hp")
                World.Hp = (float)v.Number;
            else if (kind == Wallet && field == "money")
                World.Money = (float)v.Number;
            else if (kind == Wallet && field == "money_spent")
                World.MoneySpent = (float)v.Number;
        }

        DynValue LuaRandom(CallbackArguments a)
        {
            // Noita: Random() in [0,1), Random(a, b) an integer in [a, b]
            if (a.Count >= 2)
                return DynValue.NewNumber(_rng.Next((int)a[0].Number, (int)a[1].Number + 1));
            if (a.Count == 1)
                return DynValue.NewNumber(_rng.Next(0, (int)a[0].Number + 1));
            return DynValue.NewNumber(_rng.NextDouble());
        }

        // ---- the engine's side of a cast ----

        void BeginProjectile(string file)
        {
            var s = new LuaShot { File = file };
            (_scopes.Count > 0 ? _scopes.Peek() : _cast.Shots).Add(s);
            _open.Push(s);
        }

        void EndProjectile()
        {
            if (_open.Count > 0)
                _open.Pop();
        }

        void BeginTrigger(string kind, int frames)
        {
            var s = _open.Count > 0 ? _open.Peek() : null;
            if (s == null)
                return;
            s.Trigger = kind;
            s.TriggerFrames = frames;
            _scopes.Push(s.Payload);
        }

        void EndTrigger()
        {
            if (_scopes.Count > 0)
                _scopes.Pop();
        }

        void RegisterGunAction(CallbackArguments a)
        {
            var cfg = new Dictionary<string, DynValue>(StringComparer.Ordinal);
            for (int i = 0; i < _actionFields.Length && i < a.Count; i++)
                cfg[_actionFields[i]] = a[i];
            _lastConfig = cfg;
            if (_cast != null && cfg.TryGetValue("fire_rate_wait", out var fw) && fw.Type == DataType.Number)
                _cast.CastDelay += (float)fw.Number;
        }

        void SetProjectileConfigs()
        {
            // the shot just registered: its projectiles are the ones in the current scope without a config yet
            var list = _scopes.Count > 0 ? _scopes.Peek() : _cast?.Shots;
            if (list == null)
                return;
            foreach (var s in list)
                if (s.Config == null)
                    s.Config = _lastConfig;
        }

        // ---- the wand ----

        /// <summary>Set the wand up and build its deck, as the engine does when a wand is made or changed.</summary>
        public void Load(LuaWand w)
        {
            using (LuaCulture.Enter())
                LoadInner(w);
        }

        void LoadInner(LuaWand w)
        {
            Call("ConfigGun_ReadToLua", w.SpellsPerCast, w.Shuffle, w.RechargeTime, w.Capacity);
            Call("_set_gun");
            var state = new Table(_lua);
            Call("ConfigGunActionInfo_Init", state);
            state["fire_rate_wait"] = w.CastDelay;
            state["spread_degrees"] = w.Spread;
            state["speed_multiplier"] = w.SpeedMultiplier;
            state["reload_time"] = w.RechargeTime;
            _lua.Globals["__globaldata"] = state;
            Call("_set_gun2");
            Call("_clear_deck", false);
            int item = 1;
            foreach (var (id, uses) in w.Spells)
                Call("_add_card_to_deck", id, item++, uses, true);
            _always = w.AlwaysCast.ToList();
        }

        List<string> _always = new List<string>();

        /// <summary>One cast with this much mana; the wand's cooldown is max(CastDelay, Recharge) as in Noita.</summary>
        public LuaCast Cast(float mana)
        {
            _cast = new LuaCast();
            _scopes.Clear();
            _open.Clear();
            _lastConfig = null;
            var culture = LuaCulture.Enter();
            try
            {
                Call("_start_shot", mana);
                foreach (var id in _always)
                    Call("_play_permanent_card", id);
                Call("_draw_actions_for_shot", true);
                _cast.Mana = (float)_lua.Globals.Get("mana").CastToNumber().GetValueOrDefault(mana);
                return _cast;
            }
            finally { _cast = null; _frame++; culture.Dispose(); }
        }

        /// <summary>Ids of every spell in the player's gun_actions.lua.</summary>
        public List<string> ActionIds()
        {
            var list = new List<string>();
            var actions = _lua.Globals.Get("actions").Table;
            if (actions != null)
                foreach (var v in actions.Values)
                    if (v.Type == DataType.Table && v.Table.Get("id").Type == DataType.String)
                        list.Add(v.Table.Get("id").String);
            return list;
        }

        DynValue Call(string fn, params object[] args) => _lua.Call(_lua.Globals.Get(fn), args);
    }
}
