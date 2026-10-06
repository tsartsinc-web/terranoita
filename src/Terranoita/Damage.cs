using HarmonyLib;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>Noita damage multipliers on hits against our enemies (design/sheets/systems.json, damage_types).</summary>
    public static class Damage
    {
        [System.ThreadStatic] static string _kind;

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
            static void Prefix(NPC __instance, ref int Damage)
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
