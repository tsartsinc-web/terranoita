using System;

namespace Terranoita.Noita
{
    /// <summary>
    /// The beam of Noita's LaserEmitterComponent (ConfigLaser max_length), in Noita px: from its emitter along an angle
    /// until max_length or the last free cell before the ground. A beam that starts in the ground has no length.
    /// </summary>
    public static class LaserBeam
    {
        public static (float x, float y) End(float x, float y, float angle, float maxLength, Func<float, float, bool> solid)
        {
            float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
            float lastX = x, lastY = y;
            for (int d = 1; d <= (int)maxLength; d++)
            {
                float px = x + dx * d, py = y + dy * d;
                if (solid(px, py))
                    return (lastX, lastY);
                lastX = px;
                lastY = py;
            }
            return solid(x, y) ? (x, y) : (lastX, lastY);
        }
    }
}
