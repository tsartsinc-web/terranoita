using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.UI.ResourceSets;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// More mana for Noita's dear spells (author: giga spells cost 500-600): 15 Mana Crystals instead of 10 (300 base),
    /// the total cap 600 instead of 400. The bar keeps 15 stars that hold 20 mana each, up to 40 at 600 (author:
    /// "15 x 40"). Terraria's constants are changed in place (transpilers): crystal use, player load, Player.Update.
    /// </summary>
    public static class ManaCap
    {
        public const int CrystalCap = 300, TotalCap = 600, Stars = 15;
        const int VanillaCrystalCap = 200, VanillaTotalCap = 400;

        /// <summary>Mana a star holds: 20 up to 300 mana, then the 15 stars share it (40 each at 600).</summary>
        public static float PerStar(int manaMax) => Math.Max(20f, (float)Math.Ceiling(manaMax / (float)Stars));
        public static int PerStarLocal() => (int)PerStar(Main.LocalPlayer?.statManaMax2 ?? 20);

        /// <summary>`ldfld field; ldc.i4 from` and the next `ldc.i4 from` close after it (the clamp's store) become `to`.</summary>
        static IEnumerable<CodeInstruction> Bump(IEnumerable<CodeInstruction> instructions, FieldInfo field, int from, int to, string what)
        {
            var code = instructions.ToList();
            int changed = 0;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!code[i].LoadsField(field) || !code[i + 1].LoadsConstant(from))
                    continue;
                code[i + 1].opcode = OpCodes.Ldc_I4; code[i + 1].operand = to; changed++;
                for (int j = i + 2; j < Math.Min(code.Count, i + 7); j++)
                    if (code[j].LoadsConstant(from))
                    {
                        code[j].opcode = OpCodes.Ldc_I4; code[j].operand = to; changed++;
                        break;
                    }
            }
            Entry.Log("mana cap: " + what + " " + from + " -> " + to + " (" + changed + " constants)");
            return code;
        }

        static readonly FieldInfo ManaMax = AccessTools.Field(typeof(Player), nameof(Player.statManaMax));
        static readonly FieldInfo ManaMax2 = AccessTools.Field(typeof(Player), nameof(Player.statManaMax2));
        static readonly FieldInfo Mana = AccessTools.Field(typeof(Player), nameof(Player.statMana));

        [Hook("mana_crystal_cap")]
        [HarmonyPatch(typeof(Player), "ItemCheck_UseManaCrystal")]
        static class CrystalPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                Bump(code, ManaMax, VanillaCrystalCap, CrystalCap, "crystals");
        }

        [Hook("mana_load_cap")]
        [HarmonyPatch]
        static class LoadPatch
        {
            static MethodBase TargetMethod() => AccessTools.Method(typeof(Player), "Deserialize");
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                Bump(Bump(code, ManaMax, VanillaCrystalCap, CrystalCap, "load crystals"), Mana, VanillaTotalCap, TotalCap, "load mana");
        }

        [Hook("mana_total_cap")]
        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        static class TotalPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                Bump(code, ManaMax2, VanillaTotalCap, TotalCap, "total");
        }

        // the classic bar sets UIDisplay_ManaPerStar = 20 at the top of DrawMana: ours instead
        [Hook("mana_stars_classic")]
        [HarmonyPatch(typeof(ClassicPlayerResourcesDisplaySet), "DrawMana")]
        static class ClassicPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                var perStar = AccessTools.Field(typeof(ClassicPlayerResourcesDisplaySet), "UIDisplay_ManaPerStar");
                for (int i = 0; i + 1 < code.Count; i++)
                    if (code[i].LoadsConstant(20) && code[i + 1].StoresField(perStar))
                    {
                        code[i].opcode = OpCodes.Call;
                        code[i].operand = AccessTools.Method(typeof(ManaCap), nameof(PerStarLocal));
                        Entry.Log("mana cap: classic stars");
                        break;
                    }
                return code;
            }
        }

        // the fancy and bar styles read the snapshot's ManaPerSegment
        [Hook("mana_stars_snapshot")]
        [HarmonyPatch]
        static class SnapshotPatch
        {
            static MethodBase TargetMethod() => AccessTools.Constructor(typeof(PlayerStatsSnapshot), new[] { typeof(Player) });
            static void Postfix(ref PlayerStatsSnapshot __instance) => __instance.ManaPerSegment = PerStar(__instance.ManaMax);
        }
    }
}
