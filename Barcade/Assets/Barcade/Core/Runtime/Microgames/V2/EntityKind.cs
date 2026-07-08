namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// What a <see cref="RenderEntity"/> represents, for the Framework's
    /// <c>StagePresenter</c> to pick a prefab by Kind+VisualVariant (GDD §10.4).
    /// </summary>
    public enum EntityKind
    {
        PlayerAvatar = 0,
        Hazard = 1,
        Projectile = 2,
        Target = 3,
        BoardPawn = 4,
        Pickup = 5,

        /// <summary>Static cover (GDD §4 MECH_06's "2-3 obstáculos"). Added additively (T-111) — never moves, blocks avatar movement.</summary>
        Obstacle = 6,
    }
}
