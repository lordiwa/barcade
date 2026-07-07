using System;
using Barcade.Core;
using Barcade.Core.Microgames.V2;

namespace Barcade.SlowTests
{
    /// <summary>
    /// MANTÉN (MECH_01) optimal-play oracle — a faithful extraction of the private
    /// <c>CorrectionOracle</c> nested in
    /// <c>Barcade/Assets/Tests/EditMode/MantenMicrogameV2Tests.cs</c>
    /// (test <c>PerfectCorrectionOracle_EveryPendulumSurvivesFullDuration_AcrossTestSeeds</c>).
    /// Copied verbatim (constants and logic) so the slow sweep proves the GDD
    /// "corrección perfecta -> 100% survive" property with the SAME controller the
    /// fast suite validated on 20 seeds. The slow sweep's seeds 0..19 overlap the
    /// fast test, giving a built-in parity check — keep the two in sync.
    ///
    /// "Corrección perfecta simulada" (GDD AC2): a real reactive controller, not a
    /// hardcoded survivor. Reads only the pendulum's current lean, published via
    /// <see cref="RenderEntity.Rotation"/> (degrees), plus an angular-velocity
    /// ESTIMATE it derives itself from the previous tick's reading (finite
    /// difference over the fixed 1/60 s tick). Full-authority bang-bang: pushes E
    /// or W at maximum whenever the PREDICTED lean crosses a tiny deadband around
    /// 0, using the SAME sign as the predicted lean (the sim's formula subtracts
    /// <c>torqueGain * input</c>, so countering a disturbance of sign D needs input
    /// of the same sign as D). Stateful (remembers the previous tick's theta per
    /// seat) but allocates nothing per Decide() call.
    /// </summary>
    internal sealed class MantenCorrectionOracle
    {
        private const float Kd = 0.15f;
        private const float Deadband = 0.001f;
        private const float DtSeconds = 1f / 60f;

        private readonly float[] _prevTheta = new float[4];
        private readonly bool[] _hasPrev = new bool[4];

        public Direction8 Decide(RenderState rs, int seat)
        {
            float theta = 0f;
            bool found = false;
            for (int i = 0; i < rs.EntityCount; i++)
            {
                if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == seat)
                {
                    theta = rs.Entities[i].Rotation * (MathF.PI / 180f);
                    found = true;
                    break;
                }
            }
            if (!found) return Direction8.None;

            float velocityEstimate = _hasPrev[seat] ? (theta - _prevTheta[seat]) / DtSeconds : 0f;
            _prevTheta[seat] = theta;
            _hasPrev[seat] = true;

            float errorSignal = theta + Kd * velocityEstimate;
            if (errorSignal > Deadband) return Direction8.E;
            if (errorSignal < -Deadband) return Direction8.W;
            return Direction8.None;
        }
    }
}
