using System;

namespace Barcade.Core.Board
{
    /// <summary>
    /// Constructor-injected board parameters (GDD §5.1: ring size N is a "parámetro
    /// de dato"; §5.2/§2.2 give the stop-meter's tick math in terms of the sim's
    /// tick rate rather than a hardcoded constant).
    /// </summary>
    public readonly struct BoardConfig
    {
        public const int MinRingSize = 16;
        public const int MaxRingSize = 24;
        public const int DefaultRingSize = 20;

        /// <summary>Ring square count (GDD §5.1), in [<see cref="MinRingSize"/>, <see cref="MaxRingSize"/>] (AC3).</summary>
        public readonly int RingSize;

        /// <summary>Fixed simulation tick rate (GDD §3.2: 60 Hz) the stop-meter's 4 Hz cycle and 8 s timeout are derived from.</summary>
        public readonly int TicksPerSecond;

        public BoardConfig(int ringSize, int ticksPerSecond)
        {
            if (ringSize < MinRingSize || ringSize > MaxRingSize)
                throw new ArgumentOutOfRangeException(nameof(ringSize), $"ringSize must be in [{MinRingSize},{MaxRingSize}] (GDD §5.1/§5.3); got {ringSize}");
            if (ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "must be positive.");

            RingSize = ringSize;
            TicksPerSecond = ticksPerSecond;
        }

        public BoardConfig(int ringSize) : this(ringSize, SessionStateMachineConfig.GddDefaults.TicksPerSecond)
        {
        }

        /// <summary>GDD §5.1 default: N=20 squares at the session's own 60 Hz tick rate.</summary>
        public static BoardConfig GddDefaults => new BoardConfig(DefaultRingSize);
    }
}
