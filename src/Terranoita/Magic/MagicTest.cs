using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_MAGIC=1 (with TERRANOITA_AUTOTEST=1): the test character's starting wands, then wands with
    /// harder spells, fired at a Noita creature in front. Lines start with "MAGIC".
    /// </summary>
    public static class MagicTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_MAGIC") == "1";
        static readonly string[][] Sets =
        {
            null,   // the starting bolt staff
            new[] { "BOMB" },
            new[] { "DAMAGE", "BURST_2", "LIGHT_BULLET_TRIGGER", "BOMB", "LIGHT_BULLET" },
            new[] { "DIVIDE_10", "LIGHT_BULLET" },
            new[] { "HOMING", "SPITTER", "SPITTER" },
            new[] { "BLACK_HOLE" },
            new[] { "GRENADE", "FIREBALL", "ACIDSHOT" },
            // modifiers: components of extra entities, then Noita's own shot scripts
            new[] { "SINEWAVE", "FIRE_TRAIL", "LIGHT_BULLET" },
            new[] { "ARC_ELECTRIC", "BURST_2", "LIGHT_BULLET", "LIGHT_BULLET" },
            new[] { "ORBIT_DISCS", "SPIRALING_SHOT", "LIGHT_BULLET" },
            new[] { "WALL_HORIZONTAL" },
            new[] { "TELEPORT_PROJECTILE" },
        };
        const int Each = 300;
        public static int Length => 120 + Sets.Length * Each + 60;
        static NPC _target;

        static void Log(string s) => Entry.Log("MAGIC " + s);

        public static void Frame(Player p, int frame)
        {
            p.statManaMax = 400;
            if (frame % 30 == 0)
                p.statMana = 400;
            if (frame == 100)
            {
                Log("hotbar wands: " + string.Join(", ", Enumerable.Range(0, 10).Where(i => MagicItems.IsWand(p.inventory[i]))
                    .Select(i => i + " " + p.inventory[i].Name + " [" + (MagicItems.WandOf(p.inventory[i]) == null ? "NUMBER LOST" : string.Join(" ", MagicItems.WandOf(p.inventory[i]).Slots.Select(s => s ?? "-"))) + "]")));
                Log("wand slots: " + string.Join(", ", WandWindow.WandSlots.Select(it => it.IsAir ? "-" : it.Name)));
                Log("hotbar raw: " + string.Join(" ", Enumerable.Range(0, 10).Select(i => p.inventory[i].type + ":" + p.inventory[i].prefix)) +
                    "; prefix calls while loading " + MagicItems.PrefixCalls + ", last " + MagicItems.LastPrefix);
                // where would a wand's number get lost? straight Prefix calls and a Serialize/DeserializeFrom round trip
                var probe = MagicItems.MakeWand(new WandData { Id = 300 });   // not kept in the store
                byte made = probe.prefix;
                var a = new Item(); a.SetDefaults(probe.type); bool ok1 = a.Prefix(made);
                var b = new Item(); b.SetDefaults(probe.type); bool ok2 = b.Prefix(made, out bool top);
                var ms = new System.IO.MemoryStream();
                probe.Serialize(new System.IO.BinaryWriter(ms), false);
                ms.Position = 0;
                var c = new Item(); c.DeserializeFrom(new System.IO.BinaryReader(ms), false);
                Log("prefix probe: made " + made + ", Prefix(int) " + a.prefix + " " + ok1 + ", Prefix(int, ref) " + b.prefix + " " + ok2 +
                    ", round trip " + c.type + ":" + c.prefix + " (" + ms.Length + " bytes)");
            }
            if (frame < 120 || frame >= 120 + Sets.Length * Each)
            {
                Casting.TestFire = false;
                if (frame == 120 + Sets.Length * Each)
                {
                    Log("saving hotbar: " + string.Join(" ", Enumerable.Range(0, 10).Select(i => p.inventory[i].type + ":" + p.inventory[i].prefix)));
                    Main.ActivePlayerFileData.Player = p;   // the file's player is a copy from the menu: save the one playing
                    Terraria.Player.SavePlayer(Main.ActivePlayerFileData);   // the next run checks the wands come back
                    Log("done, player saved");
                }
                return;
            }
            int k = (frame - 120) / Each, t = (frame - 120) % Each;
            // pictures for checking by eye: the wand in hand while casting, the wand window in both looks
            if (k == 0 && t == 100)
                Screenshot.Request("held_wand");
            if (k == 2 && t == 30)
                WandWindow.TestOpen(true, false);
            if (k == 2 && t == 60)
                Screenshot.Request("window_noita");
            if (k == 2 && t == 90)
                WandWindow.TestOpen(true, true);
            if (k == 2 && t == 120)
                Screenshot.Request("window_terraria");
            if (k == 2 && t == 150)
                WandWindow.TestOpen(false, false);
            if (k == 5 && t == 150)
                Screenshot.Request("black_hole");
            if (k == 8 && t == 60)
                Screenshot.Request("arc");
            if (k == 9 && t == 90)
                Screenshot.Request("orbit_scripts");
            if (t == 0)
            {
                int slot = Enumerable.Range(0, 10).FirstOrDefault(i => MagicItems.IsWand(p.inventory[i]));
                if (Sets[k] != null)
                {
                    var w = WandStore.NewWand();
                    w.Name = "test " + k; w.Sprite = "data/items_gfx/handgun.xml"; w.CastDelay = 10; w.RechargeTime = 30; w.SpellsPerCast = 1;
                    w.Slots = Sets[k].ToArray();
                    w.Uses = w.Slots.Select(s => -1).ToArray();
                    WandStore.Save();   // the saved character carries its number: the next run must find it
                    slot = 1;
                    p.inventory[slot] = MagicItems.MakeWand(w);
                }
                p.selectedItemState.Select(slot);
                _target?.StrikeNPCNoInteraction(99999, 0, 0);
                var def = Enemies.All.First(e => e.Id == "zombie_weak");
                int x = (int)p.Center.X + 14 * 16, bottom = (int)(p.position.Y + p.height);
                int who = Carriers.Spawn(def, x, bottom);
                _target = who >= 0 ? Main.npc[who] : null;
                Log("set " + k + ": " + p.inventory[slot].Name + " [" + string.Join(" ", MagicItems.WandOf(p.inventory[slot])?.Slots ?? new string[0]) + "] at a " +
                    (_target != null ? "zombie_weak life " + _target.life : "no target"));
            }
            Casting.TestFire = true;
            Casting.TestAim = _target != null && _target.active ? _target.Center : p.Center + new Vector2(p.direction * 200, 0);
            if (t % 60 == 59)
                Log("set " + k + " +" + (t + 1) / 60 + "s: mana " + p.statMana + ", spell shots " + SpellShots.Ids().Count + " (scripted " + SpellShots.ScriptedCount + ")" + ", target " +
                    (_target != null && _target.active ? "life " + _target.life + "/" + _target.lifeMax : "gone") + ", player hp " + p.statLife);
        }
    }
}
