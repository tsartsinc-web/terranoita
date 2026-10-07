using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Terranoita.Game.Magic
{
    /// <summary>A Noita wand the player has: its stats (as Noita's scripts made them) and what is in its slots.</summary>
    public sealed class WandData
    {
        public int Id;
        public string Name = "", Sprite = "";
        public int SpellsPerCast = 1;
        public bool Shuffle;
        public float CastDelay, RechargeTime, ManaMax, ManaChargeSpeed, Spread, SpeedMultiplier = 1;
        public List<string> AlwaysCast = new List<string>();
        public string[] Slots = new string[0];       // action ids, null = empty slot
        public int[] Uses = new int[0];              // uses left per slot (-1 = unlimited)

        public int Capacity => Slots.Length;
    }

    /// <summary>
    /// Wands and spell numbers, kept for every character and world on this PC (%LOCALAPPDATA%/Terranoita):
    /// a wand item carries only its wand number, a spell item only its spell number (MagicItems).
    /// </summary>
    public static class WandStore
    {
        static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terranoita");
        static string WandsFile => Path.Combine(Folder, "wands.txt");
        static string SpellsFile => Path.Combine(Folder, "spell_numbers.txt");

        static Dictionary<int, WandData> _wands;
        static List<string> _spells;
        static Dictionary<string, int> _spellNumber;

        // ---- spells: an action id gets a number the first time it is seen, kept for good ----

        public static int SpellNumber(string actionId)
        {
            LoadSpells();
            if (_spellNumber.TryGetValue(actionId, out int n))
                return n;
            n = _spells.Count;
            _spells.Add(actionId);
            _spellNumber[actionId] = n;
            try { File.AppendAllText(SpellsFile, actionId + "\n"); }
            catch (Exception ex) { Entry.Error("spell numbers", ex); }
            return n;
        }

        public static string SpellId(int number)
        {
            LoadSpells();
            return number >= 0 && number < _spells.Count ? _spells[number] : null;
        }

        static void LoadSpells()
        {
            if (_spells != null)
                return;
            _spells = new List<string>();
            _spellNumber = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(SpellsFile))
                    foreach (var line in File.ReadAllLines(SpellsFile))
                        if (line.Trim().Length > 0 && !_spellNumber.ContainsKey(line.Trim()))
                        {
                            _spellNumber[line.Trim()] = _spells.Count;
                            _spells.Add(line.Trim());
                        }
            }
            catch (Exception ex) { Entry.Error("spell numbers load", ex); }
        }

        // ---- wands ----

        public static WandData Wand(int id)
        {
            LoadWands();
            return _wands.TryGetValue(id, out var w) ? w : null;
        }

        public static WandData NewWand()
        {
            LoadWands();
            int id = 0;
            while (_wands.ContainsKey(id))
                id++;
            var w = new WandData { Id = id };
            _wands[id] = w;
            return w;
        }

        public static void Save()
        {
            if (_wands == null)
                return;
            try
            {
                Directory.CreateDirectory(Folder);
                var lines = _wands.Values.OrderBy(w => w.Id).Select(w => string.Join("|", new[]
                {
                    w.Id.ToString(CultureInfo.InvariantCulture), Clean(w.Name), Clean(w.Sprite), w.SpellsPerCast.ToString(CultureInfo.InvariantCulture),
                    w.Shuffle ? "1" : "0", F(w.CastDelay), F(w.RechargeTime), F(w.ManaMax), F(w.ManaChargeSpeed), F(w.Spread), F(w.SpeedMultiplier),
                    string.Join(",", w.AlwaysCast),
                    string.Join(",", w.Slots.Select((s, i) => s == null ? "-" : s + ":" + (i < w.Uses.Length ? w.Uses[i] : -1).ToString(CultureInfo.InvariantCulture))),
                }));
                File.WriteAllLines(WandsFile + ".tmp", lines);
                if (File.Exists(WandsFile))
                    File.Delete(WandsFile);
                File.Move(WandsFile + ".tmp", WandsFile);
            }
            catch (Exception ex) { Entry.Error("wands save", ex); }
        }

        static string Clean(string s) => (s ?? "").Replace("|", "/");
        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;

        static void LoadWands()
        {
            if (_wands != null)
                return;
            _wands = new Dictionary<int, WandData>();
            try
            {
                if (!File.Exists(WandsFile))
                    return;
                foreach (var line in File.ReadAllLines(WandsFile))
                {
                    var p = line.Split('|');
                    if (p.Length < 13)
                        continue;
                    var w = new WandData
                    {
                        Id = int.Parse(p[0], CultureInfo.InvariantCulture), Name = p[1], Sprite = p[2],
                        SpellsPerCast = (int)P(p[3]), Shuffle = p[4] == "1", CastDelay = P(p[5]), RechargeTime = P(p[6]),
                        ManaMax = P(p[7]), ManaChargeSpeed = P(p[8]), Spread = P(p[9]), SpeedMultiplier = P(p[10]),
                        AlwaysCast = p[11].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                    };
                    var slots = p[12].Length == 0 ? new string[0] : p[12].Split(',');
                    w.Slots = slots.Select(s => s == "-" ? null : s.Split(':')[0]).ToArray();
                    w.Uses = slots.Select(s => s == "-" || !s.Contains(":") ? -1 : (int)P(s.Split(':')[1])).ToArray();
                    _wands[w.Id] = w;
                }
            }
            catch (Exception ex) { Entry.Error("wands load", ex); }
        }
    }
}
