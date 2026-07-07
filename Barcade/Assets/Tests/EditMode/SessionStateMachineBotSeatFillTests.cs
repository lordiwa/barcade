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
    /// GDD T-110 AC5 — "Novato fills an Empty/Idle seat in a simulated session
    /// without stalling any state (integration test with the
    /// SessionStateMachine)."
    ///
    /// <para>
    /// <b>[ASSUMED] scope.</b> <see cref="SessionStateMachine.CompleteJoin"/>
    /// only ever assigns <see cref="SeatState.Human"/>/<see cref="SeatState.Empty"/>
    /// off real Join claims — wiring an UNCLAIMED seat to
    /// <see cref="SeatState.Bot"/> is explicit future work the GDD itself defers
    /// ("real bot-fill... es T-110, out of scope" — <see cref="SessionStateMachine"/>'s
    /// own class doc, "Join with zero ready players" note). This test therefore
    /// proves the literal AC: a real <see cref="Bot.Novato"/> policy, driven off
    /// the SAME <see cref="IMicrogame"/> instance the FSM is playing, supplies a
    /// continuous, legitimate <see cref="PlayerInput"/> stream for an EMPTY
    /// (unclaimed) seat throughout an entire simulated session, and the FSM
    /// still reaches <see cref="SessionPhase.GameOver"/> within its expected
    /// tick budget — i.e. the bot's presence changes nothing about the FSM's
    /// own liveness (no new stall, no new hang), the actual risk a real bot-fill
    /// wiring ticket would need to not regress. The HumanIdle half of "Empty/Idle"
    /// is architecturally the same code path (a bot supplying continuous input
    /// for a seat that would otherwise flanco-starve into
    /// <see cref="SeatState.HumanIdle"/> just keeps registering flancos —
    /// TASK-056's own dead-seat tracker only watches CLAIMED
    /// <see cref="SeatState.Human"/> seats, see <c>UpdateDeadSeatTracking</c>'s
    /// own guard) — not given a second dedicated scenario here to keep this
    /// integration test's scope bounded; flagged in the TASK-038 hand-off.
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
        private static SessionStateMachineConfig FastConfig() => new SessionStateMachineConfig(
            joinTimeoutSeconds: 0.1f,
            joinMinReady: 2,
            mgIntroSeconds: 0.02f,
            mgResultSeconds: 0.02f,
            intermissionSeconds: 0.02f,
            finalWagerSeconds: 0.05f,
            gameOverSeconds: 0.05f,
            totalRounds: 1,
            ticksPerSecond: 60);

        [Test]
        public void NovatoBot_FillsUnclaimedSeats_SessionReachesGameOverWithoutStalling()
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

            // Only Rojo claims a seat — Azul/Amarillo/Verde stay Empty. Join
            // (TASK-024 MEDIUM-1 ruling) waits out its full timeout rather than
            // exiting early on the documented ">=2 listos" minimum, so this
            // reliably produces exactly one claimed seat once Join completes.
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);

            bool sawGameOver = false;
            int t;
            for (t = 0; t < 20000 && !sawGameOver; t++)
            {
                // Once past Join, every seat (including the 3 Empty ones) gets a
                // real Bot.Novato decision each tick, read off the mechanic's own
                // public RenderState — exactly the production seat-fill scenario
                // (GDD Annex D.3: "rellena puestos vacíos en partida real").
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
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Azul], Is.EqualTo(SeatState.Empty),
                "sanity: Azul really was the unclaimed/bot-filled seat this test exercises");
        }
    }
}
