using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// No RE-LOGIC intro: the game opens straight to the main menu (author). The splash is drawn before the other patches
    /// go on (they wait for the menu), so this one is put on by hand at launch: Main.DrawSplash takes Terraria's own
    /// quick path (the one it uses when music is off: no waiting) and its logo timeline is skipped, so only the loading
    /// stars show while the content loads. Hook row skip_splash (patch "call").
    /// </summary>
    static class SkipSplash
    {
        static readonly FieldInfo Counter = AccessTools.Field(typeof(Main), "splashCounter");
        static readonly FieldInfo Quick = AccessTools.Field(typeof(Main), "quickSplash");

        public static void Apply()
        {
            try
            {
                var target = AccessTools.Method(typeof(Main), "DrawSplash");
                new Harmony("gg.melty.terranoita.splash").Patch(target,
                    prefix: new HarmonyMethod(typeof(SkipSplash), nameof(Prefix)),
                    transpiler: new HarmonyMethod(typeof(SkipSplash), nameof(Transpiler)));
                Entry.Log("RE-LOGIC intro skipped");
            }
            catch (Exception ex) { Entry.Log("RE-LOGIC intro not skipped: " + ex.GetBaseException().Message); }
        }

        static void Prefix(Main __instance)
        {
            Quick?.SetValue(__instance, true);
            if (Counter != null && (int)Counter.GetValue(__instance) < 1000)
                Counter.SetValue(__instance, 1000);   // past the logo's fade in, hold and fade out
        }

        // "if (musicVolume == 0f) { quickSplash = true; no wait }": musicVolume reads as 0 here
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var volume = AccessTools.Field(typeof(Main), nameof(Main.musicVolume));
            bool done = false;
            foreach (var ci in code)
            {
                if (!done && ci.LoadsField(volume))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0f) { labels = ci.labels, blocks = ci.blocks };
                    continue;
                }
                yield return ci;
            }
        }
    }
}
