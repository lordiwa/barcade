using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The occupancy of all 4 physical seats for a round (GDD Annex D.2). Indexed
    /// the same way as <c>(int)PlayerSlot</c> (0=Rojo, 1=Azul, 2=Amarillo, 3=Verde).
    /// </summary>
    public readonly struct PlayerRoster
    {
        /// <summary>Always length 4.</summary>
        public readonly SeatState[] Seats;

        public PlayerRoster(SeatState[] seats)
        {
            if (seats == null) throw new ArgumentNullException(nameof(seats));
            if (seats.Length != 4) throw new ArgumentException("PlayerRoster requires exactly 4 seats.", nameof(seats));
            Seats = seats;
        }

        /// <summary>True if the seat has a Human, HumanIdle, or Bot occupant (i.e. is not Empty).</summary>
        public bool IsActive(PlayerSlot slot) => Seats[(int)slot] != SeatState.Empty;

        /// <summary>Convenience roster with all 4 seats occupied by Human — the common test/dev case.</summary>
        public static PlayerRoster AllHuman => new PlayerRoster(new[]
        {
            SeatState.Human, SeatState.Human, SeatState.Human, SeatState.Human
        });
    }
}
