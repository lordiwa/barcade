using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_08 bot policy. The ideal decision is trivial and mode-blind:
    /// hold the switch continuously (<c>Stick=None, Button=true</c>) — seeing
    /// or not seeing the countdown (<see cref="SujetaMode.InfoAsym"/>) changes
    /// nothing about a bot's OPTIMAL action here, since "hold continuously"
    /// dominates any strategy that depends on timing the release (the info
    /// asymmetry exists to force HUMAN verbal coordination, GDD P2 — bots have
    /// no analogous coordination cost). Humanization is exactly the shared
    /// pattern every other <c>*BotPolicy</c> uses:
    /// <see cref="BotSkill.ErrorRate"/> rolls a per-tick "drop" (releases
    /// instead of holding) and <see cref="BotSkill.ReactionDelayTicksMean"/>
    /// delays the (possibly-dropped) decision through a
    /// <see cref="ReactionDelayBuffer"/>. <see cref="BotSkill.MashHzMean"/> is
    /// unused (no mash channel here).
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class SujetaBotPolicy : IBotPolicy
    {
        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();
        private int? _sampledDelayTicks;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            bool idealHold = !BotSkillSampler.RollError(skill, rng);

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(Direction8.None, idealHold), _sampledDelayTicks.Value);
        }
    }
}
