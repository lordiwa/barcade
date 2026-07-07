using System;
using System.Linq;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Board;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// InputSnapshot resolves against Barcade.Core's own (v1) InputSnapshot before any
// "using"-imported namespace -- same collision every other v2 fixture in this
// project works around. Barcade.Core.Board's own types (BoardModel, CoinDelta,
// StarEvent, RoundModifier, WeaponKind, BoardTileType, ...) have no such
// collision, so they are used unqualified via the plain "using" above.
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-113 — coverage for <see cref="BoardModel"/> (§5.1 ring topology,
    /// §5.2 stop-meter movement, §5.3 tile types/economy, §5.6 economic
    /// invariant; Annex D.2 <see cref="IBoardModel"/> contract).
    ///
    /// The two heavy statistical sweeps this ticket's ACs call for --
    /// AC2's 1000+-seed stop-meter uniformity check and AC3's ring-quota
    /// property test across N in [16,24] -- live in the slow suite
    /// (<c>BoardModelSweepTests</c>) instead, to keep this fast suite fast.
    /// This file covers everything else: contract shape, determinism (AC1),
    /// single-seed stop-meter mechanics (AC2's non-statistical half), N=20
    /// ring quota exactness (AC3's exact-table half), zero-alloc TickMove
    /// (AC7, with a non-vacuous allocation-sensor sanity check), a bounded
    /// scripted-sequence economic invariant check (AC4's fast-suite half),
    /// and every tile-effect rule including the inversion deposit-tie and
    /// star-relocation cases (AC5).
    ///
    /// No Unity scene required -- pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class BoardModelTests
    {
        // GDD §3.2 / SessionStateMachineConfig.GddDefaults: 60 Hz sim tick.
        private const int TicksPerSecond = 60;
        // GDD §5.2: medidor de parada cycles 1->6 at 4 Hz => a new value every 15 ticks.
        private const int MeterTicksPerStep = TicksPerSecond / 4;
        // GDD §5.2/§2.2: 8 s auto-stop timeout.
        private const int TimeoutTicks = 8 * TicksPerSecond;

        // A ring index kept Evento in every test ring below: Evento only ever sets
        // BoardResolution.Modifier, never CoinFlows/Stars/ownership, so parking the
        // three seats NOT under test there means their landing never interferes
        // with an assertion about the one seat that IS under test.
        private const int EventoIdx = 18;
        private const int StarIdx = 19;

        // ── Test doubles ─────────────────────────────────────────────────────────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        /// <summary>20-square ring, Evento everywhere except <see cref="StarIdx"/> (Estrella) and whatever <paramref name="extra"/> overrides.</summary>
        private static BoardTileType[] Ring(int size = 20, params (int index, BoardTileType type)[] extra)
        {
            var ring = new BoardTileType[size];
            for (int i = 0; i < size; i++) ring[i] = BoardTileType.Evento;
            ring[StarIdx] = BoardTileType.Estrella;
            foreach ((int index, BoardTileType type) in extra) ring[index] = type;
            return ring;
        }

        /// <summary>All 4 seats at <see cref="EventoIdx"/> except <paramref name="testedSeat"/>, which is placed at <paramref name="testedIndex"/>.</summary>
        private static int[] Positions(int testedSeat, int testedIndex)
        {
            var p = new[] { EventoIdx, EventoIdx, EventoIdx, EventoIdx };
            p[testedSeat] = testedIndex;
            return p;
        }

        private static CoinDelta FindFlow(CoinDelta[] flows, int origin, int destination)
        {
            foreach (CoinDelta f in flows)
                if (f.Origin == origin && f.Destination == destination) return f;
            Assert.Fail($"no CoinDelta found with Origin={origin}, Destination={destination} (flows: [{string.Join(", ", flows.Select(f => $"{f.Origin}->{f.Destination}:{f.Amount}"))}])");
            throw new InvalidOperationException("unreachable");
        }

        // ── AC1: contract shape + determinism ────────────────────────────────────

        [Test]
        public void BoardModel_ImplementsIBoardModel()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1));
            Assert.That(board, Is.InstanceOf<IBoardModel>());
        }

        [Test]
        public void SameSeed_ProducesIdenticalRingLayout()
        {
            var boardA = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(77));
            var boardB = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(77));

            Assert.That(boardB.GetSnapshot().Ring, Is.EqualTo(boardA.GetSnapshot().Ring));
        }

        [Test]
        public void SameSeedAndInputScript_ProducesIdenticalMoveAndResolveOutcome()
        {
            BoardResolution ResolveOnce(int seed)
            {
                var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(seed));
                board.BeginMovePhase(new SeededRandom(seed + 1));
                var inputs = new FakeInputs();
                for (int t = 0; t < 20; t++) board.TickMove(inputs.Build(t));
                inputs.Set(PlayerSlot.Rojo, button: true);
                for (int t = 20; t <= TimeoutTicks; t++)
                {
                    board.TickMove(inputs.Build(t));
                    if (board.MoveFinished) break;
                }

                board.BeginResolvePhase();
                var noInput = new FakeInputs();
                while (!board.ResolveFinished) board.TickResolve(noInput.Build(0));
                return board.Resolve(new SeededRandom(seed + 2));
            }

            BoardResolution a = ResolveOnce(42);
            BoardResolution b = ResolveOnce(42);

            Assert.That(b.Modifier, Is.EqualTo(a.Modifier));
            Assert.That(b.CoinFlows.Length, Is.EqualTo(a.CoinFlows.Length));
            for (int i = 0; i < a.CoinFlows.Length; i++)
            {
                Assert.That(b.CoinFlows[i].Origin, Is.EqualTo(a.CoinFlows[i].Origin), $"flow[{i}].Origin");
                Assert.That(b.CoinFlows[i].Destination, Is.EqualTo(a.CoinFlows[i].Destination), $"flow[{i}].Destination");
                Assert.That(b.CoinFlows[i].Amount, Is.EqualTo(a.CoinFlows[i].Amount), $"flow[{i}].Amount");
            }
        }

        [Test]
        public void Constructor_FixedRing_WrongLength_Throws()
        {
            var badRing = new BoardTileType[19];
            Assert.Throws<ArgumentException>(() => new BoardModel(BoardConfig.GddDefaults, badRing));
        }

        [Test]
        public void Constructor_FixedRing_NotExactlyOneEstrella_Throws()
        {
            var ring = new BoardTileType[20]; // all default MonedaPositiva, zero Estrella
            Assert.Throws<ArgumentException>(() => new BoardModel(BoardConfig.GddDefaults, ring));
        }

        [Test]
        public void BoardConfig_RingSizeOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardConfig(15));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardConfig(25));
        }

        // ── AC3 (exact-table half): N=20 ring quota + shuffle ────────────────────

        [Test]
        public void BoardTileQuota_N20_MatchesGddTable()
        {
            int[] quota = BoardTileQuota.Compute(20);

            Assert.That(quota[(int)BoardTileType.MonedaPositiva], Is.EqualTo(6));
            Assert.That(quota[(int)BoardTileType.MonedaNegativa], Is.EqualTo(2));
            Assert.That(quota[(int)BoardTileType.Inversion], Is.EqualTo(3));
            Assert.That(quota[(int)BoardTileType.Propiedad], Is.EqualTo(3));
            Assert.That(quota[(int)BoardTileType.Evento], Is.EqualTo(3));
            Assert.That(quota[(int)BoardTileType.Estrella], Is.EqualTo(1));
            Assert.That(quota[(int)BoardTileType.Cangrejo], Is.EqualTo(2));
            Assert.That(quota.Sum(), Is.EqualTo(20));
        }

        [Test]
        public void BoardRing_Build_ProducesExactQuotaCounts()
        {
            int[] quota = BoardTileQuota.Compute(20);
            BoardTileType[] ring = BoardRing.Build(quota, new SeededRandom(3));

            Assert.That(ring.Length, Is.EqualTo(20));
            var counts = new int[7];
            foreach (BoardTileType t in ring) counts[(int)t]++;
            Assert.That(counts, Is.EqualTo(quota));
        }

        [Test]
        public void BoardRing_Build_IsDeterministicGivenSeed()
        {
            int[] quota = BoardTileQuota.Compute(20);
            BoardTileType[] ringA = BoardRing.Build(quota, new SeededRandom(3));
            BoardTileType[] ringB = BoardRing.Build(quota, new SeededRandom(3));

            Assert.That(ringB, Is.EqualTo(ringA));
        }

        // ── AC2 (single-seed half): stop-meter mechanics ─────────────────────────

        [Test]
        public void TickMove_MeterCycles1Through6_At4HzSawtooth()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1));
            board.BeginMovePhase(new SeededRandom(1));
            var noInput = new FakeInputs();

            int[] expectedPerStep = { 1, 2, 3, 4, 5, 6, 1 }; // 7 steps: proves the wrap back to 1
            for (int step = 0; step < expectedPerStep.Length; step++)
            {
                for (int i = 0; i < MeterTicksPerStep; i++)
                    board.TickMove(noInput.Build(step * MeterTicksPerStep + i));

                Assert.That(board.GetSnapshot().MeterValues[0], Is.EqualTo(expectedPerStep[step]), $"step {step}");
            }
        }

        [Test]
        public void TickMove_TapFreezesMeterAtTicksCurrentValue()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1));
            board.BeginMovePhase(new SeededRandom(1));
            var inputs = new FakeInputs();

            for (int t = 0; t < 30; t++) board.TickMove(inputs.Build(t)); // ticks 0..29, no press
            Assert.That(board.GetSnapshot().MeterFrozen[0], Is.False, "test setup sanity");

            inputs.Set(PlayerSlot.Rojo, button: true);
            board.TickMove(inputs.Build(30)); // tick 30: cycling = 1+(30/15)%6 = 3

            Assert.That(board.GetSnapshot().MeterFrozen[0], Is.True);
            Assert.That(board.GetSnapshot().MeterValues[0], Is.EqualTo(3));

            // Holding or releasing afterward must not change the frozen value.
            inputs.Set(PlayerSlot.Rojo, button: false);
            board.TickMove(inputs.Build(31));
            Assert.That(board.GetSnapshot().MeterValues[0], Is.EqualTo(3));
        }

        [Test]
        public void TickMove_NoTapWithinTimeout_AutoFreezesAtTimeoutTickValue()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1));
            board.BeginMovePhase(new SeededRandom(1));
            var noInput = new FakeInputs();

            for (int t = 0; t < TimeoutTicks; t++) board.TickMove(noInput.Build(t));
            Assert.That(board.MoveFinished, Is.False, "test setup sanity: not yet at the timeout tick");

            board.TickMove(noInput.Build(TimeoutTicks));
            Assert.That(board.MoveFinished, Is.True);

            int expected = 1 + (TimeoutTicks / MeterTicksPerStep) % 6; // 1 + (480/15)%6 = 3
            Assert.That(board.GetSnapshot().MeterValues[0], Is.EqualTo(expected));
        }

        [Test]
        public void MoveFinished_OnlyTrueOnceEverySeatIsFrozen()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1));
            board.BeginMovePhase(new SeededRandom(1));
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, button: true);
            inputs.Set(PlayerSlot.Azul, button: true);
            inputs.Set(PlayerSlot.Amarillo, button: true);
            // Verde (seat 3) never presses -- must wait out the 8s timeout.

            board.TickMove(inputs.Build(0));
            Assert.That(board.GetSnapshot().MeterFrozen, Is.EqualTo(new[] { true, true, true, false }));
            Assert.That(board.MoveFinished, Is.False);

            for (int t = 1; t < TimeoutTicks; t++) board.TickMove(inputs.Build(t));
            Assert.That(board.MoveFinished, Is.False, "still one tick short of the timeout");

            board.TickMove(inputs.Build(TimeoutTicks));
            Assert.That(board.MoveFinished, Is.True);
        }

        [Test]
        public void TickMove_PositionAdvancesByStoppedValue_WrappingAroundTheRing()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(1)); // N=20
            board.SetForcedPositions(new[] { 18, 0, 0, 0 });
            board.BeginMovePhase(new SeededRandom(1));
            var inputs = new FakeInputs();

            for (int t = 0; t < 30; t++) board.TickMove(inputs.Build(t));
            inputs.Set(PlayerSlot.Rojo, button: true);
            board.TickMove(inputs.Build(30)); // freezes seat 0 at value 3 (see tap test above)
            for (int t = 31; t <= TimeoutTicks; t++) board.TickMove(inputs.Build(t));

            Assert.That(board.MoveFinished, Is.True);
            Assert.That(board.GetSnapshot().Positions[0], Is.EqualTo((18 + 3) % 20), "18 + 3 must wrap past the ring end to 1");
        }

        // ── AC7: zero heap allocation in steady-state TickMove ───────────────────

        [Test]
        public void TickMove_SteadyState_AllocatesNoManagedMemory()
        {
            var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(7));
            board.BeginMovePhase(new SeededRandom(7));
            var inputs = new FakeInputs(); // every seat None/no button -- nobody taps, nobody times out yet

            for (int i = 0; i < 50; i++) board.TickMove(inputs.Build(i));
            Assert.That(board.MoveFinished, Is.False, "sensor setup: must still be mid-move after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 50; i < 400; i++) board.TickMove(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(board.MoveFinished, Is.False, "sensor setup: must still be mid-move after the measured window (timeout is tick 480), or it wasn't measuring the live path");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        [Test]
        public void AllocationSensor_CanActuallyDetectAnAllocation()
        {
            // Non-vacuous check: proves GC.GetAllocatedBytesForCurrentThread genuinely
            // observes an allocation on this runtime/test host, so a passing
            // TickMove_SteadyState_AllocatesNoManagedMemory above means "genuinely
            // zero alloc", not "the sensor is silently broken and would pass no
            // matter what happened in TickMove."
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sink = new object[8];
            for (int i = 0; i < sink.Length; i++) sink[i] = new object();
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.GreaterThan(0L), "sensor sanity: GC.GetAllocatedBytesForCurrentThread must observe a deliberate allocation");
            GC.KeepAlive(sink);
        }

        // ── AC4 (bounded-scripted half): economic invariant ──────────────────────

        [Test]
        public void Resolve_MultiRoundScriptedSequence_ConservesCoinsAndNeverGoesNegative()
        {
            BoardTileType[] ring = Ring(20,
                (0, BoardTileType.MonedaPositiva), (1, BoardTileType.MonedaNegativa),
                (2, BoardTileType.Propiedad), (3, BoardTileType.Inversion), (4, BoardTileType.Cangrejo));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 5, 5, 5, 5 });

            var rng = new SeededRandom(999);
            long bank = 0; // the visible pot (GDD §5.3 "pozo visible") -- tracks net creation/destruction

            var positionsScript = new[]
            {
                new[] { 0, 1, 2, 3 },
                new[] { 4, 0, 1, 2 },
                new[] { 3, 4, 0, 1 },
            };

            foreach (int[] positions in positionsScript)
            {
                board.SetForcedPositions(positions);
                board.BeginMovePhase(rng);
                board.BeginResolvePhase();
                var inputs = new FakeInputs();
                inputs.Set(PlayerSlot.Rojo, stick: Direction8.N); // 25% whenever Rojo is on Inversion this round
                while (!board.ResolveFinished) board.TickResolve(inputs.Build(0));
                BoardResolution res = board.Resolve(rng);

                foreach (CoinDelta flow in res.CoinFlows)
                {
                    Assert.That(flow.Amount, Is.GreaterThan(0), "a CoinDelta with a non-positive amount is noise, not a real flow");
                    if (flow.Origin == CoinDelta.Bank) bank -= flow.Amount;
                    if (flow.Destination == CoinDelta.Bank) bank += flow.Amount;
                }

                foreach (int b in board.GetSnapshot().Balances)
                    Assert.That(b, Is.GreaterThanOrEqualTo(0), "GDD §5.3: a balance must never go negative");
            }

            long totalPlayers = board.GetSnapshot().Balances.Sum();
            Assert.That(totalPlayers + bank, Is.EqualTo(20), "player total + net bank balance must equal the untouched starting total -- no coin unaccounted");
        }

        // ── AC5: tile-effect rules ────────────────────────────────────────────────

        [Test]
        public void Resolve_MonedaPositiva_CreditsPlayerFromBank_WithinGddRange()
        {
            var ring = Ring(20, (0, BoardTileType.MonedaPositiva));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 10, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            Assert.That(board.ResolveFinished, Is.True, "no Inversion landings this round -- resolve needs no input window");

            BoardResolution res = board.Resolve(new SeededRandom(1));

            Assert.That(board.GetSnapshot().Balances[0], Is.InRange(13, 16)); // 10 + U{3,6}
            CoinDelta flow = FindFlow(res.CoinFlows, CoinDelta.Bank, 0);
            Assert.That(flow.Amount, Is.InRange(3, 6));
        }

        [Test]
        public void Resolve_MonedaNegativa_ClampsAtZero_NeverNegative()
        {
            var ring = Ring(20, (0, BoardTileType.MonedaNegativa));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 1, 10, 10, 10 }); // less than the U{2,4} draw could ever be
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(3));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(3));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(0), "GDD §5.3: Moneda(-) never takes a player below 0");
            CoinDelta flow = FindFlow(res.CoinFlows, 0, CoinDelta.Bank);
            Assert.That(flow.Amount, Is.EqualTo(1)); // clamped: only had 1 to give
        }

        [Test]
        public void Resolve_MonedaNegativa_ZeroBalance_EmitsNoFlow()
        {
            var ring = Ring(20, (0, BoardTileType.MonedaNegativa));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 0, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(5));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(5));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(0));
            Assert.That(res.CoinFlows.Any(f => f.Origin == 0), Is.False);
        }

        [Test]
        public void Resolve_Propiedad_EmptySquare_BuysIfAffordable()
        {
            var ring = Ring(20, (0, BoardTileType.Propiedad));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 10, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(1));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(2)); // 10-8
            Assert.That(board.GetSnapshot().PropertyOwner[0], Is.EqualTo(0));
            CoinDelta flow = FindFlow(res.CoinFlows, 0, CoinDelta.Bank);
            Assert.That(flow.Amount, Is.EqualTo(8));
        }

        [Test]
        public void Resolve_Propiedad_EmptySquare_CannotAfford_NoPurchase()
        {
            var ring = Ring(20, (0, BoardTileType.Propiedad));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 5, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            board.Resolve(new SeededRandom(1));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(5));
            Assert.That(board.GetSnapshot().PropertyOwner[0], Is.EqualTo(-1));
        }

        [Test]
        public void Resolve_Propiedad_RivalLanding_Transfers4ToOwner()
        {
            var ring = Ring(20, (0, BoardTileType.Propiedad));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 10, 10, 10, 10 });

            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            board.Resolve(new SeededRandom(1));
            Assert.That(board.GetSnapshot().PropertyOwner[0], Is.EqualTo(0), "test setup sanity");

            board.SetForcedPositions(Positions(1, 0));
            board.BeginMovePhase(new SeededRandom(2));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(2));

            Assert.That(board.GetSnapshot().Balances[1], Is.EqualTo(10 - 4));
            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(2 + 4)); // 10-8 round1, +4 round2
            CoinDelta flow = FindFlow(res.CoinFlows, 1, 0);
            Assert.That(flow.Amount, Is.EqualTo(4));
        }

        [Test]
        public void Resolve_Propiedad_RivalLanding_ClampsTransferToRivalBalance()
        {
            var ring = Ring(20, (0, BoardTileType.Propiedad));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 10, 2, 10, 10 }); // seat 1 only has 2 coins

            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            board.Resolve(new SeededRandom(1));

            board.SetForcedPositions(Positions(1, 0));
            board.BeginMovePhase(new SeededRandom(2));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(2));

            Assert.That(board.GetSnapshot().Balances[1], Is.EqualTo(0), "[ASSUMED] transfer clamps to what the rival can pay -- a balance must never go negative");
            CoinDelta flow = FindFlow(res.CoinFlows, 1, 0);
            Assert.That(flow.Amount, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_Estrella_Purchase_GrantsStarAndRelocates()
        {
            var ring = Ring(20);
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 20, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, StarIdx));
            board.BeginMovePhase(new SeededRandom(9));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(9));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(5)); // 20-15
            Assert.That(res.Stars, Has.Length.EqualTo(1));
            Assert.That(res.Stars[0].Seat, Is.EqualTo(0));
            Assert.That(res.Stars[0].Count, Is.EqualTo(1));

            int newStarIndex = board.GetSnapshot().StarSquareIndex;
            Assert.That(newStarIndex, Is.Not.EqualTo(StarIdx), "GDD §5.3: after purchase the star square relocates to another position");
            Assert.That(board.GetSnapshot().Ring[StarIdx], Is.Not.EqualTo(BoardTileType.Estrella), "the vacated square must no longer be the star");
            Assert.That(board.GetSnapshot().Ring[newStarIndex], Is.EqualTo(BoardTileType.Estrella));
        }

        [Test]
        public void Resolve_Estrella_CannotAfford_StaysPut()
        {
            var ring = Ring(20);
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 5, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, StarIdx));
            board.BeginMovePhase(new SeededRandom(9));
            board.BeginResolvePhase();
            BoardResolution res = board.Resolve(new SeededRandom(9));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(5));
            Assert.That(res.Stars, Is.Empty);
            Assert.That(board.GetSnapshot().StarSquareIndex, Is.EqualTo(StarIdx));
        }

        [Test]
        public void Resolve_Cangrejo_GrantsWeapon_LandingAgainStillLeavesExactlyOne()
        {
            var ring = Ring(20, (0, BoardTileType.Cangrejo));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(11));
            board.BeginResolvePhase();
            board.Resolve(new SeededRandom(11));

            Assert.That(board.GetSnapshot().Weapons[0], Is.Not.Null);

            // GDD §5.4: "máx. 1 arma en mano; recoger otra la sustituye" -- the
            // inventory slot is a single nullable, not a list, so landing again
            // structurally replaces rather than accumulates.
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(12));
            board.BeginResolvePhase();
            board.Resolve(new SeededRandom(12));

            Assert.That(board.GetSnapshot().Weapons[0], Is.Not.Null);
        }

        [Test]
        public void Resolve_Evento_ModifierIsDeterministicGivenSeed()
        {
            var ring = Ring(20, (0, BoardTileType.Evento));

            var boardA = new BoardModel(BoardConfig.GddDefaults, ring);
            boardA.SetForcedPositions(Positions(0, 0));
            boardA.BeginMovePhase(new SeededRandom(4));
            boardA.BeginResolvePhase();
            BoardResolution resA = boardA.Resolve(new SeededRandom(4));

            var boardB = new BoardModel(BoardConfig.GddDefaults, ring);
            boardB.SetForcedPositions(Positions(0, 0));
            boardB.BeginMovePhase(new SeededRandom(4));
            boardB.BeginResolvePhase();
            BoardResolution resB = boardB.Resolve(new SeededRandom(4));

            Assert.That(resB.Modifier, Is.EqualTo(resA.Modifier));
            Assert.That(resA.Modifier, Is.Not.EqualTo(RoundModifier.None));
        }

        [Test]
        public void Resolve_Inversion_AppliesChosenTierAndDeductsBalance()
        {
            var ring = Ring(20, (0, BoardTileType.Inversion));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 100, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            Assert.That(board.ResolveFinished, Is.False, "a seat landed on Inversion -- the <=4s choice window must be open");

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, stick: Direction8.N); // Up = 25% (see class-level convention note in BoardModel)
            while (!board.ResolveFinished) board.TickResolve(inputs.Build(0));

            BoardResolution res = board.Resolve(new SeededRandom(1));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(75)); // 100 - 25%
            CoinDelta flow = FindFlow(res.CoinFlows, 0, CoinDelta.Bank);
            Assert.That(flow.Amount, Is.EqualTo(25));
        }

        [Test]
        public void Resolve_Inversion_NoChoiceMade_DefaultsToZeroPercent()
        {
            var ring = Ring(20, (0, BoardTileType.Inversion));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 100, 10, 10, 10 });
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();

            var noInput = new FakeInputs();
            while (!board.ResolveFinished) board.TickResolve(noInput.Build(0));
            board.Resolve(new SeededRandom(1));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(100), "[ASSUMED] no input within the window defaults to a 0% deposit (safest neutral -- see hand-off)");
        }

        [Test]
        public void Resolve_Inversion_HighestCumulativeDepositor_IsOwner()
        {
            var ring = Ring(20, (0, BoardTileType.Inversion));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 100, 100, 10, 10 });

            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            var inputA = new FakeInputs();
            inputA.Set(PlayerSlot.Rojo, stick: Direction8.E); // 50%
            while (!board.ResolveFinished) board.TickResolve(inputA.Build(0));
            board.Resolve(new SeededRandom(1));

            board.SetForcedPositions(Positions(1, 0));
            board.BeginMovePhase(new SeededRandom(2));
            board.BeginResolvePhase();
            var inputB = new FakeInputs();
            inputB.Set(PlayerSlot.Azul, stick: Direction8.N); // 25% -- smaller
            while (!board.ResolveFinished) board.TickResolve(inputB.Build(0));
            board.Resolve(new SeededRandom(2));

            Assert.That(board.GetSnapshot().InversionOwner[0], Is.EqualTo(0), "seat 0 deposited more (50 vs 25) -- highest cumulative owns");
        }

        [Test]
        public void Resolve_Inversion_TiedCumulativeDeposits_FirstDepositorOwns()
        {
            var ring = Ring(20, (0, BoardTileType.Inversion));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 100, 100, 10, 10 });

            // Round 1: seat 1 deposits 25% FIRST.
            board.SetForcedPositions(Positions(1, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            var inputA = new FakeInputs();
            inputA.Set(PlayerSlot.Azul, stick: Direction8.N);
            while (!board.ResolveFinished) board.TickResolve(inputA.Build(0));
            board.Resolve(new SeededRandom(1));

            // Round 2: seat 0 deposits 25% of its own (also 100) balance -- equal
            // absolute amount, deposited LATER.
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(2));
            board.BeginResolvePhase();
            var inputB = new FakeInputs();
            inputB.Set(PlayerSlot.Rojo, stick: Direction8.N);
            while (!board.ResolveFinished) board.TickResolve(inputB.Build(0));
            board.Resolve(new SeededRandom(2));

            Assert.That(board.GetSnapshot().Balances[0], Is.EqualTo(75));
            Assert.That(board.GetSnapshot().Balances[1], Is.EqualTo(75));
            Assert.That(board.GetSnapshot().InversionOwner[0], Is.EqualTo(1), "GDD §5.3: deposit tie -> owner is whoever deposited FIRST");
        }

        [Test]
        public void Resolve_Inversion_OwnerEarnsStar_EveryThirdRound()
        {
            var ring = Ring(20, (0, BoardTileType.Inversion));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetBalances(new[] { 100, 10, 10, 10 });

            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();
            var input = new FakeInputs();
            input.Set(PlayerSlot.Rojo, stick: Direction8.E); // 50% -> becomes owner
            while (!board.ResolveFinished) board.TickResolve(input.Build(0));
            BoardResolution res1 = board.Resolve(new SeededRandom(1));
            Assert.That(res1.Stars, Is.Empty, "round 1 is not a multiple of 3 -- no payout yet");

            for (int round = 2; round <= 3; round++)
            {
                board.SetForcedPositions(Positions(0, EventoIdx)); // nobody revisits the Inversion square
                board.BeginMovePhase(new SeededRandom(round));
                board.BeginResolvePhase();
                var noInput = new FakeInputs();
                while (!board.ResolveFinished) board.TickResolve(noInput.Build(0));
                BoardResolution res = board.Resolve(new SeededRandom(round));

                if (round == 3)
                {
                    StarEvent star = res.Stars.Single(s => s.Seat == 0);
                    Assert.That(star.Count, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(res.Stars, Is.Empty);
                }
            }
        }

        // ── AC6: BOARD_RESOLVE integration shape (no real-time choice needed) ────

        [Test]
        public void ResolvePhase_NoInversionLandings_FinishesImmediately()
        {
            // GDD §2.2: BOARD_RESOLVE budgets 3-6s typical, 10s max; "cero tiempo
            // muerto" (§5 intro) means a round with nothing to decide must not
            // stall waiting on an unused window.
            var ring = Ring(20, (0, BoardTileType.MonedaPositiva));
            var board = new BoardModel(BoardConfig.GddDefaults, ring);
            board.SetForcedPositions(Positions(0, 0));
            board.BeginMovePhase(new SeededRandom(1));
            board.BeginResolvePhase();

            Assert.That(board.ResolveFinished, Is.True);
        }
    }
}
