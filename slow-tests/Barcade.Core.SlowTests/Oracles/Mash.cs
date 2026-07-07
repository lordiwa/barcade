using System;

namespace Barcade.SlowTests
{
    /// <summary>
    /// Button-mash pattern generator, lifted verbatim from the private
    /// <c>MashButton</c> helper in CorreMicrogameV2Tests.cs so the CORRE sweep and
    /// its perfect-runner oracle produce the exact mash cadence the fast tests use.
    /// </summary>
    internal static class Mash
    {
        /// <summary>
        /// A button-mash pattern at approximately <paramref name="hz"/> presses per
        /// second on the 60 Hz tick clock: button down for the first half of each
        /// period, up for the second half. Both half-widths are &gt;= the
        /// InputInterpreter's 2-tick debounce confirm, so every period registers
        /// exactly one confirmed press edge (§3.3 mash counting).
        /// </summary>
        public static bool Button(int tick, int hz)
        {
            int period = (int)MathF.Round(60f / hz);
            return (tick % period) < (period / 2);
        }
    }
}
