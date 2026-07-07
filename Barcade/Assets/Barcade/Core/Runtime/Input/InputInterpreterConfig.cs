using System;

namespace Barcade.Core
{
    /// <summary>
    /// Constructor-injected calibration for <see cref="InputInterpreter"/>
    /// (GDD §3.1/§3.3). Kept as a plain data struct — no code constants — so it
    /// can be sourced from <c>GameTuning</c> (remote data, §11) once that pipeline
    /// exists, and swapped freely in tests.
    /// </summary>
    public readonly struct InputInterpreterConfig
    {
        /// <summary>Fixed simulation tick rate (GDD §3.2: 60 Hz).</summary>
        public readonly int TicksPerSecond;

        /// <summary>
        /// Maximum ticks between a confirmed press and its confirmed release for
        /// the release to count as a tap (GDD §3.1: ≤150 ms ⇒ 9 ticks @ 60 Hz).
        /// </summary>
        public readonly int TapWindowTicks;

        /// <summary>Mash frequency below which force is 0 (GDD §3.3: 2 Hz).</summary>
        public readonly float MashMinHz;

        /// <summary>Mash frequency at and above which force saturates at 1 (GDD §3.3: 9 Hz).</summary>
        public readonly float MashSatHz;

        /// <summary>Sliding window over which mash edges are counted (GDD §3.3: 500 ms).</summary>
        public readonly float MashWindowSeconds;

        public InputInterpreterConfig(int ticksPerSecond, int tapWindowTicks, float mashMinHz, float mashSatHz, float mashWindowSeconds)
        {
            if (ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "must be positive.");
            if (tapWindowTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(tapWindowTicks), "must be non-negative.");
            if (mashMinHz < 0f)
                throw new ArgumentOutOfRangeException(nameof(mashMinHz), "must be non-negative — a negative floor would make MashForce report a phantom positive force at 0 Hz.");
            if (mashSatHz <= mashMinHz)
                throw new ArgumentOutOfRangeException(nameof(mashSatHz), "must be greater than mashMinHz.");
            if (mashWindowSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(mashWindowSeconds), "must be positive.");

            TicksPerSecond = ticksPerSecond;
            TapWindowTicks = tapWindowTicks;
            MashMinHz = mashMinHz;
            MashSatHz = mashSatHz;
            MashWindowSeconds = mashWindowSeconds;
        }

        /// <summary>GDD §3.1/§3.3 default calibration: 60 Hz sim, 9-tick (150 ms) tap window, 2-9 Hz mash curve over a 500 ms window.</summary>
        public static InputInterpreterConfig GddDefaults => new InputInterpreterConfig(
            ticksPerSecond: 60,
            tapWindowTicks: 9,
            mashMinHz: 2f,
            mashSatHz: 9f,
            mashWindowSeconds: 0.5f);

        /// <summary>The mash window expressed in ticks at <see cref="TicksPerSecond"/>.</summary>
        public int MashWindowTicks => (int)MathF.Round(MashWindowSeconds * TicksPerSecond);
    }
}
