using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Where a slow update goes (author: "it lags when I throw a bomb"): the mod's parts are timed every update; one
    /// that took over 8 ms is logged with the share of each part ("SLOW update 23 ms: falling 15.2, fluids 6.1 ...",
    /// at most once a second). Chat lines go into the log too, so the author's notes ("лагает") line up with them.
    /// </summary>
    public static class SlowFrames
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly Dictionary<string, double> Parts = new Dictionary<string, double>(StringComparer.Ordinal);
        static long _lastLog, _lastFrameLog, _prevEnd;
        static int _gc1, _gc2;

        /// <summary>Times one part of this update.</summary>
        public static void Time(string part, Action a)
        {
            long t = Clock.ElapsedTicks;
            try { a(); }
            finally
            {
                double ms = (Clock.ElapsedTicks - t) * 1000.0 / Stopwatch.Frequency;
                Parts[part] = (Parts.TryGetValue(part, out double old) ? old : 0) + ms;
            }
        }

        /// <summary>End of an update: log it if the mod's parts took long.</summary>
        public static void EndUpdate()
        {
            // the whole frame (Terraria's update and draw too): a slow one is logged with what the world holds
            long now = Clock.ElapsedTicks;
            double frameMs = _prevEnd == 0 ? 0 : (now - _prevEnd) * 1000.0 / Stopwatch.Frequency;
            _prevEnd = now;
            int gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            string gc = gc2 != _gc2 ? ", full GC" : gc1 != _gc1 ? ", GC gen1" : "";
            _gc1 = gc1;
            _gc2 = gc2;
            if (frameMs > 40 && !Main.gameMenu && !Main.gamePaused && Clock.ElapsedMilliseconds - _lastFrameLog > 1000)
            {
                _lastFrameLog = Clock.ElapsedMilliseconds;
                Entry.Log("SLOW frame " + frameMs.ToString("0") + " ms (mod parts " + Parts.Values.Sum().ToString("0.0") + " ms): items " +
                    Main.item.Count(i => i != null && i.active) + ", projectiles " + Main.projectile.Count(x => x != null && x.active) +
                    ", npcs " + Main.npc.Count(n => n != null && n.active) + ", dust " + Main.dust.Count(d => d != null && d.active) +
                    ", gore " + Main.gore.Count(g => g != null && g.active) + ", liquids " + Physics.Fluids.Count +
                    ", spell shots " + Magic.SpellShots.Ids().Count + gc + ", memory " + (GC.GetTotalMemory(false) >> 20) + " MB");
            }
            if (Parts.Count == 0)
                return;
            double total = Parts.Values.Sum();
            if (total > 8 && Clock.ElapsedMilliseconds - _lastLog > 1000)
            {
                _lastLog = Clock.ElapsedMilliseconds;
                Entry.Log("SLOW update " + total.ToString("0.0") + " ms: " +
                    string.Join(", ", Parts.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value.ToString("0.0"))) +
                    "; liquids " + Physics.Fluids.Count + ", falling " + Physics.Falling.Active + ", burning " + Physics.Fire.Count +
                    ", spell shots " + Magic.SpellShots.Ids().Count + " (scripted " + Magic.SpellShots.ScriptedCount + ")");
            }
            Parts.Clear();
        }

        [Hook("chat_log")]
        [HarmonyPatch(typeof(Main), nameof(Main.NewText), new[] { typeof(string), typeof(Color) })]
        static class ChatPatch
        {
            static void Postfix(string newText)
            {
                if (!string.IsNullOrEmpty(newText))
                    Entry.Log("CHAT " + newText);
            }
        }
    }
}
