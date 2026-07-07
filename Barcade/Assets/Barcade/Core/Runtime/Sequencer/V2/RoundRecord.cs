using System;

namespace Barcade.Core.Sequencer.V2
{
    /// <summary>
    /// One completed round in the session history consumed by
    /// <see cref="MicrogameSelector"/> (GDD §9.2: "el sorteo es función pura de
    /// (seed, historial)"). Plain data — the selector never mutates it.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class RoundRecord
    {
        /// <summary>Mechanic id played (<see cref="Barcade.Core.Content.MicrogameDefinitionV2.Mechanic"/>).</summary>
        public readonly string Mechanic;

        /// <summary>Variant id played (<see cref="Barcade.Core.Content.MicrogameDefinitionV2.Id"/>).</summary>
        public readonly string VariantId;

        /// <summary>
        /// Per-seat final place this round, indexed like <c>(int)PlayerSlot</c>.
        /// 1..4 (ties share a place, GDD Annex D.2); 0 = seat absent/excluded.
        /// </summary>
        public readonly int[] Places;

        public RoundRecord(string mechanic, string variantId, int[] places)
        {
            if (mechanic == null) throw new ArgumentNullException(nameof(mechanic));
            if (variantId == null) throw new ArgumentNullException(nameof(variantId));
            if (places == null) throw new ArgumentNullException(nameof(places));
            if (places.Length != 4) throw new ArgumentException("RoundRecord requires exactly 4 places.", nameof(places));

            Mechanic = mechanic;
            VariantId = variantId;
            // LOW-2 (TASK-035 review fix round): clone rather than alias the
            // caller's array -- session history is long-lived and fed back into
            // future SelectRound calls, so a caller mutating an array it already
            // handed off (e.g. reusing a scratch buffer) must not corrupt an
            // already-recorded round (replay purity).
            Places = (int[])places.Clone();
        }
    }
}
