using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_ITEMS=1|2 (with TERRANOITA_AUTOTEST=1): can an unused item type carry a wand or a spell
    /// through saving? Run 1 puts candidates (stack = an id) in the inventory and a chest and saves; run 2 reads them
    /// back. Lines start with "ITEMS".
    /// </summary>
    public static class ItemSpike
    {
        public static readonly string Phase = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_ITEMS");
        public static bool Enabled => Phase == "1" || Phase == "2";
        static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "item_spike.txt");

        static void Log(string s) => Entry.Log("ITEMS " + s);

        public static void Frame(Player p, int frame)
        {
            if (frame == 120 && Phase == "1")
                Put(p);
            if (frame == 100 && Phase == "2")
                Candidates();
            if (frame == 120 && Phase == "2")
                Read(p);
        }

        public static int[] Candidates()
        {
            // placeholders without a name; no longer deprecated, so loading keeps them
            var list = new[] { 6143, 3847, 3848 };
            foreach (int i in list)
                ItemID.Sets.Deprecated[i] = false;
            return list;
        }

        static void Put(Player p)
        {
            var c = Candidates();
            int x = (int)(p.Center.X / 16) - 6, y = (int)((p.position.Y + p.height) / 16) - 1;
            int chest = -1;
            for (int dx = 2; dx < 40 && chest < 0; dx++)
                foreach (int sx in new[] { dx, -dx })
                    for (int dy = -4; dy <= 4 && chest < 0; dy++)
                        if ((chest = WorldGen.PlaceChest((int)(p.Center.X / 16) + sx, y + dy)) >= 0) { x = (int)(p.Center.X / 16) + sx; y += dy; break; }
            Log("chest " + chest + " at " + x + "," + y);
            for (int k = 0; k < c.Length; k++)
            {
                var it = p.inventory[20 + k];
                it.SetDefaults(c[k]);
                it.stack = 300 + k;
                it.prefix = (byte)(200 + k);
                Log("put type " + c[k] + " stack " + it.stack + " maxStack " + it.maxStack + " name '" + it.Name + "' into slot " + (20 + k));
                if (chest >= 0)
                {
                    Main.chest[chest].item[k].SetDefaults(c[k]);
                    Main.chest[chest].item[k].stack = 400 + k;
                    Main.chest[chest].item[k].prefix = (byte)(210 + k);
                }
            }
            File.WriteAllText(StateFile, string.Join(",", c) + ";" + chest + ";" + x + "," + y);
            Terraria.Player.SavePlayer(Main.ActivePlayerFileData);
            Terraria.IO.WorldFile.SaveWorld();
            Log("saved");
        }

        static void Read(Player p)
        {
            if (!File.Exists(StateFile))
            {
                Log("no state file");
                return;
            }
            var parts = File.ReadAllText(StateFile).Split(';');
            var c = parts[0].Split(',').Select(int.Parse).ToArray();
            for (int k = 0; k < c.Length; k++)
            {
                var it = p.inventory[20 + k];
                Log("slot " + (20 + k) + ": type " + it.type + " prefix " + it.prefix + " stack " + it.stack + " (put " + c[k] + " x" + (300 + k) + ")");
            }
            int chest = int.Parse(parts[1]);
            var xy = parts[2].Split(',');
            int found = Chest.FindChest(int.Parse(xy[0]), int.Parse(xy[1]) - 1);
            Log("chest then " + chest + ", found now " + found);
            for (int ci = 0; ci < Main.maxChests && found < 0; ci++)
                if (Main.chest[ci] != null && Main.chest[ci].item.Any(i => c.Contains(i.type)))
                    found = ci;
            if (found >= 0)
                for (int k = 0; k < c.Length; k++)
                    Log("chest item " + k + ": type " + Main.chest[found].item[k].type + " prefix " + Main.chest[found].item[k].prefix + " stack " + Main.chest[found].item[k].stack + " (put " + c[k] + " x" + (400 + k) + ")");
        }
    }
}
