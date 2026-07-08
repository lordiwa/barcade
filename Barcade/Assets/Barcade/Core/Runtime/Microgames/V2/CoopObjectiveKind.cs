namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Which existing v2 coop mechanic (GDD §7.1: "construido con MECH_08/MECH_09
    /// ampliadas") governs a <see cref="CoopObjective"/>'s interaction once the
    /// team arrives at its location.
    /// </summary>
    public enum CoopObjectiveKind
    {
        /// <summary>Delegates to a <see cref="SujetaMicrogame"/> instance (MECH_08, hold-all-switches).</summary>
        Sujeta = 0,

        /// <summary>Delegates to an <see cref="IgualaMicrogame"/> instance (MECH_09, color-relay).</summary>
        Iguala = 1,
    }
}
