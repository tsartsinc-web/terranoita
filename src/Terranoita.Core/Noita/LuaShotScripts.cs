using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MoonSharp.Interpreter;

namespace Terranoita.Noita
{
    /// <summary>
    /// What the game supplies for shot entities it owns (the root projectiles). Units are Noita's: pixels, px/second.
    /// </summary>
    public interface IShotHost
    {
        int FrameNum { get; }
        bool GetPosition(int entity, out float x, out float y);
        void SetPosition(int entity, float x, float y);
        bool GetVelocity(int entity, out float vx, out float vy);
        void SetVelocity(int entity, float vx, float vy);
        /// <summary>A host-owned field value (ProjectileComponent lifetime, damage, mWhoShot...), or null to use the stored one.</summary>
        string GetField(int entity, string component, string field);
        /// <summary>True when the host took the value (it is stored as well).</summary>
        bool SetField(int entity, string component, string field, string value);
        void Kill(int entity);
        IEnumerable<int> InRadiusWithTag(float x, float y, float radius, string tag);
        bool HitboxCenter(int entity, out float x, out float y);
        bool Raytrace(float x1, float y1, float x2, float y2, out float hitX, out float hitY);
        /// <summary>EntityLoad: the host decides what the file becomes; returns the new entity id or 0.</summary>
        int Load(string file, float x, float y);
        int HerdRelation(int a, int b);
        void Screenshake(float x, float y, float strength);
        void CameraPos(out float x, out float y);
        /// <summary>GameShootProjectile: the entity (usually one the host made in Load) flies from x,y toward tx,ty,
        /// shot by shooter.</summary>
        void Shoot(int shooter, int entity, float x, float y, float tx, float ty);
        /// <summary>GameGetSkyVisibility: 0..1, how open to the sky a spot is.</summary>
        float SkyVisibility(float x, float y);
        /// <summary>GamePlaySound (bank, event, x, y).</summary>
        void PlaySound(string bank, string evt, float x, float y);
        /// <summary>EntityGetTransform's scale_x of a game entity: the caster's facing, 1 or -1 (true_orbit.lua reads it).</summary>
        float ScaleX(int entity);
    }

    /// <summary>An IShotHost that owns nothing: override what the game (or a test) needs.</summary>
    public class ShotHostBase : IShotHost
    {
        public virtual int FrameNum => 0;
        public virtual bool GetPosition(int entity, out float x, out float y) { x = y = 0; return false; }
        public virtual void SetPosition(int entity, float x, float y) { }
        public virtual bool GetVelocity(int entity, out float vx, out float vy) { vx = vy = 0; return false; }
        public virtual void SetVelocity(int entity, float vx, float vy) { }
        public virtual string GetField(int entity, string component, string field) => null;
        public virtual bool SetField(int entity, string component, string field, string value) => false;
        public virtual void Kill(int entity) { }
        public virtual IEnumerable<int> InRadiusWithTag(float x, float y, float radius, string tag) => Enumerable.Empty<int>();
        public virtual bool HitboxCenter(int entity, out float x, out float y) { x = y = 0; return false; }
        public virtual bool Raytrace(float x1, float y1, float x2, float y2, out float hitX, out float hitY) { hitX = x2; hitY = y2; return false; }
        public virtual int Load(string file, float x, float y) => 0;
        public virtual int HerdRelation(int a, int b) => 100;
        public virtual void Screenshake(float x, float y, float strength) { }
        public virtual void CameraPos(out float x, out float y) { x = y = 0; }
        public virtual void Shoot(int shooter, int entity, float x, float y, float tx, float ty) { }
        public virtual float SkyVisibility(float x, float y) => 0;
        public virtual void PlaySound(string bank, string evt, float x, float y) { }
        public virtual float ScaleX(int entity) => 1;
    }

    /// <summary>
    /// Runs Noita's own per-projectile scripts (the LuaComponents of a projectile and of its extra_entities such as
    /// data/entities/misc/sinewave.xml) on a small entity/component store, as Noita's engine does: script_source_file
    /// every execute_every_n_frame frames, execute_on_added, execute_times, remove_after_executed, event scripts
    /// (script_death...) through Fire. Other components are only stored: the game reads them with Components().
    /// Root shots are backed by the game (IShotHost): their position, velocity and some fields are the game's.
    /// </summary>
    public sealed class LuaShotScripts
    {
        sealed class Ent
        {
            public int Id, Parent;
            public string Name = "", File = "";
            public readonly List<string> Tags = new List<string>();
            public readonly List<int> Children = new List<int>();
            public readonly List<int> Comps = new List<int>();
            public float X, Y, Rot, Sx = 1, Sy = 1;
            public bool Host, Dead;
        }

        sealed class Comp
        {
            public int Id, Entity;
            public string Type;
            public readonly List<string> Tags = new List<string>();
            public bool Enabled = true, Removed;
            public readonly Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal);
            public int AddedFrame, NextFrame = int.MaxValue, Executed;
            public int KillFrame = -1;     // LifetimeComponent: kill_frame, kept parsed
            // LuaComponent settings, parsed again only after a field of the component was set
            public bool SettingsDirty = true, RemoveAfter;
            public string Script;
            public int EveryN = 1, Times = -1;
            public bool AddedPending;
        }

        /// <summary>A read-only view of a stored component, for the game.</summary>
        public sealed class ComponentView
        {
            readonly Comp _c;
            internal ComponentView(object c) { _c = (Comp)c; }
            public int Id => _c.Id;
            public int Entity => _c.Entity;
            public string Type => _c.Type;
            public bool Enabled => _c.Enabled;
            public IEnumerable<string> Tags => _c.Tags;
            public IReadOnlyDictionary<string, string> Fields => _c.Fields;
            public string Get(string field) => _c.Fields.TryGetValue(field, out var v) ? v : null;
            public float Float(string field, float fallback = 0) =>
                float.TryParse(Get(field), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
        }

        // Noita calls a function of the script for these events; the rest use the field name without "script_"
        static readonly Dictionary<string, string> EventFunctions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "script_collision_trigger_hit", "collision_trigger" },
            { "script_item_picked_up", "item_pickup" },
        };

        readonly IShotHost _host;
        readonly Func<string, string> _read;
        readonly ComponentFieldTypes _types;
        readonly Script _lua;
        readonly Dictionary<int, Ent> _ents = new Dictionary<int, Ent>();
        readonly Dictionary<int, Comp> _comps = new Dictionary<int, Comp>();
        // hot path: the LuaComponents and LifetimeComponents in id order (ids only grow, so appending keeps the order);
        // removed ones are dropped by Sweep, which runs only when something died or was removed
        readonly List<Comp> _luaComps = new List<Comp>(), _lifeComps = new List<Comp>();
        readonly Dictionary<string, HashSet<int>> _byTag = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        bool _sweep, _cultured;
        readonly Dictionary<string, DynValue> _chunks = new Dictionary<string, DynValue>(StringComparer.Ordinal);
        readonly HashSet<string> _brokenFiles = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _materials = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Same as FirstEntityId (the game side's name).</summary>
        public const int FirstEntity = FirstEntityId;
        const int MaxEntities = 3000;   // EntityLoad stops making more (self-copying spells)
        int _nextEntity = FirstEntityId, _nextComp = 1, _frame, _curEntity, _curComp;
        Random _rng = new Random(0);

        /// <summary>Our entity ids start here, clear of the game's own numbers (creatures 1000+, spell shots 100000+):
        /// ids below it that scripts pass back (from InRadiusWithTag, EntityLoad) are the game's and go to the host.</summary>
        public const int FirstEntityId = 1000000;

        public readonly List<string> Missing = new List<string>();   // engine functions scripts called that we lack
        /// <summary>Noita's run-wide state the scripts keep (GlobalsGetValue/GlobalsSetValue, GameAddFlagRun): perks count
        /// their stacks here (PERK_SHIELD_COUNT...); the game saves them with the character's perks.</summary>
        public readonly Dictionary<string, string> GlobalValues = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly HashSet<string> RunFlags = new HashSet<string>(StringComparer.Ordinal);
        int _worldState;
        public readonly List<string> Errors = new List<string>();    // one line per broken script file
        public Action<string> Log = _ => { };

        public LuaShotScripts(IShotHost host, Func<string, string> read, ComponentFieldTypes types = null)
        {
            _host = host ?? new ShotHostBase();
            _read = read;
            _types = types ?? new ComponentFieldTypes();
            using (LuaCulture.Enter())
            {
                _lua = new Script(CoreModules.Preset_SoftSandbox);
                Api(_lua.Globals);
                _lua.Globals.MetaTable = Stubs();
                _lua.DoString(LuaCulture.Prelude);
            }
            _frame = _host.FrameNum;
        }

        /// <summary>A field of an object field (damage_multipliers.projectile). An unset damage multiplier is 1: assumed (no
        /// Noita file states it; player_base.xml sets only explosion and holy, and Noita's perk funcs multiply the others
        /// unguarded, e.g. BLEED_SLIME's projectile * 0.75).</summary>
        string ObjectRaw(Comp c, string obj, string field) =>
            GetRaw(c, obj + "." + field) ?? (obj == "damage_multipliers" ? "1" : null);

        // ---------------- entities, for the game ----------------

        /// <summary>A root shot the game owns (position, velocity, host fields); components from its projectile file if given.
        /// Its own execute_on_added scripts run on the next Update, once the game has placed it.</summary>
        public int CreateShot(string projectileFile = null)
        {
            var e = NewEnt(0);
            e.Host = true;
            if (projectileFile != null)
            {
                var x = NoitaEntityXml.Load(projectileFile, _read);
                e.File = projectileFile;
                e.Name = x.Name;
                AddTags(e, x.Tags);
                var added = new List<Comp>();
                foreach (var c in x.Components)
                    added.Add(AddComp(e, c));
                foreach (var child in x.Children)
                    Build(child, e.Id, added);
                Added(added, false);   // the game places the shot after this: on-added scripts wait for Update
            }
            return e.Id;
        }

        /// <summary>An entity of the store made by the game from parts, not from a file: the player as Noita's perk funcs
        /// see it (PC-34: DamageModelComponent, CharacterDataComponent...). Its added scripts run like any others.</summary>
        public int CreateEntity(string name, string tags, float x, float y, IEnumerable<(string type, IDictionary<string, string> fields)> comps)
        {
            var e = NewEnt(0);
            e.Name = name ?? "";
            AddTags(e, tags);
            e.X = x; e.Y = y;
            var added = new List<Comp>();
            foreach (var (type, fields) in comps)
            {
                var c = NewComp(e, type);
                foreach (var kv in fields)
                    c.Fields[kv.Key] = kv.Value;
                added.Add(c);
            }
            Added(added);
            return e.Id;
        }

        /// <summary>A perk's func from Noita's perk_list.lua, called as Noita's perk pickup calls it:
        /// func(entity_perk_item, entity_who_picked, item_name, pickup_count). False when the perk has none; a broken func
        /// throws (the game logs it).</summary>
        public bool RunPerk(string id, int who, int perkItem, int pickupCount)
        {
            using (LuaCulture.Enter())
            {
                _lua.Call(_lua.Globals.Get("dofile_once"), NoitaPerks.ListFile);
                var list = _lua.Globals.Get("perk_list").Table;
                if (list == null)
                    throw new InvalidOperationException(NoitaPerks.ListFile + " has no perk_list table");
                for (int i = 1; i <= list.Length; i++)
                {
                    var t = list.Get(i).Table;
                    if (t == null || t.Get("id").CastToString() != id)
                        continue;
                    var f = t.Get("func");
                    if (f.Type != DataType.Function)
                        return false;
                    int oldE = _curEntity;
                    _curEntity = who;
                    try { _lua.Call(f, perkItem, who, t.Get("ui_name"), pickupCount); }
                    finally { _curEntity = oldE; }
                    if (_sweep && !_updating)
                        Sweep();
                    return true;
                }
                throw new ArgumentException("not one of Noita's perks: " + id);
            }
        }

        /// <summary>Loads a Noita entity file into the store (not owned by the game) at a spot, optionally as a child.</summary>
        public int Spawn(string file, float x, float y, int parent = 0)
        {
            var xml = NoitaEntityXml.Load(file, _read);
            var added = new List<Comp>();
            int id = Build(xml, parent, added);
            var e = _ents[id];
            e.File = file;
            e.X = x + xml.X; e.Y = y + xml.Y;
            Added(added);
            return id;
        }

        /// <summary>Noita's extra_entities: loaded INTO the shot (as EntityLoadToEntity): the file's components and tags
        /// join the shot entity itself, its child entities become the shot's children. Noita's modifier scripts rely
        /// on it: true_orbit.lua, spiraling_shot.lua, horizontal_arc.lua read the ProjectileComponent and
        /// VelocityComponent of GetUpdatedEntityID(). Returns the shot entity.</summary>
        public int AttachExtra(int shotEntity, string xmlFile)
        {
            if (!_ents.TryGetValue(shotEntity, out var e) || e.Dead)
                return 0;
            var xml = NoitaEntityXml.Load(xmlFile, _read);
            AddTags(e, xml.Tags);
            var added = new List<Comp>();
            foreach (var c in xml.Components)
                added.Add(AddComp(e, c));
            foreach (var child in xml.Children)
                Build(child, e.Id, added);
            Added(added);
            return shotEntity;
        }

        public bool Alive(int entity) => _ents.TryGetValue(entity, out var e) && !e.Dead;

        public IEnumerable<int> ChildrenOf(int entity) =>
            _ents.TryGetValue(entity, out var e) ? e.Children.Where(Alive).ToList() : new List<int>();

        /// <summary>Stored components of a type (null = all) on the entity (and its descendants), enabled or not.</summary>
        public IEnumerable<ComponentView> Components(int entity, string type, bool includeChildren = true)
        {
            var list = new List<ComponentView>();
            void Walk(int id)
            {
                if (!_ents.TryGetValue(id, out var e) || e.Dead)
                    return;
                foreach (int c in e.Comps)
                    if ((type == null || _comps[c].Type == type) && !_comps[c].Removed)
                        list.Add(new ComponentView(_comps[c]));
                if (includeChildren)
                    foreach (int ch in e.Children)
                        Walk(ch);
            }
            Walk(entity);
            return list;
        }

        /// <summary>The game sets a field of the entity's own first component of a type, as the engine does when it makes
        /// a shot (the cast's gravity on VelocityComponent.gravity_y); false when the entity has none.</summary>
        public bool SetField(int entity, string type, string field, string value)
        {
            if (!_ents.TryGetValue(entity, out var e) || e.Dead)
                return false;
            foreach (int id in e.Comps)
                if (_comps[id].Type == type && !_comps[id].Removed)
                {
                    SetRaw(_comps[id], field, value);
                    return true;
                }
            return false;
        }

        /// <summary>The game removed this shot: it and its children leave the store (no scripts run).</summary>
        public void Forget(int entity)
        {
            if (!_ents.TryGetValue(entity, out var e))
                return;
            foreach (int ch in e.Children.ToList())
                Forget(ch);
            foreach (int ci in e.Comps)
                if (_comps.TryGetValue(ci, out var c))
                {
                    c.Removed = true;
                    _comps.Remove(ci);
                }
            foreach (var t in e.Tags)
                if (_byTag.TryGetValue(t, out var set))
                    set.Remove(entity);
            if (_ents.TryGetValue(e.Parent, out var p))
                p.Children.Remove(entity);
            _ents.Remove(entity);
            _sweep = true;
        }

        /// <summary>One frame: lifetimes, then every due LuaComponent (script_source_file).</summary>
        public void Update(int frame)
        {
            _updating = true;
            try
            {
            _frame = frame;
            int lives = _lifeComps.Count;   // components added during this frame wait for the next one
            for (int i = 0; i < lives; i++)
            {
                var c = _lifeComps[i];
                if (!c.Removed && c.Enabled && c.KillFrame >= 0 && frame >= c.KillFrame && Alive(c.Entity))
                    Kill(c.Entity);
            }
            int scripts = _luaComps.Count;
            using (LuaCulture.Enter())
            {
            _cultured = true;
            try
            {
            for (int i = 0; i < scripts; i++)
            {
                var c = _luaComps[i];
                if (c.Removed || !c.Enabled || !c.AddedPending && frame < c.NextFrame || !Alive(c.Entity))
                    continue;
                if (c.AddedPending)
                {
                    c.AddedPending = false;
                    RunSource(c);
                    continue;
                }
                RunSource(c);
            }
            }
            finally { _cultured = false; }
            }
            if (_sweep)
                Sweep();
            }
            finally { _updating = false; }
        }

        /// <summary>An engine event on the entity and its children: runs each LuaComponent that has this script field
        /// (e.g. "script_death") and calls the event's function with the arguments.</summary>
        public void Fire(int entity, string scriptField, params object[] args)
        {
            var targets = new List<Comp>();
            void Walk(int id)
            {
                if (!_ents.TryGetValue(id, out var e) || e.Dead)
                    return;
                foreach (int ci in e.Comps)
                {
                    var c = _comps[ci];
                    if (c.Type == "LuaComponent" && c.Enabled && !c.Removed && !string.IsNullOrEmpty(Str(c.Fields, scriptField)))
                        targets.Add(c);
                }
                foreach (int ch in e.Children)
                    Walk(ch);
            }
            Walk(entity);
            string fn = EventFunctions.TryGetValue(scriptField, out var f) ? f : scriptField.StartsWith("script_") ? scriptField.Substring(7) : scriptField;
            foreach (var c in targets)
                Run(c, Str(c.Fields, scriptField), fn, args);
            if (_sweep && !_updating)   // inside Update the lists are being walked: Update sweeps at its end
                Sweep();
        }

        /// <summary>The entity is going (the game ends its shot): the LuaComponents with execute_on_removed on it and its
        /// children run their script once, as Noita runs a component's script when it is removed (chain_shot.xml's
        /// chain_shot_launch.lua shoots the next shot of a CHAIN_SHOT from there).</summary>
        public void Removed(int entity)
        {
            RunRemoved(entity);
            if (_sweep && !_updating)
                Sweep();
        }

        void RunRemoved(int entity)
        {
            var targets = new List<Comp>();
            void Walk(int id)
            {
                if (!_ents.TryGetValue(id, out var e))
                    return;
                foreach (int ci in e.Comps)
                    if (_comps.TryGetValue(ci, out var c) && c.Type == "LuaComponent" && !c.Removed && Flag(c, "execute_on_removed"))
                        targets.Add(c);
                foreach (int ch in e.Children)
                    Walk(ch);
            }
            Walk(entity);
            foreach (var c in targets)
            {
                RemoveComp(c);   // once
                string file = Str(c.Fields, "script_source_file");
                if (!string.IsNullOrEmpty(file))
                    Run(c, file, null, null);
            }
        }

        // ---------------- building ----------------

        Ent NewEnt(int parent)
        {
            var e = new Ent { Id = _nextEntity++, Parent = parent };
            _ents[e.Id] = e;
            if (parent != 0 && _ents.TryGetValue(parent, out var p))
                p.Children.Add(e.Id);
            return e;
        }

        int Build(XmlEntity x, int parent, List<Comp> added)
        {
            var e = NewEnt(parent);
            e.Name = x.Name;
            e.File = x.Path ?? "";
            AddTags(e, x.Tags);
            if (parent != 0)
            {
                Transform(parent, out float px, out float py, out _, out _, out _);
                e.X = px + x.X; e.Y = py + x.Y;
            }
            else { e.X = x.X; e.Y = x.Y; }
            e.Rot = x.Rotation; e.Sx = x.ScaleX; e.Sy = x.ScaleY;
            foreach (var c in x.Components)
                added.Add(AddComp(e, c));
            foreach (var child in x.Children)
                Build(child, e.Id, added);
            return e.Id;
        }

        void AddTags(Ent e, string tags)
        {
            foreach (var t in (tags ?? "").Split(','))
                AddTag(e, t.Trim());
        }

        void AddTag(Ent e, string tag)
        {
            if (e == null || string.IsNullOrEmpty(tag) || e.Tags.Contains(tag))
                return;
            e.Tags.Add(tag);
            if (!_byTag.TryGetValue(tag, out var set))
                _byTag[tag] = set = new HashSet<int>();
            set.Add(e.Id);
        }

        void RemoveTag(Ent e, string tag)
        {
            if (e == null || tag == null || !e.Tags.Remove(tag))
                return;
            if (_byTag.TryGetValue(tag, out var set))
                set.Remove(e.Id);
        }

        /// <summary>Live entities of the store with the tag, in id order: the game's shots too (extra entities merge their
        /// tags into them, e.g. orbit_shot); callers drop the ones the host already reported.</summary>
        List<int> Tagged(string tag)
        {
            var list = new List<int>();
            if (tag != null && _byTag.TryGetValue(tag, out var set))
                foreach (int id in set)
                    if (_ents.TryGetValue(id, out var e) && !e.Dead)
                        list.Add(id);
            list.Sort();
            return list;
        }

        Comp AddComp(Ent e, XmlComponent x)
        {
            var c = NewComp(e, x.Type);
            c.Enabled = x.Enabled;
            foreach (var t in x.Tags.Split(','))
                if (t.Trim().Length > 0) c.Tags.Add(t.Trim());
            foreach (var kv in x.Fields)
                c.Fields[kv.Key] = kv.Value;
            return c;
        }

        Comp NewComp(Ent e, string type)
        {
            var c = new Comp { Id = _nextComp++, Entity = e.Id, Type = type, AddedFrame = _frame };
            _comps[c.Id] = c;
            e.Comps.Add(c.Id);
            if (type == "LuaComponent")
                _luaComps.Add(c);
            else if (type == "LifetimeComponent")
                _lifeComps.Add(c);
            return c;
        }

        /// <summary>Components just added: lifetimes start, scripts are scheduled (execute_on_added runs now).</summary>
        void Added(List<Comp> comps, bool runNow = true)
        {
            foreach (var c in comps)
            {
                if (c.Type == "LifetimeComponent")
                {
                    int life = (int)Num(c.Fields, "lifetime", -1);
                    c.Fields["creation_frame"] = Itos(_frame);
                    if (life > 0)
                        c.Fields["kill_frame"] = Itos(_frame + life);
                    c.KillFrame = (int)Num(c.Fields, "kill_frame", -1);
                }
                if (c.Type == "LuaComponent")
                {
                    int n = (int)Num(c.Fields, "execute_every_n_frame", 1);
                    c.NextFrame = n < 0 ? int.MaxValue : _frame + Math.Max(1, n);
                    c.AddedPending = Flag(c, "execute_on_added");
                }
            }
            if (!runNow)
                return;
            foreach (var c in comps.Where(c => c.AddedPending).ToList())
            {
                c.AddedPending = false;
                if (c.Enabled && !c.Removed && Alive(c.Entity))
                    RunSource(c);
            }
            if (_sweep && !_updating)
                Sweep();
        }

        // ---------------- running scripts ----------------

        void RunSource(Comp c)
        {
            if (c.SettingsDirty)
            {
                c.Script = Str(c.Fields, "script_source_file");
                c.EveryN = (int)Num(c.Fields, "execute_every_n_frame", 1);
                c.Times = (int)Num(c.Fields, "execute_times", -1);
                c.RemoveAfter = Flag(c, "remove_after_executed");
                c.SettingsDirty = false;
            }
            c.NextFrame = c.EveryN < 0 ? int.MaxValue : _frame + Math.Max(1, c.EveryN);
            if (string.IsNullOrEmpty(c.Script))
                return;
            Run(c, c.Script, null, null);
            c.Executed++;
            c.Fields["mTimesExecuted"] = Itos(c.Executed);
            c.Fields["mLastExecutionFrame"] = Itos(_frame);
            if (c.SettingsDirty)
            {
                // the script changed its own settings: next time they are read again; times/removal use the new ones
                c.Times = (int)Num(c.Fields, "execute_times", -1);
                c.RemoveAfter = Flag(c, "remove_after_executed");
            }
            if (c.Times > 0 && c.Executed >= c.Times)
                c.NextFrame = int.MaxValue;
            // remove_after_executed with no execute_times: removed after its first run (the Noita probe, 2026-10-09:
            // accelerating_shot.xml's air_friction - 3 is applied once)
            if (c.RemoveAfter && (c.Times < 1 || c.Executed >= c.Times))
                RemoveComp(c);
        }

        void Run(Comp c, string file, string function, object[] args)
        {
            if (_brokenFiles.Contains(file))
            {
                c.Enabled = false;
                return;
            }
            int oldE = _curEntity, oldC = _curComp;
            _curEntity = c.Entity;
            _curComp = c.Id;
            try
            {
                if (_cultured)
                    Call(file, function, args);
                else
                    using (LuaCulture.Enter())
                        Call(file, function, args);
            }
            catch (Exception ex)
            {
                c.Enabled = false;
                if (_brokenFiles.Add(file))
                {
                    string msg = file + ": " + (ex is InterpreterException ie ? ie.DecoratedMessage ?? ie.Message : ex.Message);
                    Errors.Add(msg);
                    Log(msg);
                }
            }
            finally
            {
                _curEntity = oldE;
                _curComp = oldC;
            }
        }

        void Call(string file, string function, object[] args)
        {
            _lua.Call(Chunk(file));
            if (function != null)
            {
                var fn = _lua.Globals.RawGet(function);
                if (fn != null && fn.Type == DataType.Function)
                    _lua.Call(fn, (args ?? new object[0]).Select(a => DynValue.FromObject(_lua, a)).ToArray());
            }
        }

        DynValue Chunk(string file)
        {
            if (_chunks.TryGetValue(file, out var f))
                return f;
            string text = _read(file) ?? throw new InvalidOperationException("Noita file missing: " + file);
            f = _lua.LoadString(text, null, file);
            _chunks[file] = f;
            return f;
        }

        void Kill(int entity)
        {
            if (!_ents.TryGetValue(entity, out var e))
            {
                if (entity > 0)
                    _host.Kill(entity);   // one of the game's entities
                return;
            }
            if (e.Dead)
                return;
            e.Dead = true;
            _sweep = true;
            foreach (int ch in e.Children)
                Kill(ch);
            if (e.Host)
                _host.Kill(entity);
        }

        void RemoveComp(Comp c)
        {
            c.Removed = true;
            c.Enabled = false;
            _sweep = true;
        }

        /// <summary>Drops dead entities and removed components (after a run, so scripts never see ids vanish mid-run).</summary>
        bool _updating;   // Update is walking _luaComps/_lifeComps by index: no Sweep may shrink them meanwhile

        void Sweep()
        {
            _sweep = false;
            foreach (var c in _comps.Values.Where(c => c.Removed).ToList())
            {
                _comps.Remove(c.Id);
                if (_ents.TryGetValue(c.Entity, out var e))
                    e.Comps.Remove(c.Id);
            }
            // the top-most dead entities; Forget takes their children with them
            foreach (var e in _ents.Values.Where(e => e.Dead && !(_ents.TryGetValue(e.Parent, out var p) && p.Dead)).ToList())
            {
                RunRemoved(e.Id);
                Forget(e.Id);
            }
            _luaComps.RemoveAll(c => c.Removed);
            _lifeComps.RemoveAll(c => c.Removed);
            _sweep = false;
        }

        // ---------------- transforms and fields ----------------

        bool Inherits(Ent e)
        {
            if (e.Parent == 0)
                return false;
            foreach (int ci in e.Comps)
                if (_comps.TryGetValue(ci, out var c) && c.Enabled && c.Type == "InheritTransformComponent")
                    return true;
            return false;
        }

        /// <summary>The first component of a type (and tag), without allocating (hot: most scripts start with it).</summary>
        Comp FirstComp(Ent e, string type, string tag, bool includeDisabled)
        {
            if (e == null || e.Dead)
                return null;
            foreach (int ci in e.Comps)
                if (_comps.TryGetValue(ci, out var c) && !c.Removed && (type == null || c.Type == type) && (includeDisabled || c.Enabled) &&
                    (string.IsNullOrEmpty(tag) || c.Tags.Contains(tag)))
                    return c;
            return null;
        }

        void Transform(int entity, out float x, out float y, out float rot, out float sx, out float sy)
        {
            x = y = rot = 0; sx = sy = 1;
            if (!_ents.TryGetValue(entity, out var e))
            {
                _host.GetPosition(entity, out x, out y);   // one of the game's entities
                sx = _host.ScaleX(entity);
                return;
            }
            if (Inherits(e))
            {
                Transform(e.Parent, out x, out y, out rot, out sx, out sy);
                return;
            }
            x = e.X; y = e.Y; rot = e.Rot; sx = e.Sx; sy = e.Sy;
            if (e.Host && _host.GetPosition(entity, out float hx, out float hy))
            {
                x = hx; y = hy;
                e.X = hx; e.Y = hy;
            }
        }

        void SetTransform(int entity, float x, float y, float? rot, float? sx, float? sy)
        {
            if (!_ents.TryGetValue(entity, out var e))
            {
                _host.SetPosition(entity, x, y);
                return;
            }
            e.X = x; e.Y = y;
            if (rot.HasValue) e.Rot = rot.Value;
            if (sx.HasValue) e.Sx = sx.Value;
            if (sy.HasValue) e.Sy = sy.Value;
            if (e.Host)
                _host.SetPosition(entity, x, y);
        }

        bool HostVelocityField(Comp c, string field) =>
            c.Type == "VelocityComponent" && field == "mVelocity" && _ents.TryGetValue(c.Entity, out var e) && e.Host;

        string GetRaw(Comp c, string field)
        {
            if (_ents.TryGetValue(c.Entity, out var e) && e.Host)
            {
                string h = _host.GetField(c.Entity, c.Type, field);
                if (h != null)
                    return h;
            }
            if (c.Fields.TryGetValue(field, out var v))
                return v;
            // never set: Noita's default for the field (component_documentation.txt), "" for a documented string
            string d = _types.Default(c.Type, field);
            return d ?? (_types.Kind(c.Type, field) == "string" ? "" : null);
        }

        void SetRaw(Comp c, string field, string value)
        {
            c.Fields[field] = value;
            if (c.Type == "LuaComponent")
                c.SettingsDirty = true;
            if (_ents.TryGetValue(c.Entity, out var e) && e.Host)
                _host.SetField(c.Entity, c.Type, field, value);
            if (c.Type == "LifetimeComponent" && field == "lifetime" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int life))
                c.Fields["kill_frame"] = Itos(life > 0 ? _frame + life : -1);
            if (c.Type == "LifetimeComponent" && (field == "lifetime" || field == "kill_frame"))
                c.KillFrame = (int)Num(c.Fields, "kill_frame", -1);
        }

        bool GetVec(Comp c, string field, out double x, out double y)
        {
            x = y = 0;
            if (HostVelocityField(c, field) && _host.GetVelocity(c.Entity, out float vx, out float vy))
            {
                x = vx; y = vy;
                return true;
            }
            string sx = GetRaw(c, field + ".x"), sy = GetRaw(c, field + ".y");
            if (sx != null || sy != null)
            {
                x = D(sx); y = D(sy);
                return true;
            }
            string both = GetRaw(c, field);
            if (both != null && both.Contains(","))
            {
                var p = both.Split(',');
                x = D(p[0]); y = D(p[1]);
                return true;
            }
            return false;
        }

        void SetVec(Comp c, string field, double x, double y)
        {
            if (HostVelocityField(c, field))
                _host.SetVelocity(c.Entity, (float)x, (float)y);
            SetRaw(c, field + ".x", Dtos(x));
            SetRaw(c, field + ".y", Dtos(y));
        }

        string Kind(Comp c, string field)
        {
            string k = _types.Kind(c.Type, field);
            if (k != null)
                return k;
            if (HostVelocityField(c, field) || GetRaw(c, field) == null && GetRaw(c, field + ".x") != null)
                return "vec2";
            return null;
        }

        DynValue Typed(string kind, string raw)
        {
            if (raw == null)
                return DynValue.Nil;
            switch (kind)
            {
                case "bool": return DynValue.NewBoolean(raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase));
                case "number": return DynValue.NewNumber(D(raw));
                case "string": return DynValue.NewString(raw);
            }
            if (raw == "true" || raw == "false")
                return DynValue.NewBoolean(raw == "true");
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? DynValue.NewNumber(d) : DynValue.NewString(raw);
        }

        static string ToRaw(DynValue v)
        {
            switch (v.Type)
            {
                case DataType.Boolean: return v.Boolean ? "1" : "0";
                case DataType.Number: return Dtos(v.Number);
                case DataType.Nil: case DataType.Void: return "";
                default: return v.CastToString() ?? "";
            }
        }

        // ---------------- the Noita API ----------------

        Comp C(CallbackArguments a, int i = 0) =>
            a.Count > i && a[i].Type == DataType.Number && _comps.TryGetValue((int)a[i].Number, out var c) && !c.Removed ? c : null;

        Ent E(CallbackArguments a, int i = 0) =>
            a.Count > i && a[i].Type == DataType.Number && _ents.TryGetValue((int)a[i].Number, out var e) ? e : null;

        static string S(CallbackArguments a, int i) => a.Count > i && a[i].Type != DataType.Nil && a[i].Type != DataType.Void ? a[i].CastToString() : null;
        /// <summary>Noita's RandomDistribution(min, max, mean, sharpness): values cluster around mean.</summary>
        double Distribution(CallbackArguments a)
        {
            double min = N(a, 0), max = N(a, 1);
            double mean = a.Count > 2 ? N(a, 2) : (min + max) / 2;
            int sharp = a.Count > 3 ? Math.Max(1, (int)N(a, 3)) : 1;
            double sum = 0;
            for (int i = 0; i < sharp; i++)
                sum += _rng.NextDouble() * 2 - 1;
            double v = mean + sum / sharp * Math.Max(mean - min, max - mean);
            return Math.Max(min, Math.Min(max, v));
        }

        static double N(CallbackArguments a, int i, double d = 0) => a.Count > i && a[i].Type == DataType.Number ? a[i].Number : a.Count > i && a[i].CastToNumber() is double x ? x : d;
        static bool B(CallbackArguments a, int i) => a.Count > i && a[i].CastToBool();

        IEnumerable<Comp> CompsOf(Ent e, string type, string tag, bool includeDisabled) =>
            e == null || e.Dead ? Enumerable.Empty<Comp>() :
            e.Comps.Select(ci => _comps[ci]).Where(c => !c.Removed && (type == null || c.Type == type) && (includeDisabled || c.Enabled) &&
                                                        (string.IsNullOrEmpty(tag) || c.Tags.Contains(tag)));

        DynValue Ids(IEnumerable<int> ids)
        {
            var list = ids.ToList();
            if (list.Count == 0)
                return DynValue.Nil;
            var t = new Table(_lua);
            foreach (int i in list)
                t.Append(DynValue.NewNumber(i));
            return DynValue.NewTable(t);
        }

        void Def(Table g, string name, Func<CallbackArguments, DynValue> f) => g[name] = DynValue.NewCallback((ctx, a) => f(a) ?? DynValue.Nil);

        void Api(Table g)
        {
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            // dofile runs the file every time (Noita), but it is compiled once; a missing file runs nothing
            g["dofile"] = DynValue.NewCallback((ctx, a) =>
            {
                string p = S(a, 0) ?? "";
                return _chunks.ContainsKey(p) || _read(p) != null ? _lua.Call(Chunk(p)) : DynValue.Nil;
            });
            g["dofile_once"] = DynValue.NewCallback((ctx, a) =>
            {
                string p = S(a, 0);
                return loaded.Add(p) ? _lua.DoString(_read(p) ?? "", null, p) : DynValue.Nil;
            });
            Def(g, "print", a => DynValue.Nil);

            Def(g, "GetUpdatedEntityID", a => DynValue.NewNumber(_curEntity));
            Def(g, "GetUpdatedComponentID", a => DynValue.NewNumber(_curComp));
            Def(g, "GameGetFrameNum", a => DynValue.NewNumber(_frame));

            // run-wide state
            Def(g, "GlobalsGetValue", a => DynValue.NewString(GlobalValues.TryGetValue(S(a, 0) ?? "", out var v) ? v : S(a, 1) ?? ""));
            Def(g, "GlobalsSetValue", a => { GlobalValues[S(a, 0) ?? ""] = a.Count > 1 ? a[1].CastToString() ?? "" : ""; return null; });
            Def(g, "GameAddFlagRun", a => { RunFlags.Add(S(a, 0) ?? ""); return null; });
            Def(g, "GameRemoveFlagRun", a => { RunFlags.Remove(S(a, 0) ?? ""); return null; });
            Def(g, "GameHasFlagRun", a => DynValue.NewBoolean(RunFlags.Contains(S(a, 0) ?? "")));
            Def(g, "GameGetWorldStateEntity", a =>
            {
                // Noita's world state entity: one, with its WorldStateComponent (perks set fields such as perk_gold_is_forever)
                if (_worldState == 0 || !_ents.ContainsKey(_worldState))
                    _worldState = CreateEntity("world_state", "", 0, 0, new (string, IDictionary<string, string>)[] { ("WorldStateComponent", new Dictionary<string, string>()) });
                return DynValue.NewNumber(_worldState);
            });

            // random
            Def(g, "SetRandomSeed", a =>
            {
                _rng = new Random(unchecked((int)(N(a, 0) * 31) * 7919 + (int)(N(a, 1) * 131)));
                return null;
            });
            Def(g, "Random", a =>
            {
                if (a.Count >= 2) { int lo = (int)N(a, 0), hi = (int)N(a, 1); return DynValue.NewNumber(hi < lo ? lo : _rng.Next(lo, hi + 1)); }
                if (a.Count == 1) { int hi = (int)N(a, 0); return DynValue.NewNumber(hi < 0 ? 0 : _rng.Next(0, hi + 1)); }
                return DynValue.NewNumber(_rng.NextDouble());
            });
            Def(g, "Randomf", a => DynValue.NewNumber(a.Count >= 2 ? N(a, 0) + _rng.NextDouble() * (N(a, 1) - N(a, 0)) : _rng.NextDouble()));

            // entities
            Def(g, "EntityGetTransform", a =>
            {
                Transform((int)N(a, 0), out float x, out float y, out float r, out float sx, out float sy);
                return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y), DynValue.NewNumber(r), DynValue.NewNumber(sx), DynValue.NewNumber(sy));
            });
            Func<CallbackArguments, DynValue> setT = a =>
            {
                SetTransform((int)N(a, 0), (float)N(a, 1), (float)N(a, 2),
                    a.Count > 3 && a[3].Type == DataType.Number ? (float)a[3].Number : (float?)null,
                    a.Count > 4 && a[4].Type == DataType.Number ? (float)a[4].Number : (float?)null,
                    a.Count > 5 && a[5].Type == DataType.Number ? (float)a[5].Number : (float?)null);
                return null;
            };
            Def(g, "EntitySetTransform", setT);
            Def(g, "EntityApplyTransform", setT);
            Def(g, "EntityGetRootEntity", a =>
            {
                var e = E(a);
                if (e == null) return DynValue.NewNumber(N(a, 0));
                while (e.Parent != 0 && _ents.TryGetValue(e.Parent, out var p)) e = p;
                return DynValue.NewNumber(e.Id);
            });
            Def(g, "EntityGetParent", a => DynValue.NewNumber(E(a)?.Parent ?? 0));
            Def(g, "EntityGetAllChildren", a => Ids(E(a)?.Children.Where(Alive) ?? Enumerable.Empty<int>()));
            Def(g, "EntityGetName", a => DynValue.NewString(E(a)?.Name ?? ""));
            Def(g, "EntitySetName", a => { var e = E(a); if (e != null) e.Name = S(a, 1) ?? ""; return null; });
            Def(g, "EntityGetFilename", a => DynValue.NewString(E(a)?.File ?? ""));
            Def(g, "EntityGetIsAlive", a => DynValue.NewBoolean(Alive((int)N(a, 0))));
            Def(g, "EntityAddTag", a => { AddTag(E(a), S(a, 1)); return null; });
            Def(g, "EntityRemoveTag", a => { RemoveTag(E(a), S(a, 1)); return null; });
            Def(g, "EntityHasTag", a => DynValue.NewBoolean(E(a)?.Tags.Contains(S(a, 1)) ?? false));
            Def(g, "EntityGetTags", a => DynValue.NewString(string.Join(",", E(a)?.Tags ?? new List<string>())));
            Def(g, "EntityGetWithTag", a =>
            {
                // Noita: a table, empty when none; the game's shots/creatures with the tag too
                string tag = S(a, 0);
                var ids = new List<int>(_host.InRadiusWithTag(0, 0, float.MaxValue, tag));
                var have = new HashSet<int>(ids);
                foreach (int id in Tagged(tag))
                    if (have.Add(id))
                        ids.Add(id);
                var t = new Table(_lua);
                foreach (int i in ids) t.Append(DynValue.NewNumber(i));
                return DynValue.NewTable(t);
            });
            Def(g, "EntityKill", a => { Kill((int)N(a, 0)); return null; });
            Def(g, "EntityAddChild", a =>
            {
                var p = E(a, 0); var ch = E(a, 1);
                if (p == null || ch == null || p == ch) return null;
                if (_ents.TryGetValue(ch.Parent, out var old)) old.Children.Remove(ch.Id);
                ch.Parent = p.Id;
                p.Children.Add(ch.Id);
                return null;
            });
            Def(g, "EntityLoad", a =>
            {
                // the game first (projectiles, creatures); anything else (effects with their own scripts) lives here
                string file = S(a, 0);
                float x = (float)N(a, 1), y = (float)N(a, 2);
                int id = _host.Load(file, x, y);
                if (id == 0 && file != null && _ents.Count < MaxEntities && _read(file) != null)
                {
                    try { id = Spawn(file, x, y); }
                    catch (Exception ex) { Log?.Invoke("EntityLoad " + file + ": " + ex.Message); }
                }
                return DynValue.NewNumber(id);
            });
            Def(g, "GameShootProjectile", a =>
            {
                _host.Shoot((int)N(a, 0), (int)N(a, 5), (float)N(a, 1), (float)N(a, 2), (float)N(a, 3), (float)N(a, 4));
                return null;
            });
            Def(g, "RandomDistribution", a => DynValue.NewNumber(Math.Round(Distribution(a))));
            Def(g, "RandomDistributionf", a => DynValue.NewNumber(Distribution(a)));
            Def(g, "EntityGetClosestWithTag", a =>
            {
                float x = (float)N(a, 0), y = (float)N(a, 1);
                string tag = S(a, 2);
                int best = 0;
                float bestD = float.MaxValue;
                var ids = new List<int>(_host.InRadiusWithTag(x, y, float.MaxValue, tag));
                ids.AddRange(Tagged(tag));
                foreach (int id in ids)
                {
                    Transform(id, out float ex, out float ey, out _, out _, out _);
                    float d = (ex - x) * (ex - x) + (ey - y) * (ey - y);
                    if (d < bestD) { bestD = d; best = id; }
                }
                return DynValue.NewNumber(best);
            });
            Def(g, "GameGetSkyVisibility", a => DynValue.NewNumber(_host.SkyVisibility((float)N(a, 0), (float)N(a, 1))));
            Def(g, "GamePlaySound", a =>
            {
                _host.PlaySound(S(a, 0), S(a, 1), (float)N(a, 2), (float)N(a, 3));
                return null;
            });
            Def(g, "ProceduralRandomi", a =>
            {
                // Noita's position-seeded random: the same spot gives the same number
                double x = N(a, 0), y = N(a, 1);
                int lo = a.Count > 2 ? (int)N(a, 2) : 0, hi = a.Count > 3 ? (int)N(a, 3) : 1;
                unchecked
                {
                    uint h = (uint)(int)(x * 73856093) ^ (uint)(int)(y * 19349663) ^ 0x9E3779B9u;
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    return DynValue.NewNumber(hi <= lo ? lo : lo + (int)(h % (uint)(hi - lo + 1)));
                }
            });
            // physics bodies, the player's inventory and worm attractors are not part of spell shots here
            Def(g, "PhysicsApplyForceOnArea", a => null);
            Def(g, "PhysicsApplyForce", a => null);
            Def(g, "PhysicsApplyTorque", a => null);
            Def(g, "GameGetAllInventoryItems", a => DynValue.NewTable(new Table(_lua)));
            Def(g, "GamePickUpInventoryItem", a => null);
            Def(g, "EntityGetClosestWormAttractor", a => DynValue.Nil);
            Def(g, "EntityGetClosestWormDetractor", a => DynValue.Nil);
            Def(g, "EntityLoadToEntity", a =>
            {
                var e = E(a, 1);
                if (e == null) return null;
                var x = NoitaEntityXml.Load(S(a, 0), _read);
                var added = x.Components.Select(c => AddComp(e, c)).ToList();
                AddTags(e, x.Tags);
                foreach (var child in x.Children)
                    Build(child, e.Id, added);
                Added(added);
                return null;
            });
            Def(g, "EntityGetInRadiusWithTag", a =>
            {
                float x = (float)N(a, 0), y = (float)N(a, 1), r = (float)N(a, 2);
                string tag = S(a, 3);
                var ids = new List<int>(_host.InRadiusWithTag(x, y, r, tag));
                var have = new HashSet<int>(ids);
                foreach (int id in Tagged(tag))
                {
                    Transform(id, out float ex, out float ey, out _, out _, out _);
                    if ((ex - x) * (ex - x) + (ey - y) * (ey - y) <= r * r && have.Add(id))
                        ids.Add(id);
                }
                var t = new Table(_lua);
                foreach (int i in ids) t.Append(DynValue.NewNumber(i));
                return DynValue.NewTable(t);   // Noita: an empty table, not nil
            });
            Def(g, "EntityGetFirstHitboxCenter", a =>
            {
                int id = (int)N(a, 0);
                if (!_host.HitboxCenter(id, out float x, out float y))
                    Transform(id, out x, out y, out _, out _, out _);
                return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y));
            });
            Def(g, "EntityGetHerdRelation", a => DynValue.NewNumber(_host.HerdRelation((int)N(a, 0), (int)N(a, 1))));
            Def(g, "EntityGetHerdRelationSafe", a => DynValue.NewNumber(_host.HerdRelation((int)N(a, 0), (int)N(a, 1))));

            // components of an entity
            Def(g, "EntityGetComponent", a => Ids(CompsOf(E(a), S(a, 1), S(a, 2), false).Select(c => c.Id)));
            Def(g, "EntityGetComponentIncludingDisabled", a => Ids(CompsOf(E(a), S(a, 1), S(a, 2), true).Select(c => c.Id)));
            Def(g, "EntityGetAllComponents", a => Ids(CompsOf(E(a), null, null, true).Select(c => c.Id)));
            Def(g, "EntityGetFirstComponent", a =>
            {
                var c = FirstComp(E(a), S(a, 1), S(a, 2), false);
                return c == null ? DynValue.Nil : DynValue.NewNumber(c.Id);
            });
            Def(g, "EntityGetFirstComponentIncludingDisabled", a =>
            {
                var c = FirstComp(E(a), S(a, 1), S(a, 2), true);
                return c == null ? DynValue.Nil : DynValue.NewNumber(c.Id);
            });
            Func<CallbackArguments, DynValue> addComp = a =>
            {
                var e = E(a);
                if (e == null || e.Dead) return DynValue.NewNumber(0);
                var c = NewComp(e, S(a, 1));
                if (a.Count > 2 && a[2].Type == DataType.Table)
                    foreach (var kv in a[2].Table.Pairs)
                    {
                        string k = kv.Key.CastToString();
                        if (k == "_tags") { foreach (var t in (kv.Value.CastToString() ?? "").Split(',')) if (t.Trim().Length > 0) c.Tags.Add(t.Trim()); }
                        else if (k == "_enabled") c.Enabled = kv.Value.CastToBool() && kv.Value.CastToString() != "0";
                        else if (kv.Value.Type == DataType.Table)
                        {
                            var t = kv.Value.Table;
                            if (t.Length >= 2 && t.Get(1).Type == DataType.Number)
                            {
                                c.Fields[k + ".x"] = Dtos(t.Get(1).Number);
                                c.Fields[k + ".y"] = Dtos(t.Get(2).Number);
                            }
                            else foreach (var kv2 in t.Pairs)
                                c.Fields[k + "." + kv2.Key.CastToString()] = ToRaw(kv2.Value);
                        }
                        else c.Fields[k] = ToRaw(kv.Value);
                    }
                Added(new List<Comp> { c });
                return DynValue.NewNumber(c.Id);
            };
            Def(g, "EntityAddComponent", addComp);
            Def(g, "EntityAddComponent2", addComp);
            Def(g, "EntityRemoveComponent", a => { var c = C(a, 1); if (c != null) RemoveComp(c); return null; });
            Def(g, "EntitySetComponentIsEnabled", a => { var c = C(a, 1); if (c != null) c.Enabled = B(a, 2); return null; });
            Def(g, "EntitySetComponentsWithTagEnabled", a =>
            {
                foreach (var c in CompsOf(E(a), null, S(a, 1), true)) c.Enabled = B(a, 2);
                return null;
            });
            Def(g, "ComponentGetIsEnabled", a => DynValue.NewBoolean(C(a)?.Enabled ?? false));
            Def(g, "ComponentGetEntity", a => DynValue.NewNumber(C(a)?.Entity ?? 0));
            Def(g, "ComponentGetTypeName", a => DynValue.NewString(C(a)?.Type ?? ""));
            Def(g, "ComponentHasTag", a => DynValue.NewBoolean(C(a)?.Tags.Contains(S(a, 1)) ?? false));
            Def(g, "GameGetGameEffect", a =>
            {
                string name = S(a, 1);
                var c = CompsOf(E(a), "GameEffectComponent", null, true).FirstOrDefault(x => Str(x.Fields, "effect") == name);
                return DynValue.NewNumber(c?.Id ?? 0);
            });

            // component values
            // lua_api_documentation.txt: "ComponentGetMetaCustom ... [Deprecated, use ComponentGetValue2() instead.]" (STRONG_KICK)
            Def(g, "ComponentGetMetaCustom", a => { var c = C(a); return c == null ? DynValue.Nil : Typed(null, GetRaw(c, S(a, 1))); });
            Def(g, "ComponentGetValue", a => { var c = C(a); return DynValue.NewString(c == null ? "" : GetRaw(c, S(a, 1)) ?? ""); });
            Def(g, "ComponentGetValueInt", a => { var c = C(a); return DynValue.NewNumber(c == null ? 0 : Math.Truncate(D(GetRaw(c, S(a, 1))))); });
            Def(g, "ComponentGetValueFloat", a => { var c = C(a); return DynValue.NewNumber(c == null ? 0 : D(GetRaw(c, S(a, 1)))); });
            Def(g, "ComponentGetValueBool", a => { var c = C(a); string v = c == null ? null : GetRaw(c, S(a, 1)); return DynValue.NewBoolean(v == "1" || v == "true"); });
            Def(g, "ComponentGetValue2", a =>
            {
                var c = C(a);
                if (c == null) return DynValue.Nil;
                string f = S(a, 1);
                string kind = Kind(c, f);
                if (kind == "vec2" && GetVec(c, f, out double x, out double y))
                    return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y));
                return Typed(kind, GetRaw(c, f));
            });
            Def(g, "ComponentGetValueVector2", a =>
            {
                var c = C(a);
                double x = 0, y = 0;
                if (c != null) GetVec(c, S(a, 1), out x, out y);
                return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y));
            });
            Def(g, "ComponentSetValue", a => { var c = C(a); if (c != null) SetRaw(c, S(a, 1), S(a, 2) ?? ""); return null; });
            Def(g, "ComponentSetValue2", a =>
            {
                var c = C(a);
                if (c == null) return null;
                string f = S(a, 1);
                if (a.Count >= 4 && a[2].Type == DataType.Number && a[3].Type == DataType.Number)
                    SetVec(c, f, a[2].Number, a[3].Number);
                else
                    SetRaw(c, f, a.Count > 2 ? ToRaw(a[2]) : "");
                return null;
            });
            Def(g, "ComponentSetValueVector2", a => { var c = C(a); if (c != null) SetVec(c, S(a, 1), N(a, 2), N(a, 3)); return null; });
            Def(g, "ComponentObjectGetValue", a => { var c = C(a); return DynValue.NewString(c == null ? "" : ObjectRaw(c, S(a, 1), S(a, 2)) ?? ""); });
            Def(g, "ComponentObjectGetValue2", a =>
            {
                var c = C(a);
                if (c == null) return DynValue.Nil;
                string f = S(a, 1) + "." + S(a, 2);
                if (GetRaw(c, f) == null && GetVec(c, f, out double x, out double y))
                    return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y));
                return Typed(null, ObjectRaw(c, S(a, 1), S(a, 2)));
            });
            Def(g, "ComponentObjectSetValue", a => { var c = C(a); if (c != null) SetRaw(c, S(a, 1) + "." + S(a, 2), S(a, 3) ?? ""); return null; });
            Def(g, "ComponentObjectSetValue2", a =>
            {
                var c = C(a);
                if (c == null) return null;
                string f = S(a, 1) + "." + S(a, 2);
                if (a.Count >= 5 && a[3].Type == DataType.Number && a[4].Type == DataType.Number)
                    SetVec(c, f, a[3].Number, a[4].Number);
                else
                    SetRaw(c, f, a.Count > 3 ? ToRaw(a[3]) : "");
                return null;
            });

            // world, through the game
            Def(g, "GameGetVelocityCompVelocity", a =>
            {
                int id = (int)N(a, 0);
                var e = E(a);
                float vx = 0, vy = 0;
                if ((e == null || e.Host) && _host.GetVelocity(id, out vx, out vy)) { }
                else
                {
                    var c = CompsOf(e, "VelocityComponent", null, true).FirstOrDefault();
                    if (c != null && GetVec(c, "mVelocity", out double x, out double y)) { vx = (float)x; vy = (float)y; }
                }
                return DynValue.NewTuple(DynValue.NewNumber(vx), DynValue.NewNumber(vy));
            });
            Func<CallbackArguments, DynValue> ray = a =>
            {
                bool hit = _host.Raytrace((float)N(a, 0), (float)N(a, 1), (float)N(a, 2), (float)N(a, 3), out float hx, out float hy);
                return DynValue.NewTuple(DynValue.NewBoolean(hit), DynValue.NewNumber(hx), DynValue.NewNumber(hy));
            };
            Def(g, "RaytraceSurfacesAndLiquiform", ray);
            Def(g, "RaytraceSurfaces", ray);
            Def(g, "RaytracePlatforms", ray);
            Def(g, "CellFactory_GetType", a =>
            {
                string m = S(a, 0) ?? "";
                if (!_materials.TryGetValue(m, out int id)) { id = _materials.Count + 1; _materials[m] = id; }
                return DynValue.NewNumber(id);
            });
            Def(g, "GameScreenshake", a => { _host.Screenshake((float)N(a, 1), (float)N(a, 2), (float)N(a, 0)); return null; });
            Def(g, "GameGetCameraPos", a => { _host.CameraPos(out float x, out float y); return DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y)); });
        }

        Table Stubs()
        {
            var meta = new Table(_lua);
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

        // ---------------- small helpers ----------------

        static string Str(Dictionary<string, string> f, string k) => f.TryGetValue(k, out var v) ? v : null;
        static double Num(Dictionary<string, string> f, string k, double d) =>
            f.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        static bool Flag(Comp c, string k) { string v = Str(c.Fields, k); return v == "1" || v == "true"; }
        static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        static string Dtos(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string Itos(int i) => i.ToString(CultureInfo.InvariantCulture);
    }
}
