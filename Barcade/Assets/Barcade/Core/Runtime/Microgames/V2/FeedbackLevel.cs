namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// "Juice" intensity tier for a <see cref="FeedbackEvent"/> (GDD §8.2 feedback
    /// hierarchy: Bajo/Medio/Alto). The Framework enforces the resource budget per
    /// tier (e.g. the &lt;=3/minute High-tier audit); Core only classifies events.
    /// </summary>
    public enum FeedbackLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }
}
