using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terranoita.Noita;

namespace Terranoita.Cli
{
    /// <summary>
    /// Developer tool run on the PC that has Noita installed. It reads (never writes) the player's data.wak.
    ///   tncli wak-list  &lt;noitaDir&gt; [substring]      list archive paths
    ///   tncli wak-cat   &lt;noitaDir&gt; &lt;path&gt;           print a text file from the archive
    ///   tncli entity    &lt;noitaDir&gt; &lt;id|path&gt;        facts the sheets need, for one enemy
    ///   tncli facts     &lt;noitaDir&gt; &lt;enemies.json&gt; &lt;out.json&gt;
    ///                   facts for every enemy row and the projectiles they fire, for tools/apply_facts.py
    ///   tncli shot-script &lt;noitaDir&gt; &lt;extra_entity.xml&gt; [frames] [projectile.xml]
    ///                   a fake shot moving right at 300 px/s with the file attached (LuaShotScripts), every 10 frames
    ///   tncli spells    &lt;noitaDir&gt; &lt;out.json&gt;
    ///                   every spell of gun_actions.lua, the projectiles they fire and the wand entities (stage 3)
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "tr-methods")   // tr-methods <Terraria.exe> <Type> [regex]
                return Terranoita.Cli.TerrariaMethods.Run(args[1], args[2], args.Length > 3 ? args[3] : null);
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: tncli wak-list|wak-cat|entity|facts|spells <noitaDir> ...");
                return 2;
            }
            try
            {
                using (var files = new NoitaFiles(args[1]))
                {
                    switch (args[0])
                    {
                        case "wak-list":
                            foreach (var e in files.Archive.Entries.OrderBy(e => e.Path, StringComparer.Ordinal))
                                if (args.Length < 3 || e.Path.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0)
                                    Console.WriteLine($"{e.Size,10}  {e.Path}");
                            Console.Error.WriteLine($"{files.Archive.Count} files in data.wak");
                            return 0;
                        case "wak-cat":
                            Console.Write(Text(files, args[2]) ?? throw new FileNotFoundException(args[2]));
                            return 0;
                        case "lua-golden":   // lua-golden <noita> [--check golden.txt]: one line per spell through gun.lua (spell, bolt, bomb), or the diff against the golden file
                        {
                            Func<string, string> read = p => Text(files, p);
                            if (args.Length > 3 && args[2] == "--check")
                            {
                                var diff = Terranoita.Noita.LuaGolden.Diff(File.ReadAllText(args[3]), Terranoita.Noita.LuaGolden.Lines(read));
                                foreach (var d in diff.Take(40)) Console.WriteLine(d);
                                Console.WriteLine(diff.Count == 0 ? "golden: no change" : "golden: " + diff.Count + " differences" + (diff.Count > 40 ? " (first 40 shown)" : ""));
                                return diff.Count == 0 ? 0 : 1;
                            }
                            foreach (var line in Terranoita.Noita.LuaGolden.Lines(read))
                                Console.WriteLine(line);
                            return 0;
                        }
                        case "lua-all":   // lua-all <noita>: every spell cast through gun.lua (with a bolt after it), errors and engine calls we lack
                        {
                            var probe = new Terranoita.Noita.LuaGun(p => Text(files, p));
                            var ids = probe.ActionIds();
                            var missing = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
                            int ok = 0, failed = 0;
                            foreach (var id in ids)
                            {
                                try
                                {
                                    var gun = new Terranoita.Noita.LuaGun(p => Text(files, p));
                                    var w = new Terranoita.Noita.LuaWand { RechargeTime = 30, CastDelay = 10, Capacity = 4 };
                                    w.Spells.Add((id, -1)); w.Spells.Add(("LIGHT_BULLET", -1)); w.Spells.Add(("BOMB", -1));
                                    gun.Load(w);
                                    for (int k = 0; k < 3; k++)
                                        foreach (var name in gun.Cast(1000).Missing)
                                        {
                                            if (!missing.TryGetValue(name, out var l)) missing[name] = l = new System.Collections.Generic.List<string>();
                                            if (!l.Contains(id)) l.Add(id);
                                        }
                                    ok++;
                                }
                                catch (Exception ex)
                                {
                                    failed++;
                                    Console.WriteLine("FAIL " + id + ": " + ex.Message.Split('\n')[0]);
                                }
                            }
                            Console.WriteLine("spells " + ids.Count + ": ran " + ok + ", failed " + failed);
                            foreach (var kv in missing)
                                Console.WriteLine("engine call " + kv.Key + " (" + kv.Value.Count + "): " + string.Join(" ", kv.Value.Take(12)));
                            return 0;
                        }
                        case "lua-potion":   // lua-potion <noita> [count] [script]: flasks filled by Noita's potion.lua, materials counted
                        {
                            int n = args.Length > 2 ? int.Parse(args[2]) : 200;
                            var maker = new Terranoita.Noita.LuaWandMaker(p => Text(files, p), 1);
                            var counts = new Dictionary<string, int>();
                            for (int k = 0; k < n; k++)
                            {
                                var (m, amount) = args.Length > 3 ? maker.MakePotion(13 * k, 200 + 37 * k, args[3]) : maker.MakePotion(13 * k, 200 + 37 * k);
                                counts[m + " " + amount] = (counts.TryGetValue(m + " " + amount, out int c) ? c : 0) + 1;
                            }
                            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
                                Console.WriteLine(kv.Value + " " + kv.Key);
                            return 0;
                        }
                        case "biome-spawns":   // biome-spawns <noita> <biome script.lua> [count] [function,function]: placements histogram (worldgen)
                        {
                            int n = args.Length > 3 ? int.Parse(args[3]) : 100;
                            var b = new Terranoita.Noita.NoitaBiomeSpawns(p => Text(files, p), 1);
                            b.Load(args[2]);
                            var fns = args.Length > 4 ? args[4].Split(',', StringSplitOptions.RemoveEmptyEntries)
                                : b.SpawnFunctions.Values.Concat(new[] { "spawn_wands", "spawn_potions", "spawn_items", "spawn_chest" })
                                   .Distinct().Where(b.Has).ToArray();
                            Console.WriteLine("spawn colours: " + string.Join(", ", b.SpawnFunctions.Select(kv => kv.Key.ToString("x8") + "=" + kv.Value)));
                            foreach (var fn in fns)
                            {
                                var counts = new Dictionary<string, int>();
                                for (int k = 0; k < n; k++)
                                    foreach (var p in b.Call(fn, 512 * (k % 10) + 13 * k, 1024 + 37 * k))
                                        counts[p.Kind + " " + p.File] = (counts.TryGetValue(p.Kind + " " + p.File, out int c) ? c : 0) + 1;
                                Console.WriteLine(fn + ": " + (counts.Count == 0 ? "nothing" : string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + "x " + kv.Key))));
                            }
                            if (b.Missing.Count > 0) Console.WriteLine("engine calls we lack: " + string.Join(", ", b.Missing));
                            foreach (var e in b.Errors.Take(10)) Console.WriteLine("error: " + e);
                            return b.Errors.Count > 0 ? 1 : 0;
                        }
                        case "pixel-scene":   // pixel-scene <noita> <materials.png>: the scene as Terraria tiles (one letter per material)
                        {
                            if (!files.TryRead(args[2], out var png)) throw new FileNotFoundException(args[2]);
                            var grid = Terranoita.Noita.PixelScene.Decode(Terranoita.Noita.NoitaPng.Read(png),
                                Terranoita.Noita.PixelScene.WangColors(Text(files, "data/materials.xml")));
                            var tiles = Terranoita.Noita.PixelScene.Downscale(grid);
                            var letters = new Dictionary<string, char>();
                            for (int y = 0; y < tiles.GetLength(1); y++)
                            {
                                var row = new System.Text.StringBuilder();
                                for (int x = 0; x < tiles.GetLength(0); x++)
                                {
                                    var m = tiles[x, y];
                                    if (m != null && !letters.ContainsKey(m)) letters[m] = (char)('a' + letters.Count % 26);
                                    row.Append(m == null ? '.' : letters[m]);
                                }
                                Console.WriteLine(row);
                            }
                            Console.WriteLine(grid.GetLength(0) + "x" + grid.GetLength(1) + " px -> " + tiles.GetLength(0) + "x" + tiles.GetLength(1) +
                                              " tiles; " + string.Join(", ", letters.Select(kv => kv.Value + "=" + kv.Key)));
                            return 0;
                        }
                        case "lua-wand":   // lua-wand <noita> <script.lua> [count]: wands made by Noita's own procedural script
                        {
                            int n = args.Length > 3 ? int.Parse(args[3]) : 3;
                            for (int k = 0; k < n; k++)
                            {
                                var maker = new Terranoita.Noita.LuaWandMaker(p => Text(files, p), k + 1);
                                Terranoita.Noita.MadeWand w;
                                try { w = args[2].EndsWith(".xml") ? maker.MakeEntity(args[2], 100 * k, 200 + 37 * k) : maker.Make(args[2], 100 * k, 200 + 37 * k); }
                                catch (MoonSharp.Interpreter.InterpreterException ex) { Console.WriteLine("lua error: " + ex.DecoratedMessage); return 1; }
                                Console.WriteLine("'" + w.Name + "' " + w.Sprite + " | casts " + w.SpellsPerCast + " shuffle " + w.Shuffle + " delay " + w.CastDelay +
                                                  " recharge " + w.RechargeTime + " mana " + w.ManaMax + "/" + w.ManaChargeSpeed + " capacity " + w.Capacity +
                                                  " spread " + w.Spread + " speed " + w.SpeedMultiplier + " | spells " + string.Join(" ", w.Spells) +
                                                  (w.AlwaysCast.Count > 0 ? " | always " + string.Join(" ", w.AlwaysCast) : "") +
                                                  (w.Missing.Count > 0 ? " | engine calls we lack: " + string.Join(", ", w.Missing) : ""));
                                if (Environment.GetEnvironmentVariable("TN_RAW") == "1") Console.WriteLine("   raw: " + string.Join("; ", w.Raw.Select(kv => kv.Key + "=" + kv.Value)));
                            }
                            return 0;
                        }
                        case "lua-cast":   // lua-cast <noita> <SPELL,SPELL,...> [casts] [always,cast]: Noita's own gun.lua shooting a wand
                        {
                            var gun = new Terranoita.Noita.LuaGun(p => Text(files, p));
                            var w = new Terranoita.Noita.LuaWand { SpellsPerCast = 1, RechargeTime = 30, CastDelay = 10, Capacity = 26 };
                            foreach (var id in args[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
                                w.Spells.Add((id, -1));
                            if (args.Length > 4)
                                w.AlwaysCast.AddRange(args[4].Split(',', StringSplitOptions.RemoveEmptyEntries));
                            gun.Load(w);
                            int casts = args.Length > 3 ? int.Parse(args[3]) : 3;
                            float mana = 1000;
                            for (int k = 0; k < casts; k++)
                            {
                                var c = gun.Cast(mana);
                                mana = c.Mana;
                                Console.WriteLine("cast " + (k + 1) + ": played " + string.Join(" ", c.Played) + " | delay " + c.CastDelay + " recharge " + c.Recharge + " mana " + c.Mana + " recoil " + c.Recoil +
                                                  (c.Missing.Count > 0 ? " | engine calls we lack: " + string.Join(", ", c.Missing) : ""));
                                void Dump(System.Collections.Generic.List<Terranoita.Noita.LuaShot> shots, string ind)
                                {
                                    foreach (var s in shots)
                                    {
                                        Console.WriteLine(ind + s.File + (s.Trigger != null ? " [" + s.Trigger + " " + s.TriggerFrames + "]" : "") +
                                                          " dmg+" + s.Get("damage_projectile_add") + " speed*" + s.Get("speed_multiplier") + " spread " + s.Get("spread_degrees") +
                                                          (s.Text("extra_entities") != "" ? " extra " + s.Text("extra_entities") : ""));
                                        Dump(s.Payload, ind + "    ");
                                    }
                                }
                                Dump(c.Shots, "  ");
                            }
                            return 0;
                        }
                        case "wak-get":   // wak-get <noita> <path> <out file>: a file as it is (images)
                            if (!files.TryRead(args[2], out var bytes)) throw new FileNotFoundException(args[2]);
                            File.WriteAllBytes(args[3], bytes);
                            return 0;
                        case "entity":
                        {
                            string path = args[2].Contains('/') ? args[2]
                                : new EntityLookup(files.Archive.Entries.Select(x => x.Path), p => Text(files, p), null).Find(args[2], null, null, null).Path
                                  ?? throw new FileNotFoundException("no entity file for " + args[2]);
                            var facts = EnemyJson(files, path);
                            Console.WriteLine(facts.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true }));
                            return 0;
                        }
                        case "facts":
                            return Facts(files, args[2], args[3]);
                        case "spells":
                            return SpellFacts(files, args[2]);
                        case "shot-script":   // shot-script <noita> <extra_entity.xml> [frames] [projectile.xml]: Noita's shot scripts on a fake shot
                            return ShotScript(files, args[2], args.Length > 3 ? int.Parse(args[3]) : 120, args.Length > 4 ? args[4] : null);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
            Console.Error.WriteLine("unknown command " + args[0]);
            return 2;
        }

        static string Text(NoitaFiles files, string path) => files.TryReadText(path, out var t) ? t : null;

        static JsonObject EnemyJson(NoitaFiles files, string path)
        {
            var e = NoitaEntity.Load(p => Text(files, p), path);
            var f = EnemyFacts.From(e);
            var dump = EntityDump.Of(e);
            var ranged = RangedJson(files, f.Ranged);
            var mult = new JsonObject();
            foreach (var kv in f.DamageMultipliers)
                mult[kv.Key] = kv.Value;
            string spriteImage = null;
            var animations = new JsonArray();
            if (f.Sprite != null)
            {
                try
                {
                    var sprite = NoitaSprite.Load(p => Text(files, p), f.Sprite);
                    spriteImage = sprite.Image;
                    foreach (var name in sprite.Animations.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        animations.Add(name);
                }
                catch (Exception) { }
            }
            return new JsonObject
            {
                ["entity"] = f.Entity,
                ["name_key"] = f.NameKey,
                ["display_hp"] = f.DisplayHp,
                ["sprite"] = f.Sprite,
                ["sprite_image"] = spriteImage,
                ["sprite_image_exists"] = spriteImage != null && files.TryRead(spriteImage, out _),
                ["sprite_animations"] = animations,
                ["hitbox_noita_px"] = f.HitboxNoitaPx == null ? null : new JsonArray(f.HitboxNoitaPx[0], f.HitboxNoitaPx[1]),
                ["melee_frames_between"] = f.MeleeFramesBetween,
                ["melee_max_distance_px"] = f.MeleeRange,
                ["dash"] = !f.DashEnabled ? null : new JsonObject
                {
                    ["frames_between"] = f.DashFramesBetween,
                    ["distance_px"] = f.DashDistance,
                    ["speed"] = f.DashSpeed,
                    ["damage"] = f.DashDamage,
                },
                ["movement"] = f.Movement == null ? null : new JsonObject
                {
                    ["can_walk"] = f.Movement.CanWalk,
                    ["can_fly"] = f.Movement.CanFly,
                    ["can_jump"] = f.Movement.CanJump,
                    ["run_velocity"] = f.Movement.RunVelocity,
                    ["fly_velocity_x"] = f.Movement.FlyVelocityX,
                    ["fly_speed_max_up"] = f.Movement.FlySpeedMaxUp,
                    ["accel_x"] = f.Movement.AccelX,
                    ["pixel_gravity"] = f.Movement.PixelGravity,
                    ["jump_speed"] = f.Movement.JumpSpeed,
                    ["detection_range_px"] = f.Movement.DetectionRange,
                },
                ["audio_roots"] = new JsonArray(f.AudioRoots.Select(a => (JsonNode)a).ToArray()),
                ["damage_multipliers"] = mult,
                ["ranged"] = ranged,
                ["ranged_disabled"] = RangedJson(files, f.RangedDisabled),
                ["components"] = DumpJson(dump),
                ["scripts"] = new JsonArray(EntityDump.Scripts(dump).Select(x => (JsonNode)x).ToArray()),
                ["script_entities"] = ScriptEntities(files, EntityDump.Scripts(dump)),
                ["script_projectiles"] = ScriptProjectiles(files, EntityDump.Scripts(dump)),
            };
        }

        static JsonArray RangedJson(NoitaFiles files, List<RangedAttackFacts> list)
        {
            var ranged = new JsonArray();
            foreach (var r in list)
            {
                var o = new JsonObject
                {
                    ["source"] = r.Source,
                    ["entity_file"] = r.EntityFile,
                    ["frames_between"] = r.FramesBetween,
                    ["min_distance_px"] = r.MinDistance,
                    ["max_distance_px"] = r.MaxDistance,
                    ["count_min"] = r.CountMin,
                    ["count_max"] = r.CountMax,
                    ["state_frames"] = r.StateFrames,
                };
                if (r.EntityFile != null && files.TryReadText(r.EntityFile, out _))
                {
                    try
                    {
                        o["projectile"] = ProjectileJson(files, r.EntityFile);
                    }
                    catch (Exception ex)
                    {
                        o["projectile_error"] = ex.Message;
                    }
                }
                ranged.Add(o);
            }
            return ranged;
        }

        /// <summary>Entity files each script names (what a nest releases, what a creature summons), not the script itself.</summary>
        static JsonObject ScriptEntities(NoitaFiles files, IEnumerable<string> scripts)
        {
            var o = new JsonObject();
            foreach (var script in scripts)
            {
                var text = Text(files, script);
                o[script] = text == null ? null
                    : new JsonArray(EntityDump.EntityFilesIn(text).Select(x => (JsonNode)x).ToArray());
            }
            return o;
        }

        static JsonObject ProjectileJson(NoitaFiles files, string file)
        {
            var p = ProjectileFacts.From(NoitaEntity.Load(x => Text(files, x), file));
            return new JsonObject
            {
                ["sprite"] = p.Sprite,
                ["speed_min"] = p.SpeedMin,
                ["speed_max"] = p.SpeedMax,
                ["gravity_y"] = p.GravityY,
                ["lifetime_frames"] = p.LifetimeFrames,
                ["explosion_radius_px"] = p.ExplosionRadius,
                ["damage"] = p.Damage,
                ["audio_root"] = p.AudioRoot,
                ["explosion_sound"] = p.ExplosionSound,
                ["components"] = DumpJson(EntityDump.Of(NoitaEntity.Load(x => Text(files, x), file))),
            };
        }

        /// <summary>Projectile facts of the projectile/explosion files a creature's scripts load (death explosions, script attacks).</summary>
        static JsonObject ScriptProjectiles(NoitaFiles files, IEnumerable<string> scripts)
        {
            var o = new JsonObject();
            foreach (var script in scripts)
            {
                var text = Text(files, script);
                if (text == null)
                    continue;
                foreach (var file in EntityDump.EntityFilesIn(text).Where(x => x.StartsWith("data/entities/projectiles/") && x.EndsWith(".xml")))
                {
                    if (o.ContainsKey(file) || !files.TryReadText(file, out _))
                        continue;
                    try { o[file] = ProjectileJson(files, file); }
                    catch (Exception ex) { o[file] = new JsonObject { ["error"] = ex.Message }; }
                }
            }
            return o;
        }

        /// <summary>Components as JSON: {component, entity (child entities only), attrs, children}.</summary>
        static JsonArray DumpJson(List<EntityDump.Item> items)
        {
            var a = new JsonArray();
            foreach (var i in items)
            {
                var o = NodeJson(i.Component);
                if (i.Entity != null)
                    o["entity"] = i.Entity;
                a.Add(o);
            }
            return a;
        }

        static JsonObject NodeJson(NxmlNode n)
        {
            var attrs = new JsonObject();
            foreach (var kv in n.Attributes.OrderBy(k => k.Key, StringComparer.Ordinal))
                attrs[kv.Key] = kv.Value;
            var o = new JsonObject { ["component"] = n.Name, ["attrs"] = attrs };
            if (n.Children.Count > 0)
                o["children"] = new JsonArray(n.Children.Select(c => (JsonNode)NodeJson(c)).ToArray());
            return o;
        }

        static void CollectComponentNames(JsonNode node, HashSet<string> names)
        {
            if (node is JsonObject o)
            {
                if (o["component"] is JsonValue v && o["attrs"] != null)
                    names.Add((string)v);
                foreach (var kv in o)
                    CollectComponentNames(kv.Value, names);
            }
            else if (node is JsonArray arr)
                foreach (var x in arr)
                    CollectComponentNames(x, names);
        }

        static int Facts(NoitaFiles files, string enemiesSheet, string outPath)
        {
            var sheet = JsonNode.Parse(File.ReadAllText(enemiesSheet));
            var result = new JsonObject();
            int ok = 0, missing = 0, failed = 0;
            var translations = files.TryReadText("data/translations/common.csv", out var csv) ? NoitaTranslations.Parse(csv) : null;
            var lookup = new EntityLookup(files.Archive.Entries.Select(x => x.Path), p => Text(files, p), translations);
            foreach (var row in sheet["rows"].AsArray())
            {
                string id = (string)row["id"];
                var found = lookup.Find(id, (string)row["noita_entity"], (string)row["name_key"], (string)row["name_en"]);
                var candidates = new JsonArray(found.Candidates.Select(c => (JsonNode)c).ToArray());
                if (found.Path == null)
                {
                    result[id] = new JsonObject { ["error"] = "no entity file found for this id" };
                    missing++;
                    continue;
                }
                try
                {
                    var o = EnemyJson(files, found.Path);
                    o["found_by"] = found.How;
                    o["candidates"] = candidates;
                    result[id] = o;
                    ok++;
                }
                catch (Exception ex)
                {
                    result[id] = new JsonObject { ["entity"] = found.Path, ["error"] = ex.Message };
                    failed++;
                }
            }
            // what each component attribute means and defaults to, from Noita's own modding documentation
            var names = new HashSet<string>(StringComparer.Ordinal);
            CollectComponentNames(result, names);
            string docPath = Path.Combine(files.GameDir, "tools_modding", "component_documentation.txt");
            if (File.Exists(docPath))
            {
                var docs = new JsonObject();
                foreach (var kv in ComponentDocs.Split(File.ReadAllText(docPath)).Where(kv => names.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    docs[kv.Key] = kv.Value;
                result["_component_docs"] = docs;
            }
            else
                Console.Error.WriteLine("note: " + docPath + " not found; facts written without component documentation");
            File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine($"facts: {ok} read, {missing} without an entity file, {failed} failed -> {outPath}");
            return 0;
        }

        /// <summary>Stage 3 facts: spells (gun_actions.lua), their projectiles, wand entities; for tools/apply_spells.py.</summary>
        static int SpellFacts(NoitaFiles files, string outPath)
        {
            var translations = files.TryReadText("data/translations/common.csv", out var csv) ? NoitaTranslations.Parse(csv) : null;
            string En(string key) => key != null && key.StartsWith("$") ? translations?.Get(key.Substring(1), "en") : null;
            var lua = Text(files, GunActions.Path) ?? throw new FileNotFoundException(GunActions.Path);
            // gun.lua's constants (ACTION_DRAW_RELOAD_TIME_INCREASE...) for the action functions that use them
            var actions = GunActions.Parse(lua, GunActions.Constants(Text(files, "data/scripts/gun/gun.lua")));
            var spells = new JsonObject();
            var projectileFiles = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var a in actions)
            {
                if (a.Id == null || spells.ContainsKey(a.Id))
                    continue;
                var fields = new JsonObject();
                foreach (var kv in a.Fields)
                    fields[kv.Key] = kv.Value;
                var o = new JsonObject
                {
                    ["name_key"] = a.Name, ["name_en"] = En(a.Name), ["description_key"] = a.Description, ["description_en"] = En(a.Description),
                    ["sprite"] = a.Sprite, ["type"] = a.Type,
                    ["spawn_level"] = a.SpawnLevel == null ? null : new JsonArray(a.SpawnLevel.Select(x => (JsonNode)x).ToArray()),
                    ["spawn_probability"] = a.SpawnProbability == null ? null : new JsonArray(a.SpawnProbability.Select(x => (JsonNode)x).ToArray()),
                    ["price"] = a.Price, ["mana"] = a.Mana, ["max_uses"] = a.MaxUses,
                    ["related_projectiles"] = new JsonArray(a.RelatedProjectiles.Select(x => (JsonNode)x).ToArray()),
                    ["projectiles"] = new JsonArray(a.Projectiles.Select(x => (JsonNode)x).ToArray()),
                    ["triggers"] = new JsonArray(a.Triggers.Select(t => (JsonNode)new JsonObject { ["kind"] = t.Kind, ["file"] = t.File, ["draws"] = t.Draws, ["frames"] = t.Frames }).ToArray()),
                    ["draws"] = a.Draws,
                    ["config_add"] = Dict(a.ConfigAdd), ["config_mul"] = Dict(a.ConfigMul),
                    ["config_set"] = new JsonObject(a.ConfigSet.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value))),
                    ["reload_add"] = a.ReloadAdd,
                    ["shot_add"] = Dict(a.ShotAdd), ["shot_set"] = Dict(a.ShotSet),
                    ["clamps"] = new JsonArray(a.Clamps.Select(x => (JsonNode)x).ToArray()),
                    ["conditional"] = a.Conditional,
                    ["calls"] = new JsonArray(a.Calls.Select(x => (JsonNode)x).ToArray()),
                    ["unparsed"] = new JsonArray(a.Unparsed.Select(x => (JsonNode)x).ToArray()),
                    ["fields"] = fields,
                };
                spells[a.Id] = o;
                foreach (var f in a.RelatedProjectiles.Concat(a.Projectiles).Concat(a.Triggers.Select(t => t.File)))
                    projectileFiles.Add(f);
            }
            var projectiles = new JsonObject();
            foreach (var f in projectileFiles)
            {
                try { projectiles[f] = ProjectileJson(files, f); }
                catch (Exception ex) { projectiles[f] = new JsonObject { ["error"] = ex.Message }; }
            }
            // wands: item entities with an AbilityComponent that has a gun_config (fixed and template wands)
            var wands = new JsonObject();
            foreach (var path in files.Archive.Entries.Select(e => e.Path).Where(p => p.StartsWith("data/entities/items/") && p.EndsWith(".xml")).OrderBy(p => p, StringComparer.Ordinal))
            {
                var text = Text(files, path);
                if (text == null || text.IndexOf("gun_config", StringComparison.Ordinal) < 0)
                    continue;
                try
                {
                    var e = NoitaEntity.Load(x => Text(files, x), path);
                    var ab = e.Component("AbilityComponent");
                    if (ab == null || ab.Child("gun_config") == null)
                        continue;
                    wands[path] = new JsonObject
                    {
                        ["ability"] = NodeJson(ab),
                        ["scripts"] = new JsonArray(e.ComponentsNamed("LuaComponent").SelectMany(l => l.Attributes.Where(kv => kv.Key.StartsWith("script_")).Select(kv => kv.Value)).Distinct().Select(x => (JsonNode)x).ToArray()),
                    };
                }
                catch (Exception ex) { wands[path] = new JsonObject { ["error"] = ex.Message }; }
            }
            var result = new JsonObject { ["spells"] = spells, ["projectiles"] = projectiles, ["wands"] = wands };
            File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            int unparsed = actions.Count(a => a.Unparsed.Count > 0);
            Console.WriteLine($"spells: {spells.Count} ({unparsed} need hand work), {projectiles.Count} projectile files, {wands.Count} wand entities -> {outPath}");
            return 0;
        }

        static JsonObject Dict(Dictionary<string, float> d) =>
            new JsonObject(d.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value)));

        /// <summary>A shot the CLI owns: flies by its velocity, never hits anything.</summary>
        sealed class CliShotHost : ShotHostBase
        {
            public int Frame;
            public readonly Dictionary<int, float[]> Shots = new Dictionary<int, float[]>();
            public readonly List<string> Events = new List<string>();
            public override int FrameNum => Frame;
            // entity 1 = the caster, standing at 0,0 (scripts reach it through ProjectileComponent.mWhoShot)
            public override bool GetPosition(int e, out float x, out float y) { x = y = 0; if (e == 1) return true; if (!Shots.TryGetValue(e, out var s)) return false; x = s[0]; y = s[1]; return true; }
            public override string GetField(int e, string component, string field) =>
                Shots.ContainsKey(e) && component == "ProjectileComponent" && (field == "mWhoShot" || field == "mShooterHerdId") ? "1" : null;
            public override bool SetField(int e, string component, string field, string value)
            {
                if (component == "ProjectileComponent")
                    Events.Add($"frame {Frame}: {component}.{field} = {value}");
                return false;
            }
            public override void SetPosition(int e, float x, float y) { if (Shots.TryGetValue(e, out var s)) { s[0] = x; s[1] = y; } }
            public override bool GetVelocity(int e, out float vx, out float vy) { vx = vy = 0; if (!Shots.TryGetValue(e, out var s)) return false; vx = s[2]; vy = s[3]; return true; }
            public override void SetVelocity(int e, float vx, float vy) { if (Shots.TryGetValue(e, out var s)) { s[2] = vx; s[3] = vy; } }
            public override void Kill(int e) { Events.Add($"frame {Frame}: shot killed"); Shots.Remove(e); }
            public override int Load(string file, float x, float y) { Events.Add($"frame {Frame}: EntityLoad {file} at {x:0.#},{y:0.#}"); return 0; }
            public override void Screenshake(float x, float y, float strength) => Events.Add($"frame {Frame}: screenshake {strength:0.#}");
        }

        static int ShotScript(NoitaFiles files, string xml, int frames, string projectile)
        {
            string docPath = Path.Combine(files.GameDir, "tools_modding", "component_documentation.txt");
            var types = File.Exists(docPath) ? ComponentFieldTypes.Parse(File.ReadAllText(docPath)) : null;
            if (types == null)
                Console.WriteLine("note: no component_documentation.txt, values are typed by their text");
            projectile = projectile ?? "data/entities/projectiles/deck/light_bullet.xml";
            if (Text(files, projectile) == null)
                projectile = null;
            var host = new CliShotHost();
            var lua = new LuaShotScripts(host, p => Text(files, p), types) { Log = m => Console.WriteLine("script error: " + m) };
            int shot = lua.CreateShot(projectile);
            host.Shots[shot] = new float[] { 0, 0, 300, 0 };
            int child = lua.AttachExtra(shot, xml);
            Console.WriteLine($"shot {shot} ({projectile ?? "no projectile file"}), {xml} -> entity {child}");
            Console.WriteLine("components: " + string.Join(", ", new[] { child }.Concat(lua.ChildrenOf(child))
                .SelectMany(e => lua.Components(e, null, false)).Select(c => c.Type + (c.Enabled ? "" : " (off)"))));
            for (int f = 1; f <= frames; f++)
            {
                host.Frame = f;
                foreach (var s in host.Shots.Values) { s[0] += s[2] / 60f; s[1] += s[3] / 60f; }
                lua.Update(f);
                if (f % 10 == 0 || !host.Shots.ContainsKey(shot))
                {
                    if (host.Shots.TryGetValue(shot, out var s))
                        Console.WriteLine($"frame {f,4}: pos {s[0],8:0.0} {s[1],8:0.0}  vel {s[2],8:0.0} {s[3],8:0.0}");
                    else
                    {
                        Console.WriteLine($"frame {f,4}: shot gone");
                        break;
                    }
                }
            }
            foreach (var e in host.Events) Console.WriteLine(e);
            Console.WriteLine("missing: " + (lua.Missing.Count == 0 ? "none" : string.Join(", ", lua.Missing)));
            Console.WriteLine("errors: " + (lua.Errors.Count == 0 ? "none" : string.Join(" | ", lua.Errors)));
            return lua.Errors.Count == 0 ? 0 : 1;
        }
    }
}
