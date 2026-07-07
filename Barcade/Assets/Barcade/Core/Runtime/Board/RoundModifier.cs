namespace Barcade.Core.Board
{
    /// <summary>GDD §5.5 — round modifiers drawn from an Evento square landing. <see cref="None"/> when no Evento square was landed on this round.</summary>
    public enum RoundModifier
    {
        None = 0,
        DobleVelocidad,
        MuerteSubita,
        ModoPinata,
        LluviaDeMonedas,
        Apagon,
    }
}
