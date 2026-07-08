using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Presentation;

namespace Barcade.Framework.Stage
{
    /// <summary>
    /// Generalizes the existing Esquiva-3D demo (<see cref="Barcade.Framework.DodgeGameBootstrap"/>)
    /// into a reusable presentation layer for ANY v2 <see cref="IMicrogame"/>
    /// (GDD §10.4, TASK-027): projects <see cref="RenderState"/> entities from
    /// logical space into the 3D world per the active <see cref="StageProfile"/>,
    /// places one of the 4 camera rigs at MG_INTRO and keeps it fixed during
    /// MG_PLAY (shake/runnerLateral excepted), and gives player avatars their
    /// color + overhead shape marker (§8.1).
    ///
    /// All the actual math (projection, interpolation, camera placement, shake,
    /// seat visuals) lives in the UnityEngine-free <c>Barcade.Presentation</c>
    /// module so it's regression-locked in the fast dotnet suite (AC5) — this
    /// class is thin MonoBehaviour glue over it, per the repo's thin-MonoBehaviour
    /// principle. It is Unity-compile-gated: not verified by the Developer agent
    /// that authored it, only by the Unity Editor compile step.
    ///
    /// Usage: call <see cref="Bind"/> once at MG_INTRO with the round's
    /// <see cref="IMicrogame"/> and its <see cref="StageProfile"/>; this
    /// component then drives itself every <c>Update</c>. Call <see cref="Unbind"/>
    /// when the round ends (releases pooled instances, hides avatar markers).
    ///
    /// NOTE (integration): no existing controller wires a v2 <see cref="IMicrogame"/>
    /// into the current v1 <c>MicrogameLoopController</c>/<c>MicrogameHost</c> loop
    /// yet (they still run the v1 <c>Barcade.Core.IMicrogame</c> contract) — that
    /// wiring is out of this ticket's scope (see hand-off). This component is a
    /// self-contained, independently bindable presenter ready for whichever
    /// ticket does that wiring.
    /// </summary>
    public sealed class StagePresenter : MonoBehaviour
    {
        // ── GDD §10.3: fixed 60 Hz sim tick ──────────────────────────────────────
        private const float TickDurationSeconds = 1f / 60f;

        [Tooltip("Optional pre-placed Camera. If left unassigned, one is created as a child at Bind time.")]
        [SerializeField] private Camera _camera;

        // ── Bound round state ─────────────────────────────────────────────────
        private IMicrogame _source;
        private StageProfile _profile;
        private CameraRigKind _rigKind;
        private CameraPlacement _fixedPlacement;
        private StageEntityFactory _entityFactory;
        private bool _bound;

        // ── GDD §10.3 double buffer (two owned snapshots, swapped on each new tick) ──
        private readonly RenderStateSnapshot _bufferA = new RenderStateSnapshot();
        private readonly RenderStateSnapshot _bufferB = new RenderStateSnapshot();
        private RenderStateSnapshot _current;
        private RenderStateSnapshot _previous;
        private float _lastTickRealTime;

        // ── Pooled per-slot views (index = RenderState.Entities slot) ───────────
        private readonly List<StageEntityView> _activeViews = new List<StageEntityView>();

        // ── Avatar overhead markers, one per seat (GDD §8.1 shape channel) ──────
        private readonly GameObject[] _avatarMarkers = new GameObject[4];
        private readonly StageEntityView[] _avatarViewBySeat = new StageEntityView[4]; // per-frame scratch, reused (no alloc)

        // ── Feedback shake (GDD §8.2: "Screen shake <= 300 ms" at nivel Alto) ────
        private float _shakeStartTime = -1f;
        private float _shakeDurationSeconds;
        private float _shakePeakAmplitude;

        /// <summary>The Camera this presenter drives (created at <see cref="Bind"/> if none was assigned in the Inspector).</summary>
        public Camera Camera => _camera;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Places the camera and prepares pooling for a new round (GDD §10.4
        /// "cámara por fase" — this is the MG_INTRO placement). Call once per
        /// round, before MG_PLAY begins.
        /// </summary>
        public void Bind(IMicrogame source, StageProfile profile)
        {
            _source = source;
            _profile = profile ?? new StageProfile();

            if (!CameraRigKindCodec.TryParse(_profile.Camera, out _rigKind))
            {
                Debug.LogWarning(
                    $"[StagePresenter] Unknown stageProfile.camera '{_profile.Camera}' — defaulting to topDownOrtho.");
                _rigKind = CameraRigKind.TopDownOrtho;
            }

            EnsureCamera();
            _fixedPlacement = CameraRigMath.ComputeFixedPlacement(_rigKind, _profile.PlaneMapping);
            StageVectorMath.ApplyCameraPlacement(_camera, _fixedPlacement, Vector3.zero);

            _entityFactory = new StageEntityFactory(_profile.EntityPrefabSet, transform);
            EnsureAvatarMarkers();

            // Seed both buffers with the same first tick so nothing appears to
            // move on the very first rendered frame (previous == current).
            _current = _bufferA;
            _previous = _bufferB;
            RenderState initial = _source.GetRenderState();
            _current.CopyFrom(initial);
            _previous.CopyFrom(initial);
            _lastTickRealTime = Time.unscaledTime;

            _shakeStartTime = -1f;
            _bound = true;
        }

        /// <summary>
        /// Destroys this round's active instances and hides avatar markers.
        /// Call when the round ends. Destroys rather than pools its views:
        /// <see cref="Bind"/> replaces <see cref="_entityFactory"/> with a fresh
        /// one for the next round (a different microgame can declare a
        /// different <c>entityPrefabSet</c>, so pooled instances must not
        /// survive across rounds) — pooling into a factory about to be
        /// discarded would just leak GameObjects instead of reusing them.
        /// Within a round, steady-state <see cref="Present"/> still does zero
        /// per-frame heap allocations once pooling has warmed up (GDD §14) —
        /// this per-round teardown/setup cost is not part of that steady state.
        /// </summary>
        public void Unbind()
        {
            _bound = false;
            _source = null;

            for (int i = 0; i < _activeViews.Count; i++)
            {
                if (_activeViews[i] == null) continue;
                Destroy(_activeViews[i].GameObject);
                _activeViews[i] = null;
            }

            for (int seat = 0; seat < 4; seat++)
            {
                if (_avatarMarkers[seat] != null) _avatarMarkers[seat].SetActive(false);
                _avatarViewBySeat[seat] = null;
            }
        }

        /// <summary>
        /// Advances presentation by one render frame: polls for a new sim tick,
        /// interpolates entities, and drives the camera. Called automatically
        /// from <c>Update</c> while bound; exposed publicly so callers/tests can
        /// drive it manually (same testability-seam convention as
        /// <c>MicrogameHost.Tick</c>).
        /// </summary>
        public void Present()
        {
            if (!_bound || _source == null) return;

            RenderState live = _source.GetRenderState();
            PollTick(live);

            float tickFactor = RenderInterpolation.ComputeTickFactor(
                Time.unscaledTime - _lastTickRealTime, TickDurationSeconds);

            UpdateCamera(tickFactor);
            UpdateEntities(tickFactor);
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update() => Present();

        // ── Double buffer ─────────────────────────────────────────────────────

        private void PollTick(RenderState live)
        {
            if (_current.HasData && live.Tick == _current.Tick) return;

            // The buffer currently labeled "previous" holds the oldest data —
            // safe to overwrite it with the fresh tick, then swap the labels.
            // Zero-allocation: CopyFrom reuses its own arrays.
            RenderStateSnapshot stale = _previous;
            stale.CopyFrom(live);
            _previous = _current;
            _current = stale;
            _lastTickRealTime = Time.unscaledTime;

            CheckForShakeTrigger();
        }

        private void CheckForShakeTrigger()
        {
            for (int i = 0; i < _current.FeedbackCount; i++)
            {
                if (_current.Feedback[i].Level != FeedbackLevel.High) continue;

                _shakeStartTime = Time.unscaledTime;
                _shakeDurationSeconds = CameraShake.MaxDurationSeconds;
                _shakePeakAmplitude = CameraShake.DefaultPeakAmplitude;
                break;
            }
        }

        // ── Camera ────────────────────────────────────────────────────────────

        private void EnsureCamera()
        {
            if (_camera != null) return;

            var cameraGO = new GameObject("StageCamera");
            cameraGO.transform.SetParent(transform, worldPositionStays: false);
            _camera = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
        }

        private void UpdateCamera(float tickFactor)
        {
            Vector3 shakeOffset = ComputeShakeOffset();

            if (_rigKind == CameraRigKind.RunnerLateral)
            {
                // The sole rig that moves during MG_PLAY (GDD §10.4).
                float progress = FindLeadRunnerProgress();
                CameraPlacement placement = CameraRigMath.RunnerLateral(_profile.PlaneMapping, progress);
                StageVectorMath.ApplyCameraPlacement(_camera, placement, shakeOffset);
            }
            else
            {
                // "Cámara por fase, no por frame" (§10.4): re-apply the SAME
                // fixed MG_INTRO placement every frame — only the shake offset
                // (<=300ms) ever changes it.
                StageVectorMath.ApplyCameraPlacement(_camera, _fixedPlacement, shakeOffset);
            }
        }

        /// <summary>[ASSUMED] "leading" = the greatest logical X (track-progress axis) among active PlayerAvatar entities this tick.</summary>
        private float FindLeadRunnerProgress()
        {
            float best = 0f;
            bool any = false;
            for (int i = 0; i < _current.EntityCount; i++)
            {
                if (_current.Entities[i].Kind != EntityKind.PlayerAvatar) continue;
                float x = _current.Entities[i].X;
                if (!any || x > best) { best = x; any = true; }
            }
            return any ? best : 0f;
        }

        private Vector3 ComputeShakeOffset()
        {
            if (_shakeStartTime < 0f) return Vector3.zero;

            float elapsed = Time.unscaledTime - _shakeStartTime;
            float amplitude = CameraShake.Amplitude(elapsed, _shakeDurationSeconds, _shakePeakAmplitude);
            if (amplitude <= 0f) return Vector3.zero;

            // Cosmetic-only jitter (GDD §10.4: gameplay resolution never depends
            // on non-deterministic sources) — a fresh random direction each
            // frame reads as a shake rather than a directional nudge.
            Vector2 dir = Random.insideUnitCircle.normalized;
            return new Vector3(dir.x, dir.y, 0f) * amplitude;
        }

        // ── Entities ──────────────────────────────────────────────────────────

        private void EnsureAvatarMarkers()
        {
            for (int seat = 0; seat < 4; seat++)
            {
                if (_avatarMarkers[seat] != null) continue;

                var slot = (PlayerSlot)seat;
                _avatarMarkers[seat] = AvatarMarkerFactory.Create(slot, SeatVisuals.ShapeFor(slot), transform);
                _avatarMarkers[seat].SetActive(false);
            }
        }

        private void UpdateEntities(float tickFactor)
        {
            int count = _current.EntityCount;

            while (_activeViews.Count < count) _activeViews.Add(null);

            for (int seat = 0; seat < 4; seat++) _avatarViewBySeat[seat] = null;

            for (int i = 0; i < count; i++)
            {
                RenderEntity current = _current.Entities[i];
                RenderEntity previous = i < _previous.EntityCount ? _previous.Entities[i] : current;

                StageEntityView view = _activeViews[i];
                if (view == null || view.Kind != current.Kind || view.Variant != current.VisualVariant)
                {
                    if (view != null) _entityFactory.Release(view);
                    view = _entityFactory.Acquire(current.Kind, current.VisualVariant);
                    _activeViews[i] = view;
                }

                view.Transform.position = StageVectorMath.LerpPosition(previous, current, _profile.PlaneMapping, tickFactor);
                view.Transform.rotation = StageVectorMath.LerpRotation(previous, current, tickFactor);

                float scale = StageVectorMath.LerpScale(previous, current, tickFactor);
                view.Transform.localScale = Vector3.one * (scale > 0f ? scale : 1f);

                if (current.Kind == EntityKind.PlayerAvatar && current.OwnerSeat >= 0 && current.OwnerSeat < 4)
                {
                    var slot = (PlayerSlot)current.OwnerSeat;
                    view.ApplyTint(StageVectorMath.ToUnityColor(SeatVisuals.ColorFor(slot)));
                    _avatarViewBySeat[current.OwnerSeat] = view;
                }
            }

            // Release any views left over from a previous, larger tick.
            for (int i = count; i < _activeViews.Count; i++)
            {
                if (_activeViews[i] == null) continue;
                _entityFactory.Release(_activeViews[i]);
                _activeViews[i] = null;
            }

            UpdateAvatarMarkers();
        }

        private void UpdateAvatarMarkers()
        {
            for (int seat = 0; seat < 4; seat++)
            {
                StageEntityView view = _avatarViewBySeat[seat];
                GameObject marker = _avatarMarkers[seat];
                if (marker == null) continue;

                if (view == null)
                {
                    marker.SetActive(false);
                    continue;
                }

                marker.SetActive(true);
                if (marker.transform.parent != view.Transform)
                    marker.transform.SetParent(view.Transform, worldPositionStays: false);

                // [ASSUMED] HudState.Meters (per-seat continuous charge/progress,
                // TASK-048) pulses the marker's scale — the presenter's generic
                // contract for consuming that channel visually.
                float meter = _current.HudMeters[seat];
                float scale = AvatarMarkerFactory.BaseSize * (1f + meter * AvatarMarkerFactory.MeterPulseScale);
                marker.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
