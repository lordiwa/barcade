using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Captures the per-player win/lose outcome of a single microgame round.
    ///
    /// Build phase: construct, call <see cref="SetOutcome"/> for each participant,
    /// then call <see cref="Freeze"/> to make the result effectively immutable.
    /// After <see cref="Freeze"/>, any further <see cref="SetOutcome"/> call throws
    /// <see cref="InvalidOperationException"/>. <see cref="IsWin"/> is always readable.
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
        private bool _frozen;

        /// <summary>
        /// Creates a result for the given set of participating slots.
        /// All outcomes default to <c>false</c> (loss). Not yet frozen.
        /// </summary>
        public MicrogameResult(PlayerSlot[] participants)
        {
            if (participants == null) throw new ArgumentNullException("participants");

            _participants = (PlayerSlot[])participants.Clone();
            _outcomes = new Dictionary<PlayerSlot, bool>(_participants.Length);

            foreach (PlayerSlot slot in _participants)
                _outcomes[slot] = false;

            _frozen = false;
        }

        /// <summary>
        /// A defensive copy of the participating slot set in construction order.
        /// </summary>
        public PlayerSlot[] Participants => (PlayerSlot[])_participants.Clone();

        /// <summary>
        /// Whether this result has been frozen (made immutable) by the producing microgame.
        /// </summary>
        public bool IsFrozen => _frozen;

        /// <summary>
        /// Records the outcome for a participating slot.
        /// </summary>
        /// <param name="slot">The player slot.</param>
        /// <param name="win"><c>true</c> = win, <c>false</c> = loss.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Freeze"/> has already been called.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="slot"/> is not in the participant set.
        /// </exception>
        public void SetOutcome(PlayerSlot slot, bool win)
        {
            if (_frozen)
                throw new InvalidOperationException(
                    "MicrogameResult is frozen and cannot be mutated.");

            if (!_outcomes.ContainsKey(slot))
                throw new ArgumentException("Slot " + slot + " is not a participant.", "slot");

            _outcomes[slot] = win;
        }

        /// <summary>
        /// Freezes this result, preventing any further <see cref="SetOutcome"/> calls.
        /// Called by the microgame at the end of <c>Evaluate()</c> before returning
        /// the result to the sequencer. Idempotent — calling twice is safe.
        /// </summary>
        public void Freeze()
        {
            _frozen = true;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="slot"/> won this round.
        /// Returns <c>false</c> for both a loss and a non-participant slot.
        /// Always readable regardless of frozen state.
        /// </summary>
        public bool IsWin(PlayerSlot slot)
        {
            bool win;
            return _outcomes.TryGetValue(slot, out win) && win;
        }
    }
}
