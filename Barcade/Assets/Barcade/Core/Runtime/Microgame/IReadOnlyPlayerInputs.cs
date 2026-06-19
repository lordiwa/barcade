namespace Barcade.Core
{
    /// <summary>
    /// Read-only view of all players' input for a single Tick call.
    /// Each call to <see cref="For"/> returns an immutable <see cref="InputSnapshot"/>
    /// for the requested player slot.
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// The Unity-side adapter that populates this from PlayerInput lives in Barcade.Framework.
    /// </summary>
    public interface IReadOnlyPlayerInputs
    {
        /// <summary>
        /// Returns the input snapshot for <paramref name="slot"/> this tick.
        /// </summary>
        InputSnapshot For(PlayerSlot slot);
    }
}
