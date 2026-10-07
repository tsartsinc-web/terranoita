using System;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Tests: a picture of one drawn frame (%LOCALAPPDATA%/Terranoita/shots/name.png). Terraria draws in XNA's Reach
    /// profile, which cannot read the back buffer, so for that one frame everything meant for the screen goes into a
    /// render target of ours instead, which is then saved and shown.
    /// </summary>
    public static class Screenshot
    {
        static string _pending;
        static RenderTarget2D _target;
        static bool _capturing;

        public static void Request(string name) => _pending = name;

        [Hook("test_screenshot")]
        [HarmonyPatch(typeof(Main), "DoDraw")]
        static class DrawPatch
        {
            static void Prefix()
            {
                if (_pending == null)
                    return;
                try
                {
                    var gd = Main.instance.GraphicsDevice;
                    int w = gd.PresentationParameters.BackBufferWidth, h = gd.PresentationParameters.BackBufferHeight;
                    if (_target == null || _target.Width != w || _target.Height != h)
                        _target = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                    _capturing = true;
                    gd.SetRenderTarget(_target);
                }
                catch (Exception ex) { Entry.Error("screenshot", ex); _pending = null; _capturing = false; }
            }

            static void Postfix()
            {
                if (!_capturing)
                    return;
                _capturing = false;
                string name = _pending;
                _pending = null;
                var gd = Main.instance.GraphicsDevice;
                try
                {
                    gd.SetRenderTarget(null);
                    var data = new Color[_target.Width * _target.Height];
                    _target.GetData(data);
                    for (int i = 0; i < data.Length; i++)
                        data[i].A = 255;
                    var tex = new Texture2D(gd, _target.Width, _target.Height);
                    tex.SetData(data);
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "shots");
                    Directory.CreateDirectory(dir);
                    using (var fs = File.Create(Path.Combine(dir, name + ".png")))
                        tex.SaveAsPng(fs, tex.Width, tex.Height);
                    tex.Dispose();
                    Entry.Log("screenshot " + name + " " + _target.Width + "x" + _target.Height);
                }
                catch (Exception ex) { Entry.Error("screenshot", ex); }
            }
        }

        [Hook("test_screenshot_target")]
        [HarmonyPatch(typeof(GraphicsDevice), nameof(GraphicsDevice.SetRenderTarget), new[] { typeof(RenderTarget2D) })]
        static class TargetPatch
        {
            // while capturing, "draw to the screen" means "draw to our target"
            static void Prefix(ref RenderTarget2D renderTarget)
            {
                if (_capturing && renderTarget == null)
                    renderTarget = _target;
            }
        }
    }
}
