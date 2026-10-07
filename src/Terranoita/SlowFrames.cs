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
        static long _lastLog;

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
