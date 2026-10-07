using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Noita;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Noita's own per-projectile Lua scripts (the LuaComponents of a shot's projectile file and of its modifiers'
    /// extra_entities) run on Core's LuaShotScripts; this is the game side it asks: shots, the caster and creatures
    /// as Noita entities. Noita pixels = Terraria pixels / 3, Noita velocity in pixels per second.
    /// Entities: 1 = the caster, 1000 + whoAmI = creatures, LuaShotScripts.FirstEntity and up = shots and effects.
    /// </summary>
    public static partial class SpellShots
    {
        const int CasterEntity = 1, NpcBase = 1000;
        static LuaShotScripts _scripts;
        static GameShotHost _host;
        static readonly Dictionary<int, Shot> ByScript = new Dictionary<int, Shot>();
        static readonly Dictionary<string, bool> HasLua = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        static LuaShotScripts Scripts
        {
            get
            {
                if (_scripts == null && NoitaArt.Ready)
                {
                    ComponentFieldTypes types = null;
                    try
                    {
                        string doc = NoitaArt.GameDir == null ? null : Path.Combine(NoitaArt.GameDir, "tools_modding", "component_documentation.txt");
                        if (doc != null && File.Exists(doc))
                            types = ComponentFieldTypes.Parse(File.ReadAllText(doc));
                        else
                            Entry.Log("shot scripts: no component_documentation.txt, field types guessed");
                    }
                    catch (Exception ex) { Entry.Error("component docs", ex); }
                    _host = new GameShotHost();
                    _scripts = new LuaShotScripts(_host, NoitaArt.ReadText, types) { Log = m => Entry.Log("shot script: " + m) };
                }
                return _scripts;
            }
        }

        /// <summary>Does a Noita entity file (or a file of its Base chain) carry a Lua script? Shots without any skip the store.</summary>
        static bool CarriesLua(string file, int depth = 0)
        {
            if (string.IsNullOrEmpty(file) || depth > 6)
                return false;
            if (HasLua.TryGetValue(file, out bool b))
                return b;
            string xml = NoitaArt.ReadText(file) ?? "";
            b = xml.Contains("LuaComponent");
            if (!b)
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xml, "<Base\\s+file=\"([^\"]+)\""))
                    b |= CarriesLua(m.Groups[1].Value, depth + 1);
            HasLua[file] = b;
            return b;
        }

        /// <summary>A new shot enters the script store when its file or one of its extra entities has a script.</summary>
        static void ScriptsAdd(Shot s, bool always)
        {
            var extras = s.Lua.Text("extra_entities").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            if (!always && !CarriesLua(s.Lua.File) && !extras.Any(x => CarriesLua(x)))
                return;
            var sc = Scripts;
            if (sc == null)
                return;
            try
            {
                s.Script = sc.CreateShot(s.Lua.File);
                ByScript[s.Script] = s;
                foreach (var x in extras.Where(x => CarriesLua(x)))
                    sc.AttachExtra(s.Script, x);
            }
            catch (Exception ex)
            {
                if (NotYet.Add("scripts:" + s.Lua.File))
                    Entry.Error("shot scripts " + s.Lua.File, ex);
            }
        }

        /// <summary>The shot is gone: Noita's death scripts, then it leaves the store.</summary>
        static void ScriptsRemove(Shot s)
        {
            if (s.Script == 0 || _scripts == null)
                return;
            try
            {
                _scripts.Fire(s.Script, "script_death", 0, "", 0, false);
                _scripts.Forget(s.Script);
            }
            catch (Exception ex) { Entry.Error("shot death script", ex); }
            ByScript.Remove(s.Script);
            s.Script = 0;
        }

        static void ScriptsUpdate()
        {
            if (_scripts == null)
                return;
            try { _scripts.Update((int)Main.GameUpdateCount); }
            catch (Exception ex) { Entry.Error("shot scripts", ex); }
            foreach (var m in _scripts.Missing)
                if (NotYet.Add("api:" + m))
                    Entry.Log("shot scripts call a Noita function we lack: " + m);
        }

        static void ScriptsClear()
        {
            _scripts = null;
            ByScript.Clear();
        }

        /// <summary>Tests: shots that run Noita's scripts now.</summary>
        public static int ScriptedCount => ByScript.Count;

        static Shot ShotOf(int entity) => ByScript.TryGetValue(entity, out var s) ? s : null;
        static NPC NpcOf(int entity)
        {
            int i = entity - NpcBase;
            return i >= 0 && i < Main.maxNPCs && Main.npc[i].active ? Main.npc[i] : null;
        }

        /// <summary>The game's answers to Noita's shot scripts.</summary>
        sealed class GameShotHost : ShotHostBase
        {
            static Player Caster => Main.LocalPlayer;

            public override int FrameNum => (int)Main.GameUpdateCount;

            public override bool GetPosition(int entity, out float x, out float y)
            {
                var at = Where(entity);
                x = at?.X / Px ?? 0;
                y = at?.Y / Px ?? 0;
                return at != null;
            }

            static Vector2? Where(int entity)
            {
                if (entity == CasterEntity)
                    return Caster?.Center;
                var n = NpcOf(entity);
                if (n != null)
                    return n.Center;
                return ShotOf(entity)?.Pos;
            }

            public override void SetPosition(int entity, float x, float y)
            {
                var to = new Vector2(x, y) * Px;
                var s = ShotOf(entity);
                if (s != null)
                    s.Pos = to;
                else if (NpcOf(entity) is NPC n)
                    n.Center = to;
            }

            public override bool GetVelocity(int entity, out float vx, out float vy)
            {
                Vector2? v = ShotOf(entity)?.Vel ?? NpcOf(entity)?.velocity ?? (entity == CasterEntity ? Caster?.velocity : null);
                vx = (v?.X ?? 0) * 60f / Px;
                vy = (v?.Y ?? 0) * 60f / Px;
                return v != null;
            }

            public override void SetVelocity(int entity, float vx, float vy)
            {
                var v = new Vector2(vx, vy) * Px / 60f;
                var s = ShotOf(entity);
                if (s != null)
                    s.Vel = v;
                else if (NpcOf(entity) is NPC n)
                    n.velocity = v;
                else if (entity == CasterEntity && Caster != null)
                    Caster.velocity = v;
            }

            public override string GetField(int entity, string component, string field)
            {
                var s = ShotOf(entity);
                if (s == null || component != "ProjectileComponent")
                    return null;
                switch (field)
                {
                    case "mWhoShot": case "mShooterHerdId": return CasterEntity.ToString();
                    case "lifetime": return s.Life.ToString();
                    case "mStartingLifetime": return s.StartLife.ToString();
                    case "damage": return (s.Damage / 25f).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case "bounces_left": return s.Bounces.ToString();
                }
                return null;
            }

            public override bool SetField(int entity, string component, string field, string value)
            {
                var s = ShotOf(entity);
                if (s == null || component != "ProjectileComponent" ||
                    !float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    return false;
                switch (field)
                {
                    case "lifetime": s.Life = Math.Max(1, (int)f); return true;
                    case "damage": s.Damage = f * 25f; return true;
                    case "bounces_left": s.Bounces = (int)f; return true;
                }
                return false;
            }

            public override void Kill(int entity)
            {
                var s = ShotOf(entity);
                if (s != null)
                    s.Killed = true;
            }

            public override IEnumerable<int> InRadiusWithTag(float x, float y, float radius, string tag)
            {
                var at = new Vector2(x, y) * Px;
                float r = radius >= float.MaxValue / 4 ? float.MaxValue : radius * Px;
                bool Near(Vector2 p) => r == float.MaxValue || Vector2.Distance(p, at) <= r;
                switch (tag)
                {
                    case "player_unit": case "player": case "wand":
                        if (Caster != null && Caster.active && !Caster.dead && Near(Caster.Center))
                            yield return CasterEntity;
                        break;
                    case "projectile": case "projectile_player":
                        foreach (var s in ByScript.Values)   // read at once into a list by Core, no script runs meanwhile
                            if (Near(s.Pos))
                                yield return s.Script;
                        break;
                    case "enemy": case "mortal": case "hittable": case "prey": case "homing_target": case "monster": case "touchmagic_immunity":
                        for (int i = 0; i < Main.maxNPCs; i++)
                        {
                            var n = Main.npc[i];
                            if (n.active && !n.friendly && !n.townNPC && n.life > 0 && Near(n.Center))
                                yield return NpcBase + i;
                        }
                        break;
                }
            }

            public override bool HitboxCenter(int entity, out float x, out float y) => GetPosition(entity, out x, out y);

            public override bool Raytrace(float x1, float y1, float x2, float y2, out float hitX, out float hitY)
            {
                var a = new Vector2(x1, y1) * Px;
                var b = new Vector2(x2, y2) * Px;
                float len = Vector2.Distance(a, b);
                for (float k = 0; k <= len; k += 4)
                {
                    var p = Vector2.Lerp(a, b, len < 1 ? 1 : k / len);
                    int tx = (int)(p.X / 16), ty = (int)(p.Y / 16);
                    if (Physics.Mats.InWorld(tx, ty) && Physics.Mats.Solid(tx, ty))
                    {
                        hitX = p.X / Px;
                        hitY = p.Y / Px;
                        return true;
                    }
                }
                hitX = x2;
                hitY = y2;
                return false;
            }

            public override int Load(string file, float x, float y) => LoadEntity(file, new Vector2(x, y) * Px, Caster);

            public override void Shoot(int shooter, int entity, float x, float y, float tx, float ty)
            {
                var s = ShotOf(entity);
                if (s == null)
                    return;
                s.Pos = new Vector2(x, y) * Px;
                var dir = new Vector2(tx - x, ty - y);
                float speed = Math.Max(s.Vel.Length(), (s.Def.SpeedMin + s.Def.SpeedMax) / 2f * Px / 60f);
                s.Vel = dir.LengthSquared() > 0.0001f ? Vector2.Normalize(dir) * speed : s.Vel;
            }

            public override int HerdRelation(int a, int b) =>
                (NpcOf(a) != null) == (NpcOf(b) != null) ? 100 : 0;   // the caster and its shots are one herd, creatures the other

            public override void CameraPos(out float x, out float y)
            {
                x = (Main.screenPosition.X + Main.screenWidth / 2f) / Px;
                y = (Main.screenPosition.Y + Main.screenHeight / 2f) / Px;
            }

            public override float SkyVisibility(float x, float y)
            {
                int tx = (int)(x * Px / 16), ty = (int)(y * Px / 16);
                if (!Physics.Mats.InWorld(tx, ty) || ty > Main.worldSurface)
                    return 0;
                for (int k = ty - 1; k > Math.Max(1, ty - 60); k--)
                    if (Physics.Mats.Solid(tx, k))
                        return 0;
                return 1;
            }

            public override void PlaySound(string bank, string evt, float x, float y) =>
                NoitaSound.Play(evt, new Vector2(x, y) * Px);
        }
    }
}
