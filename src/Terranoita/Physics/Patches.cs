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

        // single player and, in multiplayer, our client (Terraria's own server runs none of this: NetSync sends our changes)
        // multiplayer physics (NetSync, PC-19) is not verified in game yet: off unless TERRANOITA_MP_PHYSICS=1 (0.4.3)
        static readonly bool MpPhysics = Environment.GetEnvironmentVariable("TERRANOITA_MP_PHYSICS") == "1";
        static bool Live => On && !Main.gameMenu && !WorldGen.generatingWorld && (Main.netMode == 0 || Main.netMode == 1 && MpPhysics);

        // Terraria projectiles that set wood and grass on fire: the ones Terraria's own hit code (Projectile.StatusNPC,
        // read with ilspycmd) gives On Fire / Hellfire / Cursed Inferno, and fire weapons that burn in other ways
        // (author: "the flare gun and every fire weapon"). Shadowflame and frostburn are not fire here.
        static readonly string[] FireProjectiles =
        {
            "FireArrow", "HellfireArrow", "BallofFire", "Flamelash", "Flames", "MolotovCocktail", "MolotovFire",
            "MolotovFire2", "MolotovFire3", "Flamarang", "FlamethrowerTrap", "FlamesTrap", "GreekFire1", "GreekFire2", "GreekFire3",
            "ImpFireball", "Fireball", "CursedFlameFriendly", "CursedFlameHostile", "InfernoFriendlyBolt",
            "InfernoFriendlyBlast", "InfernoHostileBolt", "InfernoHostileBlast", "Spark", "Sunfury",
            "Flare", "BlueFlare", "SpelunkerFlare", "CursedFlare", "RainbowFlare", "ShimmerFlare",
            "CursedArrow", "CursedBullet", "CursedDart", "CursedDartFlame", "ClingerStaff", "Cascade", "CascadeExplosion",
            "HelFire", "DD2FlameBurstTowerT1Shot", "DD2FlameBurstTowerT2Shot", "DD2FlameBurstTowerT3Shot",
            "DD2PhoenixBowShot", "FlamingMace", "Volcano", "LavaBoulder", "Hellwing", "Daybreak", "SolarWhipSword",
            "SolarWhipSwordExplosion", "DD2BetsyFireball", "DD2BetsyFlameBreath", "FireWhip", "FireWhipProj",
            "FlamingJack", "FlamingScythe", "Meteor1", "Meteor2", "Meteor3",
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
                    ToxicGround.Remove(i, j);
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
                try
                {
                    Placed.Add(i, j);
                    Falling.Disturb(i, j);   // placed in the air: it falls
                }
                catch (Exception ex) { Entry.Error("tile_place", ex); }
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
                Electricity.Clear();
                NetSync.Clear();
                Magic.SpellShots.Clear();
                Magic.Flasks.Clear();
                try { Magic.WorldLoot.Load(); }
                catch (Exception ex) { Entry.Error("world loot", ex); }
                Status.Clear();
                Fluids.Clear();
                ToxicGround.Clear();
                if (!On)
                    return;
                // never let our files or pools stop a world from loading
                try { Placed.Load(); }
                catch (Exception ex) { Entry.Error("placed load", ex); }
                try
                {
                    // a world without our liquids yet (new or old) gets its cave pools once; a 0.3.0 world the extra ones
                    Fluids.Load();
                    if (Fluids.PoolsVersion < 2)
                        CavePools.Generate();
                    if (Fluids.PoolsVersion < 3)
                        CavePools.Banks();
                    Fluids.PoolsVersion = CavePools.Version;
                }
                catch (Exception ex) { Entry.Error("cave pools", ex); }
            }
        }

        [Hook("player_spawn")]
        [HarmonyPatch(typeof(Player), nameof(Player.Spawn), new[] { typeof(PlayerSpawnContext) })]
        static class SpawnPatch
        {
            // author: a respawned player starts clean, without the effects that killed them
            static void Postfix(Player __instance)
            {
                if (On && __instance.whoAmI == Main.myPlayer)
                    Status.Clear();
            }
        }

        [Hook("world_save")]
        [HarmonyPatch(typeof(WorldFile), nameof(WorldFile.SaveWorld), new[] { typeof(bool), typeof(bool), typeof(bool) })]
        static class SavePatch
        {
            static void Postfix()
            {
                try { Magic.WandWindow.SaveSlots(); Magic.WandStore.Save(); Magic.WorldLoot.Save(); }
                catch (Exception ex) { Entry.Error("magic save", ex); }
                if (!On)
                    return;
                try { Placed.Save(); Fluids.Save(); }
                catch (Exception ex) { Entry.Error("physics save", ex); }
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

        /// <summary>A fire projectile dies where it hit (a fire arrow in a wooden wall, a fireball on grass): the burnable
        /// blocks and walls around it catch fire (author). Projectile.Update's postfix never sees it: it is gone by then.</summary>
        [Hook("projectile_kill")]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Kill))]
        static class ProjectileKillPatch
        {
            static void Prefix(Projectile __instance)
            {
                if (!__instance.active || !Fiery(__instance.type) || !Live)
                    return;
                try { Fire.IgniteArea(__instance.Center, 20f, 0.8f); }
                catch (Exception ex) { Entry.Error("fire projectile", ex); }
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
                    // what is saved changes only under this lock (the autosave copies it from another thread)
                    lock (SaveSync.Gate)
                    {
                        if (PerfTest.Enabled)
                            PerfTest.Update(() => { Falling.Update(); Fire.Update(); Fluids.Update(); Electricity.Update(); });
                        else
                        {
                            SlowFrames.Time("falling", Falling.Update);
                            SlowFrames.Time("fire", Fire.Update);
                            SlowFrames.Time("fluids", Fluids.Update);
                            SlowFrames.Time("electricity", Electricity.Update);
                        }
                        NetSync.Flush();
                        ToxicGround.Touch(Main.LocalPlayer);
                    }
                }
                catch (Exception ex) { Entry.Error("physics update", ex); }
            }
        }

        [Hook("mouse_over")]
        [HarmonyPatch(typeof(Main), "DrawMouseOver")]
        static class MouseOverPatch
        {
            // where Terraria shows a sign's text under the mouse: the liquid's name (or ???)
            static void Postfix()
            {
                if (!Live)
                    return;
                try { Fluids.HoverName(); }
                catch (Exception ex) { Entry.Error("mouse over", ex); }
            }
        }

        [Hook("cursor_draw")]
        [HarmonyPatch(typeof(Main), "DrawInterface_36_Cursor")]
        static class CursorPatch
        {
            static void Postfix()
            {
                if (!Live)
                    return;
                try { Fluids.DrawHoverText(); }
                catch (Exception ex) { Entry.Error("hover text", ex); }
            }
        }

        [Hook("fluids_draw")]
        [HarmonyPatch(typeof(Main), "DrawPlayers_AfterProjectiles")]
        static class FluidsDrawPatch
        {
            // in front of the player, like Terraria's water
            static void Postfix()
            {
                if (!Live || Fluids.Count == 0 && ToxicGround.Count == 0)
                    return;
                var sb = Main.spriteBatch;
                try
                {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                }
                catch (Exception ex) { Entry.Error("fluids draw begin", ex); return; }
                try { ToxicGround.Draw(sb); }
                catch (Exception ex) { Entry.Error("toxic draw", ex); }
                try
                {
                    if (PerfTest.Enabled) PerfTest.Draw(() => Fluids.Draw(sb));
                    else Fluids.Draw(sb);
                }
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
