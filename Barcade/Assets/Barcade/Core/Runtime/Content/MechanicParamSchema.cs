using System;
using System.Collections.Generic;

namespace Barcade.Core.Content
{
    /// <summary>Declares one named, numeric-range-bounded parameter for a mechanic
    /// (GDD §11.1: "el rango declarado por la mecánica").</summary>
    public readonly struct ParamRange
    {
        public readonly string Name;
        public readonly double Min;
        public readonly double Max;

        public ParamRange(string name, double min, double max)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name must be non-empty", nameof(name));
            if (max < min) throw new ArgumentException("max must be >= min", nameof(max));
            Name = name;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// The set of parameter declarations for one mechanic id: numeric ranges that
    /// <see cref="MicrogameDefinitionValidator"/> enforces, plus non-numeric
    /// "reserved" param names the mechanic is known to use (enum/bool-valued)
    /// that are not range-checked but must never be treated as unexpected.
    /// </summary>
    public sealed class MechanicParamSchema
    {
        public string MechanicId { get; }
        public IReadOnlyList<ParamRange> Ranges { get; }
        public IReadOnlyList<string> ReservedParamNames { get; }

        public MechanicParamSchema(string mechanicId, ParamRange[] ranges = null, string[] reservedParamNames = null)
        {
            if (string.IsNullOrEmpty(mechanicId)) throw new ArgumentException("mechanicId must be non-empty", nameof(mechanicId));
            MechanicId = mechanicId;
            Ranges = ranges ?? Array.Empty<ParamRange>();
            ReservedParamNames = reservedParamNames ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Registry of per-mechanic parameter declarations consulted by
    /// <see cref="MicrogameDefinitionValidator"/> (AC2). A mechanic with no
    /// registered schema is not range-checked (vacuously valid) -- this ticket is
    /// scoped to the definition/data layer, not to authoring a schema for every
    /// mechanic; MECH_04's schema demonstrates the mechanism using the exact GDD
    /// §11.1 worked example.
    ///
    /// [ASSUMED] Mech04Apunta's numeric bounds are engineering defaults, not
    /// GDD-specified numbers (GDD gives one representative value per param, e.g.
    /// chargeCycleSec ~1.2s, not a formal min/max) -- same pattern as
    /// <c>Barcade.Core.Microgames.V2.ReaccionaParams.ReactionTimeoutSeconds</c>.
    /// Flagged for design calibration, not load-bearing for any AC beyond "the
    /// mechanism rejects an out-of-range param."
    /// </summary>
    public static class MechanicParamSchemas
    {
        public static readonly MechanicParamSchema Mech04Apunta = new MechanicParamSchema(
            "MECH_04",
            ranges: new[]
            {
                new ParamRange("chargeCycleSec", 0.5, 3.0),
                new ParamRange("targetCount", 1.0, 6.0),
                new ParamRange("windAccel", 0.0, 0.2),
            });

        /// <summary>
        /// GDD §4 MECH_05 declares signalMode (visual/audio/both) and colorFilter
        /// (bool -- only the shown color reacts) as dato variants. Neither is
        /// implemented in ReaccionaMicrogame yet (flagged in the TASK-025 review,
        /// relayed mid-flight during this ticket) -- reserved here purely so a v2
        /// definition that sets them validates instead of being treated as
        /// unexpected. No enum/bool validation semantics are implemented for them;
        /// that is Core logic for a future ticket.
        /// </summary>
        public static readonly MechanicParamSchema Mech05Reacciona = new MechanicParamSchema(
            "MECH_05",
            reservedParamNames: new[] { "signalMode", "colorFilter" });

        private static readonly Dictionary<string, MechanicParamSchema> ByMechanicId =
            new Dictionary<string, MechanicParamSchema>(StringComparer.Ordinal)
            {
                { Mech04Apunta.MechanicId, Mech04Apunta },
                { Mech05Reacciona.MechanicId, Mech05Reacciona },
            };

        public static bool TryGet(string mechanicId, out MechanicParamSchema schema) =>
            ByMechanicId.TryGetValue(mechanicId ?? string.Empty, out schema);
    }
}
