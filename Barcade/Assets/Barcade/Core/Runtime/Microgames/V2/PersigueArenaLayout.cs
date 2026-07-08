namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_06 "arenaLayout (enum de 4 layouts)". Each names a FIXED,
    /// deterministic placement of the "2-3 obstáculos (cover)" GDD names but
    /// never geometrizes — [ASSUMED] engineering layouts (see
    /// <see cref="PersigueMicrogame.GetObstacles"/> for the exact coordinates),
    /// not GDD-literal numbers. Layouts are static data, never RNG-generated —
    /// "layout" is a per-definition/variant choice (like Esquiva's
    /// <c>HazardPattern</c>), not a per-round random draw.
    /// </summary>
    public enum PersigueArenaLayout
    {
        /// <summary>3 obstacles in a shallow triangular spread across the upper half.</summary>
        Cross = 0,

        /// <summary>2 obstacles, one in each of two opposite corners.</summary>
        Corners = 1,

        /// <summary>2 obstacles flanking the arena's center, leaving a corridor.</summary>
        Central = 2,

        /// <summary>3 obstacles strung along the main diagonal.</summary>
        Diagonal = 3,
    }
}
