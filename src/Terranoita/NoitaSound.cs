using System;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>Noita's own sounds through the player's Noita FMOD (design/sheets: enemies.audio, attacks.sound, projectiles.audio).</summary>
    public static class NoitaSound
    {
        static NoitaFmod _fmod;

        public static bool Ready => _fmod != null;

        public static void Open(string noitaDir)
        {
            if (string.IsNullOrEmpty(noitaDir))
                return;
            try
            {
                _fmod = NoitaFmod.Open(noitaDir, new[] { "animals.bank", "projectiles.bank", "explosion.bank" }, Entry.Log);
                Entry.Log("Noita sounds ready");
            }
            catch (Exception ex)
            {
                _fmod = null;
                Entry.Log("Noita sounds off: " + ex.Message);
            }
        }

        static bool Real(string ev) => !string.IsNullOrEmpty(ev) && ev != "none";

        /// <summary>Play a Noita event at a Terraria world position.</summary>
        public static bool Play(string ev, Vector2 world)
        {
            if (_fmod == null || !Real(ev) || Main.dedServ)
                return false;
            return _fmod.Play(ev, world.X / Units.PixelScale, world.Y / Units.PixelScale);
        }

        /// <summary>The first of folder/name that exists.</summary>
        public static bool PlayFirst(string folder, Vector2 world, params string[] names)
        {
            if (_fmod == null || !Real(folder))
                return false;
            foreach (var n in names)
                if (_fmod.Has(folder + "/" + n))
                    return Play(folder + "/" + n, world);
            return false;
        }

        public static void Update()
        {
            if (_fmod == null)
                return;
            var center = Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
            float volume = Main.gameMenu || !Main.instance.IsActive ? 0f : Main.soundVolume;
            _fmod.Update(center.X / Units.PixelScale, center.Y / Units.PixelScale, volume);
        }
    }
}
