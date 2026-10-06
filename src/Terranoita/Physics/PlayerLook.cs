using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// How Noita status effects change the player's look (status_effects.json): invisible = not drawn at all (as in
    /// Noita); polymorphed = drawn as the Noita creature (sheep, or a random one, changing for the unstable kind).
    /// </summary>
    public static class PlayerLook
    {
        static bool Hidden(Player p) =>
            p.whoAmI == Main.myPlayer && !Main.gameMenu && (Status.Has("INVISIBILITY") || Status.Form != null);

        [Hook("player_draw")]
        [HarmonyPatch]
        static class DrawPlayerPatch
        {
            // LegacyPlayerRenderer.DrawPlayer(Camera, Player, ...) takes ReLogic types, so it is found by name
            static MethodBase TargetMethod() =>
                AccessTools.GetDeclaredMethods(AccessTools.TypeByName("Terraria.Graphics.Renderers.LegacyPlayerRenderer"))
                    .Where(m => m.Name == "DrawPlayer").OrderByDescending(m => m.GetParameters().Length).First();

            static bool Prefix(object[] __args)
            {
                foreach (var a in __args)
                    if (a is Player p)
                        return !Hidden(p);
                return true;
            }
        }

        [Hook("player_form_draw")]
        [HarmonyPatch(typeof(Main), "DrawPlayers_AfterProjectiles")]
        static class FormDrawPatch
        {
            static void Postfix()
            {
                var p = Main.LocalPlayer;
                if (Main.gameMenu || Status.Form == null || !p.active || p.dead)
                    return;
                var art = NoitaArt.Get(Status.Form);
                if (art?.Texture == null)
                    return;
                var sb = Main.spriteBatch;
                try
                {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                }
                catch (Exception ex) { Entry.Error("form draw begin", ex); return; }
                try
                {
                    bool moving = Math.Abs(p.velocity.X) > 0.5f;
                    var anim = p.velocity.Y != 0 ? art.Sprite.Find("fly", "jump", "walk", "stand")
                             : moving ? art.Sprite.Find("walk", "run", "stand") : art.Sprite.Find("stand", "walk");
                    int fx = 0, fy = 0, fw = art.Texture.Width, fh = art.Texture.Height;
                    if (anim != null)
                        anim.FrameRect(anim.FrameAt((int)Main.GameUpdateCount / 2), out fx, out fy, out fw, out fh);
                    var light = Lighting.GetColor((int)(p.Center.X / 16), (int)(p.Center.Y / 16));
                    float scale = Terranoita.Noita.Units.PixelScale;
                    var pivot = new Vector2(p.Center.X, p.position.Y + p.height - art.Foot * scale + p.gfxOffY) - Main.screenPosition;
                    bool left = p.direction < 0;
                    var origin = new Vector2(left ? fw - art.Sprite.OffsetX : art.Sprite.OffsetX, art.Sprite.OffsetY);
                    sb.Draw(art.Texture, pivot, new Rectangle(fx, fy, fw, fh), light, 0f, origin, scale,
                            left ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
                }
                catch (Exception ex) { Entry.Error("form draw", ex); }
                sb.End();
            }
        }
    }
}
