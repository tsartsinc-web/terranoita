using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terranoita.Physics;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Electricity in conducting liquids (design/effect_interactions.md section 3, on Core's Conduction): Noita's
    /// electricity entities (ElectricityComponent: misc/electricity*.xml, shot by scripts and blasts) and ELECTRIC_CHARGE
    /// impacts charge the connected pool; whoever stands in a charged
    /// tile is electrocuted (creatures: hurt and held, Noita's ELECTROCUTION; the player: Terraria's Electrified, author
    /// 2026-10-08). Only what conducts in Noita conducts (liquids.json conducts). Transient: not saved.
    /// </summary>
    public static class Electricity
    {
        const int Tick = 6;
        // Noita's electricity walks through conducting cells (ElectricityComponent speed 32, energy 1000 by default); ours
        // floods the pool at once. Assumed: energy = Noita pixels it walks -> tiles x PixelScale / 16 (capped by MaxSpread)
        const float Px = Terranoita.Noita.Units.PixelScale;
        // ours: how long a charged tile stays charged after the last emission (effect_electricity.xml frames 40)
        const int ChargeFrames = 40;
        const int ShockFrames = 40;              // Noita: effect_electricity.xml GameEffectComponent frames
        const float NpcDamage = 0.4f * 25f;      // the same as Noita's arc lightning (SpellShots.Arc), per HurtEvery frames
        const int HurtEvery = 10;

        sealed class Grid : IConductGrid
        {
            public bool Conducts(int x, int y) => Fluids.Conducts(x, y);
        }

        static readonly Grid TheGrid = new Grid();
        static Conduction _c;
        static int _frame;

        public static readonly bool Enabled = System.Environment.GetEnvironmentVariable("TERRANOITA_ELECTRICITY") == "1";
        public static int Count => _c?.Count ?? 0;
        public static bool ChargedAt(int x, int y) => _c != null && _c.Charged(x, y);
        public static void Clear() { _c = null; }

        /// <summary>Noita's electricity (its ElectricityComponent energy) at a world position (pixels): charges the
        /// conducting pool it starts in or next to; how many tiles.</summary>
        public static int Emit(Vector2 pos, int energy, int radius = 1)
        {
            // not verified in game yet (PC-4): off unless TERRANOITA_ELECTRICITY=1 (0.4.3)
            if (!Patches.On || !Enabled)
                return 0;
            if (_c == null || _c.Width != Main.maxTilesX)
                _c = new Conduction(Main.maxTilesX);
            int n = _c.Emit(TheGrid, (int)(pos.X / 16), (int)(pos.Y / 16), radius, (int)(energy * Px / 16f), ChargeFrames);
            if (n > 0 && DebugTools.Testing)
                Entry.Log("ELECTRICITY emit at " + (int)(pos.X / 16) + "," + (int)(pos.Y / 16) + ": " + n + " tiles, " + _c.Count + " charged");
            return n;
        }

        public static void Update()
        {
            if (_c == null || _c.Count == 0)
                return;
            _frame++;
            var me = Main.LocalPlayer;
            if (me.active && !me.dead && Touches(me.Hitbox))
                me.AddBuff(BuffID.Electrified, ShockFrames);
            if (_frame % HurtEvery == 0)
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var n = Main.npc[i];
                    if (n.active && !n.friendly && !n.dontTakeDamage && n.life > 0 && Touches(n.Hitbox))
                    {
                        Damage.StrikeAs(n, "electricity", (int)NpcDamage, 0f, 0);
                        Magic.SpellShots.Electrocute(n);
                    }
                }
            if (_frame % Tick != 0)
                return;
            _c.Tick(TheGrid, Tick);
            int sx = (int)Main.screenPosition.X / 16 - 2, sy = (int)Main.screenPosition.Y / 16 - 2;
            int ex = sx + Main.screenWidth / 16 + 4, ey = sy + Main.screenHeight / 16 + 4;
            _c.ForEach((x, y) =>
            {
                if (x < sx || x > ex || y < sy || y > ey)
                    return;
                Lighting.AddLight(x, y, 0.3f, 0.5f, 0.9f);
                if (Main.rand.Next(3) == 0)
                    Dust.NewDustDirect(new Vector2(x * 16, y * 16), 16, 16, DustID.Electric, 0f, 0f, 0, default(Color), 0.6f).noGravity = true;
            });
        }

        static bool Touches(Rectangle box)
        {
            for (int x = box.Left / 16; x <= (box.Right - 1) / 16; x++)
                for (int y = box.Top / 16; y <= (box.Bottom - 1) / 16; y++)
                    if (_c.Charged(x, y))
                        return true;
            return false;
        }
    }
}
