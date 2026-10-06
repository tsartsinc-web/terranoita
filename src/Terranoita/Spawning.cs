using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// After Terraria's own spawn roll, sometimes add a Noita enemy whose Noita locations map (biome_map) to a
    /// Terraria zone the player is in (terraria_zones), weighted by its tier's spawn weight. Single player.
    /// </summary>
    public static class Spawning
    {
        const int MaxNear = 4;            // Noita enemies within 120 tiles of the player
        const int ChancePerFrame = 480;   // ~ one roll every 8 seconds at spawn weight 1
        const int PreHardmodeMaxLife = 1000;  // author: tougher enemies only after the Wall of Flesh

        static List<(EnemyDef def, Func<Player, int, int, bool>[] where)> _pool;

        static void Build()
        {
            _pool = new List<(EnemyDef, Func<Player, int, int, bool>[])>();
            foreach (var e in Enemies.All)
            {
                if (!Defs.InStage(e.Stage, Entry.Stage) || e.SpawnRule != "natural")
                    continue;
                var checks = new List<Func<Player, int, int, bool>>();
                bool tough = e.NoitaHp * Defs.Tier[e.Tier].HpMult > PreHardmodeMaxLife;
                foreach (var loc in e.SpawnIn ?? new string[0])
                {
                    if (!Defs.Biome.TryGetValue(loc, out var b) || !Defs.Zone.TryGetValue(b.Zone, out var z))
                        continue;
                    if (!ZoneChecks.All.TryGetValue(z.Id, out var check) || check == null)
                        continue;
                    var zone = z;
                    checks.Add((p, x, y) => Unlocked(zone) && (!tough || Main.hardMode) && check(p, x, y));
                }
                if (checks.Count > 0)
                    _pool.Add((e, checks.ToArray()));
            }
            Entry.Log("spawn pool: " + string.Join(", ", _pool.Select(p => p.def.Id)));
        }

        static bool Unlocked(TerrariaZoneDef z)
        {
            if (z.Hardmode && !Main.hardMode)
                return false;
            switch (z.AfterBoss)
            {
                case "skeletron": return NPC.downedBoss3;
                case "plantera": return NPC.downedPlantBoss;
                default: return true;
            }
        }

        static void Roll()
        {
            if (Main.netMode != 0 || Main.gameMenu || !NoitaArt.Ready || DebugTools.Testing)
                return;
            if (_pool == null)
                Build();
            var p = Main.LocalPlayer;
            if (!p.active || p.dead || p.townNPCs > 1 || p.ZonePeaceCandle || Main.CurrentFrameFlags.AnyActiveBossNPC)
                return;
            if (Main.rand.Next(ChancePerFrame) != 0)
                return;
            if (Carriers.CountNear(p.Center, 120 * 16) >= MaxNear)
                return;
            TrySpawnNear(p, null);
        }

        /// <summary>Find a free spot just off screen and spawn something that lives there (or the given enemy).</summary>
        public static int TrySpawnNear(Player p, EnemyDef only, int minTiles = 62, int maxTiles = 84)
        {
            if (_pool == null)
                Build();
            int px = (int)(p.Center.X / 16), py = (int)(p.Center.Y / 16);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int x = px + (Main.rand.Next(2) == 0 ? -1 : 1) * Main.rand.Next(minTiles, maxTiles + 1);
                int y = py + Main.rand.Next(-30, 31);
                if (x < 50 || y < 50 || x > Main.maxTilesX - 50 || y > Main.maxTilesY - 50)
                    continue;
                // drop to the floor
                int floor = y;
                while (floor < y + 25 && !Solid(x, floor + 1))
                    floor++;
                bool grounded = Solid(x, floor + 1);
                var options = _pool.Where(o => (only == null || o.def == only) &&
                                               (grounded || o.def.Flies) &&
                                               o.where.Any(w => w(p, x, floor))).ToList();
                if (only != null && options.Count == 0)
                    options = _pool.Where(o => o.def == only).ToList();
                if (options.Count == 0)
                    continue;
                var pick = Weighted(options);
                int bw = pick.Hitbox?[0] ?? 20, bh = pick.Hitbox?[1] ?? 20;
                var box = new Vector2(x * 16 + 8 - bw / 2f, (floor + 1) * 16 - bh);
                if (Collision.SolidCollision(box, bw, bh) || Collision.LavaCollision(box, bw, bh))
                    continue;
                return Carriers.Spawn(pick, x * 16 + 8, (floor + 1) * 16);
            }
            return -1;
        }

        static bool Solid(int x, int y)
        {
            var t = Main.tile[x, y];
            return t != null && t.active() && (Main.tileSolid[t.type] || Main.tileSolidTop[t.type]);
        }

        static EnemyDef Weighted(List<(EnemyDef def, Func<Player, int, int, bool>[] where)> options)
        {
            float total = options.Sum(o => Defs.Tier[o.def.Tier].SpawnWeight);
            float r = (float)Main.rand.NextDouble() * total;
            foreach (var o in options)
            {
                r -= Defs.Tier[o.def.Tier].SpawnWeight;
                if (r <= 0)
                    return o.def;
            }
            return options[options.Count - 1].def;
        }

        [Hook("npc_spawn")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.SpawnNPC))]
        static class SpawnPatch
        {
            static void Postfix() => Roll();
        }
    }
}
