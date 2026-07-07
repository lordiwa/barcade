using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_01 — ¡MANTÉN! (inverted-pendulum balance). T-107 slice 3: v2
    /// <see cref="IMicrogame"/> implementation. This is a NEW mechanic (unlike
    /// ¡ESQUIVA!/¡APUNTA!, there is no v1 predecessor to adapt).
    /// <see cref="Barcade.Core.Alcocheck.AlcocheckSim"/> (the drunk-balance demo
    /// sim) is mined ONLY for its surrounding inverted-pendulum-integration
    /// structure (Euler integration, forced-lean test injection, latched
    /// terminal state) -- its own perturbation model (a discrete, periodic
    /// uniform re-roll) is deliberately NOT reused; see the noise-substitute
    /// ruling below.
    ///
    /// <para>
    /// <b>Simulation model (GDD literal).</b>
    /// <code>
    ///   θ'' = (g/L)·sin(θ) + perturbación(t) − k·input_palanca
    ///   perturbación(t) = A(t)·ruido_perlin(seed, t), A(t) = A0 + rampa·t
    /// </code>
    /// <see cref="MantenParams.GravityFactor"/> = g/L, <see cref="MantenParams.TorqueGain"/>
    /// = k, <see cref="MantenParams.PerturbAmplitude0"/> = A0,
    /// <see cref="MantenParams.PerturbRamp"/> = rampa. Fails when |θ| &gt;
    /// <see cref="MantenParams.ThetaMaxDegrees"/> (GDD literal default 35°,
    /// strict <c>&gt;</c> — exactly at the threshold is still safe).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Noise substitute (human-ruled 2026-07-07, reviewer-verified
    /// — cited by <see cref="SeededValueNoise1D"/>'s own class doc too).</b> GDD's
    /// literal "ruido_perlin" is substituted with <see cref="SeededValueNoise1D"/>,
    /// a deterministic value-noise curve (NOT required to be true Perlin) —
    /// smoothly interpolated between a lattice of independently-seeded random
    /// values, so the perturbation is continuous (no sudden jumps) rather than a
    /// discrete re-roll like AlcocheckSim's own drunk sway. The lattice density
    /// (<see cref="NoisePointsPerSecond"/>) is an [ASSUMED] engineering constant,
    /// not a GDD-named param — same status as AlcocheckSim's own
    /// <c>drunkChangeInterval</c> — not exposed for per-definition tuning.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Damping term (human-ruled 2026-07-07, same citation).</b> The
    /// GDD's literal ODE has no damping term. A small <see cref="MantenParams.Damping"/>
    /// term (<c>− damping·θ'</c>, added to the formula above) is kept for Euler
    /// integration stability, the identical role AlcocheckSim's own damping
    /// term already plays for the same inverted-pendulum integration pattern —
    /// without it, a bang-bang corrective controller (see AC2 below) would tend
    /// to chatter with growing amplitude rather than settle into a bounded
    /// oscillation around upright.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Shared perturbation curve across all 4 seats.</b> The GDD
    /// formula names a single "seed, t" — no per-player index — so this
    /// implementation samples ONE <see cref="SeededValueNoise1D"/> curve per
    /// round and applies the identical <c>perturbación(t)</c> value to every
    /// active seat's angular acceleration each tick (one shared "gust", not 4
    /// independent sways). A consequence, not a bug: 4 seats given IDENTICAL
    /// initial conditions and IDENTICAL input (e.g. the null-input AC test)
    /// follow byte-identical trajectories and fall on the exact same tick.
    /// </para>
    ///
    /// <para>
    /// <b>Sign convention (why pressing a direction sometimes "matches" theta's
    /// own sign).</b> The literal formula SUBTRACTS <c>k·input_palanca</c>. To
    /// counteract a disturbance <c>D = g·sin(θ) + perturbación(t)</c> (whatever
    /// its sign), the correcting <c>input_palanca</c> must satisfy
    /// <c>−k·input ≈ −D</c>, i.e. <c>input ≈ D/k</c> — the SAME sign as D, not
    /// the opposite. This is purely a consequence of the GDD's literal minus
    /// sign (code adapts to the GDD's formula, not to naive intuition about
    /// "which way to push"); see <c>MantenMicrogameV2Tests</c>'s
    /// <c>CorrectionOracle</c> for the concrete reactive controller this
    /// implies, and its <c>DiagonalInput_UsesHorizontalComponentOnly</c> /
    /// boundary tests for the sign verified by construction.
    /// </para>
    ///
    /// <para>
    /// <b>input_palanca (GDD casos borde: "entrada diagonal → se usa componente
    /// horizontal").</b> Read as <c>InputBridge.ToUnitVector(stick).X</c> — the
    /// horizontal component of the canonical stick-vector convention already
    /// shared with every other v2 mechanic (E=+1, W=-1, diagonals=±0.70710678,
    /// N/S/None=0). This single read already satisfies the GDD's literal edge
    /// case with no special-casing: a diagonal's Y component is simply never
    /// consulted. The button is unused this ticket ("botón sin uso" is the
    /// baseline GDD variant); <see cref="MantenParams.DashRecoveryEnabled"/> is
    /// declared for schema completeness only — no AC names dash/recovery
    /// behavior, mirroring <see cref="EsquivaParams.JumpEnabled"/>'s identical
    /// out-of-scope status.
    /// </para>
    ///
    /// <para>
    /// <b>Idle seat (GDD casos borde: "jugador Idle → su péndulo cae
    /// naturalmente, no se congela").</b> <see cref="SeatState.HumanIdle"/> is
    /// still an ACTIVE seat per <see cref="PlayerRoster.IsActive"/>, so this
    /// requires no special-casing at all: an Idle seat is simulated identically
    /// to any other active seat, and whatever input snapshot arrives for it
    /// (typically <see cref="Direction8.None"/>, since nobody is touching the
    /// stick) drives it exactly like a null-input seat would — it falls
    /// naturally rather than being frozen. Only a truly <see cref="SeatState.Empty"/>
    /// seat is skipped (not simulated, not rendered).
    /// </para>
    ///
    /// <para>
    /// <b>Elimination is latched.</b> Once a seat's |θ| exceeds thetaMax, its
    /// θ/velocity/mean-|θ| accumulation freeze for the rest of the round —
    /// same irreversible-elimination convention already established by
    /// <see cref="EsquivaMicrogame"/>/<see cref="ReaccionaMicrogame"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Victory / ranking (GDD literal).</b> "Último en pie" (last standing):
    /// <see cref="PlayerRank.Metric"/> = ticks survived (the fallen tick, or the
    /// full round duration if never fallen — directly telemetry-meaningful,
    /// same convention as <see cref="EsquivaMicrogame"/>'s own Metric),
    /// DESCENDING (more ticks survived = better place). GDD's own literal
    /// timeout rule is NOT a cosmetic afterthought like Esquiva's optional
    /// near-miss tiebreak (left unimplemented there) — "si el tiempo agota con
    /// ≥2 en pie, gana el de menor |θ| medio acumulado" is the actual primary
    /// mechanism for ranking multiple timeout survivors, so it IS implemented: a
    /// secondary sort key (mean |θ| accumulated over every simulated tick,
    /// ascending — lower wobble ranks better) breaks ties, but ONLY within the
    /// group of seats that survived to the exact full round duration (GDD scopes
    /// the rule to "si el tiempo agota" — seats eliminated earlier and tied on
    /// the same fallen tick still simply share a place, no secondary criterion
    /// the GDD names for that case). "Empate exacto" (identical mean |θ|,
    /// intentionally exact float equality — matching the GDD's own "empate
    /// exacto" framing and Esquiva's identical exact-tick-equality convention
    /// for its own simultaneous-elimination tie) → shared place, standard
    /// competition ranking (ties skip ahead).
    /// </para>
    ///
    /// Fixed 60 Hz tick (GDD §3.2/§10.2). Pure C# — no UnityEngine dependency.
    /// C# 9 compatible. Zero heap allocation in steady-state <see cref="Tick"/>
    /// (all per-seat state is fixed-size arrays allocated once in the
    /// constructor; the noise lattice is allocated once in
    /// <see cref="Initialize"/>, never inside <see cref="Tick"/>).
    /// <see cref="GetResult"/> allocates once per round, the same accepted class
    /// as every other v2 mechanic's own result-building.
    /// </summary>
    public sealed class MantenMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        /// <summary>
        /// [ASSUMED] engineering constant: lattice density of the shared
        /// perturbation noise curve (see <see cref="SeededValueNoise1D"/> /
        /// class doc's noise-substitute ruling). Not a GDD-named param — an
        /// implementation detail of the noise substitute, the same status as
        /// AlcocheckSim's own <c>drunkChangeInterval</c>.
        /// </summary>
        private const float NoisePointsPerSecond = 4f;

        /// <summary>Feedback cue: this tick, the named seat's pendulum toppled past thetaMax.</summary>
        public const byte CueFallen = 1;

        public const byte VariantStanding = 0;
        public const byte VariantFallen = 1;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly MantenParams _params;
        private readonly RenderState _renderState;

        private SeededRandom _rng;
        private PlayerRoster _roster;
        private SeededValueNoise1D _noise;

        private float _thetaMaxRadians;
        private float _effectivePerturbAmplitude0;
        private float _effectivePerturbRamp;

        private int _durationTicks;
        private int _tick;
        private float _elapsedSeconds;
        private bool _isFinished;

        private readonly float[] _theta = new float[SeatCount];
        private readonly float[] _thetaVelocity = new float[SeatCount];
        private readonly bool[] _fallen = new bool[SeatCount];
        private readonly int[] _fallenTick = new int[SeatCount];
        private readonly float[] _sumAbsTheta = new float[SeatCount];
        private readonly int[] _sampleCount = new int[SeatCount];

        // ── Test injection (mirrors AlcocheckSim's SetForcedLean / Esquiva's
        // SetForcedAvatarPositions pattern for deterministic tests) ─────────────

        private float[] _forcedTheta;
        private float[] _forcedThetaVelocity;

        public MantenMicrogame() : this(MantenParams.GddDefaults)
        {
        }

        public MantenMicrogame(MantenParams parameters)
        {
            _params = parameters;
            _renderState = new RenderState(SeatCount, FeedbackCapacity);
        }

        /// <summary>Override starting theta (and optionally angular velocity) for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedTheta(float[] thetaPerSeat, float[] velocityPerSeat = null)
        {
            _forcedTheta = thetaPerSeat;
            _forcedThetaVelocity = velocityPerSeat;
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Manten;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;

            _thetaMaxRadians = DegreesToRadians(_params.ThetaMaxDegrees);
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);
            _tick = 0;
            _elapsedSeconds = 0f;
            _isFinished = false;

            // GDD §9.1 ("aplicado a velocidad/densidad") gap-fill: MECH_01's own
            // analog of Esquiva's hazardSpeed/spawnRate is "how strong/dense the
            // disturbance is" -- PerturbAmplitude0/PerturbRamp -- direct multiply,
            // same convention as every other v2 mechanic's own difficultyMult
            // handling. GravityFactor/TorqueGain/ThetaMax are physics constants
            // of the mechanic itself, not session-difficulty knobs (GDD names no
            // difficultyScaling list for MECH_01 the way §11.1's MECH_04 example
            // does -- [ASSUMED] mapping, flagged for reviewer).
            _effectivePerturbAmplitude0 = _params.PerturbAmplitude0 * difficultyMult;
            _effectivePerturbRamp = _params.PerturbRamp * difficultyMult;

            _noise = new SeededValueNoise1D(_rng, _params.DurationSeconds, NoisePointsPerSecond);

            for (int i = 0; i < SeatCount; i++)
            {
                _theta[i] = (_forcedTheta != null && i < _forcedTheta.Length) ? _forcedTheta[i] : 0f;
                _thetaVelocity[i] = (_forcedThetaVelocity != null && i < _forcedThetaVelocity.Length) ? _forcedThetaVelocity[i] : 0f;
                _fallen[i] = false;
                _fallenTick[i] = -1;
                _sumAbsTheta[i] = 0f;
                _sampleCount[i] = 0;
            }

            PublishRenderState();
        }

        /// <inheritdoc/>
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _renderState.FeedbackCount = 0;
            _elapsedSeconds += DtSeconds;

            // One shared perturbation sample per tick, applied identically to
            // every active seat -- see class doc's shared-curve ruling.
            float amplitude = _effectivePerturbAmplitude0 + _effectivePerturbRamp * _elapsedSeconds;
            float noiseSample = _noise.Sample(_elapsedSeconds);
            float perturbation = amplitude * noiseSample;

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_fallen[i]) continue;

                (float horizontalInput, _) = InputBridge.ToUnitVector(input.Players[i].Stick);

                float angularAccel =
                    _params.GravityFactor * MathF.Sin(_theta[i])
                    + perturbation
                    - _params.TorqueGain * horizontalInput
                    - _params.Damping * _thetaVelocity[i];

                _thetaVelocity[i] += angularAccel * DtSeconds;
                _theta[i] += _thetaVelocity[i] * DtSeconds;

                _sumAbsTheta[i] += MathF.Abs(_theta[i]);
                _sampleCount[i]++;

                if (MathF.Abs(_theta[i]) > _thetaMaxRadians)
                {
                    _fallen[i] = true;
                    _fallenTick[i] = _tick;
                    EmitFeedback(i, FeedbackLevel.High, CueFallen);
                }
            }

            PublishRenderState();

            bool anyActive = false;
            bool allFallen = true;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                anyActive = true;
                if (!_fallen[i]) { allFallen = false; break; }
            }

            _tick++;

            if (_tick >= _durationTicks || (anyActive && allFallen))
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
            float[] meanAbsTheta = new float[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                seats[w] = i;
                ticksSurvived[w] = _fallen[i] ? _fallenTick[i] : _durationTicks;
                meanAbsTheta[w] = _sampleCount[i] > 0 ? _sumAbsTheta[i] / _sampleCount[i] : 0f;
                w++;
            }

            // Insertion sort, best-first: ticksSurvived descending, and -- ONLY
            // among the full-duration-survivor group -- meanAbsTheta ascending. n <= 4.
            for (int i = 1; i < n; i++)
            {
                int seatKey = seats[i], ticksKey = ticksSurvived[i];
                float meanKey = meanAbsTheta[i];
                int j = i - 1;
                while (j >= 0 && IsBetter(ticksKey, meanKey, ticksSurvived[j], meanAbsTheta[j]))
                {
                    seats[j + 1] = seats[j];
                    ticksSurvived[j + 1] = ticksSurvived[j];
                    meanAbsTheta[j + 1] = meanAbsTheta[j];
                    j--;
                }
                seats[j + 1] = seatKey;
                ticksSurvived[j + 1] = ticksKey;
                meanAbsTheta[j + 1] = meanKey;
            }

            var ranks = new PlayerRank[n];
            for (int i = 0; i < n; i++)
            {
                int place;
                if (i == 0) place = 1;
                else if (IsExactTie(ticksSurvived[i], meanAbsTheta[i], ticksSurvived[i - 1], meanAbsTheta[i - 1])) place = ranks[i - 1].Place;
                else place = i + 1;

                ranks[i] = new PlayerRank(seats[i], place, ticksSurvived[i]);
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------

        /// <summary>Converts degrees to radians via the exact expression <see cref="MantenParams.ThetaMaxDegrees"/>/tests must reuse for bit-exact boundary comparisons.</summary>
        public static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);

        private bool IsBetter(int ticksA, float meanA, int ticksB, float meanB)
        {
            if (ticksA != ticksB) return ticksA > ticksB;
            // Same ticksSurvived: only the full-duration-survivor group gets a
            // secondary criterion (GDD scopes the mean-|theta| rule to "si el
            // tiempo agota"); a tie among earlier-fallen seats simply stays tied
            // (stable order preserved), no secondary criterion the GDD names.
            if (ticksA == _durationTicks) return meanA < meanB;
            return false;
        }

        private bool IsExactTie(int ticksA, float meanA, int ticksB, float meanB)
        {
            if (ticksA != ticksB) return false;
            if (ticksA == _durationTicks) return meanA == meanB; // GDD: "empate exacto" -- intentional exact float equality
            return true;
        }

        // ------------------------------------------------------------------
        // Test/debug accessors (public -- same convention as every other v2
        // mechanic, e.g. EsquivaMicrogame.IsEliminated/GetEliminationTick).
        // ------------------------------------------------------------------

        public bool HasFallen(PlayerSlot slot) => _fallen[(int)slot];
        public int GetFallenTick(PlayerSlot slot) => _fallenTick[(int)slot];
        public float GetTheta(PlayerSlot slot) => _theta[(int)slot];
        public float GetMeanAbsTheta(PlayerSlot slot) => _sampleCount[(int)slot] > 0 ? _sumAbsTheta[(int)slot] / _sampleCount[(int)slot] : 0f;

        /// <summary>Test/telemetry accessor: PerturbAmplitude0 after difficultyMult, as applied by the most recent <see cref="Initialize"/>.</summary>
        public float EffectivePerturbAmplitude0 => _effectivePerturbAmplitude0;

        /// <summary>Test/telemetry accessor: PerturbRamp after difficultyMult, as applied by the most recent <see cref="Initialize"/>.</summary>
        public float EffectivePerturbRamp => _effectivePerturbRamp;

        // ------------------------------------------------------------------

        private void EmitFeedback(int seatIndex, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seatIndex, level, cue);
            _renderState.FeedbackCount++;
        }

        /// <summary>
        /// [ASSUMED] presentation layout: MECH_01 is 1-DOF per seat (no 2D
        /// position to simulate), so each seat's avatar is placed at a fixed
        /// "pedestal" slot in a row across the arena purely for the presenter to
        /// have an X/Y to draw at -- the actual balance signal is
        /// <see cref="RenderEntity.Rotation"/> (theta in degrees), not position.
        /// </summary>
        private static float PedestalX(int seatIndex) => 0.2f + 0.2f * seatIndex;

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡MANTÉN!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = _fallen[i] ? _fallenTick[i] : _tick;
                _renderState.Hud.Meters[i] = _thetaMaxRadians > 0f
                    ? MathF.Min(1f, MathF.Abs(_theta[i]) / _thetaMaxRadians)
                    : 0f;

                if (!_roster.IsActive(AllSlots[i])) continue;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = PedestalX(i);
                _renderState.Entities[entityCount].Y = 0.5f;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = _theta[i] * (180f / MathF.PI);
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = _fallen[i] ? VariantFallen : VariantStanding;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
