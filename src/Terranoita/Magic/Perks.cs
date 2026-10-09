using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Terranoita.Noita;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita's perks as items (design/perks.md, PC-34): Terraria bosses drop them, the player uses one from the hand and
    /// keeps the perk (saved per character); on death all perks are lost and with more than 10 a quarter drop as items.
    /// The list is the author's perk_list.lua (Core NoitaPerks). Applied so far: the game effects below; Noita's perk
    /// funcs, scripts, shot modifiers and child entities are not run yet (logged per perk).
    /// </summary>
    public static class Perks
    {
        /// <summary>The perk item rides on Terraria's unused Apple Pie Slice (deprecated); its prefix = perk number + 1.</summary>
        public static readonly int[] PerkTypes = { ItemID.ApplePieSlice };
        /// <summary>Left out of the drop pool until they mean something here (design/perks.md).</summary>
        static readonly HashSet<string> LeftOut = new HashSet<string> { "EDIT_WANDS_EVERYWHERE", "PEACE_WITH_GODS", "ABILITY_ACTIONS_MATERIALIZED" };
        const int DropOnDeathAbove = 10;

        static List<NoitaPerk> _all;
        public static List<NoitaPerk> All
        {
            get
            {
                if (_all == null)
                {
                    try { _all = NoitaPerks.Read(NoitaArt.ReadText); }
                    catch (Exception ex) { Entry.Error("perk list", ex); _all = new List<NoitaPerk>(); }
                }
                return _all;
            }
        }

        public static NoitaPerk Get(string id) => All.FirstOrDefault(p => p.Id == id);
        public static bool IsPerk(Item i) => i != null && !i.IsAir && Array.IndexOf(PerkTypes, i.type) >= 0 && i.prefix > 0 && i.prefix <= All.Count;
        public static NoitaPerk PerkOf(Item i) => IsPerk(i) ? All[i.prefix - 1] : null;

        public static Item Make(string id)
        {
            int n = All.FindIndex(p => p.Id == id);
            if (n < 0 || n >= 255)
                throw new ArgumentException("not one of Noita's perks: " + id);
            var item = new Item();
            item.SetDefaults(PerkTypes[0]);
            item.prefix = (byte)(n + 1);
            return item;
        }

        public static string Name(NoitaPerk p) => p == null ? "?" : NoitaArt.Text(p.UiName, p.Id);
        public static IEnumerable<string> Lines(NoitaPerk p)
        {
            yield return NoitaArt.Text(p.UiDescription, "");
            yield return "Use: the perk is yours until you die";
        }

        // ---- the character's perks ----

        static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita", "perks");
        static string _owner;
        static readonly List<string> Mine = new List<string>();

        /// <summary>The local character's perks (ids, one per pickup), loaded when the character changes.</summary>
        public static List<string> Of(Player p)
        {
            string key = Main.ActivePlayerFileData?.Path == null ? p.name : Path.GetFileNameWithoutExtension(Main.ActivePlayerFileData.Path);
            if (key != _owner)
            {
                _owner = key;
                Mine.Clear();
                string f = File(key);
                if (System.IO.File.Exists(f))
                    Mine.AddRange(System.IO.File.ReadAllLines(f).Select(x => x.Trim()).Where(x => x.Length > 0));
            }
            return Mine;
        }

        static string File(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return Path.Combine(Folder, key + ".txt");
        }

        static void Save()
        {
            if (_owner == null)
                return;
            Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllLines(File(_owner), Mine);
        }

        public static bool Has(Player p, string id) => p.whoAmI == Main.myPlayer && Of(p).Contains(id);
        public static bool HasEffect(Player p, string effect) =>
            p.whoAmI == Main.myPlayer && Of(p).Any(id => Get(id)?.GameEffects.Contains(effect) == true);

        // ---- use from the hand ----

        static bool _useHeld;

        public static void Update(Player p, bool typing)
        {
            bool press = p.controlUseItem && !_useHeld;   // one perk per click
            _useHeld = p.controlUseItem;
            var item = p.inventory[p.selectedItem];
            var perk = PerkOf(item);
            if (perk != null && press && !typing && !p.mouseInterface && !Main.mapFullscreen && !p.CCed && !p.noItems)
            {
                Take(p, perk);
                if (!(Has(p, "PERKS_LOTTERY") && Main.rand.Next(2) == 0))
                    item.TurnToAir();
            }
        }

        static readonly HashSet<string> Told = new HashSet<string>();

        static void Take(Player p, NoitaPerk perk)
        {
            Of(p).Add(perk.Id);
            Save();
            Entry.Log("perk taken: " + perk.Id + " (" + Of(p).Count + " perks)");
            int who = Entity(p);
            if (who != 0)
                Run(p, who, perk.Id, Of(p).Count(x => x == perk.Id));
        }

        // ---- the player as Noita's perk funcs see it ----

        const string PlayerFile = "data/entities/player_base.xml";
        /// <summary>The parts of Noita's player the perk funcs read and write; its scripts, sprites and children stay out
        /// (they would act on their own in our store).</summary>
        static readonly HashSet<string> PlayerParts = new HashSet<string>
        {
            "DamageModelComponent", "CharacterDataComponent", "CharacterPlatformingComponent", "KickComponent", "WalletComponent",
            "GenomeDataComponent", "Inventory2Component", "ItemPickUpperComponent", "PlayerComponent", "CharacterStatsComponent",
            "IngestionComponent", "StatusEffectDataComponent", "MaterialSuckerComponent", "SpriteStainsComponent",
        };
        /// <summary>One-off perks act on the world or the wands once (ALWAYS_CAST, GAMBLE...): not replayed when the player
        /// entity is made again; EXTRA_HP and RESPAWN only change the player, so they are.</summary>
        static bool Replays(NoitaPerk k) => !k.OneOff || k.Id == "EXTRA_HP" || k.Id == "RESPAWN";
        static int _entity;
        static string _entityOwner;
        static float _baseMaxHp = 4;
        const float Px = Terranoita.Noita.Units.PixelScale;

        /// <summary>The character's player entity in the script store, made from Noita's player_base.xml with the
        /// character's perks run on it again in the order they were taken; 0 while Noita's files are not read.</summary>
        static int Entity(Player p)
        {
            var store = SpellShots.ScriptStore;
            if (store == null)
                return 0;
            var mine = Of(p);
            if (_entity != 0 && _entityOwner == _owner && store.Alive(_entity))
                return _entity;
            var x = NoitaEntityXml.Load(PlayerFile, NoitaArt.ReadText);
            var dm = x.Components.FirstOrDefault(c => c.Type == "DamageModelComponent");
            if (dm != null && float.TryParse(dm.Get("max_hp"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hp) && hp > 0)
                _baseMaxHp = hp;
            _entity = store.CreateEntity(x.Name, x.Tags, p.Center.X / Px, p.Center.Y / Px,
                x.Components.Where(c => PlayerParts.Contains(c.Type)).Select(c => (c.Type, (IDictionary<string, string>)c.Fields)).ToList());
            _entityOwner = _owner;
            var count = new Dictionary<string, int>();
            foreach (string id in mine.ToList())
            {
                count[id] = count.TryGetValue(id, out int n) ? n + 1 : 1;
                var k = Get(id);
                if (k != null && Replays(k))
                    Run(p, _entity, id, count[id]);
            }
            return _entity;
        }

        /// <summary>The extra_modifier of every ShotEffectComponent Noita's perk funcs put on the player (CRITICAL_HIT:
        /// critical_hit_boost...), for gun.lua on each cast.</summary>
        public static IEnumerable<string> ShotModifiers(Player p)
        {
            if (p.whoAmI != Main.myPlayer || Of(p).Count == 0)
                return Enumerable.Empty<string>();
            int who = Entity(p);
            return who == 0 ? Enumerable.Empty<string>() :
                SpellShots.ScriptStore.Components(who, "ShotEffectComponent").Select(c => c.Get("extra_modifier")).Where(m => !string.IsNullOrEmpty(m)).ToList();
        }

        static void Run(Player p, int who, string id, int pickupCount)
        {
            var store = SpellShots.ScriptStore;
            int item = store.CreateEntity("perk", "perk", p.Center.X / Px, p.Center.Y / Px, new (string, IDictionary<string, string>)[0]);
            try
            {
                if (store.RunPerk(id, who, item, pickupCount))
                    Entry.Log("perk func: " + id + " x" + pickupCount + (store.Missing.Count > 0 ? "; engine calls missing so far: " + string.Join(",", store.Missing) : ""));
            }
            catch (Exception ex)
            {
                if (Told.Add("func:" + id))
                    Entry.Error("perk func " + id, ex);
            }
            finally { store.Forget(item); }
        }

        /// <summary>Noita's game effects of the perks the player holds, as Terraria's own immunities (design/perks.md).</summary>
        public static void Effects(Player p)
        {
            if (p.whoAmI != Main.myPlayer || Of(p).Count == 0)
                return;
            var store = SpellShots.ScriptStore;
            int who = Entity(p);
            var effects = new HashSet<string>(Of(p).SelectMany(id => Get(id)?.GameEffects ?? new List<string>()));
            if (who != 0)
            {
                store.Place(who, p.Center.X / Px, p.Center.Y / Px);
                // what the funcs did to Noita's player: game effects they added, max hp (Terraria life scaled by the same
                // share: adapted, Noita's hp has no Terraria twin)
                foreach (var c in store.Components(who, "GameEffectComponent"))
                    if (!string.IsNullOrEmpty(c.Get("effect")))
                        effects.Add(c.Get("effect"));
                var dm = store.Components(who, "DamageModelComponent", false).FirstOrDefault();
                float max = dm?.Float("max_hp", _baseMaxHp) ?? _baseMaxHp;
                if (max > 0 && Math.Abs(max - _baseMaxHp) > 0.001f)
                    p.statLifeMax2 = Math.Max(1, (int)(p.statLifeMax2 * max / _baseMaxHp));
            }
            {

                foreach (string e in effects)
                {
                    switch (e)
                    {
                        case "PROTECTION_FIRE":
                            p.buffImmune[BuffID.OnFire] = p.buffImmune[BuffID.OnFire3] = p.buffImmune[BuffID.Burning] = true;
                            p.fireWalk = true;
                            p.lavaImmune = true;
                            break;
                        case "PROTECTION_RADIOACTIVITY":
                            p.buffImmune[BuffID.Poisoned] = p.buffImmune[BuffID.Venom] = true;
                            break;
                        case "PROTECTION_ELECTRICITY":
                            p.buffImmune[BuffID.Electrified] = true;
                            break;
                        case "PROTECTION_FREEZE":
                            p.buffImmune[BuffID.Frozen] = p.buffImmune[BuffID.Chilled] = true;
                            break;
                        case "BREATH_UNDERWATER":
                            p.gills = true;
                            break;
                        case "KNOCKBACK_IMMUNITY":
                            p.noKnockback = true;
                            break;
                        default:
                            if (Told.Add("effect:" + e))
                                Entry.Log("perk effect not done yet: " + e);
                            break;
                    }
                }
            }
        }

        // ---- for the perk test (PerkTest) ----

        internal static void TestTake(Player p, string id) => Take(p, Get(id));
        internal static NoitaPerk RandomFor(Player p) => Random(p);
        internal static void TestClear(Player p)
        {
            Of(p).Clear();
            Save();
            if (_entity != 0)
                SpellShots.ScriptStore?.Forget(_entity);
            _entity = 0;
        }

        // ---- on screen ----

        /// <summary>The character's perks as Noita's ui_icon row (one per perk taken, stacks shown once with a count) under
        /// the buff icons; the mouse over one shows its name and what it does.</summary>
        public static void DrawIcons()
        {
            var p = Main.LocalPlayer;
            if (Main.gameMenu || p == null || !p.active || Main.mapFullscreen || Of(p).Count == 0)
                return;
            var sb = Main.spriteBatch;
            int x = 32, y = 150, size = 24;
            foreach (var g in Of(p).GroupBy(id => id))
            {
                var perk = Get(g.Key);
                var art = perk == null ? null : NoitaArt.Get(perk.UiIcon);
                if (art?.Texture == null)
                    continue;
                var r = new Microsoft.Xna.Framework.Rectangle(x, y, size, size);
                sb.Draw(art.Texture, r, Microsoft.Xna.Framework.Color.White);
                if (g.Count() > 1)
                    Terraria.Utils.DrawBorderString(sb, g.Count().ToString(), new Microsoft.Xna.Framework.Vector2(x + size - 6, y + size - 10), Microsoft.Xna.Framework.Color.White, 0.7f);
                if (r.Contains(Main.mouseX, Main.mouseY))
                {
                    p.mouseInterface = true;
                    Main.instance.MouseText(Name(perk) + "\n" + NoitaArt.Text(perk.UiDescription, ""));
                }
                x += size + 4;
                if (x > 32 + 10 * (size + 4))
                {
                    x = 32;
                    y += size + 4;
                }
            }
        }

        // ---- drops ----

        static int _lastBossDrop = -1000;

        /// <summary>A Terraria boss died: random perk items, more on harder worlds (author's design).</summary>
        public static void BossDrop(NPC npc)
        {
            if (!npc.boss || Main.netMode == 1)
                return;
            // worm bosses and twins die in pieces close together: one drop for them
            if (Main.GameUpdateCount - _lastBossDrop < 120)
                return;
            if ((npc.type == NPCID.EaterofWorldsHead || npc.type == NPCID.EaterofWorldsBody || npc.type == NPCID.EaterofWorldsTail) &&
                Main.npc.Any(n => n.active && n.whoAmI != npc.whoAmI && (n.type == NPCID.EaterofWorldsHead || n.type == NPCID.EaterofWorldsBody || n.type == NPCID.EaterofWorldsTail)))
                return;
            _lastBossDrop = (int)Main.GameUpdateCount;
            int count = 1 + (Main.expertMode ? 1 : 0) + (Main.masterMode ? 1 : 0) + (Main.getGoodWorld ? 1 : 0) + Of(Main.LocalPlayer).Count(x => x == "EXTRA_PERK");
            for (int k = 0; k < count; k++)
            {
                var perk = Random(Main.LocalPlayer);
                if (perk == null)
                    break;
                Drop(perk.Id, npc.position, npc.width, npc.height, new EntitySource_Loot(npc));
            }
        }

        static NoitaPerk Random(Player p)
        {
            var pool = All.Where(x => !x.NotInDefaultPool && !LeftOut.Contains(x.Id) && (x.Stackable || !Of(p).Contains(x.Id))).ToList();
            return pool.Count == 0 ? null : pool[Main.rand.Next(pool.Count)];
        }

        static void Drop(string id, Microsoft.Xna.Framework.Vector2 pos, int w, int h, IEntitySource src)
        {
            var item = Make(id);
            int at = Item.NewItem(src, (int)pos.X, (int)pos.Y, w, h, item.type, 1, false, item.prefix);
            if (at >= 0 && at < Main.maxItems)
                Main.item[at].prefix = item.prefix;
            Entry.Log("perk drop: " + id);
        }

        [Hook("perk_death")]
        [HarmonyPatch(typeof(Player), nameof(Player.KillMe))]
        static class DeathPatch
        {
            // all perks are lost; with more than 10 a quarter of them (random) drop where the player died, like coins
            static void Postfix(Player __instance)
            {
                try
                {
                    if (__instance.whoAmI != Main.myPlayer || !__instance.dead)
                        return;
                    var mine = Of(__instance);
                    if (mine.Count == 0)
                        return;
                    if (mine.Count > DropOnDeathAbove)
                        foreach (string id in mine.OrderBy(_ => Main.rand.Next()).Take(mine.Count / 4).ToList())
                            Drop(id, __instance.position, __instance.width, __instance.height, new EntitySource_Parent(__instance));
                    Entry.Log("perks lost on death: " + mine.Count);
                    mine.Clear();
                    Save();
                    if (_entity != 0)
                        SpellShots.ScriptStore?.Forget(_entity);   // Noita's player as it was before any perk, next time
                    _entity = 0;
                }
                catch (Exception ex) { Entry.Error("perks on death", ex); }
            }
        }
    }
}
