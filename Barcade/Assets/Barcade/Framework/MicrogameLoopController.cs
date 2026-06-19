using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Thin MonoBehaviour orchestrator for the per-round microgame loop.
    ///
    /// Responsibilities:
    ///   1. Owns a <see cref="RoundPhaseMachine"/> and advances it via Update.
    ///   2. When a phase transition occurs, toggles <see cref="CommandDisplayView"/>
    ///      and <see cref="IntermissionView"/> accordingly.
    ///   3. At the start of each round calls <see cref="SequencerDirector.PickNext"/>
    ///      to select the next microgame and passes the effective duration to the
    ///      phase machine via <see cref="RoundPhaseMachine.StartRound"/>.
    ///   4. When the Play phase begins, delegates to <see cref="MicrogameHost"/> to
    ///      actually run the selected microgame.
    ///   5. TASK-007 BUG FIX: the controller now waits for the configured
    ///      <see cref="_resultDuration"/> to elapse after entering the Result phase
    ///      before advancing to the next round. Previously it called OnRoundComplete
    ///      every frame while IsRoundComplete was true, skipping the Result window.
    ///      Fix: a separate _resultElapsed timer is started on the first frame of
    ///      Result; OnRoundComplete is called only once that timer >= _resultDuration.
    ///   6. When IsRoundComplete AND the result window has elapsed, records the
    ///      MicrogameHost's real MicrogameResult into ScoreModel via SequencerDirector
    ///      and begins the next round.
    ///
    /// Designer-tunable durations are exposed as [SerializeField] for Inspector access.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class MicrogameLoopController : MonoBehaviour
    {
        // ── Designer-tunable durations ────────────────────────────────────────────

        [Header("Phase Durations")]
        [Tooltip("Duration of the intermission buffer between microgames (seconds).")]
        [SerializeField] private float _intermissionDuration = 2.0f;

        [Tooltip("Duration the giant verb is displayed before play starts (seconds).")]
        [SerializeField] private float _commandShowDuration = 1.0f;

        [Tooltip("Duration the result flash is shown after play ends (seconds).")]
        [SerializeField] private float _resultDuration = 1.5f;

        // ── Inspector references ──────────────────────────────────────────────────

        [Header("Views")]
        [SerializeField] private CommandDisplayView _commandDisplayView;
        [SerializeField] private IntermissionView   _intermissionView;

        [Header("Host")]
        [Tooltip("MicrogameHost component that runs the active microgame prefab.")]
        [SerializeField] private MicrogameHost _microgameHost;

        [Header("Input")]
        [Tooltip("ArcadeInputBridge component providing IReadOnlyPlayerInputs.")]
        [SerializeField] private ArcadeInputBridge _inputBridge;

        [Header("Microgame Pool")]
        [Tooltip("All available microgame definitions for this session.")]
        [SerializeField] private List<MicrogameDefinition> _pool;

        [Tooltip("Seed for the microgame selection RNG. 0 = use TickCount (non-deterministic).")]
        [SerializeField] private int _seed = 0;

        // ── Runtime state ─────────────────────────────────────────────────────────

        private RoundPhaseMachine   _phaseMachine;
        private SequencerDirector   _director;
        private MicrogameDescriptor _currentDescriptor;

        // TASK-007 BUG FIX state: track how long we've been in the Result phase.
        private bool  _inResultWindow;
        private float _resultElapsed;

        // Seed used for the current round's microgame (deterministic per round index).
        private int _currentRoundSeed;

        // ── Testability seam ──────────────────────────────────────────────────────

        // Allow tests to inject a pre-built director so the pool doesn't need
        // ScriptableObject assets in the PlayMode test scene.
        private SequencerDirector _injectedDirector;

        /// <summary>
        /// Injects a pre-built director and input provider for testing.
        /// Must be called before the controller's Start() runs.
        /// </summary>
        public void InjectForTest(
            SequencerDirector director,
            IReadOnlyPlayerInputs inputs)
        {
            _injectedDirector    = director;
            _testInputs          = inputs;
        }

        private IReadOnlyPlayerInputs _testInputs;

        // Expose director for test assertions.
        public SequencerDirector Director => _director;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            _director = _injectedDirector ?? BuildDirector();

            _phaseMachine = new RoundPhaseMachine(
                intermissionDuration: _intermissionDuration,
                commandShowDuration:  _commandShowDuration,
                resultDuration:       _resultDuration);

            BeginNextRound();
        }

        private void Update()
        {
            _phaseMachine.Tick(Time.deltaTime);

            if (_phaseMachine.PhaseChangedThisTick)
                ApplyPhaseViews(_phaseMachine.CurrentPhase);

            // ── TASK-007 BUG FIX ─────────────────────────────────────────────────
            // RoundPhaseMachine.IsRoundComplete is true as soon as we enter Result
            // and stays true until StartRound is called. The old code called
            // OnRoundComplete every frame here, skipping the result window entirely.
            //
            // Fix: use a separate _resultElapsed timer. On the first frame of Result
            // we arm _inResultWindow. We then count time until >= _resultDuration,
            // then call OnRoundComplete exactly once.
            if (_phaseMachine.IsRoundComplete)
            {
                if (!_inResultWindow)
                {
                    // First frame entering Result — arm the window.
                    _inResultWindow = true;
                    _resultElapsed  = 0f;
                }

                _resultElapsed += Time.deltaTime;

                if (_resultElapsed >= _resultDuration)
                {
                    _inResultWindow = false;
                    OnRoundComplete();
                }
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private SequencerDirector BuildDirector()
        {
            int actualSeed = (_seed == 0) ? System.Environment.TickCount : _seed;
            var rng = new SeededRandom(actualSeed);

            var descriptors = new List<MicrogameDescriptor>(_pool != null ? _pool.Count : 0);
            if (_pool != null)
            {
                foreach (MicrogameDefinition def in _pool)
                {
                    if (def == null) continue;
                    descriptors.Add(new MicrogameDescriptor(
                        id:           def.id,
                        baseDuration: def.baseDuration,
                        difficulty:   def.difficulty));
                }
            }

            if (descriptors.Count == 0)
            {
                descriptors.Add(new MicrogameDescriptor("esquiva", 5f, 1));
                Debug.LogWarning("[MicrogameLoopController] Pool is empty — using 'esquiva' placeholder.");
            }

            return new SequencerDirector(descriptors, rng, RampSettings.Default);
        }

        private void BeginNextRound()
        {
            _currentDescriptor = _director.PickNext();
            SequencerRoundContext ctx = _director.CurrentRoundContext;

            string verbText = ResolveVerbText(_currentDescriptor.Id);
            _phaseMachine.StartRound(verbText, ctx.EffectiveDuration);

            // Compute a deterministic seed for this round's microgame from the
            // round index. Keeps replays reproducible if _seed is non-zero.
            _currentRoundSeed = _seed != 0
                ? _seed ^ ctx.RoundIndex
                : System.Environment.TickCount ^ ctx.RoundIndex;

            // Apply the initial phase view (Intermission).
            ApplyPhaseViews(_phaseMachine.CurrentPhase);
        }

        private void ApplyPhaseViews(PhaseKind phase)
        {
            if (_commandDisplayView != null) _commandDisplayView.Hide();
            if (_intermissionView   != null) _intermissionView.Hide();

            switch (phase)
            {
                case PhaseKind.Intermission:
                    if (_intermissionView != null)
                        _intermissionView.Show();
                    break;

                case PhaseKind.CommandShow:
                    if (_commandDisplayView != null)
                        _commandDisplayView.Show(_phaseMachine.VerbText);
                    break;

                case PhaseKind.Play:
                    StartMicrogame();
                    break;

                case PhaseKind.Result:
                    // Both display views hidden; result indicators shown by the host/HUD.
                    break;
            }
        }

        private void StartMicrogame()
        {
            if (_microgameHost == null)
            {
                Debug.LogWarning("[MicrogameLoopController] No MicrogameHost assigned.");
                return;
            }

            IReadOnlyPlayerInputs inputs = _testInputs
                ?? (_inputBridge != null ? _inputBridge.AsReadOnly() : null);

            if (inputs == null)
            {
                Debug.LogWarning("[MicrogameLoopController] No input bridge assigned — using zero inputs.");
                inputs = new ZeroInputs();
            }

            SequencerRoundContext ctx = _director.CurrentRoundContext;

            _microgameHost.StartRound(
                microgameId:  _currentDescriptor.Id,
                seed:         _currentRoundSeed,
                playDuration: ctx.EffectiveDuration,
                inputs:       inputs);
        }

        private void OnRoundComplete()
        {
            // Collect real results from the host; fall back to all-loss if no host.
            bool[] results;
            if (_microgameHost != null && _microgameHost.IsComplete && _microgameHost.LastResult != null)
            {
                MicrogameResult r = _microgameHost.LastResult;
                results = new bool[4];
                results[(int)PlayerSlot.Rojo]     = r.IsWin(PlayerSlot.Rojo);
                results[(int)PlayerSlot.Azul]     = r.IsWin(PlayerSlot.Azul);
                results[(int)PlayerSlot.Amarillo] = r.IsWin(PlayerSlot.Amarillo);
                results[(int)PlayerSlot.Verde]    = r.IsWin(PlayerSlot.Verde);
            }
            else
            {
                results = new bool[] { false, false, false, false };
            }

            _director.AdvanceRound(results);
            BeginNextRound();
        }

        private string ResolveVerbText(string id)
        {
            if (_pool != null)
            {
                foreach (MicrogameDefinition def in _pool)
                {
                    if (def != null && def.id == id)
                        return def.verbText ?? id;
                }
            }
            return id;
        }

        // ── Null-input fallback ───────────────────────────────────────────────────

        /// <summary>
        /// Returns zero-state snapshots for all players when no input bridge is available.
        /// Used as a safe fallback so the loop never crashes on a missing bridge reference.
        /// </summary>
        private sealed class ZeroInputs : IReadOnlyPlayerInputs
        {
            private static readonly InputSnapshot Zero = new InputSnapshot(0f, 0f, ButtonState.Released);
            public InputSnapshot For(PlayerSlot slot) => Zero;
        }
    }
}
