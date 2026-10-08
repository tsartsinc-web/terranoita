using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Terranoita.Progress
{
    /// <summary>
    /// What one character has met, like Noita's Progress menu (author: kept per character). When something becomes
    /// known (author): a creature when killed, a liquid when touched, a wand or a spell when taken. Later items, perks.
    /// Each entry carries counters ("kills", "casts"...).
    /// Text file, one entry per line: category TAB id [TAB counter=value ...].
    /// </summary>
    public sealed class ProgressBook
    {
        public const string Spells = "spell", Creatures = "creature", Liquids = "liquid", Wands = "wand", Items = "item", Perks = "perk";

        sealed class Entry
        {
            public readonly Dictionary<string, long> Counters = new Dictionary<string, long>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Dictionary<string, Entry>> _cats = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);

        /// <summary>Changed since the last save (the game saves on world save, window close or every few minutes).</summary>
        public bool Dirty { get; private set; }

        /// <summary>Raised the first time something is met (the game can show "new spell" like Noita).</summary>
        public event Action<string, string> Discovered;

        Dictionary<string, Entry> Cat(string category)
        {
            if (!_cats.TryGetValue(category, out var c))
                _cats[category] = c = new Dictionary<string, Entry>(StringComparer.Ordinal);
            return c;
        }

        /// <summary>Mark as met; true the first time.</summary>
        public bool See(string category, string id)
        {
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(id) || Has(category, id))
                return false;
            Cat(category)[id] = new Entry();
            Dirty = true;
            Discovered?.Invoke(category, id);
            return true;
        }

        /// <summary>Add to a counter (it also marks the entry as met).</summary>
        public void Count(string category, string id, string counter, long add = 1)
        {
            See(category, id);
            if (!Cat(category).TryGetValue(id ?? "", out var e) || string.IsNullOrEmpty(counter))
                return;
            e.Counters[counter] = (e.Counters.TryGetValue(counter, out var v) ? v : 0) + add;
            Dirty = true;
        }

        public bool Has(string category, string id) =>
            id != null && _cats.TryGetValue(category, out var c) && c.ContainsKey(id);

        public long CountOf(string category, string id, string counter) =>
            id != null && _cats.TryGetValue(category, out var c) && c.TryGetValue(id, out var e) && e.Counters.TryGetValue(counter, out var v) ? v : 0;

        public IEnumerable<string> Known(string category) =>
            _cats.TryGetValue(category, out var c) ? c.Keys.ToList() : new List<string>();

        public int KnownCount(string category) => _cats.TryGetValue(category, out var c) ? c.Count : 0;

        /// <summary>
        /// One tab of the window: every id of the game's full list in its order (Noita's order: gun_actions.lua for spells,
        /// the creature list for creatures), known or not, plus anything known that the list no longer has (kept, shown last).
        /// </summary>
        public List<(string id, bool known)> Page(string category, IEnumerable<string> fullList)
        {
            var page = new List<(string, bool)>();
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in fullList ?? Enumerable.Empty<string>())
                if (id != null && listed.Add(id))
                    page.Add((id, Has(category, id)));
            foreach (var id in Known(category).OrderBy(x => x, StringComparer.Ordinal))
                if (!listed.Contains(id))
                    page.Add((id, true));
            return page;
        }

        // ---- text form ----

        public string Write()
        {
            var sb = new StringBuilder();
            sb.Append("# Terranoita progress v1\n");
            foreach (var cat in _cats.Keys.OrderBy(x => x, StringComparer.Ordinal))
                foreach (var kv in _cats[cat].OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append(Clean(cat)).Append('\t').Append(Clean(kv.Key));
                    foreach (var c in kv.Value.Counters.OrderBy(x => x.Key, StringComparer.Ordinal))
                        sb.Append('\t').Append(Clean(c.Key).Replace("=", "")).Append('=').Append(c.Value.ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
            return sb.ToString();
        }

        /// <summary>Reads the text form; bad lines are skipped (a hand-edited or cut file never loses the rest).</summary>
        public static ProgressBook Read(string text)
        {
            var b = new ProgressBook();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Length == 0 || raw[0] == '#')
                    continue;
                var parts = raw.Split('\t');
                if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
                    continue;
                var e = new Entry();
                for (int i = 2; i < parts.Length; i++)
                {
                    int eq = parts[i].IndexOf('=');
                    if (eq > 0 && long.TryParse(parts[i].Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                        e.Counters[parts[i].Substring(0, eq)] = v;
                }
                b.Cat(parts[0])[parts[1]] = e;
            }
            return b;
        }

        static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", " ");

        // ---- file ----

        /// <summary>The book of a character: next to its player file if it has one (unique per character), else by name.</summary>
        public static string FileFor(string baseDir, string playerFilePath, string playerName)
        {
            string key = !string.IsNullOrEmpty(playerFilePath) ? Path.GetFileNameWithoutExtension(playerFilePath) : playerName;
            // characters Windows refuses in file names (the game runs there), whatever system this runs on
            key = string.Concat((key ?? "player").Split(Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToArray()));
            return Path.Combine(baseDir, "progress_" + (key.Length > 0 ? key : "player") + ".txt");
        }

        public static ProgressBook Load(string path)
        {
            try
            {
                return File.Exists(path) ? Read(File.ReadAllText(path, Encoding.UTF8)) : new ProgressBook();
            }
            catch (IOException) { return new ProgressBook(); }
            catch (UnauthorizedAccessException) { return new ProgressBook(); }
        }

        /// <summary>Writes a temporary file and swaps it in, so a crash mid-save never leaves a cut file.</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Write(), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
            Dirty = false;
        }
    }
}
