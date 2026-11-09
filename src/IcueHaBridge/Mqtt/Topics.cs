namespace IcueHaBridge.Mqtt
{
    /// <summary>
    /// Builds every MQTT topic this program uses, so they are all defined in one place.
    /// </summary>
    public sealed class Topics
    {
        private readonly string discoveryPrefix;

        /// <summary>Root of the topics owned by this program.</summary>
        public string Base { get; }

        /// <summary>Node identifier used inside the Home Assistant discovery topics.</summary>
        public string NodeId { get; }

        public Topics(string discoveryPrefix, string nodeId)
        {
            this.discoveryPrefix = discoveryPrefix.TrimEnd('/');
            NodeId = nodeId;
            Base = nodeId;
        }

        /// <summary>Retained "online" or "offline", also used as the last will.</summary>
        public string Availability { get { return Base + "/status"; } }

        public string LightState(string slug) { return Base + "/light/" + slug + "/state"; }

        public string LightCommand(string slug) { return Base + "/light/" + slug + "/set"; }

        /// <summary>One subscription covers every light, whatever the devices turn out to be.</summary>
        public string LightCommandWildcard { get { return Base + "/light/+/set"; } }

        public string LightDiscovery(string slug)
        {
            return discoveryPrefix + "/light/" + NodeId + "/" + slug + "/config";
        }

        /// <summary>
        /// Pulls the device slug out of a command topic, or null when the topic is not one of ours.
        /// </summary>
        public string SlugFromCommandTopic(string topic)
        {
            string prefix = Base + "/light/";
            const string suffix = "/set";
            if (topic == null || !topic.StartsWith(prefix) || !topic.EndsWith(suffix))
            {
                return null;
            }
            int start = prefix.Length;
            int length = topic.Length - suffix.Length - start;
            return length <= 0 ? null : topic.Substring(start, length);
        }
    }
}
