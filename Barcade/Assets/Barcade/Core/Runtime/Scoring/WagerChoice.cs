namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The compulsory FINAL_WAGER stake fractions (GDD §6.2: "cada jugador
    /// apuesta obligatoriamente 25/50/75% de sus monedas"). Values are the
    /// percentage so the math reads literally.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public enum WagerChoice
    {
        Quarter = 25,
        Half = 50,
        ThreeQuarters = 75,
    }
}
