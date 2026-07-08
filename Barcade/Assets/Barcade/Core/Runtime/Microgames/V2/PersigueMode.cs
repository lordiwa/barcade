namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_06 "dos sub-modos por dato". Both modes share the exact same
    /// movement/dash/collision rules (<see cref="PersigueMicrogame"/> AC6) —
    /// only WHICH group's catch triggers a win flips.
    /// </summary>
    public enum PersigueMode
    {
        /// <summary>The solo seat catches (collides with) the trio; catching every active trio seat wins for the solo.</summary>
        SoloHunts = 0,

        /// <summary>The trio catches the solo; the solo wins by surviving the full duration uncaught.</summary>
        SoloFlees = 1,
    }
}
