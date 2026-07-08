using Barcade.Core.Microgames.V2;

namespace Barcade.Presentation
{
    /// <summary>
    /// Owned, deep-copied snapshot of one tick of a Core <see cref="RenderState"/>
    /// (GDD §10.3 double buffer). <c>IMicrogame.GetRenderState()</c> returns ONE
    /// mutable instance the sim overwrites every tick (see <see cref="RenderState"/>'s
    /// class doc) — a presenter that wants to interpolate between "the two most
    /// recent" states must own two independent copies, or the "previous" one
    /// mutates out from under it the instant the sim advances. This is that owned
    /// copy; pure C# (arrays of plain structs), so it's regression-locked in the
    /// fast suite without Unity.
    ///
    /// <see cref="CopyFrom"/> reuses its own arrays across calls (growing only if
    /// the source capacity grew) — no per-tick heap allocation once capacity has
    /// stabilised (GDD §14).
    /// </summary>
    public sealed class RenderStateSnapshot
    {
        public int Tick { get; private set; }

        public RenderEntity[] Entities = System.Array.Empty<RenderEntity>();
        public int EntityCount { get; private set; }

        public string HudActiveVerb { get; private set; } = string.Empty;
        public readonly int[] HudScores = new int[4];
        public readonly float[] HudMeters = new float[4];

        public FeedbackEvent[] Feedback = System.Array.Empty<FeedbackEvent>();
        public int FeedbackCount { get; private set; }

        /// <summary>False until the first <see cref="CopyFrom"/> — lets callers tell "no tick observed yet" apart from "tick 0".</summary>
        public bool HasData { get; private set; }

        public void CopyFrom(RenderState source)
        {
            if (source == null) return;

            Tick = source.Tick;

            EnsureEntityCapacity(source.Entities.Length);
            EntityCount = source.EntityCount;
            for (int i = 0; i < EntityCount; i++)
                Entities[i] = source.Entities[i];

            if (source.Hud != null)
            {
                HudActiveVerb = source.Hud.ActiveVerb ?? string.Empty;
                for (int i = 0; i < 4; i++)
                {
                    HudScores[i] = source.Hud.Scores[i];
                    HudMeters[i] = source.Hud.Meters[i];
                }
            }

            EnsureFeedbackCapacity(source.Feedback.Length);
            FeedbackCount = source.FeedbackCount;
            for (int i = 0; i < FeedbackCount; i++)
                Feedback[i] = source.Feedback[i];

            HasData = true;
        }

        private void EnsureEntityCapacity(int capacity)
        {
            if (Entities.Length < capacity)
                Entities = new RenderEntity[capacity];
        }

        private void EnsureFeedbackCapacity(int capacity)
        {
            if (Feedback.Length < capacity)
                Feedback = new FeedbackEvent[capacity];
        }
    }
}
