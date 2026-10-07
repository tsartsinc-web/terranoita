using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// What modifier spells add to a shot that is not a Lua script: the components of Noita's extra_entities files
    /// (read from the player's Noita) and the shot config's trail_material / game_effect_entities. Homing, sine wave,
    /// arcs between the shots of one cast, material conversion, particle trails, light, ground eaters, area damage,
    /// energy shield, critical hits on wet/oiled/bloody/burning targets, statuses applied on hit.
    /// </summary>
    public static partial class SpellShots
    {
        sealed class Extra
        {
            public string Type;
            public Dictionary<string, string> F;
            public float N(string k, float d) => F.TryGetValue(k, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
            public string S(string k, string d = "") => F.TryGetValue(k, out var s) ? s : d;
            public bool Enabled => S("_enabled", "1") != "0";
        }

        static readonly Dictionary<string, List<Extra>> ExtraFiles = new Dictionary<string, List<Extra>>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> NotYet = new HashSet<string>();
        static readonly Regex CompRx = new Regex(@"<(\w+Component)\b([^>]*)>", RegexOptions.Compiled);
        static readonly Regex AttrRx = new Regex(@"([\w.]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        /// <summary>The components of one Noita entity file (no Base chains: extra_entities files are flat).</summary>
        static List<Extra> ReadExtra(string file)
        {
            if (ExtraFiles.TryGetValue(file, out var list))
                return list;
            list = new List<Extra>();
            string xml = NoitaArt.ReadText(file);
            if (xml != null)
            {
                xml = Regex.Replace(xml, "<!--.*?-->", "", RegexOptions.Singleline);
                foreach (Match m in CompRx.Matches(xml))
                {
                    var e = new Extra { Type = m.Groups[1].Value, F = new Dictionary<string, string>(StringComparer.Ordinal) };
                    foreach (Match a in AttrRx.Matches(m.Groups[2].Value))
                        e.F[a.Groups[1].Value] = a.Groups[2].Value;
                    list.Add(e);
                }
            }
            ExtraFiles[file] = list;
            return list;
        }

        static List<Extra> ExtrasOf(LuaShot ls)
        {
            var all = new List<Extra>();
            foreach (var f in ls.Text("extra_entities").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var comps = ReadExtra(f.Trim());
                all.AddRange(comps.Where(c => c.Enabled));
                if (comps.Any(c => c.Type == "LuaComponent") && NotYet.Add(f))
                    Entry.Log("spell extra entity with a Lua script, not run yet: " + f);
            }
            return all;
        }

        // ---- every frame ----

        static void StepExtras(Shot s)
        {
            if (s.Extras.Count == 0 && s.Trail.Length == 0)
                return;
            foreach (var e in s.Extras)
                switch (e.Type)
                {
                    case "HomingComponent": Homing(s, e); break;
                    case "SineWaveComponent": SineWave(s, e); break;
                    case "ArcComponent": Arc(s, e); break;
                    case "MagicConvertMaterialComponent":
                        if (s.Age % 15 == 1 && Physics.Patches.On)
                            Physics.Fluids.ConvertMaterial((int)(s.Pos.X / 16), (int)(s.Pos.Y / 16), Math.Max(1, (int)(e.N("radius", 16) * Px / 16)),
                                e.S("from_material"), e.S("to_material"));
                        break;
                    case "ParticleEmitterComponent":
                    case "SpriteParticleEmitterComponent":
                        Particles(s, e);
                        break;
                    case "LightComponent":
                        float lr = e.N("r", 255) / 255f, lg = e.N("g", 255) / 255f, lb = e.N("b", 255) / 255f;
                        Lighting.AddLight(s.Pos, lr, lg, lb);
                        break;
                    case "CellEaterComponent":
                        if (s.Age % 3 == 0)
                            EatAt(s.Pos, e.N("radius", 10) * Px, e.N("eat_probability", 100));
                        break;
                    case "AreaDamageComponent":
                        AreaDamageAt(s, e.N("aabb_max.x", 8) * Px, e.N("damage_per_frame", 0.1f) * 25f);
                        break;
                    case "EnergyShieldComponent":
                        // stops enemy shots close to the spell
                        Shots.StopNear(s.Pos, Math.Max(12f, e.N("radius", 8) * Px));
                        break;
                }
            // trail_material: a fire/water/oil/acid/poison/gunpowder trail
            if (s.Trail.Length > 0 && s.Age % 4 == 0)
                foreach (var m in s.Trail)
                {
                    int tx = (int)(s.Pos.X / 16), ty = (int)(s.Pos.Y / 16);
                    if (m == "fire")
                    {
                        Dust.NewDust(s.Pos - new Vector2(4, 4), 8, 8, DustID.Torch);
                        if (Physics.Patches.On)
                            Physics.Fluids.Ignite(tx, ty);
                        foreach (var n in NpcsNear(s.Pos, 20))
                            n.AddBuff(BuffID.OnFire, 180);
                    }
                    else if (Physics.Patches.On)
                        Physics.Fluids.Add(tx, ty, m, Math.Max(4, (int)s.Lua.Get("trail_material_amount")));
                }
        }

        static Vector2 NoitaVel(Shot s) => s.Vel * 60f / Px;
        static void SetNoitaVel(Shot s, Vector2 v) => s.Vel = v * Px / 60f;

        /// <summary>Noita's HomingComponent: the shot turns toward the nearest creature (or its caster: boomerang,
        /// homing_wand), negative strength turns it away; just_rotate_towards_target turns by max_turn_rate.</summary>
        static void Homing(Shot s, Extra e)
        {
            Vector2? target = null;
            float range = e.N("detect_distance", 150) * Px;
            if (e.S("target_who_shot") == "1" || e.S("target_tag") == "wand")
            {
                if (s.Owner != null && s.Owner.active && Vector2.Distance(s.Owner.Center, s.Pos) <= range)
                    target = s.Owner.Center;
            }
            else
            {
                float best = range;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var n = Main.npc[i];
                    if (!n.active || n.friendly || n.townNPC || n.life <= 0)
                        continue;
                    float d = Vector2.Distance(n.Center, s.Pos);
                    if (d < best)
                    {
                        best = d;
                        target = n.Center;
                    }
                }
            }
            if (target == null)
                return;
            var v = NoitaVel(s);
            float speed = v.Length();
            if (speed < 1f)
                return;
            var dir = Vector2.Normalize(target.Value - s.Pos);
            if (e.S("just_rotate_towards_target") == "1")
            {
                float now = (float)Math.Atan2(v.Y, v.X), want = (float)Math.Atan2(dir.Y, dir.X);
                float diff = MathHelper.WrapAngle(want - now), turn = e.N("max_turn_rate", 0.05f);
                now += MathHelper.Clamp(diff, -turn, turn);
                SetNoitaVel(s, new Vector2((float)Math.Cos(now), (float)Math.Sin(now)) * speed);
                return;
            }
            // Noita: velocity * multiplier + direction * targeting coefficient; the speed is kept here
            var blended = v * e.N("homing_velocity_multiplier", 0.9f) + dir * e.N("homing_targeting_coeff", 175);
            if (blended.LengthSquared() > 0.01f)
                SetNoitaVel(s, Vector2.Normalize(blended) * speed);
        }

        /// <summary>Noita's SineWaveComponent: the shot weaves across its path.</summary>
        static void SineWave(Shot s, Extra e)
        {
            float life = e.N("lifetime", -1);
            if (life >= 0 && s.Age > life)
                return;
            var v = NoitaVel(s);
            float speed = v.Length();
            if (speed < 1f)
                return;
            float freq = e.N("sinewave_freq", 1), m = e.N("sinewave_m", 0.6f);
            // turn by the change of the wave's angle this frame, so the mean direction stays
            float t = s.Age / 60f * MathHelper.TwoPi * freq * 4f;
            float turn = m * ((float)Math.Sin(t) - (float)Math.Sin(t - MathHelper.TwoPi * freq * 4f / 60f));
            float a = (float)Math.Atan2(v.Y, v.X) + turn;
            SetNoitaVel(s, new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * speed);
        }

        /// <summary>Noita's ArcComponent: an arc between this shot and the other arc shot of the same cast
        /// (fired the same frame by the same caster): lightning hurts, fire burns, poison/gunpowder are spilled.</summary>
        static void Arc(Shot s, Extra e)
        {
            if (s.Age > e.N("lifetime", 120))
                return;
            Shot other = null;
            float best = float.MaxValue;
            foreach (var o in Live)
                if (o != s && o.Owner == s.Owner && o.Born == s.Born && o.Id < s.Id && o.Extras.Any(x => x.Type == "ArcComponent"))
                {
                    float d = Vector2.DistanceSquared(o.Pos, s.Pos);
                    if (d < best)
                    {
                        best = d;
                        other = o;
                    }
                }
            if (other == null || best > 600 * 600)
                return;
            bool lightning = e.S("type") == "LIGHTNING";
            string mat = e.S("material");
            int dust = lightning ? DustID.Electric : mat == "fire" ? DustID.Torch : mat == "poison" ? DustID.CursedTorch : DustID.Smoke;
            float len = (float)Math.Sqrt(best);
            for (float k = 0; k < len; k += 10)
            {
                var at = Vector2.Lerp(s.Pos, other.Pos, k / Math.Max(1f, len));
                if (Main.rand.Next(3) == 0)
                    Dust.NewDustPerfect(at, dust, Vector2.Zero, 0, default(Color), 0.8f).noGravity = true;
                if (s.Age % 6 == 0 && mat.Length > 0 && mat != "fire" && Physics.Patches.On && Main.rand.Next(4) == 0)
                    Physics.Fluids.Add((int)(at.X / 16), (int)(at.Y / 16), mat, 6);
            }
            if (s.Age % 10 != 0)
                return;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly || n.dontTakeDamage || n.life <= 0 || !OnSegment(n.Hitbox, s.Pos, other.Pos))
                    continue;
                if (lightning)
                    Strike(s, n, 0.4f * 25f);   // Noita's arc lightning: electricity damage while it touches
                else if (mat == "fire")
                    n.AddBuff(BuffID.OnFire, 180);
                else if (mat == "poison")
                    n.AddBuff(BuffID.Poisoned, 180);
            }
        }

        static bool OnSegment(Rectangle box, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            for (float k = 0; k <= len; k += 8)
            {
                var at = Vector2.Lerp(a, b, k / Math.Max(1f, len));
                if (box.Contains((int)at.X, (int)at.Y))
                    return true;
            }
            return false;
        }

        /// <summary>Noita's particle emitters on a shot: a trail of coloured sparks.</summary>
        static void Particles(Shot s, Extra e)
        {
            int every = Math.Max(1, (int)e.N("emission_interval_min_frames", 2));
            if (s.Age % every != 0 || Main.gamePaused)
                return;
            string name = (e.S("emitted_material_name") + " " + e.S("sprite_file")).ToLowerInvariant();
            Color c = name.Contains("red") || name.Contains("blood") ? new Color(255, 70, 60) :
                      name.Contains("green") || name.Contains("poison") ? new Color(90, 255, 90) :
                      name.Contains("purple") || name.Contains("plasma") ? new Color(200, 90, 255) :
                      name.Contains("yellow") ? new Color(255, 240, 90) :
                      name.Contains("orange") || name.Contains("fire") ? new Color(255, 160, 50) :
                      name.Contains("blue") || name.Contains("freez") || name.Contains("ice") ? new Color(110, 190, 255) :
                      name.Contains("electric") ? new Color(160, 220, 255) : Color.White;
            var d = Dust.NewDustPerfect(s.Pos + Main.rand.NextVector2Circular(3, 3), DustID.TintableDustLighted, -s.Vel * 0.1f, 0, c, 0.9f);
            d.noGravity = true;
        }

        static IEnumerable<NPC> NpcsNear(Vector2 pos, float r)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && !n.friendly && !n.dontTakeDamage && n.life > 0 && Vector2.Distance(n.Center, pos) <= r + n.width / 2f)
                    yield return n;
            }
        }

        // ---- on hit ----

        /// <summary>Noita statuses a hit creature has (for critical hits): Terraria buffs and wetness, plus our own
        /// marks for the ones Terraria has no buff for.</summary>
        static readonly Dictionary<int, Dictionary<string, uint>> Marks = new Dictionary<int, Dictionary<string, uint>>();

        static bool HasStatus(NPC n, string status)
        {
            switch (status)
            {
                case "WET": if (n.wet || (n.FindBuffIndex(BuffID.Wet) >= 0)) return true; break;
                case "OILED": if ((n.FindBuffIndex(BuffID.Oiled) >= 0)) return true; break;
                case "ON_FIRE": if (n.onFire || (n.FindBuffIndex(BuffID.OnFire) >= 0) || (n.FindBuffIndex(BuffID.OnFire3) >= 0)) return true; break;
                case "POISONED": if (n.poisoned) return true; break;
            }
            return Marks.TryGetValue(n.whoAmI, out var m) && m.TryGetValue(status, out uint until) && until > Main.GameUpdateCount;
        }

        static void Mark(NPC n, string status, int frames)
        {
            if (!Marks.TryGetValue(n.whoAmI, out var m))
                Marks[n.whoAmI] = m = new Dictionary<string, uint>();
            m[status] = Main.GameUpdateCount + (uint)frames;
        }

        /// <summary>Critical chance added by HitEffectComponents (CRITICAL_HIT_BOOST when the target is wet...).</summary>
        static float CritBoost(Shot s, NPC n)
        {
            float add = 0;
            foreach (var e in s.Extras)
            {
                if (e.Type != "HitEffectComponent")
                    continue;
                string kind = e.S("effect_hit"), cond = e.S("condition_effect", e.S("condition_status"));
                if (kind == "CRITICAL_HIT_BOOST" && (cond.Length == 0 || HasStatus(n, cond)))
                    add += e.N("value", 100);
                else if (kind.StartsWith("LOAD_") && NotYet.Add("hit:" + e.S("value_string")))
                    Entry.Log("spell hit effect not done yet: " + kind + " " + e.S("value_string"));
            }
            return add;
        }

        /// <summary>game_effect_entities: Noita's status effects put on what the shot hits.</summary>
        static void ApplyStatuses(Shot s, NPC n)
        {
            foreach (var f in s.Lua.Text("game_effect_entities").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var g = ReadExtra(f.Trim()).FirstOrDefault(c => c.Type == "GameEffectComponent");
                string effect = g?.S("effect") ?? "";
                int frames = (int)(g?.N("frames", 600) ?? 600);
                if (frames <= 0)
                    frames = 600;
                switch (effect)
                {
                    case "ON_FIRE": n.AddBuff(BuffID.OnFire, frames); break;
                    case "WET": n.AddBuff(BuffID.Wet, frames); break;
                    case "OILED": n.AddBuff(BuffID.Oiled, frames); break;
                    case "POISON": n.AddBuff(BuffID.Poisoned, frames); break;
                    case "FROZEN": n.AddBuff(BuffID.Frostburn, frames); n.velocity *= 0.2f; break;
                    case "BLOODY": Mark(n, "BLOODY", frames); break;
                    default:
                        if (effect.Length > 0 && NotYet.Add("effect:" + effect))
                            Entry.Log("spell status not done yet: " + effect + " (" + f + ")");
                        break;
                }
                if (effect.Length > 0)
                    Mark(n, effect == "POISON" ? "POISONED" : effect, frames);
            }
        }
    }
}
