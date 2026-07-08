namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// One station in a <see cref="CoopLevelData"/> level (GDD §7.1): a fixed
    /// arena location plus the already-existing mechanic params
    /// (<see cref="SujetaParams"/> or <see cref="IgualaParams"/>) that govern
    /// its interaction once the whole team arrives. Composable level DATA — no
    /// simulation logic lives here; <see cref="CoopPhaseMicrogame"/> owns the
    /// actual delegated sub-mechanic instance built from this data.
    ///
    /// <b>distanciasEstiradas (GDD "objetivo A y B en extremos") is realized
    /// ENTIRELY through <see cref="X"/>/<see cref="Y"/> data</b> — author two
    /// objectives at opposite arena extremes and the element exists; no
    /// separate boolean flag or simulation branch is needed for it (unlike
    /// cuelloBotella/interseccionForzada/sueloDinamico, which DO change how
    /// <see cref="CoopPhaseMicrogame"/> ticks and so are flags on
    /// <see cref="CoopLevelData"/>).
    /// </summary>
    public sealed class CoopObjective
    {
        public readonly CoopObjectiveKind Kind;
        public readonly float X;
        public readonly float Y;

        /// <summary>Meaningful only when <see cref="Kind"/> == <see cref="CoopObjectiveKind.Sujeta"/>.</summary>
        public readonly SujetaParams SujetaParams;

        /// <summary>Meaningful only when <see cref="Kind"/> == <see cref="CoopObjectiveKind.Iguala"/>.</summary>
        public readonly IgualaParams IgualaParams;

        private CoopObjective(CoopObjectiveKind kind, float x, float y, SujetaParams sujetaParams, IgualaParams igualaParams)
        {
            Kind = kind;
            X = x;
            Y = y;
            SujetaParams = sujetaParams;
            IgualaParams = igualaParams;
        }

        /// <summary>A hold-all-switches (MECH_08) station at <paramref name="x"/>,<paramref name="y"/>.</summary>
        public static CoopObjective Sujeta(float x, float y, SujetaParams parameters) =>
            new CoopObjective(CoopObjectiveKind.Sujeta, x, y, parameters, default);

        /// <summary>A color-relay (MECH_09) station at <paramref name="x"/>,<paramref name="y"/>.</summary>
        public static CoopObjective Iguala(float x, float y, IgualaParams parameters) =>
            new CoopObjective(CoopObjectiveKind.Iguala, x, y, default, parameters);
    }
}
