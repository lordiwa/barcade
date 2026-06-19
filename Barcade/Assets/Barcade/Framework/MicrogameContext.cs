using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Concrete implementation of <see cref="IMicrogameContext"/> used by
    /// <see cref="MicrogameHost"/> to pass per-round context to microgame logic.
    ///
    /// Constructed once per round by MicrogameHost before calling Prepare().
    ///
    /// Lives in Barcade.Framework.
    /// </summary>
    internal sealed class MicrogameContext : IMicrogameContext
    {
        public ISeededRandom Rng     { get; }
        public PlayerSlot[]  Players { get; }

        public MicrogameContext(ISeededRandom rng, PlayerSlot[] players)
        {
            Rng     = rng;
            Players = players;
        }
    }
}
