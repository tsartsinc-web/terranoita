using HarmonyLib;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>Noita damage multipliers on hits against our enemies (design/sheets/systems.json, damage_types).</summary>
    public static class Damage
    {
        [System.ThreadStatic] static string _kind;

        /// <summary>A hit of a Noita damage type (its multipliers and immunities apply), e.g. a creature's explosion.</summary>
        public static void StrikeAs(NPC n, string kind, int damage, float knockback, int dir)
        {
            _kind = kind;
            try { n.StrikeNPCNoInteraction(damage, knockback, dir); }
            finally { _kind = null; }
        }

        [Hook("hit_by_item")]
        [HarmonyPatch(typeof(Player), "ProcessHitAgainstNPC")]
        static class ItemHit
        {
            static void Prefix() => _kind = "melee";
            static void Postfix() => _kind = null;
        }

        [Hook("hit_by_projectile")]
        [HarmonyPatch(typeof(Projectile), "Damage_PVE_Inner")]
        static class ProjectileHit
        {
            // aiStyle 16 = Terraria's bombs, grenades, dynamite and rockets
            static void Prefix(Projectile __instance) =>
                _kind = __instance.melee ? "melee" : __instance.aiStyle == 16 ? "explosion" : "projectile";
            static void Postfix() => _kind = null;
        }

        [Hook("npc_strike")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.StrikeNPC))]
        static class Strike
        {
            static bool Prefix(NPC __instance, ref int Damage, object[] __args, ref int __result)
            {
                // a worm's body: the head takes the hit (its shield, multipliers, hurt sound and death)
                var head = Carriers.HeadOfSegment(__instance);
                if (head != null)
                {
                    if (head.active && Carriers.Get(head) != null)
                        __result = head.StrikeNPC(Damage, 0f, (int)__args[2], (bool)__args[3], (bool)__args[4], (int)__args[5]);
                    return false;
                }
                Hit(__instance, ref Damage);
                return true;
            }

            static void Hit(NPC __instance, ref int Damage)
            {
                var n = Carriers.Get(__instance);
                if (n != null && n.Shield)
                {
                    n.Shield = false;     // a support shield takes the whole hit
                    Damage = 0;
                    return;
                }
                if (n?.Def.DmgMult == null)
                    return;
                string kind = _kind ?? "melee";
                if (n.Def.DmgMult.TryGetValue(kind, out float m) && m != 1f)
                    Damage = m <= 0 ? 0 : System.Math.Max(1, (int)System.Math.Round(Damage * m));
            }
        }
    }
}
