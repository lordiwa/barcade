using System;
using System.Collections.Generic;
using Barcade.Core;
using Barcade.Core.Microgames.V2;

namespace Barcade.SlowTests
{
    /// <summary>
    /// ESQUIVA (MECH_02) optimal-play oracle — a faithful extraction of the
    /// private <c>EscapeBot</c> nested in
    /// <c>Barcade/Assets/Tests/EditMode/EsquivaMicrogameV2Tests.cs</c>
    /// (test <c>EscapabilityBot_SurvivesFullDuration_EveryPatternEverySeed</c>).
    /// The logic and constants are copied verbatim so the slow sweep asserts the
    /// GDD fairness property with the SAME solver the fast suite proved on its
    /// ~20-seed set. Because the slow sweep's seed range covers the fast test's
    /// seeds 0..19, a divergence between this copy and the private original would
    /// show up as a slow-sweep failure on one of those overlapping seeds — i.e.
    /// the overlap is a built-in parity check. Keep the two in sync.
    ///
    /// A REAL reactive solver (not a hardcoded path), two layers:
    /// (1) STRATEGIC wall-hysteresis: near a wall with no imminent hazard, head
    /// for the arena center (prevents getting trapped hugging a wall/corner);
    /// (2) TACTICAL one-ply lookahead: over the 8 Direction8 values plus None,
    /// pick whichever candidate maximizes the minimum resulting distance to any
    /// currently-alive hazard. See the original's class doc for the full rationale
    /// (Cross seed 16 / HomingSoft seed 4 / Sides seed 2 diagnoses).
    /// </summary>
    internal sealed class EsquivaEscapeBot
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

        private readonly bool[] _inWallMode = new bool[4];

        public Direction8 Decide(RenderState rs, int mySeat)
        {
            float myX = 0f, myY = 0f;
            bool foundSelf = false;
            for (int i = 0; i < rs.EntityCount; i++)
            {
                if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == mySeat)
                {
                    myX = rs.Entities[i].X;
                    myY = rs.Entities[i].Y;
                    foundSelf = true;
                    break;
                }
            }
            if (!foundSelf) return Direction8.None;

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
            if (_inWallMode[mySeat])
            {
                if (distToNearestWall > ExitWallMargin) _inWallMode[mySeat] = false;
            }
            else if (distToNearestWall < EnterWallMargin)
            {
                _inWallMode[mySeat] = true;
            }

            bool imminentDanger = nearestHazardDist < ImmediateDangerRadius;
            if (_inWallMode[mySeat] && !imminentDanger)
                return TowardCenter(myX, myY, hazards);

            return BestLookaheadMove(myX, myY, hazards);
        }

        private static Direction8 TowardCenter(float myX, float myY, List<(float x, float y)> hazards)
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
