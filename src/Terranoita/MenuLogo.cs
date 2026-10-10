using System;
using Microsoft.Xna.Framework;
using HarmonyLib;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// The main menu logo reads "TERRA NOITA" (author): "NOITA" written over the right end of Terraria's logo,
    /// moving and pulsing with it.
    /// </summary>
    static class MenuLogo
    {
        // Noita's sky behind the main menu (author): data/weather_gfx/parallax_*.png, back to front, drifting slowly
        static readonly (string file, float speed, bool bottom)[] Layers =
        {
            ("data/weather_gfx/parallax_clounds_02.png", 4f, false),
            ("data/weather_gfx/parallax_clounds_01.png", 7f, false),
            ("data/weather_gfx/parallax_mountains_02.png", 2f, true),
            ("data/weather_gfx/parallax_mountains_layer_02.png", 2f, true),
            ("data/weather_gfx/parallax_mountains_01.png", 5f, true),
            ("data/weather_gfx/parallax_mountains_layer_01.png", 5f, true),
        };

        [Hook("menu_background")]
        [HarmonyPatch(typeof(Main), "DrawMenu")]
        static class BackgroundPatch
        {
            // DrawMenu starts with the sprite batch closed, after Terraria drew its own background: ours goes over it
            static void Prefix()
            {
                if (!Main.gameMenu || !NoitaArt.Ready)
                    return;
                var sb = Main.spriteBatch;
                try
                {
                    sb.Begin(Microsoft.Xna.Framework.Graphics.SpriteSortMode.Deferred, Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
                             Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp, null, null, null, Main.UIScaleMatrix);
                    int w = Main.screenWidth, h = Main.screenHeight;
                    var sky = NoitaArt.Get("data/weather_gfx/parallax_background.png")?.Texture;
                    if (sky != null)
                        sb.Draw(sky, new Rectangle(0, 0, w, h), Color.White);
                    float t = (float)Main.GlobalTimeWrappedHourly;
                    foreach (var (file, speed, bottom) in Layers)
                    {
                        var tex = NoitaArt.Get(file)?.Texture;
                        if (tex == null)
                            continue;
                        float s = h / (float)tex.Height;
                        float tw = tex.Width * s;
                        float x0 = -(t * speed % tw);
                        float y = bottom ? h - tex.Height * s : 0f;
                        for (float x = x0; x < w; x += tw)
                            sb.Draw(tex, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, s, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
                    }
                }
                catch (Exception ex) { Entry.Error("menu background", ex); }
                finally { sb.End(); }
            }
        }

        static readonly System.Reflection.FieldInfo LogoScale = AccessTools.Field(typeof(Main), "logoScale");
        static readonly System.Reflection.FieldInfo LogoRotation = AccessTools.Field(typeof(Main), "logoRotation");

        static Vector2 Turn(Vector2 v, float a) =>
            new Vector2(v.X * (float)Math.Cos(a) - v.Y * (float)Math.Sin(a), v.X * (float)Math.Sin(a) + v.Y * (float)Math.Cos(a));

        [Hook("menu_logo")]
        [HarmonyPatch(typeof(Main), "DrawVersionNumber")]
        static class DrawPatch
        {
            // DrawVersionNumber runs inside DrawMenu with the sprite batch open, after the logo is drawn
            static void Postfix()
            {
                try
                {
                    if (!Main.gameMenu)
                        return;
                    var main = Main.instance;
                    float logoScale = (float)LogoScale.GetValue(main), logoRot = (float)LogoRotation.GetValue(main);
                    // lower right corner of the logo, turned with it
                    var pos = new Vector2(Main.screenWidth / 2f, 100f) + Turn(new Vector2(150f, 42f) * logoScale, logoRot);
                    Utils.DrawBorderStringBig(Main.spriteBatch, "NOITA", pos, new Color(255, 196, 64), logoScale * 0.9f, 0.5f, 0.5f);
                    // a splash line under the logo, pulsing like Terraria's (author)
                    float pulse = 1f + 0.06f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);
                    var kick = new Vector2(Main.screenWidth / 2f, 100f) + Turn(new Vector2(0f, 95f) * logoScale, logoRot);
                    Utils.DrawBorderString(Main.spriteBatch, "PRESS F TO KICK GID!", kick, new Color(255, 230, 90), 1.1f * pulse, 0.5f, 0.5f);
                }
                catch (Exception ex) { Entry.Error("menu logo", ex); }
            }
        }
    }
}
