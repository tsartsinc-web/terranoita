using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
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

        /// <summary>The components of one Noita entity file, after its Base chain (Core's NoitaEntityXml).</summary>
        static List<Extra> ReadExtra(string file)
        {
            if (ExtraFiles.TryGetValue(file, out var list))
                return list;
            list = new List<Extra>();
            try
            {
                if (NoitaArt.ReadText(file) != null)
                    foreach (var c in NoitaEntityXml.Load(file, NoitaArt.ReadText).Components)
                    {
                        var e = new Extra { Type = c.Type, F = new Dictionary<string, string>(c.Fields, StringComparer.Ordinal) };
                        e.F["_enabled"] = c.Enabled ? "1" : "0";
                        list.Add(e);
                    }
            }
            catch (Exception ex) { Entry.Error("spell entity " + file, ex); }
            ExtraFiles[file] = list;
            return list;
        }

        // components of the projectile's own file played here (its ProjectileComponent, CellEater and AreaDamage are
        // already in spell_projectiles.json)
        static readonly HashSet<string> OwnTypes = new HashSet<string>
        {
            "HomingComponent", "SineWaveComponent", "MagicConvertMaterialComponent", "ParticleEmitterComponent",
            "SpriteParticleEmitterComponent", "LightComponent", "HitEffectComponent", "TeleportProjectileComponent",
            "BlackHoleComponent", "MaterialSeaSpawnerComponent", "EnergyShieldComponent", "CollisionTriggerComponent",
        };

        static List<Extra> ExtrasOf(LuaShot ls)
        {
            var all = ReadExtra(ls.File ?? "").Where(c => c.Enabled && OwnTypes.Contains(c.Type)).ToList();
            foreach (var f in ls.Text("extra_entities").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var comps = ReadExtra(f.Trim());
                all.AddRange(comps.Where(c => c.Enabled));
                if (comps.Any(c => c.Type == "LuaComponent") && NotYet.Add(f))
                    Entry.Log("spell extra entity with a Lua script, not run yet: " + f);
            }
            return all;
        }

        static bool MortalNear(Vector2 at, float radius)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n.active && !n.friendly && n.life > 0 && Vector2.Distance(n.Center, at) <= radius + n.width / 2f)
                    return true;
            }
            return false;
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
                            EatAt(s.Pos, e.N("radius", 10) * Px, e.N("eat_probability", 100), Physics.Blast.PickPower(s.Owner));
                        break;
                    case "AreaDamageComponent":
                        AreaDamageAt(s, e.N("aabb_max.x", 8) * Px, e.N("damage_per_frame", 0.1f) * 25f);
                        break;
                    case "BlackHoleComponent":
                        BlackHole(s, e);
                        break;
                    case "MaterialSeaSpawnerComponent":
                        Sea(s, e);
                        break;
                    case "CollisionTriggerComponent":
                        // Noita's mines: a "mortal" within radius sets it off, it dies timer_for_destruction frames later
                        // (destroy_this_entity_when_triggered) and explodes (on_death_explode)
                        if (!s.Triggered && s.Age > 10 && e.N("destroy_this_entity_when_triggered", 1) > 0 && MortalNear(s.Pos, e.N("radius", 32) * Px))
                        {
                            s.Triggered = true;
                            s.Life = Math.Max(1, (int)e.N("timer_for_destruction", 0));
                        }
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

        /// <summary>Noita's BlackHoleComponent: creatures are pulled in and hurt at its centre.</summary>
        static void BlackHole(Shot s, Extra e)
        {
            float pull = e.N("particle_attractor_force", 2) * 0.05f;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (!n.active || n.friendly || n.boss || n.life <= 0 || n.CountsAsACritter || n.dontTakeDamage)
                    continue;
                var to = s.Pos - n.Center;
                float d = to.Length();
                if (d > 240 || d < 1)
                    continue;
                n.velocity += to / d * pull * (1f - d / 240f) * 4f;
                if (d < 24 + n.width / 2f && s.Age % 10 == 0 && Main.rand.NextFloat() < Math.Max(0.1f, e.N("damage_probability", 0.25f)) * 4f)
                    Strike(s, n, 25f);
            }
        }

        /// <summary>Noita's MaterialSeaSpawnerComponent (sea of water/lava/acid... spells): the material pours into a
        /// big box under where the spell is.</summary>
        static void Sea(Shot s, Extra e)
        {
            if (!Physics.Patches.On)
                return;
            float w = e.N("size.x", 300) * Px / 16f, h = e.N("size.y", 256) * Px / 16f;
            float cx = s.Pos.X / 16f + e.N("offset.x", 0) * Px / 16f, cy = s.Pos.Y / 16f + e.N("offset.y", 0) * Px / 16f;
            int per = Math.Max(1, (int)e.N("speed", 10) / 2);
            for (int k = 0; k < per; k++)
            {
                int x = (int)(cx + (Main.rand.NextFloat() - 0.5f) * w), y = (int)(cy + (Main.rand.NextFloat() - 0.5f) * h);
                if (Physics.Mats.InWorld(x, y) && !Main.tile[x, y].active())
                    Physics.Fluids.Add(x, y, e.S("material", "water"), 255);
            }
        }

        /// <summary>Noita's TeleportProjectileComponent: the caster appears where the shot ends.</summary>
        static void TeleportOwner(Shot s)
        {
            var p = s.Owner;
            if (p == null || !p.active || p.dead || !s.Extras.Any(x => x.Type == "TeleportProjectileComponent"))
                return;
            var to = s.Pos - new Vector2(p.width / 2f, p.height / 2f);
            var back = s.Vel.LengthSquared() > 0.01f ? -Vector2.Normalize(s.Vel) * 8f : new Vector2(0, -8);
            for (int k = 0; k < 12 && Collision.SolidCollision(to, p.width, p.height); k++)
                to += back;
            if (Collision.SolidCollision(to, p.width, p.height))
                return;
            p.Teleport(to, 1);
            p.velocity = Vector2.Zero;
        }

        /// <summary>EntityLoad from Noita's spell scripts: a spell projectile file becomes a spell shot, a creature
        /// file one of our Noita creatures. The entity number, or 0 when the file is not one of those yet.</summary>
        public static int LoadEntity(string file, Vector2 pos, Player owner)
        {
            if (string.IsNullOrEmpty(file))
                return 0;
            if (Def(file) != null)
            {
                int before = _nextId;
                var config = new Dictionary<string, MoonSharp.Interpreter.DynValue> { ["speed_multiplier"] = MoonSharp.Interpreter.DynValue.NewNumber(1) };
                Fire(new LuaShot { File = file, Config = config }, pos, new Vector2(owner != null && owner.direction < 0 ? -1 : 1, 0), owner, null);
                var made = _nextId > before ? Live.LastOrDefault() : null;
                if (made == null)
                    return 0;
                made.Vel = Vector2.Zero;   // Noita: EntityLoad makes it at rest; the script (GameShootProjectile) sends it
                if (made.Script == 0)
                    ScriptsAdd(made, true);   // the script that loaded it may shoot it (GameShootProjectile)
                return made.Script;
            }
            // a wand file (Noita's summon-a-wand spells): a wand made by Noita's own scripts, dropped there
            if (file.StartsWith("data/entities/items/wand", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var w = WandWindow.Store(new LuaWandMaker(NoitaArt.ReadText, Main.rand.Next()).MakeEntity(file, pos.X / Px, pos.Y / Px));
                    var item = MagicItems.MakeWand(w);
                    int at = Item.NewItem(new Terraria.DataStructures.EntitySource_WorldEvent(), (int)pos.X, (int)pos.Y, 16, 16, item.type, 1, false, item.prefix);
                    if (at >= 0 && at < Main.maxItems)
                        Main.item[at].prefix = item.prefix;
                }
                catch (Exception ex) { Entry.Error("spell wand " + file, ex); }
                return 0;
            }
            string id = System.IO.Path.GetFileNameWithoutExtension(file);
            var def = file.Contains("/animals/") ? Enemies.All.FirstOrDefault(x => x.Id == id) : null;
            if (def != null)
            {
                int who = Carriers.Spawn(def, (int)pos.X, (int)pos.Y);
                return who >= 0 ? 1000 + who : 0;
            }
            if (NotYet.Add("load:" + file))
                Entry.Log("spell EntityLoad not done yet: " + file);
            return 0;
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
                    if (!n.active || n.friendly || n.townNPC || n.life <= 0 || n.CountsAsACritter || n.dontTakeDamage)
                        continue;   // Noita homes on homing_target creatures, not on bunnies
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
            if (e.S("is_emitting") == "0")
                return;   // switched on by a script or only for the explosion
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
        static readonly Dictionary<int, (int type, Dictionary<string, uint> until)> Marks = new Dictionary<int, (int, Dictionary<string, uint>)>();

        /// <summary>Marks of creatures that are gone leave with them (a new creature in the slot starts clean).</summary>
        static void ForgetMarks()
        {
            if (Marks.Count == 0 || Main.GameUpdateCount % 30 != 0)
                return;
            foreach (int i in Marks.Keys.ToList())
                if (!Main.npc[i].active || Main.npc[i].type != Marks[i].type)
                    Marks.Remove(i);
        }

        static bool HasStatus(NPC n, string status)
        {
            switch (status)
            {
                case "WET": if (n.wet || (n.FindBuffIndex(BuffID.Wet) >= 0)) return true; break;
                case "OILED": if ((n.FindBuffIndex(BuffID.Oiled) >= 0)) return true; break;
                case "ON_FIRE": if (n.onFire || (n.FindBuffIndex(BuffID.OnFire) >= 0) || (n.FindBuffIndex(BuffID.OnFire3) >= 0)) return true; break;
                case "POISONED": if (n.poisoned) return true; break;
            }
            return Marks.TryGetValue(n.whoAmI, out var m) && m.type == n.type && m.until.TryGetValue(status, out uint until) && until > Main.GameUpdateCount;
        }

        static void Mark(NPC n, string status, int frames)
        {
            if (!Marks.TryGetValue(n.whoAmI, out var m) || m.type != n.type)
                Marks[n.whoAmI] = m = (n.type, new Dictionary<string, uint>());
            m.until[status] = Main.GameUpdateCount + (uint)frames;
        }

        const int ElectrocutionFrames = 40;   // Noita: effect_electricity.xml GameEffectComponent frames

        /// <summary>Noita's ELECTROCUTION: the creature cannot move while it lasts (status_effects.json).</summary>
        static void Electrocute(NPC n)
        {
            Hold(n, "ELECTROCUTION", ElectrocutionFrames);
        }

        /// <summary>A status that keeps the creature where it is (ELECTROCUTION, FROZEN); not bosses.</summary>
        static void Hold(NPC n, string status, int frames)
        {
            if (n.boss)
                return;
            if (!HasStatus(n, "ELECTROCUTION") && !HasStatus(n, "FROZEN"))
            {
                HeldAt[n.whoAmI] = n.position.X;
                if (DebugTools.Testing)
                    Entry.Log("SPELLS held " + n.TypeName + ": " + status + " " + frames + " frames");
            }
            Mark(n, status, frames);
        }

        static readonly Dictionary<int, float> HeldAt = new Dictionary<int, float>();

        /// <summary>Electrocuted and frozen creatures stand still (falling still works), sparking.</summary>
        static void HoldElectrocuted()
        {
            foreach (var kv in Marks)
            {
                var n = Main.npc[kv.Key];
                bool shocked = HasStatus(n, "ELECTROCUTION"), frozen = HasStatus(n, "FROZEN");
                if (!n.active || n.type != kv.Value.type || !shocked && !frozen)
                {
                    HeldAt.Remove(kv.Key);
                    continue;
                }
                // its own AI moves it after this: put it back where it was struck
                if (HeldAt.TryGetValue(kv.Key, out float x))
                    n.position.X = x;
                n.velocity.X = 0;
                if (n.velocity.Y < 0)
                    n.velocity.Y = 0;
                if (Main.rand.Next(3) == 0)
                    Dust.NewDust(n.position, n.width, n.height, shocked ? DustID.Electric : DustID.IceTorch, 0, 0, 0, default, 0.7f);
            }
        }

        /// <summary>Critical chance added by HitEffectComponents (CRITICAL_HIT_BOOST when the target is wet...).</summary>
        static float CritBoost(Shot s, NPC n)
        {
            float add = 0;
            foreach (var e in s.Extras)
            {
                if (e.Type != "HitEffectComponent")
                    continue;
                string kind = e.S("effect_hit");
                // both conditions count on their own; "" and "NONE" = no condition
                bool Met(string c) => c.Length == 0 || c == "NONE" || HasStatus(n, c);
                if (kind == "CRITICAL_HIT_BOOST" && Met(e.S("condition_effect")) && Met(e.S("condition_status")))
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
                foreach (var g in ReadExtra(f.Trim()).Where(c => c.Type == "GameEffectComponent"))
                {
                    string effect = g.S("effect") ?? "";
                    int frames = (int)g.N("frames", 600);
                    if (frames <= 0)
                        frames = 600;
                    switch (effect)
                    {
                        case "NONE": case "": continue;
                        case "ON_FIRE": n.AddBuff(BuffID.OnFire, frames); break;
                        case "WET": n.AddBuff(BuffID.Wet, frames); break;
                        case "OILED": n.AddBuff(BuffID.Oiled, frames); break;
                        case "POISON": n.AddBuff(BuffID.Poisoned, frames); break;
                        // Noita: a frozen creature cannot move while it lasts (held like ELECTROCUTION)
                        case "FROZEN": n.AddBuff(BuffID.Frostburn, frames); Hold(n, "FROZEN", frames); break;
                        case "BLOODY": Mark(n, "BLOODY", frames); break;
                        default:
                            if (NotYet.Add("effect:" + effect))
                                Entry.Log("spell status not done yet: " + effect + " (" + f + ")");
                            break;
                    }
                    Mark(n, effect == "POISON" ? "POISONED" : effect, frames);
                }
        }
    }
}
