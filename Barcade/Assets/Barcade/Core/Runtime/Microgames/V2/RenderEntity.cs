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

        /// <summary>
        /// Discrete, small-cardinality, mechanic-defined visual state code (e.g.
        /// waiting/false-started/reacted for ¡REACCIONA!; intact/consumed for
        /// ¡APUNTA!'s Target entities). GDD §10.4 defines this field's role
        /// literally: it is one half of the <c>EntityKind+VisualVariant -> prefab</c>
        /// key the Framework's <c>StagePresenter</c> uses to pick a 3D prefab/skin
        /// per entity (see <c>stageProfile.entityPrefabSet</c> in Annex D.1's
        /// worked example). Treat it as a prefab/skin selector only — a handful of
        /// named states a presenter can map 1:1 onto distinct prefabs or materials.
        ///
        /// It is NOT a general-purpose scalar channel: packing a continuous or
        /// finely-interpolated value in here (e.g. 0..255 standing in for a [0,1]
        /// progress) works today only by accident of both being a byte, has no
        /// natural prefab-per-value mapping under §10.4, and was exactly the
        /// TASK-026-review-flagged overload TASK-048 removed (¡APUNTA!'s aim-angle
        /// interpolation progress now lives in <see cref="Progress01"/> instead).
        /// A future mechanic that needs a continuous per-entity presentation value
        /// should use <see cref="Progress01"/> (or add another dedicated field,
        /// additively, the same way <see cref="HudState.Meters"/> was added
        /// alongside <see cref="HudState.Scores"/>) rather than reach for this one.
        /// </summary>
        public byte VisualVariant;

        /// <summary>
        /// Optional [0,1] presentation-only progress value, mechanic-defined
        /// meaning (e.g. ¡APUNTA!'s discrete-angle interpolation progress within
        /// GDD §4's 0.25s "interpolada... para sensación analógica" smoothing
        /// window — the sim itself snaps instantly; this is only how far along
        /// that window a presenter is free to visually smooth toward). Defaults to
        /// 0 for mechanics that don't populate it. Added additively (TASK-048),
        /// the per-entity analog of how <see cref="HudState.Meters"/> already adds
        /// a continuous per-seat channel alongside <see cref="HudState.Scores"/> —
        /// kept as its own field, separate from <see cref="VisualVariant"/>, which
        /// GDD §10.4 defines as the discrete prefab/skin selector and nothing else.
        /// </summary>
        public float Progress01;
    }
}
