using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-007 — RoundPhaseMachine: deterministic timing and phase-transition tests.
    ///
    /// AC1: CommandShow phase lasts ~1 s (configurable); exposes verb text.
    /// AC2: Intermission phase has a configurable duration.
    /// AC3: All timing is testable in pure C# (no UnityEngine).
    /// AC4: Phase machine drives the loop; individual microgames do not manage phases.
    ///
    /// Phase order per round: Intermission -> CommandShow -> Play -> Result.
    /// </summary>
    [TestFixture]
    public class RoundPhaseMachineTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private const float IntermissionDur = 2.0f;
        private const float CommandShowDur  = 1.0f;
        private const float PlayDur         = 4.0f;
        private const float ResultDur       = 1.5f;

        private static RoundPhaseMachine MakeMachine(
            float intermissionDuration = IntermissionDur,
            float commandShowDuration  = CommandShowDur,
            float resultDuration       = ResultDur)
        {
            return new RoundPhaseMachine(
                intermissionDuration: intermissionDuration,
                commandShowDuration:  commandShowDuration,
                resultDuration:       resultDuration);
        }

        // ── AC2: Initial state is Intermission ───────────────────────────────────

        [Test]
        public void InitialPhase_IsIntermission()
        {
            var m = MakeMachine();
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission));
        }

        // ── AC2: Intermission configurable duration ───────────────────────────────

        [Test]
        public void Intermission_EndsBefore_WhenDurationElapses()
        {
            var m = MakeMachine(intermissionDuration: 2.0f);
            m.StartRound("¡ESQUIVA!", PlayDur);

            // Just before the end — still Intermission
            m.Tick(1.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission));
        }

        [Test]
        public void Intermission_TransitionsToCommandShow_ExactlyAtDuration()
        {
            var m = MakeMachine(intermissionDuration: 2.0f);
            m.StartRound("¡ESQUIVA!", PlayDur);

            m.Tick(2.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow));
        }

        [Test]
        public void Intermission_CustomDuration_Respected()
        {
            // Use a 3.5s intermission
            var m = MakeMachine(intermissionDuration: 3.5f);
            m.StartRound("¡CORRE!", PlayDur);

            m.Tick(3.4f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission),
                "Should still be in Intermission at 3.4 s when duration is 3.5 s");

            m.Tick(0.2f); // total 3.6 s >= 3.5 s
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow));
        }

        // ── AC1: CommandShow lasts ~1 s (configurable) ───────────────────────────

        [Test]
        public void CommandShow_LastsConfiguredDuration_Default1s()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 1.0f);
            m.StartRound("¡TOCA!", PlayDur);

            // Intermission=0 -> immediately in CommandShow
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow));

            m.Tick(0.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow),
                "Should still be CommandShow at 0.9 s");

            m.Tick(0.1f); // total 1.0 s
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));
        }

        [Test]
        public void CommandShow_CustomDuration_Respected()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 2.5f);
            m.StartRound("¡SALTA!", PlayDur);

            m.Tick(2.4f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow));

            m.Tick(0.2f); // total 2.6 s
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));
        }

        // ── AC1: VerbText exposed during CommandShow ──────────────────────────────

        [Test]
        public void VerbText_IsAvailable_AfterStartRound()
        {
            var m = MakeMachine();
            m.StartRound("¡ESQUIVA!", PlayDur);
            Assert.That(m.VerbText, Is.EqualTo("¡ESQUIVA!"));
        }

        [Test]
        public void VerbText_PersistsThroughAllPhases()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);
            m.StartRound("¡AGACHA!", PlayDur);

            // Already in Play with 0-duration intermission and command
            Assert.That(m.VerbText, Is.EqualTo("¡AGACHA!"),
                "VerbText must persist even during Play phase");
        }

        // ── Phase order: Intermission -> CommandShow -> Play -> Result ────────────

        [Test]
        public void PhaseOrder_FullRound_AllPhasesTravelled()
        {
            var m = MakeMachine(
                intermissionDuration: 1.0f,
                commandShowDuration:  1.0f,
                resultDuration:       1.0f);
            m.StartRound("¡CORRE!", playDuration: 2.0f);

            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission), "0: Intermission");

            m.Tick(1.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow), "after 1s: CommandShow");

            m.Tick(1.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play), "after 2s: Play");

            m.Tick(2.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result), "after 4s: Result");

            m.Tick(1.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result), "after 5s: still Result (terminal)");
        }

        // ── Play phase uses playDuration from StartRound ───────────────────────

        [Test]
        public void Play_EndsBefore_WhenPlayDurationElapses()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);
            m.StartRound("¡BLOQUEA!", playDuration: 3.0f);

            // Starting in Play (0-duration predecessors)
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));

            m.Tick(2.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play),
                "Still Play at 2.9 s with 3.0 s limit");
        }

        [Test]
        public void Play_TransitionsToResult_ExactlyAtPlayDuration()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);
            m.StartRound("¡BLOQUEA!", playDuration: 3.0f);

            m.Tick(3.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));
        }

        [Test]
        public void Play_VariableDuration_CanBeSetPerRound()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);

            // Round 1: 5s play
            m.StartRound("¡TOCA!", playDuration: 5.0f);
            m.Tick(4.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));
            m.Tick(0.2f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));

            // Round 2: 2s play
            m.StartRound("¡CORRE!", playDuration: 2.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission));
            // Skip intermission (0 dur) -> CommandShow (0 dur) automatically
            // Since intermission=0 and commandShow=0, one tick of 0 should advance
            m.Tick(0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play),
                "After 0-duration intermission and command phases, should be in Play");

            m.Tick(1.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));
            m.Tick(0.2f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));
        }

        // ── Result phase is terminal until next StartRound ────────────────────────

        [Test]
        public void Result_IsTerminal_AdditionalTicksDoNotChange()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f, resultDuration: 1.0f);
            m.StartRound("¡PARA!", playDuration: 1.0f);

            m.Tick(1.0f); // ends Play -> Result
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));

            m.Tick(10.0f); // extra ticks do not crash or change phase past Result
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));
        }

        // ── StartRound resets the machine ─────────────────────────────────────────

        [Test]
        public void StartRound_Resets_PhaseToIntermission()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f, resultDuration: 0f);
            m.StartRound("¡SALTA!", playDuration: 0.5f);
            m.Tick(0.5f); // -> Result
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));

            m.StartRound("¡CORRE!", playDuration: 1.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission),
                "StartRound must reset to Intermission");
        }

        [Test]
        public void StartRound_UpdatesVerbText()
        {
            var m = MakeMachine();
            m.StartRound("¡PRIMERO!", PlayDur);
            Assert.That(m.VerbText, Is.EqualTo("¡PRIMERO!"));

            m.StartRound("¡SEGUNDO!", PlayDur);
            Assert.That(m.VerbText, Is.EqualTo("¡SEGUNDO!"));
        }

        // ── PhaseChangedThisTick flag ─────────────────────────────────────────────

        [Test]
        public void PhaseChangedThisTick_TrueOnTransitionTick()
        {
            var m = MakeMachine(intermissionDuration: 1.0f, commandShowDuration: 0f);
            m.StartRound("¡ESQUIVA!", PlayDur);

            m.Tick(0.5f);
            Assert.That(m.PhaseChangedThisTick, Is.False,
                "No phase change during early tick");

            m.Tick(0.5f); // exactly 1.0 s -> transitions to CommandShow (then to Play with 0-dur)
            Assert.That(m.PhaseChangedThisTick, Is.True,
                "Phase must have changed on the transition tick");
        }

        [Test]
        public void PhaseChangedThisTick_FalseOnSubsequentTick()
        {
            var m = MakeMachine(intermissionDuration: 1.0f, commandShowDuration: 1.0f);
            m.StartRound("¡ESQUIVA!", PlayDur);

            m.Tick(1.0f); // -> CommandShow
            Assert.That(m.PhaseChangedThisTick, Is.True);

            m.Tick(0.1f); // mid-CommandShow, no transition
            Assert.That(m.PhaseChangedThisTick, Is.False,
                "PhaseChangedThisTick must clear after non-transition tick");
        }

        // ── Zero/negative duration edge cases ────────────────────────────────────

        [Test]
        public void ZeroDuration_Intermission_ImmediatelyAdvances()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 1.0f);
            m.StartRound("¡ZAP!", PlayDur);

            // A Tick(0) should handle 0-duration phase transitions
            m.Tick(0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow),
                "Zero-duration Intermission should advance to CommandShow on first Tick");
        }

        [Test]
        public void ZeroDuration_CommandShow_ImmediatelyAdvancesToPlay()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);
            m.StartRound("¡ZAP!", playDuration: 1.0f);

            m.Tick(0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play),
                "Zero-duration CommandShow should land in Play on first Tick(0)");
        }

        [Test]
        public void NegativeDuration_Intermission_TreatedAsZero()
        {
            // Negative durations are clamped / treated as 0; should not throw
            // and should behave like zero-duration.
            var m = new RoundPhaseMachine(
                intermissionDuration: -1f,
                commandShowDuration:  1.0f,
                resultDuration:       1.0f);
            m.StartRound("¡SANO!", playDuration: 1.0f);

            Assert.DoesNotThrow(() => m.Tick(0f));
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow),
                "Negative intermission should behave like zero-duration");
        }

        [Test]
        public void NegativeDuration_CommandShow_TreatedAsZero()
        {
            var m = new RoundPhaseMachine(
                intermissionDuration: 0f,
                commandShowDuration:  -1f,
                resultDuration:       1.0f);
            m.StartRound("¡SANO!", playDuration: 1.0f);

            m.Tick(0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play),
                "Negative commandShow should behave like zero-duration");
        }

        // ── AC4: Configurable durations from sequencer ────────────────────────────

        [Test]
        public void PlayDuration_FromSequencer_UsedCorrectly()
        {
            // Simulate sequencer providing effective duration = 3.7 s
            float sequencerEffDuration = 3.7f;
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f);
            m.StartRound("¡ATACA!", sequencerEffDuration);

            m.Tick(3.6f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Play));

            m.Tick(0.2f); // 3.8 >= 3.7
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result));
        }

        // ── IsRoundComplete helper ────────────────────────────────────────────────

        [Test]
        public void IsRoundComplete_FalseUntilResultPhase()
        {
            var m = MakeMachine(intermissionDuration: 0f, commandShowDuration: 0f, resultDuration: 1.0f);
            m.StartRound("¡MIRA!", playDuration: 1.0f);

            Assert.That(m.IsRoundComplete, Is.False, "During Play, not complete");

            m.Tick(1.0f); // -> Result
            Assert.That(m.IsRoundComplete, Is.True, "In Result phase, round is complete");
        }

        // ── Elapsed time resets on StartRound ────────────────────────────────────

        [Test]
        public void ElapsedTime_ResetsOnStartRound()
        {
            var m = MakeMachine(intermissionDuration: 1.0f, commandShowDuration: 1.0f);
            m.StartRound("¡A!", PlayDur);
            m.Tick(1.0f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow));

            // Start a new round — elapsed should reset
            m.StartRound("¡B!", PlayDur);
            // After StartRound, phase is Intermission regardless of accumulated time
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission));

            // Must re-tick to leave Intermission
            m.Tick(0.9f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Intermission),
                "Elapsed time must have been reset; 0.9 s should not exceed 1.0 s intermission");
        }

        // ── Large deltaTime skips multiple phases atomically ─────────────────────

        [Test]
        public void LargeDeltaTime_CanSkipMultiplePhases()
        {
            // 0-duration intermission, 1s command, 2s play, 1s result
            var m = new RoundPhaseMachine(
                intermissionDuration: 0f,
                commandShowDuration:  1.0f,
                resultDuration:       1.0f);
            m.StartRound("¡GRANDE!", playDuration: 2.0f);

            // A single huge tick should land us in Result
            m.Tick(100f);
            Assert.That(m.CurrentPhase, Is.EqualTo(PhaseKind.Result),
                "A huge delta should skip to the final terminal phase");
        }
    }
}
