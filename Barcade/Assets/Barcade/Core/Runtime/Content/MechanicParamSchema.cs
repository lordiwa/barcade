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

        /// <summary>
        /// GDD §4 MECH_02 (T-107 slice 1). Numeric bounds are engineering
        /// defaults (GDD names the param list but not formal min/max), same
        /// pattern as <see cref="Mech04Apunta"/>. <c>hazardPattern</c> and
        /// <c>jumpEnabled</c> are reserved (enum-string/bool-valued, not
        /// range-checked) so a definition that sets them validates.
        /// </summary>
        public static readonly MechanicParamSchema Mech02Esquiva = new MechanicParamSchema(
            "MECH_02",
            ranges: new[]
            {
                new ParamRange("spawnRatePerSec", 0.1, 5.0),
                new ParamRange("hazardSpeed", 0.0, 2.0),
            },
            reservedParamNames: new[] { "hazardPattern", "jumpEnabled" });

        /// <summary>
        /// GDD §4 MECH_01 (T-107 slice 3). Numeric bounds are engineering
        /// defaults (GDD names the param list but not formal min/max), same
        /// pattern as <see cref="Mech04Apunta"/>/<see cref="Mech02Esquiva"/>.
        /// <c>dashRecoveryEnabled</c> is reserved (bool-valued, not
        /// range-checked) so a definition that sets it validates.
        /// </summary>
        public static readonly MechanicParamSchema Mech01Manten = new MechanicParamSchema(
            "MECH_01",
            ranges: new[]
            {
                new ParamRange("gravityFactor", 0.5, 8.0),
                new ParamRange("perturbAmplitude0", 0.0, 5.0),
                new ParamRange("perturbRamp", 0.0, 2.0),
                new ParamRange("torqueGain", 1.0, 20.0),
                new ParamRange("thetaMax", 10.0, 60.0),
            },
            reservedParamNames: new[] { "dashRecoveryEnabled" });

        /// <summary>
        /// GDD §4 MECH_03 (T-107 slice 2). Numeric bounds are engineering
        /// defaults (GDD names the param list but not formal min/max), same
        /// pattern as <see cref="Mech04Apunta"/>/<see cref="Mech02Esquiva"/>/
        /// <see cref="Mech01Manten"/>. <c>raceToFinish</c> is reserved (bool-valued,
        /// not range-checked) so a definition that sets it validates -- it is a
        /// declared GDD variant this Core slice does not yet read, the same
        /// out-of-scope status as <c>jumpEnabled</c>/<c>dashRecoveryEnabled</c>.
        /// </summary>
        public static readonly MechanicParamSchema Mech03Corre = new MechanicParamSchema(
            "MECH_03",
            ranges: new[]
            {
                new ParamRange("vBase", 0.5, 10.0),
                new ParamRange("vGain", 0.0, 10.0),
                new ParamRange("obstacleDensity", 0.0, 2.0),
                new ParamRange("stunSeconds", 0.0, 3.0),
                new ParamRange("rubberBandPct", 0.0, 0.5),
            },
            reservedParamNames: new[] { "raceToFinish" });

        /// <summary>
        /// GDD §4 MECH_06 (T-111). Numeric bounds are engineering defaults (GDD
        /// names the param list but not formal min/max, same pattern as every
        /// other schema here) except <c>dashCooldown</c>, whose floor/ceiling
        /// bracket the GDD-literal 1.5s so a recalibration can adjust it without
        /// drifting far from the named value. <c>arenaLayout</c> and <c>mode</c>
        /// are reserved (enum-string-valued, not range-checked) so a definition
        /// that sets them validates.
        /// </summary>
        public static readonly MechanicParamSchema Mech06Persigue = new MechanicParamSchema(
            "MECH_06",
            ranges: new[]
            {
                new ParamRange("soloSpeedBonus", 0.0, 1.0),
                new ParamRange("dashCooldown", 0.5, 3.0),
                new ParamRange("dashDistance", 0.02, 0.5),
            },
            reservedParamNames: new[] { "arenaLayout", "mode" });

        /// <summary>
        /// GDD §4 MECH_07 (T-111). <c>telegraphSec</c>'s floor matches the P4
        /// guarantee already hard-enforced by <see cref="Microgames.V2.BombardeaParams"/>'s
        /// own constructor (0.5s) — declared here too so a definition with an
        /// out-of-range value is rejected at the data layer, before it ever
        /// reaches the mechanic's constructor.
        /// </summary>
        public static readonly MechanicParamSchema Mech07Bombardea = new MechanicParamSchema(
            "MECH_07",
            ranges: new[]
            {
                new ParamRange("fireCooldown", 0.2, 3.0),
                new ParamRange("telegraphSec", 0.5, 2.0),
                new ParamRange("blastRadius", 0.02, 0.5),
                new ParamRange("soloScorePerHit", 1.0, 10.0),
            });

        /// <summary>
        /// GDD §4 MECH_08 (T-112). <c>holdWindow</c>'s bracket brackets the
        /// GDD-literal 1.5s default; <c>windowsToWin</c>'s bracket allows small
        /// integer counts (GDD names the knob but not a default -- see
        /// <see cref="Microgames.V2.SujetaParams"/>'s own doc). <c>mode</c> is
        /// reserved (enum-string-valued, not range-checked).
        /// </summary>
        public static readonly MechanicParamSchema Mech08Sujeta = new MechanicParamSchema(
            "MECH_08",
            ranges: new[]
            {
                new ParamRange("holdWindow", 0.5, 4.0),
                new ParamRange("windowsToWin", 1.0, 5.0),
            },
            reservedParamNames: new[] { "mode" });

        /// <summary>
        /// GDD §4 MECH_09 (T-112). <c>reactWindow0</c>/<c>windowDecay</c> are
        /// bracketed around the GDD literals (0.9/0.05); the 0.45s floor itself
        /// is NOT a range here -- it is a fixed constant
        /// (<see cref="Microgames.V2.IgualaParams.ReactWindowFloorSeconds"/>),
        /// not a per-definition value. <c>sequenceLength</c>'s bracket allows a
        /// short 3-symbol round up to a long 12-symbol one. <c>mode</c> is
        /// reserved (enum-string-valued; Core implements colorRelay only today
        /// -- see <see cref="Microgames.V2.IgualaMicrogame"/>'s own doc).
        /// </summary>
        public static readonly MechanicParamSchema Mech09Iguala = new MechanicParamSchema(
            "MECH_09",
            ranges: new[]
            {
                new ParamRange("sequenceLength", 3.0, 12.0),
                new ParamRange("reactWindow0", 0.5, 1.5),
                new ParamRange("windowDecay", 0.0, 0.2),
            },
            reservedParamNames: new[] { "mode" });

        private static readonly Dictionary<string, MechanicParamSchema> ByMechanicId =
            new Dictionary<string, MechanicParamSchema>(StringComparer.Ordinal)
            {
                { Mech04Apunta.MechanicId, Mech04Apunta },
                { Mech05Reacciona.MechanicId, Mech05Reacciona },
                { Mech02Esquiva.MechanicId, Mech02Esquiva },
                { Mech01Manten.MechanicId, Mech01Manten },
                { Mech03Corre.MechanicId, Mech03Corre },
                { Mech06Persigue.MechanicId, Mech06Persigue },
                { Mech07Bombardea.MechanicId, Mech07Bombardea },
                { Mech08Sujeta.MechanicId, Mech08Sujeta },
                { Mech09Iguala.MechanicId, Mech09Iguala },
            };

        public static bool TryGet(string mechanicId, out MechanicParamSchema schema) =>
            ByMechanicId.TryGetValue(mechanicId ?? string.Empty, out schema);
    }
}
