using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Tracks per-player wins, losses, and games played across microgame rounds.
    ///
    /// Design constraints (GDD law-of-large-numbers / variance design):
    ///   - Raw integer counters only — no bonus multipliers, no hidden randomness.
    ///   - WinRate = wins / gamesPlayed (0 when no games played, never throws).
    ///   - GetLeaders returns ALL players tied at the highest win count.
    ///   - GetSingleLeader returns a deterministic pick among tied leaders
    ///     (lowest PlayerSlot enum value = lowest integer index wins the tie).
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner and Unity EditMode.
    /// C# 9 compatible (Unity 6 / 6000.0.38f1).
    /// </summary>
    public sealed class ScoreModel
    {
        // ── Private state ────────────────────────────────────────────────────────

        private const int SlotCount = 4;

        // Index by (int)PlayerSlot — 0=Rojo, 1=Azul, 2=Amarillo, 3=Verde
        private readonly int[] _wins        = new int[SlotCount];
        private readonly int[] _losses      = new int[SlotCount];
        private readonly int[] _gamesPlayed = new int[SlotCount];

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Records the outcome of one microgame round for all four players.
        /// </summary>
        /// <param name="results">
        /// A bool[4] indexed by <c>(int)PlayerSlot</c>.
        /// <c>true</c> = win, <c>false</c> = loss.
        /// </param>
        public void RecordRound(bool[] results)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _gamesPlayed[i]++;
                if (results[i])
                    _wins[i]++;
                else
                    _losses[i]++;
            }
        }

        /// <returns>Total wins for <paramref name="slot"/>.</returns>
        public int GetWins(PlayerSlot slot) => _wins[(int)slot];

        /// <returns>Total losses for <paramref name="slot"/>.</returns>
        public int GetLosses(PlayerSlot slot) => _losses[(int)slot];

        /// <returns>Total rounds played for <paramref name="slot"/>.</returns>
        public int GetGamesPlayed(PlayerSlot slot) => _gamesPlayed[(int)slot];

        /// <summary>
        /// Win rate for <paramref name="slot"/> in the range [0, 1].
        /// Returns 0 when no games have been played (no division-by-zero).
        /// </summary>
        public float WinRate(PlayerSlot slot)
        {
            int games = _gamesPlayed[(int)slot];
            if (games == 0) return 0f;
            return (float)_wins[(int)slot] / games;
        }

        /// <summary>
        /// Returns all players whose win count equals the highest win count.
        /// When all players are tied (including zero wins) all four slots are returned.
        /// The list is ordered by ascending <see cref="PlayerSlot"/> value (Rojo first).
        /// </summary>
        public List<PlayerSlot> GetLeaders()
        {
            int maxWins = 0;
            for (int i = 0; i < SlotCount; i++)
                if (_wins[i] > maxWins) maxWins = _wins[i];

            var result = new List<PlayerSlot>(SlotCount);
            for (int i = 0; i < SlotCount; i++)
                if (_wins[i] == maxWins) result.Add((PlayerSlot)i);

            return result;
        }

        /// <summary>
        /// Returns a single leader, deterministically.
        /// Tie-break rule: lowest <see cref="PlayerSlot"/> integer value among tied leaders.
        /// Never throws — always returns a valid slot.
        /// </summary>
        public PlayerSlot GetSingleLeader()
        {
            // GetLeaders is already sorted ascending by slot index; first element wins the tie.
            List<PlayerSlot> leaders = GetLeaders();
            return leaders[0];
        }
    }
}
