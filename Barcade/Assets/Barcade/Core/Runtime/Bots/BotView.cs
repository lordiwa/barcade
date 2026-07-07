using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD Annex D.3: "BotView es la proyección del estado que un humano vería
    /// (nunca estado oculto: el bot no hace trampa)." Literally just a mechanic's
    /// own public <see cref="IMicrogame.GetRenderState"/> output (which already
    /// embeds its <see cref="HudState"/>) plus which seat this bot occupies.
    ///
    /// <para>
    /// <b>AC1 compile-enforcement — how it works.</b> This struct's ONLY
    /// constructor takes a <see cref="Microgames.V2.RenderState"/> and a seat index.
    /// <see cref="Microgames.V2.RenderState"/>/<see cref="HudState"/> are themselves
    /// already the mechanic's PUBLIC per-tick presentation output — the exact
    /// same object the Framework's presenter (and, on a real cabinet, the human
    /// player's eyes) consume (see <see cref="Microgames.V2.RenderState"/>'s own class
    /// doc). There is no constructor, factory, or field anywhere on this type
    /// that accepts a concrete mechanic instance (<c>EsquivaMicrogame</c>,
    /// <c>ApuntaMicrogame</c>, …) or any of their PRIVATE fields — so a caller
    /// physically cannot compile a <see cref="BotView"/> that carries hidden sim
    /// state; the guarantee is structural, not a discipline every
    /// <see cref="IBotPolicy"/> author has to remember. Each mechanic's own
    /// <see cref="IBotPolicy"/> implementation interprets <see cref="RenderState"/>'s
    /// generic <c>Entities</c>/<c>Hud</c> fields according to that mechanic's own
    /// documented publish format (exactly how <c>EsquivaMicrogameV2Tests</c>'s
    /// pre-existing escapability bot and <c>MantenMicrogameV2Tests</c>'s
    /// correction oracle already worked, before this ticket — see each
    /// <c>*BotPolicy</c> class doc).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] one shared BotView type, not one per mechanic.</b> GDD Annex
    /// D.3 names a single <c>IBotPolicy { PlayerInput Decide(BotSkill, in
    /// BotView, SeededRandom); }</c> — one interface, implemented identically in
    /// signature by every mechanic's policy. A C# interface method has exactly
    /// one parameter type, so <see cref="BotView"/> is necessarily ONE shared
    /// type, not five mechanic-specific ones; what varies per mechanic is the
    /// CONTENT of the <see cref="Microgames.V2.RenderState"/> it wraps (already
    /// mechanic-specific by construction) and how each policy reads it, not the
    /// wrapper type itself.
    /// </para>
    /// </summary>
    public readonly struct BotView
    {
        public readonly RenderState RenderState;

        /// <summary>Which seat (0..3, <c>(int)PlayerSlot</c>) this bot is deciding for.</summary>
        public readonly int Seat;

        public BotView(RenderState renderState, int seat)
        {
            RenderState = renderState;
            Seat = seat;
        }

        /// <summary>Convenience: the wrapped RenderState's own Hud (same object every tick — see RenderState's class doc).</summary>
        public HudState Hud => RenderState.Hud;

        /// <summary>
        /// Scans <see cref="RenderState"/>.Entities for THIS bot's own
        /// <see cref="EntityKind.PlayerAvatar"/> (every v2 mechanic publishes
        /// exactly one per active seat, matching <see cref="Seat"/> via
        /// <see cref="RenderEntity.OwnerSeat"/>). Returns false if not found
        /// (e.g. this seat is not <see cref="PlayerRoster.IsActive"/> this
        /// round).
        /// </summary>
        public bool TryFindMyAvatar(out RenderEntity entity)
        {
            for (int i = 0; i < RenderState.EntityCount; i++)
            {
                if (RenderState.Entities[i].Kind == EntityKind.PlayerAvatar && RenderState.Entities[i].OwnerSeat == Seat)
                {
                    entity = RenderState.Entities[i];
                    return true;
                }
            }
            entity = default;
            return false;
        }
    }
}
