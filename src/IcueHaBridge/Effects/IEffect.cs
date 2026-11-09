using IcueHaBridge.Icue;

namespace IcueHaBridge.Effects
{
    /// <summary>
    /// Draws one frame onto the LEDs of a device.
    /// </summary>
    public interface IEffect
    {
        /// <summary>Name shown in the Home Assistant effect list.</summary>
        string Name { get; }

        /// <summary>Whether this effect changes over time and therefore needs the render loop.</summary>
        bool IsAnimated { get; }

        void Render(IcueDevice device, EffectFrame frame);
    }
}
