using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terranoita.Noita;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita's flask (data/entities/items/pickup/potion.xml): 1000 cells of one material (MaterialInventoryComponent),
    /// filled by Noita's potion.lua (LuaWandMaker.MakePotion). In hand: the use button sprays it (PotionComponent),
    /// right click throws it (it shatters and spills), down drinks from it, and dipped in a liquid it sucks it up
    /// (MaterialSuckerComponent, 5 cells a frame, same material or empty). The item is a wand carrier whose WandData has
    /// Flask set. Rates of spraying and the throw speed are ours.
    /// </summary>
    public static class Flasks
    {
        public const string Sprite = "data/ui_gfx/items/potion.png";
        const string HandSprite = "data/items_gfx/potion.png";
        public const float Capacity = 1000;      // barrel_size
        const float SuckPerFrame = 5;            // num_cells_sucked_per_frame
        const int SprayUnits = 40;               // ours: liquid units (of 255 a tile) a frame
        const int DrinkUnits = 12;               // as drinking from a pool (NoitaActions)
        const float CellsPerUnit = 16f / 255f;   // a full tile holds about 16 Noita cells (NoitaActions)
        const float ThrowSpeed = 12f;            // ours (Noita max_throw_speed 180 px/s)
        const int SpillRings = 12;               // ours: how far around the break a flask's contents may land

        sealed class Thrown { public Vector2 Pos, Vel; public float Rot; public string Material; public float Amount; public int Age; }
        static readonly List<Thrown> Flying = new List<Thrown>();
        static int _saveIn, _drinkSound;
        static bool _dirty;

        // ---- what it is ----

        static string MaterialName(string id)
        {
            var def = Liquids.All.FirstOrDefault(l => l.Id == id);
            return def?.NameKey != null ? NoitaArt.Text(def.NameKey, id) : id;
        }

        public static string Name(WandData w)
        {
            if (w == null)
                return "";
            if (string.IsNullOrEmpty(w.Flask) || w.FlaskAmount <= 0)
                return NoitaArt.Text("$item_potion", "Potion") + " (" + NoitaArt.Text("$item_potion_empty", "Empty") + ")";
            return NoitaArt.Text("$item_potion_with_material", "$0 potion").Replace("$0", Capitalize(MaterialName(w.Flask)));
        }

        static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        public static List<string> Lines(WandData w)
        {
            var lines = new List<string>();
            int pct = (int)Math.Round(100 * Math.Max(0, w.FlaskAmount) / Capacity);
            lines.Add(NoitaArt.Text("$item_potion_fullness", " ($0% full)").Replace("$0", pct.ToString()).Trim());
            lines.Add("LMB: " + NoitaArt.Text("$controls_sprayflask", "spray from potion"));
            lines.Add("RMB: " + NoitaArt.Text("$controls_throw", "throw"));
            lines.Add(NoitaArt.Text("$item_description_potion_usage", "\n$0 on item - drink").Replace("$0", "Down").Trim());
            return lines;
        }

        /// <summary>A flask filled by one of Noita's potion scripts at a world position (pixels).</summary>
        public static Item Make(Vector2 at, string script = "data/scripts/items/potion.lua")
        {
            try
            {
                var (material, amount) = new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next()).MakePotion(at.X / Units.PixelScale, at.Y / Units.PixelScale, script);
                return MagicItems.MakeFlask(material, amount);
            }
            catch (Exception ex) { Entry.Error("flask " + script, ex); return MagicItems.MakeFlask("water", Capacity); }
        }

        // ---- in hand ----

        public static void Update(Player p, bool typing)
        {
            StepThrown();
            if (_dirty && --_saveIn <= 0)
            {
                WandStore.Save();
                _dirty = false;
            }
            var item = p.inventory[p.selectedItem];
            var w = MagicItems.FlaskOf(item);
            if (w == null)
                return;
            bool busy = typing || p.mouseInterface || Main.mapFullscreen || p.CCed || p.noItems;
            var dir = Main.MouseWorld - p.Center;
            if (dir.LengthSquared() < 1)
                dir = new Vector2(p.direction, 0);
            dir.Normalize();
            var tip = p.Center + dir * 20f;
            bool full = w.FlaskAmount > 0 && !string.IsNullOrEmpty(w.Flask);

            if (!busy && Main.mouseRight && Main.mouseRightRelease)
            {
                Throw(p, item, w, tip, dir);
                return;
            }
            if (!busy && p.controlUseItem && full)
            {
                Spray(p, w, tip, dir);
                return;
            }
            if (!busy && p.controlDown && p.velocity.Y == 0 && full)
            {
                float cells = Math.Min(w.FlaskAmount, DrinkUnits * CellsPerUnit);
                w.FlaskAmount -= cells;
                NoitaActions.Ingest(p, w.Flask, cells);
                Changed(w);
                if (--_drinkSound <= 0)
                {
                    _drinkSound = 20;
                    NoitaSound.Play("player/potion_drink", p.Center);
                }
                return;
            }
            // dipped in a liquid it sucks it up (Noita's MaterialSuckerComponent)
            if (w.FlaskAmount < Capacity)
            {
                int tx = (int)(tip.X / 16), ty = (int)(tip.Y / 16);
                string there = Physics.Fluids.MaterialAt(tx, ty);
                if (there != null && (!full || there == w.Flask))
                {
                    float cells = Math.Min(SuckPerFrame, Capacity - w.FlaskAmount);
                    string got = Physics.Fluids.Drink(tx, ty, Math.Max(1, (int)(cells / CellsPerUnit)));
                    if (got == there)
                    {
                        w.Flask = got;
                        w.FlaskAmount += cells;
                        Changed(w);
                    }
                }
            }
        }

        static void Changed(WandData w)
        {
            if (w.FlaskAmount <= 0.01f)
            {
                w.FlaskAmount = 0;
                w.Flask = "";
            }
            _dirty = true;
            if (_saveIn <= 0)
                _saveIn = 120;
        }

        static void Spray(Player p, WandData w, Vector2 tip, Vector2 dir)
        {
            // a stream that lands a couple of tiles ahead (Noita: particles at spray_velocity_coeff 75)
            var at = tip + dir * 16f * (1.5f + Main.rand.NextFloat() * 2f);
            int x = (int)(at.X / 16), y = (int)(at.Y / 16);
            if (Physics.Mats.Solid(x, y))
            {
                x = (int)(tip.X / 16);
                y = (int)(tip.Y / 16);
            }
            if (Physics.Mats.Solid(x, y))
                return;
            int units = (int)Math.Min(SprayUnits, w.FlaskAmount / CellsPerUnit);
            if (units <= 0)
                return;
            // only what went in leaves the flask (a full tile takes nothing: it stays in the flask)
            int put = Physics.Fluids.Add(x, y, w.Flask, units);
            Physics.Fluids.Learn(w.Flask);
            w.FlaskAmount = Math.Max(0, w.FlaskAmount - put * CellsPerUnit);
            Changed(w);
            p.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, (float)Math.Atan2(dir.Y, dir.X) - MathHelper.PiOver2);
        }

        static void Throw(Player p, Item item, WandData w, Vector2 tip, Vector2 dir)
        {
            Flying.Add(new Thrown { Pos = tip, Vel = dir * ThrowSpeed + p.velocity * 0.5f, Material = w.Flask, Amount = w.FlaskAmount });
            item.TurnToAir();
            NoitaSound.Play("player/throw", p.Center);
        }

        // ---- thrown: flies, shatters on what it hits, spills what is in it ----

        public static int FlyingCount => Flying.Count;

        static void StepThrown()
        {
            for (int i = Flying.Count - 1; i >= 0; i--)
            {
                var f = Flying[i];
                f.Age++;
                f.Vel.Y = Math.Min(f.Vel.Y + 0.3f, 12f);
                f.Rot += f.Vel.X * 0.05f;
                var before = f.Vel;
                var size = new Vector2(10, 10);
                f.Vel = Collision.TileCollision(f.Pos - size / 2, f.Vel, 10, 10, true, true);
                bool hit = f.Vel != before;
                for (int n = 0; n < Main.maxNPCs && !hit; n++)
                    hit = Main.npc[n].active && !Main.npc[n].friendly && Main.npc[n].Hitbox.Contains((int)f.Pos.X, (int)f.Pos.Y);
                f.Pos += f.Vel;
                if (hit || f.Age > 600 || f.Pos.Y > Main.maxTilesY * 16)
                {
                    Break(f);
                    Flying.RemoveAt(i);
                }
            }
        }

        /// <summary>Tests: a full flask of the material shatters at pos; the units that did not land (0 = all of it did).</summary>
        public static int TestShatter(string material, Vector2 pos) =>
            Break(new Thrown { Pos = pos, Material = material, Amount = Capacity });

        /// <summary>Shatters: what was in it spills; returns the liquid units that found no room.</summary>
        static int Break(Thrown f)
        {
            SoundEngine.PlaySound(SoundID.Shatter, f.Pos);
            for (int k = 0; k < 8; k++)
                Dust.NewDust(f.Pos - new Vector2(6, 6), 12, 12, DustID.Glass, Main.rand.NextFloat() * 6 - 3, Main.rand.NextFloat() * 4 - 3);
            if (string.IsNullOrEmpty(f.Material) || f.Amount <= 0)
                return 0;
            // all that was in it lands around where it broke (Noita: death_throw_particle_velocity_coeff): ring by ring
            // until it is all out, counting only what went in (a cell full of another liquid takes nothing), so a
            // thrown flask spills as much as pouring it out (author)
            int left = (int)Math.Round(f.Amount / CellsPerUnit);
            int cx = (int)(f.Pos.X / 16), cy = (int)(f.Pos.Y / 16);
            if (Physics.Mats.Solid(cx, cy))
            {
                // it broke against a wall: spill from the side it came from
                var back = f.Vel.LengthSquared() > 0.01f ? -Vector2.Normalize(f.Vel) : new Vector2(0, -1);
                cx = (int)((f.Pos.X + back.X * 12) / 16);
                cy = (int)((f.Pos.Y + back.Y * 12) / 16);
            }
            for (int r = 0; r <= SpillRings && left > 0; r++)
                for (int dy = -r; dy <= r && left > 0; dy++)
                    for (int dx = -r; dx <= r && left > 0; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || Physics.Mats.Solid(cx + dx, cy + dy))
                            continue;
                        left -= Physics.Fluids.Add(cx + dx, cy + dy, f.Material, Math.Min(255, left));
                    }
            Physics.Fluids.Learn(f.Material);
            return left;
        }

        public static void Clear() => Flying.Clear();

        // ---- drawing ----

        static Color Tint(string material)
        {
            var def = Liquids.All.FirstOrDefault(l => l.Id == material);
            if (def?.Color == null || def.Color.Length < 6)
                return Color.White;
            try
            {
                var hex = def.Color.TrimStart('#');
                uint v = Convert.ToUInt32(hex.Substring(hex.Length - 6), 16);
                return Color.Lerp(Color.White, new Color((int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255), 0.6f);
            }
            catch { return Color.White; }
        }

        /// <summary>Thrown flasks (inside a begun sprite batch).</summary>
        public static void DrawThrown()
        {
            var art = NoitaArt.Get(HandSprite);
            if (art?.Texture == null)
                return;
            foreach (var f in Flying)
            {
                var light = Lighting.GetColor((int)(f.Pos.X / 16), (int)(f.Pos.Y / 16)).MultiplyRGB(Tint(f.Material));
                Main.spriteBatch.Draw(art.Texture, f.Pos - Main.screenPosition, null, light, f.Rot,
                                      new Vector2(art.Texture.Width / 2f, art.Texture.Height / 2f), 2f, SpriteEffects.None, 0f);
            }
        }

        /// <summary>The flask in hand (inside a begun sprite batch).</summary>
        public static void DrawHeld(Player p, WandData w)
        {
            var art = NoitaArt.Get(HandSprite);
            if (art?.Texture == null)
                return;
            var dir = Main.MouseWorld - p.Center;
            float rot = (float)Math.Atan2(dir.Y, dir.X);
            var hand = p.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, rot - MathHelper.PiOver2);
            var light = Lighting.GetColor((int)(p.Center.X / 16), (int)(p.Center.Y / 16)).MultiplyRGB(Tint(w.Flask));
            Main.spriteBatch.Draw(art.Texture, hand - Main.screenPosition, null, light, 0f,
                                  new Vector2(art.Texture.Width / 2f, art.Texture.Height / 2f), 2f, SpriteEffects.None, 0f);
        }
    }
}
