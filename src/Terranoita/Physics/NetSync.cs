using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Multiplayer (Host &amp; Play runs Terraria's own server; our game is a client, netMode 1): our physics runs in the
    /// client's game, and every tile it changes is sent to the server, which passes it on to the others: blocks as
    /// Terraria's tile square (MessageBuffer case 20, the server takes it as sent), Terraria's water/lava/honey as
    /// its liquid message (48, NetMessage.sendWater). Our own liquids (Fluids cells) stay in each player's game.
    /// Single player: nothing is queued.
    /// </summary>
    public static class NetSync
    {
        const int MaxPerFrame = 120;   // ours: tile messages per frame; the rest goes on the next frames

        static readonly HashSet<int> Tiles = new HashSet<int>(), Water = new HashSet<int>();
        static readonly List<int> Sending = new List<int>();

        static bool Client => Main.netMode == 1;   // multiplayer client

        /// <summary>A block, wall or frame our physics changed at x,y.</summary>
        public static void Tile(int x, int y)
        {
            if (Client && Mats.InWorld(x, y))
                Tiles.Add(x + y * Main.maxTilesX);
        }

        /// <summary>Terraria's liquid our physics changed at x,y: Terraria's own Liquid.AddWater plus the message.</summary>
        public static void AddWater(int x, int y)
        {
            Liquid.AddWater(x, y);
            if (Client && Mats.InWorld(x, y))
                Water.Add(x + y * Main.maxTilesX);
        }

        public static void Clear()
        {
            Tiles.Clear();
            Water.Clear();
        }

        /// <summary>Sends what changed (called once per frame after the physics update).</summary>
        public static void Flush()
        {
            if (!Client)
            {
                Clear();
                return;
            }
            int budget = MaxPerFrame;
            budget = Send(Tiles, budget, (x, y) => NetMessage.SendTileSquare(-1, x, y, 1, TileChangeType.None));
            Send(Water, budget, NetMessage.sendWater);
        }

        static int Send(HashSet<int> set, int budget, System.Action<int, int> send)
        {
            if (set.Count == 0 || budget <= 0)
                return budget;
            Sending.Clear();
            foreach (int k in set)
            {
                if (Sending.Count >= budget)
                    break;
                Sending.Add(k);
            }
            foreach (int k in Sending)
            {
                set.Remove(k);
                send(k % Main.maxTilesX, k / Main.maxTilesX);
            }
            return budget - Sending.Count;
        }
    }
}
