namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The shape of a <see cref="MicrogameResult"/> (GDD Annex D.2). Competitive
    /// mechanics (like ¡REACCIONA!) always produce <see cref="Ranked"/>; cooperative
    /// mechanics produce one of the CoopSuccess/CoopFail outcomes instead.
    /// </summary>
    public enum ResultKind
    {
        Ranked = 0,
        CoopSuccess = 1,
        CoopFail = 2,
    }
}
