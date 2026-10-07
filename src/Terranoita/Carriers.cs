using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terranoita.Ai;
using Terranoita.Generated;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terranoita.Game
{
    /// <summary>A Noita enemy living in a carrier NPC slot.</summary>
    public sealed class NoitaNpc
    {
        public EnemyDef Def;
        public BalanceDef Tier;
        public Brain Brain;
        public string Name;
        /// <summary>A shield from a shield drone or hiisi: the next hit does nothing.</summary>
        public bool Shield;
        /// <summary>Frames it stays (nearly) invisible, from an invisibility hiisi.</summary>
        public int Invisible;
        /// <summary>Worms: the NPC slots of the body segments, next to the head first.</summary>
        public int[] Parts;
    }

    /// <summary>
    /// Every Noita enemy rides on NPC type 146 (NPCID.None3): unused by Terraria, no SetDefaults case, no AI, no loot,
    /// no banner, hidden from the Bestiary (design/sheets/systems.json, carrier_npc). This side table says which one.
    /// </summary>
    public static class Carriers
    {
        public const int CarrierType = 146;
        static readonly NoitaNpc[] Table = new NoitaNpc[Main.maxNPCs + 1];
        // worm body segments: also carrier NPCs, with the slot of their head here (-1: not a segment)
        static readonly int[] HeadOf = System.Linq.Enumerable.Repeat(-1, Main.maxNPCs + 1).ToArray();
        static readonly Random Rng = new Random();

        public static NoitaNpc Get(NPC npc) =>
            npc != null && npc.type == CarrierType && npc.whoAmI >= 0 && npc.whoAmI < Table.Length ? Table[npc.whoAmI] : null;

        /// <summary>A worm's body segment: hit like the worm, drawn and moved by its head.</summary>
        public static bool IsSegment(NPC npc) =>
            npc != null && npc.type == CarrierType && npc.whoAmI >= 0 && npc.whoAmI < HeadOf.Length && HeadOf[npc.whoAmI] >= 0;

        /// <summary>The head a segment belongs to.</summary>
        public static NPC HeadOfSegment(NPC npc) => IsSegment(npc) ? Main.npc[HeadOf[npc.whoAmI]] : null;

        public static int CountNear(Vector2 where, float range)
        {
            int n = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
                if (Main.npc[i].active && Get(Main.npc[i]) != null && Vector2.Distance(Main.npc[i].Center, where) < range)
                    n++;
            return n;
        }

        /// <summary>Spawn a Noita enemy standing with its feet at world pixel (x, bottom).</summary>
        public static int Spawn(EnemyDef def, int x, int bottom)
        {
            int i = NPC.NewNPC(new EntitySource_SpawnNPC(), x, bottom, CarrierType);
            if (i < 0 || i >= Main.maxNPCs)
                return -1;
            var npc = Main.npc[i];
            var tier = Defs.Tier[def.Tier];
            float size = (def.Size > 0 ? def.Size : 1f) * (def.HitboxMult > 0 ? def.HitboxMult : 1f);
            int w = (int)((def.Hitbox != null && def.Hitbox.Length > 1 ? def.Hitbox[0] : 20) * size);
            int h = (int)((def.Hitbox != null && def.Hitbox.Length > 1 ? def.Hitbox[1] : 20) * size);
            npc.width = Math.Max(8, w);
            npc.height = Math.Max(8, h);
            npc.position = new Vector2(x - npc.width / 2f, bottom - npc.height);
            npc.lifeMax = Math.Max(1, (int)Math.Round(def.NoitaHp * tier.HpMult));
            npc.life = npc.lifeMax;
            npc.defense = tier.Defense;
            npc.defDefense = tier.Defense;
            npc.damage = 0;                 // no contact damage: Noita enemies hurt with their attacks
            npc.defDamage = 0;
            npc.knockBackResist = 0.5f;
            npc.noGravity = true;           // the brain applies the enemy's own Noita gravity
            npc.noTileCollide = false;
            npc.aiStyle = 0;                // never -1: UpdateNPC indexes NPCID.Sets.NoMultiplayerSmoothingByAI[aiStyle]
            npc.value = 0;
            npc.friendly = false;
            bool noitaAudio = NoitaSound.Ready && def.Audio != null && def.Audio != "none";
            npc.HitSound = noitaAudio ? null : SoundID.NPCHit1;      // Noita's own hurt/death sounds in HitEffect
            npc.DeathSound = noitaAudio ? null : SoundID.NPCDeath1;
            npc.timeLeft = 750;             // NPC.activeTime (private)
            if (Array.IndexOf(def.Immunities ?? new string[0], "fire") >= 0)
                foreach (int b in new[] { BuffID.OnFire, BuffID.OnFire3, BuffID.Burning, BuffID.CursedInferno, BuffID.ShadowFlame })
                    npc.buffImmune[b] = true;
            HeadOf[i] = -1;
            Table[i] = new NoitaNpc { Def = def, Tier = tier, Brain = new Brain(def), Name = NoitaArt.Name(def) };
            npc.noTileCollide = Table[i].Brain.PassesTiles;     // ghosts drift and worms burrow through tiles
            if (Table[i].Brain.Burrows && def.Segments > 0)
                SpawnSegments(npc, Table[i]);
            Entry.Log("spawned " + def.Id + " (" + Table[i].Name + ") #" + i + " life " + npc.lifeMax + " at tile " + x / 16 + "," + bottom / 16);
            return i;
        }

        /// <summary>
        /// A worm's body as NPCs of its own (author: worms with a real body), like Terraria's worms: every segment can be
        /// hit and the hit goes to the head (Damage.Strike), fire and poison on the body burn the worm. Only the head bites,
        /// as in Noita.
        /// </summary>
        static void SpawnSegments(NPC head, NoitaNpc n)
        {
            n.Parts = new int[n.Def.Segments];
            for (int s = 0; s < n.Parts.Length; s++)
            {
                int j = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)head.Center.X, (int)head.Center.Y, CarrierType);
                n.Parts[s] = j >= 0 && j < Main.maxNPCs ? j : -1;
                if (n.Parts[s] < 0)
                    continue;
                var seg = Main.npc[j];
                Table[j] = null;
                HeadOf[j] = head.whoAmI;
                seg.width = head.width;
                seg.height = head.height;
                seg.Center = head.Center;
                seg.lifeMax = head.lifeMax;
                seg.life = head.life;
                seg.realLife = head.whoAmI;      // one health bar, Terraria's worms do the same
                seg.defense = seg.defDefense = head.defense;
                seg.damage = seg.defDamage = 0;
                seg.knockBackResist = 0f;
                seg.noGravity = true;
                seg.noTileCollide = true;
                seg.aiStyle = 0;
                seg.value = 0;
                seg.friendly = false;
                seg.HitSound = null;
                seg.DeathSound = null;
                seg.dontCountMe = true;
                seg.npcSlots = 0f;
                seg.timeLeft = head.timeLeft;
                Array.Copy(head.buffImmune, seg.buffImmune, Math.Min(head.buffImmune.Length, seg.buffImmune.Length));
            }
        }

        /// <summary>Where segment i (1 = next to the head, Segments = the tail) is on the head's trail; false while the worm has not come that far.</summary>
        static bool SegmentAt(NoitaNpc n, int i, out V2 at, out V2 ahead)
        {
            var trail = n.Brain.Trail;
            float spacing = n.Def.SegmentSpacing > 0 ? n.Def.SegmentSpacing : 24f;
            float want = i * spacing, walked = 0;
            int k;
            for (k = 1; k < trail.Count && walked < want; k++)
                walked += (trail[k] - trail[k - 1]).Length;
            at = ahead = default(V2);
            if (trail.Count < 2 || walked < want)
                return false;
            at = trail[k - 1];
            ahead = trail[Math.Max(0, k - 2)];
            return true;
        }

        /// <summary>After the head moved: the segments onto its trail, with its life; what the body caught goes to the head.</summary>
        static void PlaceSegments(NPC head, NoitaNpc n)
        {
            for (int s = 0; s < n.Parts.Length; s++)
            {
                int j = n.Parts[s];
                if (j < 0 || HeadOf[j] != head.whoAmI || !Main.npc[j].active)
                    continue;
                var seg = Main.npc[j];
                seg.Center = SegmentAt(n, s + 1, out var at, out _) ? new Vector2(at.X, at.Y) : head.Center;
                seg.velocity = Vector2.Zero;
                seg.life = head.life;
                seg.lifeMax = head.lifeMax;
                seg.timeLeft = head.timeLeft;
                for (int b = 0; b < seg.buffType.Length; b++)
                    if (seg.buffType[b] > 0 && seg.buffTime[b] > 0)
                    {
                        head.AddBuff(seg.buffType[b], seg.buffTime[b]);
                        seg.buffType[b] = 0;
                        seg.buffTime[b] = 0;
                    }
            }
        }

        /// <summary>A segment whose head is gone goes too (no loot: the head dropped it).</summary>
        static void FollowHead(NPC seg)
        {
            int h = HeadOf[seg.whoAmI];
            var head = Main.npc[h];
            var n = head.active ? Get(head) : null;
            if (n?.Parts == null || Array.IndexOf(n.Parts, seg.whoAmI) < 0 || seg.realLife != h)
            {
                HeadOf[seg.whoAmI] = -1;
                seg.life = 0;
                seg.active = false;
            }
        }

        public static void Forget(NPC npc)
        {
            if (Get(npc) != null)
                Table[npc.whoAmI] = null;
        }

        // ---- the body and attacks the brain works through -------------------------------------------------------

        sealed class Body : IBody
        {
            public NPC Npc;
            public V2 Center => new V2(Npc.Center.X, Npc.Center.Y);
            public V2 Velocity
            {
                get => new V2(Npc.velocity.X, Npc.velocity.Y);
                set => Npc.velocity = new Vector2(value.X, value.Y);
            }
            public int Width => Npc.width;
            public int Height => Npc.height;
            public bool OnGround => Npc.velocity.Y >= 0 &&
                Collision.SolidCollision(new Vector2(Npc.position.X, Npc.position.Y + Npc.height), Npc.width, 2, true);
            // a real wall: still blocked one tile higher (a one-tile step is walked over by Collision.StepUp, no jump)
            public bool HitWall => Npc.collideX &&
                Collision.SolidCollision(new Vector2(Npc.position.X + Npc.direction * 8, Npc.position.Y - 17), Npc.width, Npc.height);
        }

        /// <summary>Terraria's tiles as the brain's climbers, burrowers and swimmers see them.</summary>
        sealed class Terrain : ITerrain
        {
            static Tile At(V2 p)
            {
                int x = (int)(p.X / 16), y = (int)(p.Y / 16);
                return x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY ? Main.tile[x, y] : null;
            }

            public bool Solid(V2 p)
            {
                var t = At(p);
                return t != null && t.active() && !t.inActive() && Main.tileSolid[t.type] && !Main.tileSolidTop[t.type];
            }

            public bool Liquid(V2 p)
            {
                var t = At(p);
                return t != null && t.liquid > 64;
            }
        }

        sealed class Attacks : IAttackSink, ISpecialAttackSink
        {
            public NPC Npc;
            public NoitaNpc Noita;
            public Player Target;

            int Roll(AttackDef a)
            {
                Defs.DamageRange(a, out float min, out float max);
                if (max <= 0)
                    return 0;
                float noita = min + (float)Rng.NextDouble() * (max - min);
                return Math.Max(1, (int)Math.Round(noita * Noita.Tier.DmgMult));
            }

            public void Melee(AttackDef a) => Hit(a, Roll(a), "melee");
            public void DashHit(AttackDef a) => Hit(a, Roll(a), "lunge");

            void Hit(AttackDef a, int dmg, string how)
            {
                if (dmg <= 0 || Target == null || !Target.active || Target.dead)
                    return;
                int dir = Target.Center.X >= Npc.Center.X ? 1 : -1;
                double dealt = Target.Hurt(DeathReason(Noita, Target), dmg, dir);
                if (dealt > 0 && DebugTools.Testing)
                    Entry.Log(Noita.Def.Id + " " + how + " " + a.Id + " hits for " + (int)dealt);
            }

            public void Started(AttackDef a)
            {
                if (DebugTools.Testing)
                    Entry.Log(Noita.Def.Id + " starts " + a.Id + " at " + (int)(Vector2.Distance(Npc.Center, Target?.Center ?? Npc.Center) / 16) + " tiles");
                if (!NoitaSound.Play(a.Sound, Npc.Center))
                    SoundEngine.PlaySound(SoundID.Item1, Npc.Center);
            }

            public void Jumped() => NoitaSound.PlayFirst(Noita.Def.Audio, Npc.Center, "jump", "voc_jump", "_voc_jump");

            public void Shoot(AttackDef a, ProjectileDef p, V2 from, V2 velocity)
            {
                Shots.Fire(Noita, a, p, new Vector2(from.X, from.Y), new Vector2(velocity.X, velocity.Y));
            }

            /// <summary>Auras, summons, heals, support and death explosions (attacks sheet, kind).</summary>
            public void Special(AttackDef a)
            {
                switch (a.Kind)
                {
                    case "aura":
                        if (Target != null && Vector2.Distance(Target.Center, Npc.Center) <= a.RangeTiles * 16f + Target.width / 2f)
                        {
                            Hit(a, Roll(a), "aura");
                            int buff = a.Effect == "weak" ? BuffID.Weak : a.Effect == "slime" ? BuffID.Slimed :
                                       a.Effect == "confuse" ? BuffID.Confused : -1;
                            if (buff >= 0 && Target.active && !Target.dead)
                                Target.AddBuff(buff, 180);
                            else if (a.Effect == "berserk")
                                Entry.Warn("aura " + a.Id + ": berserk has no Terraria player effect yet");
                        }
                        break;
                    case "support":
                        Support(a);
                        break;
                    case "summon":
                    case "retaliate":       // a retaliation that releases creatures (the projectile kind fires as a shot)
                        Summon(a);
                        break;
                    case "heal":
                        HealAllies(a);
                        break;
                    case "death_explosion":
                        Explode(a);
                        break;
                    default:
                        Entry.Warn("special attack " + a.Id + " (" + a.Kind + ") has no effect in Terraria yet");
                        break;
                }
            }

            void Summon(AttackDef a)
            {
                if (a.Summons == null || Carriers.CountNear(Npc.Center, 40 * 16) >= 12)
                    return;
                int min = a.Count != null && a.Count.Length > 0 ? a.Count[0] : 1;
                int max = a.Count != null && a.Count.Length > 1 ? a.Count[1] : min;
                int n = Math.Max(1, min + Rng.Next(Math.Max(1, max - min + 1)));
                foreach (var id in a.Summons)
                    if (Defs.Enemy.TryGetValue(id, out var def))
                        for (int k = 0; k < n; k++)
                            Carriers.Spawn(def, (int)Npc.Center.X + Rng.Next(-16, 17), (int)(Npc.position.Y + Npc.height));
            }

            /// <summary>Shield (next hit does nothing) or invisibility for the nearest other Noita creature.</summary>
            void Support(AttackDef a)
            {
                float range = Math.Max(a.RangeTiles, 1f) * 16f;
                bool invisible = a.Id.Contains("invisib");
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var other = Main.npc[i];
                    var o = other.active && i != Npc.whoAmI ? Get(other) : null;
                    if (o == null || Vector2.Distance(other.Center, Npc.Center) > range || (invisible ? o.Invisible > 0 : o.Shield))
                        continue;
                    if (invisible)
                        o.Invisible = 600;
                    else
                        o.Shield = true;
                    if (DebugTools.Testing)
                        Entry.Log(Noita.Def.Id + " " + a.Id + " gives " + o.Def.Id + (invisible ? " invisibility" : " a shield"));
                    return;
                }
            }

            void HealAllies(AttackDef a)
            {
                Defs.DamageRange(a, out float min, out float max);
                int amount = Math.Max(1, (int)Math.Round(max * Noita.Tier.HpMult));
                float range = Math.Max(a.RangeTiles, 1f) * 16f;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var other = Main.npc[i];
                    if (other.active && i != Npc.whoAmI && Get(other) != null && other.life < other.lifeMax &&
                        Vector2.Distance(other.Center, Npc.Center) <= range)
                    {
                        int healed = Math.Min(amount, other.lifeMax - other.life);
                        other.life += healed;
                        other.HealEffect(healed);
                        if (DebugTools.Testing)
                            Entry.Log(Noita.Def.Id + " " + a.Id + " heals " + Get(other).Def.Id + " for " + healed);
                        return;
                    }
                }
            }

            void Explode(AttackDef a)
            {
                // radius: the sheet's range (death explosions of data.wak: ExplodeOnDamage/config_explosion, to verify)
                float radius = Math.Max(a.RangeTiles, 2f) * 16f;
                for (int k = 0; k < 30; k++)
                {
                    var v = Main.rand.NextVector2Circular(radius / 10f, radius / 10f);
                    Dust.NewDust(Npc.Center - new Vector2(4, 4), 8, 8, DustID.Torch, v.X, v.Y);
                }
                SoundEngine.PlaySound(SoundID.Item14, Npc.Center);
                if (Target != null && Target.active && !Target.dead && Vector2.Distance(Target.Center, Npc.Center) <= radius + Target.width / 2f)
                    Hit(a, Roll(a), "death explosion");
            }
        }

        public static Terraria.DataStructures.PlayerDeathReason DeathReason(NoitaNpc n, Player p) =>
            Terraria.DataStructures.PlayerDeathReason.ByCustomReason(p.name + " was killed by " + n.Name);

        static readonly Body TheBody = new Body();
        static readonly Attacks TheAttacks = new Attacks();
        static readonly Terrain TheTerrain = new Terrain();

        /// <summary>The player an enemy is after (Terraria's own targeting), as the brain sees it.</summary>
        static Target TargetOf(NPC npc, out Player player)
        {
            npc.TargetClosest(false);
            player = npc.target >= 0 && npc.target < Main.maxPlayers ? Main.player[npc.target] : null;
            var target = new Target();
            if (player != null && player.active && !player.dead && !player.ghost)
            {
                target.Has = true;
                target.Center = new V2(player.Center.X, player.Center.Y);
                target.Width = player.width;
                target.Height = player.height;
                target.Visible = Collision.CanHitLine(npc.position, npc.width, npc.height, player.position, player.width, player.height)
                                 && !(player.whoAmI == Main.myPlayer && Physics.Status.Has("INVISIBILITY"));   // Noita invisibility
            }
            return target;
        }

        static void Bind(NPC npc, NoitaNpc n, Player player)
        {
            TheBody.Npc = npc;
            TheAttacks.Npc = npc;
            TheAttacks.Noita = n;
            TheAttacks.Target = player;
        }

        // ---- patches --------------------------------------------------------------------------------------------

        [Hook("npc_ai")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.AI))]
        static class AiPatch
        {
            static bool Prefix(NPC __instance)
            {
                if (IsSegment(__instance))
                {
                    try { FollowHead(__instance); }
                    catch (Exception ex) { Entry.Error("npc_ai segment", ex); }
                    return false;
                }
                var n = Get(__instance);
                if (n == null)
                    return true;
                try { Think(__instance, n); }
                catch (Exception ex) { Entry.Error("npc_ai " + n.Def.Id, ex); }
                return false;
            }

            static void Think(NPC npc, NoitaNpc n)
            {
                var target = TargetOf(npc, out var player);
                Bind(npc, n, player);
                n.Brain.Update(TheBody, target, TheAttacks, Rng, TheTerrain);
                npc.direction = npc.spriteDirection = n.Brain.Direction;
                if (n.Def.Walks && !n.Def.Flies && !n.Brain.PassesTiles)
                    Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
                if (n.Brain.Burrows)
                    DigSound(npc);
                if (n.Parts != null)
                    PlaceSegments(npc, n);
            }

            // Terraria's worm digging sound (vanilla worm AI): while in the ground, more often the closer the player is
            static void DigSound(NPC npc)
            {
                if (npc.soundDelay > 0)
                {
                    npc.soundDelay--;
                    return;
                }
                if (!TheTerrain.Solid(new V2(npc.Center.X, npc.Center.Y)))
                    return;
                float dist = Vector2.Distance(npc.Center, Main.LocalPlayer.Center) / 40f;
                npc.soundDelay = (int)MathHelper.Clamp(dist, 10f, 20f);
                SoundEngine.PlaySound(15, (int)npc.position.X, (int)npc.position.Y, 1);
            }
        }

        [Hook("npc_findframe")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.FindFrame))]
        static class FramePatch
        {
            static bool Prefix(NPC __instance) => Get(__instance) == null && !IsSegment(__instance);   // animated from the Noita sprite when drawn
        }

        [Hook("npc_name")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.TypeName), MethodType.Getter)]
        static class NamePatch
        {
            static void Postfix(NPC __instance, ref string __result)
            {
                var n = Get(__instance) ?? Get(HeadOfSegment(__instance));   // a worm's body has the worm's name
                if (n != null)
                    __result = n.Name;
            }
        }

        [Hook("npc_health_bars")]
        [HarmonyPatch]
        static class HealthBarsPatch
        {
            // one health bar per worm (author): the body's bars are hidden by drawing them while the segments look unhurt
            static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(Main), "DrawInterface_14_EntityHealthBars");
            static bool Prepare() => TargetMethod() != null;

            static void Prefix(out int[] __state)
            {
                __state = new int[Main.maxNPCs];
                for (int i = 0; i < Main.maxNPCs; i++)
                    if (Main.npc[i].active && IsSegment(Main.npc[i]))
                    {
                        __state[i] = Main.npc[i].life;
                        Main.npc[i].life = Main.npc[i].lifeMax;
                    }
            }

            static void Postfix(int[] __state)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                    if (__state[i] > 0 && Main.npc[i].active && IsSegment(Main.npc[i]))
                        Main.npc[i].life = __state[i];
            }
        }

        /// <summary>Every frame: segments whose head is gone go too (the head may die or despawn anywhere).</summary>
        public static void Sweep()
        {
            for (int i = 0; i < Main.maxNPCs; i++)
                if (HeadOf[i] >= 0 && Main.npc[i].active && Main.npc[i].type == CarrierType)
                    FollowHead(Main.npc[i]);
        }

        [Hook("npc_draw")]
        [HarmonyPatch(typeof(Main), "DrawNPC")]
        static class DrawPatch
        {
            static bool Prefix(NPC npc)
            {
                if (IsSegment(npc))
                    return false;   // the head draws the whole worm
                var n = Get(npc);
                if (n == null)
                    return true;
                try { Draw(npc, n); }
                catch (Exception ex) { Entry.Error("npc_draw " + n.Def.Id, ex); }
                return false;
            }

            static void Draw(NPC npc, NoitaNpc n)
            {
                var art = NoitaArt.Get(n.Def.Sprite);
                if (art?.Texture == null)
                    return;
                var anim = art.Sprite.Find(AnimNames(n.Brain.Anim));
                // a plain .png sprite (eel_head.png, potion.png) has no animations: the whole image is the frame
                int fx = 0, fy = 0, fw = art.Texture.Width, fh = art.Texture.Height;
                if (anim != null)
                    anim.FrameRect(anim.FrameAt(n.Brain.AnimTicks), out fx, out fy, out fw, out fh);
                if (n.Invisible > 0)
                    n.Invisible--;
                var light = Lighting.GetColor((int)(npc.Center.X / 16), (int)(npc.Center.Y / 16));
                // lit exactly like Terraria NPCs: dark in the dark (a brightness floor showed them through walls)
                var color = new Color(light.R, light.G, light.B, (byte)255);
                float scale = Terranoita.Noita.Units.PixelScale * (n.Def.Size > 0 ? n.Def.Size : 1f);
                // the lowest visible pixel of the standing frame rests on the bottom of the hitbox
                var pivot = new Vector2(npc.Center.X, npc.position.Y + npc.height - art.Foot * scale + npc.gfxOffY) - Main.screenPosition;
                var fx2 = n.Brain.Direction < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                var origin = new Vector2(n.Brain.Direction < 0 ? fw - art.Sprite.OffsetX : art.Sprite.OffsetX, art.Sprite.OffsetY);
                if (n.Invisible > 0)
                    color *= 0.15f;
                if (n.Brain.Burrowing)
                {
                    // worms: body and tail along the head's trail, then the head turned to where it is going
                    DrawSegments(npc, n, color, scale);
                    // Noita's worm sprites face left (author: heads were back to front): turned half round, flipped to stay upright
                    float rot = (float)Math.Atan2(npc.velocity.Y, npc.velocity.X) + MathHelper.Pi;
                    var flip = npc.velocity.X >= 0 ? SpriteEffects.FlipVertically : SpriteEffects.None;
                    Main.spriteBatch.Draw(art.Texture, npc.Center - Main.screenPosition, new Rectangle(fx, fy, fw, fh), color,
                                          rot, new Vector2(fw / 2f, fh / 2f), scale, flip, 0f);
                    return;
                }
                Main.spriteBatch.Draw(art.Texture, pivot, new Rectangle(fx, fy, fw, fh), color, 0f, origin, scale, fx2, 0f);
            }

            static void DrawSegments(NPC npc, NoitaNpc n, Color color, float scale)
            {
                for (int i = n.Def.Segments; i >= 1; i--)   // tail first, so the head is drawn on top
                {
                    if (!SegmentAt(n, i, out var at, out var ahead))
                        continue;   // the worm has not travelled that far yet
                    var art = NoitaArt.Get(i == n.Def.Segments ? n.Def.TailSprite : n.Def.BodySprite);
                    if (art?.Texture == null)
                        continue;
                    var anim = art.Sprite.Find("stand", "walk", "default");
                    int fx = 0, fy = 0, fw = art.Texture.Width, fh = art.Texture.Height;
                    if (anim != null)
                        anim.FrameRect(anim.FrameAt(n.Brain.AnimTicks), out fx, out fy, out fw, out fh);
                    float rot = (float)Math.Atan2(ahead.Y - at.Y, ahead.X - at.X) + MathHelper.Pi;
                    var flip = ahead.X >= at.X ? SpriteEffects.FlipVertically : SpriteEffects.None;
                    Main.spriteBatch.Draw(art.Texture, new Vector2(at.X, at.Y) - Main.screenPosition, new Rectangle(fx, fy, fw, fh),
                                          color, rot, new Vector2(fw / 2f, fh / 2f), scale, flip, 0f);
                }
            }

            static string[] AnimNames(string anim)
            {
                switch (anim)
                {
                    case "attack": return new[] { "attack", "attack_ranged", "throw", "eat", "walk", "stand" };
                    case "fly": return new[] { "fly", "walk", "stand" };
                    case "jump_up": return new[] { "jump_up", "jump", "fly", "walk", "stand" };
                    case "jump_fall": return new[] { "jump_fall", "fall", "jump", "fly", "walk", "stand" };
                    case "walk": return new[] { "walk", "run", "fly", "stand" };
                    default: return new[] { anim, "stand", "fly" };
                }
            }
        }

        [Hook("npc_hiteffect")]
        [HarmonyPatch(typeof(NPC), nameof(NPC.HitEffect))]
        static class HitEffectPatch
        {
            static bool Prefix(NPC __instance, int hitDirection)
            {
                var n = Get(__instance);
                if (n == null)
                    return true;
                try
                {
                    // the player who is fighting it: retaliation aims at them, death explosions hurt them
                    var target = TargetOf(__instance, out var player);
                    Bind(__instance, n, player);
                    if (__instance.life > 0)
                        n.Brain.Hurt(TheBody, target, TheAttacks, Rng);
                    else
                        n.Brain.Died(TheAttacks);
                }
                catch (Exception ex) { Entry.Error("npc_hiteffect " + n.Def.Id, ex); }
                if (__instance.life > 0)
                    NoitaSound.PlayFirst(n.Def.Audio, __instance.Center, "damage/projectile", "damage/melee");
                else
                    NoitaSound.PlayFirst(n.Def.Audio, __instance.Center, "death");
                int dust = BloodDust(n.Def.Blood);
                int count = __instance.life > 0 ? 6 : 30;
                for (int k = 0; k < count; k++)
                    Dust.NewDust(__instance.position, __instance.width, __instance.height, dust, hitDirection * 2f, -1.5f);
                Bleed(__instance, n);
                return false;
            }

            static System.Collections.Generic.Dictionary<string, string> _blood;

            /// <summary>Noita creatures bleed their own material (enemy_blood.json) as Noita liquids: a little per hit, a pool on death.</summary>
            static void Bleed(NPC npc, NoitaNpc n)
            {
                if (!Physics.Patches.On)
                    return;
                if (_blood == null)
                {
                    _blood = new System.Collections.Generic.Dictionary<string, string>();
                    foreach (var b in EnemyBlood.All)
                        _blood[b.Id] = b.Blood;
                }
                if (!_blood.TryGetValue(n.Def.Id, out var material) || material == "none")
                    return;
                bool dead = npc.life <= 0;
                int amount = dead ? 255 : 40;
                if (material == "lava")
                {
                    if (!dead)
                        return;
                    amount = 128;
                }
                Physics.Fluids.Add((int)(npc.Center.X / 16), (int)(npc.Center.Y / 16), material, amount);
            }

            static int BloodDust(string blood)
            {
                string b = (blood ?? "").ToLowerInvariant();
                if (b.Contains("acid") || b.Contains("slime") || b.Contains("radioactive") || b.Contains("toxic"))
                    return DustID.GreenBlood;
                if (b.Contains("lava") || b.Contains("fire") || b.Contains("spark"))
                    return DustID.Torch;
                if (b.Contains("oil") || b.Contains("none") || b.Contains("rock"))
                    return DustID.Smoke;
                return DustID.Blood;
            }
        }
    }
}
