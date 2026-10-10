using System;
using System.Collections.Generic;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>A Noita sprite XML: one sheet image and its named rectangle animations.</summary>
    public sealed class NoitaSprite
    {
        public string Image;
        public float OffsetX, OffsetY;
        public string DefaultAnimation;
        public readonly Dictionary<string, SpriteAnimation> Animations = new Dictionary<string, SpriteAnimation>(StringComparer.Ordinal);

        /// <summary>Parse a sprite XML, or wrap a plain .png path as a single-frame sprite.</summary>
        public static NoitaSprite Load(Func<string, string> readText, string path)
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return new NoitaSprite { Image = path };
            string text = readText(path) ?? throw new System.IO.FileNotFoundException("Not found: " + path, path);
            return Parse(text);
        }

        public static NoitaSprite Parse(string text)
        {
            var root = Nxml.ParseRoot(text);
            if (root == null || root.Name != "Sprite")
                throw new System.IO.InvalidDataException("Not a Noita sprite XML.");
            var s = new NoitaSprite
            {
                Image = root.Attr("filename"),
                OffsetX = root.Float("offset_x") ?? 0,
                OffsetY = root.Float("offset_y") ?? 0,
                DefaultAnimation = root.Attr("default_animation"),
            };
            foreach (var a in root.Children)
            {
                if (a.Name != "RectAnimation")
                    continue;
                var anim = new SpriteAnimation
                {
                    Name = a.Attr("name", ""),
                    PosX = a.Int("pos_x") ?? 0,
                    PosY = a.Int("pos_y") ?? 0,
                    FrameCount = Math.Max(1, a.Int("frame_count") ?? 1),
                    FrameWidth = a.Int("frame_width") ?? 0,
                    FrameHeight = a.Int("frame_height") ?? 0,
                    FrameWait = a.Float("frame_wait") ?? 0.1f,
                    Loop = a.Attr("loop", "1") != "0",
                    ShrinkByOnePixel = a.Attr("shrink_by_one_pixel", "0") == "1",
                    Next = a.Attr("next_animation"),
                };
                anim.FramesPerRow = Math.Max(1, a.Int("frames_per_row") ?? anim.FrameCount);
                if (anim.Name.Length > 0)
                    s.Animations[anim.Name] = anim;
            }
            if (s.DefaultAnimation == null && s.Animations.Count > 0)
                foreach (var k in s.Animations.Keys) { s.DefaultAnimation = k; break; }
            return s;
        }

        public SpriteAnimation Find(params string[] names)
        {
            foreach (var n in names)
                if (n != null && Animations.TryGetValue(n, out var a))
                    return a;
            if (DefaultAnimation != null && Animations.TryGetValue(DefaultAnimation, out var d))
                return d;
            foreach (var a in Animations.Values)
                return a;
            return null;
        }
    }

    public sealed class SpriteAnimation
    {
        public string Name;
        public int PosX, PosY, FrameCount, FrameWidth, FrameHeight, FramesPerRow;
        public float FrameWait;   // seconds per frame
        public bool Loop, ShrinkByOnePixel;
        public string Next;

        /// <summary>Source rectangle of a frame in the sheet image: x, y, w, h.</summary>
        public void FrameRect(int frame, out int x, out int y, out int w, out int h)
        {
            frame = Math.Max(0, Math.Min(FrameCount - 1, frame));
            // cells are frame_width x frame_height apart; shrink_by_one_pixel draws one pixel less on the right and bottom
            // (checked on Noita's miner_weak.png: 18 px cells, 7 per row in a 125 px sheet)
            x = PosX + (frame % FramesPerRow) * FrameWidth;
            y = PosY + (frame / FramesPerRow) * FrameHeight;
            w = FrameWidth - (ShrinkByOnePixel ? 1 : 0);
            h = FrameHeight - (ShrinkByOnePixel ? 1 : 0);
        }

        /// <summary>Frame index after a number of game ticks (60 per second).</summary>
        public int FrameAt(int ticks)
        {
            int perFrame = Math.Max(1, (int)Math.Round(FrameWait * 60f));
            int f = ticks / perFrame;
            return Loop ? f % FrameCount : Math.Min(f, FrameCount - 1);
        }
    }

    /// <summary>Noita's data/translations/common.csv: key in the first column, one column per language.</summary>
    public sealed class NoitaTranslations
    {
        readonly Dictionary<string, Dictionary<string, string>> _byLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Languages => _byLang.Keys;

        public static NoitaTranslations Parse(string csv)
        {
            var t = new NoitaTranslations();
            var rows = Csv(csv);
            if (rows.Count == 0)
                return t;
            var header = rows[0];
            for (int c = 1; c < header.Count; c++)
                if (header[c].Length > 0 && !t._byLang.ContainsKey(header[c]))
                    t._byLang[header[c]] = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || row[0].Length == 0)
                    continue;
                for (int c = 1; c < row.Count && c < header.Count; c++)
                    if (row[c].Length > 0 && t._byLang.TryGetValue(header[c], out var map))
                        map[row[0]] = row[c];
            }
            return t;
        }

        /// <summary>Look up "$animal_x" or "animal_x" in a language, falling back to English.</summary>
        public string Get(string key, string lang)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            key = key.TrimStart('$');
            if (lang != null && _byLang.TryGetValue(lang, out var m) && m.TryGetValue(key, out var v))
                return v;
            return _byLang.TryGetValue("en", out var en) && en.TryGetValue(key, out var e) ? e : null;
        }

        /// <summary>Keys ("animal_x", without $) whose English text is the given name, ignoring case.</summary>
        public IEnumerable<string> KeysWithEnglish(string english)
        {
            if (string.IsNullOrEmpty(english) || !_byLang.TryGetValue("en", out var en))
                yield break;
            foreach (var kv in en)
                if (string.Equals(kv.Value.Trim(), english.Trim(), StringComparison.OrdinalIgnoreCase))
                    yield return kv.Key;
        }

        static List<List<string>> Csv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
