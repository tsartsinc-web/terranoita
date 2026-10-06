using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;
using Terraria.Localization;

namespace Terranoita.Game
{
    /// <summary>
    /// Noita's pictures and names, read from the player's own Noita (data.wak and data/translations), never shipped.
    /// Textures are made on the game thread once the graphics device exists.
    /// </summary>
    public static class NoitaArt
    {
        public sealed class Art
        {
            public NoitaSprite Sprite;
            public Texture2D Texture;
            /// <summary>Noita px from the sprite's origin down to its lowest visible pixel in the standing frame.</summary>
            public float Foot;
        }

        static NoitaFiles _files;
        static NoitaTranslations _names;
        static readonly Dictionary<string, Art> Cache = new Dictionary<string, Art>(StringComparer.OrdinalIgnoreCase);

        public static bool Ready => _files != null;

        public static void Open(string noitaDir)
        {
            if (string.IsNullOrEmpty(noitaDir))
            {
                Entry.Log("No Noita folder given: Noita enemies are off (Melty passes it with --noita-dir)");
                return;
            }
            try
            {
                _files = new NoitaFiles(noitaDir);
                Entry.Log("Noita data.wak: " + _files.Archive.Count + " files");
                if (_files.TryReadText("data/translations/common.csv", out var csv))
                    _names = NoitaTranslations.Parse(csv);
            }
            catch (Exception ex)
            {
                _files = null;
                Entry.Log("Cannot read Noita at " + noitaDir + ": " + ex.Message);
            }
        }

        /// <summary>The sprite (xml or png) with its texture, or null when it cannot be read. Game thread only.</summary>
        public static Art Get(string spritePath)
        {
            if (_files == null || string.IsNullOrEmpty(spritePath))
                return null;
            if (Cache.TryGetValue(spritePath, out var art))
                return art;
            try
            {
                var sprite = NoitaSprite.Load(p => _files.TryReadText(p, out var t) ? t : null, spritePath);
                if (!_files.TryRead(sprite.Image, out var png))
                    throw new FileNotFoundException(sprite.Image);
                art = new Art { Sprite = sprite, Texture = LoadTexture(png, out var px) };
                art.Foot = FootOf(sprite, px, art.Texture.Width, art.Texture.Height);
            }
            catch (Exception ex)
            {
                Entry.Log("sprite " + spritePath + " failed: " + ex.Message);
                art = null;
            }
            Cache[spritePath] = art;
            return art;
        }

        static float FootOf(NoitaSprite sprite, Color[] px, int w, int h)
        {
            var anim = sprite.Find("stand", "walk", "fly");
            int x0 = 0, y0 = 0, fw = w, fh = h;
            if (anim != null)
                anim.FrameRect(0, out x0, out y0, out fw, out fh);
            for (int y = Math.Min(h, y0 + fh) - 1; y >= y0; y--)
                for (int x = x0; x < Math.Min(w, x0 + fw); x++)
                    if (px[y * w + x].A > 0)
                        return y - y0 + 1 - sprite.OffsetY;
            return 0;
        }

        static Texture2D LoadTexture(byte[] png, out Color[] px)
        {
            Texture2D tex;
            using (var ms = new MemoryStream(png))
                tex = Texture2D.FromStream(Main.instance.GraphicsDevice, ms);
            // Terraria draws with premultiplied alpha; PNGs are straight alpha.
            px = new Color[tex.Width * tex.Height];
            tex.GetData(px);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
            }
            tex.SetData(px);
            return tex;
        }

        static string NoitaLanguage()
        {
            string c = Language.ActiveCulture?.Name ?? "en-US";
            string two = c.Split('-')[0].ToLowerInvariant();
            switch (two)
            {
                case "pt": return "pt-br";
                case "es": return "es-es";
                case "fr": return "fr-fr";
                case "zh": return "zh-cn";
                case "ja": return "jp";
                default: return two;
            }
        }

        public static string Name(EnemyDef e) =>
            _names?.Get(e.NameKey, NoitaLanguage()) ?? e.NameEn ?? e.Id;

        /// <summary>Make every built enemy's and projectile's texture once the engine has loaded, so play does not stutter.</summary>
        public static void Preload()
        {
            if (!Ready)
                return;
            int ok = 0, bad = 0;
            foreach (var e in Enemies.All)
                if (Defs.InStage(e.Stage, Entry.Stage))
                    if (Get(e.Sprite) != null) ok++; else bad++;
            foreach (var p in Projectiles.All)
                if (Defs.InStage(p.Stage, Entry.Stage))
                    if (Get(p.Sprite) != null) ok++; else bad++;
            Entry.Log("Noita sprites loaded: " + ok + ", failed: " + bad);
        }
    }
}
