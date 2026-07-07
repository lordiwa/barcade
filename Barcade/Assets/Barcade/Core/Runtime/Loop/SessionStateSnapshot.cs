namespace Barcade.Core
{
    /// <summary>
    /// A per-tick, serializable POCO capturing everything needed to compare two
    /// session runs for determinism (GDD §13 replay: same (seed, input sequence)
    /// -> identical trace). A plain struct rather than a MiniJson-serialized blob
    /// (TASK-030 delivered MiniJson, and it would work fine here) — a struct trace
    /// satisfies the AC's literal "serializable POCO" requirement with far less
    /// machinery for what this ticket's determinism test actually needs (structural
    /// equality across two in-memory traces, not persistence or wire transport);
    /// nothing prevents a future ticket from JSON-encoding a sequence of these if an
    /// actual on-disk replay file (GDD Annex D.4 <c>.bcrp</c>) is ever built.
    /// </summary>
    public readonly struct SessionStateSnapshot
    {
        /// <summary>Global tick counter — advances by 1 every <see cref="SessionStateMachine.Tick"/> call, never reset.</summary>
        public readonly int Tick;

        public readonly SessionPhase Phase;

        /// <summary>0-based index of the round currently in progress (or most recently completed).</summary>
        public readonly int RoundIndex;

        /// <summary>Number of seats that have claimed a color so far (meaningful during/after Join).</summary>
        public readonly int ReadyCount;

        public SessionStateSnapshot(int tick, SessionPhase phase, int roundIndex, int readyCount)
        {
            Tick = tick;
            Phase = phase;
            RoundIndex = roundIndex;
            ReadyCount = readyCount;
        }
    }
}
