using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Captures the per-player win/lose outcome of a single microgame round.
    ///
    /// Constructed with the participating slot set; outcomes default to loss (false).
    /// Call <see cref="SetOutcome"/> during or after evaluation; then read
    /// <see cref="IsWin"/> to inspect each player's result.
    ///
    /// <see cref="Participants"/> returns a defensive copy — external mutation
    /// of the returned array has no effect on the internal state.
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class MicrogameResult
    {
        private readonly PlayerSlot[] _participants;
        private readonly Dictionary<PlayerSlot, bool> _outcomes;

        /// <summary>
        /// Creates a result for the given set of participating slots.
        /// All outcomes default to <c>false</c> (loss).
        /// </summary>
        public MicrogameResult(PlayerSlot[] participants)
        {
            if (participants == null) throw new ArgumentNullException("participants");

            _participants = (PlayerSlot[])participants.Clone();
            _outcomes = new Dictionary<PlayerSlot, bool>(_participants.Length);

            foreach (PlayerSlot slot in _participants)
                _outcomes[slot] = false;
        }

        /// <summary>
        /// A defensive copy of the participating slot set in construction order.
        /// </summary>
        public PlayerSlot[] Participants => (PlayerSlot[])_participants.Clone();

        /// <summary>
        /// Records the outcome for a participating slot.
        /// </summary>
        /// <param name="slot">The player slot.</param>
        /// <param name="win"><c>true</c> = win, <c>false</c> = loss.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="slot"/> is not in the participant set.
        /// </exception>
        public void SetOutcome(PlayerSlot slot, bool win)
        {
            if (!_outcomes.ContainsKey(slot))
                throw new ArgumentException("Slot " + slot + " is not a participant.", "slot");

            _outcomes[slot] = win;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="slot"/> won this round.
        /// Returns <c>false</c> for both a loss and a non-participant slot.
        /// </summary>
        public bool IsWin(PlayerSlot slot)
        {
            bool win;
            return _outcomes.TryGetValue(slot, out win) && win;
        }
    }
}
