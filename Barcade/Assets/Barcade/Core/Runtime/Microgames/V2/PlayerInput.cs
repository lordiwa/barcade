namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// One seat's raw input for a single tick (GDD §3.2, Annex D.2 §10.2 listing).
    /// Deliberately minimal — a raw 8-way direction and a raw button bool. Derived
    /// signals (press/release edges, hold duration, mash frequency) are computed by
    /// <see cref="Barcade.Core.InputInterpreter"/> (T-101), never stored here.
    ///
    /// Reuses <see cref="Direction8"/> from <c>Barcade.Core</c> (already introduced
    /// by T-101) rather than duplicating it — that type is exactly the GDD's
    /// 8-way stick enum, not a v1-specific concept.
    /// </summary>
    public readonly struct PlayerInput
    {
        public readonly Direction8 Stick;
        public readonly bool Button;

        public PlayerInput(Direction8 stick, bool button)
        {
            Stick = stick;
            Button = button;
        }
    }
}
