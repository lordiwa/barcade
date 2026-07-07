namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The v2 microgame contract (GDD §10.2, Annex D.2). Lives alongside the
    /// existing <see cref="Barcade.Core.IMicrogame"/> (v1) — the two are
    /// deliberately NOT unified in this ticket. See "Contract decision" below.
    ///
    /// <para>
    /// <b>Contract decision (T-103, delegated by the orchestrator; reaffirmed by
    /// human directive: the GDD is canonical — code adapts to it, not vice versa).</b>
    /// GDD §10.2 specifies <c>Initialize(MicrogameDefinition, SeededRandom, PlayerRoster, float)</c>
    /// and <c>Tick(in InputSnapshot)</c>. Evolving the existing v1 <c>IMicrogame</c> in
    /// place to add <c>IsFinished</c> / <c>MicrogameId</c> / the new result and render
    /// types would force changes to all 6 existing implementers (EsquivaMicrogame,
    /// AporreaMicrogame, ApuntaMicrogame, TimingMicrogame, RecolectaMicrogame,
    /// SampleTapMicrogame) and to <c>SequencerDirector</c>'s calling convention, which
    /// does not poll <c>IsFinished</c> today — explicitly out of scope ("do NOT rewrite
    /// existing mechanics"). So this is a NEW interface in a new namespace — REACCIONA
    /// is its first implementation; old mechanics migrate later under GDD T-107. The
    /// v1 <c>IMicrogame</c>/<c>InputSnapshot</c>/<c>SequencerDirector</c> are untouched
    /// and keep compiling; they do not shape this API.
    /// </para>
    ///
    /// <para>
    /// This interface matches the literal D.2 signature with exactly one deliberate
    /// omission: the <c>MicrogameDefinition</c> parameter is dropped from
    /// <see cref="Initialize"/>. T-106 (the v2 <c>MicrogameDefinition</c> data
    /// migration) has not landed, and the orchestrator's params note for this ticket
    /// says to take mechanic-specific tuning via a plain params POCO instead —
    /// concretely, <see cref="Barcade.Core.Microgames.V2.ReaccionaMicrogame"/> takes
    /// its <c>ReaccionaParams</c> via its own constructor. Every other parameter and
    /// the <see cref="Tick"/> signature match the GDD literally: <c>SeededRandom</c>
    /// (the concrete class, not an interface) and this namespace's own session-level
    /// <see cref="InputSnapshot"/> (<c>{ int Tick; PlayerInput[] Players; }</c>, GDD
    /// §3.2) — not the v1 per-seat <c>Barcade.Core.InputSnapshot</c>.
    /// </para>
    ///
    /// <b>Contract rules (GDD §10.2):</b> <see cref="Tick"/> allocates no heap memory
    /// in steady state (zero GC in gameplay, §14); <see cref="GetRenderState"/> returns
    /// plain data, never UnityEngine references; all randomness flows through the
    /// injected RNG.
    /// </summary>
    public interface IMicrogame
    {
        MicrogameId Id { get; }

        /// <summary>
        /// Initializes the microgame for a new round. Called once before the first
        /// <see cref="Tick"/>.
        /// </summary>
        /// <param name="rng">Deterministic RNG seeded per-round by the sequencer.</param>
        /// <param name="roster">Which of the 4 seats are occupied and by whom.</param>
        /// <param name="difficultyMult">Session difficulty multiplier (GDD §9.1); 1.0 = baseline.</param>
        void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult);

        /// <summary>Advances exactly one simulation tick (60 Hz). Pure — no external side effects.</summary>
        void Tick(in InputSnapshot input);

        /// <summary>True once the round has concluded and <see cref="GetResult"/> may be called.</summary>
        bool IsFinished { get; }

        /// <summary>Ranking or coop outcome. Valid only once <see cref="IsFinished"/> is true.</summary>
        MicrogameResult GetResult();

        /// <summary>POCO consumed by the Framework's presenter. Same instance every call — fields are overwritten in place.</summary>
        RenderState GetRenderState();
    }
}
