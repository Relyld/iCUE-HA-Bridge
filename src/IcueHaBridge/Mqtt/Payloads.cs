using IcueHaBridge.Effects;
using IcueHaBridge.Icue;
using System.Collections.Generic;
using System.Text.Json;

namespace IcueHaBridge.Mqtt
{
    /// <summary>
    /// Builds the JSON that Home Assistant expects.
    /// </summary>
    public sealed class Payloads
    {
        private readonly Topics topics;
        private readonly string version;

        public Payloads(Topics topics, string version)
        {
            this.topics = topics;
            this.version = version;
        }

        /// <summary>
        /// The device block, which groups every entity under one device in Home Assistant.
        /// </summary>
        private object DeviceBlock()
        {
            return new
            {
                identifiers = new[] { topics.NodeId },
                name = "iCUE",
                manufacturer = "Corsair",
                model = "iCUE-HA-Bridge",
                sw_version = version
            };
        }

        private object OriginBlock()
        {
            return new
            {
                name = "iCUE-HA-Bridge",
                sw = version,
                url = "https://github.com/Relyld/iCUE-HA-Bridge"
            };
        }

        /// <summary>
        /// Discovery payload of one light. Retained, so the entity survives a restart of either side.
        /// </summary>
        public string LightDiscovery(string slug, string displayName)
        {
            object payload = new
            {
                name = displayName,
                unique_id = topics.NodeId + "_" + slug,
                object_id = topics.NodeId + "_" + slug,
                schema = "json",
                state_topic = topics.LightState(slug),
                command_topic = topics.LightCommand(slug),
                availability_topic = topics.Availability,
                payload_available = "online",
                payload_not_available = "offline",
                brightness = true,
                brightness_scale = 255,
                supported_color_modes = new[] { "rgb" },
                effect = true,
                effect_list = EffectCatalog.Names(),
                icon = "mdi:keyboard-variant",
                device = DeviceBlock(),
                origin = OriginBlock()
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// State of one light, in the JSON schema Home Assistant uses.
        /// </summary>
        public static string LightState(DeviceState state)
        {
            if (!state.IsOn)
            {
                return JsonSerializer.Serialize(new { state = "OFF" });
            }
            object payload = new
            {
                state = "ON",
                brightness = state.Brightness,
                color_mode = "rgb",
                color = new { r = state.R, g = state.G, b = state.B },
                effect = state.Effect
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// Reads a command sent by Home Assistant. Unknown fields are ignored.
        /// </summary>
        public static LightCommand ParseCommand(string json)
        {
            LightCommand command = new LightCommand();
            if (string.IsNullOrWhiteSpace(json))
            {
                return command;
            }
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return command;
                    }

                    JsonElement element;
                    if (root.TryGetProperty("state", out element) && element.ValueKind == JsonValueKind.String)
                    {
                        string value = element.GetString();
                        if (string.Equals(value, "ON", System.StringComparison.OrdinalIgnoreCase)) { command.IsOn = true; }
                        else if (string.Equals(value, "OFF", System.StringComparison.OrdinalIgnoreCase)) { command.IsOn = false; }
                    }
                    if (root.TryGetProperty("brightness", out element) && element.ValueKind == JsonValueKind.Number)
                    {
                        command.Brightness = element.GetInt32();
                    }
                    if (root.TryGetProperty("effect", out element) && element.ValueKind == JsonValueKind.String)
                    {
                        command.Effect = element.GetString();
                    }
                    if (root.TryGetProperty("color", out element) && element.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement channel;
                        if (element.TryGetProperty("r", out channel) && channel.ValueKind == JsonValueKind.Number)
                        {
                            command.R = ToByte(channel.GetInt32());
                        }
                        if (element.TryGetProperty("g", out channel) && channel.ValueKind == JsonValueKind.Number)
                        {
                            command.G = ToByte(channel.GetInt32());
                        }
                        if (element.TryGetProperty("b", out channel) && channel.ValueKind == JsonValueKind.Number)
                        {
                            command.B = ToByte(channel.GetInt32());
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A payload we cannot read changes nothing, which is better than crashing the service.
            }
            return command;
        }

        private static byte ToByte(int value)
        {
            if (value < 0) { return 0; }
            if (value > 255) { return 255; }
            return (byte)value;
        }

        /// <summary>
        /// Turns a model name into the name shown in Home Assistant.
        /// </summary>
        public static string DisplayName(IcueDevice device)
        {
            return device.Model;
        }

        /// <summary>Names of every effect, for the log.</summary>
        public static List<string> EffectNames()
        {
            return EffectCatalog.Names();
        }
    }
}
