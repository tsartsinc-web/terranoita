using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terranoita.Generated;
using Terraria;
using Terraria.ID;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// TERRANOITA_AUTOTEST_AUDIT=1 (with TERRANOITA_AUTOTEST=1): every Noita liquid and gas checked in one run, so the
    /// author does not have to try each. Builds the liquid gallery, then puts the player inside each box for a second
    /// and a quarter and logs one "AUDIT" line per material: the effects it gave, hp change, whether the player was
    /// moved (teleport), turned into something, or died; at the end, whether a material got out of its closed box
    /// or changed its walls. The player gets nothing else.
    /// </summary>
    public static class LiquidAudit
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("TERRANOITA_AUTOTEST_AUDIT") == "1";
        const int Start = 420, Each = 45, Settle = 38;   // author: twice as fast
        public static bool Done { get; private set; }
        static int _index = -1, _hp;
        static Vector2 _at;
        static readonly HashSet<string> Seen = new HashSet<string>();
        static readonly List<string> Forms = new List<string>();

        static int _startTick = -1, _lastTick = -1;

        public static void Frame(Player p, int worldFrame)
        {
            var boxes = LiquidGallery.Boxes;
            if (worldFrame < Start || Done || boxes.Count == 0)
                return;
            // count world updates, not screen frames: a paused world (window not active) must not run the clock
            if (_startTick < 0)
                _startTick = Fluids.Ticks;
            if (Fluids.Ticks == _lastTick)
                return;
            _lastTick = Fluids.Ticks;
            int frame = Start + Fluids.Ticks - _startTick;
            int step = (frame - Start) % Each;
            int i = (frame - Start) / Each;
            if (i >= boxes.Count)
            {
                Leaks();
                Done = true;
                Entry.Log("AUTOTEST: done (audit)");
                return;
            }
            var (left, top, name) = boxes[i];
            if (step == 0)
            {
                // a clean start inside the box
                Status.Clear();
                if (p.dead)
                    return;
                p.statLife = p.statLifeMax2;
                LiquidGallery.Fill(left, top, name);   // gases fade: fill the box just before going in
                _at = new Vector2((left + 1) * 16 + 8, (top + 1) * 16 + 2);
                p.Teleport(_at, -1);
                p.velocity = Vector2.Zero;
                _hp = p.statLife;
                Seen.Clear();
                Forms.Clear();
                _index = i;
            }
            else if (step < Settle && _index == i)
            {
                foreach (var s in Status.Active)
                    Seen.Add(s);
                if (Status.Form != null && !Forms.Contains(Status.Form))
                    Forms.Add(Status.Form);
            }
            else if (step == Settle && _index == i)
            {
                var def = Liquids.All.FirstOrDefault(l => l.Id == name);
                int lost = _hp - p.statLife;
                bool moved = Vector2.Distance(p.position, _at) > 6 * 16;
                Entry.Log("AUDIT " + name + " | " + (def?.Kind ?? "terraria") + " | effects " + (Seen.Count > 0 ? string.Join(",", Seen) : "-") +
                          " | hp " + (lost >= 0 ? "-" : "+") + Math.Abs(lost) + " in 0.6 s" +
                          (moved ? " | TELEPORTED" : "") + (Forms.Count > 0 ? " | form " + string.Join(",", Forms.Select(f => f.Split('/').Last())) : "") +
                          (p.dead ? " | DIED" : "") +
                          " | at " + ((int)(p.position.X / 16) - left) + "," + ((int)(p.position.Y / 16) - top) + " touching " + Fluids.UnderCount(p.Hitbox) +
                          (p.wet ? " wet" : "") + (p.frozen ? " frozen" : "") + " | in box " + Fluids.Total(left + 1, left + LiquidGallery.Inner, top + 1, top + LiquidGallery.InnerH, name));
                Status.Clear();
                if (p.dead)
                    p.respawnTimer = 2;   // back for the next box
            }
        }

        /// <summary>Did anything get out of its box, or change its walls?</summary>
        static void Leaks()
        {
            int bad = 0;
            foreach (var (left, top, name) in LiquidGallery.Boxes)
            {
                int walls = 0, missing = 0;
                for (int x = left; x <= left + LiquidGallery.Inner + 1; x++)
                    for (int y = top; y <= top + LiquidGallery.InnerH + 1; y++)
                    {
                        bool edge = x == left || x == left + LiquidGallery.Inner + 1 || y == top || y == top + LiquidGallery.InnerH + 1;
                        if (!edge)
                            continue;
                        walls++;
                        var t = Main.tile[x, y];
                        if (!t.active() || t.type != TileID.Obsidian)
                            missing++;
                    }
                int inside = Fluids.Total(left + 1, left + LiquidGallery.Inner, top + 1, top + LiquidGallery.InnerH, name);
                int outside = Fluids.Total(left - 8, left + LiquidGallery.Inner + 9, top - 8, top + LiquidGallery.InnerH + 9, name) - inside;
                if (missing > 0 || outside > 0)
                {
                    bad++;
                    Entry.Log("AUDIT LEAK " + name + ": walls changed " + missing + "/" + walls + ", outside the box " + outside);
                }
            }
            Entry.Log("AUDIT: " + LiquidGallery.Boxes.Count + " boxes, " + bad + " leaked or changed their walls; cells inside solid blocks: " + Fluids.InsideBlocks() +
                      "; names learned by touching: " + Fluids.KnownCount);
            Fluids.ForgetAll();
        }
    }
}
