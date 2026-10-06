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
                            string path = args[2].Contains('/') ? args[2] : FindEntity(files, args[2], null);
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

        /// <summary>The sheet's guessed path if it exists, otherwise any entity XML named &lt;id&gt;.xml.</summary>
        static string FindEntity(NoitaFiles files, string id, string guess)
        {
            if (guess != null && files.Archive.Contains(guess))
                return guess;
            var hits = files.Archive.Entries
                .Where(e => e.Path.StartsWith("data/entities/", StringComparison.OrdinalIgnoreCase) &&
                            e.Path.EndsWith("/" + id + ".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Path).OrderBy(p => p.Length).ToList();
            return hits.FirstOrDefault();
        }

        static JsonObject EnemyJson(NoitaFiles files, string path)
        {
            var e = NoitaEntity.Load(p => Text(files, p), path);
            var f = EnemyFacts.From(e);
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
                ["damage_multipliers"] = mult,
                ["ranged"] = ranged,
            };
        }

        static int Facts(NoitaFiles files, string enemiesSheet, string outPath)
        {
            var sheet = JsonNode.Parse(File.ReadAllText(enemiesSheet));
            var result = new JsonObject();
            int ok = 0, missing = 0, failed = 0;
            foreach (var row in sheet["rows"].AsArray())
            {
                string id = (string)row["id"];
                string guess = (string)row["noita_entity"];
                string path = FindEntity(files, id, guess);
                if (path == null)
                {
                    result[id] = new JsonObject { ["error"] = "no entity file found for this id" };
                    missing++;
                    continue;
                }
                try
                {
                    result[id] = EnemyJson(files, path);
                    ok++;
                }
                catch (Exception ex)
                {
                    result[id] = new JsonObject { ["entity"] = path, ["error"] = ex.Message };
                    failed++;
                }
            }
            File.WriteAllText(outPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"facts: {ok} read, {missing} without an entity file, {failed} failed -> {outPath}");
            return 0;
        }
    }
}
