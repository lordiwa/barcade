using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Pure deterministic state engine that drives the microgame session loop.
    ///
    /// Responsibilities:
    ///   1. <b>Anti-repeat selection</b> — picks the next microgame from the registered
    ///      pool via <see cref="ISeededRandom"/>, guaranteeing the same definition is
    ///      never picked twice in a row. If the pool has exactly one entry that entry is
    ///      always returned (no infinite-loop guard needed — the anti-repeat condition is
    ///      skipped for single-item pools).
    ///   2. <b>Speed ramp</b> — computes effective duration and speed multiplier each
    ///      round using <see cref="RampSettings.EffectiveDuration"/> /
    ///      <see cref="RampSettings.SpeedMultiplier"/>.
    ///   3. <b>Score accumulation</b> — after each round <see cref="AdvanceRound"/>
    ///      calls <see cref="ScoreModel.RecordRound"/> with the per-player results.
    ///
    /// Usage per round:
    /// <code>
    ///   MicrogameDescriptor desc = director.PickNext();
    ///   SequencerRoundContext ctx = director.CurrentRoundContext;
    ///   // ... run the microgame using ctx.EffectiveDuration, ctx.SpeedMultiplier ...
    ///   director.AdvanceRound(results); // bool[4]
    /// </code>
    ///
    /// Thread-safety: not thread-safe — single-threaded game loop use only.
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SequencerDirector
    {
        // ── Configuration ────────────────────────────────────────────────────────

        private readonly IReadOnlyList<MicrogameDescriptor> _pool;
        private readonly ISeededRandom _rng;
        private readonly RampSettings  _ramp;

        // ── Per-session state ────────────────────────────────────────────────────

        private int _roundIndex;
        private int _lastPickedIndex;  // index into _pool; -1 = none picked yet
        private SequencerRoundContext _currentRoundContext;
        private readonly ScoreModel _score;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="SequencerDirector"/>.
        /// </summary>
        /// <param name="pool">
        /// Non-empty list of microgame descriptors available for selection.
        /// The order is used for index-based anti-repeat tracking.
        /// </param>
        /// <param name="rng">
        /// Seeded RNG — inject the same seed to replay an identical session.
        /// </param>
        /// <param name="ramp">Speed ramp curve; defaults to <see cref="RampSettings.Default"/>.</param>
        public SequencerDirector(
            IReadOnlyList<MicrogameDescriptor> pool,
            ISeededRandom rng,
            RampSettings ramp = null)
        {
            if (pool == null)       throw new ArgumentNullException("pool");
            if (pool.Count == 0)    throw new ArgumentException("pool must not be empty", "pool");
            if (rng == null)        throw new ArgumentNullException("rng");

            _pool            = pool;
            _rng             = rng;
            _ramp            = ramp ?? RampSettings.Default;
            _roundIndex      = 0;
            _lastPickedIndex = -1;
            _score           = new ScoreModel();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Read-only access to the cumulative score model.
        /// Updated after each <see cref="AdvanceRound"/> call.
        /// </summary>
        public ScoreModel Score => _score;

        /// <summary>
        /// The round context computed by the most recent <see cref="PickNext"/> call.
        /// Null before the first <see cref="PickNext"/> call.
        /// </summary>
        public SequencerRoundContext CurrentRoundContext => _currentRoundContext;

        /// <summary>
        /// Selects the next microgame using the seeded RNG with anti-repeat protection.
        ///
        /// Anti-repeat rule: if the pool has more than one entry, the same pool index
        /// (and therefore the same <c>Id</c>) will never be returned on two consecutive
        /// calls. Re-rolls are performed internally until a different index is drawn.
        ///
        /// Single-item pool: the only entry is always returned — the anti-repeat
        /// condition is skipped to avoid an infinite loop.
        ///
        /// Also updates <see cref="CurrentRoundContext"/> with the ramp values for
        /// the current round index.
        /// </summary>
        /// <returns>The selected <see cref="MicrogameDescriptor"/>.</returns>
        public MicrogameDescriptor PickNext()
        {
            int idx;

            if (_pool.Count == 1)
            {
                // Single-item pool: always return the only entry.
                idx = 0;
            }
            else
            {
                // Anti-repeat: re-roll until we get a different index.
                do
                {
                    idx = _rng.NextInt(0, _pool.Count);
                }
                while (idx == _lastPickedIndex);
            }

            _lastPickedIndex = idx;
            MicrogameDescriptor desc = _pool[idx];

            float effDuration  = _ramp.EffectiveDuration(desc.BaseDuration, _roundIndex);
            float speedMult    = _ramp.SpeedMultiplier(desc.BaseDuration, _roundIndex);

            _currentRoundContext = new SequencerRoundContext(
                roundIndex:        _roundIndex,
                descriptor:        desc,
                effectiveDuration: effDuration,
                speedMultiplier:   speedMult);

            return desc;
        }

        /// <summary>
        /// Records per-player results into the <see cref="Score"/> model and advances
        /// the session to the next round.
        ///
        /// Must be called once after each <see cref="PickNext"/> call (and after the
        /// microgame itself has finished). Calling without a preceding <see cref="PickNext"/>
        /// is valid but unusual — the round counter still increments.
        /// </summary>
        /// <param name="results">
        /// bool[4] indexed by <c>(int)PlayerSlot</c>; true = win, false = loss.
        /// Passed directly to <see cref="ScoreModel.RecordRound"/> — validation is
        /// enforced there (null or wrong length throws).
        /// </param>
        public void AdvanceRound(bool[] results)
        {
            _score.RecordRound(results);
            _roundIndex++;
        }
    }
}
