using System;
using System.Collections.Generic;
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
                // player.bank: the kick, drinking; music*.bank: the main menu's music (event:/music/mountain/enter, "Kick the Cart")
                var banks = new List<string> { "animals.bank", "projectiles.bank", "explosion.bank", "player.bank", "items.bank", "music.bank" };
                for (int i = 1; i <= 11; i++)
                    banks.Add("music" + i.ToString("00") + ".bank");
                _fmod = NoitaFmod.Open(noitaDir, banks, Entry.Log);
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

        const string MenuMusic = "music/mountain/enter";   // author: "Kick the Cart"
        static IntPtr _menuMusic;
        static bool _menuMissing;

        /// <summary>Noita's main menu music plays in Terraria's main menu (author), Terraria's is silenced.</summary>
        public static bool MenuMusicOn => _menuMusic != IntPtr.Zero;

        public static void Update()
        {
            if (_fmod == null)
                return;
            var center = Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
            float volume;
            if (Main.gameMenu)
            {
                if (_menuMusic == IntPtr.Zero && !_menuMissing)
                {
                    _menuMusic = _fmod.Start(MenuMusic);
                    _menuMissing = _menuMusic == IntPtr.Zero;
                    Entry.Log(_menuMissing ? "Noita menu music not found (" + MenuMusic + ")" : "Noita menu music on");
                }
                volume = Main.instance.IsActive ? Main.musicVolume : 0f;   // only the music plays in the menu
            }
            else
            {
                if (_menuMusic != IntPtr.Zero)
                {
                    _fmod.Stop(_menuMusic);
                    _menuMusic = IntPtr.Zero;
                }
                volume = Main.instance.IsActive ? Main.soundVolume : 0f;
            }
            _fmod.Update(center.X / Units.PixelScale, center.Y / Units.PixelScale, volume);
        }

        [Hook("menu_music")]
        [HarmonyLib.HarmonyPatch(typeof(Main), "UpdateAudio_DecideOnNewMusic")]
        static class TerrariaMenuMusicPatch
        {
            static void Postfix()
            {
                if (Main.gameMenu && MenuMusicOn)
                    Main.newMusic = 0;   // Terraria's title music off while Noita's plays
            }
        }
    }
}
