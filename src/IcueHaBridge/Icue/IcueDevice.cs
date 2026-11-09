using System;
using System.Collections.Generic;
using System.Text;

namespace IcueHaBridge.Icue
{
    /// <summary>
    /// A Corsair device as seen through the iCUE SDK.
    /// </summary>
    public sealed class IcueDevice
    {
        /// <summary>Index used by the SDK; it changes when devices are plugged or unplugged.</summary>
        public int Index { get; internal set; }

        /// <summary>Model name shown by iCUE, for instance "K100 RGB".</summary>
        public string Model { get; }

        /// <summary>Identifier given by the SDK, stable across restarts when the SDK provides one.</summary>
        public string DeviceId { get; }

        public IcueLed[] Leds { get; }

        /// <summary>Short stable key used in the MQTT topics and the Home Assistant entity id.</summary>
        public string Slug { get; }

        internal IcueDevice(int index, string model, string deviceId, IcueLed[] leds, string slug)
        {
            Index = index;
            Model = model;
            DeviceId = deviceId;
            Leds = leds;
            Slug = slug;
        }

        /// <summary>
        /// Turns a model name into something usable in a topic: lower case, letters, digits and underscores.
        /// </summary>
        public static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "device";
            }
            StringBuilder builder = new StringBuilder(value.Length);
            bool lastWasUnderscore = false;
            foreach (char character in value.Trim().ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                    lastWasUnderscore = false;
                }
                else if (!lastWasUnderscore && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasUnderscore = true;
                }
            }
            string slug = builder.ToString().Trim('_');
            return slug.Length == 0 ? "device" : slug;
        }

        /// <summary>
        /// Makes every slug unique by adding a number to the duplicates, so that two identical
        /// memory sticks do not collide in Home Assistant.
        /// </summary>
        public static void MakeSlugsUnique(List<IcueDevice> devices)
        {
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IcueDevice device in devices)
            {
                int count;
                if (seen.TryGetValue(device.Slug, out count))
                {
                    seen[device.Slug] = count + 1;
                }
                else
                {
                    seen[device.Slug] = 1;
                }
            }
            Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < devices.Count; i++)
            {
                IcueDevice device = devices[i];
                if (seen[device.Slug] == 1)
                {
                    continue;
                }
                int next;
                used.TryGetValue(device.Slug, out next);
                next++;
                used[device.Slug] = next;
                devices[i] = new IcueDevice(device.Index, device.Model + " " + next, device.DeviceId,
                                            device.Leds, device.Slug + "_" + next);
            }
        }
    }
}
