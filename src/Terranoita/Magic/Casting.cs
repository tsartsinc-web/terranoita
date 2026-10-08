using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Shooting a held Noita wand (stage 3): Noita's own gun.lua decides what a cast does (LuaGun), the shots become
    /// SpellShots, the cost comes out of Terraria's mana (author). One Lua state per wand: its deck lives there.
    /// </summary>
    public static class Casting
    {
        sealed class Held
        {
            public LuaGun Gun;
            public System.Threading.Tasks.Task<LuaGun> Loading;   // gun.lua + gun_actions.lua parsed off the game thread
            public int ReadyAt;            // Main.GameUpdateCount when it can cast again
            public int Version;            // WandData changes reload the deck
            public int LastUsed;           // _now when it was last asked for
            public int LastCost;           // Terraria mana its last paid cast cost (Mana Flower drinks before a cast it cannot pay)
        }

        static readonly Dictionary<int, Held> Guns = new Dictionary<int, Held>();
        // each Lua state holds Noita's whole spell code (~15 MB): only the wands used last keep one (the magic test's
        // memory grew by a state per wand); a wand taken up again gets a new state in the background
        const int MaxGuns = 6;

        /// <summary>Tests: how many wands hold a Lua state now.</summary>
        public static int GunCount => Guns.Count;

        static void Evict()
        {
            while (Guns.Count > MaxGuns)
            {
                var oldest = Guns.OrderBy(g => g.Value.LastUsed).First();
                Guns.Remove(oldest.Key);
            }
        }

        /// <summary>Tests: the wand is ready to cast now (one test wand gets a new spell each step).</summary>
        public static void TestReady(WandData w)
        {
            if (Guns.TryGetValue(w.Id, out var h))
                h.ReadyAt = 0;
            _recharge.Remove(w.Id);
        }

        /// <summary>Leaving the world: every Lua state goes.</summary>
        public static void Forget() => Guns.Clear();
        static readonly Dictionary<int, int> Versions = new Dictionary<int, int>();
        static int _now;

        /// <summary>Tests: fire as if the button were held, at this point instead of the mouse.</summary>
        public static bool TestFire;
        public static int TestCasts, TestShots, TestMana;   // tests: casts made, shots fired, mana spent
        public static Vector2? TestAim;
        static Vector2 Aim => TestAim ?? Main.MouseWorld;

        /// <summary>The wand's slots changed (wand window): its deck is built again before the next cast.</summary>
        public static void Changed(WandData w) => Versions[w.Id] = (Versions.TryGetValue(w.Id, out int v) ? v : 0) + 1;

        public static Item HeldWand(Player p)
        {
            var item = p.inventory[p.selectedItem];
            return MagicItems.IsWand(item) ? item : null;
        }

        /// <summary>The wand's Lua state, or null while it is still being made (in the background: parsing Noita's
        /// 420 KB of spell code in the game thread froze the game for a moment, author: FPS drop).</summary>
        static Held GunFor(WandData w)
        {
            int version = Versions.TryGetValue(w.Id, out int v) ? v : 0;
            if (!Guns.TryGetValue(w.Id, out var h))
            {
                Guns[w.Id] = h = new Held { Version = -1 };
                h.Loading = System.Threading.Tasks.Task.Run(() => new LuaGun(NoitaArt.ReadText, new TerrariaWorld()));
                h.LastUsed = _now;
                Evict();
            }
            h.LastUsed = _now;
            if (h.Gun == null)
            {
                if (!h.Loading.IsCompleted)
                    return null;
                h.Gun = h.Loading.Result;   // throws the loading error, if any
            }
            if (h.Version != version)
            {
                var lw = new LuaWand
                {
                    SpellsPerCast = w.SpellsPerCast, Shuffle = w.Shuffle, RechargeTime = w.RechargeTime, CastDelay = w.CastDelay,
                    Capacity = w.Capacity, Spread = w.Spread, SpeedMultiplier = w.SpeedMultiplier, AlwaysCast = w.AlwaysCast.ToList(),
                };
                var slotOf = new List<int>();
                for (int i = 0; i < w.Slots.Length; i++)
                    if (w.Slots[i] != null)
                    {
                        lw.Spells.Add((w.Slots[i], i < w.Uses.Length ? w.Uses[i] : -1));
                        slotOf.Add(i);
                    }
                h.Gun.UsesChanged = (card, uses) =>
                {
                    if (card >= 1 && card <= slotOf.Count && slotOf[card - 1] < w.Uses.Length)
                        w.Uses[slotOf[card - 1]] = uses;
                };
                h.Gun.Load(lw);
                h.Version = version;
            }
            return h;
        }

        /// <summary>Every game update (main_update): cast while the use button is held with a wand in hand.</summary>
        public static void Update()
        {
            _now++;
            var p = Main.LocalPlayer;
            if (Main.gameMenu && Guns.Count > 0)
                Forget();
            if (Main.gameMenu || p == null || !p.active || p.dead || !NoitaArt.Ready)
                return;
            var item = HeldWand(p);
            if (item == null)
                return;
            var w = MagicItems.WandOf(item);
            if (w == null)
                return;
            // aim: the player faces the mouse and holds the wand out toward it, like Noita
            p.ChangeDir(Aim.X < p.Center.X ? -1 : 1);
            var aimDir = Aim - p.Center;
            p.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, (float)Math.Atan2(aimDir.Y, aimDir.X) - MathHelper.PiOver2);
            Held h;
            try { h = GunFor(w); }   // started as soon as the wand is in hand
            catch (Exception ex) { Entry.Error("wand " + w.Id, ex); return; }
            if (!TestFire && (!p.controlUseItem || p.mouseInterface || Main.mapFullscreen || p.noItems || p.CCed))
                return;
            if (h == null || _now < h.ReadyAt)
                return;
            LuaCast cast;
            // Terraria's magic bonuses apply to Noita wands (author): mana cost (p.manaCost: armour, Magic Cuffs, Mana
            // Flower...) scales what the spells cost; Mana Flower drinks a potion when the next cast cannot be paid
            if (p.manaFlower && h.LastCost > 0 && p.statMana < h.LastCost)
                p.QuickMana();
            float costMult = Math.Max(0.05f, p.manaCost);
            int real = TestFire ? Math.Max(p.statMana, 1000) : p.statMana;   // tests: Terraria caps mana at 400, giga spells cost 500+
            int have = (int)(real / costMult);   // the budget gun.lua sees, in Noita mana
            try
            {
                ((TerrariaWorld)h.Gun.World).Player = p;
                cast = h.Gun.Cast(have);
            }
            catch (Exception ex) { Entry.Error("cast " + w.Id, ex); h.ReadyAt = _now + 30; return; }
            int spent = (int)Math.Round((have - cast.Mana) * costMult);   // Terraria mana paid
            p.statMana = Math.Max(0, Math.Min(p.statManaMax2, real - spent));   // some spells give mana back (spent < 0)
            if (spent > 0)
            {
                h.LastCost = spent;
                p.ApplyManaRegenerationDelay();   // Terraria's own delay (Mana Regeneration Band, Celestial Cuffs...)
            }
            var dir = Aim - p.Center;
            if (dir.LengthSquared() < 1)
                dir = new Vector2(p.direction, 0);
            dir.Normalize();
            var tip = p.Center + dir * 24f;
            foreach (var id in cast.Played)
                ProgressWindow.Cast(id);   // the progress book counts casts per spell (Noita's OnActionPlayed)
            TestCasts++;
            TestMana += Math.Max(0, spent);
            TestShots += cast.Shots.Count;
            SpellShots.FireAll(cast.Shots, tip, dir, p, w);
            // recoil: Noita's shot effects push the caster back
            if (cast.Recoil != 0)
                p.velocity -= dir * cast.Recoil / 20f;
            float wait = Math.Max(cast.CastDelay, cast.Recharge);
            h.ReadyAt = _now + Math.Max(1, (int)Math.Ceiling(wait));
            _recharge[w.Id] = (_now, cast.Recharge > 0 ? (int)cast.Recharge : 0);
        }

        static readonly Dictionary<int, (int at, int frames)> _recharge = new Dictionary<int, (int, int)>();

        /// <summary>0..1 how far the wand is through its recharge, or -1 if it is not recharging (the wand window shows it).</summary>
        public static float Recharging(int wandId)
        {
            if (!_recharge.TryGetValue(wandId, out var r) || r.frames <= 0)
                return -1;
            float f = (_now - r.at) / (float)r.frames;
            return f >= 1 ? -1 : f;
        }

        // ---- the wand in hand, pointing at the mouse ----

        [Hook("magic_held_wand")]
        [HarmonyPatch(typeof(Main), "DrawPlayers_AfterProjectiles")]
        static class DrawPatch
        {
            static void Postfix()
            {
                var p = Main.LocalPlayer;
                if (Main.gameMenu || p == null || !p.active || p.dead)
                    return;
                var item = HeldWand(p);
                var w = item == null ? null : MagicItems.WandOf(item);
                var art = w == null ? null : NoitaArt.Get(w.Sprite);
                var sb = Main.spriteBatch;
                var flask = MagicItems.FlaskOf(p.inventory[p.selectedItem]);
                if (flask != null)
                {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                    try { Flasks.DrawHeld(p, flask); }
                    catch (Exception ex) { Entry.Error("held flask", ex); }
                    finally { sb.End(); }
                    return;
                }
                if (art?.Texture == null)
                    return;
                try
                {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                    var frame = MagicItems.Frame(art);
                    var dir = Aim - p.Center;
                    float rot = (float)Math.Atan2(dir.Y, dir.X);
                    var light = Lighting.GetColor((int)(p.Center.X / 16), (int)(p.Center.Y / 16));
                    var flip = dir.X < 0 ? SpriteEffects.FlipVertically : SpriteEffects.None;
                    // held by its grip (Noita's sprite offset), a little in front of the body
                    var origin = new Vector2(art.Sprite.OffsetX, art.Sprite.OffsetY);
                    if (flip != 0)
                        origin.Y = frame.Height - origin.Y;
                    // in the hand of the arm stretched toward the mouse
                    var hand = p.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, rot - MathHelper.PiOver2);
                    sb.Draw(art.Texture, hand - Main.screenPosition, frame, light, rot, origin, 2f, flip, 0f);
                }
                catch (Exception ex) { Entry.Error("held wand", ex); }
                finally { sb.End(); }
            }
        }
    }

    /// <summary>The world as Noita's spell scripts see it, answered from Terraria (Noita pixels = Terraria pixels / 3).</summary>
    sealed class TerrariaWorld : LuaWorld
    {
        public Player Player;
        const float Px = Terranoita.Noita.Units.PixelScale;

        public override int Load(string file, float x, float y) => SpellShots.LoadEntity(file, new Vector2(x, y) * Px, Player);

        // NPCs are entities 1000 + whoAmI
        public override List<int> WithTag(string tag)
        {
            if (tag == "player_unit")
                return new List<int> { Caster };
            if (tag == "homing_target" || tag == "enemy" || tag == "mortal")
                return Enumerable.Range(0, Main.maxNPCs).Where(i => Hostile(Main.npc[i])).Select(i => 1000 + i).ToList();
            if (tag == "projectile")
                return SpellShots.Ids();
            return new List<int>();
        }

        static bool Hostile(NPC n) => n.active && !n.friendly && !n.townNPC && n.life > 0 && !Carriers.IsSegment(n);

        public override List<int> InRadiusWithTag(float x, float y, float radius, string tag)
        {
            var c = new Vector2(x * Px, y * Px);
            float r = radius * Px;
            return WithTag(tag).Where(e => Vector2.Distance(At(e), c) <= r).ToList();
        }

        Vector2 At(int e) =>
            e == Caster ? Player?.Center ?? Vector2.Zero :
            e >= 1000 && e < 1000 + Main.maxNPCs ? Main.npc[e - 1000].Center : SpellShots.Position(e);

        public override (float x, float y) Position(int entity)
        {
            var v = At(entity);
            return (v.X / Px, v.Y / Px);
        }

        public override bool HasTag(int entity, string tag) =>
            entity == Caster ? tag == "player_unit" : entity >= 1000 && entity < 1000 + Main.maxNPCs && (tag == "homing_target" || tag == "enemy" || tag == "mortal");

        // Noita hp units: 1 = 25 Terraria hp
        public override float Hp
        {
            get => (Player?.statLife ?? 100) / 25f;
            set { if (Player != null) Player.statLife = Math.Max(1, (int)Math.Round(value * 25)); }
        }

        public override float MaxHp => (Player?.statLifeMax2 ?? 100) / 25f;

        public override void Damage(int entity, float amount, string type)
        {
            int dmg = (int)Math.Round(amount * 25);
            if (entity == Caster && Player != null && dmg > 0)
                Player.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(Player.name + " cast too much."), dmg, 0);
            else if (entity >= 1000 && entity < 1000 + Main.maxNPCs && dmg > 0 && Player != null)
                Player.ApplyDamageToNPC(Main.npc[entity - 1000], (int)Math.Round(dmg * Player.magicDamage), 0f, 0, false, null, 0, -1);
        }

        public override List<string> WandSpells()
        {
            var w = Player == null ? null : MagicItems.WandOf(Casting.HeldWand(Player));
            return w == null ? new List<string>() : w.Slots.Where(s => s != null).ToList();
        }
    }
}
