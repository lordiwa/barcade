namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Adapts a tick's v2 <see cref="InputSnapshot.Players"/> array into
    /// <see cref="Barcade.Core.IReadOnlyPlayerInputs"/> (v1, per-seat) so a v1
    /// <see cref="Barcade.Core.InputInterpreter"/> can be reused unchanged by v2
    /// mechanics.
    ///
    /// <para>
    /// <b>Why this exists (TASK-046).</b> Extracted from two private, textually-
    /// diverging copies (<c>ReaccionaMicrogame</c>'s and <c>ApuntaMicrogame</c>'s,
    /// rev-t026 LOW-3) that behaved identically only by coincidence:
    /// <see cref="Barcade.Core.InputInterpreter.ToDirection8"/> classifies a stick
    /// vector by axis SIGN alone, never magnitude, so it could not tell the two
    /// copies' outputs apart. Any future magnitude-sensitive change (e.g. wiring
    /// <see cref="Barcade.Core.InputSnapshot.WithDeadZone"/> into the
    /// interpreter's own pipeline, which it does not do today) would have made
    /// the two mechanics diverge silently. One canonical implementation removes
    /// that latent hazard by construction.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Canonical stick-vector convention: normalized (unit
    /// vectors).</b> Every pressed <see cref="Direction8"/> maps to a unit-length
    /// (x,y) — cardinals (±1,0)/(0,±1), diagonals (±0.70710678,±0.70710678) — and
    /// <see cref="Direction8.None"/> maps to (0,0). Chosen over the sign-pure
    /// alternative (cardinals magnitude 1, diagonals magnitude √2≈1.414 — what
    /// ReaccionaMicrogame's old copy produced) for two reasons: (1) the cabinet's
    /// stick is digital and 8-way with no analog "how hard" gradation (GDD §1.3:
    /// "1 palanca digital de 8 direcciones... no hay ejes analógicos") — a
    /// uniform magnitude of exactly 1 for every pressed direction is the
    /// semantically honest representation of that hardware; sign-pure's
    /// inconsistent magnitude (diagonals reading "harder pressed" than
    /// cardinals) has no physical meaning behind it. (2) <see cref="Barcade.Core.InputSnapshot"/>
    /// already exposes <c>Magnitude()</c>/<c>Normalized()</c>/<c>WithDeadZone()</c> —
    /// none wired into <c>InputInterpreter</c>'s own pipeline today, but if one
    /// ever is, a uniform magnitude is what makes a single deadzone threshold
    /// behave consistently across cardinals and diagonals, where sign-pure would
    /// silently treat diagonal presses as "stronger" for no reason.
    /// <see cref="Barcade.Core.InputInterpreter.ToDirection8"/>'s own sign-only
    /// classification is unaffected either way — this choice only matters for
    /// whichever future consumer reads magnitude.
    /// </para>
    ///
    /// One instance is safe to reuse tick to tick: <see cref="SetSource"/> just
    /// reassigns the array reference (no allocation); <see cref="For"/>
    /// allocates nothing.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class InputBridge : Barcade.Core.IReadOnlyPlayerInputs
    {
        private PlayerInput[] _players;

        public void SetSource(PlayerInput[] players) => _players = players;

        public Barcade.Core.InputSnapshot For(PlayerSlot slot)
        {
            PlayerInput p = _players[(int)slot];
            (float x, float y) = ToUnitVector(p.Stick);
            // InputInterpreter only ever distinguishes "down" from "up" (Pressed
            // and Held are treated identically as "down") — Held is an arbitrary
            // but harmless choice for the raw "down" state.
            Barcade.Core.ButtonState state = p.Button ? Barcade.Core.ButtonState.Held : Barcade.Core.ButtonState.Released;
            return new Barcade.Core.InputSnapshot(x, y, state);
        }

        /// <summary>
        /// The canonical stick-vector convention (see class doc), exposed
        /// publicly and statically so a consumer that needs the raw mapping
        /// without routing through <see cref="For"/> (e.g. a future presenter
        /// wanting to draw a stick indicator) can reuse the exact same values
        /// rather than reimplementing the switch.
        /// </summary>
        public static (float X, float Y) ToUnitVector(Direction8 direction)
        {
            const float diag = 0.70710678f; // 1/sqrt(2)
            switch (direction)
            {
                case Direction8.N: return (0f, 1f);
                case Direction8.S: return (0f, -1f);
                case Direction8.E: return (1f, 0f);
                case Direction8.W: return (-1f, 0f);
                case Direction8.NE: return (diag, diag);
                case Direction8.SE: return (diag, -diag);
                case Direction8.NW: return (-diag, diag);
                case Direction8.SW: return (-diag, -diag);
                default: return (0f, 0f);
            }
        }
    }
}
