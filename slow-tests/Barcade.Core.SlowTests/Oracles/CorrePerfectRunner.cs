using System;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
using V2Corre = Barcade.Core.Microgames.V2.CorreMicrogame;

namespace Barcade.SlowTests
{
    /// <summary>
    /// CORRE (MECH_03) optimal-play oracle — a faithful extraction of the private
    /// <c>PerfectRunner</c> nested in
    /// <c>Barcade/Assets/Tests/EditMode/CorreMicrogameV2Tests.cs</c>
    /// (test <c>ConstantSixHzMash_WithPerfectJumps_FinishesUnstunned_AcrossSeeds</c>).
    /// Copied verbatim so the slow sweep proves the GDD "track is fair — every
    /// obstacle jumpable" property with the SAME jump bot the fast suite validated
    /// on 20 seeds. Overlapping seeds 0..19 give a built-in parity check — keep the
    /// two in sync.
    ///
    /// A real reactive jump bot (not a hardcoded path). Drives PUBLIC input only:
    /// a 6 Hz mash on the button, and a stick-up tap when the next obstacle on the
    /// shared track is close enough ahead that the jump's airtime will span it —
    /// reading only the production surface (GetDistance / GetVelocity / IsAirborne
    /// / IsStunned and the exposed obstacle track). Because a jump requested this
    /// tick counts as airborne for this tick's crossing, timing the tap a little
    /// before the obstacle guarantees the crossing happens airborne. One tap per
    /// obstacle: once airborne it emits neutral, so the held-up edge resets before
    /// the next obstacle.
    /// </summary>
    internal sealed class CorrePerfectRunner
    {
        public void Drive(V2Corre mg, FakeInputs inputs, PlayerSlot slot, int tick)
        {
            bool button = Mash.Button(tick, 6);
            Direction8 stick = Direction8.None;

            if (!mg.IsAirborne(slot) && !mg.IsStunned(slot))
            {
                float dist = mg.GetDistance(slot);
                float v = mg.GetVelocity(slot);
                float nextObs = float.MaxValue;
                for (int i = 0; i < mg.ObstacleCount; i++)
                {
                    float pos = mg.GetObstaclePosition(i);
                    if (pos > dist && pos < nextObs) nextObs = pos;
                }
                if (nextObs < float.MaxValue)
                {
                    float span = MathF.Max(v, 0.01f) * mg.JumpAirtimeSeconds;
                    if (nextObs - dist <= span * 0.6f) stick = Direction8.N;
                }
            }

            inputs.Set(slot, stick, button);
        }
    }
}
