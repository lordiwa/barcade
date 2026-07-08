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

        /// <summary>MECH_03 — ¡CORRE! (GDD §4, endless run). T-107 slice 2.</summary>
        Corre = 4,

        /// <summary>MECH_06 — ¡PERSIGUE!/¡ESCAPA! (GDD §4, asymmetric 1v3 chase). T-111.</summary>
        Persigue = 5,

        /// <summary>MECH_07 — ¡BOMBARDEA! (GDD §4, asymmetric 1v3 inverted-power telegraph). T-111.</summary>
        Bombardea = 6,

        /// <summary>MECH_08 — ¡SUJETA!/¡SINCRONIZA! (GDD §4, cooperative hold-all-switches). T-112.</summary>
        Sujeta = 7,

        /// <summary>MECH_09 — ¡IGUALA! (GDD §4, cooperative color-relay, `colorRelay` mode). T-112.</summary>
        Iguala = 8,
    }
}
