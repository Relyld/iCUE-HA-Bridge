using System;
using System.Collections.Generic;

namespace IcueHaBridge.Effects
{
    /// <summary>
    /// The effects offered to Home Assistant, looked up by name.
    /// </summary>
    public static class EffectCatalog
    {
        public const string None = "None";
        public const string Rainbow = "Rainbow";
        public const string ColorCycle = "Color Cycle";
        public const string Breathing = "Breathing";
        public const string Wave = "Wave";
        public const string Pulse = "Pulse";

        private static readonly IEffect[] All =
        {
            new StaticEffect(),
            new RainbowEffect(),
            new ColorCycleEffect(),
            new BreathingEffect(),
            new WaveEffect(),
            new PulseEffect()
        };

        private static readonly Dictionary<string, IEffect> ByName = Build();

        private static Dictionary<string, IEffect> Build()
        {
            Dictionary<string, IEffect> map = new Dictionary<string, IEffect>(StringComparer.OrdinalIgnoreCase);
            foreach (IEffect effect in All)
            {
                map[effect.Name] = effect;
            }
            return map;
        }

        /// <summary>Names in the order Home Assistant should show them.</summary>
        public static List<string> Names()
        {
            List<string> names = new List<string>(All.Length);
            foreach (IEffect effect in All)
            {
                names.Add(effect.Name);
            }
            return names;
        }

        /// <summary>
        /// Finds an effect by name, falling back to the plain colour for anything unknown.
        /// </summary>
        public static IEffect Resolve(string name)
        {
            IEffect effect;
            if (!string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out effect))
            {
                return effect;
            }
            return ByName[None];
        }

        /// <summary>Whether this name is one we offer.</summary>
        public static bool IsKnown(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && ByName.ContainsKey(name.Trim());
        }
    }
}
