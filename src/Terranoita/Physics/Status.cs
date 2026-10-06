using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Noita status effects on the player (design/sheets/status_effects.json): Noita's icons and names after Terraria's
    /// buff icons, and what each does (the sheet's mechanic column) run by our code. Terraria has no room for new buffs.
    /// </summary>
    public static class Status
    {
        static readonly Dictionary<string, int> Left = new Dictionary<string, int>();   // id -> frames left
        static Dictionary<string, StatusEffectDef> _defs;
        static int _teleportTimer;

        static Dictionary<string, StatusEffectDef> Defs =>
            _defs ?? (_defs = StatusEffects.All.ToDictionary(s => s.Id));

        public static bool Has(string id) => Left.ContainsKey(id);
        public static IEnumerable<string> Active => Left.Keys;

        /// <summary>Start or refresh an effect for its sheet seconds (or the given ones).</summary>
        public static void Apply(string id, float seconds = 0)
        {
            if (!Defs.TryGetValue(id, out var d))
                return;
            if ((id == "ON_FIRE" || id == "INGESTION_ON_FIRE") && FireProof)
                return;   // wet, oily, slimy... (Noita protects_from_fire)
            int frames = (int)((seconds > 0 ? seconds : d.Seconds) * 60);
            if (!Left.TryGetValue(id, out int now) || now < frames)
                Left[id] = frames;
            // author: only opposite effects replace each other (status_effects.json cancels); the rest stay together
            foreach (var other in d.Cancels ?? new string[0])
                Left.Remove(other);
            if (d.ProtectsFromFire || Array.IndexOf(d.Cancels ?? new string[0], "ON_FIRE") >= 0)
                Main.LocalPlayer.ClearBuff(BuffID.OnFire);
        }

        /// <summary>
        /// A liquid's touch effects. Only opposite effects replace each other (sheet column cancels: water washes
        /// off oil and blood, fire and cold, healing and poison...); a burning liquid burns off fire-proof stains.
        /// </summary>
        public static void Stain(IEnumerable<string> ids)
        {
            if (ids.Contains("ON_FIRE"))
                // a burning liquid (Noita's "fire", molten metal) burns off the stains that would keep fire away
                foreach (var old in Left.Keys.Where(k => Defs.TryGetValue(k, out var d) && d.ProtectsFromFire).ToList())
                    Left.Remove(old);
            foreach (var id in ids)
                Apply(id);
        }

        /// <summary>Noita remove_cells_that_cause_when_activated: the liquid that caused it is used up.</summary>
        public static bool RemovesCause(string id) => Defs.TryGetValue(id, out var d) && d.RemovesCause;

        /// <summary>The Noita creature the player is turned into (sprite path), or null.</summary>
        public static string Form { get; private set; }
        static int _formTimer;

        static void UpdateForm()
        {
            bool sheep = Has("POLYMORPH"), random = Has("POLYMORPH_RANDOM"), unstable = Has("POLYMORPH_UNSTABLE");
            if (!sheep && !random && !unstable)
            {
                Form = null;
                return;
            }
            // the unstable kind keeps changing, every 3 seconds
            if (Form != null && !(unstable && ++_formTimer % 180 == 0))
                return;
            if (sheep && !random && !unstable)
            {
                Form = "data/enemies_gfx/sheep.xml";
                return;
            }
            var all = Enemies.All.Where(e => Terranoita.Generated.Defs.InStage(e.Stage, Entry.Stage) && e.Sprite != null && e.Sprite.EndsWith(".xml") && (e.Walks || e.Flies)).ToArray();   // creatures, not nests or turrets
            Form = all.Length > 0 ? all[Main.rand.Next(all.Length)].Sprite : "data/enemies_gfx/sheep.xml";
        }

        public static void Clear()
        {
            Form = null;
            Left.Clear();
        }

        static bool FireProof => Left.Keys.Any(k => Defs.TryGetValue(k, out var d) && d.ProtectsFromFire);

        /// <summary>After Terraria's buffs set the player's flags for this frame.</summary>
        static void Effects(Player p)
        {
            foreach (var id in Left.Keys.ToList())
                if (--Left[id] <= 0)
                    Left.Remove(id);
            UpdateForm();
            HurtSound(p);
            if (Left.Count == 0)
                return;
            if (FireProof)
            {
                p.buffImmune[BuffID.OnFire] = true;
                p.ClearBuff(BuffID.OnFire);
            }
            if (Has("ON_FIRE") || Has("INGESTION_ON_FIRE"))
                p.onFire = true;
            if (Has("SLIMY")) p.moveSpeed *= 0.7f;
            if (Has("CHILLED")) p.moveSpeed *= 0.5f;
            if (Has("FOOD_POISONING")) p.moveSpeed *= 0.8f;
            if (Has("MOVEMENT_FASTER_2X")) p.moveSpeed *= 2f;
            if (Has("POLYMORPH") || Has("POLYMORPH_RANDOM") || Has("POLYMORPH_UNSTABLE"))
            {
                p.noItems = true;
                p.moveSpeed *= 0.6f;
            }
            if (Has("CONFUSION")) p.confused = true;
            if (Has("NIGHTVISION")) p.nightVision = true;
            if (Has("INVISIBILITY")) p.invis = true;
            if (Has("INGESTION_FREEZING")) p.frozen = true;
            if (Has("FARTS")) p.stinky = true;
            if (Has("WEAKNESS")) p.endurance -= 1f;     // damage taken x (1 - endurance): double
            if (Has("PROTECTION_ALL"))
            {
                p.immune = true;
                p.immuneTime = Math.Max(p.immuneTime, 2);
            }
            if (Has("BERSERK"))
            {
                p.meleeDamage += 1f;
                p.rangedDamage += 1f;
                p.magicDamage += 1f;
                p.minionDamage += 1f;
            }
            if (Has("MANA_REGENERATION") && Main.GameUpdateCount % 3 == 0 && p.statMana < p.statManaMax2)
                p.statMana++;
            if (Has("FASTER_LEVITATION") && p.controlJump)
                p.velocity.Y = Math.Max(p.velocity.Y - 0.9f, -7f);
            if ((Has("ALCOHOLIC") || Has("INGESTION_DRUNK")) && Main.rand.Next(40) == 0)
                p.velocity.X += (Main.rand.NextFloat() * 6f - 3f);
            if (Has("TELEPORTATION") || Has("UNSTABLE_TELEPORTATION"))
            {
                if (++_teleportTimer >= 120)
                {
                    _teleportTimer = 0;
                    bool far = Has("UNSTABLE_TELEPORTATION");
                    Teleport(p, far ? 40 : 10, far ? 80 : 25);
                }
            }
            Drips(p);
        }

        static void Drips(Player p)
        {
            void Drip(int dust, int chance, Color tint = default(Color))
            {
                if (Main.rand.Next(chance) == 0)
                    Dust.NewDust(p.position, p.width, p.height, dust, 0f, 1f, 100, tint);
            }
            if (Has("WET")) Drip(33, 6)   /* water drops */;
            if (Has("OILED")) Drip(DustID.Smoke, 6, Color.Black);
            if (Has("BLOODY")) Drip(DustID.Blood, 6);
            if (Has("SLIMY")) Drip(DustID.t_Slime, 6);
            if (Has("RADIOACTIVE")) Drip(DustID.GreenTorch, 4);
            if (Has("JARATE")) Drip(DustID.YellowTorch, 8);
            if (Has("RAINBOW_FARTS") || Has("TRIP"))
                if (Main.rand.Next(4) == 0)
                {
                    var d = Dust.NewDustDirect(p.position - new Vector2(16, 16), p.width + 32, p.height + 32, DustID.RainbowTorch, 0f, 0f, 100, Main.hslToRgb(Main.rand.NextFloat(), 1f, 0.6f));
                    d.noGravity = true;
                }
        }

        static void Teleport(Player p, int min, int max)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int dx = Main.rand.Next(min, max + 1) * (Main.rand.Next(2) == 0 ? -1 : 1);
                int dy = Main.rand.Next(-max / 2, max / 2 + 1);
                var to = p.position + new Vector2(dx * 16, dy * 16);
                if (to.X < 800 || to.Y < 800 || to.X > Main.maxTilesX * 16 - 800 || to.Y > Main.maxTilesY * 16 - 800)
                    continue;
                if (Collision.SolidCollision(to, p.width, p.height) || Collision.LavaCollision(to, p.width, p.height))
                    continue;
                p.Teleport(to, 1);
                return;
            }
        }

        static float _touchHps;
        static bool _hurting;
        static int _lastLife;
        static uint _lastHurtSound;

        /// <summary>Losing hp to an effect or a liquid makes the player's hurt sound (twice a second at most).</summary>
        static void HurtSound(Player p)
        {
            if (_hurting && p.statLife < _lastLife && Main.GameUpdateCount - _lastHurtSound >= 30)
            {
                Terraria.Audio.SoundEngine.PlaySound(p.Male ? SoundID.PlayerHit : SoundID.FemaleHit, p.position);
                _lastHurtSound = Main.GameUpdateCount;
            }
            _lastLife = p.statLife;
        }

        /// <summary>A liquid hurting (or healing, &lt; 0) whoever touches it this frame, in hp per second.</summary>
        public static void TouchHurt(float hpPerSecond) => _touchHps += hpPerSecond;

        /// <summary>Damage and healing over time, the way Terraria's own debuffs do it (lifeRegen is half hp per second).</summary>
        static void Regen(Player p)
        {
            int loss = 0;
            if (Has("RADIOACTIVE")) loss += 12;
            if (Has("POISONED")) loss += 4;
            if (Has("FOOD_POISONING")) loss += 4;
            _hurting = loss > 0 || _touchHps > 0;
            if (loss > 0)
            {
                if (p.lifeRegen > 0)
                    p.lifeRegen = 0;
                p.lifeRegenTime = 0;
                p.lifeRegen -= loss;
            }
            if (Has("HP_REGENERATION"))
                p.lifeRegen += 20;
            if (_touchHps > 0)
            {
                if (p.lifeRegen > 0)
                    p.lifeRegen = 0;
                p.lifeRegenTime = 0;
                p.lifeRegen -= (int)Math.Ceiling(_touchHps * 2);
            }
            else if (_touchHps < 0)
                p.lifeRegen += (int)Math.Ceiling(-_touchHps * 2);
            _touchHps = 0;
        }

        // ---- icons after Terraria's buff icons ----

        static void DrawIcons()
        {
            if (Left.Count == 0)
                return;
            var p = Main.LocalPlayer;
            int index = 0;
            for (int i = 0; i < p.buffType.Length; i++)
                if (p.buffType[i] > 0)
                    index++;
            var sb = Main.spriteBatch;
            foreach (var kv in Left.OrderBy(k => k.Key))
            {
                if (!Defs.TryGetValue(kv.Key, out var d))
                    continue;
                int x = 32 + index % 11 * 38, y = 76 + index / 11 * 50;
                index++;
                var art = NoitaArt.Get(d.Icon);
                var r = new Rectangle(x, y, 32, 32);
                if (art?.Texture != null)
                    sb.Draw(art.Texture, r, Color.White);
                Utils.DrawBorderString(sb, (kv.Value / 60 + 1) + " s", new Vector2(x, y + 34), Color.White, 0.8f);
                if (r.Contains(Main.mouseX, Main.mouseY))
                {
                    p.mouseInterface = true;
                    Main.instance.MouseText(NoitaArt.Text(d.NameKey, d.Id) + "\n" + NoitaArt.Text(d.DescKey, d.Mechanic));
                }
            }
        }

        [Hook("player_buffs")]
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateBuffs))]
        static class BuffsPatch
        {
            static void Postfix(Player __instance, int i)
            {
                if (i != Main.myPlayer || Main.gameMenu)
                    return;
                try { Effects(__instance); }
                catch (Exception ex) { Entry.Error("status effects", ex); }
            }
        }

        [Hook("player_liferegen")]
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateLifeRegen))]
        static class RegenPatch
        {
            static void Prefix(Player __instance)
            {
                if (__instance.whoAmI == Main.myPlayer && (Left.Count > 0 || _touchHps != 0))
                    Regen(__instance);
            }
        }

        [Hook("buffs_draw")]
        [HarmonyPatch(typeof(Main), "DrawInterface_Resources_Buffs")]
        static class DrawPatch
        {
            static void Postfix()
            {
                try { DrawIcons(); }
                catch (Exception ex) { Entry.Error("status icons", ex); }
            }
        }
    }
}
