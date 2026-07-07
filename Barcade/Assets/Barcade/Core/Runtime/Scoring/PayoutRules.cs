using System;

namespace Barcade.Core.Scoring
{
    /// <summary>
    /// GDD §6.1 payout application. Payout tables are DATA (they normally arrive
    /// via <c>MicrogameDefinitionV2.PayoutTable</c>, validated by T-106's
    /// validator); the constants here are the GDD's initial GameTuning values.
    /// The §6.1 anti-zero invariant ("el 0 absoluto desengancha") is enforced at
    /// application time as well: a zero or missing payout entry is a data bug and
    /// fails loudly.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public static class PayoutRules
    {
        /// <summary>GDD §6.1: 1º=6, 2º=4, 3º=2, 4º=1 (indexed by place-1).</summary>
        public static readonly int[] DefaultCompetitive = { 6, 4, 2, 1 };

        /// <summary>GDD §6.1: coop success pays 4 to everyone.</summary>
        public const int DefaultCoopSuccess = 4;

        /// <summary>GDD §6.1: coop fail still pays 1 to everyone — never zero.</summary>
        public const int DefaultCoopFail = 1;

        /// <summary>
        /// Adds each seat's payout for a competitive round: seat with place p gets
        /// <c>payoutTable[p-1]</c>; tied seats share the place and take that
        /// place's payout; place 0 = absent seat, no payout.
        /// </summary>
        public static void ApplyCompetitive(int[] coins, int[] places, int[] payoutTable)
        {
            if (coins == null) throw new ArgumentNullException(nameof(coins));
            if (places == null) throw new ArgumentNullException(nameof(places));
            if (coins.Length != 4 || places.Length != 4)
                throw new ArgumentException("coins and places must be length 4");
            ValidateTable(payoutTable);

            for (int seat = 0; seat < 4; seat++)
            {
                int place = places[seat];
                if (place == 0) continue;
                if (place < 1 || place > 4)
                    throw new ArgumentException($"place {place} out of range for seat {seat}", nameof(places));
                coins[seat] += payoutTable[place - 1];
            }
        }

        /// <summary>Adds the coop payout (success or fail value) to every active seat.</summary>
        public static void ApplyCoop(int[] coins, bool success, int payout, int activeSeatsMask)
        {
            if (coins == null) throw new ArgumentNullException(nameof(coins));
            if (coins.Length != 4) throw new ArgumentException("coins must be length 4", nameof(coins));
            if (payout < 1)
                throw new ArgumentException("GDD 6.1: coop payout must be >= 1 — the absolute zero is banned", nameof(payout));

            for (int seat = 0; seat < 4; seat++)
                if ((activeSeatsMask & (1 << seat)) != 0)
                    coins[seat] += payout;
        }

        private static void ValidateTable(int[] payoutTable)
        {
            if (payoutTable == null) throw new ArgumentNullException(nameof(payoutTable));
            if (payoutTable.Length != 4)
                throw new ArgumentException("competitive payout table needs one entry per place (4)", nameof(payoutTable));
            for (int i = 0; i < payoutTable.Length; i++)
                if (payoutTable[i] < 1)
                    throw new ArgumentException(
                        $"GDD 6.1: payout for place {i + 1} is {payoutTable[i]} — the absolute zero is banned",
                        nameof(payoutTable));
        }
    }
}
