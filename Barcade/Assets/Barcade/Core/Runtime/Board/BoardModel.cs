using System;
using System.Collections.Generic;
// Barcade.Core.Board is lexically nested inside Barcade.Core -- see IBoardModel.cs's
// own using-directive comment for why this needs a distinctly-named alias rather
// than a same-name one. Direction8/PlayerSlot/SeededRandom (all declared directly
// in Barcade.Core) resolve unqualified with no alias needed, same as every other
// file nested under Barcade.Core.
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Board
{
    /// <summary>
    /// The board meta-game core (GDD T-113, §5.1-§5.3/§5.6, Annex D.2). Engine-free
    /// Core: no <c>UnityEngine</c> reference; every random draw goes through an
    /// injected <see cref="SeededRandom"/> (AC1).
    ///
    /// <para>
    /// <b>Two constructors, two purposes.</b>
    /// <see cref="BoardModel(BoardConfig, SeededRandom)"/> builds the ring layout
    /// once via <see cref="BoardTileQuota"/>/<see cref="BoardRing"/> (a session-level
    /// concern — the ring persists for the whole session, unlike the per-round
    /// <see cref="BeginMovePhase"/>/<see cref="Resolve"/> calls). The overload
    /// taking a <c>BoardTileType[]</c> directly is a test/framework seam for
    /// deterministic square-effect testing (same idea as
    /// <c>EsquivaMicrogameV2.SetForcedAvatarPositions</c>) — it bypasses the seeded
    /// shuffle so a test can pin an exact layout instead of hunting for a seed that
    /// happens to place a tile where it's needed. Both require the ring to contain
    /// exactly one <see cref="BoardTileType.Estrella"/> square (GDD §5.3: "Solo hay
    /// una a la vez").
    /// </para>
    ///
    /// <para>
    /// <b>Stop-meter tap detection is level-triggered, not debounced (design
    /// choice).</b> Several v2 mechanics (ReaccionaMicrogame, MantenMicrogame) read
    /// taps through the shared <c>InputInterpreter</c>/<c>InputBridge</c> pair for
    /// its 2-tick debounce and precise edge timing — needed there because those
    /// mechanics measure reaction LATENCY. GDD §5.2's medidor de parada has no such
    /// precision requirement ("tap detiene el medidor" — any observed press this
    /// tick freezes it, GDD names no debounce/anti-bounce rule for it) and
    /// InputInterpreter's own debounce would not even fully solve the "seat still
    /// holding a button from a previous phase" concern it might seem to address
    /// (its own doc: "A button already held down when Reset is called is treated
    /// as a brand-new press... 2 ticks later" — the interpreter delays the false
    /// freeze, it does not prevent it). Given neither approach eliminates that
    /// edge case and GDD asks for none of debounce's precision here, TickMove
    /// reads <c>PlayerInput.Button</c> directly — simpler, self-contained (no
    /// InputInterpreter/InputBridge instances needed), and exactly testable
    /// (freeze happens on the literal tick a press is observed, no offset to
    /// reason about).
    /// </para>
    ///
    /// <para>
    /// <b>BeginMovePhase's rng parameter (Annex D.2 signature).</b> Accepted but
    /// not consumed for movement itself: GDD §5.2 models the stop-meter as a
    /// deterministic sawtooth sampled at an unpredictable human/bot reaction
    /// moment, not a dice roll — "la distribución resultante es ≈ uniforme"
    /// precisely BECAUSE tap timing is unpredictable relative to the 4 Hz cycle,
    /// not because a random number is drawn. All square-effect randomness (Moneda
    /// draws, Evento/Cangrejo draws, star relocation) flows through
    /// <see cref="Resolve"/>'s own rng instead — see GDD §6.4 "la aleatoriedad
    /// jamás se anida": one decision, at most one roll, at the point the decision
    /// is actually made.
    /// </para>
    ///
    /// <para>
    /// <b>BOARD_RESOLVE seam beyond the literal interface.</b> Annex D.2's
    /// <see cref="Resolve"/> takes only an RNG — no room for GDD §5.3's Inversión
    /// "elección con palanca en ≤4s" (a real-time, multi-tick player choice).
    /// <see cref="BeginResolvePhase"/>/<see cref="TickResolve"/>/
    /// <see cref="ResolveFinished"/> are an ADDITIVE mini-contract, not part of
    /// <see cref="IBoardModel"/>, mirroring the Move phase's own
    /// BeginMovePhase/TickMove/MoveFinished shape exactly: a future BOARD_RESOLVE
    /// integration ticks this trio the same way BOARD_MOVE ticks the Move trio,
    /// then calls the literal <see cref="Resolve"/> once <see cref="ResolveFinished"/>
    /// is true — the SessionStateMachine FSM's phase graph needs no new states for
    /// this (AC6), only its existing BoardResolve case body would tick these
    /// instead of its current 1-tick stub. Deposit tier reads the raw stick
    /// direction directly (W=0%, N=25%, E=50%, sticky across other directions),
    /// mirroring <c>SessionStateMachine.MapCardinalToWagerChoice</c>'s own
    /// FINAL_WAGER convention (left-to-right low-to-high, one direction unused) —
    /// deliberately NOT routed through InputInterpreter, for the same
    /// simplicity/no-debounce-needed reasoning as the Move phase above. When
    /// nothing needs a choice this round, the window closes immediately (GDD §5
    /// "cero tiempo muerto") rather than always waiting out the full 4s.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] global round marker (GDD §5.3: Inversión owner "paga 1
    /// estrella... cada vez que el marcador global de rondas avanza 3").</b> This
    /// model tracks its own round counter, incremented once per
    /// <see cref="BeginMovePhase"/> call (1-based: the Nth BeginMovePhase call
    /// makes this counter N) — the closest reading achievable without threading a
    /// round index through the Annex D.2 signature, which does not carry one. The
    /// payout check runs inside <see cref="Resolve"/>, using the counter value for
    /// the round that just finished moving.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] balances are self-contained.</b> This model owns its own
    /// per-seat coin balances (<see cref="SetBalances"/> to seed them; default 0),
    /// independent of <c>SessionStateMachine.Coins</c> (fed today by microgame
    /// payouts only — GDD §5.6 says both sources should ultimately feed the SAME
    /// pool, ~60% microgames / ~40% casillas). Reconciling the two into one shared
    /// balance is a real design question for whoever wires this into the FSM for
    /// real; flagged in the hand-off rather than resolved here, since no AC in
    /// this ticket exercises that integration and every AC is testable against
    /// this model in isolation.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] purchases/transfers clamp, never go negative.</b> GDD §5.3
    /// states this explicitly for Moneda(-) ("nunca por debajo de 0") but is silent
    /// for Propiedad's rival-landing transfer and for an unaffordable
    /// Propiedad/Estrella purchase. This model clamps every outgoing transfer to
    /// what the payer actually has (a partial transfer rather than a blocked one)
    /// and simply skips a purchase the landing player cannot fully afford (no
    /// partial buy) — the same "no balance ever negative" invariant applied
    /// uniformly (AC4).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] star relocation target.</b> GDD §5.3 says the Estrella square
    /// "se muda a otra posición (semilla)" after purchase but not how the target is
    /// chosen. This model reverts the vacated square to
    /// <see cref="BoardTileType.MonedaPositiva"/> (a neutral, stateless type) and
    /// picks the new position uniformly at random (seeded) among every OTHER
    /// square that is not currently Inversión or Propiedad — excluding those two
    /// avoids silently orphaning a square's stateful ownership/deposit history by
    /// overwriting it with Estrella.
    /// </para>
    ///
    /// <para>
    /// <b>Zero-alloc steady state (AC7).</b> <see cref="TickMove"/> touches only
    /// fixed-size per-seat arrays allocated once at construction — no
    /// LINQ/boxing/new[] per tick. <see cref="Resolve"/>/<see cref="GetSnapshot"/>
    /// are NOT required to be zero-alloc by AC7 (scoped to TickMove only) and are
    /// not written to be — <see cref="Resolve"/> builds its result arrays fresh
    /// each call (like every existing <c>IMicrogame.GetResult()</c>), while
    /// <see cref="GetSnapshot"/> reuses one <see cref="BoardSnapshot"/> instance
    /// (same "instancia doble-buffer" convention as <c>RenderState</c>) since it IS
    /// called every tick during BOARD_MOVE for live stop-meter rendering.
    /// </para>
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class BoardModel : IBoardModel
    {
        private const int MeterCycleLength = 6; // GDD §5.2: medidor cicla 1->6
        private const int DefaultInversionTierPercent = 0; // [ASSUMED] no choice made -> safest neutral (see class doc)
        private const int PropiedadPrice = 8;
        private const int PropiedadTransfer = 4;
        private const int EstrellaPrice = 15;
        private const int InversionStarPayoutInterval = 3; // GDD §5.3: "cada vez que el marcador... avanza 3"

        private readonly BoardConfig _config;
        private readonly BoardTileType[] _ring;
        private int _starSquareIndex;

        private readonly int[] _positions = new int[4];
        private readonly int[] _balances = new int[4];

        // ── Move-phase state (GDD §5.2 medidor de parada) ───────────────────────
        private readonly int[] _meterValue = new int[4];
        private readonly bool[] _meterFrozen = new bool[4];
        private int _moveTick;
        private bool _moveFinished;
        private readonly int _meterTicksPerStep;
        private readonly int _timeoutTicks;

        // ── Resolve-phase state (Inversión's <=4s deposit-choice window) ────────
        private readonly bool[] _landedOnInversionThisRound = new bool[4];
        private readonly int?[] _pendingInversionTierPercent = new int?[4];
        private bool _resolveNeedsInput;
        private bool _resolveFinished;
        private int _resolveTick;
        private readonly int _resolveWindowTicks;

        // ── Persistent square/seat state ─────────────────────────────────────────
        private readonly int[] _propertyOwner; // per ring index; -1 = unowned/not Propiedad
        private readonly int[,] _inversionCumulative; // [ringIndex, seat]
        private readonly int[,] _inversionFirstDepositRound; // [ringIndex, seat]; int.MaxValue = never deposited
        private readonly WeaponKind?[] _inventory = new WeaponKind?[4]; // per seat, max 1 (GDD §5.4)
        private int _roundsStarted; // [ASSUMED] "marcador global de rondas" -- see class doc

        private readonly BoardSnapshot _snapshot;

        /// <summary>Real construction: builds the ring via a seeded shuffle of the GDD §5.3 quota table.</summary>
        public BoardModel(BoardConfig config, SeededRandom setupRng)
        {
            if (setupRng == null) throw new ArgumentNullException(nameof(setupRng));

            _config = config;
            int[] quota = BoardTileQuota.Compute(config.RingSize);
            _ring = BoardRing.Build(quota, setupRng);

            (_meterTicksPerStep, _timeoutTicks, _resolveWindowTicks, _starSquareIndex,
                _propertyOwner, _inversionCumulative, _inversionFirstDepositRound, _snapshot) = InitCommon(_config, _ring);
        }

        /// <summary>Test/framework seam: an exact, caller-supplied ring layout instead of a seeded shuffle. Must contain exactly one Estrella square.</summary>
        public BoardModel(BoardConfig config, BoardTileType[] fixedRing)
        {
            if (fixedRing == null) throw new ArgumentNullException(nameof(fixedRing));
            if (fixedRing.Length != config.RingSize)
                throw new ArgumentException($"fixedRing.Length ({fixedRing.Length}) must equal config.RingSize ({config.RingSize})", nameof(fixedRing));

            _config = config;
            _ring = (BoardTileType[])fixedRing.Clone();

            (_meterTicksPerStep, _timeoutTicks, _resolveWindowTicks, _starSquareIndex,
                _propertyOwner, _inversionCumulative, _inversionFirstDepositRound, _snapshot) = InitCommon(_config, _ring);
        }

        private static (int meterTicksPerStep, int timeoutTicks, int resolveWindowTicks, int starIndex,
            int[] propertyOwner, int[,] inversionCumulative, int[,] inversionFirstDepositRound, BoardSnapshot snapshot)
            InitCommon(BoardConfig config, BoardTileType[] ring)
        {
            int starCount = 0, starIndex = -1;
            for (int i = 0; i < ring.Length; i++)
                if (ring[i] == BoardTileType.Estrella) { starCount++; starIndex = i; }
            if (starCount != 1)
                throw new ArgumentException($"ring must contain exactly one Estrella square (GDD §5.3: 'Solo hay una a la vez'); found {starCount}");

            int meterTicksPerStep = Math.Max(1, config.TicksPerSecond / 4);
            int timeoutTicks = (int)Math.Round(8.0 * config.TicksPerSecond);
            int resolveWindowTicks = (int)Math.Round(4.0 * config.TicksPerSecond);

            var propertyOwner = new int[ring.Length];
            for (int i = 0; i < propertyOwner.Length; i++) propertyOwner[i] = -1;

            var inversionCumulative = new int[ring.Length, 4];
            var inversionFirstDepositRound = new int[ring.Length, 4];
            for (int i = 0; i < ring.Length; i++)
                for (int s = 0; s < 4; s++)
                    inversionFirstDepositRound[i, s] = int.MaxValue;

            var snapshot = new BoardSnapshot(ring.Length);

            return (meterTicksPerStep, timeoutTicks, resolveWindowTicks, starIndex,
                propertyOwner, inversionCumulative, inversionFirstDepositRound, snapshot);
        }

        // ── Test/framework seams ─────────────────────────────────────────────────

        /// <summary>Overrides every seat's ring position directly. For deterministic square-effect testing/framework setup — real gameplay derives positions from <see cref="TickMove"/>.</summary>
        public void SetForcedPositions(int[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (positions.Length != 4) throw new ArgumentException("positions must have length 4", nameof(positions));
            for (int i = 0; i < 4; i++)
            {
                if (positions[i] < 0 || positions[i] >= _ring.Length)
                    throw new ArgumentOutOfRangeException(nameof(positions), $"positions[{i}]={positions[i]} out of range [0,{_ring.Length})");
            }
            for (int i = 0; i < 4; i++) _positions[i] = positions[i];
        }

        /// <summary>Overrides every seat's coin balance directly (default 0 — see class doc "[ASSUMED] balances are self-contained").</summary>
        public void SetBalances(int[] balances)
        {
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            if (balances.Length != 4) throw new ArgumentException("balances must have length 4", nameof(balances));
            for (int i = 0; i < 4; i++)
                if (balances[i] < 0) throw new ArgumentOutOfRangeException(nameof(balances), $"balances[{i}]={balances[i]} must be >= 0");
            for (int i = 0; i < 4; i++) _balances[i] = balances[i];
        }

        // ── IBoardModel: Move phase (GDD §5.2) ───────────────────────────────────

        /// <inheritdoc/>
        public void BeginMovePhase(SeededRandom rng)
        {
            // rng accepted per the Annex D.2 signature but not consumed -- see
            // class doc "BeginMovePhase's rng parameter".
            _moveTick = 0;
            _moveFinished = false;
            _roundsStarted++;
            for (int i = 0; i < 4; i++)
            {
                _meterFrozen[i] = false;
                _meterValue[i] = 1;
            }
        }

        /// <inheritdoc/>
        public void TickMove(in V2Snapshot input)
        {
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));
            if (_moveFinished) return;

            bool allFrozen = true;
            for (int i = 0; i < 4; i++)
            {
                if (!_meterFrozen[i])
                {
                    int cycling = 1 + (_moveTick / _meterTicksPerStep) % MeterCycleLength;
                    _meterValue[i] = cycling;

                    bool tapped = input.Players[i].Button; // level-triggered -- see class doc
                    bool timedOut = _moveTick >= _timeoutTicks;
                    if (tapped || timedOut) _meterFrozen[i] = true;
                    else allFrozen = false;
                }
            }

            _moveTick++;

            if (allFrozen)
            {
                _moveFinished = true;
                for (int i = 0; i < 4; i++)
                    _positions[i] = (_positions[i] + _meterValue[i]) % _ring.Length;
            }
        }

        /// <inheritdoc/>
        public bool MoveFinished => _moveFinished;

        // ── Additive resolve-phase seam (see class doc) ──────────────────────────

        /// <summary>Starts BOARD_RESOLVE: determines which seats landed on an Inversión square this round and opens their deposit-choice window (immediately closed if nobody needs one).</summary>
        public void BeginResolvePhase()
        {
            _resolveTick = 0;
            _resolveNeedsInput = false;
            for (int i = 0; i < 4; i++)
            {
                bool onInversion = _ring[_positions[i]] == BoardTileType.Inversion;
                _landedOnInversionThisRound[i] = onInversion;
                _pendingInversionTierPercent[i] = onInversion ? (int?)DefaultInversionTierPercent : null;
                if (onInversion) _resolveNeedsInput = true;
            }
            _resolveFinished = !_resolveNeedsInput;
        }

        /// <summary>Advances the deposit-choice window by one simulation tick.</summary>
        public void TickResolve(in V2Snapshot input)
        {
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));
            if (_resolveFinished) return;

            for (int i = 0; i < 4; i++)
            {
                if (!_landedOnInversionThisRound[i]) continue;
                int? tier = MapStickToTierPercent(input.Players[i].Stick);
                if (tier.HasValue) _pendingInversionTierPercent[i] = tier;
            }

            _resolveTick++;
            if (_resolveTick >= _resolveWindowTicks) _resolveFinished = true;
        }

        /// <summary>True once the deposit-choice window has closed (or never opened, if nobody landed on Inversión this round).</summary>
        public bool ResolveFinished => _resolveFinished;

        private static int? MapStickToTierPercent(Direction8 stick)
        {
            // W=0%, N=25%, E=50% -- mirrors SessionStateMachine.MapCardinalToWagerChoice's
            // own FINAL_WAGER convention (left-to-right low-to-high, one direction
            // unused). Any other direction (including S/diagonals/None) is a no-op:
            // sticky, keeps whatever was already pending.
            switch (stick)
            {
                case Direction8.W: return 0;
                case Direction8.N: return 25;
                case Direction8.E: return 50;
                default: return null;
            }
        }

        // ── IBoardModel: Resolve (GDD §5.3/§5.4) ─────────────────────────────────

        /// <inheritdoc/>
        public BoardResolution Resolve(SeededRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var coinFlows = new List<CoinDelta>();
            var stars = new List<StarEvent>();
            RoundModifier modifier = RoundModifier.None;
            bool modifierSet = false;

            for (int seat = 0; seat < 4; seat++)
            {
                int square = _positions[seat];
                switch (_ring[square])
                {
                    case BoardTileType.MonedaPositiva:
                        ResolveMonedaPositiva(rng, seat, coinFlows);
                        break;
                    case BoardTileType.MonedaNegativa:
                        ResolveMonedaNegativa(rng, seat, coinFlows);
                        break;
                    case BoardTileType.Inversion:
                        ResolveInversion(seat, square, coinFlows);
                        break;
                    case BoardTileType.Propiedad:
                        ResolvePropiedad(seat, square, coinFlows);
                        break;
                    case BoardTileType.Estrella:
                        ResolveEstrella(rng, seat, coinFlows, stars);
                        break;
                    case BoardTileType.Cangrejo:
                        ResolveCangrejo(rng, seat);
                        break;
                    case BoardTileType.Evento:
                        if (!modifierSet)
                        {
                            modifier = RollEvento(rng);
                            modifierSet = true;
                        }
                        break;
                }
            }

            ApplyInversionStarPayouts(stars);

            return new BoardResolution(coinFlows.ToArray(), stars.ToArray(), modifier);
        }

        private void ResolveMonedaPositiva(SeededRandom rng, int seat, List<CoinDelta> coinFlows)
        {
            int amount = rng.NextInt(3, 7); // GDD §5.3: +coins ~ U{3,6}
            _balances[seat] += amount;
            coinFlows.Add(new CoinDelta(CoinDelta.Bank, seat, amount));
        }

        private void ResolveMonedaNegativa(SeededRandom rng, int seat, List<CoinDelta> coinFlows)
        {
            int draw = rng.NextInt(2, 5); // GDD §5.3: -coins ~ U{2,4}
            int amount = Math.Min(draw, _balances[seat]); // GDD §5.3: "nunca por debajo de 0"
            if (amount <= 0) return;
            _balances[seat] -= amount;
            coinFlows.Add(new CoinDelta(seat, CoinDelta.Bank, amount));
        }

        private void ResolveInversion(int seat, int square, List<CoinDelta> coinFlows)
        {
            int percent = _pendingInversionTierPercent[seat] ?? DefaultInversionTierPercent;
            int amount = _balances[seat] * percent / 100;
            if (amount <= 0) return;

            bool firstDeposit = _inversionCumulative[square, seat] == 0;
            _balances[seat] -= amount;
            _inversionCumulative[square, seat] += amount;
            if (firstDeposit) _inversionFirstDepositRound[square, seat] = _roundsStarted;
            coinFlows.Add(new CoinDelta(seat, CoinDelta.Bank, amount));
        }

        private void ResolvePropiedad(int seat, int square, List<CoinDelta> coinFlows)
        {
            int owner = _propertyOwner[square];
            if (owner == -1)
            {
                if (_balances[seat] < PropiedadPrice) return; // [ASSUMED] cannot afford -- no purchase
                _balances[seat] -= PropiedadPrice;
                coinFlows.Add(new CoinDelta(seat, CoinDelta.Bank, PropiedadPrice));
                _propertyOwner[square] = seat;
            }
            else if (owner != seat)
            {
                int transfer = Math.Min(PropiedadTransfer, _balances[seat]); // [ASSUMED] clamp
                if (transfer <= 0) return;
                _balances[seat] -= transfer;
                _balances[owner] += transfer;
                coinFlows.Add(new CoinDelta(seat, owner, transfer));
            }
            // else: landed on own property -- no effect.
        }

        private void ResolveEstrella(SeededRandom rng, int seat, List<CoinDelta> coinFlows, List<StarEvent> stars)
        {
            if (_balances[seat] < EstrellaPrice) return; // cannot afford -- stays put
            _balances[seat] -= EstrellaPrice;
            coinFlows.Add(new CoinDelta(seat, CoinDelta.Bank, EstrellaPrice));
            stars.Add(new StarEvent(seat, 1));
            RelocateStar(rng);
        }

        private void ResolveCangrejo(SeededRandom rng, int seat)
        {
            int roll = rng.NextInt(0, 3);
            _inventory[seat] = (WeaponKind)roll; // GDD §5.4: replaces whatever was held
        }

        private static RoundModifier RollEvento(SeededRandom rng)
        {
            int roll = rng.NextInt(1, 6); // GDD §5.5: 5 named modifiers, excluding None
            return (RoundModifier)roll;
        }

        private void RelocateStar(SeededRandom rng)
        {
            int oldIndex = _starSquareIndex;
            _ring[oldIndex] = BoardTileType.MonedaPositiva; // [ASSUMED] revert -- see class doc

            int candidateCount = 0;
            for (int i = 0; i < _ring.Length; i++)
                if (i != oldIndex && _ring[i] != BoardTileType.Inversion && _ring[i] != BoardTileType.Propiedad)
                    candidateCount++;

            if (candidateCount == 0)
            {
                _ring[oldIndex] = BoardTileType.Estrella; // degenerate ring -- nowhere else to relocate to
                return;
            }

            int pick = rng.NextInt(0, candidateCount);
            int newIndex = -1;
            int seen = 0;
            for (int i = 0; i < _ring.Length; i++)
            {
                if (i == oldIndex || _ring[i] == BoardTileType.Inversion || _ring[i] == BoardTileType.Propiedad) continue;
                if (seen == pick) { newIndex = i; break; }
                seen++;
            }

            _ring[newIndex] = BoardTileType.Estrella;
            _starSquareIndex = newIndex;
        }

        private void ApplyInversionStarPayouts(List<StarEvent> stars)
        {
            if (_roundsStarted % InversionStarPayoutInterval != 0) return;
            for (int square = 0; square < _ring.Length; square++)
            {
                if (_ring[square] != BoardTileType.Inversion) continue;
                int owner = ComputeInversionOwner(square);
                if (owner >= 0) stars.Add(new StarEvent(owner, 1));
            }
        }

        private int ComputeInversionOwner(int square)
        {
            int owner = -1;
            int bestAmount = 0;
            int bestFirstRound = int.MaxValue;
            for (int seat = 0; seat < 4; seat++)
            {
                int amount = _inversionCumulative[square, seat];
                if (amount <= 0) continue;
                int firstRound = _inversionFirstDepositRound[square, seat];
                if (amount > bestAmount || (amount == bestAmount && firstRound < bestFirstRound))
                {
                    owner = seat;
                    bestAmount = amount;
                    bestFirstRound = firstRound;
                }
            }
            return owner;
        }

        // ── IBoardModel: snapshot ────────────────────────────────────────────────

        /// <inheritdoc/>
        public BoardSnapshot GetSnapshot()
        {
            Array.Copy(_ring, _snapshot.Ring, _ring.Length);
            Array.Copy(_positions, _snapshot.Positions, 4);
            Array.Copy(_balances, _snapshot.Balances, 4);
            Array.Copy(_meterValue, _snapshot.MeterValues, 4);
            Array.Copy(_meterFrozen, _snapshot.MeterFrozen, 4);
            Array.Copy(_propertyOwner, _snapshot.PropertyOwner, _ring.Length);
            for (int i = 0; i < _ring.Length; i++)
                _snapshot.InversionOwner[i] = _ring[i] == BoardTileType.Inversion ? ComputeInversionOwner(i) : -1;
            Array.Copy(_inventory, _snapshot.Weapons, 4);
            _snapshot.StarSquareIndex = _starSquareIndex;
            return _snapshot;
        }
    }
}
