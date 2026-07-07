using System;

namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The per-session hidden-objective counters behind the GDD §6.3 bonus stars
    /// ("rastreados por telemetría interna de la partida"). Gameplay systems feed
    /// these as events happen; <see cref="WinnerOf"/> resolves each star's holder
    /// at Game Over.
    ///
    /// Holder rules: max-direction stars go to the strictly highest counter,
    /// min-direction to the strictly lowest, among ACTIVE seats only. A tie means
    /// the star has no holder (-1) — [ASSUMED] a tied hidden objective is awarded
    /// to no one; the GDD does not specify tie handling. Gatillo additionally
    /// requires at least one recorded latency sample.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SessionCounters
    {
        private const int SeatCount = 4;

        private readonly int[] _eliminations = new int[SeatCount];
        private readonly int[] _weaponsUsed = new int[SeatCount];
        private readonly float[] _zenMetric = new float[SeatCount];
        private readonly float[] _latencySum = new float[SeatCount];
        private readonly int[] _latencyCount = new int[SeatCount];
        private readonly int[] _coinsInvested = new int[SeatCount];
        private readonly int[] _coinsRobbed = new int[SeatCount];

        /// <summary>Kamikaze: the seat was eliminated in a microgame.</summary>
        public void RecordElimination(int seat) => _eliminations[CheckSeat(seat)]++;

        /// <summary>Cangreja: the seat used an arsenal weapon (§5.4).</summary>
        public void RecordWeaponUse(int seat) => _weaponsUsed[CheckSeat(seat)]++;

        /// <summary>
        /// Zen: accumulates the [ASSUMED] min-direction metric (see
        /// <see cref="StarKind.Zen"/> — the GDD row is truncated at the source).
        /// </summary>
        public void RecordZenMetric(int seat, float amount) => _zenMetric[CheckSeat(seat)] += amount;

        /// <summary>Gatillo: one ¡REACCIONA! reaction latency sample, in milliseconds.</summary>
        public void RecordReaccionaLatency(int seat, float milliseconds)
        {
            _latencySum[CheckSeat(seat)] += milliseconds;
            _latencyCount[seat]++;
        }

        /// <summary>Inversora: coins deposited into an Inversión tile (§5.3).</summary>
        public void RecordInvestment(int seat, int coins) => _coinsInvested[CheckSeat(seat)] += coins;

        /// <summary>Fantasma: coins robbed FROM this seat by rivals (props, weapons).</summary>
        public void RecordRobbed(int seat, int coins) => _coinsRobbed[CheckSeat(seat)] += coins;

        /// <summary>
        /// The seat holding <paramref name="kind"/> among the seats in
        /// <paramref name="activeSeatsMask"/> (bit i = seat i), or -1 when tied /
        /// nobody qualifies.
        /// </summary>
        public int WinnerOf(StarKind kind, int activeSeatsMask)
        {
            switch (kind)
            {
                case StarKind.Kamikaze: return Extreme(activeSeatsMask, seat => _eliminations[seat], max: true, s => true);
                case StarKind.Cangreja: return Extreme(activeSeatsMask, seat => _weaponsUsed[seat], max: true, s => true);
                case StarKind.Zen: return Extreme(activeSeatsMask, seat => _zenMetric[seat], max: false, s => true);
                case StarKind.Gatillo:
                    return Extreme(activeSeatsMask, seat => _latencySum[seat] / _latencyCount[seat], max: false,
                        seat => _latencyCount[seat] > 0);
                case StarKind.Inversora: return Extreme(activeSeatsMask, seat => _coinsInvested[seat], max: true, s => true);
                case StarKind.Fantasma: return Extreme(activeSeatsMask, seat => _coinsRobbed[seat], max: false, s => true);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static int Extreme(int activeMask, Func<int, float> metric, bool max, Func<int, bool> eligible)
        {
            int best = -1;
            float bestValue = 0f;
            bool tied = false;

            for (int seat = 0; seat < SeatCount; seat++)
            {
                if ((activeMask & (1 << seat)) == 0 || !eligible(seat)) continue;
                float value = metric(seat);
                if (best == -1 || (max ? value > bestValue : value < bestValue))
                {
                    best = seat;
                    bestValue = value;
                    tied = false;
                }
                else if (value == bestValue)
                {
                    tied = true;
                }
            }

            return tied ? -1 : best;
        }

        private static int CheckSeat(int seat)
        {
            if (seat < 0 || seat >= SeatCount)
                throw new ArgumentOutOfRangeException(nameof(seat));
            return seat;
        }
    }
}
