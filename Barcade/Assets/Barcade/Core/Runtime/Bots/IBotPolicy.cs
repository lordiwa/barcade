using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD Annex D.3 literal: one policy per mechanic. Each mechanic's
    /// implementation (<c>EsquivaBotPolicy</c>, <c>MantenBotPolicy</c>,
    /// <c>CorreBotPolicy</c>, <c>ApuntaBotPolicy</c>, <c>ReaccionaBotPolicy</c>)
    /// interprets <see cref="BotView"/> according to that mechanic's own
    /// published <see cref="RenderState"/>/<see cref="HudState"/>
    /// shape — see each implementation's own class doc for exactly which fields
    /// it reads and how it maps a decision to a <see cref="PlayerInput"/>.
    ///
    /// <para>
    /// <b>[ASSUMED] one policy INSTANCE per seat.</b> Several mechanics' optimal
    /// play needs per-seat memory across ticks (Esquiva's wall-hysteresis mode,
    /// Manten's previous-theta finite difference, every mechanic's own sampled
    /// reaction-delay/mash traits and reaction-delay ring buffer — see
    /// <see cref="ReactionDelayBuffer"/>). <see cref="Decide"/>'s signature takes
    /// no seat parameter (it is GDD-literal — see <see cref="BotView"/>'s own
    /// doc), so a policy instance is scoped to exactly one seat for its whole
    /// lifetime; driving 4 seats means constructing 4 instances (one per bot-
    /// controlled seat), each with its own <see cref="BotView.Seat"/> passed in
    /// every <see cref="Decide"/> call. This matches how a real production bot
    /// would be wired (one bot brain per empty/idle seat) and how the
    /// statistical harness drives a round (<c>BotStatisticalHarness</c>).
    /// </para>
    /// </summary>
    public interface IBotPolicy
    {
        /// <summary>
        /// Decides this tick's input for whichever seat <paramref name="view"/>
        /// is scoped to. Every stochastic choice inside an implementation MUST
        /// flow through <paramref name="rng"/> (GDD/T-110: "cada decisión
        /// estocástica del bot fluye por el SeededRandom inyectado") — no
        /// <c>System.Random</c>, no other entropy source, so that (AC2) the same
        /// seed sequence always reproduces the identical
        /// <see cref="Microgames.V2.PlayerInput"/> stream.
        /// </summary>
        PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng);
    }
}
