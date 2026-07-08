using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="SujetaMicrogame"/> (GDD §4
    /// MECH_08). Plain engine-free POCO — same approach as
    /// <see cref="PersigueParams"/>/<see cref="BombardeaParams"/>.
    ///
    /// <para>
    /// <b><see cref="ViewerSeat"/> — decided OUTSIDE this mechanic, same pattern
    /// as <see cref="PersigueParams.SoloSeat"/>.</b> GDD's own "rotativo" text for
    /// the infoAsym countdown-viewer is the identical session-level "who gets
    /// this role this round" bookkeeping <see cref="SoloSeatRotation"/> already
    /// solves for MECH_06 — reused verbatim here rather than a second,
    /// duplicated rotation type. Ignored entirely when
    /// <see cref="Mode"/> != <see cref="SujetaMode.InfoAsym"/>.
    /// </para>
    /// </summary>
    public readonly struct SujetaParams
    {
        /// <summary>GDD "holdWindow": seconds all engaged seats must hold their switch simultaneously to complete one window (GDD literal default 1.5).</summary>
        public readonly float HoldWindowSeconds;

        /// <summary>GDD "windowsToWin": number of completed windows the team needs within <see cref="DurationSeconds"/> to succeed. [ASSUMED] GDD names the parameter but not a default value.</summary>
        public readonly int WindowsToWin;

        /// <summary>GDD "mode": holdTogether/infoAsym/platformSync.</summary>
        public readonly SujetaMode Mode;

        /// <summary>Seat index (0..3) that sees the countdown in <see cref="SujetaMode.InfoAsym"/> — decided by <see cref="SoloSeatRotation"/>, unused otherwise.</summary>
        public readonly int ViewerSeat;

        /// <summary>Round length in seconds — must be within the TASK-030 validator's GDD §11.1 [3,8] bound.</summary>
        public readonly float DurationSeconds;

        public SujetaParams(
            float holdWindowSeconds,
            int windowsToWin,
            SujetaMode mode,
            int viewerSeat,
            float durationSeconds)
        {
            if (holdWindowSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(holdWindowSeconds), "must be positive.");
            if (windowsToWin < 1)
                throw new ArgumentOutOfRangeException(nameof(windowsToWin), "must be at least 1.");
            if (viewerSeat < 0 || viewerSeat > 3)
                throw new ArgumentOutOfRangeException(nameof(viewerSeat), "must be a seat index 0..3.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            HoldWindowSeconds = holdWindowSeconds;
            WindowsToWin = windowsToWin;
            Mode = mode;
            ViewerSeat = viewerSeat;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// GDD-plausible baseline calibration: HoldWindowSeconds is the GDD
        /// literal 1.5s. WindowsToWin defaults to 1 — [ASSUMED], GDD names the
        /// knob but not a number; the simplest reading ("hold once, together")
        /// exercises the whole rule set without inventing an un-named threshold.
        /// </summary>
        public static SujetaParams GddDefaults(SujetaMode mode = SujetaMode.HoldTogether, int viewerSeat = 0) => new SujetaParams(
            holdWindowSeconds: 1.5f,
            windowsToWin: 1,
            mode: mode,
            viewerSeat: viewerSeat,
            durationSeconds: 6.0f);
    }
}
