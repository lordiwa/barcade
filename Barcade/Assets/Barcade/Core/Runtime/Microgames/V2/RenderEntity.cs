namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// A single renderable thing in logical space (GDD Annex D.2 / §10.4). Core
    /// never emits Unity world coordinates — X/Y are normalized logical space
    /// ([0,1]^2); the Framework's <c>StagePresenter</c> projects to the 3D world.
    /// </summary>
    public struct RenderEntity
    {
        public EntityKind Kind;

        /// <summary>Seat index 0..3, or -1 if the entity is neutral (owned by no seat).</summary>
        public int OwnerSeat;

        public float X;
        public float Y;

        /// <summary>Logical height above the ground plane (jump/fall); 0 = grounded.</summary>
        public float Height;

        public float Rotation;
        public float Scale;

        /// <summary>Mechanic-defined visual state code (e.g. waiting/false-started/reacted for ¡REACCIONA!).</summary>
        public byte VisualVariant;
    }
}
