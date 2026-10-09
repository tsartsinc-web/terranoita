using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terranoita.Game.Magic
{
    /// <summary>
    /// Tests only (design/magic_plan.md PC-23): records a cast the way the Noita probe mod records it in Noita
    /// (tools/noita_probe), in Noita's units, so the two can be compared field by field: every shot (file, the shot that
    /// released it, born/end frame after the cast, start position and velocity relative to the caster) and every hit on
    /// the test target (Noita damage units and Noita's damage message). Off unless a test turns it on.
    /// </summary>
    public static class SpellRecorder
    {
        sealed class Rec
        {
            public string File, Parent;
            public int Born;
            public int? End;
            public Vector2 P0, V0, P1;
            public readonly List<string> Path = new List<string>();   // every 5 frames: [age, x, y, vx, vy] (the probe's)
        }

        public static bool On;
        /// <summary>The file of the shot whose payload is being fired (SpellShots.Release), "" otherwise.</summary>
        public static string Parent = "";

        static readonly List<Rec> Shots = new List<Rec>();
        static readonly Dictionary<int, Rec> ById = new Dictionary<int, Rec>();
        static readonly List<(double damage, string message, string by)> Hits = new List<(double, string, string)>();
        static uint _castFrame;
        static Vector2 _origin;
        public static NPC Target;
        const float Px = Terranoita.Noita.Units.PixelScale;

        public static int Alive => Shots.Count(r => r.End == null);
        public static int Count => Shots.Count;

        public static void Begin(Vector2 casterCenter)
        {
            Shots.Clear();
            ById.Clear();
            Hits.Clear();
            Parent = "";
            _castFrame = Main.GameUpdateCount;
            _origin = casterCenter;
        }

        static int Frame => (int)(Main.GameUpdateCount - _castFrame);

        internal static void Born(int id, string file, Vector2 pos, Vector2 vel)
        {
            if (!On)
                return;
            var r = new Rec { File = file ?? "", Parent = Parent ?? "", Born = Frame, P0 = (pos - _origin) / Px, V0 = vel * 60f / Px, P1 = (pos - _origin) / Px };
            Shots.Add(r);
            ById[id] = r;
        }

        /// <summary>A live shot this frame: its flight path every 5 frames after it was born, as the probe samples it.</summary>
        internal static void Sample(int id, Vector2 pos, Vector2 vel)
        {
            if (!On || !ById.TryGetValue(id, out var r) || r.End != null)
                return;
            int age = Frame - r.Born;
            if (age % 5 != 0 || r.Path.Count >= 48)
                return;
            var p = (pos - _origin) / Px;
            var v = vel * 60f / Px;
            r.Path.Add("[" + age + "," + N(p.X) + "," + N(p.Y) + "," + N(v.X) + "," + N(v.Y) + "]");
        }

        internal static void Gone(int id, Vector2 pos)
        {
            if (On && ById.TryGetValue(id, out var r) && r.End == null)
            {
                r.End = Frame;
                r.P1 = (pos - _origin) / Px;
            }
        }

        /// <summary>A hit on the test target: damage in our hp (Noita x25) before Terraria's bonuses; message as Noita's
        /// ("$damage_projectile", "$damage_explosion"...).</summary>
        internal static void Hit(NPC n, string file, float hp, string message)
        {
            if (On && n != null && n == Target && hp > 0)
                Hits.Add((hp / 25.0, message, file ?? ""));
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string N(double? x) => x == null ? "null" : x.Value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The cast as one probe line (the same fields as probe_out.jsonl).</summary>
        public static string Line(string name, string[] deck, double manaUsed, int frames, string note)
        {
            var sb = new StringBuilder();
            sb.Append("{\"name\":").Append(Q(name)).Append(",\"deck\":[").Append(string.Join(",", deck.Select(Q))).Append("]")
              .Append(",\"mana_used\":").Append(N(manaUsed)).Append(",\"frames\":").Append(frames);
            if (!string.IsNullOrEmpty(note))
                sb.Append(",\"note\":").Append(Q(note));
            sb.Append(",\"projectiles\":[").Append(string.Join(",", Shots.Select(r =>
                "{\"file\":" + Q(r.File) + ",\"parent\":" + Q(r.Parent) + ",\"born\":" + r.Born + ",\"end\":" + (r.End?.ToString() ?? "null") +
                ",\"x0\":" + N(r.P0.X) + ",\"y0\":" + N(r.P0.Y) + ",\"vx0\":" + N(r.V0.X) + ",\"vy0\":" + N(r.V0.Y) +
                ",\"x1\":" + N(r.P1.X) + ",\"y1\":" + N(r.P1.Y) + ",\"path\":[" + string.Join(",", r.Path) + "]}"))).Append("]");
            sb.Append(",\"hits\":[").Append(string.Join(",", Hits.Select(h =>
                "{\"damage\":" + N(h.damage) + ",\"message\":" + Q(h.message) + ",\"by\":" + Q(h.by) + "}"))).Append("]}");
            return sb.ToString();
        }
    }
}
