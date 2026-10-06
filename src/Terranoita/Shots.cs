using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Generated;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace Terranoita.Game
{
    /// <summary>
    /// Enemy shots: the mod's own list, not Terraria projectiles (design/sheets/systems.json, enemy_projectiles).
    /// Single player: they hurt the local player only.
    /// </summary>
    public static class Shots
    {
        sealed class Shot
        {
            public NoitaNpc From;
            public AttackDef Attack;
            public ProjectileDef Def;
            public Vector2 Pos, Vel;
            public int Life;
        }

        static readonly List<Shot> Live = new List<Shot>();
        const int Max = 300;

        public static void Fire(NoitaNpc from, AttackDef a, ProjectileDef p, Vector2 pos, Vector2 vel)
        {
            if (Live.Count >= Max)
                return;
            Live.Add(new Shot { From = from, Attack = a, Def = p, Pos = pos, Vel = vel, Life = p.LifetimeFrames > 0 ? p.LifetimeFrames : 300 });
            NoitaSound.PlayFirst(p.Audio, pos, "create");
        }

        public static void Clear() => Live.Clear();

        static float Part(AttackDef a, bool explosion)
        {
            float sum = 0;
            if (a.Damage == null)
                return 0;
            foreach (var kv in a.Damage)
                if ((kv.Key == "explosion") == explosion && kv.Value != null && kv.Value.Length > 0)
                    sum += kv.Value.Length > 1 ? (kv.Value[0] + kv.Value[1]) / 2 : kv.Value[0];
            return sum;
        }

        static void Update()
        {
            if (Main.gameMenu || Live.Count == 0)
                return;
            var me = Main.LocalPlayer;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var s = Live[i];
                s.Vel.Y += s.Def.Gravity;
                if (s.Def.Drag < 0)
                {
                    // Noita lasers: negative air friction speeds them up, to the terminal velocity
                    // (positive friction is not applied yet: Brain.Aim does not allow for it)
                    s.Vel *= 1f - s.Def.Drag / 60f;
                    float max = s.Def.MaxSpeed > 0 ? s.Def.MaxSpeed : 50f;
                    if (s.Vel.Length() > max)
                        s.Vel = Vector2.Normalize(s.Vel) * max;
                }
                s.Pos += s.Vel;
                s.Life--;
                bool hitPlayer = me.active && !me.dead && me.Hitbox.Contains((int)s.Pos.X, (int)s.Pos.Y);
                bool hitTile = Collision.SolidCollision(s.Pos - new Vector2(2, 2), 4, 4);
                if (s.Def.Effect == "explosive" && hitTile && s.Life > 0)
                {
                    // thrown tnt bounces until its fuse runs out
                    s.Pos -= s.Vel;
                    s.Vel = new Vector2(-s.Vel.X * 0.4f, -s.Vel.Y * 0.3f);
                    hitTile = false;
                }
                if (hitPlayer || hitTile || s.Life <= 0)
                {
                    Impact(s, me, hitPlayer);
                    Live.RemoveAt(i);
                }
                else if (s.Def.Effect == "fire")
                    Lighting.AddLight(s.Pos, 0.9f, 0.5f, 0.1f);
            }
        }

        static void Impact(Shot s, Player me, bool direct)
        {
            float mult = s.From.Tier.DmgMult;
            int dir = me.Center.X >= s.Pos.X ? 1 : -1;
            float dmg = direct ? Part(s.Attack, false) : 0;
            float radius = s.Def.ExplosionRadius * 16f;
            if (radius > 0)
            {
                if (Vector2.Distance(me.Center, s.Pos) <= radius + me.width / 2f)
                    dmg += Part(s.Attack, true);
                int dust = s.Def.Effect == "fire" || s.Def.Effect == "explosive" ? DustID.Torch : s.Def.Effect == "none" ? DustID.Smoke : DustID.GreenBlood;
                int n = (int)Math.Min(60, 6 + radius / 2);
                for (int k = 0; k < n; k++)
                {
                    var v = Main.rand.NextVector2Circular(radius / 10f, radius / 10f);
                    Dust.NewDust(s.Pos - new Vector2(4, 4), 8, 8, dust, v.X, v.Y);
                }
                if (!NoitaSound.Play(s.Def.ExplosionSound, s.Pos) && (s.Def.Effect == "explosive" || radius >= 24))
                    SoundEngine.PlaySound(SoundID.Item14, s.Pos);
            }
            else
                NoitaSound.PlayFirst(s.Def.Audio, s.Pos, "destroy");
            if (dmg <= 0 || !me.active || me.dead)
                return;
            int final = Math.Max(1, (int)Math.Round(dmg * mult));
            if (me.Hurt(Carriers.DeathReason(s.From, me), final, dir) <= 0)
                return;
            switch (s.Def.Effect)
            {
                case "fire": me.AddBuff(BuffID.OnFire, 180); break;
                case "poison": me.AddBuff(BuffID.Poisoned, 300); break;
                case "acid": me.AddBuff(BuffID.Poisoned, 120); break;   // early-game strength, not Venom
                case "neutralize": me.AddBuff(BuffID.Slow, 300); break;  // neutralizer_target.xml: MOVEMENT_SLOWER_2X
            }
            Entry.Log(s.From.Def.Id + " shot " + s.Def.Id + " hits for " + final + (direct ? "" : " (blast)"));
        }

        static void Draw()
        {
            if (Live.Count == 0)
                return;
            var sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            foreach (var s in Live)
            {
                var art = NoitaArt.Get(s.Def.Sprite);
                if (art?.Texture == null)
                {
                    // no image of its own: Noita draws it with particles; a trail of dust in that material's colour
                    if (!Main.gamePaused)
                        Dust.NewDustPerfect(s.Pos, ParticleDust(s.Def.Particle), Vector2.Zero, 0, default(Color), 1.2f).noGravity = true;
                    Lighting.AddLight(s.Pos, 0.3f, 0.3f, 0.5f);
                    continue;
                }
                var anim = art.Sprite.Find("fireball", "default", "stand");
                int fx = 0, fy = 0, fw = art.Texture.Width, fh = art.Texture.Height;
                if (anim != null)
                    anim.FrameRect(anim.FrameAt((int)Main.GameUpdateCount), out fx, out fy, out fw, out fh);
                var origin = anim != null ? new Vector2(art.Sprite.OffsetX, art.Sprite.OffsetY) : new Vector2(fw / 2f, fh / 2f);
                var light = s.Def.Effect == "fire" ? Color.White : Lighting.GetColor((int)(s.Pos.X / 16), (int)(s.Pos.Y / 16));
                float rot = s.Def.Effect == "explosive" ? s.Life * 0.2f : (float)Math.Atan2(s.Vel.Y, s.Vel.X);
                sb.Draw(art.Texture, s.Pos - Main.screenPosition, new Rectangle(fx, fy, fw, fh), light, rot, origin, Terranoita.Noita.Units.PixelScale, SpriteEffects.None, 0f);
            }
            sb.End();
        }

        static int ParticleDust(string material)
        {
            string m = (material ?? "").ToLowerInvariant();
            if (m.Contains("blue") || m == "plasma_fading" || m.Contains("electric")) return DustID.BlueTorch;
            if (m.Contains("purple") || m.Contains("pink")) return DustID.PurpleTorch;
            if (m.Contains("green") || m.Contains("acid") || m.Contains("radioactive") || m.Contains("poison")) return DustID.GreenTorch;
            if (m.Contains("fire") || m.Contains("lava") || m.Contains("red") || m.Contains("orange")) return DustID.Torch;
            if (m.Contains("blood")) return DustID.Blood;
            return DustID.WhiteTorch;
        }

        [Hook("shots_update")]
        [HarmonyPatch(typeof(Main), "UpdateWorld_Projectiles")]
        static class UpdatePatch
        {
            static void Postfix() => Update();
        }

        [Hook("shots_draw")]
        [HarmonyPatch(typeof(Main), "DrawProjectiles")]
        static class DrawPatch
        {
            static void Postfix() => Draw();
        }
    }
}
