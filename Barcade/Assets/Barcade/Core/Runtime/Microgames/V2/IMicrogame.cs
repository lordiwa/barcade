using Barcade.Core;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The v2 microgame contract (GDD §10.2, Annex D.2). Lives alongside the
    /// existing <see cref="Barcade.Core.IMicrogame"/> (v1) — the two are
    /// deliberately NOT unified in this ticket. See "Contract decision" below.
    ///
    /// <para>
    /// <b>Contract decision (T-103, delegated by the orchestrator):</b> GDD §10.2
    /// specifies <c>Initialize(MicrogameDefinition, SeededRandom, PlayerRoster, float)</c>
    /// and <c>Tick(in InputSnapshot)</c> where that <c>InputSnapshot</c> is the
    /// session-level <c>{ int Tick; PlayerInput[] Players; }</c> struct described in
    /// GDD §3.2 — a type that does not exist in Core (see the delta already
    /// documented on <c>Barcade.Core.InputInterpreter</c>: the real
    /// <see cref="Barcade.Core.InputSnapshot"/> is per-seat, not a session wrapper).
    /// Evolving the existing v1 <c>IMicrogame</c> in place to add <c>IsFinished</c> /
    /// <c>MicrogameId</c> / the new result and render types would force changes to
    /// all 6 existing implementers (EsquivaMicrogame, AporreaMicrogame, ApuntaMicrogame,
    /// TimingMicrogame, RecolectaMicrogame, SampleTapMicrogame) and to
    /// <c>SequencerDirector</c>'s calling convention, which does not poll
    /// <c>IsFinished</c> today. That is explicitly out of scope for this ticket
    /// ("do NOT rewrite existing mechanics"). So this is a NEW interface in a new
    /// namespace — REACCIONA is its first implementation; old mechanics migrate
    /// later under GDD T-107. Two further, smaller adaptations from the literal
    /// D.2 signature, made for the same "smallest blast radius, reuse what
    /// exists" reason:
    /// <list type="bullet">
    /// <item><description>
    /// <c>Tick</c> takes <see cref="IReadOnlyPlayerInputs"/> (the existing per-seat
    /// callback interface every current microgame and <c>InputInterpreter</c>
    /// already consume) instead of a nonexistent session-level <c>InputSnapshot</c>.
    /// </description></item>
    /// <item><description>
    /// <c>Initialize</c> takes <see cref="ISeededRandom"/> (the existing interface
    /// <c>IMicrogameContext.Rng</c> already uses, for testability/mocking) instead
    /// of the concrete <c>SeededRandom</c> class, and drops the <c>MicrogameDefinition</c>
    /// parameter entirely — T-106 (the v2 data migration) has not landed, and the
    /// orchestrator's params note for this ticket says to take mechanic-specific
    /// tuning via a plain params POCO instead. Concretely, <c>ReaccionaMicrogame</c>
    /// takes its <c>ReaccionaParams</c> via its own constructor, mirroring how
    /// v1 mechanics (e.g. <c>EsquivaMicrogame</c>) take mechanic-specific config
    /// via their constructor rather than via the shared <c>Prepare(ctx)</c> call.
    /// </description></item>
    /// </list>
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
        void Initialize(ISeededRandom rng, PlayerRoster roster, float difficultyMult);

        /// <summary>Advances exactly one simulation tick (60 Hz). Pure — no external side effects.</summary>
        void Tick(IReadOnlyPlayerInputs inputs);

        /// <summary>True once the round has concluded and <see cref="GetResult"/> may be called.</summary>
        bool IsFinished { get; }

        /// <summary>Ranking or coop outcome. Valid only once <see cref="IsFinished"/> is true.</summary>
        MicrogameResult GetResult();

        /// <summary>POCO consumed by the Framework's presenter. Same instance every call — fields are overwritten in place.</summary>
        RenderState GetRenderState();
    }
}
