using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Burning tiles (systems.json block_physics, materials.json burns / burn_seconds / burns_to / melts_to): they burn
    /// for burn_seconds, set what touches them on fire, spread to burnable neighbours and melt ice and snow, then go.
    /// Water next to a burning tile puts it out. Lava sets burnable tiles next to it on fire.
    /// </summary>
    public static class Fire
    {
        const int Tick = 6;                    // spread, melt and burn-down are looked at every 6 frames
        const float SpreadChance = 0.07f;      // per burnable neighbour and tick (a wooden wall burns through in ~10 s)
        const float LavaChance = 0.25f;        // per burnable tile next to lava and scan
        const int MaxBurning = 3000;
        // [fire]+ice -> water at 40, [fire]+snow -> water at 80 (materials.xml reactions), per tick
        const float MeltIce = 0.04f, MeltSnow = 0.08f;

        static readonly Dictionary<int, int> Burning = new Dictionary<int, int>();   // tile -> frames left
        static int _frame;

        public static int Count => Burning.Count;
        public static void Clear() => Burning.Clear();

        public static bool Ignite(int x, int y)
        {
            if (!Mats.InWorld(x, y) || Burning.Count >= MaxBurning)
                return false;
            var t = Main.tile[x, y];
            if (!Mats.Burns(t) || Wet(x, y))
                return false;
            int k = x + y * Main.maxTilesX;
            if (Burning.ContainsKey(k))
                return false;
            Burning[k] = Math.Max(Tick, (int)(Mats.Of(t).BurnSeconds * 60));
            return true;
        }

        /// <summary>Set burnable tiles within radius px of pos on fire (fire shots, fiery explosions).</summary>
        public static void IgniteArea(Vector2 pos, float radius, float chance = 1f)
        {
            int r = Math.Max(1, (int)Math.Ceiling(radius / 16f));
            int cx = (int)(pos.X / 16), cy = (int)(pos.Y / 16);
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r && Main.rand.NextFloat() < chance)
                    {
                        Ignite(x, y);
                        if (Mats.InWorld(x, y) && Mats.Melts(Main.tile[x, y]))
                            Melt(x, y);
                    }
        }

        static bool Wet(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var t = Main.tile[x + dx, y + dy];
                    if (t != null && t.liquid >= 32 && t.liquidType() != LiquidID.Lava)
                        return true;
                }
            return false;
        }

        public static void Update()
        {
            _frame++;
            var me = Main.LocalPlayer;
            if (Burning.Count > 0)
            {
                var screen = new Rectangle((int)Main.screenPosition.X / 16 - 2, (int)Main.screenPosition.Y / 16 - 2,
                                           Main.screenWidth / 16 + 4, Main.screenHeight / 16 + 4);
                var meBox = me.Hitbox;
                meBox.Inflate(4, 4);
                foreach (var kv in Burning)
                {
                    int x = kv.Key % Main.maxTilesX, y = kv.Key / Main.maxTilesX;
                    if (!screen.Contains(x, y))
                        continue;
                    Lighting.AddLight(x, y, 0.9f, 0.45f, 0.1f);
                    if (Main.rand.Next(4) == 0)
                    {
                        var d = Dust.NewDustDirect(new Vector2(x * 16, y * 16), 16, 16, DustID.Torch, 0f, -2f, 100, default(Color), 1.6f);
                        d.noGravity = true;
                    }
                    if (Main.rand.Next(40) == 0)
                        Dust.NewDust(new Vector2(x * 16, y * 16 - 8), 16, 8, DustID.Smoke, 0f, -1.5f, 120);
                    if (me.active && !me.dead && meBox.Intersects(new Rectangle(x * 16, y * 16, 16, 16)))
                        me.AddBuff(BuffID.OnFire, 180);
                }
            }
            if (_frame % Tick == 0 && Burning.Count > 0)
                BurnTick();
            if (_frame % 60 == 0)
                LavaScan(me);
        }

        static void BurnTick()
        {
            var done = new List<int>();
            var spread = new List<(int, int)>();
            foreach (var k in Burning.Keys.ToList())
            {
                int x = k % Main.maxTilesX, y = k / Main.maxTilesX;
                var t = Main.tile[x, y];
                if (!Mats.Burns(t) || Wet(x, y))
                {
                    Burning.Remove(k);
                    if (t != null && t.active())
                        for (int s = 0; s < 4; s++)
                            Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0f, -1f, 150);
                    continue;
                }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        var n = Main.tile[x + dx, y + dy];
                        // flames climb: up and sideways more than down
                        float chance = SpreadChance * (dy < 0 ? 1.5f : dy > 0 ? 0.5f : 1f);
                        if (Mats.Burns(n) && Main.rand.NextFloat() < chance)
                            spread.Add((x + dx, y + dy));
                        else if (Mats.Melts(n) && Main.rand.NextFloat() < (n.type == TileID.SnowBlock ? MeltSnow : MeltIce))
                            Melt(x + dx, y + dy);
                    }
                HurtNpcs(x, y);
                if ((Burning[k] -= Tick) <= 0)
                    done.Add(k);
            }
            foreach (var (x, y) in spread)
                Ignite(x, y);
            foreach (var k in done)
            {
                Burning.Remove(k);
                BurnOut(k % Main.maxTilesX, k / Main.maxTilesX);
            }
        }

        static void HurtNpcs(int x, int y)
        {
            var r = new Rectangle(x * 16 - 4, y * 16 - 4, 24, 24);
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && !n.friendly && !n.buffImmune[BuffID.OnFire] && n.Hitbox.Intersects(r))
                    n.AddBuff(BuffID.OnFire, 180);
            }
        }

        static void BurnOut(int x, int y)
        {
            var t = Main.tile[x, y];
            if (t == null || !t.active())
                return;
            ushort to = Mats.BurnsTo(t.type);
            for (int s = 0; s < 3; s++)
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0f, -1f, 100, default(Color), 1.3f);
            if (to != 0)
            {
                t.type = to;
                WorldGen.SquareTileFrame(x, y, true);
                Falling.Disturb(x, y);
            }
            else
                WorldGen.KillTile(x, y, false, false, true);   // burned away: nothing drops; the hook disturbs around it
        }

        static void Melt(int x, int y)
        {
            var t = Main.tile[x, y];
            if (!Mats.Melts(t))
                return;
            Placed.Remove(x, y);
            WorldGen.KillTile(x, y, false, false, true);
            if (t.active())
                return;
            t.liquidType(LiquidID.Water);
            t.liquid = 255;
            Liquid.AddWater(x, y);
            for (int s = 0; s < 3; s++)
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Cloud, 0f, -1f, 150);
        }

        /// <summary>Lava next to burnable tiles (and ice, snow) around the player.</summary>
        static void LavaScan(Player me)
        {
            if (!me.active)
                return;
            int cx = (int)(me.Center.X / 16), cy = (int)(me.Center.Y / 16);
            for (int x = cx - 70; x <= cx + 70; x++)
                for (int y = cy - 45; y <= cy + 45; y++)
                {
                    if (!Mats.InWorld(x, y))
                        continue;
                    var t = Main.tile[x, y];
                    if (t.liquid < 32 || t.liquidType() != LiquidID.Lava)
                        continue;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = Main.tile[x + dx, y + dy];
                            if (Mats.Burns(n) && Main.rand.NextFloat() < LavaChance)
                                Ignite(x + dx, y + dy);
                            else if (Mats.Melts(n) && Main.rand.NextFloat() < LavaChance)
                                Melt(x + dx, y + dy);
                        }
                }
        }
    }
}
