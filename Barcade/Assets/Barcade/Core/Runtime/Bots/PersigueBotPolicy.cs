using System;
using System.Collections.Generic;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_06 bot policy. ONE class drives either role for its seat,
    /// branching only on published, human-visible <see cref="RenderState"/>
    /// data — never a hidden mechanic reference (Annex D.3: "el bot no hace
    /// trampa"):
    /// <list type="bullet">
    /// <item><description><b>Role</b> — this seat's own <see cref="EntityKind.PlayerAvatar"/>
    /// <see cref="RenderEntity.VisualVariant"/>, bit0 (see
    /// <see cref="PersigueMicrogame"/>'s own doc for the encoding).</description></item>
    /// <item><description><b>Mode</b> — <see cref="HudState.ActiveVerb"/>, the
    /// same giant verb a human player reads ("¡PERSIGUE!" = soloHunts,
    /// "¡ESCAPA!" = soloFlees).</description></item>
    /// </list>
    /// From (role, mode) this policy derives whether ITS seat is the chaser
    /// (closes distance onto the nearest opposing avatar, quantized toward it)
    /// or the evader (Esquiva-style one-ply lookahead: the candidate direction
    /// that maximizes the resulting minimum distance to any opposing avatar).
    /// Both roles attempt a dash whenever their own debounce-friendly duty
    /// cycle allows a fresh press edge (see <see cref="DashDutyCycleTicks"/>) —
    /// the mechanic itself silently ignores an attempt still on cooldown, so
    /// "try often" is a safe, simple policy; PersigueMicrogame's own cooldown
    /// gate is what actually enforces the exact 1.5s spacing (AC3).
    ///
    /// Humanization identical to every other <c>*BotPolicy</c>:
    /// <see cref="BotSkill.ErrorRate"/> freezes the WHOLE tick's decision
    /// (stick+dash) to neutral; <see cref="BotSkill.ReactionDelayTicksMean"/>
    /// delays the (possibly-frozen) decision through a
    /// <see cref="ReactionDelayBuffer"/>. <see cref="BotSkill.MashHzMean"/> is
    /// unused (no mash channel here).
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class PersigueBotPolicy : IBotPolicy
    {
        private const float ProbeStep = 0.05f;

        /// <summary>
        /// Ticks between fresh dash attempts: 2 ticks down + 2 ticks up satisfies
        /// <c>InputInterpreter.DebounceConfirmTicks</c> (2) for both the press and
        /// release edge, so a genuine <c>PressedThisTick</c> edge fires once per
        /// cycle — far more often than the 1.5s (90-tick) dash cooldown needs.
        /// </summary>
        private const int DashDutyCycleTicks = 4;

        private static readonly Direction8[] AllChoices =
        {
            Direction8.None, Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };

        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();
        private int? _sampledDelayTicks;
        private int _localTick;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            (Direction8 idealStick, bool idealDash) = DecideIdeal(view);
            _localTick++;

            if (BotSkillSampler.RollError(skill, rng))
            {
                idealStick = Direction8.None;
                idealDash = false;
            }

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(idealStick, idealDash), _sampledDelayTicks.Value);
        }

        private (Direction8, bool) DecideIdeal(in BotView view)
        {
            RenderState rs = view.RenderState;

            if (!view.TryFindMyAvatar(out RenderEntity mine))
                return (Direction8.None, false);

            bool isSolo = (mine.VisualVariant & 1) != 0;
            bool modeIsSoloHunts = rs.Hud.ActiveVerb == "¡PERSIGUE!";
            bool iAmChaser = isSolo == modeIsSoloHunts;

            var others = new List<(float x, float y)>();
            for (int i = 0; i < rs.EntityCount; i++)
            {
                RenderEntity e = rs.Entities[i];
                if (e.Kind != EntityKind.PlayerAvatar || e.OwnerSeat == view.Seat) continue;
                bool otherIsSolo = (e.VisualVariant & 1) != 0;
                if (otherIsSolo == isSolo) continue; // only the opposing role is relevant to either chase or flee
                others.Add((e.X, e.Y));
            }

            if (others.Count == 0) return (Direction8.None, false);

            bool dashDue = (_localTick % DashDutyCycleTicks) < 2;

            if (iAmChaser)
            {
                (float tx, float ty) = NearestOf(mine.X, mine.Y, others);
                Direction8 toward = QuantizeToward(mine.X, mine.Y, tx, ty);
                return (toward, dashDue);
            }
            else
            {
                Direction8 flee = BestLookaheadMove(mine.X, mine.Y, others);
                float nearestDist = NearestDistance(mine.X, mine.Y, others);
                bool imminentDanger = nearestDist < 0.15f;
                return (flee, dashDue && imminentDanger);
            }
        }

        private static (float, float) NearestOf(float fromX, float fromY, List<(float x, float y)> points)
        {
            float bestDist = float.MaxValue;
            float bestX = points[0].x, bestY = points[0].y;
            foreach ((float x, float y) in points)
            {
                float dx = fromX - x, dy = fromY - y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < bestDist) { bestDist = d; bestX = x; bestY = y; }
            }
            return (bestX, bestY);
        }

        private static float NearestDistance(float fromX, float fromY, List<(float x, float y)> points)
        {
            float bestDist = float.MaxValue;
            foreach ((float x, float y) in points)
            {
                float dx = fromX - x, dy = fromY - y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < bestDist) bestDist = d;
            }
            return bestDist;
        }

        private static Direction8 QuantizeToward(float fromX, float fromY, float toX, float toY)
        {
            float dx = toX - fromX, dy = toY - fromY;
            if (MathF.Abs(dx) < 1e-6f && MathF.Abs(dy) < 1e-6f) return Direction8.None;

            float deg = MathF.Atan2(dy, dx) * (180f / MathF.PI);
            if (deg < 0f) deg += 360f;
            int octant = (int)MathF.Round(deg / 45f) % 8;

            switch (octant)
            {
                case 0: return Direction8.E;
                case 1: return Direction8.NE;
                case 2: return Direction8.N;
                case 3: return Direction8.NW;
                case 4: return Direction8.W;
                case 5: return Direction8.SW;
                case 6: return Direction8.S;
                default: return Direction8.SE;
            }
        }

        private static Direction8 BestLookaheadMove(float myX, float myY, List<(float x, float y)> threats)
        {
            Direction8 best = Direction8.None;
            float bestMinDist = float.MinValue;

            foreach (Direction8 candidate in AllChoices)
            {
                (float dx, float dy) = InputBridge.ToUnitVector(candidate);
                float nx = Clamp01(myX + dx * ProbeStep);
                float ny = Clamp01(myY + dy * ProbeStep);

                float minDist = float.MaxValue;
                foreach ((float tx, float ty) in threats)
                {
                    float ddx = nx - tx, ddy = ny - ty;
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
