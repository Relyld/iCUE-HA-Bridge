using IcueHaBridge.Icue;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IcueHaBridge.Effects
{
    /// <summary>
    /// Owns the devices and draws them. While at least one device is on, this program draws in
    /// front of iCUE; as soon as every device is off it steps behind and iCUE shows again.
    /// </summary>
    public sealed class LightingEngine
    {
        /// <summary>Name of the entity that drives every device at once.</summary>
        public const string AllSlug = "all";

        /// <summary>How often the layer priority is claimed again, in seconds.</summary>
        private const int LayerReassertSeconds = 5;

        /// <summary>How often iCUE is asked whether it is still there, in seconds.</summary>
        private const int AliveCheckSeconds = 10;

        private readonly IcueController controller;
        private readonly ILogger logger;
        private readonly int framesPerSecond;

        private readonly object sync = new object();
        private List<IcueDevice> devices = new List<IcueDevice>();
        private readonly Dictionary<string, DeviceState> states =
            new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);

        private bool needsRedraw;

        /// <summary>Raised when the device list changed, so that discovery can be published again.</summary>
        public event Action DevicesChanged;

        /// <summary>Raised with the slug of a device whose state changed.</summary>
        public event Action<string> StateChanged;

        public LightingEngine(IcueController controller, ILogger logger, int framesPerSecond)
        {
            this.controller = controller;
            this.logger = logger;
            this.framesPerSecond = framesPerSecond;
        }

        /// <summary>The devices currently known, in SDK order.</summary>
        public List<IcueDevice> Devices
        {
            get { lock (sync) { return new List<IcueDevice>(devices); } }
        }

        /// <summary>
        /// Returns a copy of the state of a device, or of the whole set for <see cref="AllSlug"/>.
        /// </summary>
        public DeviceState GetState(string slug)
        {
            lock (sync)
            {
                if (string.Equals(slug, AllSlug, StringComparison.OrdinalIgnoreCase))
                {
                    return SummariseAll();
                }
                DeviceState state;
                return states.TryGetValue(slug, out state) ? state.Clone() : new DeviceState();
            }
        }

        /// <summary>
        /// Builds the state shown by the "all devices" entity: on when any device is on, and the
        /// average of the devices that are on.
        /// </summary>
        private DeviceState SummariseAll()
        {
            DeviceState summary = new DeviceState { IsOn = false };
            int count = 0;
            int r = 0, g = 0, b = 0, brightness = 0;
            string effect = null;
            bool sameEffect = true;
            foreach (IcueDevice device in devices)
            {
                DeviceState state;
                if (!states.TryGetValue(device.Slug, out state) || !state.IsOn)
                {
                    continue;
                }
                count++;
                r += state.R;
                g += state.G;
                b += state.B;
                brightness += state.Brightness;
                if (effect == null) { effect = state.Effect; }
                else if (!string.Equals(effect, state.Effect, StringComparison.OrdinalIgnoreCase)) { sameEffect = false; }
            }
            if (count == 0)
            {
                return summary;
            }
            summary.IsOn = true;
            summary.R = (byte)(r / count);
            summary.G = (byte)(g / count);
            summary.B = (byte)(b / count);
            summary.Brightness = brightness / count;
            summary.Effect = sameEffect && effect != null ? effect : EffectCatalog.None;
            return summary;
        }

        /// <summary>
        /// Applies a command to one device, or to every device when the slug is <see cref="AllSlug"/>.
        /// </summary>
        /// <returns>The slugs whose state changed, so they can be published.</returns>
        public List<string> Apply(string slug, LightCommand command)
        {
            List<string> changed = new List<string>();
            lock (sync)
            {
                if (string.Equals(slug, AllSlug, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (IcueDevice device in devices)
                    {
                        if (ApplyToOne(device.Slug, command)) { changed.Add(device.Slug); }
                    }
                }
                else if (states.ContainsKey(slug))
                {
                    if (ApplyToOne(slug, command)) { changed.Add(slug); }
                }
                if (changed.Count > 0)
                {
                    needsRedraw = true;
                }
            }
            return changed;
        }

        private bool ApplyToOne(string slug, LightCommand command)
        {
            DeviceState state;
            if (!states.TryGetValue(slug, out state))
            {
                return false;
            }
            if (command.R.HasValue && command.G.HasValue && command.B.HasValue)
            {
                state.R = command.R.Value;
                state.G = command.G.Value;
                state.B = command.B.Value;
            }
            if (command.Brightness.HasValue)
            {
                int brightness = command.Brightness.Value;
                state.Brightness = brightness < 1 ? 1 : (brightness > 255 ? 255 : brightness);
            }
            if (command.Effect != null && EffectCatalog.IsKnown(command.Effect))
            {
                state.Effect = EffectCatalog.Resolve(command.Effect).Name;
            }
            if (command.IsOn.HasValue)
            {
                state.IsOn = command.IsOn.Value;
            }
            return true;
        }

        /// <summary>
        /// Connects to iCUE, then draws until the service stops.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Stopwatch clock = Stopwatch.StartNew();
            EffectFrame frame = new EffectFrame();
            DateTime nextConnectionAttempt = DateTime.MinValue;
            DateTime nextLayerReassert = DateTime.MinValue;
            DateTime nextAliveCheck = DateTime.UtcNow.AddSeconds(AliveCheckSeconds);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!controller.IsConnected)
                {
                    if (DateTime.UtcNow < nextConnectionAttempt)
                    {
                        await Delay(500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    nextConnectionAttempt = DateTime.UtcNow.AddSeconds(10);
                    if (!TryConnect())
                    {
                        continue;
                    }
                }

                bool anyOn;
                bool anyAnimated;
                List<IcueDevice> snapshot;
                lock (sync)
                {
                    snapshot = new List<IcueDevice>(devices);
                    anyOn = false;
                    anyAnimated = false;
                    foreach (IcueDevice device in snapshot)
                    {
                        DeviceState state;
                        if (!states.TryGetValue(device.Slug, out state) || !state.IsOn)
                        {
                            continue;
                        }
                        anyOn = true;
                        if (EffectCatalog.Resolve(state.Effect).IsAnimated) { anyAnimated = true; }
                    }
                }

                controller.SetInFront(anyOn);

                // Restarting iCUE silently drops our layer priority, so it is claimed again
                // regularly instead of only when the state changes.
                if (DateTime.UtcNow >= nextLayerReassert)
                {
                    nextLayerReassert = DateTime.UtcNow.AddSeconds(LayerReassertSeconds);
                    controller.ReassertLayer();
                }

                // While nothing is being drawn, nothing would notice iCUE going away.
                if (DateTime.UtcNow >= nextAliveCheck)
                {
                    nextAliveCheck = DateTime.UtcNow.AddSeconds(AliveCheckSeconds);
                    if (!controller.CheckAlive())
                    {
                        logger.LogInformation("iCUE is no longer answering, waiting for it to come back.");
                        continue;
                    }
                }

                if (anyOn && (anyAnimated || needsRedraw))
                {
                    needsRedraw = false;
                    frame.TimeSeconds = clock.Elapsed.TotalSeconds;
                    RenderAll(snapshot, frame);
                    if (!controller.Flush(snapshot))
                    {
                        logger.LogWarning("Lost the link to iCUE ({Reason}), reconnecting.", controller.ProtocolSummary);
                        nextConnectionAttempt = DateTime.MinValue;
                        continue;
                    }
                }

                int delay = anyAnimated ? Math.Max(1, 1000 / framesPerSecond) : 200;
                await Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            // Leaving the lighting to iCUE on the way out is nicer than freezing the last frame.
            controller.SetInFront(false);
        }

        private void RenderAll(List<IcueDevice> snapshot, EffectFrame frame)
        {
            foreach (IcueDevice device in snapshot)
            {
                DeviceState state;
                lock (sync)
                {
                    if (!states.TryGetValue(device.Slug, out state))
                    {
                        state = null;
                    }
                    else
                    {
                        state = state.Clone();
                    }
                }
                if (state == null || !state.IsOn)
                {
                    // A device that is off goes dark; iCUE only comes back once every device is off.
                    for (int i = 0; i < device.Leds.Length; i++)
                    {
                        device.Leds[i].R = 0;
                        device.Leds[i].G = 0;
                        device.Leds[i].B = 0;
                    }
                    continue;
                }
                frame.R = state.R;
                frame.G = state.G;
                frame.B = state.B;
                frame.Brightness = state.Brightness / 255.0;
                EffectCatalog.Resolve(state.Effect).Render(device, frame);
            }
        }

        /// <summary>
        /// Connects and reads the device list, keeping the states of devices already known.
        /// </summary>
        private bool TryConnect()
        {
            try
            {
                if (!controller.Connect())
                {
                    logger.LogInformation("Waiting for iCUE to start.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "iCUE refused the connection.");
                return false;
            }

            List<IcueDevice> found = controller.Enumerate();
            bool listChanged;
            lock (sync)
            {
                listChanged = found.Count != devices.Count;
                if (!listChanged)
                {
                    for (int i = 0; i < found.Count; i++)
                    {
                        if (!string.Equals(found[i].Slug, devices[i].Slug, StringComparison.OrdinalIgnoreCase))
                        {
                            listChanged = true;
                            break;
                        }
                    }
                }
                devices = found;
                Dictionary<string, DeviceState> kept =
                    new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
                foreach (IcueDevice device in found)
                {
                    DeviceState existing;
                    kept[device.Slug] = states.TryGetValue(device.Slug, out existing) ? existing : new DeviceState();
                }
                states.Clear();
                foreach (KeyValuePair<string, DeviceState> pair in kept)
                {
                    states[pair.Key] = pair.Value;
                }
                needsRedraw = true;
            }

            foreach (string line in controller.LastDiagnostics)
            {
                logger.LogInformation("Device detail {Detail}", line);
            }
            logger.LogInformation("Connected to iCUE ({Protocol}), {Count} device(s): {Models}",
                controller.ProtocolSummary, found.Count, string.Join(", ", found.ConvertAll(d => d.Model)));

            if (listChanged)
            {
                Action handler = DevicesChanged;
                if (handler != null) { handler(); }
            }
            return true;
        }

        /// <summary>
        /// Raises <see cref="StateChanged"/> for each slug given.
        /// </summary>
        public void NotifyChanged(IEnumerable<string> slugs)
        {
            Action<string> handler = StateChanged;
            if (handler == null)
            {
                return;
            }
            foreach (string slug in slugs)
            {
                handler(slug);
            }
            handler(AllSlug);
        }

        private static async Task Delay(int milliseconds, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
