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
                }
                catch (Exception ex) { Entry.Error("menu logo", ex); }
            }
        }
    }
}
