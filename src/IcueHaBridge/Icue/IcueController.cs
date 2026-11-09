using CUESDK;
using System;
using System.Collections.Generic;

namespace IcueHaBridge.Icue
{
    /// <summary>
    /// Everything this program does to the hardware goes through here: the handshake with iCUE,
    /// listing the devices, and writing LED colours.
    /// </summary>
    public sealed class IcueController
    {
        /// <summary>
        /// Layer the colours of this program are drawn on. iCUE draws its own lighting on layer 127,
        /// so a higher number puts us in front of it.
        /// </summary>
        private const int LayerInFront = 130;

        /// <summary>
        /// Layer used when nothing is active, which puts this program behind iCUE and gives it the
        /// lighting back without having to stop the service.
        /// </summary>
        private const int LayerBehind = 126;

        private readonly object sync = new object();
        private bool isInFront;

        /// <summary>Whether iCUE answered the last handshake.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Readable version information, for the log.</summary>
        public string ProtocolSummary { get; private set; } = "not connected";

        /// <summary>
        /// Performs the handshake with iCUE.
        /// </summary>
        /// <returns><c>true</c> when iCUE answered.</returns>
        /// <exception cref="InvalidOperationException">The SDK and iCUE versions are incompatible.</exception>
        public bool Connect()
        {
            lock (sync)
            {
                CorsairProtocolDetails details = CorsairLightingSDK.PerformProtocolHandshake();
                IsConnected = details.ServerProtocolVersion != 0;
                ProtocolSummary = IsConnected
                    ? "SDK " + details.SdkVersion + ", iCUE " + details.ServerVersion
                    : "iCUE did not answer";
                if (IsConnected && details.BreakingChanges)
                {
                    IsConnected = false;
                    throw new InvalidOperationException(
                        "This SDK version (" + details.SdkVersion + ") is not compatible with iCUE "
                        + details.ServerVersion + ".");
                }
                // The layer is set again after every (re)connection, because iCUE forgets it.
                isInFront = false;
                return IsConnected;
            }
        }

        /// <summary>
        /// Lists the devices and where each of their LEDs sits, normalised per device so that
        /// effects can travel from one side to the other whatever the shape of the device.
        /// </summary>
        public List<IcueDevice> Enumerate()
        {
            lock (sync)
            {
                List<IcueDevice> devices = new List<IcueDevice>();
                if (!IsConnected)
                {
                    return devices;
                }

                int count = CorsairLightingSDK.GetDeviceCount();
                for (int index = 0; index < count; index++)
                {
                    CorsairDeviceInfo info = CorsairLightingSDK.GetDeviceInfo(index);
                    CorsairLedPositions positions = CorsairLightingSDK.GetLedPositionsByDeviceIndex(index);
                    if (info == null || positions == null || positions.LedPosition == null || positions.LedPosition.Length == 0)
                    {
                        continue;
                    }

                    IcueLed[] leds = BuildLeds(positions.LedPosition);
                    string model = string.IsNullOrWhiteSpace(info.Model) ? "Device " + index : info.Model.Trim();
                    devices.Add(new IcueDevice(index, model, info.DeviceId, leds, IcueDevice.Slugify(model)));
                }
                IcueDevice.MakeSlugsUnique(devices);
                return devices;
            }
        }

        /// <summary>
        /// Converts the raw LED rectangles into positions between 0 and 1 across the device.
        /// </summary>
        private static IcueLed[] BuildLeds(CorsairLedPosition[] positions)
        {
            int length = positions.Length;
            double[] centreX = new double[length];
            double[] centreY = new double[length];
            double minimumX = double.MaxValue, maximumX = double.MinValue;
            double minimumY = double.MaxValue, maximumY = double.MinValue;

            for (int i = 0; i < length; i++)
            {
                centreX[i] = positions[i].Left + (positions[i].Width / 2.0);
                centreY[i] = positions[i].Top + (positions[i].Height / 2.0);
                if (centreX[i] < minimumX) { minimumX = centreX[i]; }
                if (centreX[i] > maximumX) { maximumX = centreX[i]; }
                if (centreY[i] < minimumY) { minimumY = centreY[i]; }
                if (centreY[i] > maximumY) { maximumY = centreY[i]; }
            }

            double spanX = maximumX - minimumX;
            double spanY = maximumY - minimumY;

            IcueLed[] leds = new IcueLed[length];
            for (int i = 0; i < length; i++)
            {
                // A device whose LEDs all report the same position, such as a headset, is spread out
                // evenly by index instead, so that effects still travel across it.
                double x = spanX > 0.0001 ? (centreX[i] - minimumX) / spanX
                                          : (length > 1 ? (double)i / (length - 1) : 0.5);
                double y = spanY > 0.0001 ? (centreY[i] - minimumY) / spanY : 0.5;
                leds[i] = new IcueLed(positions[i].LedId, x, y);
            }
            return leds;
        }

        /// <summary>
        /// Puts this program in front of iCUE, or behind it when nothing is active.
        /// </summary>
        public void SetInFront(bool inFront)
        {
            lock (sync)
            {
                if (!IsConnected || inFront == isInFront)
                {
                    return;
                }
                CorsairLightingSDK.SetLayerPriority(inFront ? LayerInFront : LayerBehind);
                isInFront = inFront;
            }
        }

        /// <summary>
        /// Sends the colours currently held by the devices to the hardware, in one flush.
        /// </summary>
        /// <returns><c>false</c> when the SDK refused, which usually means iCUE went away.</returns>
        public bool Flush(IList<IcueDevice> devices)
        {
            lock (sync)
            {
                if (!IsConnected || devices == null || devices.Count == 0)
                {
                    return true;
                }
                for (int d = 0; d < devices.Count; d++)
                {
                    IcueDevice device = devices[d];
                    CorsairLedColor[] colors = new CorsairLedColor[device.Leds.Length];
                    for (int i = 0; i < device.Leds.Length; i++)
                    {
                        IcueLed led = device.Leds[i];
                        CorsairLedColor color = new CorsairLedColor();
                        color.LedId = led.LedId;
                        color.R = led.R;
                        color.G = led.G;
                        color.B = led.B;
                        colors[i] = color;
                    }
                    if (!CorsairLightingSDK.SetLedsColorsBufferByDeviceIndex(device.Index, colors))
                    {
                        MarkDisconnectedIfServerGone();
                        return false;
                    }
                }
                if (!CorsairLightingSDK.SetLedsColorsFlushBuffer())
                {
                    MarkDisconnectedIfServerGone();
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Reads back what the devices are showing, used once at startup so that Home Assistant
        /// does not report black for lighting that iCUE is happily displaying.
        /// </summary>
        public bool ReadBack(IcueDevice device)
        {
            lock (sync)
            {
                if (!IsConnected || device == null)
                {
                    return false;
                }
                CorsairLedColor[] colors = new CorsairLedColor[device.Leds.Length];
                for (int i = 0; i < device.Leds.Length; i++)
                {
                    CorsairLedColor color = new CorsairLedColor();
                    color.LedId = device.Leds[i].LedId;
                    colors[i] = color;
                }
                if (!CorsairLightingSDK.GetLedsColorsByDeviceIndex(device.Index, ref colors))
                {
                    return false;
                }
                for (int i = 0; i < colors.Length && i < device.Leds.Length; i++)
                {
                    device.Leds[i].R = Clamp(colors[i].R);
                    device.Leds[i].G = Clamp(colors[i].G);
                    device.Leds[i].B = Clamp(colors[i].B);
                }
                return true;
            }
        }

        private static byte Clamp(int value)
        {
            if (value < 0) { return 0; }
            if (value > 255) { return 255; }
            return (byte)value;
        }

        /// <summary>
        /// Notes that iCUE is gone so that the loop stops writing and tries to reconnect.
        /// </summary>
        private void MarkDisconnectedIfServerGone()
        {
            if (CorsairLightingSDK.GetLastError() == CorsairError.ServerNotFound)
            {
                IsConnected = false;
                isInFront = false;
                ProtocolSummary = "iCUE is no longer running";
            }
        }

        /// <summary>Last error reported by the SDK, for the log.</summary>
        public string LastError()
        {
            return CorsairLightingSDK.GetLastError().ToString();
        }
    }
}
