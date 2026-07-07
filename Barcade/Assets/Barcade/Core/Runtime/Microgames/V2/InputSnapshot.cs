using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Session-level input for a single simulation tick (GDD §3.2, §10.2 listing):
    /// <c>{ int Tick; PlayerInput[] Players; }</c>, length 4, indexed like
    /// <c>(int)PlayerSlot</c>.
    ///
    /// Distinct from the per-seat <see cref="Barcade.Core.InputSnapshot"/> (v1) that
    /// <see cref="Barcade.Core.IReadOnlyPlayerInputs"/>/<see cref="Barcade.Core.InputInterpreter"/>
    /// consume — the two coexist for the same reason the v1/v2 <c>IMicrogame</c>
    /// contracts coexist (see that interface's doc). A v2 <see cref="IMicrogame"/>
    /// still gets T-101's debounce/edge detection: it bridges this session-level
    /// snapshot into the per-seat shape internally (see <c>ReaccionaMicrogame</c>'s
    /// private input bridge) rather than duplicating InputInterpreter's logic.
    /// </summary>
    public readonly struct InputSnapshot
    {
        public readonly int Tick;

        /// <summary>Always length 4.</summary>
        public readonly PlayerInput[] Players;

        public InputSnapshot(int tick, PlayerInput[] players)
        {
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (players.Length != 4) throw new ArgumentException("InputSnapshot requires exactly 4 players.", nameof(players));

            Tick = tick;
            Players = players;
        }
    }
}
