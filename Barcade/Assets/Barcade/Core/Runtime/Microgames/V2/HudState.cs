namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// HUD data published each tick (GDD Annex D.2: "scores, verbo activo,
    /// medidores"). Intentionally minimal for T-103 — only the fields ¡REACCIONA!
    /// actually populates exist; future mechanics add meters/etc. additively.
    /// </summary>
    public sealed class HudState
    {
        /// <summary>The imperative verb shown for the active mechanic (e.g. "¡REACCIONA!").</summary>
        public string ActiveVerb;

        /// <summary>Per-seat running score/metric, length 4, indexed like <c>(int)PlayerSlot</c>.</summary>
        public readonly int[] Scores = new int[4];
    }
}
