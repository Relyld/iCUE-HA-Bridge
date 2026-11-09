using System;

namespace IcueHaBridge.Effects
{
    /// <summary>
    /// Small colour helpers shared by the effects.
    /// </summary>
    internal static class ColorMath
    {
        /// <summary>
        /// Converts a hue, saturation and value into bytes. Hue wraps, so 1.2 is the same as 0.2.
        /// </summary>
        internal static void HsvToRgb(double hue, double saturation, double value,
                                      out byte red, out byte green, out byte blue)
        {
            hue = hue - Math.Floor(hue);
            if (saturation <= 0)
            {
                red = green = blue = ToByte(value);
                return;
            }

            double sector = hue * 6.0;
            int index = (int)Math.Floor(sector) % 6;
            double fraction = sector - Math.Floor(sector);
            double p = value * (1.0 - saturation);
            double q = value * (1.0 - (saturation * fraction));
            double t = value * (1.0 - (saturation * (1.0 - fraction)));

            double r, g, b;
            switch (index)
            {
                case 0: r = value; g = t; b = p; break;
                case 1: r = q; g = value; b = p; break;
                case 2: r = p; g = value; b = t; break;
                case 3: r = p; g = q; b = value; break;
                case 4: r = t; g = p; b = value; break;
                default: r = value; g = p; b = q; break;
            }
            red = ToByte(r);
            green = ToByte(g);
            blue = ToByte(b);
        }

        /// <summary>
        /// Turns a 0 to 1 value into a byte, clamped.
        /// </summary>
        internal static byte ToByte(double value)
        {
            double scaled = Math.Round(value * 255.0);
            if (scaled <= 0) { return 0; }
            if (scaled >= 255) { return 255; }
            return (byte)scaled;
        }

        /// <summary>
        /// Scales a colour channel by a factor between 0 and 1.
        /// </summary>
        internal static byte Scale(byte channel, double factor)
        {
            double scaled = Math.Round(channel * factor);
            if (scaled <= 0) { return 0; }
            if (scaled >= 255) { return 255; }
            return (byte)scaled;
        }
    }
}
