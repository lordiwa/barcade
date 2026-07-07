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

        /// <summary>
        /// Per-seat live meter value in [0,1], length 4, indexed like <c>(int)PlayerSlot</c>
        /// ("medidores" — GDD Annex D.2). Mechanic-defined meaning: e.g. ¡APUNTA!'s
        /// oscillating charge power while a seat is holding, 0 while not charging.
        /// Added additively (T-104) — REACCIONA never populates this.
        /// </summary>
        public readonly float[] Meters = new float[4];
    }
}
