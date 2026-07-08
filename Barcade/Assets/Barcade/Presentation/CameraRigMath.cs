using System;
using Barcade.Core.Content;

namespace Barcade.Presentation
{
    /// <summary>
    /// [ASSUMED] tuning defaults for the 4 stage camera rigs (GDD §10.4, DESIGN
    /// CONTRACT RATIFIED 2026-07-08). No GDD number is given for any of these —
    /// each is a documented engineering default for human UAT to retune per
    /// <c>CLAUDE.md</c>'s "ningún número de balance vive hardcodeado" spirit
    /// (these aren't balance numbers, but the same retune-at-UAT expectation
    /// applies). <c>runnerLateral</c> and <c>boardOverview</c> have no Hito-1
    /// microgame to validate against; revisit at Hito 2 (¡CORRE!) / Hito 4 (board).
    /// </summary>
    public static class StageCameraDefaults
    {
        // ── topDownOrtho (¡ESQUIVA!/¡PERSIGUE!) — orthographic cenital ──────────
        public const float TopDownOrthoHeight = 12f;       // world units above the plane
        public const float TopDownOrthoSizeMargin = 1.15f; // headroom over half the plane's larger side

        // ── frontFixed (¡APUNTA!/¡REACCIONA!) — RULED: low perspective, narrow FOV, NOT ortho ──
        public const float FrontFixedFovDeg = 35f;
        public const float FrontFixedHeight = 1.6f;          // roughly eye height
        public const float FrontFixedDistanceMargin = 1.25f; // pull-back margin, mirrors DodgeGameBootstrap.FitCamera's 1.25x
        public const float FrontFixedPitchDeg = 8f;          // slight downward tilt so the ground plane reads in frame

        // ── runnerLateral (¡CORRE!) — perspective baja, the sole travelling exception ──
        public const float RunnerLateralFovDeg = 50f;
        public const float RunnerLateralHeight = 1.8f;
        public const float RunnerLateralSideOffset = 6f;   // world units to the side of the runner's lane
        public const float RunnerLateralForwardLead = 1.5f; // world units the camera leads ahead of the player
        public const float RunnerLateralPitchDeg = 10f;
        public const float RunnerLateralYawDeg = -90f;      // facing across the lane toward the runner

        // ── boardOverview — angled perspective framing all 20 ring cells ────────
        public const float BoardOverviewFovDeg = 45f;
        public const float BoardOverviewHeight = 14f;
        public const float BoardOverviewPitchDeg = 55f;      // steep downward angle so the ring reads
        public const float BoardOverviewDistanceMargin = 1.3f;
    }

    /// <summary>
    /// Pure per-rig camera placement math (GDD §10.4). UnityEngine-free — plain
    /// floats, using <see cref="System.MathF"/> (already the repo convention,
    /// e.g. <c>Barcade.Core.Alcocheck.AlcocheckSim</c>). "Cámara por fase, no por
    /// frame" (§10.4): every rig except <see cref="CameraRigKind.RunnerLateral"/>
    /// is placed ONCE at MG_INTRO via <see cref="ComputeFixedPlacement"/> and
    /// never recomputed during MG_PLAY (feedback shake is applied as an additive
    /// offset on top, not a recompute — see <see cref="CameraShake"/>).
    /// </summary>
    public static class CameraRigMath
    {
        private const float DegToRad = MathF.PI / 180f;

        /// <summary>
        /// Placement for the 3 fixed rigs. For <see cref="CameraRigKind.RunnerLateral"/>
        /// (which moves during play) this returns the MG_INTRO placement at
        /// <c>playerProgress01 = 0</c> — callers driving that rig during MG_PLAY
        /// must instead call <see cref="RunnerLateral"/> every frame.
        /// </summary>
        public static CameraPlacement ComputeFixedPlacement(CameraRigKind kind, PlaneMapping mapping)
        {
            switch (kind)
            {
                case CameraRigKind.TopDownOrtho: return TopDownOrtho(mapping);
                case CameraRigKind.FrontFixed: return FrontFixed(mapping);
                case CameraRigKind.BoardOverview: return BoardOverview(mapping);
                case CameraRigKind.RunnerLateral: return RunnerLateral(mapping, playerProgress01: 0f);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static CameraPlacement TopDownOrtho(PlaneMapping mapping)
        {
            float halfSpan = MathF.Max(mapping.WorldSizeX, mapping.WorldSizeZ) * 0.5f;
            float orthoSize = halfSpan * StageCameraDefaults.TopDownOrthoSizeMargin;

            return new CameraPlacement(
                posX: mapping.WorldOriginX,
                posY: mapping.WorldOriginY + StageCameraDefaults.TopDownOrthoHeight,
                posZ: mapping.WorldOriginZ,
                pitchDeg: 90f, yawDeg: 0f,
                orthographic: true, orthoSize: orthoSize, fieldOfViewDeg: 0f);
        }

        public static CameraPlacement FrontFixed(PlaneMapping mapping)
        {
            float halfFovRad = StageCameraDefaults.FrontFixedFovDeg * 0.5f * DegToRad;
            float halfDepth = mapping.WorldSizeZ * 0.5f;
            float distance = halfDepth / MathF.Tan(halfFovRad) * StageCameraDefaults.FrontFixedDistanceMargin;

            return new CameraPlacement(
                posX: mapping.WorldOriginX,
                posY: mapping.WorldOriginY + StageCameraDefaults.FrontFixedHeight,
                posZ: mapping.WorldOriginZ - distance,
                pitchDeg: StageCameraDefaults.FrontFixedPitchDeg, yawDeg: 0f,
                orthographic: false, orthoSize: 0f, fieldOfViewDeg: StageCameraDefaults.FrontFixedFovDeg);
        }

        public static CameraPlacement BoardOverview(PlaneMapping mapping)
        {
            float halfSpan = MathF.Max(mapping.WorldSizeX, mapping.WorldSizeZ) * 0.5f;
            float halfFovRad = StageCameraDefaults.BoardOverviewFovDeg * 0.5f * DegToRad;
            float distance = halfSpan / MathF.Tan(halfFovRad) * StageCameraDefaults.BoardOverviewDistanceMargin;
            float pitchRad = StageCameraDefaults.BoardOverviewPitchDeg * DegToRad;

            return new CameraPlacement(
                posX: mapping.WorldOriginX,
                posY: mapping.WorldOriginY + StageCameraDefaults.BoardOverviewHeight,
                posZ: mapping.WorldOriginZ - distance * MathF.Cos(pitchRad),
                pitchDeg: StageCameraDefaults.BoardOverviewPitchDeg, yawDeg: 0f,
                orthographic: false, orthoSize: 0f, fieldOfViewDeg: StageCameraDefaults.BoardOverviewFovDeg);
        }

        /// <summary>
        /// runnerLateral is the sole camera that MOVES during MG_PLAY (GDD §10.4:
        /// "travelling lateral suave en ¡CORRE!"). <paramref name="playerProgress01"/>
        /// is the runner's forward progress along the track (¡CORRE!'s logical X
        /// axis — a <c>RenderEntity.X</c> in [0,1] for the leading PlayerAvatar).
        /// [ASSUMED] "whether it tracks player advance": yes — the lateral dolly
        /// follows progress along world X (the runner's lane-advance axis) so the
        /// pack stays framed; the side offset is along world Z (the lane's
        /// cross-axis, logical Y).
        /// </summary>
        public static CameraPlacement RunnerLateral(PlaneMapping mapping, float playerProgress01)
        {
            float clamped = playerProgress01 < 0f ? 0f : (playerProgress01 > 1f ? 1f : playerProgress01);
            float worldX = mapping.WorldOriginX + (clamped - 0.5f) * mapping.WorldSizeX
                + StageCameraDefaults.RunnerLateralForwardLead;

            return new CameraPlacement(
                posX: worldX,
                posY: mapping.WorldOriginY + StageCameraDefaults.RunnerLateralHeight,
                posZ: mapping.WorldOriginZ + StageCameraDefaults.RunnerLateralSideOffset,
                pitchDeg: StageCameraDefaults.RunnerLateralPitchDeg, yawDeg: StageCameraDefaults.RunnerLateralYawDeg,
                orthographic: false, orthoSize: 0f, fieldOfViewDeg: StageCameraDefaults.RunnerLateralFovDeg);
        }
    }

    /// <summary>
    /// Pure feedback-shake amplitude math (GDD §8.2: "Screen shake ≤ 300 ms" at
    /// feedback nivel Alto). The camera itself stays fixed per-phase (§10.4);
    /// shake is an additive positional offset the caller (StagePresenter) applies
    /// on top of the fixed placement, scaled by this decaying amplitude.
    /// </summary>
    public static class CameraShake
    {
        /// <summary>GDD §8.2 hard cap for Nivel Alto screen shake.</summary>
        public const float MaxDurationSeconds = 0.3f;

        /// <summary>[ASSUMED] peak offset magnitude in world units; no GDD number given.</summary>
        public const float DefaultPeakAmplitude = 0.15f;

        /// <summary>
        /// Linear decay to 0 at <paramref name="durationSeconds"/>. Returns 0
        /// once elapsed is outside [0, duration) or either input is non-positive.
        /// </summary>
        public static float Amplitude(float elapsedSeconds, float durationSeconds, float peakAmplitude)
        {
            if (!(durationSeconds > 0f) || !(peakAmplitude > 0f)) return 0f;
            if (elapsedSeconds < 0f || elapsedSeconds >= durationSeconds) return 0f;
            return peakAmplitude * (1f - elapsedSeconds / durationSeconds);
        }
    }
}
