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
        /// <summary>
        /// The latest stage this build contains. TERRANOITA_STAGE=1b tries the next stage's enemies in a test run
        /// before it ships (its sheet rows must be complete: preflight --gate 1b).
        /// </summary>
        public static readonly string Stage = Environment.GetEnvironmentVariable("TERRANOITA_STAGE") ?? "1b";

        public static Action<string> Log = _ => { };
        public static string NoitaDir;

        static Harmony _harmony;
        static readonly System.Collections.Generic.HashSet<string> Reported = new System.Collections.Generic.HashSet<string>();

        /// <summary>Log an exception from a patch once per place and kind (Terraria swallows them silently).</summary>
        /// <summary>Errors so far (tests see whether one happened during a step).</summary>
        public static int Errors;

        public static void Error(string where, Exception ex)
        {
            Errors++;
            if (Reported.Add(where + ex.GetType().Name))
                Log("ERROR in " + where + ": " + ex);
        }

        /// <summary>Log a warning once.</summary>
        public static void Warn(string message)
        {
            if (Reported.Add(message))
                Log("WARN " + message);
        }

        /// <summary>ReLogic.OS.Platform.Get&lt;IPathService&gt;().GetStoragePath("Terraria"), as Program.LaunchGame does.
        /// ReLogic is embedded in Terraria.exe, so it is reached by reflection.</summary>
        static string DefaultSavePath()
        {
            try
            {
                var platform = Type.GetType("ReLogic.OS.Platform, ReLogic", true);
                var service = Type.GetType("ReLogic.OS.IPathService, ReLogic", true);
                object paths = platform.GetMethod("Get").MakeGenericMethod(service).Invoke(null, null);
                return (string)service.GetMethod("GetStoragePath", new[] { typeof(string) }).Invoke(paths, new object[] { "Terraria" });
            }
            catch (Exception ex)
            {
                Log("ReLogic storage path unavailable (" + ex.GetBaseException().Message + "), using My Games");
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria");
            }
        }

        public static void Start(Assembly terraria, string noitaDir, Action<string> log)
        {
            Log = log ?? Log;
            NoitaDir = noitaDir;
            NoitaArt.Open(noitaDir);
            // Patching JIT-compiles Terraria methods, and that runs the static constructors of the types they use
            // (Main, CaptureManager...), which need the game set up. So patch once the engine has loaded
            // (Main.OnEngineLoad, first menu frame). Subscribing runs Main's static constructor, which reads
            // Program.SavePath; Terraria sets that later in LaunchGame, so set it first exactly as it will.
            var args = Environment.GetCommandLineArgs();
            int sd = Array.FindIndex(args, a => string.Equals(a, "-savedirectory", StringComparison.OrdinalIgnoreCase));
            Terraria.Program.SavePath = sd >= 0 && sd + 1 < args.Length ? args[sd + 1] : DefaultSavePath();
            Log("Terraria save folder: " + Terraria.Program.SavePath);
            if (HostServer.ServerMode)
                HostServer.StartServer(ApplyPatches);   // the world's server (Terranoita.exe -server): no menu, no engine load
            else
            {
                Terraria.Main.OnEngineLoad += ApplyPatches;
                SkipSplash.Apply();
            }
        }

        static void ApplyPatches()
        {
            Magic.MagicItems.Init();
            if (!Terraria.Main.dedServ)
                TerranoitaNet.Register();   // Terraria registered its modules in Initialize already (the server: NetworkInitializer.Load postfix)   // before any player or world is loaded: Terraria keeps the spell and wand items
            var harmony = _harmony = new Harmony("gg.melty.terranoita");
            harmony.PatchAll(typeof(Entry).Assembly);
            NoitaArt.Preload();
            NoitaSound.Open(NoitaDir);

            // Oracle: every hook the sheet lists must have a patch class, and the reverse. PatchAll applies every patch
            // whatever the stage (Stage only picks the creatures), so every hook is checked.
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
