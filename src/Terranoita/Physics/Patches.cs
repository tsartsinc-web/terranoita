using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.IO;

namespace Terranoita.Game.Physics
{
    /// <summary>Stage 2 block physics hooked into Terraria (systems.json block_physics, hooks stage 2).</summary>
    public static class Patches
    {
        /// <summary>On while stage 2 is being built; TERRANOITA_PHYSICS=0 turns it off.</summary>
        public static readonly bool On = Environment.GetEnvironmentVariable("TERRANOITA_PHYSICS") != "0";

        static bool Live => On && !Main.gameMenu && !WorldGen.generatingWorld && Main.netMode == 0;

        // Terraria projectiles that set wood and grass on fire
        static readonly string[] FireProjectiles =
        {
            "FireArrow", "HellfireArrow", "BallofFire", "Flamelash", "Flames", "MolotovCocktail", "MolotovFire",
            "MolotovFire2", "MolotovFire3", "Flamarang", "FlamethrowerTrap", "GreekFire1", "GreekFire2", "GreekFire3",
            "ImpFireball", "Fireball", "CursedFlameFriendly", "CursedFlameHostile", "InfernoFriendlyBolt",
            "InfernoFriendlyBlast", "InfernoHostileBolt", "InfernoHostileBlast", "Spark", "HellfireArrow",
        };
        static bool[] _fiery;

        static bool Fiery(int type)
        {
            if (_fiery == null)
            {
                _fiery = new bool[ProjectileID.Count];
                foreach (var name in FireProjectiles)
                {
                    var f = typeof(ProjectileID).GetField(name);
                    if (f != null)
                        _fiery[Convert.ToInt32(f.GetValue(null))] = true;
                }
            }
            return type >= 0 && type < _fiery.Length && _fiery[type];
        }

        [Hook("tile_kill")]
        [HarmonyPatch(typeof(WorldGen), nameof(WorldGen.KillTile))]
        static class KillTilePatch
        {
            static void Postfix(int i, int j)
            {
                if (!Live)
                    return;
                try
                {
                    var t = Main.tile[i, j];
                    if (t != null && t.active())
                        return;   // only hit, not broken
                    if (PhysicsTest.Enabled && Placed.Has(i, j))
                        Entry.Log("physics: placed tile at " + i + "," + j + " removed by " + new System.Diagnostics.StackTrace().GetFrame(2)?.GetMethod()?.Name);
                    Placed.Remove(i, j);
                    Falling.Disturb(i, j);
                }
                catch (Exception ex) { Entry.Error("tile_kill", ex); }
            }
        }

        [Hook("tile_place")]
        [HarmonyPatch(typeof(WorldGen), nameof(WorldGen.PlaceTile))]
        static class PlaceTilePatch
        {
            static void Postfix(bool __result, int i, int j, int plr)
            {
                if (!Live || !__result || plr < 0)
                    return;
                Placed.Add(i, j);
                Falling.Disturb(i, j);   // placed in the air: it falls
            }
        }

        [Hook("world_load")]
        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.LoadWorld))]
        static class LoadPatch
        {
            static void Postfix()
            {
                Falling.Clear();
                Fire.Clear();
                Status.Clear();
                Fluids.Clear();
                if (On)
                    Placed.Load();
            }
        }

        [Hook("world_save")]
        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.SaveWorld), new[] { typeof(bool), typeof(bool), typeof(bool) })]
        static class SavePatch
        {
            static void Postfix()
            {
                if (On)
                    Placed.Save();
            }
        }

        [Hook("projectile_update")]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Update))]
        static class ProjectilePatch
        {
            static void Postfix(Projectile __instance)
            {
                if (!__instance.active || !Fiery(__instance.type) || !Live)
                    return;
                int x = (int)(__instance.Center.X / 16), y = (int)(__instance.Center.Y / 16);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (Main.rand.Next(4) == 0)
                            Fire.Ignite(x + dx, y + dy);
            }
        }

        [Hook("shots_update")]
        [HarmonyPatch(typeof(Main), "UpdateWorld_Projectiles")]
        static class UpdatePatch
        {
            static void Postfix()
            {
                if (!Live)
                    return;
                try
                {
                    Falling.Update();
                    Fire.Update();
                    Fluids.Update();
                }
                catch (Exception ex) { Entry.Error("physics update", ex); }
            }
        }

        [Hook("fluids_draw")]
        [HarmonyPatch(typeof(Main), "DrawPlayers_AfterProjectiles")]
        static class FluidsDrawPatch
        {
            // in front of the player, like Terraria's water
            static void Postfix()
            {
                if (!Live || Fluids.Count == 0)
                    return;
                var sb = Main.spriteBatch;
                try
                {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                }
                catch (Exception ex) { Entry.Error("fluids draw begin", ex); return; }
                try { Fluids.Draw(sb); }
                catch (Exception ex) { Entry.Error("fluids draw", ex); }
                sb.End();
            }
        }

        [Hook("shots_draw")]
        [HarmonyPatch(typeof(Main), "DrawProjectiles")]
        static class DrawPatch
        {
            static void Postfix()
            {
                if (!Live || Falling.Active == 0)
                    return;
                var sb = Main.spriteBatch;
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                try { Falling.Draw(sb); }
                catch (Exception ex) { Entry.Error("physics draw", ex); }
                sb.End();
            }
        }
    }
}
