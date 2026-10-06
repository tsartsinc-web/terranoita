using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terranoita.Game
{
    /// <summary>Noita-style gold (design/sheets/drops.json): coins by health, sometimes a Heart or a Mana Star.</summary>
    public static class Loot
    {
        static void Drop(NPC npc, NoitaNpc n)
        {
            if (n.Def.Drops != "noita_gold")
                return;
            var src = new EntitySource_Loot(npc);
            int copper = (int)System.Math.Round(n.Def.NoitaHp * 6 * n.Tier.HpMult);   // drops.json noita_gold
            int[] types = { ItemID.PlatinumCoin, ItemID.GoldCoin, ItemID.SilverCoin, ItemID.CopperCoin };
            int[] values = { 1000000, 10000, 100, 1 };
            for (int k = 0; k < types.Length; k++)
            {
                int stack = copper / values[k];
                copper %= values[k];
                if (stack > 0)
                    Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, types[k], stack);
            }
            if (Main.rand.Next(12) == 0)
                Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, ItemID.Heart);
            if (Main.rand.Next(15) == 0)
                Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, ItemID.Star);
        }

        [Hook("npc_loot")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.NPCLoot))]
        static class LootPatch
        {
            static bool Prefix(NPC __instance)
            {
                var n = Carriers.Get(__instance);
                if (n == null)
                    return true;
                Drop(__instance, n);
                Entry.Log("killed " + n.Def.Id + " #" + __instance.whoAmI);
                Carriers.Forget(__instance);
                return false;
            }
        }
    }
}
