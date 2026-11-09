using CUESDK;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

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
        private bool wantsInFront;

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
                LastDiagnostics.Clear();
                if (!IsConnected)
                {
                    return devices;
                }

                int count = CorsairLightingSDK.GetDeviceCount();
                for (int index = 0; index < count; index++)
                {
                    CorsairDeviceInfo info = CorsairLightingSDK.GetDeviceInfo(index);
                    CorsairLedPositions positions = CorsairLightingSDK.GetLedPositionsByDeviceIndex(index);
                    // Both of these are structs, so only the LED array can actually be missing.
                    if (positions.LedPosition == null || positions.LedPosition.Length == 0)
                    {
                        continue;
                    }

                    IcueLed[] leds = BuildLeds(positions.LedPosition);
                    string model = string.IsNullOrWhiteSpace(info.Model) ? "Device " + index : info.Model.Trim();
                    devices.Add(new IcueDevice(index, model, info.DeviceId, leds, IcueDevice.Slugify(model)));
                    LastDiagnostics.Add(Describe(index, model, info, positions.LedPosition));
                }
                IcueDevice.MakeSlugsUnique(devices);
                return devices;
            }
        }

        /// <summary>
        /// One line per device describing exactly what the SDK reported, written to the log after
        /// every enumeration. This is what tells us whether a zone we cannot light is missing from
        /// the SDK or merely mishandled here.
        /// </summary>
        public List<string> LastDiagnostics { get; } = new List<string>();

        /// <summary>
        /// Describes a device without assuming which fields the SDK wrapper exposes, so that the
        /// log stays useful whatever the wrapper version.
        /// </summary>
        private static string Describe(int index, string model, CorsairDeviceInfo info, CorsairLedPosition[] positions)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('[').Append(index).Append("] ").Append(model);
            builder.Append(" | reported by the SDK: ");

            Type type = info.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Append(builder, field.Name, SafeValue(field, info));
            }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0)
                {
                    Append(builder, property.Name, SafeValue(property, info));
                }
            }

            builder.Append("| positions returned: ").Append(positions.Length);
            if (positions.Length > 0 && positions.Length <= 40)
            {
                builder.Append(" (");
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i > 0) { builder.Append(", "); }
                    builder.Append(positions[i].LedId);
                }
                builder.Append(')');
            }
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string name, string value)
        {
            if (value != null)
            {
                builder.Append(name).Append('=').Append(value).Append(' ');
            }
        }

        private static string SafeValue(FieldInfo field, object target)
        {
            try { object value = field.GetValue(target); return value == null ? "null" : value.ToString(); }
            catch (Exception) { return null; }
        }

        private static string SafeValue(PropertyInfo property, object target)
        {
            try { object value = property.GetValue(target); return value == null ? "null" : value.ToString(); }
            catch (Exception) { return null; }
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
                wantsInFront = inFront;
                if (!IsConnected || inFront == isInFront)
                {
                    return;
                }
                CorsairLightingSDK.SetLayerPriority(inFront ? LayerInFront : LayerBehind);
                isInFront = inFront;
            }
        }

        /// <summary>
        /// Sets the layer again even when nothing changed on our side. Restarting iCUE wipes the
        /// priority we asked for, and nothing tells us it happened, so it is simply re-applied
        /// from time to time.
        /// </summary>
        public void ReassertLayer()
        {
            lock (sync)
            {
                if (!IsConnected)
                {
                    return;
                }
                CorsairLightingSDK.SetLayerPriority(wantsInFront ? LayerInFront : LayerBehind);
                isInFront = wantsInFront;
            }
        }

        /// <summary>
        /// Checks iCUE is still answering, so that closing it is noticed even while idle.
        /// </summary>
        /// <returns><c>true</c> when iCUE is still there.</returns>
        public bool CheckAlive()
        {
            lock (sync)
            {
                if (!IsConnected)
                {
                    return false;
                }
                CorsairProtocolDetails details = CorsairLightingSDK.PerformProtocolHandshake();
                if (details.ServerProtocolVersion == 0)
                {
                    MarkLost("iCUE stopped answering");
                    return false;
                }
                return true;
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
                        MarkLost("writing to " + device.Model + " failed");
                        return false;
                    }
                }
                if (!CorsairLightingSDK.SetLedsColorsFlushBuffer())
                {
                    MarkLost("flushing the colours failed");
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
        /// Any refused call means the link to iCUE is no longer trustworthy, most often because it
        /// was restarted. Rather than guess which errors are recoverable, the connection is dropped
        /// and built again, which also picks up devices that moved in the meantime.
        /// </summary>
        private void MarkLost(string reason)
        {
            IsConnected = false;
            isInFront = false;
            ProtocolSummary = reason + " (" + CorsairLightingSDK.GetLastError() + ")";
        }

        /// <summary>Last error reported by the SDK, for the log.</summary>
        public string LastError()
        {
            return CorsairLightingSDK.GetLastError().ToString();
        }
    }
}
