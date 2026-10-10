using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Playing together (PC-38, author's test 2026-10-10): Terraria's Host &amp; Play starts plain TerrariaServer.exe, a
    /// server without the mod. It turned our spell, wand, flask and perk items (deprecated item types the mod revives) to
    /// air in chests and saved the world so, spawned no Noita creatures and knew nothing of our kicks. Host &amp; Play now
    /// starts Terranoita.exe -server instead: the same Terraria with the same mod (the launcher sets Main.dedServ, the one
    /// thing TerrariaServer.exe does differently), its log in server.log.
    /// </summary>
    public static class HostServer
    {
        const string PlainServer = "TerrariaServer.exe", ArgsStart = "-autoshutdown -password \"";

        /// <summary>Our launcher, the program this game runs in.</summary>
        static string Launcher => Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>This process is the world's server (our launcher's -server).</summary>
        public static readonly bool ServerMode = Environment.GetEnvironmentVariable("TERRANOITA_SERVER") == "1";

        static Action _apply;

        /// <summary>Server mode: what Terranoita.exe -server does before Terraria starts. Main.dedServ as TerrariaServer.exe
        /// sets it (its Program.RunGame; the only difference from Terraria.exe), and the mod's patches applied when the
        /// server loop begins (Main.DedServ, before any world loads): the client applies them on Main.OnEngineLoad, which
        /// only the menu's drawing raises.</summary>
        public static void StartServer(Action applyPatches)
        {
            Main.dedServ = true;
            _apply = applyPatches;
            new Harmony("gg.melty.terranoita.server").Patch(AccessTools.Method(typeof(Main), nameof(Main.DedServ)),
                prefix: new HarmonyMethod(typeof(HostServer), nameof(DedServPrefix)),
                postfix: new HarmonyMethod(typeof(HostServer), nameof(DedServPostfix)));
            Entry.Log("server mode: Main.dedServ set; the mod starts with the server loop");
        }

        static void DedServPrefix()
        {
            var apply = _apply;
            _apply = null;
            apply?.Invoke();
            Entry.Log("server: starting with world " + (Main.ActiveWorldFileData?.Path ?? "(none)"));
        }

        /// <summary>The server loop ended (the host left: -autoshutdown; the world is saved by then). TerrariaServer.exe's
        /// Main.Run does nothing after it; Terraria.exe's starts the game window, which failed in PlayerInput.Initialize
        /// ("an item with the same key", the author's screenshot 2026-10-10, 0.4.7): the server process ends here.</summary>
        static void DedServPostfix()
        {
            Entry.Log("server: stopped");
            Environment.Exit(0);
        }

        [Hook("host_and_play")]
        [HarmonyPatch(typeof(Main), "HostAndPlay")]
        static class HostAndPlayPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                int server = 0, args = 0;
                foreach (var c in code)
                {
                    if (c.opcode == OpCodes.Ldstr && (c.operand as string) == PlainServer)
                    {
                        c.operand = Launcher;
                        server++;
                    }
                    else if (c.opcode == OpCodes.Ldstr && (c.operand as string) == ArgsStart)
                    {
                        c.operand = "-server" + (string.IsNullOrEmpty(Entry.NoitaDir) ? "" : " --noita-dir \"" + Entry.NoitaDir + "\"") + " " + ArgsStart;
                        args++;
                    }
                    yield return c;
                }
                // a Terraria that changed this method: Host & Play would start the plain server again (items lost)
                if (server != 1 || args != 1)
                    Entry.Error("host_and_play", new InvalidOperationException("Main.HostAndPlay changed: server name " + server + ", arguments " + args));
                else
                    Entry.Log("Host & Play starts the server with the mod: " + Launcher + " -server");
            }
        }
    }
}
