using System;
using System.Collections.Generic;
using System.Globalization;
using MoonSharp.Interpreter;

namespace Terranoita.Noita
{
    /// <summary>What a Noita script placed: an entity file, a pixel scene, a spell card, a background sprite.</summary>
    public sealed class Placement
    {
        public string Kind;    // entity | scene | spell | background
        public string File;    // entity xml, scene materials png, spell id, sprite png
        public string Extra;   // scene: its colours png ("" when none)
        public float X, Y;

        public override string ToString() =>
            Kind + " " + File + " " + X.ToString("0.#", CultureInfo.InvariantCulture) + "," + Y.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Noita's own placement scripts run in MoonSharp with a recording host (design/worldgen_plan.md, section 2):
    /// a biome script (data/scripts/biomes/*.lua: its g_* tables, spawn_items, spawn_wands, spawn_potions,
    /// spawn_chest, the pixel scene functions and RegisterSpawnFunction colours) or any script function such as
    /// chest_random.lua's drop_random_reward. Nothing is placed: EntityLoad, LoadPixelScene, CreateItemActionEntity and
    /// LoadBackgroundSprite are recorded as Placements, with the position the script gave. Engine calls we lack return
    /// nothing and are listed in Missing; Lua errors of a call go to Errors and the call returns what it placed so far.
    /// </summary>
    public sealed class NoitaBiomeSpawns
    {
        readonly Func<string, string> _read;
        readonly int _seed;
        readonly LuaWandMaker _actions;
        Random _rng;
        Script _lua;
        int _nextEntity = 1;
        readonly Dictionary<int, (float x, float y)> _entities = new Dictionary<int, (float, float)>();
        List<Placement> _placed = new List<Placement>();

        public readonly List<string> Missing = new List<string>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>RegisterSpawnFunction: wang tile colour (ARGB) -> function name.</summary>
        public readonly Dictionary<uint, string> SpawnFunctions = new Dictionary<uint, string>();

        public NoitaBiomeSpawns(Func<string, string> readText, int seed = 0)
        {
            _read = readText;
            _seed = seed;
            _rng = new Random(seed);
            _actions = new LuaWandMaker(readText, seed);
        }

        /// <summary>Runs a script (a biome's, chest_random.lua...) in a fresh state; its functions can then be called.</summary>
        public void Load(string script)
        {
            using (LuaCulture.Enter())
            {
                _lua = NewEnv();
                _lua.DoString(LuaCulture.Prelude);
                string text = _read(script) ?? throw new InvalidOperationException("Noita file missing: " + script);
                try { _lua.DoString(text, null, script); }
                catch (InterpreterException ex) { Errors.Add(script + ": " + ex.DecoratedMessage); }
            }
        }

        public bool Has(string function) => _lua != null && _lua.Globals.Get(function).Type == DataType.Function;

        /// <summary>Calls a loaded function (Lua numbers and strings as given) and returns what it placed.</summary>
        public List<Placement> Call(string function, params object[] args)
        {
            if (_lua == null)
                throw new InvalidOperationException("Load a script first");
            _placed = new List<Placement>();
            var fn = _lua.Globals.Get(function);
            if (fn.Type != DataType.Function)
            {
                Errors.Add(function + ": no such function");
                return _placed;
            }
            using (LuaCulture.Enter())
            {
                var lua = new DynValue[args.Length];
                for (int i = 0; i < args.Length; i++)
                    lua[i] = args[i] is string s ? DynValue.NewString(s) : DynValue.NewNumber(Convert.ToDouble(args[i], CultureInfo.InvariantCulture));
                try { _lua.Call(fn, lua); }
                catch (InterpreterException ex) { Errors.Add(function + ": " + ex.DecoratedMessage); }
            }
            return _placed;
        }

        /// <summary>A Noita entity id our recorder handed out, at the position it was placed (scripts read it back).</summary>
        public int NewEntity(float x, float y)
        {
            int e = _nextEntity++;
            _entities[e] = (x, y);
            return e;
        }

        Script NewEnv()
        {
            var lua = new Script(CoreModules.Preset_SoftSandbox);
            var g = lua.Globals;
            var loaded = new HashSet<string>();
            g["dofile"] = (Func<string, DynValue>)(p => lua.DoString(_read(p) ?? "", null, p));
            g["dofile_once"] = (Func<string, DynValue>)(p => loaded.Add(p) ? lua.DoString(_read(p) ?? "", null, p) : DynValue.Nil);
            g["print"] = (Action<DynValue>)(_ => { });

            DynValue Record(string kind, CallbackArguments a)
            {
                string file = a.Count > 0 ? a[0].CastToString() ?? "" : "";
                float x = a.Count > 1 ? (float)(a[1].CastToNumber() ?? 0) : 0, y = a.Count > 2 ? (float)(a[2].CastToNumber() ?? 0) : 0;
                _placed.Add(new Placement { Kind = kind, File = file, X = x, Y = y, Extra = "" });
                return DynValue.NewNumber(NewEntity(x, y));
            }
            g["EntityLoad"] = DynValue.NewCallback((c, a) => Record("entity", a));
            g["EntityLoadCameraBound"] = DynValue.NewCallback((c, a) => Record("entity", a));
            g["EntityLoadEndGameItem"] = DynValue.NewCallback((c, a) => Record("entity", a));
            g["LoadBackgroundSprite"] = DynValue.NewCallback((c, a) => { Record("background", a); return DynValue.Nil; });
            g["LoadPixelScene"] = DynValue.NewCallback((c, a) =>
            {
                // LoadPixelScene(materials, colors, x, y, background, ...)
                _placed.Add(new Placement
                {
                    Kind = "scene", File = a.Count > 0 ? a[0].CastToString() ?? "" : "", Extra = a.Count > 1 ? a[1].CastToString() ?? "" : "",
                    X = a.Count > 2 ? (float)(a[2].CastToNumber() ?? 0) : 0, Y = a.Count > 3 ? (float)(a[3].CastToNumber() ?? 0) : 0,
                });
                return DynValue.Nil;
            });
            g["CreateItemActionEntity"] = DynValue.NewCallback((c, a) =>
            {
                string id = a.Count > 0 ? a[0].CastToString() ?? "" : "";
                float x = a.Count > 1 ? (float)(a[1].CastToNumber() ?? 0) : 0, y = a.Count > 2 ? (float)(a[2].CastToNumber() ?? 0) : 0;
                _placed.Add(new Placement { Kind = "spell", File = id, X = x, Y = y, Extra = "" });
                return DynValue.NewNumber(NewEntity(x, y));
            });
            g["EntityGetTransform"] = DynValue.NewCallback((c, a) =>
            {
                var pos = a.Count > 0 && _entities.TryGetValue((int)(a[0].CastToNumber() ?? 0), out var p) ? p : (0f, 0f);
                return DynValue.NewTuple(DynValue.NewNumber(pos.Item1), DynValue.NewNumber(pos.Item2), DynValue.NewNumber(0),
                                         DynValue.NewNumber(1), DynValue.NewNumber(1));
            });
            g["RegisterSpawnFunction"] = DynValue.NewCallback((c, a) =>
            {
                if (a.Count >= 2)
                    SpawnFunctions[(uint)(long)(a[0].CastToNumber() ?? 0)] = a[1].CastToString();
                return DynValue.Nil;
            });

            // Noita's random: SetRandomSeed(x, y) then Random(a, b) / Randomf; ProceduralRandom(x, y, a, b) needs no seed
            g["SetRandomSeed"] = DynValue.NewCallback((c, a) =>
            {
                _rng = new Random(Hash(a.Count > 0 ? a[0].CastToNumber() ?? 0 : 0, a.Count > 1 ? a[1].CastToNumber() ?? 0 : 0));
                return DynValue.Nil;
            });
            g["Random"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(Next(_rng, a, 0)));
            g["Randomf"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(NextF(_rng, a, 0)));
            g["ProceduralRandom"] = DynValue.NewCallback((c, a) =>
                DynValue.NewNumber(Next(new Random(Hash(a[0].CastToNumber() ?? 0, a[1].CastToNumber() ?? 0)), a, 2)));
            g["ProceduralRandomi"] = g.Get("ProceduralRandom");
            g["ProceduralRandomf"] = DynValue.NewCallback((c, a) =>
                DynValue.NewNumber(NextF(new Random(Hash(a[0].CastToNumber() ?? 0, a[1].CastToNumber() ?? 0)), a, 2)));

            g["GetRandomActionWithType"] = DynValue.NewCallback((c, a) =>
                DynValue.NewString(_actions.RandomAction((int)(a[2].CastToNumber() ?? 1), (int)(a[3].CastToNumber() ?? -1))));
            g["GetRandomAction"] = DynValue.NewCallback((c, a) => DynValue.NewString(_actions.RandomAction((int)(a[2].CastToNumber() ?? 1), -1)));
            g["GameGetFrameNum"] = (Func<double>)(() => 0);
            g["GlobalsGetValue"] = DynValue.NewCallback((c, a) => a.Count > 1 ? a[1] : DynValue.NewString(""));
            g["GlobalsSetValue"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["HasFlagPersistent"] = DynValue.NewCallback((c, a) => DynValue.False);
            g["GameHasFlagRun"] = DynValue.NewCallback((c, a) => DynValue.False);
            g["ModIsEnabled"] = DynValue.NewCallback((c, a) => DynValue.False);
            g["ModSettingGet"] = DynValue.NewCallback((c, a) => DynValue.Nil);
            g["BiomeMapGetName"] = DynValue.NewCallback((c, a) => DynValue.NewString(""));
            g["GetParallelWorldPosition"] = DynValue.NewCallback((c, a) => DynValue.NewTuple(DynValue.NewNumber(0), DynValue.NewNumber(0)));
            g["EntityGetInRadiusWithTag"] = DynValue.NewCallback((c, a) => DynValue.NewTable(lua));
            g["EntityGetWithTag"] = DynValue.NewCallback((c, a) => DynValue.NewTable(lua));
            g.MetaTable = Stubs(lua);
            return lua;
        }

        int Hash(double x, double y) => unchecked(_seed * 7919 + (int)(x * 31) + (int)(y * 131071));

        static double Next(Random r, CallbackArguments a, int from)
        {
            if (a.Count >= from + 2)
            {
                int lo = (int)(a[from].CastToNumber() ?? 0), hi = (int)(a[from + 1].CastToNumber() ?? 0);
                return hi < lo ? lo : r.Next(lo, hi + 1);
            }
            if (a.Count == from + 1)
                return r.Next(0, (int)(a[from].CastToNumber() ?? 0) + 1);
            return r.NextDouble();
        }

        static double NextF(Random r, CallbackArguments a, int from)
        {
            if (a.Count >= from + 2)
            {
                double lo = a[from].CastToNumber() ?? 0, hi = a[from + 1].CastToNumber() ?? 0;
                return lo + r.NextDouble() * (hi - lo);
            }
            return r.NextDouble();
        }

        Table Stubs(Script lua)
        {
            var meta = new Table(lua);
            meta["__index"] = DynValue.NewCallback((c, a) =>
            {
                string name = a[1].Type == DataType.String ? a[1].String : null;
                if (name == null || name.Length == 0 || !char.IsUpper(name[0]))
                    return DynValue.Nil;
                return DynValue.NewCallback((c2, a2) =>
                {
                    if (!Missing.Contains(name))
                        Missing.Add(name);
                    return DynValue.Nil;
                });
            });
            return meta;
        }
    }
}
