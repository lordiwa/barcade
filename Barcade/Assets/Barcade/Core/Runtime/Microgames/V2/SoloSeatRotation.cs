using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_06 (T-111): "Rol solista rotativo y forzado por el sistema
    /// (round-robin sobre la sesión; nunca lo elige el juego dos veces seguidas
    /// para el mismo puesto)." Shared by both T-111 asymmetric mechanics
    /// (<see cref="PersigueMicrogame"/>/<see cref="BombardeaMicrogame"/>) since
    /// "who is solo this round" is the same session-level bookkeeping concern
    /// for either — see each mechanic's own params (<c>SoloSeat</c>) for how the
    /// result of this class feeds into <c>Initialize</c>.
    ///
    /// <para>
    /// <b>Why this lives outside <see cref="IMicrogame"/> (design decision).</b>
    /// <see cref="IMicrogame.Initialize"/> takes a <see cref="PlayerRoster"/> for
    /// THIS round only — it has no notion of session history, and a fresh
    /// mechanic instance is constructed every round (same pattern as every
    /// other v2 mechanic). "Round-robin over the session" is therefore a
    /// session/sequencer-level concern: the caller (production sequencer, or a
    /// test simulating a full session) calls <see cref="NextSoloSeat"/> once per
    /// round and passes the result into that round's mechanic params — this
    /// class holds no state of its own between calls.
    /// </para>
    ///
    /// <para>
    /// <b>"System-forced, never chosen" (structural, not a discipline).</b>
    /// <see cref="NextSoloSeat"/> is a PURE function of
    /// (<paramref name="roundIndex"/>, roster) — no <see cref="SeededRandom"/>,
    /// no <see cref="Microgames.V2.PlayerInput"/>, no bot/player state feeds into
    /// it at all, so "the game never lets a player pick the role" is guaranteed
    /// by the method's own signature, not by convention.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] round-robin over ACTIVE seats only, fixed
    /// Rojo/Azul/Amarillo/Verde order</b> (the same canonical seat order every
    /// v2 mechanic already uses, e.g. <c>EsquivaMicrogame.AllSlots</c>) — GDD
    /// names "round-robin" but not a tie-breaking seat order, since a physical
    /// cabinet has no other natural ordering. Cycling through N active seats in
    /// a fixed order trivially satisfies "never the same seat twice in a row"
    /// for any N &gt;= 2 (round (r) and round (r+1) always land on two distinct
    /// positions in the cycle); with exactly 1 active seat the no-repeat
    /// guarantee is vacuously true (there is no other seat to rotate to).
    /// </para>
    /// </summary>
    public static class SoloSeatRotation
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        /// <summary>
        /// Returns the solo seat index (0..3) for round <paramref name="roundIndex"/>
        /// (0-based; a caller increments this once per round this rotation
        /// governs). Deterministic: the same (roundIndex, roster) always
        /// returns the same seat.
        /// </summary>
        public static int NextSoloSeat(int roundIndex, PlayerRoster roster)
        {
            if (roundIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(roundIndex), "must be non-negative.");

            int activeCount = 0;
            for (int i = 0; i < AllSlots.Length; i++)
                if (roster.IsActive(AllSlots[i])) activeCount++;

            if (activeCount == 0)
                throw new ArgumentException("PlayerRoster has no active seats.", nameof(roster));

            int target = roundIndex % activeCount;
            int seen = 0;
            for (int i = 0; i < AllSlots.Length; i++)
            {
                if (!roster.IsActive(AllSlots[i])) continue;
                if (seen == target) return i;
                seen++;
            }

            throw new InvalidOperationException("unreachable: activeCount counted above must match the scan below.");
        }
    }
}
