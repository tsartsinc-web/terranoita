using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Burning tiles and background walls (systems.json block_physics, materials.json burns / burn_seconds / burns_to /
    /// melts_to / terraria_walls): they burn for burn_seconds, set what touches them on fire, spread to burnable
    /// neighbours (blocks and walls), melt ice and snow, then go. Water next to them puts them out. Lava and whoever is
    /// on fire set them alight.
    /// </summary>
    public static class Fire
    {
        const int Tick = 6;                    // spread, melt and burn-down are looked at every 6 frames
        const float SpreadChance = 0.07f;      // per burnable neighbour and tick (a wooden wall burns through in ~10 s)
        const float WallFactor = 0.7f;         // fire creeps along background walls a little slower
        const float LavaChance = 0.25f;        // per burnable tile next to lava and scan
        const int MaxBurning = 4000;
        // [fire]+ice -> water at 40, [fire]+snow -> water at 80 (materials.xml reactions), per tick
        const float MeltIce = 0.04f, MeltSnow = 0.08f;

        // key: (x + y * maxTilesX) * 2 + layer (0 = block, 1 = wall) -> frames left
        static readonly Dictionary<int, int> Burning = new Dictionary<int, int>();
        static int _frame;

        public static int Count => Burning.Count;
        public static bool BurningAt(int x, int y) => Burning.ContainsKey(Key(x, y, false)) || Burning.ContainsKey(Key(x, y, true));
        public static void Clear() => Burning.Clear();

        static int Key(int x, int y, bool wall) => (x + y * Main.maxTilesX) * 2 + (wall ? 1 : 0);

        static bool CanBurn(Tile t, bool wall) => wall ? Mats.WallBurns(t) : Mats.Burns(t);

        public static bool Ignite(int x, int y, bool wall = false)
        {
            if (!Mats.InWorld(x, y) || Burning.Count >= MaxBurning)
                return false;
            var t = Main.tile[x, y];
            if (!CanBurn(t, wall) || Wet(x, y))
                return false;
            int k = Key(x, y, wall);
            if (Burning.ContainsKey(k))
                return false;
            var m = wall ? Mats.OfWall(t) : Mats.Of(t);
            Burning[k] = Math.Max(Tick, (int)(m.BurnSeconds * 60));
            return true;
        }

        /// <summary>Blocks and walls in this cell.</summary>
        static void IgniteCell(int x, int y, float chance)
        {
            if (Main.rand.NextFloat() < chance)
                Ignite(x, y);
            if (Main.rand.NextFloat() < chance)
                Ignite(x, y, true);
        }

        /// <summary>Set burnable tiles within radius px of pos on fire (fire shots, fiery explosions).</summary>
        public static void IgniteArea(Vector2 pos, float radius, float chance = 1f)
        {
            int r = Math.Max(1, (int)Math.Ceiling(radius / 16f));
            int cx = (int)(pos.X / 16), cy = (int)(pos.Y / 16);
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    {
                        IgniteCell(x, y, chance);
                        if (Mats.InWorld(x, y) && Mats.Melts(Main.tile[x, y]) && Main.rand.NextFloat() < chance)
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
                    int cell = kv.Key / 2, x = cell % Main.maxTilesX, y = cell / Main.maxTilesX;
                    bool wall = kv.Key % 2 == 1;
                    if (!screen.Contains(x, y))
                        continue;
                    Lighting.AddLight(x, y, wall ? 0.6f : 0.9f, wall ? 0.3f : 0.45f, 0.1f);
                    if (Main.rand.Next(wall ? 7 : 4) == 0)
                    {
                        var d = Dust.NewDustDirect(new Vector2(x * 16, y * 16), 16, 16, DustID.Torch, 0f, -2f, 100, default(Color), wall ? 1.2f : 1.6f);
                        d.noGravity = true;
                    }
                    if (Main.rand.Next(40) == 0)
                        Dust.NewDust(new Vector2(x * 16, y * 16 - 8), 16, 8, DustID.Smoke, 0f, -1.5f, 120);
                    if (me.active && !me.dead && meBox.Intersects(new Rectangle(x * 16, y * 16, 16, 16)))
                        Status.Apply("ON_FIRE", 3);
                }
            }
            if (_frame % Tick == 0 && Burning.Count > 0)
                BurnTick();
            if (_frame % Tick == 0)
                BurningCreatures(me);
            if (_frame % 60 == 0)
                LavaScan(me);
        }

        static void BurnTick()
        {
            var done = new List<int>();
            var spread = new List<(int, int, bool)>();
            // the creatures fire can still catch, once per tick (not every burning tile x every NPC slot)
            var targets = new List<NPC>();
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && !n.friendly && !n.dontTakeDamage && !n.buffImmune[BuffID.OnFire])
                    targets.Add(n);
            }
            foreach (var k in Burning.Keys.ToList())
            {
                int cell = k / 2, x = cell % Main.maxTilesX, y = cell / Main.maxTilesX;
                bool wall = k % 2 == 1;
                var t = Main.tile[x, y];
                if (!CanBurn(t, wall) || Wet(x, y))
                {
                    Burning.Remove(k);
                    if (t != null && (t.active() || t.wall > 0))
                        for (int s = 0; s < 4; s++)
                            Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0f, -1f, 150);
                    continue;
                }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var n = Main.tile[x + dx, y + dy];
                        // flames climb: up and sideways more than down
                        float chance = SpreadChance * (dy < 0 ? 1.5f : dy > 0 ? 0.5f : 1f);
                        if (dx == 0 && dy == 0)
                            chance = SpreadChance * 2;   // the block and the wall behind it light each other
                        if (Mats.Burns(n) && (dx != 0 || dy != 0 || wall) && Main.rand.NextFloat() < chance * (wall ? WallFactor : 1f))
                            spread.Add((x + dx, y + dy, false));
                        if (Mats.WallBurns(n) && (dx != 0 || dy != 0 || !wall) && Main.rand.NextFloat() < chance * WallFactor)
                            spread.Add((x + dx, y + dy, true));
                        if ((dx != 0 || dy != 0) && Mats.Melts(n) && Main.rand.NextFloat() < (n.type == TileID.SnowBlock ? MeltSnow : MeltIce))
                            Melt(x + dx, y + dy);
                    }
                if (targets.Count > 0)
                    HurtNpcs(x, y, targets);
                if ((Burning[k] -= Tick) <= 0)
                    done.Add(k);
            }
            foreach (var (x, y, wall) in spread)
                Ignite(x, y, wall);
            foreach (var k in done)
            {
                Burning.Remove(k);
                int cell = k / 2;
                if (k % 2 == 1)
                    WallBurnOut(cell % Main.maxTilesX, cell / Main.maxTilesX);
                else
                    BurnOut(cell % Main.maxTilesX, cell / Main.maxTilesX);
            }
        }

        /// <summary>Like in Noita, whoever is on fire lights the burnable tiles they touch.</summary>
        static void BurningCreatures(Player me)
        {
            if (me.active && !me.dead && me.onFire)
                IgniteTouching(me.Hitbox);
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && n.onFire)
                    IgniteTouching(n.Hitbox);
            }
        }

        static void IgniteTouching(Rectangle box)
        {
            box.Inflate(2, 2);
            for (int x = box.Left / 16; x <= box.Right / 16; x++)
                for (int y = box.Top / 16; y <= box.Bottom / 16; y++)
                    IgniteCell(x, y, SpreadChance * 2);
        }

        /// <summary>Creatures touching the burning tile catch fire; one caught is not looked at again this tick.</summary>
        static void HurtNpcs(int x, int y, List<NPC> targets)
        {
            var r = new Rectangle(x * 16 - 4, y * 16 - 4, 24, 24);
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var n = targets[i];
                if (n.Hitbox.Intersects(r))
                {
                    n.AddBuff(BuffID.OnFire, 180);
                    targets.RemoveAt(i);
                }
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
                NetSync.Tile(x, y);
                Falling.Disturb(x, y);
            }
            else if (!Mats.HoldsItems(x, y))
            {
                WorldGen.KillTile(x, y, false, false, true);   // burned away: nothing drops; the hook disturbs around it
                NetSync.Tile(x, y);
            }
        }

        static void WallBurnOut(int x, int y)
        {
            var t = Main.tile[x, y];
            if (t == null || t.wall == 0)
                return;
            for (int s = 0; s < 2; s++)
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0f, -1f, 100, default(Color), 1.1f);
            t.wall = 0;            // burned away: nothing drops
            WorldGen.SquareWallFrame(x, y, true);
            NetSync.Tile(x, y);
        }

        static void Melt(int x, int y)
        {
            var t = Main.tile[x, y];
            if (!Mats.Melts(t) || Mats.HoldsItems(x, y))
                return;
            Placed.Remove(x, y);
            WorldGen.KillTile(x, y, false, false, true);
            NetSync.Tile(x, y);
            if (t.active())
                return;
            t.liquidType(LiquidID.Water);
            t.liquid = 255;
            NetSync.AddWater(x, y);
            for (int s = 0; s < 3; s++)
                Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Cloud, 0f, -1f, 150);
        }

        /// <summary>Lava next to burnable blocks and walls (and ice, snow) around the player.</summary>
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
                            if (Mats.Melts(n) && Main.rand.NextFloat() < LavaChance)
                                Melt(x + dx, y + dy);
                            else
                                IgniteCell(x + dx, y + dy, LavaChance);
                        }
                }
        }
    }
}
