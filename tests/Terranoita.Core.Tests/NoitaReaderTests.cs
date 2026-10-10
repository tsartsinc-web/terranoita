using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terranoita.Generated;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class NoitaReaderTests
    {
        // Builds an archive in the documented layout, so the reader is checked against the spec we wrote down.
        static byte[] MakeWak(params (string path, string text)[] files)
        {
            var names = files.Select(f => Encoding.UTF8.GetBytes(f.path)).ToArray();
            var datas = files.Select(f => Encoding.UTF8.GetBytes(f.text)).ToArray();
            int tocEnd = 16 + names.Sum(n => 12 + n.Length);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0u); w.Write((uint)files.Length); w.Write((uint)tocEnd); w.Write(0u);
            uint offset = (uint)tocEnd;
            for (int i = 0; i < files.Length; i++)
            {
                w.Write(offset); w.Write((uint)datas[i].Length); w.Write((uint)names[i].Length); w.Write(names[i]);
                offset += (uint)datas[i].Length;
            }
            foreach (var d in datas)
                w.Write(d);
            return ms.ToArray();
        }

        [Fact]
        public void WakArchive_ReadsEntriesAndContents()
        {
            var bytes = MakeWak(("data/a.txt", "hello"), ("data/entities/animals/rat.xml", "<Entity/>"));
            using (var wak = new WakArchive(new MemoryStream(bytes)))
            {
                Assert.Equal(2, wak.Count);
                Assert.True(wak.Contains("data\\entities\\animals\\rat.xml"));
                Assert.Equal("hello", wak.ReadText("data/a.txt"));
                Assert.False(wak.TryRead("data/missing.xml", out _));
            }
        }

        [Fact]
        public void WakArchive_RejectsGarbage()
        {
            var junk = Encoding.ASCII.GetBytes("this is definitely not a wak archive at all");
            Assert.Throws<InvalidDataException>(() => new WakArchive(new MemoryStream(junk)));
        }

        [Fact]
        public void Nxml_IsLenient()
        {
            var root = Nxml.ParseRoot(@"<?xml version=""1.0""?>
<!-- comment -- with double dash -->
<Entity name=""$animal_rat"" tags=enemy,mortal>
  <DamageModelComponent hp=""0.2"" >
    <damage_multipliers fire=""1.5"" ice='0.5' />
  </DamageModelComponent>
  stray text & more
  <SpriteComponent image_file=""data/enemies_gfx/rat.xml""/>
</Entity>");
            Assert.Equal("Entity", root.Name);
            Assert.Equal("enemy,mortal", root.Attr("tags"));
            var dm = root.Child("DamageModelComponent");
            Assert.Equal(0.2f, dm.Float("hp"));
            Assert.Equal(1.5f, dm.Child("damage_multipliers").Float("fire"));
            Assert.Equal("data/enemies_gfx/rat.xml", root.Child("SpriteComponent").Attr("image_file"));
        }

        [Fact]
        public void Entity_ResolvesBaseOverrides()
        {
            var files = new Dictionary<string, string>
            {
                ["data/entities/base_enemy.xml"] = @"<Entity name=""base"" tags=""enemy"">
                    <DamageModelComponent hp=""1"" />
                    <HitboxComponent aabb_min_x=""-4"" aabb_max_x=""4"" aabb_min_y=""-10"" aabb_max_y=""0"" />
                    <AnimalAIComponent attack_ranged_enabled=""0"" attack_melee_frames_between=""30"" />
                    <SpriteComponent image_file=""data/enemies_gfx/base.xml"" />
                </Entity>",
                ["data/entities/animals/hiisi.xml"] = @"<Entity name=""$animal_hiisi"">
                    <Base file=""data/entities/base_enemy.xml"">
                        <DamageModelComponent hp=""0.36"" />
                        <AnimalAIComponent attack_ranged_enabled=""1"" attack_ranged_entity_file=""data/entities/projectiles/shot.xml""
                            attack_ranged_entity_count_min=""3"" attack_ranged_entity_count_max=""4"" attack_ranged_max_distance=""150"" />
                        <SpriteComponent image_file=""data/enemies_gfx/hiisi.xml"" />
                    </Base>
                    <SpriteComponent image_file=""data/enemies_gfx/hiisi_emissive.xml"" emissive=""1"" />
                </Entity>",
            };
            var e = NoitaEntity.Load(p => files.TryGetValue(p, out var t) ? t : null, "data/entities/animals/hiisi.xml");
            var f = EnemyFacts.From(e);
            Assert.Equal(9f, f.DisplayHp);
            Assert.Equal("data/enemies_gfx/hiisi.xml", f.Sprite);
            Assert.Equal(new[] { 8, 10 }, f.HitboxNoitaPx);
            Assert.Equal(30, f.MeleeFramesBetween);
            Assert.Single(f.Ranged);
            Assert.Equal("data/entities/projectiles/shot.xml", f.Ranged[0].EntityFile);
            Assert.Equal(3, f.Ranged[0].CountMin);
            Assert.Equal(4, f.Ranged[0].CountMax);
            Assert.Equal("$animal_hiisi", f.NameKey);
        }

        [Fact]
        public void Entity_RemoveFromBaseDropsTheBaseComponent()
        {
            var files = new Dictionary<string, string>
            {
                ["b.xml"] = @"<Entity><ProjectileComponent damage=""1"" /><DamageModelComponent hp=""1"" /></Entity>",
                ["e.xml"] = @"<Entity><Base file=""b.xml""><ProjectileComponent _remove_from_base=""1"" /></Base></Entity>",
            };
            var e = NoitaEntity.Load(p => files.TryGetValue(p, out var t) ? t : null, "e.xml");
            Assert.Null(e.Component("ProjectileComponent"));
            Assert.NotNull(e.Component("DamageModelComponent"));
        }

        [Fact]
        public void Projectile_FactsAndUnits()
        {
            var e = NoitaEntity.Load(_ => @"<Entity>
                <ProjectileComponent speed_min=""300"" speed_max=""330"" lifetime=""90"" damage=""0.24"">
                    <config_explosion explosion_radius=""8"" />
                </ProjectileComponent>
                <VelocityComponent gravity_y=""400"" />
                <SpriteComponent image_file=""data/projectiles_gfx/acid.xml"" />
            </Entity>", "p.xml");
            var f = ProjectileFacts.From(e);
            Assert.Equal(300f, f.SpeedMin);
            Assert.Equal(90, f.LifetimeFrames);
            Assert.Equal(8f, f.ExplosionRadius);
            Assert.Equal(400f, f.GravityY);
            Assert.Equal(15f, Units.SpeedToTerraria(300f), 3);
            Assert.Equal(1.5f, Units.PxToTiles(8f), 3);
        }

        [Fact]
        public void Sprite_FrameRectsAndTiming()
        {
            var s = NoitaSprite.Parse(@"<Sprite filename=""data/enemies_gfx/rat.png"" offset_x=""6"" offset_y=""11"" default_animation=""stand"">
                <RectAnimation name=""stand"" pos_x=""0"" pos_y=""0"" frame_count=""6"" frame_width=""12"" frame_height=""12"" frame_wait=""0.1"" frames_per_row=""4"" loop=""1"" />
                <RectAnimation name=""walk"" pos_x=""0"" pos_y=""24"" frame_count=""3"" frame_width=""12"" frame_height=""12"" frame_wait=""0.05"" loop=""0"" />
            </Sprite>");
            Assert.Equal("data/enemies_gfx/rat.png", s.Image);
            var stand = s.Find("stand");
            stand.FrameRect(5, out int x, out int y, out int w, out int h);
            Assert.Equal((12, 12, 12, 12), (x, y, w, h));
            Assert.Equal(1, stand.FrameAt(6));
            Assert.Equal(0, stand.FrameAt(36));            // 6 frames x 6 ticks, looped
            var shrunk = NoitaSprite.Parse(@"<Sprite filename=""m.png""><RectAnimation name=""walk"" pos_x=""0"" pos_y=""33"" frame_count=""6"" frame_width=""18"" frame_height=""16"" frames_per_row=""6"" shrink_by_one_pixel=""1"" /></Sprite>").Animations["walk"];
            shrunk.FrameRect(2, out x, out y, out w, out h);
            Assert.Equal((36, 33, 17, 15), (x, y, w, h));   // same 18 px steps, one pixel less drawn
            Assert.Equal(2, s.Find("walk").FrameAt(1000)); // no loop: stays on the last frame
            Assert.Same(stand, s.Find("missing"));
        }

        [Fact]
        public void Translations_LookUpWithFallback()
        {
            var t = NoitaTranslations.Parse("key,en,ru\nanimal_rat,Rat,Крыса\n\"animal_x\",\"Quoted, name\",\n");
            Assert.Equal("Крыса", t.Get("$animal_rat", "ru"));
            Assert.Equal("Quoted, name", t.Get("animal_x", "ru"));
            Assert.Null(t.Get("animal_none", "ru"));
        }

        [Fact]
        public void GeneratedTables_MatchTheSheets()
        {
            Assert.Equal(203, Enemies.All.Length);
            Assert.Equal(Enemies.All.Length, Enemies.All.Select(e => e.Id).Distinct().Count());
            var attackIds = new HashSet<string>(Attacks.All.Select(a => a.Id));
            Assert.All(Enemies.All, e => Assert.All(e.Attacks, a => Assert.Contains(a, attackIds)));
            Assert.Equal(12, Enemies.All.Count(e => e.Stage == "1a"));
        }
    
        [Fact]
        public void Nxml_ACommentBetweenAttributesKeepsTheRest()
        {
            // data/entities/projectiles/deck/glitter_bomb.xml: its shards were lost after this comment
            var root = Nxml.Parse("<config_explosion damage=\"1\" durability=\"11\" <!-- fuse is 11 --> load_this_entity=\"a.xml\" ></config_explosion>");
            var e = root.Children[0];
            Assert.Equal("a.xml", e.Attributes["load_this_entity"]);
            Assert.Equal("11", e.Attributes["durability"]);
        }
    }
}
