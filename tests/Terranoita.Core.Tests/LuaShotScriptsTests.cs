using System.Collections.Generic;
using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class LuaShotScriptsTests
    {
        // A game stand-in: shots move by their velocity each frame (px/s at 60 fps).
        sealed class FakeHost : ShotHostBase
        {
            public int Frame;
            public readonly Dictionary<int, float[]> Shots = new Dictionary<int, float[]>();   // x, y, vx, vy
            public readonly List<int> Killed = new List<int>();
            public readonly Dictionary<string, string> HostFields = new Dictionary<string, string>();
            public override int FrameNum => Frame;
            public override bool GetPosition(int e, out float x, out float y)
            {
                x = y = 0;
                if (!Shots.TryGetValue(e, out var s)) return false;
                x = s[0]; y = s[1];
                return true;
            }
            public override void SetPosition(int e, float x, float y) { if (Shots.TryGetValue(e, out var s)) { s[0] = x; s[1] = y; } }
            public override bool GetVelocity(int e, out float vx, out float vy)
            {
                vx = vy = 0;
                if (!Shots.TryGetValue(e, out var s)) return false;
                vx = s[2]; vy = s[3];
                return true;
            }
            public override void SetVelocity(int e, float vx, float vy) { if (Shots.TryGetValue(e, out var s)) { s[2] = vx; s[3] = vy; } }
            public override string GetField(int e, string comp, string field) =>
                HostFields.TryGetValue(comp + "." + field, out var v) ? v : null;
            public override bool SetField(int e, string comp, string field, string value)
            {
                if (!HostFields.ContainsKey(comp + "." + field)) return false;
                HostFields[comp + "." + field] = value;
                return true;
            }
            public System.Action<int> OnKill;   // the game ends the shot: its death scripts run (SpellShots.End -> Fire)
            public override void Kill(int e) { Killed.Add(e); Shots.Remove(e); OnKill?.Invoke(e); }
            public void Step()
            {
                Frame++;
                foreach (var s in Shots.Values) { s[0] += s[2] / 60f; s[1] += s[3] / 60f; }
            }
        }

        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["data/scripts/lib/utilities.lua"] = "function clamp(v, a, b) return math.max(a, math.min(b, v)) end",
            ["data/entities/projectiles/bolt.xml"] = @"<Entity name=""bolt"" tags=""projectile"">
  <VelocityComponent gravity_y=""0"" />
  <ProjectileComponent lifetime=""60"" damage=""0.3"" />
</Entity>",
            // made-up versions of the shapes Noita's extra entities use
            ["data/entities/misc/test_sine.xml"] = @"<Entity>
  <InheritTransformComponent />
  <LuaComponent script_source_file=""data/scripts/test_sine.lua"" execute_every_n_frame=""1"" />
</Entity>",
            ["data/scripts/test_sine.lua"] = @"
dofile_once(""data/scripts/lib/utilities.lua"")
local me = GetUpdatedEntityID()
local root = EntityGetRootEntity(me)
local v = EntityGetFirstComponent(root, ""VelocityComponent"")
local vx, vy = ComponentGetValue2(v, ""mVelocity"")
local f = GameGetFrameNum()
ComponentSetValue2(v, ""mVelocity"", vx, clamp(math.sin(f * 0.5) * 100, -50, 50))
",
            ["data/entities/misc/test_counter.xml"] = @"<Entity>
  <VariableStorageComponent name=""count"" value_int=""0"" />
  <LuaComponent script_source_file=""data/scripts/test_count.lua"" execute_every_n_frame=""2"" execute_times=""3"" remove_after_executed=""1"" />
</Entity>",
            ["data/scripts/test_count.lua"] = @"
local me = GetUpdatedEntityID()
for _, c in ipairs(EntityGetComponent(me, ""VariableStorageComponent"") or {}) do
  if ComponentGetValue2(c, ""name"") == ""count"" then
    ComponentSetValue2(c, ""value_int"", ComponentGetValue2(c, ""value_int"") + 1)
  end
end
",
            // as accelerating_shot.xml: every frame, no execute_times, removed after it ran
            ["data/entities/misc/test_once.xml"] = @"<Entity>
  <VariableStorageComponent name=""count"" value_int=""0"" />
  <LuaComponent script_source_file=""data/scripts/test_count.lua"" execute_every_n_frame=""1"" remove_after_executed=""1"" />
</Entity>",
            ["data/entities/misc/test_fuse.xml"] = @"<Entity>
  <LuaComponent script_source_file=""data/scripts/test_fuse.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" />
</Entity>",
            ["data/scripts/test_fuse.lua"] = @"
local root = EntityGetRootEntity(GetUpdatedEntityID())
EntityAddComponent(root, ""LifetimeComponent"", { lifetime = ""5"" })
EntityAddTag(root, ""fused"")
",
            ["data/entities/misc/test_broken.xml"] = @"<Entity>
  <VariableStorageComponent name=""count"" value_int=""0"" />
  <LuaComponent script_source_file=""data/scripts/test_bad.lua"" />
  <LuaComponent script_source_file=""data/scripts/test_count.lua"" />
  <LuaComponent script_source_file=""data/scripts/test_missing.lua"" />
</Entity>",
            ["data/scripts/test_bad.lua"] = "local x = nil; x.y = 1",
            ["data/scripts/test_missing.lua"] = "GameDoSomethingNew(1); local a = ComponentGetValue(0, 'x'); if a ~= '' then error('not a string') end",
            ["data/entities/misc/test_base.xml"] = @"<Entity tags=""base_tag"">
  <!-- a comment <Entity> -->
  <ProjectileComponent damage=""1"" lifetime=""10"">
    <config_explosion damage=""3"" explosion_radius=""8"" />
  </ProjectileComponent>
  <HomingComponent target_tag=""homing_target"" />
</Entity>",
            ["data/entities/misc/test_child.xml"] = @"<Entity name=""child"" tags=""own_tag"">
  <Base file=""data/entities/misc/test_base.xml"">
    <ProjectileComponent damage=""2"" />
  </Base>
  <SineWaveComponent sinewave_freq=""0.2"" _tags=""wave"" _enabled=""0"" />
  <Entity name=""grandchild""><LifetimeComponent lifetime=""3"" /></Entity>
</Entity>",
            ["data/entities/misc/test_death.xml"] = @"<Entity>
  <VariableStorageComponent name=""died"" value_string="""" />
  <LuaComponent script_death=""data/scripts/test_death.lua"" />
</Entity>",
            ["data/entities/misc/test_suicide.xml"] = @"<Entity>
  <LuaComponent script_source_file=""data/scripts/test_suicide.lua"" execute_every_n_frame=""1"" />
  <LuaComponent script_death=""data/scripts/test_death.lua"" />
  <VariableStorageComponent name=""died"" value_string="""" />
</Entity>",
            ["data/scripts/test_suicide.lua"] = "EntityKill(GetUpdatedEntityID())",   // extras are loaded into the shot
            ["data/scripts/test_death.lua"] = @"
function death(damage_type, message, responsible, drop)
  local me = GetUpdatedEntityID()
  local c = EntityGetFirstComponent(me, ""VariableStorageComponent"")
  ComponentSetValue2(c, ""value_string"", message)
end
",
            ["data/entities/misc/test_types.xml"] = @"<Entity>
  <ProjectileComponent on_death_explode=""0"" lifetime=""4"" />
  <LuaComponent script_source_file=""data/scripts/test_types.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" />
</Entity>",
            ["data/scripts/test_types.lua"] = @"
local c = EntityGetFirstComponent(GetUpdatedEntityID(), ""ProjectileComponent"")
local explode = ComponentGetValue2(c, ""on_death_explode"")
if explode then ComponentSetValue2(c, ""lifetime"", 99) else ComponentSetValue2(c, ""lifetime"", 7) end
local s = ComponentGetValue(c, ""lifetime"")
ComponentSetValue2(c, ""seen"", type(s))
SetRandomSeed(1, 2)
local r1, r2, r3 = Random(), Random(5), Random(3, 4)
ComponentSetValue2(c, ""randoms"", (r1 >= 0 and r1 < 1 and r2 >= 0 and r2 <= 5 and r2 == math.floor(r2) and r3 >= 3 and r3 <= 4) and ""ok"" or ""bad"")
",
        };

        static string Read(string p) => Files.TryGetValue(p, out var s) ? s : null;

        static (LuaShotScripts lua, FakeHost host, int shot) Shot(ComponentFieldTypes types = null)
        {
            var host = new FakeHost();
            var lua = new LuaShotScripts(host, Read, types);
            int shot = lua.CreateShot("data/entities/projectiles/bolt.xml");
            host.Shots[shot] = new float[] { 100, 50, 300, 0 };
            return (lua, host, shot);
        }

        static void Run(LuaShotScripts lua, FakeHost host, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                host.Step();
                lua.Update(host.Frame);
            }
        }

        [Fact]
        public void SineWaveScriptSteersTheGameShot()
        {
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_sine.xml");
            Run(lua, host, 10);
            var s = host.Shots[shot];
            Assert.Equal(300, s[2]);                                   // vx read and written back unchanged
            Assert.Equal((float)System.Math.Max(-50, System.Math.Min(50, System.Math.Sin(10 * 0.5) * 100)), s[3], 3);
            Assert.True(lua.Alive(child));
            Assert.Empty(lua.Errors);
            Assert.Empty(lua.Missing);
        }

        [Fact]
        public void EveryNFramesAndExecuteTimes()
        {
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_counter.xml");
            Run(lua, host, 20);
            var store = lua.Components(child, "VariableStorageComponent").Single();
            Assert.Equal("3", store.Get("value_int"));                // ran on frames 2, 4, 6 only
            Assert.Empty(lua.Components(child, "LuaComponent"));      // remove_after_executed
        }

        [Fact]
        public void TheGameSetsAFieldOfTheShotsOwnComponent()
        {
            // as the engine does when it makes a shot: the cast's gravity added to the file's VelocityComponent
            var (lua, _, shot) = Shot();
            Assert.True(lua.SetField(shot, "VelocityComponent", "gravity_y", "120"));
            Assert.Equal("120", lua.Components(shot, "VelocityComponent", false).Single().Get("gravity_y"));
            Assert.False(lua.SetField(shot, "HomingComponent", "detect_distance", "50"));   // bolt.xml has none
            Assert.False(lua.SetField(12345, "VelocityComponent", "gravity_y", "1"));         // no such entity
        }

        [Fact]
        public void RemoveAfterExecutedWithoutTimesRunsOnce()
        {
            // the Noita probe (2026-10-09): ACCELERATING_SHOT's air_friction - 3 is applied once (the spark speeds up by
            // a steady 2.15% a frame), though its LuaComponent sets no execute_times ("< 1 means infinite")
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_once.xml");
            Run(lua, host, 10);
            Assert.Equal("1", lua.Components(child, "VariableStorageComponent").Single().Get("value_int"));
            Assert.Empty(lua.Components(child, "LuaComponent"));
        }

        [Fact]
        public void ScriptAddsALifetimeThatKillsTheShotAndItsChildren()
        {
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_fuse.xml");
            Assert.Single(lua.Components(shot, "LifetimeComponent", false));   // execute_on_added ran at once
            Run(lua, host, 4);
            Assert.True(lua.Alive(shot));
            Run(lua, host, 2);
            Assert.Equal(new[] { shot }, host.Killed);
            Assert.False(lua.Alive(shot));
            Assert.False(lua.Alive(child));
        }

        [Fact]
        public void BrokenScriptOnlyDisablesItsComponentAndMissingApiIsRecorded()
        {
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_broken.xml");
            Run(lua, host, 5);
            Assert.Single(lua.Errors);
            Assert.Contains("test_bad.lua", lua.Errors[0]);
            Assert.Equal("5", lua.Components(child, "VariableStorageComponent").Single().Get("value_int"));
            Assert.Equal(new[] { "GameDoSomethingNew" }, lua.Missing);
            Assert.Equal(new[] { false, true, true }, lua.Components(child, "LuaComponent").Select(c => c.Enabled));
        }

        [Fact]
        public void BaseChainMergesAndObjectFieldsFlatten()
        {
            var x = NoitaEntityXml.Load("data/entities/misc/test_child.xml", Read);
            Assert.Equal("child", x.Name);
            Assert.Equal("base_tag,own_tag", x.Tags);
            var proj = x.Components.Single(c => c.Type == "ProjectileComponent");
            Assert.Equal("2", proj.Get("damage"));                    // overridden inside <Base>
            Assert.Equal("10", proj.Get("lifetime"));                 // kept from the base
            Assert.Equal("3", proj.Get("config_explosion.damage"));
            var wave = x.Components.Single(c => c.Type == "SineWaveComponent");
            Assert.False(wave.Enabled);
            Assert.Equal("wave", wave.Tags);
            Assert.Equal(new[] { "ProjectileComponent", "HomingComponent", "SineWaveComponent" }, x.Components.Select(c => c.Type));
            Assert.Equal("grandchild", Assert.Single(x.Children).Name);

            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_child.xml");
            Assert.Single(lua.Components(shot, "HomingComponent"));   // the game finds it through the shot
            Assert.Single(lua.Components(shot, "HomingComponent", false));   // merged into the shot itself (Noita: EntityLoadToEntity)
            Assert.Equal(shot, child);
            int grandchild = lua.ChildrenOf(child).Single();
            Run(lua, host, 3);
            Assert.False(lua.Alive(grandchild));                      // its LifetimeComponent ran out
            Assert.True(lua.Alive(child));
            Assert.Empty(host.Killed);
        }

        [Fact]
        public void ShotKilledByAScriptMidFrameRunsItsDeathAndTheFrameGoesOn()
        {
            // three shots whose scripts kill them in the same frame; each death runs its script_death (the game's
            // End fires it), which once swept the component lists while Update walked them (IndexOutOfRange, 2026-10-09)
            var host = new FakeHost();
            var lua = new LuaShotScripts(host, Read);
            host.OnKill = e => lua.Fire(e, "script_death", 1, "gone", 0, false);
            var shots = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int s = lua.CreateShot("data/entities/projectiles/bolt.xml");
                host.Shots[s] = new float[] { 100 + i, 50, 0, 0 };
                shots.Add(s);
                lua.AttachExtra(s, "data/entities/misc/test_suicide.xml");
            }
            Run(lua, host, 2);
            Assert.Equal(shots, host.Killed);
            Assert.All(shots, s => Assert.False(lua.Alive(s)));
        }

        [Fact]
        public void FireRunsTheEventFunction()
        {
            var (lua, host, shot) = Shot();
            int child = lua.AttachExtra(shot, "data/entities/misc/test_death.xml");
            lua.Fire(shot, "script_death", 1, "boom", 0, false);
            Assert.Equal("boom", lua.Components(child, "VariableStorageComponent").Single().Get("value_string"));
        }

        [Fact]
        public void ValuesAreTypedByTheComponentDocumentation()
        {
            const string docs = @"ProjectileComponent
 - Members -----------------------------
    int                     lifetime                                                        -1 [0, 1]                       ""frames""
    bool                    on_death_explode                                                0 [0, 1]                        """"
";
            var types = ComponentFieldTypes.Parse(docs);
            Assert.Equal("bool", types.Kind("ProjectileComponent", "on_death_explode"));
            var (lua, host, shot) = Shot(types);
            int child = lua.Spawn("data/entities/misc/test_types.xml", 0, 0);   // an entity of its own: one ProjectileComponent
            var p = lua.Components(child, "ProjectileComponent").Single();
            Assert.Equal("7", p.Get("lifetime"));                     // "0" is false for a bool field, not the number 0
            Assert.Equal("string", p.Get("seen"));
            Assert.Equal("ok", p.Get("randoms"));

            // without the documentation "0" stays a number, and Lua's 0 is true
            var (lua2, _, shot2) = Shot();
            int child2 = lua2.Spawn("data/entities/misc/test_types.xml", 0, 0);
            Assert.Equal("99", lua2.Components(child2, "ProjectileComponent").Single().Get("lifetime"));
        }

        [Fact]
        public void HostOwnedFieldsGoToTheGame()
        {
            var (lua, host, shot) = Shot();
            host.HostFields["ProjectileComponent.damage"] = "0.5";
            var p = lua.Components(shot, "ProjectileComponent", false).Single();
            Assert.Equal("0.3", p.Get("damage"));                     // stored value from the file
            Files["data/scripts/test_host.lua"] = @"
local root = EntityGetRootEntity(GetUpdatedEntityID())
local p = EntityGetFirstComponent(root, ""ProjectileComponent"")
ComponentSetValue2(p, ""damage"", ComponentGetValue2(p, ""damage"") * 2)
local x, y = EntityGetTransform(root)
EntitySetTransform(root, x + 10, y)
";
            Files["data/entities/misc/test_host.xml"] = @"<Entity><LuaComponent script_source_file=""data/scripts/test_host.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" /></Entity>";
            lua.AttachExtra(shot, "data/entities/misc/test_host.xml");
            Assert.Equal("1", host.HostFields["ProjectileComponent.damage"]);
            Assert.Equal(110, host.Shots[shot][0]);
        }
    }
}

namespace Terranoita.Tests
{
    public class LuaShotScriptsAddedTests
    {
        [Fact]
        public void ProjectileOwnOnAddedScriptWaitsForTheGame()
        {
            var files = new System.Collections.Generic.Dictionary<string, string>
            {
                ["p.xml"] = @"<Entity><VariableStorageComponent name=""x"" value_float=""0"" />
  <LuaComponent script_source_file=""s.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" /></Entity>",
                ["s.lua"] = @"local me = GetUpdatedEntityID()
local x = EntityGetTransform(me)
ComponentSetValue2(EntityGetFirstComponent(me, ""VariableStorageComponent""), ""value_float"", x)",
            };
            var host = new PlacedHost();
            var lua = new Terranoita.Noita.LuaShotScripts(host, p => files.TryGetValue(p, out var s) ? s : null);
            int shot = lua.CreateShot("p.xml");
            host.Placed = shot;
            lua.Update(1);
            lua.Update(2);
            var v = System.Linq.Enumerable.Single(lua.Components(shot, "VariableStorageComponent"));
            Assert.Equal("42", v.Get("value_float"));
            Assert.Equal("1", System.Linq.Enumerable.Single(lua.Components(shot, "LuaComponent")).Get("mTimesExecuted"));
        }

        sealed class PlacedHost : Terranoita.Noita.ShotHostBase
        {
            public int Placed = -1;
            public override bool GetPosition(int e, out float x, out float y) { x = 42; y = 0; return e == Placed; }
        }
    }
}

namespace Terranoita.Tests
{
    public class LuaShotScriptsHostIdsTests
    {
        sealed class Creatures : Terranoita.Noita.ShotHostBase
        {
            public readonly System.Collections.Generic.List<int> Killed = new System.Collections.Generic.List<int>();
            public override System.Collections.Generic.IEnumerable<int> InRadiusWithTag(float x, float y, float r, string tag) => new[] { 1005 };
            public override bool GetPosition(int e, out float x, out float y) { x = e == 1005 ? 70 : 0; y = 0; return e == 1005; }
            public override void Kill(int e) => Killed.Add(e);
        }

        [Fact]
        public void GameEntitiesFromTheHostKeepTheirIds()
        {
            var files = new System.Collections.Generic.Dictionary<string, string>
            {
                ["k.xml"] = @"<Entity><VariableStorageComponent value_float=""0"" />
  <LuaComponent script_source_file=""k.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" /></Entity>",
                ["k.lua"] = @"local me = GetUpdatedEntityID()
for _, id in ipairs(EntityGetInRadiusWithTag(0, 0, 100, ""enemy"")) do
  local x = EntityGetTransform(id)
  ComponentSetValue2(EntityGetFirstComponent(me, ""VariableStorageComponent""), ""value_float"", x)
  EntityKill(id)
end",
            };
            var host = new Creatures();
            var lua = new Terranoita.Noita.LuaShotScripts(host, p => files.TryGetValue(p, out var s) ? s : null);
            int e = lua.Spawn("k.xml", 0, 0);
            Assert.True(e >= Terranoita.Noita.LuaShotScripts.FirstEntityId);
            Assert.Equal("70", System.Linq.Enumerable.Single(lua.Components(e, "VariableStorageComponent")).Get("value_float"));
            Assert.Equal(new[] { 1005 }, host.Killed);
        }
    }
}
