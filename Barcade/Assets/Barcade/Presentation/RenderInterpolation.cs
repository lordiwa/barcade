namespace Barcade.Presentation
{
    /// <summary>
    /// Pure double-buffer interpolation math (GDD §10.3: "Unity interpola
    /// posiciones entre los dos últimos estados para render suave a la
    /// frecuencia que dé el hardware"). UnityEngine-free.
    /// </summary>
    public static class RenderInterpolation
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        /// <summary>Shortest-path angle lerp in degrees (handles the 359°→1° wraparound).</summary>
        public static float LerpAngleDegrees(float a, float b, float t)
        {
            float delta = Repeat(b - a + 180f, 360f) - 180f;
            return a + delta * Clamp01(t);
        }

        private static float Repeat(float value, float length) => value - Floor(value / length) * length;

        private static float Floor(float value)
        {
            int truncated = (int)value;
            return value < truncated ? truncated - 1 : truncated;
        }

        /// <summary>
        /// The tick-boundary interpolation factor (GDD §10.3): how far a render
        /// frame sits between the previous and current sim tick, given how much
        /// real time has elapsed since the current tick was published and the
        /// sim's fixed tick duration. Clamped to [0,1] — a render frame arriving
        /// late (hitch) simply holds at the current tick rather than extrapolating.
        /// </summary>
        public static float ComputeTickFactor(float timeSinceLastTickSeconds, float tickDurationSeconds)
        {
            if (!(tickDurationSeconds > 0f)) return 1f;
            return Clamp01(timeSinceLastTickSeconds / tickDurationSeconds);
        }

        /// <summary>
        /// TASK-027 DESIGN CONTRACT: resolves which factor drives an entity's
        /// ROTATION interpolation specifically. Position/height/scale always use
        /// the ordinary tick-boundary factor; rotation prefers the entity's own
        /// <c>RenderEntity.Progress01</c> when a mechanic populates it (e.g.
        /// ¡APUNTA!'s aim-angle interpolation progress within its 0.25s smoothing
        /// window, GDD §4), since that is a purpose-built, mechanic-timed
        /// smoothing signal finer than the tick-boundary factor. Progress01
        /// defaults to 0 for mechanics that don't use it (see its doc comment on
        /// <c>RenderEntity</c>), so absent any opt-in this simply returns the
        /// ordinary tick factor.
        /// </summary>
        public static float ResolveRotationFactor(float tickFactor, float progress01) =>
            progress01 > 0f ? Clamp01(progress01) : tickFactor;
    }
}
