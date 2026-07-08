using System;
using System.Collections.Generic;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_07 bot policy. ONE class drives either role for its seat,
    /// branching on this seat's own PlayerAvatar <see cref="RenderEntity.VisualVariant"/>
    /// (<see cref="BombardeaMicrogame.VariantSolo"/>/<see cref="BombardeaMicrogame.VariantTrio"/>)
    /// — the same "read only published, human-visible data" discipline as
    /// <see cref="PersigueBotPolicy"/> (Annex D.3: "el bot no hace trampa").
    ///
    /// <para>
    /// <b>Solo:</b> moves its reticle toward the nearest trio avatar; fires
    /// (subject to its own debounce-friendly duty cycle, see
    /// <see cref="FireDutyCycleTicks"/> — <see cref="BombardeaMicrogame"/>'s own
    /// cooldown/pending-shot gates are what actually enforce the 0.9s cadence
    /// and the queue-of-1 rule, AC4) once the reticle is within
    /// <see cref="FireProximityThreshold"/> of that target — a bot-side
    /// heuristic approximation of the mechanic's own (unpublished) BlastRadius,
    /// not a read of it.
    /// </para>
    ///
    /// <para>
    /// <b>Trio:</b> "atento" dodge, GDD-literal: reads
    /// <see cref="EntityKind.Projectile"/> entities in <see cref="RenderState"/>
    /// (the telegraphed shot IS visible there the instant it fires — see
    /// <see cref="BombardeaMicrogame"/>'s own doc) and, while one is active,
    /// flees it via the same Esquiva-style one-ply lookahead every other flee
    /// policy uses. With no shot telegraphed, holds position (a human player has
    /// nothing to react to yet either).
    /// </para>
    ///
    /// Humanization identical to every other <c>*BotPolicy</c>: error freezes
    /// the whole decision; reaction delay pushes it through a
    /// <see cref="ReactionDelayBuffer"/>. MashHz is unused.
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class BombardeaBotPolicy : IBotPolicy
    {
        private const float ProbeStep = 0.05f;
        private const float FireProximityThreshold = 0.05f;

        /// <summary>2 ticks down + 2 ticks up satisfies <c>InputInterpreter.DebounceConfirmTicks</c> (2) for both edges.</summary>
        private const int FireDutyCycleTicks = 4;

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
            (Direction8 idealStick, bool idealButton) = DecideIdeal(view);
            _localTick++;

            if (BotSkillSampler.RollError(skill, rng))
            {
                idealStick = Direction8.None;
                idealButton = false;
            }

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(idealStick, idealButton), _sampledDelayTicks.Value);
        }

        private (Direction8, bool) DecideIdeal(in BotView view)
        {
            RenderState rs = view.RenderState;

            if (!view.TryFindMyAvatar(out RenderEntity mine))
                return (Direction8.None, false);

            bool isSolo = mine.VisualVariant == BombardeaMicrogame.VariantSolo;

            if (isSolo)
                return DecideSolo(rs, mine);

            return DecideTrio(rs, mine);
        }

        private (Direction8, bool) DecideSolo(RenderState rs, in RenderEntity mine)
        {
            float nearestDist = float.MaxValue;
            float nearestX = mine.X, nearestY = mine.Y;
            bool anyTrio = false;

            for (int i = 0; i < rs.EntityCount; i++)
            {
                RenderEntity e = rs.Entities[i];
                if (e.Kind != EntityKind.PlayerAvatar || e.VisualVariant != BombardeaMicrogame.VariantTrio) continue;

                anyTrio = true;
                float dx = mine.X - e.X, dy = mine.Y - e.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < nearestDist) { nearestDist = d; nearestX = e.X; nearestY = e.Y; }
            }

            if (!anyTrio) return (Direction8.None, false);

            bool fireDue = (_localTick % FireDutyCycleTicks) < 2;
            bool inRange = nearestDist <= FireProximityThreshold;

            Direction8 toward = QuantizeToward(mine.X, mine.Y, nearestX, nearestY);
            return (inRange ? Direction8.None : toward, fireDue && inRange);
        }

        private (Direction8, bool) DecideTrio(RenderState rs, in RenderEntity mine)
        {
            var shots = new List<(float x, float y)>();
            for (int i = 0; i < rs.EntityCount; i++)
            {
                RenderEntity e = rs.Entities[i];
                if (e.Kind == EntityKind.Projectile) shots.Add((e.X, e.Y));
            }

            if (shots.Count == 0) return (Direction8.None, false);

            Direction8 flee = BestLookaheadMove(mine.X, mine.Y, shots);
            return (flee, false);
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
