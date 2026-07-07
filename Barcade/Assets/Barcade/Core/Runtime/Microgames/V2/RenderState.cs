namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Per-tick presentation data published by a v2 <see cref="IMicrogame"/> (GDD
    /// Annex D.2 / §10.3). A single mutable instance is created once and its
    /// fields are overwritten every tick ("doble-buffer... cero alloc") — the
    /// actual thread-safe double-buffered publish/consume across the sim/render
    /// boundary is a Framework (Unity-side) concern, out of scope for Core.
    /// Core never puts UnityEngine references in here — only plain data.
    /// </summary>
    public sealed class RenderState
    {
        public int Tick;

        /// <summary>Fixed-capacity pool; only indices [0, EntityCount) are meaningful this tick.</summary>
        public readonly RenderEntity[] Entities;
        public int EntityCount;

        public readonly HudState Hud = new HudState();

        /// <summary>Fixed-capacity pool; only indices [0, FeedbackCount) are meaningful this tick.</summary>
        public readonly FeedbackEvent[] Feedback;
        public int FeedbackCount;

        public RenderState(int entityCapacity, int feedbackCapacity)
        {
            Entities = new RenderEntity[entityCapacity];
            Feedback = new FeedbackEvent[feedbackCapacity];
        }
    }
}
