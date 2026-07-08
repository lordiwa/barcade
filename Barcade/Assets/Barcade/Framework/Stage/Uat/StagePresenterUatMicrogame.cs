// ============================================================================
// THROWAWAY / DEV-ONLY UAT SCAFFOLDING -- TASK-027 (StagePresenter 3D visual
// UAT). Lives in the Editor-only Barcade.Framework.Uat assembly; never ships
// in a Player build. Does not modify production StagePresenter/
// Barcade.Presentation code or the ratified design.
// ============================================================================
using System;
using Barcade.Core.Microgames.V2;

namespace Barcade.Framework.Stage.Uat
{
    /// <summary>
    /// A stub v2 IMicrogame that emits just enough RenderState to exercise
    /// every StagePresenter visual a human needs to eyeball -- NOT a real
    /// mechanic: ignores input, never finishes, GetResult() is meaningless.
    /// Driven by <see cref="StagePresenterUatDriver"/>.
    ///
    /// Deliberately has NO "using Barcade.Core;" -- only
    /// Barcade.Core.Microgames.V2 (v2) is wildcard-imported here, so every
    /// plain type name in this file is unambiguous by construction.
    /// (Barcade.Core (v1) and Barcade.Core.Microgames.V2 (v2) both declare
    /// IMicrogame/MicrogameResult/InputSnapshot -- the exact ambiguity that
    /// broke the first StagePresenter compile gate. The one v1 type this stub
    /// needs, SeededRandom, is fully qualified below instead of risking a
    /// second wildcard.)
    ///
    /// Emits (every Tick, all in logical [0,1]^2 unless noted):
    ///  - 4 PlayerAvatar entities (one per seat), each orbiting a small circle
    ///    phase-offset by seat, with a periodic hop on Height -- exercises the
    ///    GDD §10.3 double-buffer interpolation on X/Y AND Height at once, plus
    ///    the seat color+shape channel (§8.1) for all 4 seats.
    ///  - One entity of each of the other 5 EntityKinds (Hazard/Projectile/
    ///    Target/BoardPawn/Pickup), spread to the 4 corners + centre,
    ///    stationary -- with the driver's entityPrefabSet left empty,
    ///    StageEntityFactory never resolves a prefab for these, so every one
    ///    exercises StagePrimitiveFactory's fallback.
    ///  - A sawtooth Progress01 on the avatars, so
    ///    StageVectorMath.LerpRotation's Progress01-overrides-tick-factor path
    ///    (TASK-027's rotation-smoothing contract) is visibly exercised too.
    ///  - HudState.Meters oscillating per seat, so the avatar marker's meter
    ///    pulse (also TASK-027) is visible.
    ///  - A FeedbackEvent at Level.High every ~4 seconds, so the camera-shake
    ///    path (GDD §8.2, &lt;=300ms) fires periodically without a real mechanic.
    /// </summary>
    public sealed class StagePresenterUatMicrogame : IMicrogame
    {
        private readonly RenderState _renderState = new RenderState(entityCapacity: 16, feedbackCapacity: 4);
        private int _tick;

        public MicrogameId Id => MicrogameId.Esquiva; // arbitrary -- unused by the harness

        public void Initialize(Barcade.Core.SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            _tick = 0;
        }

        public bool IsFinished => false; // runs indefinitely for UAT

        public MicrogameResult GetResult() => new MicrogameResult(ResultKind.CoopFail, Array.Empty<PlayerRank>(), 0);

        public void Tick(in InputSnapshot input)
        {
            _tick++;
            float t = _tick / 60f; // seconds, assuming ~60 Tick calls/sec from the driver

            int entityCount = 0;

            for (int seat = 0; seat < 4; seat++)
            {
                float phase = t + seat * (MathF.PI * 0.5f);
                ref RenderEntity e = ref _renderState.Entities[entityCount++];
                e.Kind = EntityKind.PlayerAvatar;
                e.OwnerSeat = seat;
                e.X = 0.5f + 0.25f * MathF.Cos(phase);
                e.Y = 0.5f + 0.25f * MathF.Sin(phase);
                e.Height = MathF.Max(0f, MathF.Sin(phase * 2f)) * 1.5f;
                e.Rotation = Repeat(phase * (180f / MathF.PI), 360f);
                e.Scale = 1f;
                e.VisualVariant = 0;
                e.Progress01 = Repeat(t * 0.5f, 1f);
            }

            SetNeutralEntity(ref entityCount, EntityKind.Hazard, 0.15f, 0.15f);
            SetNeutralEntity(ref entityCount, EntityKind.Projectile, 0.85f, 0.15f);
            SetNeutralEntity(ref entityCount, EntityKind.Target, 0.15f, 0.85f);
            SetNeutralEntity(ref entityCount, EntityKind.BoardPawn, 0.85f, 0.85f);
            SetNeutralEntity(ref entityCount, EntityKind.Pickup, 0.5f, 0.5f);

            _renderState.EntityCount = entityCount;
            _renderState.Tick = _tick;

            _renderState.Hud.ActiveVerb = "¡UAT!";
            for (int seat = 0; seat < 4; seat++)
            {
                _renderState.Hud.Scores[seat] = 0;
                _renderState.Hud.Meters[seat] = 0.5f + 0.5f * MathF.Sin(t + seat);
            }

            // Fires a High-level feedback event every ~4 seconds so the camera
            // shake path is exercised without needing a real mechanic.
            bool shakeTick = (_tick % 240) == 0;
            _renderState.FeedbackCount = shakeTick ? 1 : 0;
            if (shakeTick)
                _renderState.Feedback[0] = new FeedbackEvent(seat: -1, level: FeedbackLevel.High, cue: 0);
        }

        public RenderState GetRenderState() => _renderState;

        private void SetNeutralEntity(ref int entityCount, EntityKind kind, float x, float y)
        {
            ref RenderEntity e = ref _renderState.Entities[entityCount++];
            e.Kind = kind;
            e.OwnerSeat = -1;
            e.X = x;
            e.Y = y;
            e.Height = 0f;
            e.Rotation = 0f;
            e.Scale = 1f;
            e.VisualVariant = 0;
            e.Progress01 = 0f;
        }

        private static float Repeat(float value, float length) => value - MathF.Floor(value / length) * length;
    }
}
