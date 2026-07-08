using Barcade.Core.Content;

namespace Barcade.Presentation
{
    /// <summary>
    /// Pure logical-space -&gt; world-space projection math (GDD §10.4, TASK-027
    /// DESIGN CONTRACT RATIFIED 2026-07-08). UnityEngine-free: plain floats in,
    /// plain floats out, so the fast dotnet suite can regression-lock it without
    /// the Unity Editor. The Framework's <c>StagePresenter</c> is the only caller
    /// that turns these floats into <c>UnityEngine.Vector3</c> (see
    /// <c>Barcade.Framework.Stage.StageVectorMath</c>).
    ///
    /// Axis convention is FIXED IN CODE, not data: logical X -&gt; world X, logical
    /// Y -&gt; world Z, <c>RenderEntity.Height</c> -&gt; world Y (up). Mirrors the
    /// existing Esquiva-3D demo's centered-XZ convention
    /// (<c>Barcade.Framework.DodgeGameBootstrap</c>): logical (0.5, 0.5) sits at
    /// the plane's world origin; logical (0,0)/(1,1) sit at opposite corners of
    /// a <see cref="PlaneMapping.WorldSizeX"/> x <see cref="PlaneMapping.WorldSizeZ"/>
    /// rectangle centred on <see cref="PlaneMapping.WorldOriginX"/>/Y/Z.
    /// </summary>
    public static class StageProjection
    {
        /// <summary>[ASSUMED] Logical Height already reads as roughly world-scale units (mirrors DodgeGameBootstrap's jumpApex ~1.5); no GDD number given for a separate scale.</summary>
        public const float DefaultHeightScale = 1f;

        public static void Project(
            float logicalX, float logicalY, float height, PlaneMapping mapping,
            out float worldX, out float worldY, out float worldZ)
        {
            Project(logicalX, logicalY, height, mapping, DefaultHeightScale, out worldX, out worldY, out worldZ);
        }

        public static void Project(
            float logicalX, float logicalY, float height, PlaneMapping mapping, float heightScale,
            out float worldX, out float worldY, out float worldZ)
        {
            worldX = mapping.WorldOriginX + (logicalX - 0.5f) * mapping.WorldSizeX;
            worldZ = mapping.WorldOriginZ + (logicalY - 0.5f) * mapping.WorldSizeZ;
            worldY = mapping.WorldOriginY + height * heightScale;
        }
    }
}
