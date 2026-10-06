using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terranoita.Generated;

namespace Terranoita.Game
{
    /// <summary>Marks a Harmony patch class with the hook row it implements (design/sheets/hooks.json).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HookAttribute : Attribute
    {
        public readonly string Id;
        public HookAttribute(string id) { Id = id; }
    }

    /// <summary>Called by Terranoita.exe after Terraria.exe is loaded and before Terraria starts.</summary>
    public static class Entry
    {
        public static Action<string> Log = _ => { };
        public static string NoitaDir;

        public static void Start(Assembly terraria, string noitaDir, Action<string> log)
        {
            Log = log ?? Log;
            NoitaDir = noitaDir;
            var harmony = new Harmony("gg.melty.terranoita");
            harmony.PatchAll(typeof(Entry).Assembly);

            // Oracle: every hook the sheet lists for a built stage must have a patch class, and the reverse.
            var implemented = typeof(Entry).Assembly.GetTypes()
                .Select(t => t.GetCustomAttribute<HookAttribute>()?.Id).Where(id => id != null).ToList();
            foreach (var h in Hooks.All.Where(h => h.Patch != "call"))
                Log((implemented.Contains(h.Id) ? "hook ok      " : "hook MISSING ") + h.Id + " -> " + h.Target);
            foreach (var id in implemented.Where(id => Hooks.All.All(h => h.Id != id)))
                Log("hook not in the sheet: " + id);
            Log("Terranoita patches applied: " + harmony.GetPatchedMethods().Count() + " methods");
        }
    }
}
