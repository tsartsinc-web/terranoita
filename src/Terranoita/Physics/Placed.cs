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
        public static void Add(int x, int y) => Set.Add(Key(x, y));
        public static void Remove(int x, int y) => Set.Remove(Key(x, y));

        static string FilePath => string.IsNullOrEmpty(Main.worldPathName) ? null : Main.worldPathName + ".terranoita";

        public static void Load()
        {
            Set.Clear();
            var path = FilePath;
            if (path == null || !File.Exists(path))
                return;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    int w = r.ReadInt32(), n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        int k = r.ReadInt32();
                        Set.Add(k % w + k / w * Main.maxTilesX);
                    }
                }
                Entry.Log("physics: " + Set.Count + " placed tiles read from " + Path.GetFileName(path));
            }
            catch (Exception ex) { Entry.Error("placed load", ex); }
        }

        public static void Save()
        {
            var path = FilePath;
            if (path == null)
                return;
            try
            {
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    w.Write(Main.maxTilesX);
                    w.Write(Set.Count);
                    foreach (int k in Set)
                        w.Write(k);
                }
            }
            catch (Exception ex) { Entry.Error("placed save", ex); }
        }
    }
}
