using System;
using System.Collections.Generic;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class ShotFlightTests
    {
        // Noita's documented defaults (tools_modding/component_documentation.txt, VelocityComponent)
        const string Docs = @"VelocityComponent
 - Members -----------------------------
    float                   gravity_x                                                       0 [0, 1]                        """"
    float                   gravity_y                                                       400 [0, 1]                      """"
    float                   air_friction                                                    0.55 [0, 1]                     """"
    float                   terminal_velocity                                               1000 [0, 1]                     """"
    bool                    apply_terminal_velocity                                         1 [0, 1]                        """"
";

        static ShotFlight Of(params (string field, string value)[] set)
        {
            var fields = new Dictionary<string, string>();
            foreach (var (f, v) in set)
                fields[f] = v;
            return ShotFlight.From(f => fields.TryGetValue(f, out var v) ? v : null, ComponentFieldTypes.Parse(Docs));
        }

        static (float vx, float vy) Fly(ShotFlight c, float vx, float vy, int frames)
        {
            for (int i = 0; i < frames; i++)
                c.Step(ref vx, ref vy);
            return (vx, vy);
        }

        [Fact]
        public void UnsetFieldsHaveNoitasDefaults()
        {
            var c = Of();
            Assert.Equal((0f, 400f, 0.55f, 1000f, true), (c.GravityX, c.GravityY, c.AirFriction, c.TerminalVelocity, c.ApplyTerminal));
        }

        [Fact]
        public void HeavyBulletFliesAsTheProbeMeasuredInNoita()
        {
            // noita_probe.jsonl single:HEAVY_BULLET (bullet_heavy.xml gravity_y 200, air_friction 0.3): (vx, vy) at its
            // first sight 639.74, -26.92; 5 frames later 623.91, -9.83; 10 frames later 608.47, 6.83. Gravity first, then
            // friction on the whole velocity (the other order gives vy 6.99 at frame 10)
            var c = Of(("gravity_y", "200"), ("air_friction", "0.3"));
            var (vx, vy) = Fly(c, 639.74f, -26.92f, 5);
            Assert.Equal(623.91, vx, 1);
            Assert.Equal(-9.83, vy, 1);
            (vx, vy) = Fly(c, vx, vy, 5);
            Assert.Equal(608.47, vx, 1);
            Assert.Equal(6.83, vy, 1);
        }

        [Fact]
        public void NegativeFrictionSpeedsUpAndTerminalVelocityCaps()
        {
            // probe: DECELERATING_SHOT (spark 1.7 + 6) x0.503 per 5 frames; ACCELERATING_SHOT (1.7 - 3) x1.112;
            // DECELERATING_SHOT's spark was first seen at 1000 px/s exactly (terminal_velocity) though cast faster
            var slow = Of(("gravity_y", "0"), ("air_friction", "7.7"));
            Assert.Equal(282, Fly(slow, 561, 0, 5).vx, 0);   // the probe's path: 561, then 282 five frames later
            var fast = Of(("gravity_y", "0"), ("air_friction", "-1.3"));
            Assert.Equal(298, Fly(fast, 268, 0, 5).vx, 0);   // the probe's path: 268, then 298 five frames later
            var capped = Fly(Of(("gravity_y", "0"), ("air_friction", "0")), 1400, 0, 1);
            Assert.Equal(1000, capped.vx, 1);
            var free = Fly(Of(("gravity_y", "0"), ("air_friction", "0"), ("apply_terminal_velocity", "0")), 1400, 0, 1);
            Assert.Equal(1400, free.vx, 1);
        }
    }
}
