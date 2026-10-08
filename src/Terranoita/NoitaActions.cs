using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game
{
    /// <summary>
    /// Noita's own player actions (author): F kicks (creatures, things on the ground, the cart), holding down while
    /// standing in a liquid drinks it (Noita's ingestion: the liquid's ingestion statuses, too much hurts), and the cart
    /// of Noita's start, put next to the player the first time a world is entered.
    /// Numbers: KickComponent kick_damage 1/25 Noita = 1 hp (docs); IngestionComponent of player_base.xml capacity 7500,
    /// overingestion_damage 0.002 per cell (x25 hp). Kick reach, force and drink speed are not in Noita's data: ours.
    /// </summary>
    public static class NoitaActions
    {
        const float Px = Noita.Units.PixelScale;
        const float KickReach = 3 * 16;      // ours: Noita's kick_radius is 3 px around the foot, too small at our scale
        const float KickForce = 7f;          // ours
        const int DrinkPerFrame = 12;        // ours: liquid units (of 255 a tile) a frame
        const float Capacity = 7500, OverDamage = 0.002f * 25f;
        static float _stomach;               // Noita's ingestion_size, in cells
        static int _kickCooldown, _drinkSound;

        public static void Update()
        {
            var p = Main.LocalPlayer;
            if (Main.gameMenu || p == null || !p.active || p.dead)
                return;
            if (_kickCooldown > 0)
                _kickCooldown--;
            bool typing = Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput;
            if (!typing && Main.keyState.IsKeyDown(Keys.F) && !Main.oldKeyState.IsKeyDown(Keys.F) && _kickCooldown == 0)
                Kick(p);
            if (!typing && p.controlDown && p.velocity.Y == 0)
                Drink(p);
            // the stomach empties slowly (Noita: ingestion_reduce_every_n_frame 5)
            if (_stomach > 0 && Main.GameUpdateCount % 5 == 0)
                _stomach--;
            Cart.Update(p);
        }

        // ---- kick ----

        static void Kick(Player p)
        {
            _kickCooldown = 20;
            var foot = new Vector2(p.Center.X + p.direction * (p.width / 2f + 8), p.position.Y + p.height - 10);
            if (!NoitaSound.Play("player/kick", foot))
                Terraria.Audio.SoundEngine.PlaySound(SoundID.Item1, foot);
            p.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Full, -MathHelper.PiOver2 * p.direction);
            var push = new Vector2(p.direction * KickForce, -2.5f);
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly || n.townNPC || n.life <= 0 || Vector2.Distance(n.Center, foot) > KickReach + n.width / 2f)
                    continue;
                p.ApplyDamageToNPC(n, 1, KickForce, p.direction, false, null, 0, -1);   // kick_damage 1/25 Noita = 1 hp
                n.velocity += push * (n.knockBackResist > 0 ? n.knockBackResist : 0.2f);
            }
            for (int i = 0; i < Main.maxItems; i++)
            {
                var it = Main.item[i];
                if (it.active && Vector2.Distance(it.Center, foot) <= KickReach)
                    it.velocity += push;
            }
            Cart.Kick(foot, push);
        }

        // ---- drink ----

        static void Drink(Player p)
        {
            int x = (int)(p.Center.X / 16), y = (int)((p.position.Y + p.height - 4) / 16);
            string liquid = Physics.Fluids.Drink(x, y, DrinkPerFrame) ?? Physics.Fluids.Drink(x, y - 1, DrinkPerFrame);
            if (liquid == null)
                return;
            float cells = DrinkPerFrame / 255f * 16;   // a tile is 16 Terraria px tall: about 16 Noita cells of it
            _stomach += cells;
            var def = Liquids.All.FirstOrDefault(l => l.Id == liquid);
            if (def != null)
            {
                Physics.Fluids.Learn(def.Id);
                foreach (var ing in def.Ingestion ?? new string[0])
                {
                    var parts = ing.Split(':');
                    float amount = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float a) ? a : 0.1f;
                    Physics.Status.Apply(parts[0], amount * cells);   // Noita: the amount per cell eaten
                }
            }
            if (liquid == "lava")
                p.AddBuff(BuffID.OnFire, 120);
            if (_stomach > Capacity / 16f)   // too much (our tiles hold ~16 cells a unit row)
                p.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(p.name + " drank too much."), (int)Math.Ceiling(OverDamage * cells), 0);
            if (--_drinkSound <= 0)
            {
                _drinkSound = 20;
                if (!NoitaSound.Play("player/drink", p.Center))
                    Terraria.Audio.SoundEngine.PlaySound(SoundID.Item3, p.Center);
            }
        }
    }
}
