using System.Collections.Generic;
using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>Finding entity files for wiki-seeded ids, and the behaviour dump the facts file carries.</summary>
    public class EntityLookupTests
    {
        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["data/entities/animals/rat.xml"] = @"<Entity name=""$animal_rat""/>",
            ["data/entities/animals/miner_chef.xml"] = @"<Entity name=""$animal_miner_chef""/>",
            ["data/entities/buildings/flynest.xml"] = @"<Entity name=""$animal_flynest""/>",
            ["data/entities/buildings/arrowtrap_left.xml"] = @"<Entity/>",
            ["data/entities/buildings/arrowtrap_right.xml"] = @"<Entity/>",
            ["data/entities/animals/shotgunner.xml"] = @"<Entity name=""$animal_shotgunner""/>",
            ["data/entities/animals/shotgunner_weak.xml"] = @"<Entity name=""$animal_shotgunner""/>",
            ["data/translations/common.csv"] = "key,en,ru\nanimal_miner_chef,Hiisi Chef,Хийси-повар\nanimal_flynest,Amppari Hive,Улей\n",
        };

        static EntityLookup Lookup() => new EntityLookup(Files.Keys, p => Files.TryGetValue(p, out var t) ? t : null,
                                                         NoitaTranslations.Parse(Files["data/translations/common.csv"]));

        [Fact]
        public void PrefersTheGuessThenTheFileName()
        {
            var l = Lookup();
            Assert.Equal(("data/entities/animals/rat.xml", "guess"), Pair(l.Find("rat", "data/entities/animals/rat.xml", null, null)));
            Assert.Equal(("data/entities/animals/rat.xml", "file_name"), Pair(l.Find("rat", "data/entities/animals/missing.xml", null, null)));
        }

        [Fact]
        public void FindsAWikiIdByItsEnglishName()
        {
            var r = Lookup().Find("cook", "data/entities/animals/cook.xml", "$animal_cook", "Hiisi Chef");
            Assert.Equal(("data/entities/animals/miner_chef.xml", "english_name"), Pair(r));
        }

        [Fact]
        public void FindsANameKeyAndPrefersTheFileSharingTheIdsWords()
        {
            var r = Lookup().Find("shotgunner_x", null, "$animal_shotgunner", null);
            Assert.Equal("name_key", r.How);
            Assert.Equal("data/entities/animals/shotgunner.xml", r.Path);
            Assert.Contains("data/entities/animals/shotgunner_weak.xml", r.Candidates);
        }

        [Fact]
        public void FallsBackToFilesContainingEveryWordOfTheId()
        {
            var nest = Lookup().Find("nest_fly", "data/entities/animals/nest_fly.xml", "$animal_nest_fly", "Nest that is not in the csv");
            Assert.Equal(("data/entities/buildings/flynest.xml", "file_words"), Pair(nest));
            var trap = Lookup().Find("trap_arrow", null, "$animal_trap_arrow", "Arrow Trap");
            Assert.Equal("file_words", trap.How);
            Assert.Equal("data/entities/buildings/arrowtrap_left.xml", trap.Path);
            Assert.Equal(new[] { "data/entities/buildings/arrowtrap_right.xml" }, trap.Candidates);
        }

        [Fact]
        public void ReportsNothingWhenNothingMatches()
        {
            var r = Lookup().Find("hidden", null, "$animal_hidden", "Spy");
            Assert.Null(r.Path);
            Assert.Empty(r.Candidates);
        }

        static (string, string) Pair(EntityLookup.Result r) => (r.Path, r.How);

        [Fact]
        public void DumpKeepsBehaviourAndChildEntitiesAndTrimsVisuals()
        {
            var e = NoitaEntity.Load(_ => @"<Entity name=""$animal_worm"">
                <WormComponent speed=""7"" acceleration=""3"" gravity=""2"" />
                <WormAIComponent speed=""5"" speed_hunt=""7"" />
                <SpriteComponent image_file=""data/enemies_gfx/worm_head.xml"" z_index=""1"" offset_x=""3"" />
                <SpriteAnimatorComponent />
                <ParticleEmitterComponent emitted_material_name=""blood_worm"" count_min=""4"" />
                <LuaComponent script_death=""data/scripts/animals/worm_death.lua"" execute_every_n_frame=""-1"" />
                <Entity name=""tail"">
                    <IKLimbWalkerComponent ground_attachment_min_spread=""16"" />
                </Entity>
            </Entity>", "data/entities/animals/worm.xml");
            var d = EntityDump.Of(e);
            Assert.Equal("7", d.Single(i => i.Component.Name == "WormComponent").Component.Attr("speed"));
            Assert.DoesNotContain(d, i => i.Component.Name == "SpriteAnimatorComponent");
            var sprite = d.Single(i => i.Component.Name == "SpriteComponent").Component;
            Assert.Equal(new[] { "image_file" }, sprite.Attributes.Keys.ToArray());
            Assert.Equal("blood_worm", d.Single(i => i.Component.Name == "ParticleEmitterComponent").Component.Attr("emitted_material_name"));
            Assert.Equal("tail", d.Single(i => i.Component.Name == "IKLimbWalkerComponent").Entity);
            Assert.Equal(new[] { "data/scripts/animals/worm_death.lua" }, EntityDump.Scripts(d).ToArray());
        }

        [Fact]
        public void ScriptsNameTheEntitiesTheySpawn()
        {
            var lua = "local x = 1\nEntityLoad( \"data/entities/animals/fly.xml\", x, y )\nEntityLoad('data/entities/animals/fly.xml')\n" +
                      "if r < 0.1 then EntityLoad(\"data/entities/animals/bigfirebug.xml\") end -- data/entities/not_a_string.xml";
            Assert.Equal(new[] { "data/entities/animals/fly.xml", "data/entities/animals/bigfirebug.xml" }, EntityDump.EntityFilesIn(lua).ToArray());
        }

        [Fact]
        public void PhysicsBodiesUseTheirBodyImageAsSprite()
        {
            var e = NoitaEntity.Load(_ => @"<Entity name=""$animal_crystal"">
                <PhysicsBodyComponent />
                <PhysicsImageShapeComponent image_file=""data/buildings_gfx/crystal.png"" />
            </Entity>", "c.xml");
            Assert.Equal("data/buildings_gfx/crystal.png", EnemyFacts.From(e).Sprite);
        }

        [Fact]
        public void ComponentDocsSplitIntoBlocks()
        {
            var docs = ComponentDocs.Split("WormComponent\r\n - Members ----\r\n    float  speed  1  [0, 10]  \"how fast\"\r\n\r\nWormAIComponent\n    float speed_hunt 3\n");
            Assert.Equal(new[] { "WormComponent", "WormAIComponent" }, docs.Keys.ToArray());
            Assert.Contains("how fast", docs["WormComponent"]);
            Assert.Equal("float speed_hunt 3", docs["WormAIComponent"].Trim());
        }
    }
}
