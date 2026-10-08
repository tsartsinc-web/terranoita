using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// What our spell runtime runs of a Noita projectile entity (design/magic_plan.md Phase 0): the component types
    /// that have code (SpellShots*.cs, LuaShotScripts), the ProjectileComponent fields that code reads and the
    /// ConfigGunActionInfo fields it applies. One list for the game's log ("not run yet") and `tncli magic-coverage`.
    /// Hand-kept until Phase 2 replaces it with the system registry: add a name here in the same change that gives it code.
    /// "Has code" is not "complete": the Phase 1 test against Noita decides that.
    /// </summary>
    public static class SpellRuntime
    {
        /// <summary>Component types with code in the game or Core (2026-10-09 audit of SpellShots*.cs and LuaShotScripts.cs).</summary>
        public static readonly HashSet<string> Components = new HashSet<string>(StringComparer.Ordinal)
        {
            "ArcComponent", "AreaDamageComponent", "AudioComponent", "BlackHoleComponent", "CellEaterComponent",
            "CollisionTriggerComponent", "ElectricityComponent", "EnergyShieldComponent", "GameEffectComponent",
            "HomingComponent", "InheritTransformComponent", "LifetimeComponent", "LightComponent", "LightningComponent",
            "LuaComponent", "MagicConvertMaterialComponent", "MaterialSeaSpawnerComponent", "ParticleEmitterComponent",
            "PhysicsImageShapeComponent", "ProjectileComponent", "SineWaveComponent", "SpriteComponent",
            "SpriteParticleEmitterComponent", "TeleportProjectileComponent", "VariableStorageComponent", "VelocityComponent",
        };

        /// <summary>Component types that only hold data for scripts or the card item and need no behaviour of a shot:
        /// the card in the world (custom_cards: ItemComponent, ItemActionComponent, SimplePhysicsComponent,
        /// SpriteOffsetAnimatorComponent), UI text (UIInfoComponent).</summary>
        public static readonly HashSet<string> NoBehaviour = new HashSet<string>(StringComparer.Ordinal)
        {
            "ItemComponent", "ItemActionComponent", "SimplePhysicsComponent", "SpriteOffsetAnimatorComponent", "UIInfoComponent",
        };

        /// <summary>Top-level ProjectileComponent fields our code reads (2026-10-09 audit).</summary>
        public static readonly HashSet<string> ProjectileFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "lifetime", "damage", "on_lifetime_out_explode", "speed_min", "speed_max", "on_collision_die", "on_death_explode",
            "explosion_dont_damage_shooter", "direction_random_rad", "knockback_force", "lifetime_randomness",
            "die_on_low_velocity", "bounces_left", "damage_every_x_frames", "friendly_fire", "penetrate_entities",
            "die_on_liquid_collision", "bounce_energy", "penetrate_world", "die_on_low_velocity_limit", "collide_with_world",
        };

        /// <summary>ProjectileComponent fields that only change how a shot looks or sounds (their meaning from
        /// component_documentation.txt): counted apart from behaviour.</summary>
        public static readonly HashSet<string> ProjectileLookFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "muzzle_flash_file", "shoot_light_flash_radius", "shoot_light_flash_r", "shoot_light_flash_g", "shoot_light_flash_b",
            "camera_shake_when_shot", "create_shell_casing", "ragdoll_fx_on_collision", "ragdoll_force_multiplier",
            "hit_particle_force_multiplier", "velocity_sets_scale", "velocity_sets_scale_coeff", "velocity_updates_animation",
            "velocity_sets_rotation", "velocity_sets_y_flip", "angular_velocity", "bounce_fx_file", "ground_collision_fx",
            "on_death_emit_particle", "on_death_emit_particle_type", "on_death_emit_particle_count",
            "on_death_particle_check_concrete", "on_death_gfx_leave_sprite",
        };

        /// <summary>ConfigGunActionInfo fields (gun.lua's c.*) the shots apply. fire_rate_wait and screenshake are
        /// the cast's (LuaGun); damage_projectile / damage_explosion are not ConfigGunActionInfo fields (Noita ignores them).</summary>
        public static readonly HashSet<string> ConfigFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "bounces", "damage_critical_chance", "damage_electricity_add", "damage_explosion_add", "damage_ice_add",
            "damage_null_all", "damage_projectile_add", "explosion_radius", "extra_entities", "friendly_fire",
            "game_effect_entities", "gravity", "knockback_force", "lifetime_add", "lightning_count", "material",
            "pattern_degrees", "speed_multiplier", "spread_degrees", "trail_material", "trail_material_amount",
            "fire_rate_wait", "screenshake", "damage_projectile", "damage_explosion",
        };

        /// <summary>Component types in the entity and its child entities that have no code, sorted, each once.</summary>
        public static List<string> NotRun(XmlEntity e)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            void Walk(XmlEntity x)
            {
                foreach (var c in x.Components)
                    if (!Components.Contains(c.Type) && !NoBehaviour.Contains(c.Type))
                        found.Add(c.Type);
                foreach (var ch in x.Children)
                    Walk(ch);
            }
            if (e != null)
                Walk(e);
            return found.ToList();
        }

        /// <summary>Top-level ProjectileComponent fields the entity sets that our code does not read; look fields
        /// apart (look = true lists those, false the behaviour ones).</summary>
        public static List<string> UnreadFields(XmlEntity e, bool look = false)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            void Walk(XmlEntity x)
            {
                foreach (var c in x.Components.Where(c => c.Type == "ProjectileComponent"))
                    foreach (var f in c.Fields.Keys)
                        if (!f.StartsWith("_", StringComparison.Ordinal) && f.IndexOf('.') < 0 && !ProjectileFields.Contains(f) &&
                            ProjectileLookFields.Contains(f) == look)
                            found.Add(f);
                foreach (var ch in x.Children)
                    Walk(ch);
            }
            if (e != null)
                Walk(e);
            return found.ToList();
        }
    }
}
