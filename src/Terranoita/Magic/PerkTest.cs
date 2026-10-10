using System;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terranoita.Generated;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_PERKS=1 (game_test -Mode perks): the author's perk rules in one go (PC-34): a boss dies -> perk
    /// items drop; the character takes 12 perks (Noita's funcs run); it dies -> all perks are lost and a quarter drop.
    /// One "PERKS" log line per step.
    /// </summary>
    public static class PerkTest
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_PERKS") == "1";
        public const int Length = 460;
        const int Taken = 12;

        static void Log(string s) => Entry.Log("PERKS " + s);
        static Item _flask;
        static float _stomach0;

        static int PerkItemsNear(Player p) =>
            Main.item.Count(i => i.active && Perks.IsPerk(i.inner));

        static void ClearPerkItems()
        {
            foreach (var i in Main.item)
                if (i.active && Perks.IsPerk(i.inner))
                    i.TurnToAir();
        }

        public static void Frame(Player p, int frame)
        {
            try
            {
                if (frame == 60)
                {
                    Perks.TestClear(p);
                    ClearPerkItems();
                    Log("list: " + Perks.All.Count + " perks from Noita's perk_list.lua");
                    int i = NPC.NewNPC(new EntitySource_WorldEvent(), (int)p.Center.X + 300, (int)p.Center.Y - 200, NPCID.KingSlime);
                    Main.npc[i].life = 0;
                    Main.npc[i].checkDead();   // dies as if killed: NPCLoot runs (our boss drop)
                    int expected = 1 + (Main.expertMode ? 1 : 0) + (Main.masterMode ? 1 : 0) + (Main.getGoodWorld ? 1 : 0);
                    Log("boss drop: " + PerkItemsNear(p) + " perk items (expected " + expected + ")");
                }
                else if (frame == 120)
                {
                    var dropped = Main.item.Where(it => it.active && Perks.IsPerk(it.inner)).ToList();
                    foreach (var it in dropped)
                    {
                        Perks.TestTake(p, Perks.PerkOf(it.inner).Id);
                        it.TurnToAir();
                    }
                    while (Perks.Of(p).Count < Taken)
                        Perks.TestTake(p, Perks.RandomFor(p).Id);
                    Log("taken: " + Perks.Of(p).Count + " (" + string.Join(",", Perks.Of(p)) + "); max life " + p.statLifeMax2 +
                        "; shot modifiers " + string.Join(",", Perks.ShotModifiers(p)));
                }
                else if (frame == 200)
                {
                    int had = Perks.Of(p).Count;
                    ClearPerkItems();
                    p.KillMe(PlayerDeathReason.ByCustomReason(p.name + " tested perks."), 9999, 0);
                    Log("death: perks left " + Perks.Of(p).Count + " of " + had + "; dropped " + PerkItemsNear(p) + " (expected " + had / 4 + ")");
                }
                else if (frame == 300)
                {
                    ClearPerkItems();
                    // PC-35: Noita creatures drop a random spell 1 time in 100 (300 kills: ~3)
                    int before = Main.item.Count(it => it.active && MagicItems.IsSpell(it.inner));
                    var def = Enemies.All.First(e => e.Id == "zombie_weak");
                    int killed = 0;
                    for (int k = 0; k < 300; k++)
                    {
                        int who = Carriers.Spawn(def, (int)p.Center.X + 200, (int)p.Bottom.Y);
                        if (who < 0)
                            continue;
                        Main.npc[who].life = 0;
                        Main.npc[who].checkDead();
                        killed++;
                    }
                    int spells = Main.item.Count(it => it.active && MagicItems.IsSpell(it.inner)) - before;
                    Log("spell drops: " + spells + " from " + killed + " Noita creatures (1 in " + Loot.SpellDropChance + ": about " + killed / Loot.SpellDropChance + ")");
                    Log("done");
                }
                else if (frame == 130)
                {
                    // drinking (author 2026-10-10: "drinking liquids broke"): a flask of alcohol in the hand, down held
                    _flask = MagicItems.MakeFlask("alcohol", Flasks.Capacity);
                    p.inventory[1] = _flask;
                    p.selectedItemState.Select(1);
                    _stomach0 = NoitaActions.Stomach;
                }
                else if (frame > 130 && frame <= 150)
                {
                    p.velocity.Y = 0;
                    p.controlDown = true;
                    Flasks.Update(p, false);
                    p.controlDown = false;
                }
                else if (frame == 151)
                {
                    var w = MagicItems.FlaskOf(p.inventory[1]);
                    Log("flask drink: alcohol " + Flasks.Capacity + " -> " + (w == null ? "no flask" : w.FlaskAmount.ToString("0")) + ", stomach +" +
                        (NoitaActions.Stomach - _stomach0).ToString("0") + ", statuses " + string.Join(",", Physics.Status.Active));
                    // and from a pool: Terraria water at the feet
                    p.inventory[1].TurnToAir();
                    int x = (int)(p.Center.X / 16), y = (int)((p.position.Y + p.height - 4) / 16);
                    Main.tile[x, y].liquid = 255;
                    Main.tile[x, y].liquidType(0);
                    _stomach0 = NoitaActions.Stomach;
                }
                else if (frame > 151 && frame <= 171)
                {
                    p.velocity.Y = 0;
                    NoitaActions.TestDrink(p);
                }
                else if (frame == 172)
                {
                    int x = (int)(p.Center.X / 16), y = (int)((p.position.Y + p.height - 4) / 16);
                    Log("pool drink: stomach +" + (NoitaActions.Stomach - _stomach0).ToString("0") + ", water left " + Main.tile[x, y].liquid);
                    Main.tile[x, y].liquid = 0;
                }
            }
            catch (Exception ex) { Entry.Error("PERKS test", ex); }
        }
    }
}
