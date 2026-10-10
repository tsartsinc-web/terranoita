using System;
using System.Globalization;

namespace Terranoita.Noita
{
    /// <summary>
    /// Noita's VelocityComponent for one frame (design/magic_plan.md PC-24 step 1), in Noita's units: px/s, 60 frames a
    /// second. Measured in Noita by the probe (design/sources/noita_probe.jsonl): gravity adds gravity_y/60 a frame, then
    /// the whole velocity is scaled by 1 - air_friction/60 (bullet_heavy: vy -26.92 -> -9.83 -> 6.83 every 5 frames;
    /// the other order gives 6.99), negative friction speeds a shot up (ACCELERATING_SHOT), and apply_terminal_velocity
    /// caps the speed at terminal_velocity (DECELERATING_SHOT's spark was first seen at exactly 1000).
    /// </summary>
    public struct ShotFlight
    {
        public float GravityX, GravityY, AirFriction, TerminalVelocity;
        public bool ApplyTerminal;

        /// <summary>From a VelocityComponent's fields (null when the component does not set one: Noita's documented
        /// default from component_documentation.txt is used).</summary>
        public static ShotFlight From(Func<string, string> field, ComponentFieldTypes docs)
        {
            string Raw(string name) => field(name) ?? docs?.Default("VelocityComponent", name);
            float F(string name)
            {
                var raw = Raw(name);
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw new FormatException("VelocityComponent." + name + ": '" + raw + "' (no value and no documented default)");
                return v;
            }
            return new ShotFlight
            {
                GravityX = F("gravity_x"), GravityY = F("gravity_y"), AirFriction = F("air_friction"),
                TerminalVelocity = F("terminal_velocity"), ApplyTerminal = F("apply_terminal_velocity") != 0,
            };
        }

        /// <summary>One frame of the velocity (px/s).</summary>
        public void Step(ref float vx, ref float vy)
        {
            vx += GravityX / 60f;
            vy += GravityY / 60f;
            float k = 1f - AirFriction / 60f;
            if (k < 0f)
                k = 0f;
            vx *= k;
            vy *= k;
            if (ApplyTerminal && TerminalVelocity > 0f)
            {
                float s2 = vx * vx + vy * vy;
                if (s2 > TerminalVelocity * TerminalVelocity)
                {
                    float scale = TerminalVelocity / (float)Math.Sqrt(s2);
                    vx *= scale;
                    vy *= scale;
                }
            }
        }
    }
}
