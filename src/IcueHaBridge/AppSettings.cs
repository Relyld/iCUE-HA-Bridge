using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IcueHaBridge
{
    /// <summary>
    /// Everything the user can configure, read from settings.json next to the executable.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>Host name or IP address of the MQTT broker.</summary>
        [JsonPropertyName("mqttHost")]
        public string MqttHost { get; set; } = "";

        [JsonPropertyName("mqttPort")]
        public int MqttPort { get; set; } = 1883;

        [JsonPropertyName("mqttUser")]
        public string MqttUser { get; set; } = "";

        [JsonPropertyName("mqttPassword")]
        public string MqttPassword { get; set; } = "";

        /// <summary>Home Assistant discovery prefix; only change it if yours is not the default.</summary>
        [JsonPropertyName("discoveryPrefix")]
        public string DiscoveryPrefix { get; set; } = "homeassistant";

        /// <summary>Animation rate. 30 is smooth; lower it if the CPU cost bothers you.</summary>
        [JsonPropertyName("framesPerSecond")]
        public int FramesPerSecond { get; set; } = 30;

        /// <summary>Identifier used in every MQTT topic and entity id.</summary>
        [JsonIgnore]
        public string NodeId
        {
            get { return "icue_ha_bridge"; }
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        /// <summary>
        /// Reads settings.json from the folder holding the executable. Missing or broken file
        /// gives the defaults back, so the service always starts and can say what is wrong.
        /// </summary>
        public static AppSettings Load(string directory, out string problem)
        {
            problem = null;
            string path = Path.Combine(directory, "settings.json");
            if (!File.Exists(path))
            {
                problem = path + " does not exist, using the defaults";
                return new AppSettings();
            }
            try
            {
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (settings == null)
                {
                    problem = path + " is empty, using the defaults";
                    return new AppSettings();
                }
                if (settings.FramesPerSecond < 1) { settings.FramesPerSecond = 1; }
                if (settings.FramesPerSecond > 60) { settings.FramesPerSecond = 60; }
                if (settings.MqttPort <= 0) { settings.MqttPort = 1883; }
                if (string.IsNullOrWhiteSpace(settings.DiscoveryPrefix)) { settings.DiscoveryPrefix = "homeassistant"; }
                return settings;
            }
            catch (Exception exception)
            {
                problem = path + " could not be read (" + exception.Message + "), using the defaults";
                return new AppSettings();
            }
        }
    }
}
