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
                    .Select(i => i + " " + p.inventory[i].Name + " [" + string.Join(" ", MagicItems.WandOf(p.inventory[i]).Slots.Select(s => s ?? "-")) + "]")));
                Log("wand slots: " + string.Join(", ", WandWindow.WandSlots.Select(it => it.IsAir ? "-" : it.Name)));
            }
            if (frame < 120 || frame >= 120 + Sets.Length * Each)
            {
                Casting.TestFire = false;
                if (frame == 120 + Sets.Length * Each)
                    Log("done");
                return;
            }
            int k = (frame - 120) / Each, t = (frame - 120) % Each;
            if (t == 0)
            {
                int slot = Enumerable.Range(0, 10).FirstOrDefault(i => MagicItems.IsWand(p.inventory[i]));
                if (Sets[k] != null)
                {
                    var w = WandStore.NewWand();
                    w.Name = "test " + k; w.Sprite = "data/items_gfx/handgun.xml"; w.CastDelay = 10; w.RechargeTime = 30; w.SpellsPerCast = 1;
                    w.Slots = Sets[k].ToArray();
                    w.Uses = w.Slots.Select(s => -1).ToArray();
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
                Log("set " + k + " +" + (t + 1) / 60 + "s: mana " + p.statMana + ", spell shots " + SpellShots.Ids().Count + ", target " +
                    (_target != null && _target.active ? "life " + _target.life + "/" + _target.lifeMax : "gone") + ", player hp " + p.statLife);
        }
    }
}
