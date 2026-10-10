using System.Collections.Generic;

namespace Terranoita.Noita
{
    /// <summary>
    /// The world as Noita's spell scripts ask about it (gun_actions.lua: the caster, enemies and projectiles near it,
    /// its health and gold, the held wand's spells, globals). Entity 1 is the caster; positions are Noita pixels.
    /// The game answers from Terraria; the default is an empty world (tools and tests).
    /// </summary>
    public class LuaWorld
    {
        public const int Caster = 1;

        /// <summary>Entities with a Noita tag: player_unit, homing_target (enemies), projectile, black_hole_giga...</summary>
        public virtual List<int> WithTag(string tag) => tag == "player_unit" ? new List<int> { Caster } : new List<int>();
        public virtual List<int> InRadiusWithTag(float x, float y, float radius, string tag) => new List<int>();
        public virtual bool HasTag(int entity, string tag) => entity == Caster && tag == "player_unit";
        public virtual string Name(int entity) => "";
        public virtual (float x, float y) Position(int entity) => (0, 0);

        /// <summary>Caster's health in Noita units (Terraria hp / 25), as DamageModelComponent hp / max_hp.</summary>
        public virtual float Hp { get => 4; set { } }
        public virtual float MaxHp => 4;
        /// <summary>Caster's Noita gold, as WalletComponent money / money_spent.</summary>
        public virtual float Money { get => 0; set { } }
        public virtual float MoneySpent { get => 0; set { } }
        /// <summary>Damage to an entity, Noita units.</summary>
        public virtual void Damage(int entity, float amount, string type) { }
        /// <summary>EntityLoad: a Noita entity file put in the world (summons, loaders).</summary>
        public virtual int Load(string file, float x, float y) => 0;
        /// <summary>Spells (action ids) in the held wand, for the scripts that look at it.</summary>
        public virtual List<string> WandSpells() => new List<string>();
        /// <summary>HasFlagPersistent: Noita's unlocks. Everything is unlocked here.</summary>
        public virtual bool Flag(string flag) => true;

        public readonly Dictionary<string, string> Globals = new Dictionary<string, string>();
    }
}
