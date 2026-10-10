using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Noita
{
    /// <summary>
    /// Finds the entity XML of a sheet row whose id is not a Noita file name (ids seeded from the wiki, like
    /// nest_fly or trap_arrow). In order: the sheet's guessed path, a file named &lt;id&gt;.xml, an entity whose
    /// name is the row's name key, an entity whose name key translates to the row's English name, and last a file
    /// whose name contains every word of the id (flynest.xml for nest_fly). The other matches are kept as candidates.
    /// </summary>
    public sealed class EntityLookup
    {
        readonly List<string> _entityFiles;
        readonly Func<string, string> _readText;
        readonly NoitaTranslations _translations;
        Dictionary<string, List<string>> _byName;

        public EntityLookup(IEnumerable<string> archivePaths, Func<string, string> readText, NoitaTranslations translations)
        {
            _entityFiles = archivePaths
                .Where(p => p.StartsWith("data/entities/", StringComparison.OrdinalIgnoreCase) &&
                            p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal).ToList();
            _readText = readText;
            _translations = translations;
        }

        public sealed class Result
        {
            public string Path;
            /// <summary>How it was found: guess, file_name, name_key, english_name, file_words.</summary>
            public string How;
            public readonly List<string> Candidates = new List<string>();
        }

        public Result Find(string id, string guess, string nameKey, string englishName)
        {
            var r = new Result();
            if (guess != null && _entityFiles.Contains(guess))
                return Done(r, guess, "guess");
            var named = _entityFiles.Where(p => BaseName(p).Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count > 0)
                return Done(r, named[0], "file_name", named);

            var byKey = WithName(nameKey);
            if (byKey.Count > 0)
                return Done(r, Best(id, byKey), "name_key", byKey);

            if (_translations != null)
            {
                var byEnglish = _translations.KeysWithEnglish(englishName).SelectMany(k => WithName("$" + k)).Distinct().ToList();
                if (byEnglish.Count > 0)
                    return Done(r, Best(id, byEnglish), "english_name", byEnglish);
            }

            var words = Words(id);
            // creatures and buildings before the projectiles, props and particles they use (fire_trap.xml is the
            // fire trap's shot, firetrap_left.xml the trap)
            var byWords = words.Count == 0 ? new List<string>()
                : _entityFiles.Where(p => words.All(w => BaseName(p).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                              .OrderBy(FolderRank).ThenBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal).ToList();
            if (byWords.Count > 0)
                return Done(r, byWords[0], "file_words", byWords);
            return r;
        }

        static Result Done(Result r, string path, string how, List<string> all = null)
        {
            r.Path = path;
            r.How = how;
            if (all != null)
                r.Candidates.AddRange(all.Where(p => p != path).Take(12));
            return r;
        }

        /// <summary>Of several entities, the one whose file name shares most words with the id, then the shortest path.</summary>
        static string Best(string id, List<string> paths)
        {
            var words = Words(id);
            return paths.OrderByDescending(p => words.Count(w => BaseName(p).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ThenBy(p => p.StartsWith("data/entities/animals/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(p => p.Length).First();
        }

        List<string> WithName(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey))
                return new List<string>();
            if (_byName == null)
            {
                // root entity names, read once: only files that mention a $ key can have one
                _byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var p in _entityFiles)
                {
                    string text = _readText(p);
                    if (text == null || text.IndexOf("$", StringComparison.Ordinal) < 0)
                        continue;
                    string name;
                    try { name = Nxml.ParseRoot(text)?.Attr("name"); }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!_byName.TryGetValue(name, out var list))
                        _byName[name] = list = new List<string>();
                    list.Add(p);
                }
            }
            return _byName.TryGetValue(nameKey, out var hits) ? hits : new List<string>();
        }

        static int FolderRank(string p) =>
            p.StartsWith("data/entities/animals/", StringComparison.OrdinalIgnoreCase) ? 0 :
            p.StartsWith("data/entities/buildings/", StringComparison.OrdinalIgnoreCase) ? 1 :
            p.StartsWith("data/entities/projectiles/", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("data/entities/particles/", StringComparison.OrdinalIgnoreCase) ? 3 : 2;

        static string BaseName(string p)
        {
            int slash = p.LastIndexOf('/');
            string b = slash >= 0 ? p.Substring(slash + 1) : p;
            return b.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? b.Substring(0, b.Length - 4) : b;
        }

        static List<string> Words(string id) =>
            id.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToList();
    }
}
