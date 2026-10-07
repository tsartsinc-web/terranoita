using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terranoita.Game.Magic;
using Terranoita.Generated;
using Terranoita.Progress;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Noita's Progress menu, per character (author, design/progress_window.md): what the player has met, in tabs
    /// (spells, creatures, wands, liquids). Creatures count when the player kills them, liquids when touched, wands
    /// and spells when taken. Key O or the button by the Bestiary; does not pause. The book is Core's ProgressBook.
    /// </summary>
    public static class ProgressWindow
    {
        public static ProgressBook Book;
        static string _file;
        static uint _savedAt;
        static bool _open;
        static int _tab, _scroll;
        static Texture2D _pixel;
        static readonly string[] Tabs = { ProgressBook.Spells, ProgressBook.Creatures, ProgressBook.Wands, ProgressBook.Liquids };
        static readonly string[] TabKeys = { "$menu_progress_spells", "$menu_progress_enemies", "$menu_progress_wands", "$menu_progress_materials" };
        static readonly string[] TabNames = { "Spells", "Creatures", "Wands", "Liquids" };
        static List<string> _wandSprites;
        static Rectangle _button;

        static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita");

        // ---- the book's life ----

        static void EnterWorld(Player p)
        {
            try
            {
                _file = ProgressBook.FileFor(Folder, Main.ActivePlayerFileData?.Path, p.name);
                Book = ProgressBook.Load(_file);
                foreach (var id in Physics.Fluids.KnownIds)   // liquids learnt before the book existed
                    Book.See(ProgressBook.Liquids, id);
                Book.Discovered += Discovered;
                _savedAt = Main.GameUpdateCount;
            }
            catch (Exception ex) { Entry.Error("progress load", ex); }
        }

        static void Save()
        {
            if (Book == null || !Book.Dirty || _file == null)
                return;
            try { Book.Save(_file); }
            catch (Exception ex) { Entry.Error("progress save", ex); }
            _savedAt = Main.GameUpdateCount;
        }

        /// <summary>Something met for the first time: its name rises over the player, as Noita's "new" notes.</summary>
        static void Discovered(string category, string id)
        {
            var p = Main.LocalPlayer;
            if (p == null || !p.active || Main.gameMenu)
                return;
            CombatText.NewText(p.getRect(), new Color(255, 220, 120), "+ " + NameOf(category, id), false, false);
        }

        public static void Kill(string creature) => Book?.Count(ProgressBook.Creatures, creature, "kills");
        public static void Touch(string liquid) => Book?.See(ProgressBook.Liquids, liquid);
        public static void Cast(string spell) => Book?.Count(ProgressBook.Spells, spell, "casts");

        static void TakeWand(WandData w)
        {
            if (w == null)
                return;
            Book.See(ProgressBook.Wands, w.Sprite);
            foreach (var s in w.Slots.Concat(w.AlwaysCast))
                if (s != null)
                    Book.See(ProgressBook.Spells, s);
        }

        /// <summary>Wands and spells the player holds (inventory, hand, wand and spell slots) count as taken.</summary>
        static void ScanTaken()
        {
            var p = Main.LocalPlayer;
            foreach (var it in p.inventory.Concat(new[] { Main.mouseItem }).Concat(WandWindow.WandSlots).Concat(WandWindow.SpellSlots))
            {
                if (it == null || it.IsAir)
                    continue;
                if (MagicItems.IsWand(it))
                    TakeWand(MagicItems.WandOf(it));
                else if (MagicItems.IsSpell(it))
                    Book.See(ProgressBook.Spells, MagicItems.SpellOf(it));
            }
        }

        public static void Update()
        {
            if (Main.gameMenu)
            {
                if (Book != null)
                {
                    Save();
                    Book = null;
                    _open = false;
                }
                return;
            }
            if (Book == null || Main.LocalPlayer?.active != true)
                return;
            if (!Main.drawingPlayerChat && !Main.editSign && !Main.editChest &&
                Main.keyState.IsKeyDown(Keys.O) && !Main.oldKeyState.IsKeyDown(Keys.O))
                Toggle();
            if (Main.GameUpdateCount % 30 == 0)
                ScanTaken();
            if (Main.GameUpdateCount - _savedAt > 7200)
                Save();
        }

        /// <summary>Tests: open on a tab, or close.</summary>
        public static void TestOpen(bool open, int tab)
        {
            _open = open;
            _tab = tab;
            _scroll = 0;
        }

        static void Toggle()
        {
            _open = !_open;
            _scroll = 0;
            if (!_open)
                Save();
            Terraria.Audio.SoundEngine.PlaySound(_open ? Terraria.ID.SoundID.MenuOpen : Terraria.ID.SoundID.MenuClose);
        }

        // ---- what each tab lists, in the game's order ----

        static IEnumerable<string> FullList(string category)
        {
            switch (category)
            {
                case ProgressBook.Spells: return SpellTable.All.Select(s => s.Id);
                case ProgressBook.Creatures: return Enemies.All.Select(e => e.Id);
                case ProgressBook.Liquids: return Liquids.All.Select(l => l.Id);
                case ProgressBook.Wands:
                    if (_wandSprites == null)
                        _wandSprites = NoitaArt.List("data/items_gfx/wands/").Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => f.Contains("/custom/") ? 0 : 1).ThenBy(f => f, StringComparer.Ordinal).ToList();
                    return _wandSprites;
            }
            return Enumerable.Empty<string>();
        }

        static string NameOf(string category, string id)
        {
            switch (category)
            {
                case ProgressBook.Spells: return MagicItems.SpellName(id);
                case ProgressBook.Creatures:
                    var e = Enemies.All.FirstOrDefault(x => x.Id == id);
                    return e == null ? id : NoitaArt.Text(e.NameKey, e.NameEn);
                case ProgressBook.Liquids:
                    var l = Liquids.All.FirstOrDefault(x => x.Id == id);
                    return l == null ? id : NoitaArt.Text(l.NameKey, l.Id);
                case ProgressBook.Wands:
                    var w = WandStore.All().FirstOrDefault(x => x.Sprite == id);
                    return w != null ? MagicItems.WandName(w) : Path.GetFileNameWithoutExtension(id);
            }
            return id;
        }

        static string Tooltip(string category, string id)
        {
            var lines = new List<string> { NameOf(category, id) };
            switch (category)
            {
                case ProgressBook.Spells:
                    lines.AddRange(MagicItems.SpellLines(id));
                    long casts = Book.CountOf(category, id, "casts");
                    if (casts > 0)
                        lines.Add(NoitaArt.Text("$menu_progress_casts", "Casts") + ": " + casts);
                    break;
                case ProgressBook.Creatures:
                    var e = Enemies.All.FirstOrDefault(x => x.Id == id);
                    if (e != null)
                        lines.Add(NoitaArt.Text("$inventory_hp", "HP") + ": " + Math.Round(e.NoitaHp * 25));
                    lines.Add(NoitaArt.Text("$menu_progress_kills", "Kills") + ": " + Book.CountOf(category, id, "kills"));
                    break;
            }
            return string.Join("\n", lines);
        }

        static Texture2D Icon(string category, string id, out Rectangle? frame)
        {
            frame = null;
            NoitaArt.Art art = null;
            switch (category)
            {
                case ProgressBook.Spells: art = NoitaArt.Get(MagicItems.Spell(id)?.Sprite); break;
                case ProgressBook.Creatures: art = NoitaArt.Get("data/ui_gfx/animal_icons/" + id + ".png"); break;
                case ProgressBook.Wands: art = NoitaArt.Get(id); break;
            }
            if (art?.Texture != null && art.Sprite != null)
                frame = MagicItems.Frame(art);
            return art?.Texture;
        }

        static Color LiquidColor(string id)
        {
            string hex = Liquids.All.FirstOrDefault(x => x.Id == id)?.Color ?? "";
            return uint.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out uint argb)
                ? new Color((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb) : Color.Gray;
        }

        // ---- drawing ----

        static Texture2D Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(Main.instance.GraphicsDevice, 1, 1);
                    _pixel.SetData(new[] { Color.White });
                }
                return _pixel;
            }
        }

        static NoitaArt.Art Ui(string name) => NoitaArt.Get("data/ui_gfx/progress_menu/" + name + ".png");

        /// <summary>The button by Terraria's Bestiary/emote buttons (inventory open).</summary>
        static void DrawButton()
        {
            if (!Main.playerInventory || Book == null)
                return;
            _button = new Rectangle(458, 292, 26, 26);
            var sb = Main.spriteBatch;
            var box = Ui("grid_box");
            if (box?.Texture != null)
                sb.Draw(box.Texture, _button, Color.White);
            Utils.DrawBorderString(sb, "?", new Vector2(_button.X + 9, _button.Y + 3), _open ? Color.Gold : Color.White, 0.9f);
            if (!_button.Contains(Main.mouseX, Main.mouseY))
                return;
            Main.LocalPlayer.mouseInterface = true;
            Main.hoverItemName = NoitaArt.Text("$menu_progress", "Progress") + " (O)";
            if (Main.mouseLeft && Main.mouseLeftRelease)
                Toggle();
        }

        static void Draw()
        {
            DrawButton();
            if (!_open || Book == null || !NoitaArt.Ready)
                return;
            var sb = Main.spriteBatch;
            var p = Main.LocalPlayer;
            int w = Math.Min(980, Main.screenWidth - 80), h = Math.Min(600, Main.screenHeight - 160);
            var panel = new Rectangle((Main.screenWidth - w) / 2, (Main.screenHeight - h) / 2, w, h);
            sb.Draw(Pixel, panel, new Color(12, 10, 16) * 0.92f);
            if (panel.Contains(Main.mouseX, Main.mouseY))
                p.mouseInterface = true;

            // tabs
            int tx = panel.X + 16;
            for (int i = 0; i < Tabs.Length; i++)
            {
                string label = NoitaArt.Text(TabKeys[i], TabNames[i]);
                var r = new Rectangle(tx, panel.Y + 10, label.Length * 9 + 12, 26);
                bool over = r.Contains(Main.mouseX, Main.mouseY);
                sb.Draw(Pixel, r, (i == _tab ? new Color(90, 70, 40) : new Color(40, 34, 44)) * (over ? 1f : 0.85f));
                Utils.DrawBorderString(sb, label, new Vector2(r.X + 6, r.Y + 4), i == _tab ? Color.Gold : Color.White, 0.9f);
                if (over && Main.mouseLeft && Main.mouseLeftRelease && _tab != i)
                {
                    _tab = i;
                    _scroll = 0;
                    Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
                }
                tx = r.Right + 6;
            }

            // header: "Spells 45 / 393"
            string cat = Tabs[_tab];
            var page = Book.Page(cat, FullList(cat));
            string header = NoitaArt.Text(TabKeys[_tab], TabNames[_tab]) + "  " + page.Count(x => x.known) + " / " + page.Count;
            Utils.DrawBorderString(sb, header, new Vector2(panel.Right - 20, panel.Y + 14), Color.White, 0.9f, 1f, 0f);

            // the grid
            const int cell = 40, gap = 4;
            var grid = new Rectangle(panel.X + 16, panel.Y + 48, panel.Width - 32, panel.Height - 64);
            int perRow = Math.Max(1, (grid.Width + gap) / (cell + gap));
            int rows = (page.Count + perRow - 1) / perRow, visible = Math.Max(1, (grid.Height + gap) / (cell + gap));
            if (grid.Contains(Main.mouseX, Main.mouseY) && Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI != 0)
            {
                _scroll -= Math.Sign(Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI);
                Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI = 0;
            }
            _scroll = Math.Max(0, Math.Min(_scroll, rows - visible));
            var known = Ui("grid_box");
            var unknown = Ui("grid_box_unknown");
            var unknownIcon = Ui("icon_unknown");
            for (int k = _scroll * perRow; k < page.Count && k < (_scroll + visible) * perRow; k++)
            {
                int row = k / perRow - _scroll, col = k % perRow;
                var r = new Rectangle(grid.X + col * (cell + gap), grid.Y + row * (cell + gap), cell, cell);
                var (id, isKnown) = page[k];
                var bg = isKnown ? known : unknown;
                if (bg?.Texture != null)
                    sb.Draw(bg.Texture, r, Color.White);
                else
                    sb.Draw(Pixel, r, new Color(50, 44, 56));
                var inner = new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);
                if (cat == ProgressBook.Liquids)
                    sb.Draw(Pixel, inner, LiquidColor(id) * (isKnown ? 1f : 0.25f));
                else
                {
                    var tex = Icon(cat, id, out var frame);
                    if (tex != null)
                    {
                        var src = frame ?? new Rectangle(0, 0, tex.Width, tex.Height);
                        float s = Math.Min(inner.Width / (float)src.Width, inner.Height / (float)src.Height);
                        var dst = new Rectangle(inner.Center.X - (int)(src.Width * s / 2), inner.Center.Y - (int)(src.Height * s / 2),
                                                (int)(src.Width * s), (int)(src.Height * s));
                        sb.Draw(tex, dst, src, isKnown ? Color.White : Color.Black * 0.6f);   // unknown: a dark silhouette
                    }
                    else if (!isKnown && unknownIcon?.Texture != null)
                        sb.Draw(unknownIcon.Texture, inner, Color.White);
                }
                if (r.Contains(Main.mouseX, Main.mouseY))
                    Main.hoverItemName = isKnown ? Tooltip(cat, id) : "???";
            }
            if (rows > visible)
                Utils.DrawBorderString(sb, (_scroll + 1) + "-" + Math.Min(rows, _scroll + visible) + " / " + rows,
                    new Vector2(panel.Right - 20, panel.Bottom - 22), Color.Gray, 0.7f, 1f, 0f);
        }

        [Hook("progress_draw")]
        [HarmonyPatch(typeof(Main), "DrawInterface_31_BuilderAccToggles")]
        static class DrawPatch
        {
            // drawn before Terraria's mouse text, so tooltips (hoverItemName) come on top
            static void Postfix()
            {
                try { if (!Main.gameMenu) Draw(); }
                catch (Exception ex) { Entry.Error("progress window", ex); }
            }
        }

        [Hook("progress_enter_world")]
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
