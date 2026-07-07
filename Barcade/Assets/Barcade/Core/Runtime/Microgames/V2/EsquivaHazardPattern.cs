namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_02 "hazardPattern" data variant — one pattern per definition
    /// (a round's hazards are all the SAME pattern; different <c>mg_esquiva_*</c>
    /// definitions pick different patterns).
    ///
    /// [ASSUMED] geometry (human-ruled 2026-07-07, engineering [ASSUMED], reviewer-
    /// verified — GDD §4 names these four patterns but does not define their
    /// motion): see <see cref="EsquivaMicrogame"/>'s class doc for the exact
    /// per-pattern trajectory each one implements.
    /// </summary>
    public enum EsquivaHazardPattern
    {
        /// <summary>[ASSUMED] Falls straight down from the top edge (Y=0) toward the bottom (Y=1).</summary>
        Rain = 0,

        /// <summary>[ASSUMED] Enters from the left (X=0) or right (X=1) edge, moving straight across.</summary>
        Sides = 1,

        /// <summary>[ASSUMED] Enters at one of the 4 corners, moving straight toward the opposite corner.</summary>
        Cross = 2,

        /// <summary>
        /// [ASSUMED] Drifts toward the nearest active player, steering rate capped
        /// at <see cref="EsquivaMicrogame.HomingTurnRateDegPerSec"/> (GDD §4: "limita
        /// giro a 30°/s para que siempre exista escape") — escapable by construction.
        /// </summary>
        HomingSoft = 3,
    }
}
