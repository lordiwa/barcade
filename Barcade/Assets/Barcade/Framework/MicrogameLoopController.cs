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
    ///   4. When the Play phase begins, loads and runs the microgame prefab (stub).
    ///   5. When IsRoundComplete, records results and begins the next round.
    ///
    /// Designer-tunable durations are exposed as [SerializeField] so they can be
    /// adjusted in the Inspector without touching code.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// Individual microgames do NOT manage intermission or command display —
    /// this controller owns those phases (AC4).
    ///
    /// Unity integration compile verified in the batched integration pass.
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

        [Header("Microgame Pool")]
        [Tooltip("All available microgame definitions for this session.")]
        [SerializeField] private List<MicrogameDefinition> _pool;

        [Tooltip("Seed for the microgame selection RNG. 0 = use TickCount (non-deterministic).")]
        [SerializeField] private int _seed = 0;

        // ── Runtime state ─────────────────────────────────────────────────────────

        private RoundPhaseMachine  _phaseMachine;
        private SequencerDirector  _director;
        private PhaseKind          _previousPhase;
        private MicrogameDescriptor _currentDescriptor;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            InitialiseDirector();
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

            if (_phaseMachine.IsRoundComplete)
                OnRoundComplete();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void InitialiseDirector()
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
                // Fallback: single placeholder so the loop doesn't crash on an empty pool.
                descriptors.Add(new MicrogameDescriptor("placeholder", 5f, 1));
                Debug.LogWarning("[MicrogameLoopController] Pool is empty — using placeholder descriptor.");
            }

            _director = new SequencerDirector(descriptors, rng, RampSettings.Default);
        }

        private void BeginNextRound()
        {
            // Pick next microgame from the sequencer.
            _currentDescriptor = _director.PickNext();
            SequencerRoundContext ctx = _director.CurrentRoundContext;

            // Resolve the verb text from the MicrogameDefinition asset.
            string verbText = ResolveVerbText(_currentDescriptor.Id);

            // Reset the phase machine with the effective play duration for this round.
            _phaseMachine.StartRound(verbText, ctx.EffectiveDuration);

            // Apply the initial phase view (Intermission).
            ApplyPhaseViews(_phaseMachine.CurrentPhase);
        }

        private void ApplyPhaseViews(PhaseKind phase)
        {
            // Hide everything first, then show what's appropriate.
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
                    // Both views hidden; microgame prefab is visible.
                    // TODO: load and run the microgame prefab via Addressables.
                    break;

                case PhaseKind.Result:
                    // TODO: show result flash UI.
                    break;
            }
        }

        private void OnRoundComplete()
        {
            // Collect results (stub: all-loss until microgame integration provides real results).
            bool[] results = new bool[] { false, false, false, false };
            _director.AdvanceRound(results);

            // Start the next round immediately.
            BeginNextRound();
        }

        /// <summary>
        /// Finds the MicrogameDefinition matching <paramref name="id"/> and returns its
        /// verbText. Falls back to the id string if no matching definition is found.
        /// </summary>
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
    }
}
