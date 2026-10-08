using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Which tiles the player placed (author: the player's buildings need support). Terraria does not remember it,
    /// so it is kept next to the world file: &lt;world&gt;.wld.terranoita (positions as x + y * maxTilesX).
    /// </summary>
    public static class Placed
    {
        static readonly HashSet<int> Set = new HashSet<int>();

        public static int Count => Set.Count;
        static int Key(int x, int y) => x + y * Main.maxTilesX;
        public static bool Has(int x, int y) => Set.Contains(Key(x, y));
        public static void Add(int x, int y) { lock (SaveSync.Gate) Set.Add(Key(x, y)); }
        public static void Remove(int x, int y) { lock (SaveSync.Gate) Set.Remove(Key(x, y)); }

        static string FilePath => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".terranoita";

        public static void Load()
        {
            lock (SaveSync.Gate)
                Set.Clear();
            var path = FilePath;
            if (path == null || !File.Exists(path))
                return;
            var read = new HashSet<int>();
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    int w = r.ReadInt32(), n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        int k = r.ReadInt32();
                        read.Add(k % w + k / w * Main.maxTilesX);
                    }
                }
                Entry.Log("physics: " + read.Count + " placed tiles read from " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                // a cut file: keep what was read (those tiles are the player's), set the file aside
                Entry.Error("placed load", ex);
                SaveSync.SetAside(path);
            }
            lock (SaveSync.Gate)
                Set.UnionWith(read);
        }

        public static void Save()
        {
            var path = FilePath;
            if (path == null)
                return;
            try
            {
                int[] keys;
                lock (SaveSync.Gate)
                {
                    keys = new int[Set.Count];
                    Set.CopyTo(keys);
                }
                int width = Main.maxTilesX;
                SaveSync.WriteAtomic(path, w =>
                {
                    w.Write(width);
                    w.Write(keys.Length);
                    foreach (int k in keys)
                        w.Write(k);
                });
            }
            catch (Exception ex) { Entry.Error("placed save", ex); }
        }
    }
}
