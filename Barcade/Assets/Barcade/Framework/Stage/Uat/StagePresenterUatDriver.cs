// ============================================================================
// THROWAWAY / DEV-ONLY UAT SCAFFOLDING -- TASK-027 (StagePresenter 3D visual
// UAT). A plain runtime MonoBehaviour (Barcade.Framework.Uat is a normal
// runtime assembly -- an Editor-only asmdef would make this un-AddComponent-able
// in Play mode, see StagePresenterUatSceneBuilder.cs's fix-round note); it
// DOES compile into a Player build, but nothing in the shipped game ever
// instantiates it (only the throwaway, never-saved scene the Editor menu item
// builds does). Does not modify production StagePresenter/Barcade.Presentation
// code or the ratified design -- it only drives the real, unmodified
// StagePresenter through its public Bind/Present/Unbind API.
// ============================================================================
using UnityEngine;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Framework.Stage; // StagePresenter -- C# doesn't auto-import a parent namespace's members into a nested one
using Barcade.Presentation;

namespace Barcade.Framework.Stage.Uat
{
    /// <summary>
    /// Binds a real, unmodified <see cref="StagePresenter"/> to a stub
    /// <see cref="StagePresenterUatMicrogame"/> and drives both every frame, so
    /// a human can eyeball and live-tune the presenter in Play mode.
    ///
    /// Built by <see cref="StagePresenterUatSceneBuilder"/> (Barcade/UAT/Build
    /// StagePresenter UAT menu item).
    ///
    /// LIVE-tunable from this Inspector (real StageProfile data, not consts):
    ///   - _cameraRig: switch between all 4 rigs any time while playing.
    ///   - _worldSizeX/_worldSizeZ/_worldOriginY: the stageProfile.planeMapping
    ///     the presenter projects into.
    ///   - _entityPrefabSetId: leave empty (default) to force
    ///     StagePrimitiveFactory's fallback for every EntityKind; set to some
    ///     "Resources/Stage/&lt;id&gt;" folder to test real prefabs instead.
    ///
    /// NOT live-tunable here -- these are compile-time consts by design (so
    /// AC5's fast-suite regression lock has fixed values to check against).
    /// Edit the constant and re-enter Play mode to retune:
    ///   - Per-rig FOV/height/pitch/distance: Barcade.Presentation.CameraRigMath's
    ///     nested StageCameraDefaults class.
    ///   - Avatar marker size/height/meter-pulse-scale:
    ///     Barcade.Framework.Stage.AvatarMarkerFactory.
    ///   - Shake peak amplitude/duration: Barcade.Presentation.CameraRigMath's
    ///     nested CameraShake class.
    /// </summary>
    public sealed class StagePresenterUatDriver : MonoBehaviour
    {
        [Header("Wiring (set by StagePresenterUatSceneBuilder)")]
        public StagePresenter Presenter;

        [Header("Camera rig -- live, switch anytime while playing")]
        [SerializeField] private UatCameraRig _cameraRig = UatCameraRig.TopDownOrtho;

        [Header("Plane mapping -- live, real StageProfile data (not a const)")]
        [SerializeField] private float _worldSizeX = 10f;
        [SerializeField] private float _worldSizeZ = 10f;
        [SerializeField] private float _worldOriginY = 0f;

        [Header("Entity prefab set (leave empty to force the primitive fallback for every EntityKind)")]
        [SerializeField] private string _entityPrefabSetId = string.Empty;

        private StagePresenterUatMicrogame _microgame;

        // Tracks what the last Rebuild() actually bound, so Update() only
        // rebinds when an Inspector value genuinely changed.
        private UatCameraRig _boundRig;
        private float _boundWorldSizeX, _boundWorldSizeZ, _boundWorldOriginY;
        private string _boundPrefabSetId;
        private bool _everBound;

        private void Start()
        {
            _microgame = new StagePresenterUatMicrogame();
            _microgame.Initialize(new Barcade.Core.SeededRandom(1), PlayerRoster.AllHuman, difficultyMult: 1f);
            Rebuild();
        }

        private void Update()
        {
            if (_microgame == null) return;

            // The stub ignores its input entirely -- see StagePresenterUatMicrogame's doc.
            _microgame.Tick(default);

            if (!_everBound
                || _cameraRig != _boundRig
                || _worldSizeX != _boundWorldSizeX
                || _worldSizeZ != _boundWorldSizeZ
                || _worldOriginY != _boundWorldOriginY
                || _entityPrefabSetId != _boundPrefabSetId)
            {
                Rebuild();
            }
        }

        /// <summary>Also invocable from the Inspector's component context menu, for a manual re-trigger.</summary>
        [ContextMenu("Rebuild")]
        private void Rebuild()
        {
            if (Presenter == null || _microgame == null) return;

            var profile = new StageProfile(_cameraRig.ToStageProfileString(), string.Empty, _entityPrefabSetId)
            {
                PlaneMapping = new PlaneMapping(_worldSizeX, _worldSizeZ, worldOriginX: 0f, worldOriginY: _worldOriginY, worldOriginZ: 0f),
            };

            Presenter.Bind(_microgame, profile);

            _boundRig = _cameraRig;
            _boundWorldSizeX = _worldSizeX;
            _boundWorldSizeZ = _worldSizeZ;
            _boundWorldOriginY = _worldOriginY;
            _boundPrefabSetId = _entityPrefabSetId;
            _everBound = true;
        }
    }

    /// <summary>UAT-only mirror of Barcade.Presentation.CameraRigKind, as a plain Inspector-friendly enum.</summary>
    public enum UatCameraRig
    {
        TopDownOrtho,
        FrontFixed,
        RunnerLateral,
        BoardOverview,
    }

    public static class UatCameraRigExtensions
    {
        public static string ToStageProfileString(this UatCameraRig rig)
        {
            switch (rig)
            {
                case UatCameraRig.TopDownOrtho: return "topDownOrtho";
                case UatCameraRig.FrontFixed: return "frontFixed";
                case UatCameraRig.RunnerLateral: return "runnerLateral";
                case UatCameraRig.BoardOverview: return "boardOverview";
                default: throw new System.ArgumentOutOfRangeException(nameof(rig));
            }
        }
    }
}
