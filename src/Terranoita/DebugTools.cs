using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terranoita.Generated;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Test tools (design/sheets/systems.json, test_tools).
    ///   Ctrl+Shift+N  spawn the next built Noita enemy 12 tiles in front of the player
    ///   TERRANOITA_AUTOTEST=1  enter the first player/world of the save folder, make the player unkillable, spawn
    ///                          every built enemy in turn and log what happens (use with -savedirectory on a copy).
    ///   TERRANOITA_AUTOTEST_STAGE=1b  only that stage's enemies;  TERRANOITA_AUTOTEST_EXIT=1  close the game when done
    ///                          (tools/pc_step.ps1 -AutoTest runs both and keeps the log).
    /// </summary>
    public static class DebugTools
    {
        static int _next;
        static readonly bool Auto = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST") == "1";
        /// <summary>The autotest is running: natural Noita spawns are off.</summary>
        public static bool Testing => Auto;
        static readonly bool Showcase = Environment.GetEnvironmentVariable("TERRANOITA_SHOWCASE") == "1";
        static readonly string OnlyStage = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_STAGE");
        static readonly bool ExitWhenDone = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_EXIT") == "1";
        /// <summary>TERRANOITA_AUTOTEST_SECONDS: how long each enemy is watched (default 6).</summary>
        static readonly int Each = 60 * (int.TryParse(Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_SECONDS"), out int sec) && sec > 0 ? sec : 6);
        static int _menuFrames, _worldFrames, _autoIndex;
        static bool _entering;
        const string TestPlayer = "Terranoita Test";
        static readonly NoitaNpc[] _seen = new NoitaNpc[Main.maxNPCs];

        static EnemyDef[] Built => Enemies.All.Where(e => Defs.InStage(e.Stage, Entry.Stage)).ToArray();

        /// <summary>Within the visible screen and not inside solid tiles (a worm underground, a ghost in a wall).</summary>
        static bool OnScreen(NPC n)
        {
            var screen = new Rectangle((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth, Main.screenHeight);
            return screen.Intersects(n.Hitbox) && !Collision.SolidCollision(n.position + new Vector2(n.width / 4f, n.height / 4f), n.width / 2, n.height / 2);
        }

        static void SpawnInFront(EnemyDef e, int tiles)
        {
            var p = Main.LocalPlayer;
            int x = (int)p.Center.X + p.direction * tiles * 16;
            int y = (int)(p.position.Y + p.height);
            // stand it on the ground under x
            int tx = x / 16, ty = y / 16 - 3;
            while (ty < y / 16 + 20 && !Collision.SolidTiles(tx, tx, ty + 1, ty + 1))
                ty++;
            Carriers.Spawn(e, x, (ty + 1) * 16);
        }

        static void Update()
        {
            NoitaSound.Update();
            if (Auto)
                AutoTest();
            if (Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest)
                return;
            bool ctrl = Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
            bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
            if (ctrl && shift && Main.keyState.IsKeyDown(Keys.N) && !Main.oldKeyState.IsKeyDown(Keys.N))
            {
                var all = Built;
                var e = all[_next++ % all.Length];
                SpawnInFront(e, 12);
                Main.NewText("Terranoita: " + NoitaArt.Name(e), new Color(255, 200, 80));
            }
        }

        static void AutoTest()
        {
            if (Main.gameMenu)
            {
                if (_entering || Main.menuMode != 0 || ++_menuFrames < 120)
                    return;
                _entering = true;
                Main.LoadPlayers();
                if (!Main.PlayerList.Any(f => f.Name == TestPlayer))
                {
                    // a fresh starting character, so tests show early-game fights
                    var np = new Player { name = TestPlayer, difficulty = 0, statLifeMax = 100, statLife = 100, statManaMax = 20 };
                    np.inventory[0].SetDefaults(3507);   // Copper Shortsword
                    np.inventory[1].SetDefaults(3509);   // Copper Pickaxe
                    np.inventory[2].SetDefaults(3506);   // Copper Axe
                    Terraria.IO.PlayerFileData.CreateAndSave(np);
                    Main.LoadPlayers();
                    Entry.Log("AUTOTEST: created test character " + TestPlayer);
                }
                Main.LoadWorlds();
                if (Main.PlayerList.Count == 0 || Main.WorldList.Count == 0)
                {
                    Entry.Log("AUTOTEST: no player or world in " + Main.SavePath);
                    return;
                }
                var who = Main.PlayerList.First(f => f.Name == TestPlayer);
                Entry.Log("AUTOTEST: entering " + Main.WorldList[0].Name + " as " + who.Name);
                Main.SelectPlayer(who);
                Main.WorldList[0].SetAsActive();
                WorldGen.playWorld();
                Main.menuMode = 10;
                return;
            }
            var p = Main.LocalPlayer;
            // keep the test character alive but still taking hits, so attacks show in the log
            if (p.statLife < p.statLifeMax2 / 2)
                p.statLife = p.statLifeMax2;
            _worldFrames++;
            // only the enemy under test: Terraria's own hostile NPCs are removed as soon as they appear
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var other = Main.npc[i];
                if (other.active && !other.friendly && !other.townNPC && Carriers.Get(other) == null)
                    other.active = false;
            }
            var all = OnlyStage == null ? Built : Built.Where(e => e.Stage == OnlyStage).ToArray();
            if (Showcase)
            {
                // TERRANOITA_SHOWCASE=1: noon, and a group of Noita enemies around the player, for the listing's screenshots
                if (_worldFrames == 120)
                {
                    Main.dayTime = true;
                    Main.time = 27000;
                    foreach (var (id, tiles) in new[] { ("shotgunner_weak", 9), ("miner_weak", 14), ("zombie_weak", -6), ("firemage_weak", -11), ("bat", 5) })
                    {
                        int dir = p.direction;
                        p.direction = Math.Sign(tiles);
                        SpawnInFront(Defs.Enemy[id], Math.Abs(tiles));
                        p.direction = dir;
                    }
                    Entry.Log("AUTOTEST: showcase ready");
                }
                return;
            }
            // one enemy every 6 seconds, starting 5 seconds in; then a natural-spawn check
            if (_worldFrames >= 300 && (_worldFrames - 300) % Each == 0 && _autoIndex < all.Length)
            {
                var e = all[_autoIndex++];
                // one enemy at a time: remove the previous ones
                for (int i = 0; i < Main.maxNPCs; i++)
                    if (Main.npc[i].active && Carriers.Get(Main.npc[i]) != null)
                    {
                        Carriers.Forget(Main.npc[i]);
                        Main.npc[i].active = false;
                    }
                Entry.Log("AUTOTEST: spawning " + e.Id);
                SpawnInFront(e, 10);
            }
            // report Noita enemies that went away without dying
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var npc = Main.npc[i];
                var tracked = _seen[i];
                var now = npc.active ? Carriers.Get(npc) : null;
                if (tracked != null && now != tracked)
                    Entry.Log("AUTOTEST: " + tracked.Def.Id + " #" + i + " gone after " + tracked.Brain.Age + " brain frames: active=" + npc.active +
                              " type=" + npc.type + " life=" + npc.life + "/" + npc.lifeMax + " timeLeft=" + npc.timeLeft +
                              " pos=" + (int)(npc.position.X / 16) + "," + (int)(npc.position.Y / 16));
                _seen[i] = now;
            }
            if (_worldFrames % 60 == 0)
            {
                var live = Enumerable.Range(0, Main.maxNPCs).Select(i => Main.npc[i])
                    .Where(n => n.active && Carriers.Get(n) != null)
                    .Select(n => Carriers.Get(n).Def.Id + "@" + (int)((n.Center.X - p.Center.X) / 16) + "," + (int)((n.Center.Y - p.Center.Y) / 16) +
                                 " " + Carriers.Get(n).Brain.Anim + " hp" + n.life + (OnScreen(n) ? "" : " OFFSCREEN"));
                Entry.Log("AUTOTEST: player hp " + p.statLife + "; " + string.Join(" | ", live));
            }
            if (_worldFrames == 300 + Each * all.Length + 600)
            {
                Entry.Log("AUTOTEST: done; Noita enemies alive: " + Carriers.CountNear(p.Center, 99999));
                if (ExitWhenDone)
                    Main.instance.Exit();
            }
        }

        [Hook("main_update")]
        [HarmonyPatch(typeof(Main), "DoUpdate")]
        static class UpdatePatch
        {
            static void Postfix() => Update();
        }
    }
}
