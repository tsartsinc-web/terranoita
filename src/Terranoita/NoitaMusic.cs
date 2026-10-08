using System;
using System.Collections.Generic;
using System.Linq;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Noita's music in the world (author: "all of Noita's music in Terraria"): the place the player is in picks a folder of
    /// Noita's music banks (event:/music/&lt;folder&gt;/*) and a random track of it plays; Terraria's music is off.
    /// The folder of a Noita biome is its biome file's audio_music_2 (data/biome/*.xml) where it has one; the Noita biome of
    /// a Terraria zone is design/sheets/biome_map.json. Tracks whose names start with "_" are Noita's unused ones.
    /// </summary>
    public static class NoitaMusic
    {
        // most specific first: (zone of terraria_zones.json, folder); "hm" zones only in hardmode, post_plantera after Plantera
        static readonly (string zone, string folder)[] Zones =
        {
            ("underworld_post_plantera", "the_end"),     // The Work (Hell): the_end.xml
            ("sky_post_plantera", "the_end"),            // The Work (Sky): the_sky.xml
            ("lihzahrd_temple", "robobase"),             // Power Plant (robobase.xml: vault; robobase/00 is its own track)
            ("dungeon", "crypt"),                        // Temple of the Art: crypt.xml
            ("spider_cave_hm", "rainforest_dark"),       // Lukki Lair: rainforest_dark
            ("crimson_underground_hm", "rainforest"),    // Meat Realm: meat.xml
            ("underground_hallow", "wizardcave"),        // Wizards' Den
            ("underground_jungle_hm", "fungiforest"),    // Overgrown Cavern: fungiforest
            ("underground_jungle", "rainforest"),        // Underground Jungle: rainforest.xml
            ("glowing_mushroom", "fungicave"),           // Fungal Caverns: fungicave.xml
            ("underground_desert_hm", "ancient_tracks"), // Pyramid (pyramid.xml has none)
            ("underground_desert", "side_biome_action"), // Sandcave: sandcave.xml
            ("underground_snow_hm", "snowcave"),         // Frozen Vault: vault_frozen.xml
            ("underground_snow", "snowcave"),            // Snowy Depths: snowcave.xml
            ("marble", "temple"),                        // Magical Temple
            ("granite", "wandcave"),                     // Ancient Laboratory: secret_lab.xml
            ("cavern_hm", "vault"),                      // The Vault: vault.xml
            ("cavern_deep", "snowcastle"),               // Hiisi Base: snowcastle.xml
            ("cavern", "excavationsite"),                // Coal Pits: excavationsite.xml
            ("underground_dirt", "coalmine"),            // Mines: coalmine.xml
            ("sky_hm", "tower"),                         // Cloudscape (clouds.xml has none)
            ("surface_snow", "winter"),                  // Snowy Wasteland (winter, winter2)
            ("surface_desert", "desert"),                // Desert
            ("surface_water", "watercave"),              // Lake
            ("surface_forest", "surface"),               // Forest: mountain_tree.xml surface0 (and surface1)
        };
        // a folder plays its own tracks and these too
        static readonly Dictionary<string, string[]> Also = new Dictionary<string, string[]>
        {
            { "surface", new[] { "surface0", "surface1" } },
            { "winter", new[] { "winter2" } },
            { "robobase", new[] { "vault" } },
            { "excavationsite", new[] { "smokecave" } },
        };

        static Dictionary<string, List<string>> _tracks;
        static IntPtr _playing;
        static string _folder, _track;
        static int _quiet, _logged;
        static bool _intro;
        static string _world;

        /// <summary>Noita's music runs the world (also in the quiet between tracks): Terraria's stays off.</summary>
        public static bool On => _tracks != null && _tracks.Count > 0 && !Main.gameMenu;

        /// <summary>Every music event of the banks, by folder (event:/music/coalmine/03 -> coalmine).</summary>
        static void Load(NoitaFmod fmod)
        {
            _tracks = new Dictionary<string, List<string>>();
            foreach (var ev in fmod.EventPaths())
            {
                var parts = ev.Split('/');
                if (parts.Length != 4 || parts[1] != "music" || parts[3].StartsWith("_") || parts[3].EndsWith("_old"))
                    continue;
                if (!_tracks.TryGetValue(parts[2], out var list))
                    _tracks[parts[2]] = list = new List<string>();
                list.Add(ev);
            }
            Entry.Log("Noita music: " + _tracks.Sum(t => t.Value.Count) + " tracks in " + _tracks.Count + " folders");
        }

        /// <summary>The folder for where the player is now.</summary>
        static string Pick(Player p)
        {
            if (_intro)
                return "intro";
            if (Main.npc.Any(n => n.active && n.boss))
                return "boss_arena";
            if (Main.invasionType > 0 && Main.invasionProgressNearInvasion || Main.bloodMoon && p.ZoneOverworldHeight || Main.eclipse && p.ZoneOverworldHeight ||
                Main.pumpkinMoon || Main.snowMoon)
                return "miniboss";
            if (p.ZoneUnderworldHeight)
                return NPC.downedPlantBoss ? "the_end" : "lavalake";
            int x = (int)(p.Center.X / 16), y = (int)(p.Center.Y / 16);
            foreach (var (zone, folder) in Zones)
            {
                if (zone.EndsWith("_hm") && !Main.hardMode || zone.EndsWith("_post_plantera") && !NPC.downedPlantBoss)
                    continue;
                if (ZoneChecks.All.TryGetValue(zone, out var check) && check != null && check(p, x, y))
                {
                    if (folder == "surface")
                    {
                        if (p.townNPCs >= 3)
                            return "temple_town";   // a town: Noita's Holy Mountain (temple/enter)
                        if (!Main.dayTime)
                            return "darkness";
                    }
                    return folder;
                }
            }
            if (p.ZoneCorrupt || p.ZoneCrimson)
                return "barren";
            if (p.ZoneBeach)
                return "watercave";
            if (p.ZoneSkyHeight)
                return "tower";
            return p.ZoneOverworldHeight ? (Main.dayTime ? "surface" : "darkness") : "coalmine";
        }

        static List<string> TracksOf(string folder)
        {
            var list = new List<string>();
            if (folder == "temple_town")
                return _tracks.TryGetValue("temple", out var t) ? t.Where(e => e.EndsWith("/enter")).ToList() : list;
            if (folder == "boss_arena")
                return _tracks.TryGetValue("boss_arena", out var b) ? b.Where(e => e.EndsWith("/battle")).ToList() : list;
            if (_tracks.TryGetValue(folder, out var own))
                list.AddRange(own);
            if (Also.TryGetValue(folder, out var more))
                foreach (var f in more)
                    if (_tracks.TryGetValue(f, out var m))
                        list.AddRange(m);
            return list;
        }

        public static void Update(NoitaFmod fmod)
        {
            if (Main.gameMenu || Main.dedServ)
            {
                Stop(fmod);
                _world = null;
                return;
            }
            if (_tracks == null)
                Load(fmod);
            var p = Main.LocalPlayer;
            if (p == null || !p.active)
                return;
            if (_world != Main.worldPathName)
            {
                // a new world (made in the last 10 minutes): Noita's intro music first (intro/00), then the place's own
                _world = Main.worldPathName;
                var made = Main.ActiveWorldFileData?.CreationTime ?? DateTime.MinValue;
                _intro = (DateTime.Now - made).TotalMinutes < 10;
                Stop(fmod);
            }
            if (Main.GameUpdateCount % 30 == 0 || _playing == IntPtr.Zero)
            {
                string folder = Pick(p);
                if (folder != _folder)
                {
                    // a new place: its music, with a short fade of the old (FMOD's own fade out)
                    Stop(fmod);
                    _folder = folder;
                    _quiet = 0;
                }
            }
            if (_playing != IntPtr.Zero && !fmod.Playing(_playing))
            {
                // a track played to its end: a little quiet, then another of the same place
                fmod.Stop(_playing);
                _playing = IntPtr.Zero;
                if (_intro)
                {
                    _intro = false;
                    _folder = null;
                    return;
                }
                _quiet = 60 * Main.rand.Next(4, 12);
            }
            if (_playing == IntPtr.Zero && _folder != null)
            {
                if (_quiet > 0)
                {
                    _quiet--;
                    return;
                }
                var list = TracksOf(_folder);
                if (list.Count == 0)
                {
                    if (_intro)
                        _intro = false;
                    if (_logged++ < 10)
                        Entry.Log("Noita music: none for " + _folder);
                    _folder = null;
                    return;
                }
                var pick = list.Count > 1 ? list.Where(e => e != _track).ToList() : list;
                _track = pick[Main.rand.Next(pick.Count)];
                _playing = fmod.Start(_track);
                if (DebugTools.Testing || _logged++ < 30)
                    Entry.Log("Noita music: " + _track + " (" + _folder + ")");
            }
            // the bus is at Terraria's sound volume (NoitaSound.Update): the track scales it to the music volume
            if (_playing != IntPtr.Zero)
                fmod.SetVolume(_playing, Main.soundVolume > 0.01f ? Math.Min(4f, Main.musicVolume / Main.soundVolume) : 0f);
        }

        static void Stop(NoitaFmod fmod)
        {
            if (_playing != IntPtr.Zero)
                fmod.Stop(_playing);
            _playing = IntPtr.Zero;
            _folder = null;
        }
    }
}
