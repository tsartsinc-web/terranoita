using System;
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
        // ours: messages a frame. Tiles go as one rectangle per 8x8 area (a blast or a falling building is a few messages,
        // not one per tile): 120 single-tile messages a frame filled the connection and Terraria's TrySendData dropped
        // what did not fit, other players' attacks and broken blocks among them (author's test 2026-10-10, PC-40)
        const int MaxAreasPerFrame = 12, MaxWaterPerFrame = 24, Area = 8;

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
            SendAreas();
            Send(Water, MaxWaterPerFrame, NetMessage.sendWater);
        }

        static readonly Dictionary<int, (int x0, int y0, int x1, int y1)> Areas = new Dictionary<int, (int, int, int, int)>();

        static void SendAreas()
        {
            if (Tiles.Count == 0)
                return;
            Areas.Clear();
            int cols = Main.maxTilesX / Area + 1;
            foreach (int k in Tiles)
            {
                int x = k % Main.maxTilesX, y = k / Main.maxTilesX, a = x / Area + y / Area * cols;
                Areas[a] = Areas.TryGetValue(a, out var r) ? (Math.Min(r.x0, x), Math.Min(r.y0, y), Math.Max(r.x1, x), Math.Max(r.y1, y)) : (x, y, x, y);
            }
            int sent = 0;
            foreach (var kv in Areas)
            {
                if (sent++ >= MaxAreasPerFrame)
                    break;
                var r = kv.Value;
                NetMessage.SendTileSquare(-1, r.x0, r.y0, r.x1 - r.x0 + 1, r.y1 - r.y0 + 1, TileChangeType.None);
                for (int x = r.x0; x <= r.x1; x++)
                    for (int y = r.y0; y <= r.y1; y++)
                        Tiles.Remove(x + y * Main.maxTilesX);
            }
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
