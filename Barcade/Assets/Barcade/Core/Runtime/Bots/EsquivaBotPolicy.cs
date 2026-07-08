using System;
using System.Collections.Generic;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_02 — ¡ESQUIVA! bot policy. Reconciles the pre-existing
    /// escapability solver duplicated in
    /// <c>EsquivaMicrogameV2Tests</c>'s private <c>EscapeBot</c> and
    /// <c>Barcade.SlowTests.Oracles.EsquivaEscapeBot</c> (the fast/slow
    /// parity-checked copies TASK-063 already flagged as needing one canonical
    /// home) — the ideal-decision step below IS that same two-layer reactive
    /// algorithm (strategic wall-hysteresis + tactical one-ply lookahead),
    /// generalized into <see cref="IBotPolicy"/> and humanized per
    /// <see cref="BotSkill"/>. Both existing call sites now delegate to this
    /// class via <see cref="Bot.Optimo"/> (see each file's own updated doc) —
    /// no second, independently-drifting copy of the algorithm remains.
    ///
    /// <para>
    /// <b>Ideal decision (unchanged from the original solver).</b> Finds this
    /// seat's own avatar and every alive <see cref="EntityKind.Hazard"/> in
    /// <see cref="BotView.RenderState"/>. Near a wall with no imminent hazard,
    /// heads for the arena center (hysteresis: enters "wall mode" inside 0.2 of
    /// any edge, exits past 0.35, preventing boundary-hugging oscillation).
    /// Otherwise, picks whichever of the 8 <see cref="Direction8"/> values (or
    /// <see cref="Direction8.None"/>) MAXIMIZES the resulting minimum distance
    /// to any alive hazard, one probe-step ahead — a real reactive one-ply
    /// lookahead, not a hardcoded path. The full per-seed diagnosis history
    /// behind these exact constants originally lived on the ORIGINAL solver's
    /// own extended class doc, <c>Barcade.SlowTests.Oracles.EsquivaEscapeBot</c>
    /// — that file was DELETED once this class became the one canonical home
    /// (see this class's own opening paragraph); its content was reconciled
    /// and summarized into this doc rather than left to rot in a deleted file.
    /// </para>
    ///
    /// <para>
    /// <b>Humanization.</b> <see cref="BotSkill.ErrorRate"/>: a rolled error
    /// freezes the seat (<see cref="Direction8.None"/>) instead of taking the
    /// computed evasive move — panicking is at least as "suboptimal" as any
    /// single wrong direction and needs no extra candidate-ranking logic.
    /// <see cref="BotSkill.ReactionDelayTicksMean"/>: the (possibly-frozen)
    /// ideal direction is pushed through a <see cref="ReactionDelayBuffer"/> and
    /// returned stale by the sampled tick count (see that buffer's own doc for
    /// why the delay is applied to the decision, not the observation).
    /// <see cref="BotSkill.MashHzMean"/> is unused — Esquiva reads no mash/button
    /// signal.
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class EsquivaBotPolicy : IBotPolicy
    {
        private const float EnterWallMargin = 0.2f;
        private const float ExitWallMargin = 0.35f;
        private const float ImmediateDangerRadius = 0.12f;
        private const float ProbeStep = 0.05f;

        private static readonly Direction8[] AllChoices =
        {
            Direction8.None, Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };

        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();
        private bool _inWallMode;
        private int? _sampledDelayTicks;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            Direction8 ideal = DecideIdeal(view);

            if (BotSkillSampler.RollError(skill, rng))
                ideal = Direction8.None;

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(ideal, false), _sampledDelayTicks.Value);
        }

        private Direction8 DecideIdeal(in BotView view)
        {
            RenderState rs = view.RenderState;

            if (!view.TryFindMyAvatar(out RenderEntity avatar))
                return Direction8.None;

            float myX = avatar.X, myY = avatar.Y;

            var hazards = new List<(float x, float y)>();
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Hazard)
                    hazards.Add((rs.Entities[i].X, rs.Entities[i].Y));

            if (hazards.Count == 0) return Direction8.None;

            float nearestHazardDist = float.MaxValue;
            foreach ((float hx, float hy) in hazards)
            {
                float ddx = myX - hx, ddy = myY - hy;
                float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (d < nearestHazardDist) nearestHazardDist = d;
            }

            float distToNearestWall = MathF.Min(MathF.Min(myX, 1f - myX), MathF.Min(myY, 1f - myY));
            if (_inWallMode)
            {
                if (distToNearestWall > ExitWallMargin) _inWallMode = false;
            }
            else if (distToNearestWall < EnterWallMargin)
            {
                _inWallMode = true;
            }

            bool imminentDanger = nearestHazardDist < ImmediateDangerRadius;
            if (_inWallMode && !imminentDanger)
                return TowardCenter(myX, myY);

            return BestLookaheadMove(myX, myY, hazards);
        }

        private static Direction8 TowardCenter(float myX, float myY)
        {
            Direction8 best = Direction8.None;
            float bestDist = float.MaxValue;
            foreach (Direction8 candidate in AllChoices)
            {
                (float dx, float dy) = InputBridge.ToUnitVector(candidate);
                float nx = Clamp01(myX + dx * ProbeStep);
                float ny = Clamp01(myY + dy * ProbeStep);
                float d = MathF.Sqrt((nx - 0.5f) * (nx - 0.5f) + (ny - 0.5f) * (ny - 0.5f));
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best;
        }

        private static Direction8 BestLookaheadMove(float myX, float myY, List<(float x, float y)> hazards)
        {
            Direction8 best = Direction8.None;
            float bestMinDist = float.MinValue;

            foreach (Direction8 candidate in AllChoices)
            {
                (float dx, float dy) = InputBridge.ToUnitVector(candidate);
                float nx = Clamp01(myX + dx * ProbeStep);
                float ny = Clamp01(myY + dy * ProbeStep);

                float minDist = float.MaxValue;
                foreach ((float hx, float hy) in hazards)
                {
                    float ddx = nx - hx, ddy = ny - hy;
                    float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < minDist) minDist = d;
                }

                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    best = candidate;
                }
            }

            return best;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
