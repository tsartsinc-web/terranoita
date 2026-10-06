using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Generated
{
    /// <summary>Lookups over the generated sheet tables, by row id.</summary>
    public static class Defs
    {
        public static readonly Dictionary<string, EnemyDef> Enemy = Enemies.All.ToDictionary(e => e.Id);
        public static readonly Dictionary<string, AttackDef> Attack = Attacks.All.ToDictionary(a => a.Id);
        public static readonly Dictionary<string, ProjectileDef> Projectile = Projectiles.All.ToDictionary(p => p.Id);
        public static readonly Dictionary<string, AiArchetypeDef> Archetype = AiArchetypes.All.ToDictionary(a => a.Id);
        public static readonly Dictionary<string, BalanceDef> Tier = Balance.All.ToDictionary(b => b.Id);
        public static readonly Dictionary<string, TerrariaZoneDef> Zone = TerrariaZones.All.ToDictionary(z => z.Id);
        public static readonly Dictionary<string, BiomeMapDef> Biome = BiomeMap.All.ToDictionary(b => b.Id);

        static readonly string[] StageOrder = { "1a", "1b", "1c", "2", "3", "4" };

        /// <summary>True when a row's stage is built in (at or before) the given stage.</summary>
        public static bool InStage(string rowStage, string built) =>
            Array.IndexOf(StageOrder, rowStage) >= 0 && Array.IndexOf(StageOrder, rowStage) <= Array.IndexOf(StageOrder, built);

        public static IEnumerable<AttackDef> AttacksOf(EnemyDef e) =>
            (e.Attacks ?? new string[0]).Select(id => Attack.TryGetValue(id, out var a) ? a : null).Where(a => a != null);

        /// <summary>Noita damage of an attack as [min, max] over all its damage types.</summary>
        public static void DamageRange(AttackDef a, out float min, out float max)
        {
            min = max = 0;
            if (a.Damage == null)
                return;
            foreach (var v in a.Damage.Values)
            {
                if (v == null || v.Length == 0)
                    continue;
                min += v[0];
                max += v.Length > 1 ? v[1] : v[0];
            }
        }
    }
}
