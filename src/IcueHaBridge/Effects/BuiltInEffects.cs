using IcueHaBridge.Icue;
using System;

namespace IcueHaBridge.Effects
{
    /// <summary>
    /// A plain colour across the whole device. This is what "None" means in Home Assistant.
    /// </summary>
    public sealed class StaticEffect : IEffect
    {
        public string Name { get { return EffectCatalog.None; } }
        public bool IsAnimated { get { return false; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            byte r = ColorMath.Scale(frame.R, frame.Brightness);
            byte g = ColorMath.Scale(frame.G, frame.Brightness);
            byte b = ColorMath.Scale(frame.B, frame.Brightness);
            for (int i = 0; i < device.Leds.Length; i++)
            {
                device.Leds[i].R = r;
                device.Leds[i].G = g;
                device.Leds[i].B = b;
            }
        }
    }

    /// <summary>
    /// The whole spectrum laid across the device, sliding sideways. The chosen colour is ignored.
    /// </summary>
    public sealed class RainbowEffect : IEffect
    {
        private const double CyclesPerSecond = 0.15;

        public string Name { get { return EffectCatalog.Rainbow; } }
        public bool IsAnimated { get { return true; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            double offset = frame.TimeSeconds * CyclesPerSecond;
            for (int i = 0; i < device.Leds.Length; i++)
            {
                IcueLed led = device.Leds[i];
                byte r, g, b;
                ColorMath.HsvToRgb(led.X - offset, 1.0, frame.Brightness, out r, out g, out b);
                led.R = r;
                led.G = g;
                led.B = b;
            }
        }
    }

    /// <summary>
    /// The whole device holds one colour at a time, drifting slowly through the spectrum.
    /// The chosen colour is ignored.
    /// </summary>
    public sealed class ColorCycleEffect : IEffect
    {
        private const double CyclesPerSecond = 0.08;

        public string Name { get { return EffectCatalog.ColorCycle; } }
        public bool IsAnimated { get { return true; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            byte r, g, b;
            ColorMath.HsvToRgb(frame.TimeSeconds * CyclesPerSecond, 1.0, frame.Brightness, out r, out g, out b);
            for (int i = 0; i < device.Leds.Length; i++)
            {
                device.Leds[i].R = r;
                device.Leds[i].G = g;
                device.Leds[i].B = b;
            }
        }
    }

    /// <summary>
    /// The chosen colour fading in and out, never going fully dark.
    /// </summary>
    public sealed class BreathingEffect : IEffect
    {
        private const double CyclesPerSecond = 0.25;
        private const double Floor = 0.08;

        public string Name { get { return EffectCatalog.Breathing; } }
        public bool IsAnimated { get { return true; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            double wave = (Math.Sin(frame.TimeSeconds * CyclesPerSecond * 2.0 * Math.PI) + 1.0) / 2.0;
            // Squaring makes the fade linger at the dark end, which looks more like breathing.
            double intensity = (Floor + ((1.0 - Floor) * wave * wave)) * frame.Brightness;
            byte r = ColorMath.Scale(frame.R, intensity);
            byte g = ColorMath.Scale(frame.G, intensity);
            byte b = ColorMath.Scale(frame.B, intensity);
            for (int i = 0; i < device.Leds.Length; i++)
            {
                device.Leds[i].R = r;
                device.Leds[i].G = g;
                device.Leds[i].B = b;
            }
        }
    }

    /// <summary>
    /// A bright band of the chosen colour travelling from one side of the device to the other.
    /// </summary>
    public sealed class WaveEffect : IEffect
    {
        private const double CyclesPerSecond = 0.4;
        private const double BandWidth = 0.35;
        private const double Floor = 0.1;

        public string Name { get { return EffectCatalog.Wave; } }
        public bool IsAnimated { get { return true; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            double head = (frame.TimeSeconds * CyclesPerSecond) % 1.0;
            for (int i = 0; i < device.Leds.Length; i++)
            {
                IcueLed led = device.Leds[i];
                // Distance to the band, measured the short way round so the wave never jumps.
                double distance = Math.Abs(led.X - head);
                if (distance > 0.5) { distance = 1.0 - distance; }
                double inBand = distance < BandWidth ? 1.0 - (distance / BandWidth) : 0.0;
                double intensity = (Floor + ((1.0 - Floor) * inBand * inBand)) * frame.Brightness;
                led.R = ColorMath.Scale(frame.R, intensity);
                led.G = ColorMath.Scale(frame.G, intensity);
                led.B = ColorMath.Scale(frame.B, intensity);
            }
        }
    }

    /// <summary>
    /// Rings of the chosen colour spreading out from the middle of the device.
    /// </summary>
    public sealed class PulseEffect : IEffect
    {
        private const double CyclesPerSecond = 0.5;
        private const double RingWidth = 0.25;
        private const double Floor = 0.05;

        public string Name { get { return EffectCatalog.Pulse; } }
        public bool IsAnimated { get { return true; } }

        public void Render(IcueDevice device, EffectFrame frame)
        {
            double head = (frame.TimeSeconds * CyclesPerSecond) % 1.0;
            for (int i = 0; i < device.Leds.Length; i++)
            {
                IcueLed led = device.Leds[i];
                double dx = led.X - 0.5;
                double dy = led.Y - 0.5;
                // Divided by the half diagonal so the ring reaches the corners at the end of a cycle.
                double radius = Math.Sqrt((dx * dx) + (dy * dy)) / 0.7071;
                double distance = Math.Abs(radius - head);
                double onRing = distance < RingWidth ? 1.0 - (distance / RingWidth) : 0.0;
                double intensity = (Floor + ((1.0 - Floor) * onRing * onRing)) * frame.Brightness;
                led.R = ColorMath.Scale(frame.R, intensity);
                led.G = ColorMath.Scale(frame.G, intensity);
                led.B = ColorMath.Scale(frame.B, intensity);
            }
        }
    }
}
