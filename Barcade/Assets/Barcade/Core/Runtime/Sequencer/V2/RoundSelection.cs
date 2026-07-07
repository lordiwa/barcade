using Barcade.Core.Content;

namespace Barcade.Core.Sequencer.V2
{
    /// <summary>
    /// The outcome of one <see cref="MicrogameSelector"/> draw: the chosen v2
    /// definition, the §9.1 difficulty multiplier for the round, and whether the
    /// §6.4 anti-frustration bias participated in the draw (for telemetry and
    /// tests — the bias is a selection weight, never visible in gameplay).
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class RoundSelection
    {
        /// <summary>The selected definition.</summary>
        public readonly MicrogameDefinitionV2 Definition;

        /// <summary>GDD §9.1: D(r) = 1 + 0.06(r-1); the climax uses D_final = 1.5 fixed.</summary>
        public readonly float DifficultyMultiplier;

        /// <summary>Seat whose two-consecutive-4th trigger biased this draw (GDD §6.4); -1 when no bias engaged.</summary>
        public readonly int BiasSeat;

        /// <summary>Mechanic the bias favored (the seat's best in-session, drawn from the eligible set); null when no bias engaged.</summary>
        public readonly string BiasMechanic;

        public RoundSelection(MicrogameDefinitionV2 definition, float difficultyMultiplier, int biasSeat, string biasMechanic)
        {
            Definition = definition;
            DifficultyMultiplier = difficultyMultiplier;
            BiasSeat = biasSeat;
            BiasMechanic = biasMechanic;
        }
    }
}
