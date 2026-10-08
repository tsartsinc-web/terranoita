using System.Collections.Generic;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class BeamFromEntityTests
    {
        // made-up files in the shapes Noita uses (not Noita's own)
        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["laser.xml"] = @"<Entity><LaserEmitterComponent emit_until_frame=""30"">
  <laser max_length=""256"" beam_radius=""1.5"" damage_to_entities=""0.2"" damage_to_cells=""2000"" max_cell_durability_to_destroy=""7"" beam_particle_type=""spark_red"" audio_enable=""1"" />
</LaserEmitterComponent></Entity>",
            ["bolt.xml"] = @"<Entity><ProjectileComponent /><Entity><LightningComponent is_projectile=""1"" arc_lifetime=""10"">
  <config_explosion explosion_radius=""12"" damage=""0.5"" />
</LightningComponent></Entity></Entity>",
            ["plain.xml"] = @"<Entity><ProjectileComponent /></Entity>",
        };

        static BeamDef Load(string f, ComponentFieldTypes docs = null) =>
            BeamFromEntity.From(NoitaEntityXml.Load(f, p => Files.TryGetValue(p, out var s) ? s : null), docs);

        [Fact]
        public void LaserFieldsAndDocumentedDefaults()
        {
            var b = Load("laser.xml");
            Assert.True(b.HasLaser);
            Assert.False(b.HasLightning);
            Assert.True(b.IsEmitting);                    // not set: documented 1
            Assert.Equal(30, b.EmitUntilFrame);
            Assert.Equal(0f, b.LaserAngleAddRad);
            Assert.Equal(256f, b.MaxLength);
            Assert.Equal(1.5f, b.BeamRadius);
            Assert.Equal(0.2f, b.DamageToEntities);
            Assert.Equal(2000f, b.DamageToCells);
            Assert.Equal(7f, b.MaxCellDurabilityToDestroy);
            Assert.Equal("spark_red", b.BeamParticleType);
            Assert.Equal("1", b.Laser["audio_enable"]);  // every laser.* kept
        }

        [Fact]
        public void LightningInAChildAndDocsFromTheDocumentation()
        {
            var b = Load("bolt.xml");
            Assert.True(b.HasLightning);
            Assert.True(b.IsProjectile);
            Assert.Equal(10, b.ArcLifetime);
            Assert.Equal(1, b.ExplosionType);
            Assert.Equal("data/particles/lightning_ray.png", b.SpriteLightningFile);
            Assert.Equal(12f, b.ExplosionRadius);
            Assert.Equal(0.5f, b.ExplosionDamage);
            var docs = ComponentFieldTypes.Parse(@"LightningComponent
 - Members -----------------------------
    int                     explosion_type                                                  3 [0, 1]                        """"
");
            Assert.Equal(3, Load("bolt.xml", docs).ExplosionType);
            Assert.Null(Load("plain.xml"));
        }

        sealed class Wall : ISolidGrid { public int X; public bool Solid(int x, int y) => x >= X; }

        [Fact]
        public void LightningPathIsJaggedRepeatableAndStopsAtWalls()
        {
            var a = new List<(float x, float y)>();
            var b = new List<(float x, float y)>();
            Assert.False(LightningPath.Build(0, 0, 80, 0, 7, a));
            LightningPath.Build(0, 0, 80, 0, 7, b);
            Assert.Equal(a, b);                           // same seed, same path
            Assert.Equal(11, a.Count);                     // 80 / 8 = 10 segments
            Assert.Equal((0f, 0f), a[0]);
            Assert.Equal((80f, 0f), a[a.Count - 1]);       // ends stay put
            Assert.Contains(a, p => p.y != 0);             // jagged
            Assert.All(a, p => Assert.True(System.Math.Abs(p.y) <= 4.0001f));   // within jitter x segment
            var c = new List<(float x, float y)>();
            LightningPath.Build(0, 0, 80, 0, 8, c);
            Assert.NotEqual(a, c);
            var hit = new List<(float x, float y)>();
            Assert.True(LightningPath.Build(0, 0, 80, 0, 7, hit, new Wall { X = 30 }));
            Assert.True(hit[hit.Count - 1].x >= 30 && hit[hit.Count - 1].x < 31);
            Assert.True(hit.Count < a.Count);
        }
    }
}
