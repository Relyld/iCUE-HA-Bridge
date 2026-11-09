using IcueHaBridge.Effects;
using IcueHaBridge.Icue;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IcueHaBridge.Mqtt
{
    /// <summary>
    /// Talks to the MQTT broker: announces the entities, publishes their state, and hands the
    /// commands coming from Home Assistant back to the caller.
    /// </summary>
    public sealed class MqttBridge
    {
        private const string Online = "online";
        private const string Offline = "offline";

        private readonly AppSettings settings;
        private readonly Topics topics;
        private readonly Payloads payloads;
        private readonly ILogger logger;

        private IMqttClient client;
        private bool stopping;
        private bool reconnectScheduled;

        /// <summary>Called with the device slug and the command whenever Home Assistant asks for something.</summary>
        public Action<string, LightCommand> CommandReceived;

        /// <summary>Called once the broker accepted the connection, to publish everything again.</summary>
        public Func<Task> Connected;

        public MqttBridge(AppSettings settings, Topics topics, Payloads payloads, ILogger logger)
        {
            this.settings = settings;
            this.topics = topics;
            this.payloads = payloads;
            this.logger = logger;
        }

        public bool IsConnected
        {
            get { return client != null && client.IsConnected; }
        }

        /// <summary>
        /// Connects, and keeps reconnecting on its own until the service stops.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.MqttHost))
            {
                logger.LogError("No MQTT broker configured. Set mqttHost in settings.json.");
                return;
            }

            client = new MqttFactory().CreateMqttClient();
            client.ApplicationMessageReceivedAsync += OnMessageReceived;
            client.DisconnectedAsync += OnDisconnected;
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        private MqttClientOptions BuildOptions()
        {
            MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
                .WithClientId(topics.NodeId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8))
                .WithTcpServer(settings.MqttHost.Trim(), settings.MqttPort)
                .WithCleanSession()
                // The broker publishes this for us if the service dies, so Home Assistant shows the
                // entities as unavailable instead of silently keeping the last values.
                .WithWillTopic(topics.Availability)
                .WithWillPayload(Encoding.UTF8.GetBytes(Offline))
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

            if (!string.IsNullOrWhiteSpace(settings.MqttUser))
            {
                builder = builder.WithCredentials(settings.MqttUser, settings.MqttPassword ?? "");
            }
            return builder.Build();
        }

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (stopping || client == null)
            {
                return;
            }
            try
            {
                await client.ConnectAsync(BuildOptions(), cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Connected to the MQTT broker at {Host}:{Port}.", settings.MqttHost, settings.MqttPort);

                await PublishAsync(topics.Availability, Online, true, MqttQualityOfServiceLevel.AtLeastOnce)
                    .ConfigureAwait(false);

                MqttClientSubscribeOptions subscribe = new MqttClientSubscribeOptions();
                subscribe.TopicFilters = new List<MqttTopicFilter>
                {
                    new MqttTopicFilter
                    {
                        Topic = topics.LightCommandWildcard,
                        QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
                    }
                };
                await client.SubscribeAsync(subscribe).ConfigureAwait(false);

                Func<Task> connected = Connected;
                if (connected != null)
                {
                    await connected().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning("Could not reach the MQTT broker ({Message}). Retrying.", exception.Message);
                ScheduleReconnect(cancellationToken);
            }
        }

        private Task OnDisconnected(MqttClientDisconnectedEventArgs eventArgs)
        {
            if (!stopping)
            {
                logger.LogWarning("Disconnected from the MQTT broker. Reconnecting.");
                ScheduleReconnect(CancellationToken.None);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Waits a few seconds then tries again, without stacking up attempts.
        /// </summary>
        private void ScheduleReconnect(CancellationToken cancellationToken)
        {
            if (stopping || reconnectScheduled)
            {
                return;
            }
            reconnectScheduled = true;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    reconnectScheduled = false;
                }
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            });
        }

        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            try
            {
                string topic = eventArgs.ApplicationMessage.Topic;
                string slug = topics.SlugFromCommandTopic(topic);
                if (slug == null)
                {
                    return Task.CompletedTask;
                }
                byte[] payload = eventArgs.ApplicationMessage.Payload;
                string json = payload == null ? "" : Encoding.UTF8.GetString(payload, 0, payload.Length);
                LightCommand command = Payloads.ParseCommand(json);

                Action<string, LightCommand> handler = CommandReceived;
                if (handler != null)
                {
                    handler(slug, command);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not handle an MQTT message.");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Announces every light, plus the entity that drives them all at once.
        /// </summary>
        public async Task PublishLightDiscoveryAsync(List<IcueDevice> devices)
        {
            foreach (IcueDevice device in devices)
            {
                await PublishAsync(topics.LightDiscovery(device.Slug),
                                   payloads.LightDiscovery(device.Slug, device.Model),
                                   true, MqttQualityOfServiceLevel.AtLeastOnce).ConfigureAwait(false);
            }
            if (devices.Count > 0)
            {
                await PublishAsync(topics.LightDiscovery(LightingEngine.AllSlug),
                                   payloads.LightDiscovery(LightingEngine.AllSlug, "All devices"),
                                   true, MqttQualityOfServiceLevel.AtLeastOnce).ConfigureAwait(false);
            }
        }

        public Task PublishLightStateAsync(string slug, DeviceState state)
        {
            return PublishAsync(topics.LightState(slug), Payloads.LightState(state), true,
                                MqttQualityOfServiceLevel.AtMostOnce);
        }

        private async Task PublishAsync(string topic, string payload, bool retain, MqttQualityOfServiceLevel qos)
        {
            if (client == null || !client.IsConnected)
            {
                return;
            }
            try
            {
                MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .WithQualityOfServiceLevel(qos)
                    .WithRetainFlag(retain)
                    .Build();
                await client.PublishAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Could not publish to {Topic}: {Message}", topic, exception.Message);
            }
        }

        /// <summary>
        /// Says goodbye properly, so Home Assistant marks the entities unavailable straight away.
        /// </summary>
        public async Task StopAsync()
        {
            stopping = true;
            if (client == null)
            {
                return;
            }
            try
            {
                if (client.IsConnected)
                {
                    await PublishAsync(topics.Availability, Offline, true, MqttQualityOfServiceLevel.AtLeastOnce)
                        .ConfigureAwait(false);
                    await client.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning("Could not close the MQTT connection cleanly: {Message}", exception.Message);
            }
            finally
            {
                client.Dispose();
                client = null;
            }
        }
    }
}
