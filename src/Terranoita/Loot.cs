using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Biomes = Terraria.GameContent.Bestiary.BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes;

namespace Terranoita.Game
{
    /// <summary>
    /// Noita-style gold (design/sheets/drops.json noita_gold): coins by health, sometimes a Heart or a Mana Star.
    /// Plus the loot of a Terraria enemy like it (drops.json terraria_twin): one that lives where it died (bestiary
    /// biome) with the nearest max life, rolled through Terraria's own drop rules.
    /// </summary>
    public static class Loot
    {
        static void Drop(NPC npc, NoitaNpc n)
        {
            if (n.Def.Drops != "noita_gold")
                return;
            var src = new EntitySource_Loot(npc);
            int copper = (int)Math.Round(n.Def.NoitaHp * 6 * n.Tier.HpMult);   // drops.json noita_gold
            int[] types = { ItemID.PlatinumCoin, ItemID.GoldCoin, ItemID.SilverCoin, ItemID.CopperCoin };
            int[] values = { 1000000, 10000, 100, 1 };
            for (int k = 0; k < types.Length; k++)
            {
                int stack = copper / values[k];
                copper %= values[k];
                if (stack > 0)
                    Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, types[k], stack);
            }
            if (Main.rand.Next(12) == 0)
                Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, ItemID.Heart);
            if (Main.rand.Next(15) == 0)
                Item.NewItem(src, (int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, ItemID.Star);
            TwinDrop(npc, n);
        }

        // ---- terraria_twin ----

        const int Closest = 3;           // one of the 3 nearest by max life, for variety
        const int PreHardmodeDamage = 40;  // Terraria has no hardmode flag on enemies: pre-hardmode ones hit for less
                                           // than 40 in normal mode (Possessed Armor, Werewolf, Mummy, Wraith: 40+)
        static Dictionary<IBestiaryInfoElement, List<(int id, int life, int damage)>> _byBiome;

        /// <summary>
        /// Hostile non-boss Terraria enemies with their own drops, by bestiary biome (normal-mode max life and damage).
        /// Event enemies (rain, blood moon, invasions, holidays) are left out: their loot is the event's.
        /// </summary>
        static void BuildTwins()
        {
            var events = new HashSet<object>(new[] { "Events", "Invasions" }
                .Select(name => typeof(BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions).GetNestedType(name))
                .Where(t => t != null)
                .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                .Select(f => f.GetValue(null)));
            _byBiome = new Dictionary<IBestiaryInfoElement, List<(int, int, int)>>();
            foreach (var kv in ContentSamples.NpcsByNetId)
            {
                var s = kv.Value;
                // rarity: Terraria's rare finds (Nymph, Doctor Bones, Mimics, Tim...) keep their loot to themselves
                if (kv.Key <= 0 || s.boss || s.rarity > 0 || s.friendly || s.townNPC || s.CountsAsACritter || s.lifeMax <= 5 || s.damage <= 0 ||
                    s.type == NPCID.None3 || Main.ItemDropsDB.GetRulesForNPCID(kv.Key, false).Count == 0)
                    continue;
                var entry = Main.BestiaryDB.FindEntryByNPCID(kv.Key);
                if (entry?.Info == null || entry.Info.Any(events.Contains))
                    continue;
                foreach (var tag in entry.Info.OfType<SpawnConditionBestiaryInfoElement>())
                {
                    if (!_byBiome.TryGetValue(tag, out var list))
                        _byBiome[tag] = list = new List<(int, int, int)>();
                    list.Add((kv.Key, s.lifeMax, s.damage));
                }
            }
        }

        /// <summary>Bestiary biomes of the place: the special ones first, the layer as a fallback.</summary>
        static IEnumerable<IBestiaryInfoElement[]> PlaceTags(Player p)
        {
            bool under = p.ZoneDirtLayerHeight || p.ZoneRockLayerHeight;
            var special = new List<IBestiaryInfoElement>();
            if (p.ZoneDungeon) special.Add(Biomes.TheDungeon);
            if (p.ZoneLihzhardTemple) special.Add(Biomes.TheTemple);
            if (p.ZoneUnderworldHeight) special.Add(Biomes.TheUnderworld);
            if (p.ZoneSkyHeight) special.Add(Biomes.Sky);
            if (p.ZoneGlowshroom) special.Add(under ? Biomes.UndergroundMushroom : Biomes.SurfaceMushroom);
            if (p.ZoneMarble) special.Add(Biomes.Marble);
            if (p.ZoneGranite) special.Add(Biomes.Granite);
            if (p.ZoneUndergroundDesert) special.Add(Biomes.UndergroundDesert);
            else if (p.ZoneDesert) special.Add(Biomes.Desert);
            if (p.ZoneSnow) special.Add(under ? Biomes.UndergroundSnow : Biomes.Snow);
            if (p.ZoneJungle) special.Add(under ? Biomes.UndergroundJungle : Biomes.Jungle);
            if (p.ZoneCorrupt) special.Add(under ? Biomes.UndergroundCorruption : Biomes.TheCorruption);
            if (p.ZoneCrimson) special.Add(under ? Biomes.UndergroundCrimson : Biomes.TheCrimson);
            if (p.ZoneHallow) special.Add(under ? Biomes.UndergroundHallow : Biomes.TheHallow);
            if (p.ZoneBeach) special.Add(Biomes.Ocean);
            if (p.ZoneGraveyard) special.Add(Biomes.Graveyard);
            yield return special.ToArray();
            yield return new IBestiaryInfoElement[] { p.ZoneRockLayerHeight ? Biomes.Caverns : p.ZoneDirtLayerHeight ? Biomes.Underground : Biomes.Surface };
        }

        public static int PickTwin(Player p, int life)
        {
            if (_byBiome == null)
                BuildTwins();
            foreach (var tags in PlaceTags(p))
            {
                var near = tags.Where(t => t != null && _byBiome.ContainsKey(t)).SelectMany(t => _byBiome[t]).Distinct()
                               .Where(c => Main.hardMode || c.damage < PreHardmodeDamage)
                               .OrderBy(c => Math.Abs(Math.Log((double)c.life / Math.Max(1, life)))).Take(Closest).ToList();
                if (near.Count > 0)
                    return near[Main.rand.Next(near.Count)].id;
            }
            return 0;
        }

        static void TwinDrop(NPC npc, NoitaNpc n)
        {
            var p = Main.LocalPlayer;
            int id = PickTwin(p, npc.lifeMax);
            if (id <= 0)
                return;
            // a stand-in of the twin where ours died, so Terraria's rules drop its loot here
            var twin = new NPC();
            twin.SetDefaults(id);
            twin.position = npc.position;
            twin.width = npc.width;
            twin.height = npc.height;
            twin.whoAmI = npc.whoAmI;
            twin.value = 0;
            var before = new bool[Main.maxItems];
            for (int i = 0; i < Main.maxItems; i++)
                before[i] = Main.item[i].active;
            Main.ItemDropSolver.TryDropping(new DropAttemptInfo
            {
                npc = twin,
                player = p,
                rng = Main.rand,
                IsExpertMode = Main.expertMode,
                IsMasterMode = Main.masterMode,
            });
            var got = Enumerable.Range(0, Main.maxItems).Where(i => !before[i] && Main.item[i].active)
                                .Select(i => Main.item[i].Name + (Main.item[i].stack > 1 ? " x" + Main.item[i].stack : ""));
            Entry.Log("loot of " + n.Def.Id + " like " + Lang.GetNPCNameValue(id) + " (" + id + "): " + string.Join(", ", got));
        }

        [Hook("npc_loot")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.NPCLoot))]
        static class LootPatch
        {
            static bool Prefix(NPC __instance)
            {
                var n = Carriers.Get(__instance);
                if (n == null)
                    return true;
                try { Drop(__instance, n); }
                catch (Exception ex) { Entry.Error("npc_loot " + n.Def.Id, ex); }
                Entry.Log("killed " + n.Def.Id + " #" + __instance.whoAmI);
                Carriers.Forget(__instance);
                return false;
            }
        }
    }
}
