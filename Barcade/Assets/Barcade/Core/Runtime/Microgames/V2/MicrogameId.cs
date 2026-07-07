namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Identifies which GDD MECH_XX mechanic a <see cref="IMicrogame"/> (v2 contract)
    /// implements. One member per mechanic as each migrates to the v2 contract
    /// (GDD Annex D.1: T-103 REACCIONA first, others follow under T-104/T-107).
    /// </summary>
    public enum MicrogameId
    {
        /// <summary>MECH_05 — ¡REACCIONA! (GDD §4, quick-draw).</summary>
        Reacciona = 0,

        /// <summary>MECH_04 — ¡APUNTA! (GDD §4, hold-charge aiming).</summary>
        Apunta = 1,

        /// <summary>MECH_02 — ¡ESQUIVA! (GDD §4, dodge/survival). T-107 slice 1.</summary>
        Esquiva = 2,

        /// <summary>MECH_01 — ¡MANTÉN! (GDD §4, inverted-pendulum balance). T-107 slice 3.</summary>
        Manten = 3,
    }
}
