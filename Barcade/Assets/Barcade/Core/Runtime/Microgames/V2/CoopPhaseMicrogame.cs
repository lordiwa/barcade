using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §7.1 — the special cooperative phase (1 per session, ronda 3 o 4):
    /// a 60-90s scenario that REPLACES the round's microgame, composed from
    /// MECH_08 (<see cref="SujetaMicrogame"/>) and MECH_09
    /// (<see cref="IgualaMicrogame"/>) "ampliadas" (extended). v2
    /// <see cref="IMicrogame"/> implementation. [ASSUMED SCOPE] the Sequencer
    /// quota wiring (exactly 1 coop special per session) is the parent
    /// TASK-041's own AC, not this ticket — this class is the phase itself.
    ///
    /// <para>
    /// <b>Composition, not reimplementation (the ticket's own explicit
    /// requirement).</b> <see cref="CoopLevelData.Objectives"/> is a FIXED,
    /// already-known-length array — every objective's REAL
    /// <see cref="SujetaMicrogame"/>/<see cref="IgualaMicrogame"/> instance is
    /// constructed once in this class's OWN constructor and
    /// <see cref="IMicrogame.Initialize"/>d (with this phase's own injected
    /// <see cref="SeededRandom"/>, in objective order — deterministic, no
    /// separate stream derivation needed) inside THIS class's own
    /// <see cref="Initialize"/>. <see cref="Tick"/> never constructs anything —
    /// it only ever advances through the pre-built array, so the whole round
    /// is genuinely zero-alloc, not merely "steady-state excusing rare
    /// transitions." Each sub-mechanic's own hold/relay RULES (win/loss,
    /// tick-exact windows, abandonment, no-internal-ranking) are entirely
    /// theirs — this class only reads their public <see cref="IMicrogame"/>
    /// surface (<c>Tick</c>/<c>IsFinished</c>/<c>GetResult</c>), never their
    /// internals.
    /// </para>
    ///
    /// <para>
    /// <b>Flow: approach, then engage (GDD's own dual input framing — palanca
    /// to move, botón/palanca to interact).</b> While NOT yet engaged with the
    /// current objective, every active seat's stick freely moves its own
    /// avatar (uniform speed, see below) toward wherever the player steers —
    /// bottleneck/forced-intersection collision resolution (if enabled) runs
    /// every such tick. Once every ENGAGED seat
    /// (<see cref="IsEngaged"/> — the same Human-or-Bot predicate
    /// <c>SujetaMicrogame</c>/<c>IgualaMicrogame</c> already use, so an
    /// Idle/Empty seat can never block arrival) is within
    /// <see cref="CoopLevelData.ArrivalRadius"/> of the objective's position,
    /// control becomes CONTEXT-SENSITIVE: the SAME per-tick
    /// <see cref="InputSnapshot"/> is forwarded unchanged to that objective's
    /// own sub-mechanic (avatars freeze at their arrival spots — "walking up
    /// to a console operates the console, not your legs"). When the
    /// sub-mechanic finishes, its <see cref="MicrogameResult.Kind"/>
    /// (CoopSuccess/CoopFail) adds +1/-1 to the shared team score and the team
    /// advances to the next objective.
    /// </para>
    ///
    /// <para>
    /// <b>Uniform movement (GDD hard rule, AC2 — structural, not a
    /// convention).</b> There is exactly ONE speed field
    /// (<see cref="CoopLevelData.AvatarSpeed"/>) and exactly ONE radius field
    /// (<see cref="CoopLevelData.AvatarRadius"/>) in the whole class, read
    /// identically for every seat in <see cref="MoveAvatarsFreely"/> — no
    /// per-seat lookup, no role branch, nothing to special-case. Two seats
    /// given identical input produce byte-identical trajectories, by
    /// construction (proven directly in
    /// <c>CoopPhaseMicrogameV2Tests</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Inter-avatar collision exists ONLY here (GDD AC3).</b>
    /// <see cref="ResolveAvatarCollisions"/> is the only avatar-vs-avatar
    /// collision code in the entire Core runtime — no other v2 mechanic
    /// (Esquiva, Persigue, ...) has players collide with each other at all —
    /// and even here it only runs when
    /// <see cref="CoopLevelData.ForcedIntersectionEnabled"/> is true (GDD:
    /// "colisión entre jugadores activada SOLO en esta fase" — and, within
    /// this phase, only for this one named element).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] sueloDinamico ("el terreno se divide... deben
    /// renegociarse") is realized as a one-time, deterministic geometry
    /// reshuffle</b> at the GDD-literal <see cref="CoopLevelData.DynamicFloorTick"/>
    /// (t=30s): every NOT-YET-REACHED objective's X/Y swap
    /// (<see cref="ApplyDynamicFloorShift"/>) — a real, testable geometric
    /// change that forces the team to physically reroute, without simulating
    /// an actual splitting-terrain animation (out of scope for Core) or the
    /// "en voz alta" verbal renegotiation itself (a human social behavior
    /// Core enables structurally but never simulates, the same P2 pattern as
    /// every other social-effect note in the GDD).
    /// </para>
    ///
    /// <para>
    /// <b>Payout (GDD §6.1/P2).</b> <see cref="GetResult"/> always returns an
    /// EMPTY <see cref="MicrogameResult.Ranks"/> array — structural, no
    /// internal ranking. <see cref="GetPayoutCoins"/> maps the final team
    /// score against <see cref="CoopLevelData"/>'s three thresholds to the
    /// GDD-literal 2/4/6 coin tiers (<see cref="BronzeCoins"/>/<see cref="PlataCoins"/>/<see cref="OroCoins"/>),
    /// with a <see cref="NoneCoins"/> floor of 1 below Bronze — §6.1's
    /// minimum-reward invariant (never a zero payout) holds even for a team
    /// that never reaches Bronze.
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state (indeed, in EVERY) <see cref="Tick"/>.
    /// <see cref="GetResult"/> allocates once per round (an empty array), the
    /// same accepted pattern as every other v2 mechanic.
    /// </summary>
    public sealed class CoopPhaseMicrogame : IMicrogame
    {
        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        public const int NoneCoins = 1;
        public const int BronzeCoins = 2;
        public const int PlataCoins = 4;
        public const int OroCoins = 6;

        private static readonly (float x, float y)[] BottleneckWalls = { (0.5f, 0.35f), (0.5f, 0.65f) };
        private const float BottleneckWallRadius = 0.28f;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly CoopLevelData _params;
        private readonly IMicrogame[] _subMechanics;
        private readonly float[] _objectiveX;
        private readonly float[] _objectiveY;
        private readonly RenderState _renderState;

        // Stored only for IMicrogame contract compliance (GDD §10.2) — this
        // class itself consumes no randomness directly; it only forwards the
        // injected rng, in objective order, to each sub-mechanic's own
        // Initialize (their own randomness, e.g. IgualaMicrogame's sequence
        // generation, is entirely theirs).
        private SeededRandom _rng;
        private PlayerRoster _roster;

        private readonly float[] _avatarX = new float[4];
        private readonly float[] _avatarY = new float[4];

        private int _tick;
        private int _durationTicks;
        private bool _isFinished;

        private int _currentObjectiveIndex;
        private bool _engagedWithObjective;
        private int _teamScore;
        private bool _dynamicFloorApplied;

        public CoopPhaseMicrogame(CoopLevelData levelData)
        {
            _params = levelData;

            _subMechanics = new IMicrogame[levelData.Objectives.Length];
            for (int i = 0; i < _subMechanics.Length; i++)
            {
                CoopObjective o = levelData.Objectives[i];
                _subMechanics[i] = o.Kind == CoopObjectiveKind.Sujeta
                    ? (IMicrogame)new SujetaMicrogame(o.SujetaParams)
                    : new IgualaMicrogame(o.IgualaParams);
            }

            _objectiveX = new float[levelData.Objectives.Length];
            _objectiveY = new float[levelData.Objectives.Length];

            // 4 avatars + 1 current-objective marker.
            _renderState = new RenderState(4 + 1, 4);
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.CoopPhase;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;

            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);

            _currentObjectiveIndex = 0;
            _engagedWithObjective = false;
            _teamScore = 0;
            _dynamicFloorApplied = false;

            for (int i = 0; i < _params.Objectives.Length; i++)
            {
                _objectiveX[i] = _params.Objectives[i].X;
                _objectiveY[i] = _params.Objectives[i].Y;
                // Sequential, deterministic: each sub-mechanic draws its own
                // randomness from the SAME rng instance, in a fixed order —
                // same reproducibility guarantee as a single stream feeding
                // multiple independent draws.
                _subMechanics[i].Initialize(rng, roster, difficultyMult);
            }

            for (int i = 0; i < 4; i++)
            {
                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[i] = _forcedAvatarPositions[i].x;
                    _avatarY[i] = _forcedAvatarPositions[i].y;
                }
                else
                {
                    (float sx, float sy) = StartCorner(i);
                    _avatarX[i] = sx;
                    _avatarY[i] = sy;
                }
            }

            PublishRenderState();
        }

        // Test injection (mirrors EsquivaMicrogame/PersigueMicrogame's own SetForcedAvatarPositions).
        private (float x, float y)[] _forcedAvatarPositions;

        /// <summary>Override starting avatar positions for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedAvatarPositions((float x, float y)[] positions) => _forcedAvatarPositions = positions;

        /// <summary>Test-only: directly sets the team score, bypassing real objective play — for isolating <see cref="GetPayoutCoins"/>'s threshold-mapping logic from the (lengthy) full playthrough that would otherwise be needed to reach it.</summary>
        public void SetTeamScoreForTest(int score) => _teamScore = score;

        /// <inheritdoc/>
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            if (!_engagedWithObjective && _currentObjectiveIndex < _subMechanics.Length)
            {
                MoveAvatarsFreely(input);
                if (_params.ForcedIntersectionEnabled) ResolveAvatarCollisions();
                if (_params.BottleneckEnabled) ResolveBottleneckObstacles();

                if (AllEngagedSeatsHaveArrived(_currentObjectiveIndex))
                    _engagedWithObjective = true;
            }
            else if (_engagedWithObjective)
            {
                IMicrogame sub = _subMechanics[_currentObjectiveIndex];
                sub.Tick(input); // avatars frozen -- control is now the objective's

                if (sub.IsFinished)
                {
                    MicrogameResult result = sub.GetResult();
                    _teamScore += result.Kind == ResultKind.CoopSuccess ? 1 : -1;
                    _currentObjectiveIndex++;
                    _engagedWithObjective = false;
                }
            }

            if (_params.DynamicFloorEnabled && !_dynamicFloorApplied && _tick >= CoopLevelData.DynamicFloorTick)
            {
                ApplyDynamicFloorShift();
                _dynamicFloorApplied = true;
            }

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks || _currentObjectiveIndex >= _subMechanics.Length)
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            bool reachedBronze = _teamScore >= _params.BronzeThreshold;
            return new MicrogameResult(
                reachedBronze ? ResultKind.CoopSuccess : ResultKind.CoopFail,
                Array.Empty<PlayerRank>(),
                _teamScore);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        /// <summary>GDD §7.1 "umbrales de recompensa": maps the final team score to the exact 2/4/6 coin tiers (1 below Bronze — never zero, §6.1).</summary>
        public int GetPayoutCoins()
        {
            if (_teamScore >= _params.OroThreshold) return OroCoins;
            if (_teamScore >= _params.PlataThreshold) return PlataCoins;
            if (_teamScore >= _params.BronzeThreshold) return BronzeCoins;
            return NoneCoins;
        }

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2 mechanic).
        // ------------------------------------------------------------------

        public (float x, float y) GetAvatarPosition(PlayerSlot slot) => (_avatarX[(int)slot], _avatarY[(int)slot]);
        public int TeamScore => _teamScore;
        public int CurrentObjectiveIndex => _currentObjectiveIndex;
        public bool IsEngagedWithObjective => _engagedWithObjective;
        public (float x, float y) GetObjectivePosition(int index) => (_objectiveX[index], _objectiveY[index]);

        /// <summary>The real, pre-built sub-mechanic instance for objective <paramref name="index"/> — exposed so tests/telemetry can verify genuine composition (e.g. its concrete type) without reaching into private state.</summary>
        public IMicrogame GetSubMechanic(int index) => _subMechanics[index];

        public bool IsEngaged(PlayerSlot slot) => IsEngagedSeat((int)slot);

        // ------------------------------------------------------------------

        private bool IsEngagedSeat(int seatIndex) =>
            _roster.Seats[seatIndex] == SeatState.Human || _roster.Seats[seatIndex] == SeatState.Bot;

        private bool AllEngagedSeatsHaveArrived(int objectiveIndex)
        {
            float ox = _objectiveX[objectiveIndex], oy = _objectiveY[objectiveIndex];
            bool anyEngaged = false;

            for (int i = 0; i < 4; i++)
            {
                if (!IsEngagedSeat(i)) continue;
                anyEngaged = true;

                float dx = _avatarX[i] - ox, dy = _avatarY[i] - oy;
                if (MathF.Sqrt(dx * dx + dy * dy) > _params.ArrivalRadius) return false;
            }

            return anyEngaged;
        }

        private void MoveAvatarsFreely(in InputSnapshot input)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                (float dx, float dy) = InputBridge.ToUnitVector(input.Players[i].Stick);
                _avatarX[i] = Clamp01(_avatarX[i] + dx * _params.AvatarSpeed * DtSeconds);
                _avatarY[i] = Clamp01(_avatarY[i] + dy * _params.AvatarSpeed * DtSeconds);
            }
        }

        /// <summary>GDD "interseccionForzada" — the only avatar-vs-avatar collision anywhere in Core, and only while this flag is set.</summary>
        private void ResolveAvatarCollisions()
        {
            float minDist = _params.AvatarRadius * 2f;

            for (int i = 0; i < 4; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                for (int j = i + 1; j < 4; j++)
                {
                    if (!_roster.IsActive(AllSlots[j])) continue;

                    float dx = _avatarX[j] - _avatarX[i], dy = _avatarY[j] - _avatarY[i];
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist >= minDist) continue;

                    if (dist < 1e-6f) { dx = 1f; dy = 0f; dist = 1f; } // degenerate: exact overlap
                    float push = (minDist - dist) * 0.5f;
                    _avatarX[i] = Clamp01(_avatarX[i] - dx / dist * push);
                    _avatarY[i] = Clamp01(_avatarY[i] - dy / dist * push);
                    _avatarX[j] = Clamp01(_avatarX[j] + dx / dist * push);
                    _avatarY[j] = Clamp01(_avatarY[j] + dy / dist * push);
                }
            }
        }

        /// <summary>GDD "cuelloBotella" — [ASSUMED] geometry: a fixed wall pair narrowing the passable gap to a 1-unit-equivalent corridor.</summary>
        private void ResolveBottleneckObstacles()
        {
            float minDist = _params.AvatarRadius + BottleneckWallRadius;

            for (int i = 0; i < 4; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                for (int w = 0; w < BottleneckWalls.Length; w++)
                {
                    float wx = BottleneckWalls[w].x, wy = BottleneckWalls[w].y;
                    float dx = _avatarX[i] - wx, dy = _avatarY[i] - wy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist >= minDist) continue;

                    if (dist < 1e-6f) { dx = 1f; dy = 0f; dist = 1f; }
                    float push = minDist - dist;
                    _avatarX[i] = Clamp01(_avatarX[i] + dx / dist * push);
                    _avatarY[i] = Clamp01(_avatarY[i] + dy / dist * push);
                }
            }
        }

        /// <summary>GDD "sueloDinamico" — [ASSUMED] a deterministic X/Y swap of every not-yet-reached objective, at the exact GDD tick.</summary>
        private void ApplyDynamicFloorShift()
        {
            for (int i = _currentObjectiveIndex; i < _objectiveX.Length; i++)
                (_objectiveX[i], _objectiveY[i]) = (_objectiveY[i], _objectiveX[i]);
        }

        private static (float, float) StartCorner(int seatIndex)
        {
            switch (seatIndex)
            {
                case 0: return (0.15f, 0.15f);
                case 1: return (0.85f, 0.15f);
                case 2: return (0.85f, 0.85f);
                default: return (0.15f, 0.85f);
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡COOPERA!";

            int entityCount = 0;
            for (int i = 0; i < 4; i++)
            {
                _renderState.Hud.Scores[i] = _teamScore; // P2: identical shared score, no per-seat ranking

                if (!_roster.IsActive(AllSlots[i])) continue;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = _avatarX[i];
                _renderState.Entities[entityCount].Y = _avatarY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = 0;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            if (_currentObjectiveIndex < _objectiveX.Length)
            {
                _renderState.Entities[entityCount].Kind = EntityKind.Target;
                _renderState.Entities[entityCount].OwnerSeat = -1;
                _renderState.Entities[entityCount].X = _objectiveX[_currentObjectiveIndex];
                _renderState.Entities[entityCount].Y = _objectiveY[_currentObjectiveIndex];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = _engagedWithObjective ? (byte)1 : (byte)0;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
