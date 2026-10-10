using System.Collections.Generic;
using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>The engine functions added after the sweep over Noita's shot scripts (tncli shot-script).</summary>
    public class LuaShotScriptsApiTests
    {
        sealed class Host : ShotHostBase
        {
            public int Shot, Shooter, Shot2;
            public float Tx, Ty;
            public readonly List<string> Loaded = new List<string>();
            public override bool GetPosition(int e, out float x, out float y) { x = 10; y = 20; return true; }
            public override int Load(string file, float x, float y) { Loaded.Add(file); return file.Contains("game_") ? 5 : 0; }
            public override void Shoot(int shooter, int entity, float x, float y, float tx, float ty) { Shooter = shooter; Shot2 = entity; Tx = tx; Ty = ty; }
            public override IEnumerable<int> InRadiusWithTag(float x, float y, float r, string tag) =>
                tag == "player_unit" ? new[] { 1 } : Enumerable.Empty<int>();
            public override float SkyVisibility(float x, float y) => 0.5f;
        }

        const string Docs = @"VariableStorageComponent
 - Members -----------------------------
    float                   value_float                                                     2.5 [0, 1]                        """"
    std::string             value_string                                                    -                                 """"
";

        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["data/entities/projectiles/p.xml"] = @"<Entity tags=""projectile""><ProjectileComponent lifetime=""60"" /></Entity>",
            ["data/entities/misc/effect.xml"] = @"<Entity tags=""effect_thing""><VariableStorageComponent name=""a"" /></Entity>",
            ["data/entities/misc/t.xml"] = @"<Entity><VariableStorageComponent name=""out"" /><LuaComponent script_source_file=""data/scripts/t.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" /></Entity>",
            ["data/scripts/t.lua"] = @"
local me = GetUpdatedEntityID()
local c = EntityGetFirstComponent(me, ""VariableStorageComponent"")
local none = EntityGetWithTag(""nothing_has_this"")
local players = EntityGetWithTag(""player_unit"")
local p1 = ProceduralRandomi(3, 4, 0, 100)
local p2 = ProceduralRandomi(3, 4, 0, 100)
local d = RandomDistribution(0, 10, 5, 4)
local game = EntityLoad(""data/entities/projectiles/game_bolt.xml"", 1, 2)
GameShootProjectile(1, 0, 0, 30, 40, game)
local mine = EntityLoad(""data/entities/misc/effect.xml"", 1, 2)
local closest = EntityGetClosestWithTag(0, 0, ""effect_thing"")
ComponentSetValue2(c, ""value_string"", tostring(#none) .. "","" .. tostring(#players) .. "","" .. tostring(p1 == p2) .. "","" ..
  tostring(d >= 0 and d <= 10) .. "","" .. tostring(game) .. "","" .. tostring(mine == closest and mine > 0) .. "","" .. tostring(GameGetSkyVisibility(0, 0)))
",
        };

        [Fact]
        public void SweepFunctionsBehaveAsNoitas()
        {
            var host = new Host();
            var lua = new LuaShotScripts(host, p => Files.TryGetValue(p, out var s) ? s : null);
            int shot = lua.CreateShot("data/entities/projectiles/p.xml");
            int child = lua.AttachExtra(shot, "data/entities/misc/t.xml");
            Assert.True(child >= LuaShotScripts.FirstEntity);
            var store = lua.Components(child, "VariableStorageComponent").First(c => c.Get("name") == "out");
            Assert.Equal("0,1,true,true,5,true,0.5", store.Get("value_string"));
            Assert.Equal(1, host.Shooter);
            Assert.Equal(5, host.Shot2);
            Assert.Equal(30f, host.Tx);
            Assert.Equal(new[] { "data/entities/projectiles/game_bolt.xml", "data/entities/misc/effect.xml" }, host.Loaded);
            Assert.Empty(lua.Errors);
            Assert.Empty(lua.Missing);
        }

        [Fact]
        public void UnsetFieldsGiveNoitasDocumentedDefaults()
        {
            Files["data/entities/misc/d.xml"] = @"<Entity><VariableStorageComponent /><LuaComponent script_source_file=""data/scripts/d.lua"" execute_on_added=""1"" execute_every_n_frame=""-1"" /></Entity>";
            Files["data/scripts/d.lua"] = @"
local c = EntityGetFirstComponent(GetUpdatedEntityID(), ""VariableStorageComponent"")
local f = ComponentGetValue2(c, ""value_float"")
ComponentSetValue2(c, ""value_string"", tostring(f + 1) .. ""|"" .. tostring(ComponentGetValue2(c, ""value_int"")))
";
            var lua = new LuaShotScripts(new Host(), p => Files.TryGetValue(p, out var s) ? s : null, ComponentFieldTypes.Parse(Docs));
            int shot = lua.CreateShot("data/entities/projectiles/p.xml");
            int child = lua.AttachExtra(shot, "data/entities/misc/d.xml");
            var c = lua.Components(child, "VariableStorageComponent").Single();
            Assert.Equal("3.5|nil", c.Get("value_string"));   // documented default 2.5; value_int is not in these docs
            Assert.Empty(lua.Errors);
        }
    }
}
