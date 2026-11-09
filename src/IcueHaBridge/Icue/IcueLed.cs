using CUESDK;

namespace IcueHaBridge.Icue
{
    /// <summary>
    /// One LED of a device, with where it sits on that device so that effects can travel across it.
    /// </summary>
    public sealed class IcueLed
    {
        public CorsairLedId LedId { get; }

        /// <summary>Horizontal position within the device, from 0 on the left to 1 on the right.</summary>
        public double X { get; internal set; }

        /// <summary>Vertical position within the device, from 0 at the top to 1 at the bottom.</summary>
        public double Y { get; internal set; }

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        internal IcueLed(CorsairLedId ledId, double x, double y)
        {
            LedId = ledId;
            X = x;
            Y = y;
        }
    }
}
