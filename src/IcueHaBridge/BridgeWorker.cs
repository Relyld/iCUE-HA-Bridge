using IcueHaBridge.Effects;
using IcueHaBridge.Icue;
using IcueHaBridge.Mqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IcueHaBridge
{
    /// <summary>
    /// Wires everything together: reads the settings, connects to iCUE and to the broker,
    /// and keeps the two in step for as long as the service runs.
    /// </summary>
    public sealed class BridgeWorker : BackgroundService
    {
        private readonly ILogger<BridgeWorker> logger;

        private LightingEngine engine;
        private MqttBridge bridge;

        public BridgeWorker(ILogger<BridgeWorker> logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string directory = AppContext.BaseDirectory;
            string problem;
            AppSettings settings = AppSettings.Load(directory, out problem);
            if (problem != null)
            {
                logger.LogWarning("{Problem}", problem);
            }

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            logger.LogInformation("{Service} {Version} starting. Effects available: {Effects}",
                Program.ServiceName, version, string.Join(", ", EffectCatalog.Names()));

            Topics topics = new Topics(settings.DiscoveryPrefix, settings.NodeId);
            Payloads payloads = new Payloads(topics, version);
            IcueController controller = new IcueController();
            engine = new LightingEngine(controller, logger, settings.FramesPerSecond);
            bridge = new MqttBridge(settings, topics, payloads, logger);

            // When the broker accepts us, everything is announced again from scratch.
            bridge.Connected = PublishEverythingAsync;

            // A command from Home Assistant changes the state, then we tell Home Assistant what we did.
            bridge.CommandReceived = (slug, command) =>
            {
                List<string> changed = engine.Apply(slug, command);
                if (changed.Count == 0)
                {
                    logger.LogDebug("Command for an unknown light '{Slug}' ignored.", slug);
                    return;
                }
                _ = PublishStatesAsync(changed);
            };

            // A device being plugged in or unplugged changes the entity list.
            engine.DevicesChanged += () => { _ = PublishEverythingAsync(); };

            await bridge.StartAsync(stoppingToken).ConfigureAwait(false);
            await engine.RunAsync(stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Announces every entity and publishes the state of each one.
        /// </summary>
        private async Task PublishEverythingAsync()
        {
            try
            {
                List<IcueDevice> devices = engine.Devices;
                if (devices.Count == 0)
                {
                    logger.LogInformation("No iCUE device found yet, nothing announced.");
                    return;
                }
                await bridge.PublishLightDiscoveryAsync(devices).ConfigureAwait(false);

                List<string> slugs = new List<string>();
                foreach (IcueDevice device in devices)
                {
                    slugs.Add(device.Slug);
                }
                await PublishStatesAsync(slugs).ConfigureAwait(false);
                logger.LogInformation("Announced {Count} light(s) to Home Assistant.", devices.Count + 1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not announce the entities.");
            }
        }

        /// <summary>
        /// Publishes the state of the given lights, and of the "all devices" entity, which always follows.
        /// </summary>
        private async Task PublishStatesAsync(List<string> slugs)
        {
            try
            {
                foreach (string slug in slugs)
                {
                    await bridge.PublishLightStateAsync(slug, engine.GetState(slug)).ConfigureAwait(false);
                }
                await bridge.PublishLightStateAsync(LightingEngine.AllSlug,
                                                    engine.GetState(LightingEngine.AllSlug)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not publish the light states.");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("{Service} stopping, giving the lighting back to iCUE.", Program.ServiceName);
            if (bridge != null)
            {
                await bridge.StopAsync().ConfigureAwait(false);
            }
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
