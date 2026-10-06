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
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: tncli wak-list|wak-cat|entity|facts <noitaDir> ...");
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
                        case "entity":
                        {
                            string path = args[2].Contains('/') ? args[2]
                                : new EntityLookup(files.Archive.Entries.Select(x => x.Path), p => Text(files, p), null).Find(args[2], null, null, null).Path
                                  ?? throw new FileNotFoundException("no entity file for " + args[2]);
                            var facts = EnemyJson(files, path);
                            Console.WriteLine(facts.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                            return 0;
                        }
                        case "facts":
                            return Facts(files, args[2], args[3]);
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
            var ranged = new JsonArray();
            foreach (var r in f.Ranged)
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
                        var p = ProjectileFacts.From(NoitaEntity.Load(x => Text(files, x), r.EntityFile));
                        o["projectile"] = new JsonObject
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
                            ["components"] = DumpJson(EntityDump.Of(NoitaEntity.Load(x => Text(files, x), r.EntityFile))),
                        };
                    }
                    catch (Exception ex)
                    {
                        o["projectile_error"] = ex.Message;
                    }
                }
                ranged.Add(o);
            }
            var mult = new JsonObject();
            foreach (var kv in f.DamageMultipliers)
                mult[kv.Key] = kv.Value;
            string spriteImage = null;
            if (f.Sprite != null)
            {
                try { spriteImage = NoitaSprite.Load(p => Text(files, p), f.Sprite).Image; }
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
                ["components"] = DumpJson(dump),
                ["scripts"] = new JsonArray(EntityDump.Scripts(dump).Select(x => (JsonNode)x).ToArray()),
                ["script_entities"] = ScriptEntities(files, EntityDump.Scripts(dump)),
            };
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
            File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine($"facts: {ok} read, {missing} without an entity file, {failed} failed -> {outPath}");
            return 0;
        }
    }
}
