namespace IcueHaBridge.Effects
{
    /// <summary>
    /// What Home Assistant asked for on one device.
    /// </summary>
    public sealed class DeviceState
    {
        /// <summary>Whether this program is lighting the device. When false, iCUE shows through.</summary>
        public bool IsOn { get; set; }

        /// <summary>Base colour at full intensity; the brightness is applied on top when rendering.</summary>
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 255;

        /// <summary>Brightness between 1 and 255, as Home Assistant counts it.</summary>
        public int Brightness { get; set; } = 255;

        /// <summary>Name of the effect, or "None" for a plain colour.</summary>
        public string Effect { get; set; } = EffectCatalog.None;

        public DeviceState Clone()
        {
            return new DeviceState
            {
                IsOn = IsOn,
                R = R,
                G = G,
                B = B,
                Brightness = Brightness,
                Effect = Effect
            };
        }
    }
}
