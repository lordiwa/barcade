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
    /// Logical-plane-to-world-plane mapping for a <see cref="StageProfile"/> (GDD
    /// §10.4/§11.1, DESIGN CONTRACT RATIFIED 2026-07-08, TASK-027): §11.1's prose
    /// named this mapping ("mapeo de plano lógico a plano del mundo") but omitted
    /// a schema field for it -- this is that field, added additively.
    ///
    /// The axis convention is FIXED IN CODE, not data: logical X -> world X,
    /// logical Y -> world Z, <see cref="RenderEntity.Height"/> -> world Y (up) --
    /// mirroring the existing Esquiva-3D demo's centered-XZ convention
    /// (<c>Barcade.Framework.DodgeGameBootstrap</c>). The actual projection math
    /// lives in the engine-free <c>Barcade.Presentation</c> module, never here --
    /// Core "no conoce metros ni ejes de Unity" (§10.4).
    /// </summary>
    public sealed class PlaneMapping
    {
        /// <summary>[ASSUMED] No GDD number is given for plane size; the ratified contract suggests "e.g. 10x10" world units.</summary>
        public const float DefaultWorldSize = 10f;

        /// <summary>World-unit size of the logical [0,1]^2 plane along world X (logical X).</summary>
        public float WorldSizeX = DefaultWorldSize;

        /// <summary>World-unit size of the logical [0,1]^2 plane along world Z (logical Y).</summary>
        public float WorldSizeZ = DefaultWorldSize;

        /// <summary>World-space position of the plane's centre (logical 0.5, 0.5, Height 0).</summary>
        public float WorldOriginX;
        public float WorldOriginY;
        public float WorldOriginZ;

        public PlaneMapping() { }

        public PlaneMapping(float worldSizeX, float worldSizeZ, float worldOriginX, float worldOriginY, float worldOriginZ)
        {
            WorldSizeX = worldSizeX;
            WorldSizeZ = worldSizeZ;
            WorldOriginX = worldOriginX;
            WorldOriginY = worldOriginY;
            WorldOriginZ = worldOriginZ;
        }
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

        /// <summary>Added TASK-027 (DESIGN CONTRACT RATIFIED 2026-07-08) -- see <see cref="PlaneMapping"/>.</summary>
        public PlaneMapping PlaneMapping = new PlaneMapping();

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
