using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Terranoita.Launcher
{
    /// <summary>
    /// Terranoita.exe sits next to the player's Terraria.exe (Melty installs it there; no game file is changed).
    /// It loads Terraria.exe into this process, lets Terranoita.Game.dll apply its Harmony patches, then runs
    /// Terraria's own entry point. Starting Terraria.exe directly still gives plain Terraria.
    ///
    ///   Terranoita.exe --noita-dir "&lt;Noita folder&gt;" [Terraria arguments...]
    ///
    /// Things to confirm on the 1.4.5.8 decompile before the first run (see design/sheets/hooks.json, systems.json):
    ///   - Terraria.exe is x86 .NET Framework 4 + XNA 4 (this exe must match: x86);
    ///   - the entry point is Terraria.WindowsLaunch.Main(string[]);
    ///   - how Terraria resolves its embedded libraries, and what Steam start-up needs when we start it.
    /// </summary>
    static class Program
    {
        const string TerrariaSteamAppId = "105600";

        [STAThread]
        static int Main(string[] args)
        {
            // developer tool: Terranoita.exe --list-noita-sounds "<Noita folder>" out.txt
            if (args.Length >= 3 && args[0] == "--list-noita-sounds")
            {
                using (var fmod = Terranoita.Noita.NoitaFmod.Open(args[1],
                    new[] { "animals.bank", "projectiles.bank", "explosion.bank", "player.bank", "items.bank", "misc.bank" }, Console.WriteLine))
                    File.WriteAllLines(args[2], fmod.EventPaths());
                return 0;
            }
            string here = AppDomain.CurrentDomain.BaseDirectory;
            // -server: the world's server for playing together (Host & Play starts us so; the mod's Host & Play patch):
            // Terraria.exe as TerrariaServer.exe runs it (that build differs only by Main.dedServ = true before the game
            // starts), with the same mod in it. Its own log, so the host's game log stays.
            bool server = Array.IndexOf(args, "-server") >= 0;
            Log.Open(server ? "server" : "latest", server ? "server_previous" : "previous");
            Log.Write("Terranoita launcher " + typeof(Program).Assembly.GetName().Version + ", folder " + here);
            try
            {
                string noitaDir = null;
                var passThrough = new System.Collections.Generic.List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--noita-dir" && i + 1 < args.Length)
                        noitaDir = args[++i];
                    else
                        passThrough.Add(args[i]);
                }
                noitaDir = noitaDir ?? Environment.GetEnvironmentVariable("TERRANOITA_NOITA_DIR");
                Log.Write("Noita folder: " + (noitaDir ?? "(not given)"));

                string terrariaExe = Path.Combine(here, "Terraria.exe");
                if (!File.Exists(terrariaExe))
                    throw new FileNotFoundException("Terraria.exe is not next to Terranoita.exe", terrariaExe);

                // Steam: tell the Steam API which game this process is. Steam still checks the player owns Terraria.
                if (Environment.GetEnvironmentVariable("SteamAppId") == null)
                    Environment.SetEnvironmentVariable("SteamAppId", TerrariaSteamAppId);
                if (Environment.GetEnvironmentVariable("SteamGameId") == null)
                    Environment.SetEnvironmentVariable("SteamGameId", TerrariaSteamAppId);
                Environment.CurrentDirectory = here;

                // Load it by name (the default load context, Terraria.exe is in our folder) so that Terranoita.Game.dll's
                // reference to Terraria binds to this same copy. LoadFrom would leave a second copy for the mod to patch.
                Assembly terraria = Assembly.Load(new AssemblyName("Terraria"));
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) => ResolveEmbedded(terraria, e.Name);
                Log.Write("Loaded " + terraria.FullName);

                if (server)
                {
                    // the mod sets Main.dedServ where TerrariaServer.exe does (Program.RunGame): Main must not be touched
                    // before Terraria has its save folder (Main's static setup reads it)
                    Environment.SetEnvironmentVariable("TERRANOITA_SERVER", "1");
                    passThrough.Remove("-server");
                    Log.Write("server mode");
                }
                StartMod(here, terraria, noitaDir);

                MethodInfo entry = terraria.GetType("Terraria.WindowsLaunch", true)
                    .GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (entry == null)
                    throw new MissingMethodException("Terraria.WindowsLaunch", "Main");
                Log.Write("Starting Terraria");
                entry.Invoke(null, new object[] { passThrough.ToArray() });
                Log.Write("Terraria exited");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("FATAL " + (ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex));
                return 1;
            }
            finally
            {
                Log.Close();
            }
        }

        /// <summary>Load Terranoita.Game.dll (compiled against Terraria.exe) and let it patch the game.</summary>
        static void StartMod(string here, Assembly terraria, string noitaDir)
        {
            string gameDll = Path.Combine(here, "Terranoita.Game.dll");
            if (!File.Exists(gameDll))
            {
                Log.Write("Terranoita.Game.dll missing: starting plain Terraria");
                return;
            }
            Assembly mod = Assembly.Load(new AssemblyName("Terranoita.Game"));
            int copies = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Terraria");
            Log.Write("Terraria assemblies loaded: " + copies);
            MethodInfo start = mod.GetType("Terranoita.Game.Entry", true).GetMethod("Start", BindingFlags.Static | BindingFlags.Public);
            start.Invoke(null, new object[] { terraria, noitaDir, (Action<string>)Log.Write });
        }

        /// <summary>Terraria.exe ships some libraries as embedded resources; serve them if the runtime asks us.</summary>
        static Assembly ResolveEmbedded(Assembly terraria, string fullName)
        {
            string name = new AssemblyName(fullName).Name;
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (loaded != null)
                return loaded;
            string resource = terraria.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith("." + name + ".dll", StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                return null;
            using (var s = terraria.GetManifestResourceStream(resource))
            {
                var bytes = new byte[s.Length];
                int read = 0;
                while (read < bytes.Length)
                    read += s.Read(bytes, read, bytes.Length - read);
                Log.Write("Resolved " + name + " from Terraria.exe resource " + resource);
                return Assembly.Load(bytes);
            }
        }
    }

    /// <summary>%LOCALAPPDATA%/Terranoita/logs/latest.log, kept small and readable for players and for Melty.</summary>
    static class Log
    {
        static StreamWriter _w;
        static readonly object Gate = new object();

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "logs");

        public static void Open(string name = "latest", string previous = "previous")
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string latest = Path.Combine(Folder, name + ".log");
                if (File.Exists(latest))
                    File.Copy(latest, Path.Combine(Folder, previous + ".log"), true);
                _w = new StreamWriter(latest, false) { AutoFlush = true };
            }
            catch (Exception)
            {
                _w = null;
            }
        }

        public static void Write(string line)
        {
            lock (Gate)
            {
                string text = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line;
                try { _w?.WriteLine(text); } catch (Exception) { }
                Console.WriteLine(text);
            }
        }

        public static void Close()
        {
            lock (Gate)
            {
                _w?.Dispose();
                _w = null;
            }
        }
    }
}
