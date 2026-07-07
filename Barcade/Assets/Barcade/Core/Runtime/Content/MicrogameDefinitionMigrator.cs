using System;
using System.Collections.Generic;

namespace Barcade.Core.Content
{
    /// <summary>
    /// Snapshot of a v1 <c>Barcade.Framework.MicrogameDefinition</c>
    /// ScriptableObject's serialized fields, expressed as an engine-free POCO.
    /// Core cannot reference Barcade.Framework (a Unity assembly) directly, so the
    /// Editor-side migrator tool (<c>Barcade.EditorTools.MicrogameDefinitionMigrationTool</c>)
    /// reads each v1 asset's six fields into this struct and calls
    /// <see cref="MicrogameDefinitionMigrator.MapV1ToV2"/> -- keeping the actual
    /// mapping logic unit-testable in the fast suite without Unity (AC4).
    /// </summary>
    public readonly struct LegacyMicrogameDefinitionFields
    {
        public readonly string Id;
        public readonly string VerbText;
        public readonly string HintText;
        public readonly float BaseDuration;
        public readonly int Difficulty;
        public readonly string PrefabAddress;

        public LegacyMicrogameDefinitionFields(
            string id, string verbText, string hintText,
            float baseDuration, int difficulty, string prefabAddress)
        {
            Id = id ?? string.Empty;
            VerbText = verbText ?? string.Empty;
            HintText = hintText ?? string.Empty;
            BaseDuration = baseDuration;
            Difficulty = difficulty;
            PrefabAddress = prefabAddress ?? string.Empty;
        }
    }

    /// <summary>
    /// Pure mapping from the v1 MicrogameDefinition field set to the GDD §11.1 v2
    /// schema (AC4, GDD T-106). No Unity dependency -- the Editor migrator tool is
    /// a thin wrapper around this function (load the six SO fields in, write JSON
    /// out via <see cref="MicrogameDefinitionJson"/>).
    ///
    /// Fields the v1 schema simply never had (params, difficultyScaling,
    /// stageProfile detail, payoutTable, dynamics, minPlayers) are filled with GDD
    /// §6.1/§10.4 defaults where the GDD gives one, or a documented [ASSUMED]
    /// engineering default otherwise -- there is no data to lose because none
    /// existed. Fields the v1 schema DID have are carried through losslessly:
    ///   id + difficulty -> v2 Id       (v1 id is shared across variants of the
    ///                                    same mechanic, not unique -- see below)
    ///   verbText         -> DisplayVerb
    ///   hintText         -> LegacyHintText          (GDD §11.1 has no hint-copy field)
    ///   difficulty       -> LegacyDifficultyTier    (GDD §11.1 has no designer-tier field)
    ///   baseDuration     -> Duration
    ///   prefabAddress    -> Assets["prefabAddress"] (only when non-empty)
    ///
    /// [ASSUMED] Mechanic code: only "esquiva" has a confirmed GDD MECH_XX identity
    /// (MECH_02, per Annex D.1). The other four legacy prototype ids (aporrea,
    /// apunta, timing, recolecta) predate the GDD and are NOT listed in Annex D.1
    /// as an existing implementation of any of MECH_01/04-09 -- GDD T-107 is where
    /// they get reconciled against the canonical mechanic set. Until then this
    /// migrator carries the legacy id forward unchanged as a provisional
    /// `mechanic` value rather than inventing a MECH_XX mapping the GDD does not
    /// state.
    /// </summary>
    public static class MicrogameDefinitionMigrator
    {
        private const string EsquivaLegacyId = "esquiva";
        private const string EsquivaMechanicCode = "MECH_02";

        /// <summary>GDD §6.1 default competitive payout: 1st=6, 2nd=4, 3rd=2, 4th=1.</summary>
        private static readonly int[] DefaultCompetitivePayout = { 6, 4, 2, 1 };

        public static MicrogameDefinitionV2 MapV1ToV2(LegacyMicrogameDefinitionFields v1)
        {
            var v2 = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = $"mg_{v1.Id}_d{v1.Difficulty}",
                Mechanic = v1.Id == EsquivaLegacyId ? EsquivaMechanicCode : v1.Id,
                DisplayVerb = v1.VerbText,
                Dynamics = MicrogameDynamics.Competitive,
                Duration = v1.BaseDuration,
                DifficultyScaling = Array.Empty<string>(),
                Params = new Dictionary<string, object>(StringComparer.Ordinal),
                PayoutTable = (int[])DefaultCompetitivePayout.Clone(),
                Assets = new Dictionary<string, string>(StringComparer.Ordinal),
                StageProfile = new StageProfile(CameraFor(v1.Id), string.Empty, string.Empty),
                // [ASSUMED] The v1 schema has no minPlayers; 2 is the migration
                // default (orchestrator decision under the GDD-canonical
                // directive): GDD §11.1's own example uses minPlayers: 2 for a
                // competitive definition, and §1.3 makes 2-3-player sessions a
                // hard constraint -- a higher default would lock every migrated
                // microgame out of them. Design may recalibrate per-definition.
                MinPlayers = 2,
                Tags = Array.Empty<string>(),
                LegacyHintText = v1.HintText,
                LegacyDifficultyTier = v1.Difficulty,
            };

            if (!string.IsNullOrEmpty(v1.PrefabAddress))
                v2.Assets["prefabAddress"] = v1.PrefabAddress;

            return v2;
        }

        /// <summary>
        /// GDD §10.4 names the camera for the mechanics it discusses (ortográfica
        /// cenital for ¡ESQUIVA!, fija frontal for ¡APUNTA!/¡REACCIONA!);
        /// [ASSUMED] "frontFixed" default for the legacy prototypes it doesn't.
        /// </summary>
        private static string CameraFor(string legacyId) =>
            legacyId == EsquivaLegacyId ? "topDownOrtho" : "frontFixed";
    }
}
