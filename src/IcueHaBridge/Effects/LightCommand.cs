namespace IcueHaBridge.Effects
{
    /// <summary>
    /// A change asked for by Home Assistant. Anything left null is kept as it was.
    /// </summary>
    public sealed class LightCommand
    {
        public bool? IsOn { get; set; }
        public byte? R { get; set; }
        public byte? G { get; set; }
        public byte? B { get; set; }
        public int? Brightness { get; set; }
        public string Effect { get; set; }
    }
}
