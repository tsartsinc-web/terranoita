using System.Collections.Generic;
using System.Linq;
using Terranoita.Generated;

namespace Terranoita.Spells
{
    /// <summary>Spells and wands from the generated stage 3 tables (spells.json, wands.json).</summary>
    public static class FromSheets
    {
        public static Spell Spell(SpellDef d) => new Spell
        {
            Id = d.Id, Type = d.Type, Mana = d.Mana, MaxUses = d.MaxUses,
            Projectiles = d.Projectiles ?? new string[0],
            Triggers = d.TriggerKind == null || d.TriggerKind == "none" ? new Trigger[0]
                : new[] { new Trigger { Kind = d.TriggerKind, File = d.TriggerFile, Draws = d.TriggerDraws, Frames = d.TriggerFrames } },
            Draws = d.Draws, ReloadAdd = d.ReloadAdd,
            ConfigAdd = d.ConfigAdd != null ? new Dictionary<string, float>(d.ConfigAdd) : new Dictionary<string, float>(),
            ConfigMul = d.ConfigMul != null ? new Dictionary<string, float>(d.ConfigMul) : new Dictionary<string, float>(),
        };

        public static Wand Wand(WandDef d, IEnumerable<SpellDef> slots = null) => new Wand
        {
            Shuffle = d.Shuffle, SpellsPerCast = d.SpellsPerCast, CastDelay = d.CastDelay, RechargeTime = d.RechargeTime,
            ManaMax = d.ManaMax, ManaChargePerSecond = d.ManaChargeSpeed, Capacity = d.Capacity,
            Spread = d.Spread, SpeedMultiplier = d.SpeedMultiplier,
            Slots = (slots ?? Enumerable.Empty<SpellDef>()).Select(s => s == null ? null : Spell(s)).ToList(),
        };
    }
}
