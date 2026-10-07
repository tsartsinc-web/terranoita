using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using HarmonyLib;
using Terranoita.Noita;
using Terraria;
using Terraria.UI;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// The wand window (key U, author): Noita's inventory for wands. Rows for the wand in hand and the 4 wand slots
    /// (wands move between them and Terraria's inventory like ammo or coins); each row shows the wand's stats with
    /// Noita's icons and its spell slots, where spells are dragged in and out. Drawn with Noita's own UI pictures, or
    /// with Terraria's slots (button in the window, remembered).
    /// </summary>
    public static class WandWindow
    {
        public const int WandSlotCount = 4;
        public static Item[] WandSlots = NewSlots();
        static bool _open, _terrariaLook;
        static Rectangle _lookRect;
        static Texture2D _pixel;
        static string _slotsFile;
        const float S = 2f;                         // Noita UI pixels -> screen pixels

        static Item[] NewSlots() => Enumerable.Range(0, WandSlotCount).Select(_ => new Item()).ToArray();
        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita");
        static string LookFile => Path.Combine(Folder, "wand_window.txt");

        // ---- wand slots of the character, and the starting wands ----

        public static void EnterWorld(Player p)
        {
            if (p.whoAmI != Main.myPlayer)
                return;
            try
            {
                try { _terrariaLook = File.Exists(LookFile) && File.ReadAllText(LookFile).Trim() == "terraria"; } catch { }
                string name = Path.GetFileNameWithoutExtension(Main.ActivePlayerFileData?.Path ?? p.name);
                Directory.CreateDirectory(Path.Combine(Folder, "players"));
                _slotsFile = Path.Combine(Folder, "players", name + ".wands");
                WandSlots = NewSlots();
                if (File.Exists(_slotsFile))
                {
                    var lines = File.ReadAllLines(_slotsFile);
                    for (int i = 0; i < WandSlotCount && i < lines.Length; i++)
                        if (int.TryParse(lines[i].Trim(), out int id) && WandStore.Wand(id) != null)
                            WandSlots[i] = MagicItems.MakeWand(WandStore.Wand(id));
                    return;
                }
                StartingWands(p);
                SaveSlots();
            }
            catch (Exception ex) { Entry.Error("wand slots", ex); }
        }

        public static void SaveSlots()
        {
            if (_slotsFile == null)
                return;
            try
            {
                File.WriteAllLines(_slotsFile, WandSlots.Select(it => MagicItems.WandOf(it)?.Id.ToString() ?? "-1"));
            }
            catch (Exception ex) { Entry.Error("wand slots save", ex); }
        }

        /// <summary>Noita's two starting wands, made by Noita's own scripts: the bolt staff in hand, the bomb wand in a wand slot.</summary>
        static void StartingWands(Player p)
        {
            var maker = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next());
            var bolt = Store(maker.Make("data/scripts/gun/procedural/starting_wand.lua", p.Center.X / 3, p.Center.Y / 3), "data/items_gfx/handgun.xml");
            var bomb = Store(maker.Make("data/scripts/gun/procedural/starting_bomb_wand.lua", p.Center.X / 3, p.Center.Y / 3), "data/items_gfx/bomb_wand.xml");
            int free = Enumerable.Range(0, 10).FirstOrDefault(i => p.inventory[i].IsAir);
            if (p.inventory[free].IsAir)
                p.inventory[free] = MagicItems.MakeWand(bolt);
            else
                WandSlots[1] = MagicItems.MakeWand(bolt);
            WandSlots[0] = MagicItems.MakeWand(bomb);
            Entry.Log("starting wands: " + MagicItems.WandName(bolt) + " (" + string.Join(" ", bolt.Slots) + "), " + MagicItems.WandName(bomb));
        }

        /// <summary>A wand made by a Noita script, kept in the wand store.</summary>
        public static WandData Store(MadeWand m, string defaultSprite = "data/items_gfx/handgun.xml")
        {
            var w = WandStore.NewWand();
            w.Name = m.Name; w.Sprite = string.IsNullOrEmpty(m.Sprite) ? defaultSprite : m.Sprite;
            w.SpellsPerCast = m.SpellsPerCast; w.Shuffle = m.Shuffle; w.CastDelay = m.CastDelay; w.RechargeTime = m.RechargeTime;
            w.ManaMax = m.ManaMax; w.ManaChargeSpeed = m.ManaChargeSpeed; w.Spread = m.Spread; w.SpeedMultiplier = m.SpeedMultiplier;
            w.AlwaysCast = m.AlwaysCast.ToList();
            int cap = Math.Max(Math.Max(1, m.Capacity), m.Spells.Count);
            w.Slots = new string[Math.Min(26, cap)];
            w.Uses = new int[w.Slots.Length];
            for (int i = 0; i < w.Slots.Length; i++)
            {
                w.Slots[i] = i < m.Spells.Count ? m.Spells[i] : null;
                w.Uses[i] = w.Slots[i] == null ? -1 : (MagicItems.Spell(w.Slots[i])?.MaxUses ?? -1);
            }
            WandStore.Save();
            return w;
        }

        // ---- the window ----

        /// <summary>Tests: open or close the window (Noita or Terraria look).</summary>
        public static void TestOpen(bool open, bool terrariaLook)
        {
            _open = open;
            _terrariaLook = terrariaLook;
            Main.playerInventory = open;
        }

        public static void Update()
        {
            if (Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.LocalPlayer?.active != true)
                return;
            if (Main.keyState.IsKeyDown(Keys.U) && !Main.oldKeyState.IsKeyDown(Keys.U))
            {
                _open = !_open;
                if (_open)
                    Main.playerInventory = true;
                Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
            }
            if (_open && !Main.playerInventory)
                _open = false;   // closing the inventory closes the window
        }

        static NoitaArt.Art Ui(string name) => NoitaArt.Get("data/ui_gfx/inventory/" + name + ".png");

        static void Draw()
        {
            if (!_open || Main.gameMenu || !NoitaArt.Ready)
                return;
            var p = Main.LocalPlayer;
            var sb = Main.spriteBatch;
            int x0 = 20, y = Main.screenHeight / 2 - 40;
            var rows = new List<(string label, Item[] arr, int index)>();
            if (Casting.HeldWand(p) != null)
                rows.Add(("", p.inventory, p.selectedItem));
            for (int i = 0; i < WandSlotCount; i++)
                rows.Add(((i + 1).ToString(), WandSlots, i));

            // look switch
            string look = _terrariaLook ? "[Terraria]" : "[Noita]";
            var lookPos = new Vector2(x0, y - 26);
            bool overLook = _lookRect.Contains(Main.mouseX, Main.mouseY);
            Utils.DrawBorderString(sb, look, lookPos, overLook ? Color.Yellow : Color.White, 0.8f);
            _lookRect = new Rectangle((int)lookPos.X, (int)lookPos.Y, look.Length * 9, 20);
            if (overLook)
            {
                p.mouseInterface = true;
                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    _terrariaLook = !_terrariaLook;
                    try { Directory.CreateDirectory(Folder); File.WriteAllText(LookFile, _terrariaLook ? "terraria" : "noita"); } catch { }
                }
            }

            foreach (var (label, arr, index) in rows)
                y += Row(sb, p, label, arr, index, x0, y) + 6;
        }

        /// <summary>One wand: its box, stats and spell slots; the height used.</summary>
        static int Row(SpriteBatch sb, Player p, string label, Item[] arr, int index, int x, int y)
        {
            int box = (int)(20 * S);
            // the wand box: only wands go in (and out to the inventory, like ammo)
            var wandRect = new Rectangle(x, y, box, box);
            DrawBox(sb, wandRect, arr[index], arr == WandSlots ? "full_inventory_box" : "quick_inventory_box");
            if (label.Length > 0)
                Utils.DrawBorderString(sb, label, new Vector2(x + 2, y + 1), Color.White * 0.8f, 0.6f);
            if (wandRect.Contains(Main.mouseX, Main.mouseY))
            {
                p.mouseInterface = true;
                if (arr == WandSlots && (Main.mouseItem.IsAir || MagicItems.IsWand(Main.mouseItem)))
                {
                    var before = arr[index].type + ":" + arr[index].prefix;
                    ItemSlot.Handle(arr, ItemSlot.Context.ChestItem, index, true);
                    if (before != arr[index].type + ":" + arr[index].prefix)
                        SaveSlots();
                }
                else
                    ItemSlot.MouseHover(arr, ItemSlot.Context.ChestItem, index);
            }
            var w = MagicItems.WandOf(arr[index]);
            if (w == null)
                return box;

            // stats with Noita's icons
            int sx = x + box + 8, sy = y;
            var stats = new (string icon, string text)[]
            {
                ("icon_gun_shuffle", w.Shuffle ? NoitaArt.Text("$menu_yes", "Yes") : NoitaArt.Text("$menu_no", "No")),
                ("icon_gun_actions_per_round", w.SpellsPerCast.ToString()),
                ("icon_fire_rate_wait", (w.CastDelay / 60f).ToString("0.00") + "s"),
                ("icon_gun_reload_time", (w.RechargeTime / 60f).ToString("0.00") + "s"),
                ("icon_gun_capacity", w.Capacity.ToString()),
                ("icon_spread_degrees", w.Spread.ToString("0.#") + "°"),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                int cx = sx + (i / 3) * 70, cy = sy + (i % 3) * 14;
                var icon = Ui(stats[i].icon);
                if (icon?.Texture != null)
                    sb.Draw(icon.Texture, new Vector2(cx, cy + 1), null, Color.White, 0f, Vector2.Zero, 1.5f, SpriteEffects.None, 0f);
                Utils.DrawBorderString(sb, stats[i].text, new Vector2(cx + 14, cy), Color.White, 0.65f);
            }
            float charge = Casting.Recharging(w.Id);
            if (charge >= 0)
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(Main.instance.GraphicsDevice, 1, 1);
                    _pixel.SetData(new[] { Color.White });
                }
                sb.Draw(_pixel, new Rectangle(sx, sy + 44, (int)(130 * charge), 3), Color.LightBlue);
            }

            // always-cast spells, then the slots
            int px = sx + 150, py = y, slot = (int)(20 * S * 0.9f);
            foreach (var ac in w.AlwaysCast)
            {
                var r = new Rectangle(px, py, slot, slot);
                DrawSpellBox(sb, r, ac, true);
                if (r.Contains(Main.mouseX, Main.mouseY))
                {
                    p.mouseInterface = true;
                    Main.hoverItemName = NoitaArt.Text("$inventory_alwayscasts", "Always casts") + ": " + MagicItems.SpellName(ac);
                }
                px += slot + 2;
            }
            if (w.AlwaysCast.Count > 0)
                px += 6;
            int perRow = Math.Max(4, (Main.screenWidth - px - 20) / (slot + 2));
            bool changed = false;
            for (int i = 0; i < w.Slots.Length; i++)
            {
                var r = new Rectangle(px + (i % perRow) * (slot + 2), py + (i / perRow) * (slot + 2), slot, slot);
                DrawSpellBox(sb, r, w.Slots[i], false);
                if (!r.Contains(Main.mouseX, Main.mouseY))
                    continue;
                p.mouseInterface = true;
                changed |= SpellSlot(p, w, i);
            }
            if (changed)
            {
                WandStore.Save();
                Casting.Changed(w);
            }
            int rowsUsed = (w.Slots.Length + perRow - 1) / perRow;
            return Math.Max(box, Math.Max(46, rowsUsed * (slot + 2)));
        }

        /// <summary>Click on a spell slot: put the spell in the hand into it, take it out, or swap. True if changed.</summary>
        static bool SpellSlot(Player p, WandData w, int i)
        {
            var inSlot = w.Slots[i] == null ? new Item() : MagicItems.MakeSpell(w.Slots[i]);
            var arr = new[] { inSlot };
            if (!inSlot.IsAir)
                ItemSlot.MouseHover(arr, ItemSlot.Context.ChestItem, 0);
            if (!(Main.mouseLeft && Main.mouseLeftRelease))
                return false;
            var hand = Main.mouseItem;
            if (!hand.IsAir && !MagicItems.IsSpell(hand))
                return false;
            if (Main.keyState.IsKeyDown(Keys.LeftShift) && !inSlot.IsAir && hand.IsAir)
            {
                // shift-click: the spell goes back to the inventory (if there is room)
                int free = Enumerable.Range(0, 50).FirstOrDefault(k => p.inventory[k].IsAir);
                if (!p.inventory[free].IsAir)
                    return false;
                p.inventory[free] = inSlot;
                w.Slots[i] = null;
                w.Uses[i] = -1;
                return true;
            }
            string handSpell = MagicItems.SpellOf(hand);
            int handUses = handSpell == null ? -1 : (MagicItems.Spell(handSpell)?.MaxUses ?? -1);
            Main.mouseItem = inSlot.IsAir ? new Item() : inSlot;
            w.Slots[i] = handSpell;
            w.Uses[i] = handUses;
            Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.Grab);
            return true;
        }

        static void DrawBox(SpriteBatch sb, Rectangle r, Item item, string frame)
        {
            if (_terrariaLook)
            {
                var arr = new[] { item };
                float old = Main.inventoryScale;
                Main.inventoryScale = r.Width / 52f;
                ItemSlot.Draw(sb, arr, ItemSlot.Context.ChestItem, 0, new Vector2(r.X, r.Y), default(Color));
                Main.inventoryScale = old;
                return;
            }
            var bg = Ui(frame);
            if (bg?.Texture != null)
                sb.Draw(bg.Texture, r, Color.White);
            if (!item.IsAir)
                ItemSlot.DrawItemIcon(item, ItemSlot.Context.ChestItem, sb, new Vector2(r.Center.X, r.Center.Y), 1f, r.Width * 0.8f, Color.White, 1f, false);
        }

        static void DrawSpellBox(SpriteBatch sb, Rectangle r, string spell, bool always)
        {
            if (_terrariaLook)
            {
                DrawBox(sb, r, spell == null ? new Item() : MagicItems.MakeSpell(spell), "inventory_box");
                return;
            }
            var frame = Ui(always ? "full_inventory_box" : "inventory_box");
            if (frame?.Texture != null)
                sb.Draw(frame.Texture, r, Color.White);
            if (spell == null)
                return;
            // Noita's coloured background for the spell's type, then its icon
            var bg = Ui("item_bg_" + (MagicItems.Spell(spell)?.Type ?? "projectile"));
            if (bg?.Texture != null)
                sb.Draw(bg.Texture, r, Color.White);
            var art = NoitaArt.Get(MagicItems.Spell(spell)?.Sprite);
            if (art?.Texture != null)
            {
                var f = MagicItems.Frame(art);
                sb.Draw(art.Texture, new Rectangle(r.X + r.Width / 10, r.Y + r.Height / 10, r.Width * 8 / 10, r.Height * 8 / 10), f, Color.White);
            }
        }

        [Hook("wand_window_draw")]
        [HarmonyPatch(typeof(Main), "DrawInterface_27_Inventory")]
        static class DrawPatch
        {
            static void Postfix()
            {
                try { Draw(); }
                catch (Exception ex) { Entry.Error("wand window", ex); }
            }
        }

        [Hook("wand_enter_world")]
        [HarmonyPatch(typeof(Player.Hooks), nameof(Player.Hooks.EnterWorld))]
        static class EnterWorldPatch
        {
            static void Postfix(int playerIndex)
            {
                if (playerIndex == Main.myPlayer)
                    EnterWorld(Main.player[playerIndex]);
            }
        }
    }
}
