using System;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// Button-mash pattern generator for any <c>*BotPolicy</c> whose mechanic
    /// reads a mash rate (GDD §3.3's <c>InputInterpreter.MashForce</c> curve —
    /// currently CORRE). Same shape as the pre-existing test-only
    /// <c>Barcade.SlowTests.Mash</c> helper (button down for the first half of
    /// each period, up for the second — both half-widths clear
    /// <c>InputInterpreter</c>'s 2-tick debounce confirm, so every period
    /// registers exactly one confirmed press edge), reimplemented here in Core
    /// so production bot policies do not depend on the SlowTests-only project.
    /// </summary>
    public static class BotMash
    {
        /// <summary>True (button down) for the first half of each ~<paramref name="hz"/>-per-second period on the 60Hz tick clock, false otherwise.</summary>
        public static bool ButtonDown(int localTick, float hz, int ticksPerSecond = 60)
        {
            if (hz <= 0f) return false;
            int period = (int)MathF.Round(ticksPerSecond / hz);
            if (period <= 0) period = 1;
            return (localTick % period) < (period / 2);
        }
    }
}
