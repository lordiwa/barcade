using System;
using System.Collections.Generic;

namespace Barcade.Core.Content
{
    /// <summary>Session dynamics for a microgame (GDD §11.1: "competitive | asym1v3 | coop").</summary>
    public enum MicrogameDynamics
    {
        Competitive,
        Asym1v3,
        Coop,
    }

    /// <summary>
    /// Presentation projection declared by a definition (GDD §10.4 / §11.1) --
    /// consumed by the Framework's StagePresenter, ignored by Core.
    /// </summary>
    public sealed class StageProfile
    {
        public string Camera;
        public string Environment;
        public string EntityPrefabSet;

        public StageProfile() { }

        public StageProfile(string camera, string environment, string entityPrefabSet)
        {
            Camera = camera;
            Environment = environment;
            EntityPrefabSet = entityPrefabSet;
        }
    }

    /// <summary>
    /// GDD §11.1 v2 microgame definition schema -- the engine-free data model,
    /// independent of the Unity ScriptableObject that authors it
    /// (<c>Barcade.Framework.MicrogameDefinition</c>, which stays v1 for AC5
    /// backward compatibility) and independent of its JSON wire form
    /// (<see cref="MicrogameDefinitionJson"/>).
    ///
    /// Carries every field GDD §11.1 names (schemaVersion, id, mechanic,
    /// displayVerb, dynamics, duration, difficultyScaling, params, payoutTable,
    /// assets, stageProfile, minPlayers, tags), plus two additive fields
    /// (<see cref="LegacyHintText"/>, <see cref="LegacyDifficultyTier"/>) that are
    /// NOT part of the GDD schema -- they exist purely so the v1-to-v2 migration
    /// never silently drops v1 designer data the GDD schema has no slot for. See
    /// <see cref="MicrogameDefinitionMigrator"/>.
    ///
    /// Plain C#, no UnityEngine -- safe for the dotnet fast-test runner and for
    /// deserializing content downloaded to the cabinet (GDD §12).
    /// </summary>
    public sealed class MicrogameDefinitionV2
    {
        public int SchemaVersion = 2;
        public string Id;
        public string Mechanic;
        public string DisplayVerb;
        public MicrogameDynamics Dynamics;
        public float Duration;
        public string[] DifficultyScaling = Array.Empty<string>();
        public Dictionary<string, object> Params = new Dictionary<string, object>(StringComparer.Ordinal);
        public int[] PayoutTable = Array.Empty<int>();
        public Dictionary<string, string> Assets = new Dictionary<string, string>(StringComparer.Ordinal);
        public StageProfile StageProfile = new StageProfile();
        public int MinPlayers;
        public string[] Tags = Array.Empty<string>();

        /// <summary>
        /// [ASSUMED] Not part of GDD §11.1 -- the v1 SO's short one-line
        /// instruction copy, preserved so migration never discards it.
        /// </summary>
        public string LegacyHintText = string.Empty;

        /// <summary>
        /// [ASSUMED] Not part of GDD §11.1 -- the v1 SO's 1-3 designer difficulty
        /// tier (a pool-filter classification), distinct from the session-wide
        /// difficultyScaling ramp. Null when not migrated from a v1 asset.
        /// </summary>
        public int? LegacyDifficultyTier;
    }
}
