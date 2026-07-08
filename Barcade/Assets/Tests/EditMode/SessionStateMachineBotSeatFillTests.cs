using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Bots;
using Barcade.Core.Microgames.V2;
// Same collision as every other SessionStateMachine/v2-mechanic test file (see
// SessionStateMachineTests.cs's own note): Barcade.Core.Tests is lexically
// nested inside Barcade.Core, so an unqualified name resolves against
// Barcade.Core's own members before any "using"-imported namespace.
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Esquiva = Barcade.Core.Microgames.V2.EsquivaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-069 (T-110 AC5 completion) — "Novato fills an Empty/Idle seat in a
    /// simulated session without stalling any state (integration test with the
    /// SessionStateMachine)," now with the roster GENUINELY occupied
    /// (<see cref="SeatState.Bot"/>), not merely fed inert input against an
    /// <see cref="SeatState.Empty"/> slot.
    ///
    /// <para>
    /// <b>What changed from the TASK-038 version of this file.</b> That version
    /// proved bot input doesn't regress FSM liveness while the roster STAYED
    /// Empty for the unclaimed seats — "fills a seat" was functionally thin,
    /// since every mechanic that gates engagement on
    /// <c>roster.Seats[i] == Human || == Bot</c> (Sujeta/Iguala/Jefe/CoopPhase)
    /// would have treated that seat as absent the whole time. TASK-069 wires
    /// <see cref="SessionStateMachine.CompleteJoin"/> to assign
    /// <see cref="SeatState.Bot"/> (not <see cref="SeatState.Empty"/>) to any
    /// seat nobody claimed — this file now proves that against the REAL roster
    /// semantics, plus adds the "Idle" half of AC5 (a claimed-then-idle seat
    /// handed to a bot) and a TASK-056 interop regression.
    /// </para>
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class SessionStateMachineBotSeatFillTests
    {
        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick, bool pressed)
                => _players[(int)slot] = new PlayerInput(stick, pressed);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        /// <summary>Short-but-nonzero durations (mirrors SessionStateMachineTests.FastConfig) so a full session completes in well under a second of test wall time.</summary>
        private static SessionStateMachineConfig FastConfig(float idleTimeoutSeconds = 45f) => new SessionStateMachineConfig(
            joinTimeoutSeconds: 0.1f,
            joinMinReady: 2,
            mgIntroSeconds: 0.02f,
            mgResultSeconds: 0.02f,
            intermissionSeconds: 0.02f,
            finalWagerSeconds: 0.05f,
            gameOverSeconds: 0.05f,
            totalRounds: 1,
            ticksPerSecond: 60,
            idleTimeoutSeconds: idleTimeoutSeconds);

        // ── AC1: an unclaimed seat is genuinely Bot-occupied, not Empty ──────────

        [Test]
        public void NovatoBot_FillsUnclaimedSeats_WithGenuineBotOccupancy_SessionReachesGameOverWithoutStalling()
        {
            var esquiva = new V2Esquiva(new EsquivaParams(
                spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                hazardPattern: EsquivaHazardPattern.Rain, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 1.0f, jumpEnabled: false));

            var fsm = new SessionStateMachine(new SeededRandom(31), FastConfig());
            fsm.SetActiveMicrogame(esquiva, "¡ESQUIVA!", playDurationSeconds: 1.2f);

            // Bot brains: one per seat, driving MgPlay off the SAME esquiva
            // instance the FSM ticks — a real IMicrogame, not a fake, so this
            // is a genuine end-to-end drive, not just an FSM-shape probe.
            var botRng = new SeededRandom(99);
            var policies = new IBotPolicy[4];
            for (int i = 0; i < 4; i++) policies[i] = new EsquivaBotPolicy();

            var inputs = new FakeInputs();

            fsm.InsertCredit();
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Join));

            // Only Rojo claims a seat — Azul/Amarillo/Verde stay unclaimed.
            // Join (TASK-024 MEDIUM-1 ruling) waits out its full timeout rather
            // than exiting early on the documented ">=2 listos" minimum, so
            // this reliably produces exactly one claimed seat once Join
            // completes.
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);

            bool sawGameOver = false;
            int t;
            for (t = 0; t < 20000 && !sawGameOver; t++)
            {
                // Once past Join, every seat (including the 3 unclaimed ones)
                // gets a real Bot.Novato decision each tick, read off the
                // mechanic's own public RenderState — the production seat-fill
                // scenario (GDD Annex D.3: "rellena puestos vacíos en partida
                // real").
                if (fsm.CurrentPhase != SessionPhase.Join)
                {
                    RenderState rs = esquiva.GetRenderState();
                    for (int i = 0; i < 4; i++)
                    {
                        PlayerInput decision = policies[i].Decide(Bot.Novato, new BotView(rs, i), botRng);
                        inputs.Set((PlayerSlot)i, decision.Stick, decision.Button);
                    }
                }

                fsm.Tick(inputs.Build(t));
                if (fsm.CurrentPhase == SessionPhase.GameOver) sawGameOver = true;
            }

            Assert.That(sawGameOver, Is.True,
                $"a Novato-bot-filled session (3 of 4 seats unclaimed, driven by real bot input every tick) must still reach GameOver — no state may stall on the bot-filled seats (probed {t} ticks)");

            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human));
            // The AC1 crux: these are GENUINELY occupied (Bot), not left Empty —
            // every roster-gated mechanic (Sujeta/Iguala/Jefe/CoopPhase) treats
            // Bot exactly like Human for engagement, and every mechanic's own
            // entity-publish loop gates on PlayerRoster.IsActive (true for Bot),
            // so these seats now get a real published avatar too.
            foreach (PlayerSlot unclaimed in new[] { PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde })
                Assert.That(fsm.Roster.Seats[(int)unclaimed], Is.EqualTo(SeatState.Bot),
                    $"{unclaimed} was never claimed and must be GENUINELY Bot-occupied, not left Empty");
        }

        // ── AC2: HumanIdle seat-fill scenario ("the Idle half of AC5") ───────────

        [Test]
        public void IdleSeat_HandedToABot_SessionStillReachesGameOverWithoutStalling()
        {
            // Short idle window (0.1s = 6 ticks @ 60Hz) so a claimed seat going
            // quiet mid-round crosses TASK-056's threshold quickly, without
            // needing a long-running microgame just to exercise it.
            var esquiva = new V2Esquiva(new EsquivaParams(
                spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                hazardPattern: EsquivaHazardPattern.Rain, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 1.5f, jumpEnabled: false));

            var fsm = new SessionStateMachine(new SeededRandom(41), FastConfig(idleTimeoutSeconds: 0.1f));
            fsm.SetActiveMicrogame(esquiva, "¡ESQUIVA!", playDurationSeconds: 1.7f);

            var botRng = new SeededRandom(77);
            var policies = new IBotPolicy[4];
            for (int i = 0; i < 4; i++) policies[i] = new EsquivaBotPolicy();

            var inputs = new FakeInputs();
            fsm.InsertCredit();

            // All 4 claim a seat -- Azul is the one whose human will go
            // quiet mid-round.
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            inputs.Set(PlayerSlot.Amarillo, Direction8.None, true);
            inputs.Set(PlayerSlot.Verde, Direction8.None, true);

            int t = 0;
            for (; t < 50 && fsm.CurrentPhase == SessionPhase.Join; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.Not.EqualTo(SessionPhase.Join), "sensor setup: Join must complete");
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Azul], Is.EqualTo(SeatState.Human), "sensor setup: Azul claimed a seat");

            for (; t < 3000 && fsm.CurrentPhase != SessionPhase.MgPlay; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "sensor setup: must reach MgPlay");

            // Azul goes completely silent (no stick, no button) while the
            // other 3 keep receiving real bot decisions so the round itself
            // keeps making genuine progress in the meantime.
            for (int i = 0; i < 20 && fsm.Roster.Seats[(int)PlayerSlot.Azul] == SeatState.Human; i++)
            {
                RenderState rs = esquiva.GetRenderState();
                inputs.Set(PlayerSlot.Azul, Direction8.None, false);
                foreach (PlayerSlot slot in new[] { PlayerSlot.Rojo, PlayerSlot.Amarillo, PlayerSlot.Verde })
                {
                    PlayerInput decision = policies[(int)slot].Decide(Bot.Novato, new BotView(rs, (int)slot), botRng);
                    inputs.Set(slot, decision.Stick, decision.Button);
                }
                fsm.Tick(inputs.Build(t));
                t++;
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Azul], Is.EqualTo(SeatState.HumanIdle),
                "sensor setup: Azul's human must have gone idle before being handed to a bot -- this is the scenario under test");

            // Hand ALL 4 seats (including the now-Idle Azul) to real Bot.Novato
            // decisions from here on -- the production seat-fill pattern
            // applied mid-round to an already-idle seat, not just at Join.
            bool sawGameOver = false;
            for (; t < 20000 && !sawGameOver; t++)
            {
                RenderState rs = esquiva.GetRenderState();
                for (int i = 0; i < 4; i++)
                {
                    PlayerInput decision = policies[i].Decide(Bot.Novato, new BotView(rs, i), botRng);
                    inputs.Set((PlayerSlot)i, decision.Stick, decision.Button);
                }
                fsm.Tick(inputs.Build(t));
                if (fsm.CurrentPhase == SessionPhase.GameOver) sawGameOver = true;
            }

            Assert.That(sawGameOver, Is.True,
                "a session where a claimed seat goes Idle mid-round and is then handed to a bot must still reach GameOver -- no state may stall");

            // TASK-056 interop: a bot's genuine input (button/stick edges) is
            // indistinguishable from a human's to the dead-seat tracker, so it
            // registers as a flanco and resumes an Idle seat back to Human --
            // there is no separate SeatState.Bot handoff for a seat that was
            // already claimed. Both outcomes below are valid/non-broken (a
            // Novato's sampled reaction delay can leave it transiently quiet),
            // but it must never be anything OTHER than these two.
            SeatState finalAzul = fsm.Roster.Seats[(int)PlayerSlot.Azul];
            Assert.That(finalAzul, Is.EqualTo(SeatState.Human).Or.EqualTo(SeatState.HumanIdle),
                $"Azul's seat was claimed by a human -- handing it to a bot must never turn it into Bot/Empty, only Human or HumanIdle (was {finalAzul})");
        }

        // ── AC3: SeatState.Bot occupancy interoperates with the TASK-056 tracker ──

        [Test]
        public void BotOccupiedSeat_IsNeverMarkedIdle_EvenWithZeroInputForLongerThanTheThreshold()
        {
            // Short idle window (0.1s = 6 ticks) so the regression is cheap to
            // pin: if SeatState.Bot occupancy ever regressed
            // UpdateDeadSeatTracking's own claimed-Human-only guard, this would
            // catch it within a handful of ticks rather than needing the real
            // 45s window.
            var esquiva = new V2Esquiva(new EsquivaParams(
                spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                hazardPattern: EsquivaHazardPattern.Rain, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 3.0f, jumpEnabled: false));

            var fsm = new SessionStateMachine(new SeededRandom(51), FastConfig(idleTimeoutSeconds: 0.1f));
            fsm.SetActiveMicrogame(esquiva, "¡ESQUIVA!", playDurationSeconds: 3.2f);

            var inputs = new FakeInputs();
            fsm.InsertCredit();

            // Only Rojo claims -- Azul/Amarillo/Verde become Bot-occupied via
            // CompleteJoin once Join times out.
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);

            int t = 0;
            for (; t < 50 && fsm.CurrentPhase == SessionPhase.Join; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Azul], Is.EqualTo(SeatState.Bot), "sensor setup: Azul must be Bot-occupied");

            for (; t < 3000 && fsm.CurrentPhase != SessionPhase.MgPlay; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "sensor setup: must reach MgPlay");

            // Feed ZERO input for every seat (including Rojo) well beyond 3x
            // the idle threshold. Rojo (claimed Human) MUST go Idle -- proving
            // this window is genuinely long enough to trigger the tracker at
            // all (a non-vacuity check) -- while the Bot seats must NEVER be
            // touched by it regardless.
            for (int i = 0; i < 30; i++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
                fsm.Tick(inputs.Build(t));
                t++;
            }

            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle),
                "sensor sanity: the zero-input window must be long enough to actually trigger the tracker for a claimed seat");

            foreach (PlayerSlot botSlot in new[] { PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde })
                Assert.That(fsm.Roster.Seats[(int)botSlot], Is.EqualTo(SeatState.Bot),
                    $"{botSlot} is Bot-occupied and must NEVER be swept into HumanIdle by the TASK-056 tracker, no matter how long it goes with zero fed input");
        }
    }
}
