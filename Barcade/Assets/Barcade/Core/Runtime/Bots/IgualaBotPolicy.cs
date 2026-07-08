using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_09 bot policy. Reads the currently-active zone from the
    /// published <see cref="RenderState"/> (the <see cref="EntityKind.Target"/>
    /// entity with <see cref="IgualaMicrogame.VariantActiveZone"/>) and its
    /// <see cref="RenderEntity.OwnerSeat"/> — never a hidden mechanic reference
    /// (Annex D.3: "el bot no hace trampa").
    ///
    /// <para>
    /// <b>Ideal decision.</b> If THIS seat owns the active zone: aim the stick
    /// at that zone and tap the button (a correct confirm, advancing the
    /// relay). Otherwise: aim at this seat's OWN zone (a plausible "ready"
    /// stance — matches a human resting their stick over their own color) and
    /// do NOT press (avoiding a foul).
    /// </para>
    ///
    /// <para>
    /// <b>Humanization.</b> <see cref="BotSkill.ErrorRate"/> flips the decision
    /// in the direction that is actually POSSIBLE for each role: an owner's
    /// rolled error withholds the press (a missed reaction — costs a retry,
    /// never a foul); a non-owner's rolled error presses anyway (a realistic
    /// impersonation foul, exercising <see cref="IgualaMicrogame"/>'s
    /// wrong-owner path). <see cref="BotSkill.ReactionDelayTicksMean"/> delays
    /// the (possibly-flipped) decision through a <see cref="ReactionDelayBuffer"/>.
    /// <see cref="BotSkill.MashHzMean"/> is unused (no mash channel here).
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class IgualaBotPolicy : IBotPolicy
    {
        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();
        private int? _sampledDelayTicks;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            (Direction8 idealStick, bool idealButton) = DecideIdeal(view);

            bool amOwner = TryFindOwnedActiveZone(view, out _);
            if (BotSkillSampler.RollError(skill, rng))
                idealButton = amOwner ? false : true;

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(idealStick, idealButton), _sampledDelayTicks.Value);
        }

        private (Direction8, bool) DecideIdeal(in BotView view)
        {
            if (TryFindOwnedActiveZone(view, out CardinalDir activeZone))
                return (ToDirection8(activeZone), true);

            // Not my turn -- rest on my own zone, don't press.
            CardinalDir myZone = IgualaMicrogame.ZoneForColor(view.Seat);
            return (ToDirection8(myZone), false);
        }

        /// <summary>True if the currently-active zone (if any) is owned by THIS seat; outputs that zone.</summary>
        private static bool TryFindOwnedActiveZone(in BotView view, out CardinalDir zone)
        {
            RenderState rs = view.RenderState;
            for (int i = 0; i < rs.EntityCount; i++)
            {
                RenderEntity e = rs.Entities[i];
                if (e.Kind != EntityKind.Target || e.VisualVariant != IgualaMicrogame.VariantActiveZone) continue;
                if (e.OwnerSeat != view.Seat) { zone = default; return false; }
                zone = IgualaMicrogame.ZoneForColor(e.OwnerSeat);
                return true;
            }
            zone = default;
            return false;
        }

        private static Direction8 ToDirection8(CardinalDir dir)
        {
            switch (dir)
            {
                case CardinalDir.Up: return Direction8.N;
                case CardinalDir.Right: return Direction8.E;
                case CardinalDir.Down: return Direction8.S;
                case CardinalDir.Left: return Direction8.W;
                default: return Direction8.None;
            }
        }
    }
}
