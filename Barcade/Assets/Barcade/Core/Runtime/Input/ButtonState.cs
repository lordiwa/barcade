namespace Barcade.Core
{
    /// <summary>
    /// The state of the single action button for one player during a single frame.
    /// </summary>
    public enum ButtonState
    {
        /// <summary>Button was not pressed and is not being held.</summary>
        Released = 0,

        /// <summary>Button transitioned from not-pressed to pressed this frame.</summary>
        Pressed = 1,

        /// <summary>Button is held down (was pressed in a previous frame).</summary>
        Held = 2,
    }
}
