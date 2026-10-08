using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Terranoita.Generated;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    /// <summary>A projectile made from an entity file at runtime matches the sheet row apply_spells.py made from the same values.</summary>
    public class SpellProjectileFromEntityTests
    {
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "1" : "0";

        /// <summary>An entity file holding exactly what the row says (made up from the row, not Noita's file).</summary>
        static string XmlOf(SpellProjectileDef r)
        {
            var sb = new StringBuilder("<Entity>\n");
            sb.Append($"  <ProjectileComponent speed_min=\"{F(r.SpeedMin)}\" speed_max=\"{F(r.SpeedMax)}\" direction_random_rad=\"{F(r.SpreadRad)}\" " +
                      $"lifetime=\"{r.Lifetime}\" lifetime_randomness=\"{r.LifetimeRandom}\" damage=\"{F(r.Damage)}\" " +
                      $"explosion_dont_damage_shooter=\"{B(!r.HurtsShooter)}\" damage_every_x_frames=\"{r.DamageEveryFrames}\" " +
                      $"on_death_explode=\"{B(r.ExplodeOnDeath)}\" on_collision_die=\"{B(r.DieOnHit)}\" penetrate_entities=\"{B(r.Penetrate)}\" " +
                      $"bounces_left=\"{r.Bounces}\" collide_with_world=\"{B(r.CollideWithWorld)}\" knockback_force=\"{F(r.Knockback)}\">\n");
            sb.Append($"    <config_explosion explosion_radius=\"{F(r.ExplosionRadius)}\" damage=\"{F(r.ExplosionDamage)}\"" +
                      (r.ExplosionMaterial != "none" ? $" create_cell_material=\"{r.ExplosionMaterial}\"" : "") +
                      (r.ExplosionSound != "none" ? $" audio_event_name=\"{r.ExplosionSound}\"" : "") + " />\n");
            sb.Append($"    <damage_by_type fire=\"{F(r.FireDamage)}\" slice=\"{F(r.TypedDamage - r.FireDamage)}\" />\n");
            sb.Append("  </ProjectileComponent>\n");
            sb.Append($"  <VelocityComponent gravity_y=\"{F(r.Gravity)}\" air_friction=\"{F(r.AirFriction)}\" />\n");
            if (r.Sprite != "none") sb.Append($"  <SpriteComponent image_file=\"{r.Sprite}\" />\n");
            if (r.Audio != "none") sb.Append($"  <AudioComponent event_root=\"{r.Audio}\" />\n");
            if (r.Material != "none") sb.Append($"  <ParticleEmitterComponent emitted_material_name=\"{r.Material}\" />\n");
            if (r.EatRadius > 0) sb.Append($"  <CellEaterComponent radius=\"{F(r.EatRadius)}\" eat_probability=\"{F(r.EatProbability)}\" />\n");
            if (r.AreaDamage > 0) sb.Append($"  <AreaDamageComponent damage_per_frame=\"{F(r.AreaDamage)}\" aabb_max.x=\"{F(r.AreaHalf)}\" />\n");
            return sb.Append("</Entity>\n").ToString();
        }

        [Theory]
        [InlineData("data/entities/projectiles/deck/light_bullet.xml")]
        [InlineData("data/entities/projectiles/bomb.xml")]
        [InlineData("data/entities/projectiles/deck/black_hole.xml")]
        [InlineData("data/entities/projectiles/deck/black_hole_giga.xml")]
        [InlineData("data/entities/projectiles/deck/arrow.xml")]
        [InlineData("data/entities/projectiles/deck/fireball.xml")]
        public void MatchesTheSheetRow(string id)
        {
            var row = SpellProjectiles.All.Single(r => r.Id == id);
            string xml = XmlOf(row);
            var made = SpellProjectileFromEntity.From(NoitaEntityXml.Load(id, p => p == id ? xml : null));
            Assert.NotNull(made);
            foreach (var f in typeof(SpellProjectileDef).GetFields())
            {
                object want = f.GetValue(row), got = f.GetValue(made);
                if (want is float w)
                    Assert.True(System.Math.Abs(w - (float)got) < 1e-4f, f.Name + ": " + w + " vs " + got);
                else
                    Assert.True(Equals(want, got), f.Name + ": " + want + " vs " + got);
            }
        }

        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["cloud.xml"] = "<Entity><VelocityComponent /><SpriteComponent image_file=\"c.png\" /></Entity>",
            ["base.xml"] = @"<Entity>
  <ProjectileComponent speed_min=""100"" lifetime=""30"" damage=""0.5"">
    <config_explosion explosion_radius=""8"" damage=""1"" audio_event_name=""explosions/x"" />
  </ProjectileComponent>
  <VelocityComponent />
  <AudioComponent event_root=""projectiles"" />
  <AudioComponent event_root=""projectiles/base"" />
</Entity>",
            ["child.xml"] = @"<Entity>
  <Base file=""base.xml""><ProjectileComponent damage=""0.8"" /></Base>
  <SpriteComponent image_file="""" /><SpriteComponent image_file=""shot.png"" />
  <Entity><ParticleEmitterComponent emitted_material_name="""" /></Entity>
  <Entity><ParticleEmitterComponent emitted_material_name=""blood"" /></Entity>
</Entity>",
            ["kid_velocity.xml"] = @"<Entity><ProjectileComponent />
  <Entity><VelocityComponent air_friction=""2"" /></Entity></Entity>",
        };

        static SpellProjectileDef Load(string f) => SpellProjectileFromEntity.From(NoitaEntityXml.Load(f, p => Files.TryGetValue(p, out var s) ? s : null));

        [Fact]
        public void NoProjectileComponentIsNull() => Assert.Null(Load("cloud.xml"));

        [Fact]
        public void BaseChainAndTheFactsDefaults()
        {
            var d = Load("child.xml");
            Assert.Equal(100f, d.SpeedMin);
            Assert.Equal(60f, d.SpeedMax);              // not set: ProjectileComponent's documented default (facts)
            Assert.Equal(0.8f, d.Damage);               // overridden inside <Base>
            Assert.Equal(30, d.Lifetime);
            Assert.Equal(8f, d.ExplosionRadius);
            Assert.Equal("explosions/x", d.ExplosionSound);
            Assert.Equal(400f, d.Gravity);              // the entity's own VelocityComponent without gravity_y
            Assert.Equal("shot.png", d.Sprite);         // the first SpriteComponent with an image
            Assert.Equal("projectiles/base", d.Audio);  // the most specific audio folder
            Assert.Equal("none", d.Material);           // the first emitter wins even without a material (shot_row)
            Assert.Equal(100f, d.EatProbability);       // no CellEater: 100
            Assert.Equal("child.xml", d.Id);
            Assert.True(d.DieOnHit && d.CollideWithWorld && !d.HurtsShooter && !d.Penetrate);
        }

        [Fact]
        public void VelocityOnlyInAChildHasNoGravityFallback()
        {
            var d = Load("kid_velocity.xml");
            Assert.Equal(0f, d.Gravity);                // facts gave none: the entity itself has no VelocityComponent
            Assert.Equal(2f, d.AirFriction);            // but the first VelocityComponent anywhere gives its fields
            Assert.Equal(-1, d.Lifetime);
            Assert.Equal(60f, d.SpeedMin);
        }
    }
}
