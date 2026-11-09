namespace IcueHaBridge.Effects
{
    /// <summary>
    /// Everything an effect needs to draw one frame.
    /// </summary>
    public sealed class EffectFrame
    {
        /// <summary>Seconds since the service started, which is what makes animations move.</summary>
        public double TimeSeconds { get; set; }

        /// <summary>Base colour chosen in Home Assistant, at full intensity.</summary>
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        /// <summary>Brightness between 0 and 1.</summary>
        public double Brightness { get; set; }
    }
}
