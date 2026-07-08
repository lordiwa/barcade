using System;

namespace Barcade.Presentation
{
    /// <summary>
    /// The 4 stage camera rigs (GDD §10.4/§11.1). A closed set, same spirit as
    /// <c>Barcade.Core.Microgames.V2.EntityKind</c> — the mechanic set declared
    /// in §10.4 is exhaustive, not illustrative.
    /// </summary>
    public enum CameraRigKind
    {
        /// <summary>Orthographic cenital (¡ESQUIVA!/¡PERSIGUE!).</summary>
        TopDownOrtho,

        /// <summary>Low perspective, narrow FOV — RULED NOT ortho (¡APUNTA!/¡REACCIONA!, DESIGN CONTRACT RATIFIED 2026-07-08).</summary>
        FrontFixed,

        /// <summary>Perspective baja; the sole rig that moves during MG_PLAY (¡CORRE!).</summary>
        RunnerLateral,

        /// <summary>[ASSUMED] Angled perspective framing all 20 ring cells; no Hito-1 microgame validates it.</summary>
        BoardOverview,
    }

    /// <summary>Converts between <see cref="CameraRigKind"/> and the exact GDD §11.1 <c>stageProfile.camera</c> string values.</summary>
    public static class CameraRigKindCodec
    {
        public static bool TryParse(string camera, out CameraRigKind kind)
        {
            switch (camera)
            {
                case "topDownOrtho": kind = CameraRigKind.TopDownOrtho; return true;
                case "frontFixed": kind = CameraRigKind.FrontFixed; return true;
                case "runnerLateral": kind = CameraRigKind.RunnerLateral; return true;
                case "boardOverview": kind = CameraRigKind.BoardOverview; return true;
                default: kind = default; return false;
            }
        }

        public static string ToStageProfileString(this CameraRigKind kind)
        {
            switch (kind)
            {
                case CameraRigKind.TopDownOrtho: return "topDownOrtho";
                case CameraRigKind.FrontFixed: return "frontFixed";
                case CameraRigKind.RunnerLateral: return "runnerLateral";
                case CameraRigKind.BoardOverview: return "boardOverview";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>True only for <see cref="CameraRigKind.RunnerLateral"/> — GDD §10.4's sole travelling exception.</summary>
        public static bool MovesDuringPlay(this CameraRigKind kind) => kind == CameraRigKind.RunnerLateral;
    }
}
