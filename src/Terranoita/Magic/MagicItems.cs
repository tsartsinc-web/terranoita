using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita spells and wands as Terraria items, so they live in the inventory, chests and on the ground and are saved
    /// by Terraria itself. They ride on unused item types (placeholders without a name, no longer marked deprecated so
    /// loading keeps them); the item's prefix byte holds which spell or wand (1..255 per type).
    /// </summary>
    public static class MagicItems
    {
        public static readonly int[] SpellTypes = { 3847, 3848 };
        public static readonly int[] WandTypes = { 6143, 3849, 3850, 3851, 3861, 3862 };

        public static void Init()
        {
            foreach (int t in SpellTypes.Concat(WandTypes))
                ItemID.Sets.Deprecated[t] = false;
        }

        /// <summary>Tests: how often loading handed a spell or wand number to Prefix, and the last one.</summary>
        public static int PrefixCalls, LastPrefix;

        public static bool IsSpell(Item i) => i != null && !i.IsAir && Array.IndexOf(SpellTypes, i.type) >= 0 && i.prefix > 0;
        public static bool IsWand(Item i) => i != null && !i.IsAir && Array.IndexOf(WandTypes, i.type) >= 0 && i.prefix > 0;
        static bool IsCarrier(int type) => Array.IndexOf(SpellTypes, type) >= 0 || Array.IndexOf(WandTypes, type) >= 0;

        public static string SpellOf(Item i) =>
            IsSpell(i) ? WandStore.SpellId(Array.IndexOf(SpellTypes, i.type) * 255 + i.prefix - 1) : null;

        public static WandData WandOf(Item i) =>
            IsWand(i) ? WandStore.Wand(Array.IndexOf(WandTypes, i.type) * 255 + i.prefix - 1) : null;

        public static Item MakeSpell(string actionId)
        {
            int n = WandStore.SpellNumber(actionId);
            var item = new Item();
            item.SetDefaults(SpellTypes[Math.Min(SpellTypes.Length - 1, n / 255)]);
            item.prefix = (byte)(n % 255 + 1);
            return item;
        }

        public static Item MakeWand(WandData w)
        {
            var item = new Item();
            item.SetDefaults(WandTypes[Math.Min(WandTypes.Length - 1, w.Id / 255)]);
            item.prefix = (byte)(w.Id % 255 + 1);
            return item;
        }

        // ---- what a spell is (the spells sheet, read from the player's gun_actions.lua) ----

        static Dictionary<string, SpellDef> _spells;

        public static SpellDef Spell(string id)
        {
            if (_spells == null)
                _spells = SpellTable.All.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            return id != null && _spells.TryGetValue(id, out var d) ? d : null;
        }

        public static string SpellName(string id)
        {
            var d = Spell(id);
            return d == null ? id ?? "?" : NoitaArt.Text(d.NameKey, d.NameEn ?? id);
        }

        public static string WandName(WandData w) =>
            string.IsNullOrWhiteSpace(w?.Name) ? NoitaArt.Text("$item_wand", "Wand") : NoitaArt.Text(w.Name.Trim(), w.Name.Trim());

        static Texture2D Icon(Item item)
        {
            if (IsSpell(item))
                return NoitaArt.Get(Spell(SpellOf(item))?.Sprite)?.Texture;
            if (IsWand(item))
                return NoitaArt.Get(WandOf(item)?.Sprite)?.Texture;
            return null;
        }

        /// <summary>The first frame of a wand or spell picture (wand xml sprites hold several).</summary>
        public static Rectangle Frame(NoitaArt.Art art)
        {
            var anim = art?.Sprite?.Find("default", "stand");
            if (anim == null || art.Texture == null)
                return art?.Texture == null ? Rectangle.Empty : new Rectangle(0, 0, art.Texture.Width, art.Texture.Height);
            anim.FrameRect(0, out int x, out int y, out int w, out int h);
            return new Rectangle(x, y, w, h);
        }

        static NoitaArt.Art ArtOf(Item item) =>
            IsSpell(item) ? NoitaArt.Get(Spell(SpellOf(item))?.Sprite) : IsWand(item) ? NoitaArt.Get(WandOf(item)?.Sprite) : null;

        // ---- patches ----

        [Hook("magic_item_defaults")]
        [HarmonyPatch(typeof(Item), nameof(Item.SetDefaults), new[] { typeof(int), typeof(Terraria.GameContent.Items.ItemVariant) })]
        static class DefaultsPatch
        {
            static void Postfix(Item __instance, int Type)
            {
                if (!IsCarrier(Type))
                    return;
                __instance.maxStack = 1;
                __instance.width = __instance.height = 20;
                __instance.rare = Array.IndexOf(WandTypes, Type) >= 0 ? 2 : 1;
                __instance.value = 0;
                __instance.useStyle = 0;
                __instance.noMelee = true;
                __instance.material = false;
            }
        }

        [Hook("magic_item_prefix")]
        [HarmonyPatch(typeof(Item), nameof(Item.Prefix), new[] { typeof(int) })]
        static class PrefixPatch
        {
            // the prefix byte is which spell or wand: kept as loaded, never rolled or rejected
            static bool Prefix(Item __instance, int prefixWeWant, ref bool __result)
            {
                if (!IsCarrier(__instance.type))
                    return true;
                PrefixCalls++;
                LastPrefix = prefixWeWant;
                if (prefixWeWant > 0 && prefixWeWant < 256)
                    __instance.prefix = (byte)prefixWeWant;
                __result = true;
                return false;
            }
        }

        [Hook("magic_item_prefix2")]
        [HarmonyPatch(typeof(Item), nameof(Item.Prefix), new[] { typeof(int), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
        static class PrefixPatch2
        {
            // the overload loading uses (Item.DeserializeFrom): the same rule
            static bool Prefix(Item __instance, int prefixWeWant, ref bool __result)
            {
                if (!IsCarrier(__instance.type))
                    return true;
                PrefixCalls++;
                LastPrefix = prefixWeWant;
                if (prefixWeWant > 0 && prefixWeWant < 256)
                    __instance.prefix = (byte)prefixWeWant;
                __result = true;
                return false;
            }
        }

        [Hook("magic_item_rollprefix")]
        [HarmonyPatch(typeof(Item), nameof(Item.CanRollPrefix))]
        static class CanRollPatch
        {
            // Item.FixAgainstExploit (run on every loaded item) clears a prefix the item could not roll: ours is a number
            static bool Prefix(Item __instance, ref bool __result)
            {
                if (!IsCarrier(__instance.type))
                    return true;
                __result = true;
                return false;
            }
        }

        [Hook("magic_item_name")]
        [HarmonyPatch(typeof(Item), nameof(Item.Name), MethodType.Getter)]
        static class NamePatch
        {
            static void Postfix(Item __instance, ref string __result)
            {
                if (IsSpell(__instance))
                    __result = SpellName(SpellOf(__instance));
                else if (IsWand(__instance))
                    __result = WandName(WandOf(__instance));
            }
        }

        [Hook("magic_item_affix")]
        [HarmonyPatch(typeof(Item), nameof(Item.AffixName))]
        static class AffixPatch
        {
            static void Postfix(Item __instance, ref string __result)
            {
                if (IsSpell(__instance) || IsWand(__instance))
                    __result = __instance.Name;
            }
        }

        [Hook("magic_item_icon")]
        [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.DrawItemIcon))]
        static class IconPatch
        {
            static bool Prefix(Item item, SpriteBatch spriteBatch, Vector2 screenPositionForItemCenter, float scale, float sizeLimit, Color environmentColor, ref float __result)
            {
                if (!IsSpell(item) && !IsWand(item))
                    return true;
                try
                {
                    var art = ArtOf(item);
                    if (art?.Texture == null)
                        return true;
                    var frame = Frame(art);
                    float s = Math.Min(sizeLimit / Math.Max(frame.Width, frame.Height), 3f) * scale;
                    spriteBatch.Draw(art.Texture, screenPositionForItemCenter, frame, Color.White, 0f, new Vector2(frame.Width / 2f, frame.Height / 2f), s, SpriteEffects.None, 0f);
                    __result = s;
                    return false;
                }
                catch (Exception ex) { Entry.Error("magic icon", ex); return true; }
            }
        }

        [Hook("magic_item_world")]
        [HarmonyPatch(typeof(Main), "DrawItem")]
        static class WorldDrawPatch
        {
            static bool Prefix(WorldItem item)
            {
                var inner = item?.inner;
                if (!IsSpell(inner) && !IsWand(inner))
                    return true;
                try
                {
                    var art = ArtOf(inner);
                    if (art?.Texture == null)
                        return true;
                    var frame = Frame(art);
                    var light = Lighting.GetColor((int)(item.Center.X / 16), (int)(item.Center.Y / 16));
                    float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 2f;
                    Main.spriteBatch.Draw(art.Texture, item.Center - Main.screenPosition + new Vector2(0, bob), frame, light, 0f,
                                          new Vector2(frame.Width / 2f, frame.Height / 2f), 2f, SpriteEffects.None, 0f);
                    return false;
                }
                catch (Exception ex) { Entry.Error("magic world item", ex); return true; }
            }
        }

        [Hook("magic_item_tooltip")]
        [HarmonyPatch(typeof(Main), "MouseText_DrawItemTooltip_GetLinesInfo")]
        static class TooltipPatch
        {
            static void Postfix(Item item, ref int numLines, string[] toolTipLine, Color[] lineColors)
            {
                try
                {
                    var lines = IsSpell(item) ? SpellLines(SpellOf(item)) : IsWand(item) ? WandLines(WandOf(item)) : null;
                    if (lines == null)
                        return;
                    numLines = 1;
                    foreach (var l in lines)
                    {
                        if (numLines >= toolTipLine.Length)
                            break;
                        toolTipLine[numLines] = l;
                        lineColors[numLines] = Color.White;
                        numLines++;
                    }
                }
                catch (Exception ex) { Entry.Error("magic tooltip", ex); }
            }
        }

        public static List<string> SpellLines(string id)
        {
            var d = Spell(id);
            var l = new List<string>();
            if (d == null)
                return l;
            l.Add(NoitaArt.Text("$inventory_actiontype", "Type") + ": " + d.Type);
            l.Add(NoitaArt.Text("$inventory_manadrain", "Mana drain") + ": " + d.Mana);
            if (d.MaxUses > 0)
                l.Add(NoitaArt.Text("$inventory_usesremaining", "Uses") + ": " + d.MaxUses);
            return l;
        }

        public static List<string> WandLines(WandData w)
        {
            var l = new List<string>();
            if (w == null)
                return l;
            l.Add(NoitaArt.Text("$inventory_shuffle", "Shuffle") + ": " + (w.Shuffle ? NoitaArt.Text("$menu_yes", "Yes") : NoitaArt.Text("$menu_no", "No")));
            l.Add(NoitaArt.Text("$inventory_actionspercast", "Spells/Cast") + ": " + w.SpellsPerCast);
            l.Add(NoitaArt.Text("$inventory_castdelay", "Cast delay") + ": " + (w.CastDelay / 60f).ToString("0.00") + " s");
            l.Add(NoitaArt.Text("$inventory_rechargetime", "Rechrg. Time") + ": " + (w.RechargeTime / 60f).ToString("0.00") + " s");
            l.Add(NoitaArt.Text("$inventory_capacity", "Capacity") + ": " + w.Capacity);
            l.Add(NoitaArt.Text("$inventory_spread", "Spread") + ": " + w.Spread.ToString("0.#") + " DEG");
            var spells = w.Slots.Where(s => s != null).Select(SpellName).ToList();
            if (w.AlwaysCast.Count > 0)
                l.Add(NoitaArt.Text("$inventory_alwayscasts", "Always casts") + ": " + string.Join(", ", w.AlwaysCast.Select(SpellName)));
            if (spells.Count > 0)
                l.Add(string.Join(", ", spells));
            return l;
        }
    }
}
