using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_02 — ¡ESQUIVA! (dodge/survival). T-107 slice 1: v2
    /// <see cref="IMicrogame"/> implementation, adapting the v1
    /// <c>Barcade.Core.EsquivaMicrogame</c> (TASK-009, 4-player one-hit-eliminates
    /// FFA dodge) to the canonical contract. That v1 class and its tests are
    /// untouched (v1/v2 coexist, same rationale as every other mechanic's
    /// migration — see <see cref="IMicrogame"/>'s own doc). "Esquiva-3D"
    /// (<c>Barcade.Core.Dodge.DodgeSim</c>, the shrinking-floor demo with Kenney
    /// models) is a SEPARATE thing entirely — GDD §10.4 keeps it as a View/style
    /// reference for the Framework presenter, not a simulation to adapt; nothing
    /// in this class reuses or depends on it.
    ///
    /// <para>
    /// <b>Arena and collision (GDD literal).</b> Normalized [0,1]² logical arena.
    /// Player circle radius 0.03 (<see cref="EsquivaParams.AvatarRadius"/>, GDD
    /// literal). One hit eliminates for the round — no lives. Hazard shape is
    /// [ASSUMED] a circle (GDD offers "AABB/círculo" as either; circle-only keeps
    /// every pattern's collision test uniform and matches what the v1 mechanic
    /// already did).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Hazard pattern geometry (human-ruled 2026-07-07, engineering
    /// [ASSUMED], reviewer-verified — GDD §4 names the four patterns but never
    /// defines their motion).</b> One pattern is active per round (a definition's
    /// <see cref="EsquivaParams.HazardPattern"/>), applied to every hazard it spawns:
    /// <list type="bullet">
    /// <item><description><b>Rain</b>: spawns at a random X on the top edge (Y=0), falls straight toward Y=1.</description></item>
    /// <item><description><b>Sides</b>: spawns at a random Y on the left (X=0) or right (X=1) edge, moves straight across.</description></item>
    /// <item><description><b>Cross</b>: spawns at one of the 4 corners (chosen by the RNG), moves straight toward the diagonally opposite corner.</description></item>
    /// <item><description><b>HomingSoft</b>: spawns at a random point on the arena perimeter, then every tick steers toward the nearest non-eliminated active player, its heading capped to <see cref="HomingTurnRateDegPerSec"/> (GDD literal: 30°/s "para que siempre exista escape").</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Spawner (GDD literal formula).</b> <c>spawnRate(t) = spawnRateBase * (1 +
    /// rampCoef * t)</c>, t = seconds elapsed this round. Treated as an
    /// instantaneous rate: the next spawn threshold is always
    /// <c>now + 1/spawnRate(now)</c>, recomputed after every spawn — deterministic
    /// timing (GDD: "trayectorias deterministas"), with the RNG used only for
    /// WHERE (and, for Sides, which edge) each hazard spawns. A fixed-capacity
    /// pool (<see cref="MaxHazards"/>) holds active hazards; one is despawned once
    /// it exits the arena by <see cref="HazardExitMargin"/> (Rain/Sides/Cross
    /// naturally traverse and exit; HomingSoft effectively never does, since it
    /// keeps chasing a player who stays within [0,1]² — its count is bounded by
    /// the spawn rate over the round's short duration instead).
    /// </para>
    ///
    /// <para>
    /// <b>No-overlap-at-spawn (GDD AC3: separation ≥ 0.08).</b> A candidate spawn
    /// position is rejected and redrawn (up to <see cref="MaxSpawnAttempts"/>
    /// times) if it is closer than <see cref="MinSpawnSeparation"/> to any
    /// currently-alive hazard, or closer than
    /// <see cref="PlayerSpawnSafetyMargin"/> to any active player ([ASSUMED]
    /// fairness enhancement beyond the GDD's literal hazard-to-hazard-only text —
    /// see <see cref="TooCloseToAnyActivePlayer"/>). If every attempt still
    /// overlaps (observed under Cross's only-4-corners geometry at a
    /// stress-tested spawn rate well above <see cref="EsquivaParams.GddDefaults"/>),
    /// the spawn is DEFERRED to the next tick rather than forced through — unlike
    /// <see cref="Barcade.Core.Sequencer.V2.MicrogameSelector"/>'s own constraint
    /// relaxation (which trades a soft constraint away rather than stall), this
    /// guarantee is treated as non-negotiable: a slightly late spawn is a minor
    /// timing deviation, but an overlapping one is exactly the unfair "free hit"
    /// scenario AC2 exists to prevent.
    /// </para>
    ///
    /// <para>
    /// <b>Ranking (GDD literal + gap-fill).</b> Every active seat's
    /// <see cref="PlayerRank.Metric"/> is its ticks-survived value — the
    /// elimination tick if hit, or the full round duration in ticks if not (GDD's
    /// own example use for <c>Metric</c>: "survival ticks"). Ranked DESCENDING
    /// (more ticks survived = better place) with the standard shared-place-on-tie
    /// rule already established by <see cref="ReaccionaMicrogame"/> — this alone
    /// satisfies both GDD edge cases with no special-casing: seats eliminated on
    /// the exact same tick share a place (identical ticks-survived), and every
    /// seat that survives to the timeout shares Place 1 (identical
    /// ticks-survived = the full duration). GDD's own near-miss cosmetic tiebreak
    /// among timeout survivors ("por orden de menor daño rozado... si no hay
    /// dato, comparten puesto") is explicitly optional per its own text — not
    /// implemented here (no near-miss/near-graze distance tracking); survivors
    /// simply share Place 1, which the GDD text itself sanctions as the no-data
    /// fallback.
    /// </para>
    ///
    /// <para>
    /// <b>Escapability (GDD AC1, the headline correctness pin).</b> AvatarSpeed
    /// (<see cref="EsquivaParams.AvatarSpeed"/>, GDD-gap engineering default,
    /// 0.35 units/s) is set to more than double HazardSpeed
    /// (<see cref="EsquivaParams.HazardSpeed"/>, default 0.15 units/s) precisely so
    /// a straightforward reactive avoidance policy has real margin against every
    /// pattern, including HomingSoft's rate-capped pursuit (a faster evader always
    /// eventually breaks a bounded-turn-rate pursuer's lock by moving
    /// perpendicular to its heading). See
    /// <c>EsquivaMicrogameV2Tests</c>'s escapability bot for the actual solver
    /// (a real reactive policy — inverse-distance-weighted repulsion from every
    /// alive hazard, not a hardcoded path) and its seed sweep.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED SCOPE] Jump/dash is out of scope.</b> GDD names "botón tap
    /// (salto/dash corto, según variante)" as a per-definition VARIANT, and
    /// escapability is guaranteed by movement alone (HomingSoft's turn cap), not
    /// by jumping. <see cref="EsquivaParams.JumpEnabled"/> is declared for
    /// definition/schema completeness only; no AC in this ticket names jump/dash
    /// behavior, and this mechanic does not read <c>PlayerInput.Button</c> at all.
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty scaling (GDD §9.1: "aplicado a velocidad/densidad").</b>
    /// <c>difficultyMult</c> (already the GDD's own D(r)=1+0.06(r-1)-shaped
    /// multiplier, NOT the v1 EsquivaMicrogame's normalized [0,1] progress value)
    /// multiplies <see cref="EsquivaParams.HazardSpeed"/> ("velocidad") and
    /// <see cref="EsquivaParams.SpawnRateBasePerSec"/> ("densidad") directly at
    /// <see cref="Initialize"/> time — the two GDD names its own §9.1 prose
    /// literally, and the same [ASSUMED] direct-multiply interpretation
    /// <c>MicrogameDefinitionV2.DifficultyScaling</c> names for these two params.
    /// </para>
    ///
    /// Fixed 60 Hz tick (GDD §3.2/§10.2 — <see cref="Tick"/> always represents
    /// exactly one 60 Hz simulation tick; no dt is passed in). Pure C# — no
    /// UnityEngine dependency. C# 9 compatible. Zero heap allocation in
    /// steady-state <see cref="Tick"/> (all per-seat/per-hazard state is
    /// fixed-size arrays allocated once in the constructor; C# value tuples used
    /// throughout are structs, not heap allocations). <see cref="GetResult"/>
    /// allocates once per round, the same accepted class as every other v2
    /// mechanic's own result-building.
    /// </summary>
    public sealed class EsquivaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int MaxHazards = 16;
        private const int EntityCapacity = SeatCount + MaxHazards;
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        /// <summary>GDD §4 literal: minimum spawn separation between hazards.</summary>
        public const float MinSpawnSeparation = 0.08f;

        /// <summary>
        /// [ASSUMED] fairness enhancement, not GDD literal: minimum spawn
        /// distance from any active player — see <see cref="TooCloseToAnyActivePlayer"/>.
        /// </summary>
        public const float PlayerSpawnSafetyMargin = 0.2f;

        /// <summary>GDD §4 literal: HomingSoft's escape-guaranteeing turn-rate cap.</summary>
        public const float HomingTurnRateDegPerSec = 30f;

        /// <summary>
        /// [ASSUMED] engineering addition, not GDD literal: a HomingSoft hazard
        /// despawns this many ticks after spawning, regardless of position.
        /// Rain/Sides/Cross hazards naturally exit the arena and despawn on
        /// their own (<see cref="HazardExitMargin"/>); HomingSoft, by design,
        /// keeps chasing an in-bounds player and essentially never exits on its
        /// own. Without a lifetime cap, HomingSoft hazards accumulate
        /// unboundedly over a round's duration (bounded only by
        /// <see cref="MaxHazards"/>) until several are simultaneously homing on
        /// the same seat -- an escapability guarantee about ONE hazard's 30°/s
        /// turn cap says nothing about surviving an ever-growing pile of
        /// simultaneous perfect pursuers, and GDD names no lifetime for this
        /// pattern to begin with. 4 seconds keeps at most 1-2 alive at once
        /// under <see cref="EsquivaParams.GddDefaults"/>'s spawn rate.
        /// </summary>
        private const int HomingLifetimeTicks = 240; // 4s at 60 Hz

        /// <summary>Margin past [0,1]² beyond which a hazard is despawned.</summary>
        private const float HazardExitMargin = 0.15f;

        /// <summary>Bounded retries for the no-overlap-at-spawn guarantee (liveness over constraints).</summary>
        private const int MaxSpawnAttempts = 20;

        public const byte CueEliminated = 1;

        public const byte VariantAlive = 0;
        public const byte VariantEliminated = 1;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly EsquivaParams _params;
        private readonly RenderState _renderState;

        private SeededRandom _rng;
        private PlayerRoster _roster;

        private float _effectiveHazardSpeed;
        private float _effectiveSpawnRateBase;

        private int _tick;
        private int _durationTicks;
        private float _elapsedSeconds;
        private float _nextSpawnAt;
        private bool _isFinished;

        private readonly float[] _avatarX = new float[SeatCount];
        private readonly float[] _avatarY = new float[SeatCount];
        private readonly bool[] _eliminated = new bool[SeatCount];
        private readonly int[] _eliminationTick = new int[SeatCount];

        private readonly bool[] _hazardAlive = new bool[MaxHazards];
        private readonly float[] _hazardX = new float[MaxHazards];
        private readonly float[] _hazardY = new float[MaxHazards];
        private readonly float[] _hazardDirX = new float[MaxHazards];
        private readonly float[] _hazardDirY = new float[MaxHazards];
        private readonly int[] _hazardSpawnTick = new int[MaxHazards];

        public EsquivaMicrogame() : this(EsquivaParams.GddDefaults)
        {
        }

        public EsquivaMicrogame(EsquivaParams parameters)
        {
            _params = parameters;
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        // ── Test injection (mirrors v1 EsquivaMicrogame's SetForced*/ApuntaMicrogame's
        // SetForcedTargetDirections pattern for deterministic collision tests) ──────

        private (float x, float y)[] _forcedAvatarPositions;

        /// <summary>Override starting avatar positions for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedAvatarPositions((float x, float y)[] positions) => _forcedAvatarPositions = positions;

        /// <summary>
        /// Directly injects a hazard into a free pool slot for deterministic
        /// collision tests, bypassing the RNG-driven pattern spawner entirely.
        /// Returns false (no-op) if the pool is full.
        /// </summary>
        public bool ForceSpawnHazardForTest(float x, float y, float dirX, float dirY)
        {
            int slot = FindFreeHazardSlot();
            if (slot < 0) return false;

            _hazardX[slot] = x;
            _hazardY[slot] = y;
            _hazardDirX[slot] = dirX;
            _hazardDirY[slot] = dirY;
            _hazardSpawnTick[slot] = _tick;
            _hazardAlive[slot] = true;
            return true;
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Esquiva;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;

            _tick = 0;
            _elapsedSeconds = 0f;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);

            _effectiveHazardSpeed = _params.HazardSpeed * difficultyMult;
            _effectiveSpawnRateBase = _params.SpawnRateBasePerSec * difficultyMult;

            for (int i = 0; i < SeatCount; i++)
            {
                _eliminated[i] = false;
                _eliminationTick[i] = -1;

                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[i] = _forcedAvatarPositions[i].x;
                    _avatarY[i] = _forcedAvatarPositions[i].y;
                }
                else
                {
                    (float ax, float ay) = StartCorner(i);
                    _avatarX[i] = ax;
                    _avatarY[i] = ay;
                }
            }

            for (int h = 0; h < MaxHazards; h++)
                _hazardAlive[h] = false;

            _nextSpawnAt = 1f / SpawnRate(0f);

            PublishRenderState();
        }

        /// <inheritdoc/>
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _elapsedSeconds += DtSeconds;

            // A spawn whose every candidate attempt overlapped (pool full, or --
            // rare, seen under Cross's only-4-corners geometry at a stress-level
            // spawn rate -- every corner currently occupied) is DEFERRED, not
            // forced through: _nextSpawnAt does not advance, so the very next
            // tick retries rather than accepting a separation violation just to
            // keep pace with the schedule. Liveness is still preserved (retried
            // every tick until a slot opens up), it just costs timing precision
            // in this rare case rather than the AC2 guarantee itself.
            while (_elapsedSeconds >= _nextSpawnAt)
            {
                if (!TrySpawnHazard()) break;
                _nextSpawnAt += 1f / SpawnRate(_elapsedSeconds);
            }

            for (int h = 0; h < MaxHazards; h++)
            {
                if (!_hazardAlive[h]) continue;

                if (_params.HazardPattern == EsquivaHazardPattern.HomingSoft)
                    SteerHoming(h);

                _hazardX[h] += _hazardDirX[h] * _effectiveHazardSpeed * DtSeconds;
                _hazardY[h] += _hazardDirY[h] * _effectiveHazardSpeed * DtSeconds;

                if (_hazardX[h] < -HazardExitMargin || _hazardX[h] > 1f + HazardExitMargin ||
                    _hazardY[h] < -HazardExitMargin || _hazardY[h] > 1f + HazardExitMargin)
                {
                    _hazardAlive[h] = false;
                    continue;
                }

                if (_params.HazardPattern == EsquivaHazardPattern.HomingSoft
                    && _tick - _hazardSpawnTick[h] >= HomingLifetimeTicks)
                {
                    _hazardAlive[h] = false;
                }
            }

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_eliminated[i]) continue;

                (float dx, float dy) = InputBridge.ToUnitVector(input.Players[i].Stick);
                _avatarX[i] = Clamp01(_avatarX[i] + dx * _params.AvatarSpeed * DtSeconds);
                _avatarY[i] = Clamp01(_avatarY[i] + dy * _params.AvatarSpeed * DtSeconds);
            }

            float combinedRadius = _params.AvatarRadius + _params.HazardRadius;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_eliminated[i]) continue;

                for (int h = 0; h < MaxHazards; h++)
                {
                    if (!_hazardAlive[h]) continue;

                    float dx = _avatarX[i] - _hazardX[h];
                    float dy = _avatarY[i] - _hazardY[h];
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist <= combinedRadius)
                    {
                        _eliminated[i] = true;
                        _eliminationTick[i] = _tick;
                        break;
                    }
                }
            }

            PublishRenderState();

            bool anyActive = false;
            bool allEliminated = true;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                anyActive = true;
                if (!_eliminated[i]) { allEliminated = false; break; }
            }

            _tick++;

            if (_tick >= _durationTicks || (anyActive && allEliminated))
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            int n = 0;
            for (int i = 0; i < SeatCount; i++)
                if (_roster.IsActive(AllSlots[i])) n++;

            int[] seats = new int[n];
            int[] ticksSurvived = new int[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                seats[w] = i;
                ticksSurvived[w] = _eliminated[i] ? _eliminationTick[i] : _durationTicks;
                w++;
            }

            // Insertion sort DESCENDING by ticksSurvived (more survival = better place) — n <= 4.
            for (int i = 1; i < n; i++)
            {
                int seatKey = seats[i];
                int metricKey = ticksSurvived[i];
                int j = i - 1;
                while (j >= 0 && ticksSurvived[j] < metricKey)
                {
                    ticksSurvived[j + 1] = ticksSurvived[j];
                    seats[j + 1] = seats[j];
                    j--;
                }
                ticksSurvived[j + 1] = metricKey;
                seats[j + 1] = seatKey;
            }

            var ranks = new PlayerRank[n];
            for (int i = 0; i < n; i++)
            {
                int place;
                if (i == 0) place = 1;
                else if (ticksSurvived[i] == ticksSurvived[i - 1]) place = ranks[i - 1].Place;
                else place = i + 1;

                ranks[i] = new PlayerRank(seats[i], place, ticksSurvived[i]);
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------

        /// <summary>Test/telemetry accessor: true once <paramref name="slot"/> has been eliminated.</summary>
        public bool IsEliminated(PlayerSlot slot) => _eliminated[(int)slot];

        /// <summary>Test/telemetry accessor: the tick <paramref name="slot"/> was eliminated on, or -1 if still alive.</summary>
        public int GetEliminationTick(PlayerSlot slot) => _eliminationTick[(int)slot];

        /// <summary>
        /// Test-only accessor: the fixed hazard pool's capacity, exposed so a test
        /// can track a specific slot's alive/dead transition across ticks for
        /// stable hazard identity (unlike <see cref="RenderState"/>'s repacked,
        /// unstably-ordered entity list, a slot index never changes meaning for
        /// the lifetime of the hazard occupying it).
        /// </summary>
        public int HazardSlotCapacity => MaxHazards;

        /// <summary>Test-only accessor: whether hazard pool slot <paramref name="slot"/> is currently alive.</summary>
        public bool IsHazardSlotAlive(int slot) => _hazardAlive[slot];

        /// <summary>Test-only accessor: hazard pool slot <paramref name="slot"/>'s current position.</summary>
        public (float x, float y) GetHazardSlotPosition(int slot) => (_hazardX[slot], _hazardY[slot]);

        private float SpawnRate(float t) => _effectiveSpawnRateBase * (1f + _params.SpawnRampCoef * t);

        /// <summary>
        /// Attempts to spawn one hazard. Returns false (no state changed) if
        /// either the pool is full or every candidate within
        /// <see cref="MaxSpawnAttempts"/> failed the separation guarantees — the
        /// caller (<see cref="Tick"/>) defers rather than forces the spawn
        /// through in that case, so AC2's separation guarantee is never traded
        /// away for scheduling precision.
        /// </summary>
        private bool TrySpawnHazard()
        {
            int slot = FindFreeHazardSlot();
            if (slot < 0) return false; // pool full -- defer rather than corrupt state

            for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
            {
                (float x, float y, float dx, float dy) = GenerateCandidate();
                if (!OverlapsAnyAliveHazard(x, y) && !TooCloseToAnyActivePlayer(x, y))
                {
                    _hazardX[slot] = x;
                    _hazardY[slot] = y;
                    _hazardDirX[slot] = dx;
                    _hazardDirY[slot] = dy;
                    _hazardSpawnTick[slot] = _tick;
                    _hazardAlive[slot] = true;
                    return true;
                }
            }

            return false; // every attempt overlapped -- defer to the next tick
        }

        private bool OverlapsAnyAliveHazard(float x, float y)
        {
            for (int h = 0; h < MaxHazards; h++)
            {
                if (!_hazardAlive[h]) continue;
                float dx = x - _hazardX[h];
                float dy = y - _hazardY[h];
                if (MathF.Sqrt(dx * dx + dy * dy) < MinSpawnSeparation) return true;
            }
            return false;
        }

        /// <summary>
        /// [ASSUMED] fairness enhancement beyond GDD's literal spawn-separation
        /// text (which names only hazard-to-hazard separation): a hazard spawning
        /// with no separation from an ACTIVE player's current position would be an
        /// unavoidable "free" hit with zero reaction time, undermining the same
        /// escapability guarantee GDD AC1 asks for. Reject candidates within
        /// <see cref="PlayerSpawnSafetyMargin"/> of any active, non-eliminated
        /// player -- same liveness-over-constraints fallback as
        /// <see cref="OverlapsAnyAliveHazard"/> (the final retry attempt is still
        /// accepted unconditionally).
        /// </summary>
        private bool TooCloseToAnyActivePlayer(float x, float y)
        {
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_eliminated[i]) continue;

                float dx = x - _avatarX[i];
                float dy = y - _avatarY[i];
                if (MathF.Sqrt(dx * dx + dy * dy) < PlayerSpawnSafetyMargin) return true;
            }
            return false;
        }

        private int FindFreeHazardSlot()
        {
            for (int h = 0; h < MaxHazards; h++)
                if (!_hazardAlive[h]) return h;
            return -1;
        }

        private (float x, float y, float dx, float dy) GenerateCandidate()
        {
            switch (_params.HazardPattern)
            {
                case EsquivaHazardPattern.Rain:
                {
                    float x = _rng.NextFloat();
                    return (x, 0f, 0f, 1f);
                }
                case EsquivaHazardPattern.Sides:
                {
                    bool fromLeft = _rng.NextFloat() < 0.5f;
                    float y = _rng.NextFloat();
                    return fromLeft ? (0f, y, 1f, 0f) : (1f, y, -1f, 0f);
                }
                case EsquivaHazardPattern.Cross:
                {
                    const float diag = 0.70710678f;
                    int corner = _rng.NextInt(0, 4);
                    switch (corner)
                    {
                        case 0: return (0f, 0f, diag, diag);
                        case 1: return (1f, 0f, -diag, diag);
                        case 2: return (1f, 1f, -diag, -diag);
                        default: return (0f, 1f, diag, -diag);
                    }
                }
                default: // HomingSoft
                {
                    (float x, float y) = RandomPerimeterPoint();
                    (float dx, float dy) = InitialHomingDirection(x, y);
                    return (x, y, dx, dy);
                }
            }
        }

        private (float, float) RandomPerimeterPoint()
        {
            int edge = _rng.NextInt(0, 4); // 0=top,1=right,2=bottom,3=left
            float t = _rng.NextFloat();
            switch (edge)
            {
                case 0: return (t, 0f);
                case 1: return (1f, t);
                case 2: return (t, 1f);
                default: return (0f, t);
            }
        }

        private (float, float) InitialHomingDirection(float x, float y)
        {
            (float tx, float ty) = NearestActivePlayerPosition(x, y);
            float dx = tx - x, dy = ty - y;
            float mag = MathF.Sqrt(dx * dx + dy * dy);
            return mag > 1e-6f ? (dx / mag, dy / mag) : (0f, 1f);
        }

        private void SteerHoming(int h)
        {
            (float tx, float ty) = NearestActivePlayerPosition(_hazardX[h], _hazardY[h]);
            float toTargetX = tx - _hazardX[h];
            float toTargetY = ty - _hazardY[h];
            float mag = MathF.Sqrt(toTargetX * toTargetX + toTargetY * toTargetY);
            if (mag < 1e-6f) return;

            float desiredAngle = MathF.Atan2(toTargetY / mag, toTargetX / mag);
            float currentAngle = MathF.Atan2(_hazardDirY[h], _hazardDirX[h]);

            float delta = NormalizeAngle(desiredAngle - currentAngle);
            float maxStep = HomingTurnRateDegPerSec * (MathF.PI / 180f) * DtSeconds;
            float step = delta < -maxStep ? -maxStep : (delta > maxStep ? maxStep : delta);

            float newAngle = currentAngle + step;
            _hazardDirX[h] = MathF.Cos(newAngle);
            _hazardDirY[h] = MathF.Sin(newAngle);
        }

        private static float NormalizeAngle(float radians)
        {
            while (radians > MathF.PI) radians -= 2f * MathF.PI;
            while (radians < -MathF.PI) radians += 2f * MathF.PI;
            return radians;
        }

        private (float, float) NearestActivePlayerPosition(float fromX, float fromY)
        {
            float bestDistSq = float.MaxValue;
            float bestX = 0.5f, bestY = 0.5f; // arena-center fallback when no active player exists
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_eliminated[i]) continue;

                float dx = _avatarX[i] - fromX;
                float dy = _avatarY[i] - fromY;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestX = _avatarX[i];
                    bestY = _avatarY[i];
                }
            }
            return (bestX, bestY);
        }

        /// <summary>
        /// Per-seat starting corner, inset from the true arena corner (clockwise
        /// Rojo/Azul/Amarillo/Verde — the same per-seat cornering convention
        /// <c>ApuntaMicrogame</c>'s TurretCorner already establishes).
        /// </summary>
        private static (float, float) StartCorner(int seatIndex)
        {
            switch (seatIndex)
            {
                case 0: return (0.15f, 0.15f); // Rojo
                case 1: return (0.85f, 0.15f); // Azul
                case 2: return (0.85f, 0.85f); // Amarillo
                default: return (0.15f, 0.85f); // Verde
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡ESQUIVA!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = _eliminated[i] ? _eliminationTick[i] : _tick;

                if (!_roster.IsActive(AllSlots[i])) continue;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = _avatarX[i];
                _renderState.Entities[entityCount].Y = _avatarY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = _eliminated[i] ? VariantEliminated : VariantAlive;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            for (int h = 0; h < MaxHazards; h++)
            {
                if (!_hazardAlive[h]) continue;
                if (entityCount >= _renderState.Entities.Length) break; // defensive; capacity sized to fit SeatCount+MaxHazards

                _renderState.Entities[entityCount].Kind = EntityKind.Hazard;
                _renderState.Entities[entityCount].OwnerSeat = -1;
                _renderState.Entities[entityCount].X = _hazardX[h];
                _renderState.Entities[entityCount].Y = _hazardY[h];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = MathF.Atan2(_hazardDirY[h], _hazardDirX[h]) * (180f / MathF.PI);
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = (byte)_params.HazardPattern;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
