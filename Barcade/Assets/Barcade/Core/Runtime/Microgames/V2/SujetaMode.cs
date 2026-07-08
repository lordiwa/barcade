namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_08 "mode (holdTogether/infoAsym/platformSync)". All three
    /// share the exact same hold/window/abandonment rules
    /// (<see cref="SujetaMicrogame"/> — GDD P2 coop framing bans any
    /// mode-conditioned RULE branch, only the countdown's VISIBILITY differs).
    /// </summary>
    public enum SujetaMode
    {
        /// <summary>Base mode: the shared circular progress meter is visible to every engaged seat identically.</summary>
        HoldTogether = 0,

        /// <summary>Only <see cref="SujetaParams.ViewerSeat"/> sees the countdown; other seats see their own switch but not the timing (GDD: forces the verbal "¡AHORA!" call, P2).</summary>
        InfoAsym = 1,

        /// <summary>
        /// [ASSUMED SCOPE] GDD names "posicionarse en plataformas" as a per-definition
        /// variant requiring stick-driven positioning; not implemented in this slice
        /// (no AC names it — same declared-but-unread status as
        /// <see cref="EsquivaParams.JumpEnabled"/>/<see cref="CorreParams.RaceToFinish"/>).
        /// Falls back to <see cref="HoldTogether"/>'s shared-visibility behavior.
        /// </summary>
        PlatformSync = 2,
    }
}
