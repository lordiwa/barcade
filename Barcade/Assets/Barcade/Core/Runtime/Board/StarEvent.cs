namespace Barcade.Core.Board
{
    /// <summary>A star awarded to a seat during <see cref="IBoardModel.Resolve"/> (Annex D.2 <see cref="BoardResolution"/>).</summary>
    public readonly struct StarEvent
    {
        public readonly int Seat;
        public readonly int Count;

        public StarEvent(int seat, int count)
        {
            Seat = seat;
            Count = count;
        }
    }
}
