using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    public static partial class SpellShots
    {
        /// <summary>
        /// Noita's LaserEmitterComponent (design/magic_plan.md Phase 3.5), read from the shot's components in the script
        /// store every frame, so its scripts switch it (laser_emitter_start.lua sets is_emitting). While it emits: a beam
        /// along the shot's heading (velocity_sets_rotation, Noita's default) + laser_angle_add_rad, up to laser.max_length,
        /// stopped by the ground (Core LaserBeam). Creatures in it take laser.damage_to_entities a frame, reported as Noita
        /// reports it ($damage_plasmabeam, the probe); hittable shots in it go off (the probe: LASER_EMITTER_RAY's orbs set
        /// the pipe bombs off at frame 7). Not yet: digging (damage_to_cells), beam_radius.
        /// </summary>
        static void Lasers(Shot s)
        {
            if (s.Script == 0 || _scripts == null)
                return;
            foreach (var c in _scripts.Components(s.Script, "LaserEmitterComponent", true))
            {
                if (!c.Enabled)
                    continue;
                string emitting = c.Get("is_emitting") ?? Docs?.Default("LaserEmitterComponent", "is_emitting") ?? "1";
                int until = (int)c.Float("emit_until_frame", -1);
                if (emitting != "1" && emitting != "true" && !(until >= 0 && _host != null && _host.FrameNum < until))
                    continue;
                float length = c.Float("laser.max_length", 0);
                if (length <= 0)
                {
                    if (NotYet.Add("laser:" + s.Lua.File))
                        Entry.Log("spell laser without laser.max_length: " + s.Lua.File);
                    continue;
                }
                float angle = s.Rot + c.Float("laser_angle_add_rad", 0);
                var end = LaserBeam.End(s.Pos.X / Px, s.Pos.Y / Px, angle, length,
                    (x, y) => Physics.Mats.InWorld((int)(x * Px / 16), (int)(y * Px / 16)) && Physics.Mats.Solid((int)(x * Px / 16), (int)(y * Px / 16)));
                Vector2 a = s.Pos, b = new Vector2(end.x, end.y) * Px;
                float damage = c.Float("laser.damage_to_entities", 0) * 25f;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var n = Main.npc[i];
                    if (n.active && !n.friendly && !n.dontTakeDamage && n.life > 0 && damage > 0 &&
                        Collision.CheckAABBvLineCollision(n.position, n.Size, a, b))
                        Strike(s, n, damage, "$damage_plasmabeam");
                }
                foreach (var o in Live)
                    if (o != s && o.Life > 1 && Hittable(o.Lua.File) && DistanceToSegment(o.Pos, a, b) <= 6f)
                        o.Life = 1;
                for (float t = 0; t <= 1f; t += 12f / Math.Max(12f, Vector2.Distance(a, b)))
                    if (Main.rand.Next(3) == 0)
                        Dust.NewDustPerfect(Vector2.Lerp(a, b, t), DustID.PurpleTorch, Vector2.Zero).noGravity = true;
            }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 < 0.0001f ? 0 : MathHelper.Clamp(Vector2.Dot(p - a, ab) / len2, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
