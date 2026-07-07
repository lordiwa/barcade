namespace Barcade.Core
{
    /// <summary>
    /// Named per-subsystem RNG streams (GDD §13: "deriva streams independientes
    /// por subsistema (spawner, tablero, sorteos)"). Used with
    /// <see cref="SeededRandom.Derive"/>; each value selects an independent PCG32
    /// stream, so consuming randomness in one subsystem never shifts another.
    ///
    /// Values are part of the replay format — append new streams, never renumber.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public enum RngStream
    {
        /// <summary>Session-level decisions (join order effects, schedule draws).</summary>
        Session = 0,

        /// <summary>Hazard/entity spawning inside microgames.</summary>
        Spawner = 1,

        /// <summary>Board movement/tile effects (T-113).</summary>
        Board = 2,

        /// <summary>Sorteos: microgame selection, star draws, event draws.</summary>
        Draws = 3,

        /// <summary>Bot decisions: BotSkill humanizer sampling (GDD Annex D.3, T-110) — reaction delay/mash draws and per-decision error rolls.</summary>
        Bots = 4,
    }
}
