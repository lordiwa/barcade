using System;

namespace Barcade.Core
{
    /// <summary>
    /// Per-tick serializable snapshot of the session FSM (GDD §2.1 invariant:
    /// "el estado se serializa por tick", feeding the §13 replay pipeline). A
    /// plain value type with only unmanaged fields — capture allocates nothing
    /// and the struct can be written to a replay stream verbatim.
    /// Produced by <see cref="SessionStateMachine.Capture"/>.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public struct SessionStateSnapshot : IEquatable<SessionStateSnapshot>
    {
        /// <summary>Session tick counter at capture time (60 Hz, counts Tick calls since construction).</summary>
        public int Tick;

        /// <summary>The FSM state at capture time.</summary>
        public SessionState State;

        /// <summary>Whole ticks spent in <see cref="State"/> so far (0 on the entering tick).</summary>
        public int StateElapsedTicks;

        /// <summary>Completed-rounds counter (0-based index of the current round while in the round loop).</summary>
        public int RoundIndex;

        /// <summary>Bit i set = seat i has claimed its color this session (bit 0 = Rojo … bit 3 = Verde).</summary>
        public byte JoinedSeatsMask;

        /// <summary>The wrapped <see cref="RoundPhaseMachine"/>'s phase, for MG sub-phase replay fidelity.</summary>
        public PhaseKind RoundPhase;

        public bool Equals(SessionStateSnapshot other)
            => Tick == other.Tick
            && State == other.State
            && StateElapsedTicks == other.StateElapsedTicks
            && RoundIndex == other.RoundIndex
            && JoinedSeatsMask == other.JoinedSeatsMask
            && RoundPhase == other.RoundPhase;

        public override bool Equals(object obj)
            => obj is SessionStateSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Tick;
                h = (h * 397) ^ (int)State;
                h = (h * 397) ^ StateElapsedTicks;
                h = (h * 397) ^ RoundIndex;
                h = (h * 397) ^ JoinedSeatsMask;
                h = (h * 397) ^ (int)RoundPhase;
                return h;
            }
        }
    }
}
