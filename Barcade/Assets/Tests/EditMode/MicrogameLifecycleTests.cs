using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-004 — Microgame contract + lifecycle.
    ///
    /// AC1: IMicrogame contract with explicit lifecycle:
    ///      Prepare -> CommandVerb -> Tick(s) -> Evaluate -> Cleanup.
    /// AC2: MicrogameResult captures per-player win/lose (indexed by PlayerSlot), immutable.
    /// AC3: MicrogameDefinition ScriptableObject exists in Framework (NOT tested here — Unity only).
    /// AC4: Pure win/lose evaluation is unit-testable; SampleTapMicrogame exercises the full
    ///      lifecycle with synthetic inputs via the fast dotnet runner.
    /// </summary>
    [TestFixture]
    public class MicrogameLifecycleTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// Minimal IMicrogameContext for tests — fixed 4-player set, seeded RNG.
        private static IMicrogameContext MakeContext(int seed = 0)
        {
            return new TestMicrogameContext(
                new SeededRandom(seed),
                new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde });
        }

        /// Build an IReadOnlyPlayerInputs where every player's button has a given state.
        private static IReadOnlyPlayerInputs AllButtons(ButtonState state)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
                map[slot] = new InputSnapshot(0f, 0f, state);
            return new DictPlayerInputs(map);
        }

        /// Build inputs where only the specified player's button is Pressed; rest Released.
        private static IReadOnlyPlayerInputs OnlyPressed(PlayerSlot who)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
                map[slot] = new InputSnapshot(0f, 0f,
                    slot == who ? ButtonState.Pressed : ButtonState.Released);
            return new DictPlayerInputs(map);
        }

        private static IReadOnlyPlayerInputs NoInput()
            => AllButtons(ButtonState.Released);

        // ── AC2: MicrogameResult ─────────────────────────────────────────────────

        [Test]
        public void MicrogameResult_DefaultState_AllSlotsLose()
        {
            var result = new MicrogameResult(new PlayerSlot[0]);
            // An empty participant set should not throw.
            Assert.That(result.Participants.Length, Is.EqualTo(0));
        }

        [Test]
        public void MicrogameResult_SingleWinner_ReportsWin()
        {
            var participants = new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Azul };
            var result = new MicrogameResult(participants);

            result.SetOutcome(PlayerSlot.Rojo, true);
            result.SetOutcome(PlayerSlot.Azul, false);

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True);
            Assert.That(result.IsWin(PlayerSlot.Azul), Is.False);
        }

        [Test]
        public void MicrogameResult_AllFourPlayers_CanBeSetIndependently()
        {
            var participants = new PlayerSlot[]
            {
                PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
            };
            var result = new MicrogameResult(participants);

            result.SetOutcome(PlayerSlot.Rojo,     true);
            result.SetOutcome(PlayerSlot.Azul,     false);
            result.SetOutcome(PlayerSlot.Amarillo, true);
            result.SetOutcome(PlayerSlot.Verde,    false);

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True);
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False);
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True);
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False);
        }

        [Test]
        public void MicrogameResult_ParticipantsArray_MatchesConstructorInput()
        {
            var participants = new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Verde };
            var result = new MicrogameResult(participants);

            Assert.That(result.Participants.Length, Is.EqualTo(2));
            Assert.That(result.Participants[0], Is.EqualTo(PlayerSlot.Rojo));
            Assert.That(result.Participants[1], Is.EqualTo(PlayerSlot.Verde));
        }

        [Test]
        public void MicrogameResult_DefaultOutcome_IsLoss()
        {
            var result = new MicrogameResult(
                new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Azul });

            // Before any SetOutcome call, all participants should default to loss.
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False);
            Assert.That(result.IsWin(PlayerSlot.Azul), Is.False);
        }

        // ── AC1: IMicrogame contract — CommandVerb ───────────────────────────────

        [Test]
        public void SampleTapMicrogame_CommandVerb_IsNotNullOrEmpty()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            Assert.That(game.CommandVerb, Is.Not.Null.And.Not.Empty);
        }

        // ── AC1 + AC4: Full lifecycle — all press in time → all win ─────────────

        [Test]
        public void SampleTapMicrogame_AllPressBeforeTimeout_AllWin()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            IMicrogameContext ctx = MakeContext(seed: 42);

            game.Prepare(ctx);

            // Tick once — no presses yet
            game.Tick(0.1f, NoInput());

            // All four players press their button
            game.Tick(0.1f, AllButtons(ButtonState.Pressed));

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo should win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul should win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo should win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde should win");

            game.Cleanup();
        }

        // ── AC4: Full lifecycle — time expires before pressing → all lose ────────

        [Test]
        public void SampleTapMicrogame_NobodyPressesAndTimerExpires_AllLose()
        {
            const float limit = 2f;
            var game = new SampleTapMicrogame(timeLimit: limit);
            IMicrogameContext ctx = MakeContext(seed: 0);

            game.Prepare(ctx);

            // Tick past the time limit without any input.
            game.Tick(limit + 0.1f, NoInput());

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "Rojo should lose");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul should lose");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo should lose");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Verde should lose");

            game.Cleanup();
        }

        // ── AC4: Partial press — only some players win ───────────────────────────

        [Test]
        public void SampleTapMicrogame_OnlyRojoPresses_OnlyRojoWins()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            IMicrogameContext ctx = MakeContext(seed: 1);

            game.Prepare(ctx);

            // Rojo presses; others do not
            game.Tick(0.5f, OnlyPressed(PlayerSlot.Rojo));

            // Timer expires — only Rojo pressed in time
            game.Tick(10f, NoInput());

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo pressed — should win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul did not press — should lose");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo did not press — should lose");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Verde did not press — should lose");

            game.Cleanup();
        }

        // ── AC4: Multiple ticks, press arrives mid-game ───────────────────────────

        [Test]
        public void SampleTapMicrogame_PressArrivesAfterSeveralTicks_PlayersWhoPressed_Win()
        {
            var game = new SampleTapMicrogame(timeLimit: 10f);
            IMicrogameContext ctx = MakeContext(seed: 7);

            game.Prepare(ctx);

            // Several ticks with no input
            for (int i = 0; i < 5; i++)
                game.Tick(0.5f, NoInput());  // 2.5 s elapsed

            // Azul and Verde press
            var inputs = new Dictionary<PlayerSlot, InputSnapshot>
            {
                { PlayerSlot.Rojo,     new InputSnapshot(0f, 0f, ButtonState.Released) },
                { PlayerSlot.Azul,     new InputSnapshot(0f, 0f, ButtonState.Pressed)  },
                { PlayerSlot.Amarillo, new InputSnapshot(0f, 0f, ButtonState.Released) },
                { PlayerSlot.Verde,    new InputSnapshot(0f, 0f, ButtonState.Pressed)  },
            };
            game.Tick(0.1f, new DictPlayerInputs(inputs));

            // Time runs out — Rojo and Amarillo never pressed
            game.Tick(20f, NoInput());

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "Rojo never pressed");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul pressed in time");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo never pressed");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde pressed in time");

            game.Cleanup();
        }

        // ── AC1: Cleanup is callable without error after full lifecycle ───────────

        [Test]
        public void SampleTapMicrogame_Cleanup_DoesNotThrow()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            game.Prepare(MakeContext());
            game.Tick(0.1f, NoInput());
            game.Evaluate();

            Assert.DoesNotThrow(() => game.Cleanup());
        }

        // ── AC1: Prepare resets state for a second round ─────────────────────────

        [Test]
        public void SampleTapMicrogame_SecondPrepare_ResetsState()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            IMicrogameContext ctx = MakeContext();

            // Round 1 — all win
            game.Prepare(ctx);
            game.Tick(0.1f, AllButtons(ButtonState.Pressed));
            MicrogameResult round1 = game.Evaluate();
            game.Cleanup();

            Assert.That(round1.IsWin(PlayerSlot.Rojo), Is.True);

            // Round 2 — nobody presses, timer expires
            game.Prepare(ctx);
            game.Tick(100f, NoInput());
            MicrogameResult round2 = game.Evaluate();
            game.Cleanup();

            Assert.That(round2.IsWin(PlayerSlot.Rojo), Is.False,
                "State must be reset between Prepare calls");
        }

        // ── AC2: MicrogameResult participants are exposed immutably ──────────────

        [Test]
        public void MicrogameResult_ModifyingReturnedParticipantsArray_DoesNotAffectInternal()
        {
            var participants = new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Azul };
            var result = new MicrogameResult(participants);

            // Mutate the returned array
            PlayerSlot[] returned = result.Participants;
            returned[0] = PlayerSlot.Verde;

            // Internal state should be unchanged
            Assert.That(result.Participants[0], Is.EqualTo(PlayerSlot.Rojo));
        }

        // ── IReadOnlyPlayerInputs contract ───────────────────────────────────────

        [Test]
        public void IReadOnlyPlayerInputs_ForMethod_ReturnsCorrectSnapshot()
        {
            IReadOnlyPlayerInputs inputs = OnlyPressed(PlayerSlot.Amarillo);

            Assert.That(inputs.For(PlayerSlot.Amarillo).Button, Is.EqualTo(ButtonState.Pressed));
            Assert.That(inputs.For(PlayerSlot.Rojo).Button,     Is.EqualTo(ButtonState.Released));
        }

        // ── Late-press path (withinWindow guard) ─────────────────────────────────

        /// <summary>
        /// A press arriving in a Tick whose deltaTime carries elapsed PAST the time
        /// limit must NOT count. The button is pressed after expiry; all players
        /// should lose because no valid press occurred within the window.
        /// This pins the withinWindow guard in SampleTapMicrogame.Tick.
        /// </summary>
        [Test]
        public void SampleTapMicrogame_PressAfterTimerExpiry_DoesNotCountAsWin()
        {
            const float limit = 2f;
            var game = new SampleTapMicrogame(timeLimit: limit);
            IMicrogameContext ctx = MakeContext(seed: 0);

            game.Prepare(ctx);

            // Tick 1: advance past the full time limit with NO input.
            game.Tick(limit + 0.5f, NoInput());

            // Tick 2: all players press — but the timer has already expired.
            game.Tick(0.1f, AllButtons(ButtonState.Pressed));

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "Late press must not win (Rojo)");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Late press must not win (Azul)");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Late press must not win (Amarillo)");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Late press must not win (Verde)");

            game.Cleanup();
        }

        // ── MicrogameResult freeze / effective immutability ──────────────────────

        [Test]
        public void MicrogameResult_AfterFreeze_SetOutcomeThrowsInvalidOperationException()
        {
            var participants = new PlayerSlot[] { PlayerSlot.Rojo, PlayerSlot.Azul };
            var result = new MicrogameResult(participants);
            result.SetOutcome(PlayerSlot.Rojo, true);
            result.Freeze();

            // Any SetOutcome after Freeze must throw.
            Assert.Throws<System.InvalidOperationException>(
                () => result.SetOutcome(PlayerSlot.Rojo, false),
                "SetOutcome on a frozen result must throw InvalidOperationException");
        }

        [Test]
        public void MicrogameResult_AfterFreeze_IsWinRemainsReadable()
        {
            var participants = new PlayerSlot[]
            {
                PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
            };
            var result = new MicrogameResult(participants);
            result.SetOutcome(PlayerSlot.Rojo, true);
            result.SetOutcome(PlayerSlot.Azul, false);
            result.Freeze();

            // Read access must still work after freeze.
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,  "Rojo win should be readable post-freeze");
            Assert.That(result.IsWin(PlayerSlot.Azul), Is.False, "Azul loss should be readable post-freeze");
        }

        [Test]
        public void SampleTapMicrogame_EvaluateReturnsAFrozenResult()
        {
            var game = new SampleTapMicrogame(timeLimit: 5f);
            game.Prepare(MakeContext());
            game.Tick(0.1f, AllButtons(ButtonState.Pressed));

            MicrogameResult result = game.Evaluate();

            Assert.That(result.IsFrozen, Is.True,
                "Evaluate must freeze the result before returning it");

            game.Cleanup();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Test-only support types (inline — no production value, no Unity dependency)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IMicrogameContext for test use. No UnityEngine dependency.
    /// </summary>
    internal sealed class TestMicrogameContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }

        public TestMicrogameContext(ISeededRandom rng, PlayerSlot[] players)
        {
            Rng     = rng;
            Players = players;
        }
    }

    /// <summary>
    /// IReadOnlyPlayerInputs backed by a simple dictionary. Test-only.
    /// </summary>
    internal sealed class DictPlayerInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;

        public DictPlayerInputs(Dictionary<PlayerSlot, InputSnapshot> map)
        {
            _map = map;
        }

        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
