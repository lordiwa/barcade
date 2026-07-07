using System.IO;
using UnityEditor;
using UnityEngine;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that creates all <see cref="MicrogameDefinition"/> assets and a
    /// <see cref="MicrogamePool"/> asset under
    /// <c>Assets/Barcade/Content/Microgames/</c> and
    /// <c>Assets/Barcade/Content/DefaultMicrogamePool.asset</c>.
    ///
    /// Safe to re-run: existing assets are overwritten deterministically.
    ///
    /// Invoke headless via Unity batchmode:
    /// <code>
    ///   -executeMethod Barcade.EditorTools.MicrogameContentGenerator.GenerateAll
    /// </code>
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class MicrogameContentGenerator
    {
        private const string ContentFolder   = "Assets/Barcade/Content";
        private const string MicrogameFolder = "Assets/Barcade/Content/Microgames";
        private const string PoolAssetPath   = "Assets/Barcade/Content/DefaultMicrogamePool.asset";

        // ── Definition specs ──────────────────────────────────────────────────────

        private struct DefSpec
        {
            public string Id;
            public string VerbText;
            public string HintText;      // short one-line instruction in Spanish
            public float  BaseDuration;
            public int    Difficulty;   // 1=easy, 2=normal, 3=hard
        }

        /// <summary>
        /// TASK-061 (T-107 slice 4, Unit A): aporrea/timing/apunta-v1 retired --
        /// none of the three maps to a canonical GDD MECH_01-09 mechanic (Phase A
        /// disposition, ratified by the orchestrator; grounded in GDD Annex D.1's
        /// own repo-integration table, which lists no row for any of them). Their
        /// entries are removed from this array along with the mechanics themselves
        /// (Barcade.Core.Runtime.Microgames.AporreaMicrogame/TimingMicrogame/
        /// ApuntaMicrogame[v1] -- NOT the V2/ folder ApuntaMicrogame, which is the
        /// canonical MECH_04 and is untouched). recolecta's 3 entries stay for now
        /// -- its disposition is a content decision routed to the human, not an
        /// engineering call, and is explicitly out of Unit A's scope. esquiva's 3
        /// entries are also untouched (MECH_02, already GDD-mapped, not part of
        /// this ticket at all).
        ///
        /// All 6 definitions across the 2 remaining mechanics, with meaningfully
        /// varied difficulty, duration and verbText. Each id matches a
        /// MicrogameRegistry key.
        ///
        /// TASK-045 (GDD-canonical rule): durations must stay within GDD §11.1's
        /// [<see cref="Barcade.Core.Content.MicrogameDefinitionValidator.MinDurationSeconds"/>,
        /// <see cref="Barcade.Core.Content.MicrogameDefinitionValidator.MaxDurationSeconds"/>]
        /// bound. Duration, id, verbText and difficulty below were verified 1:1
        /// against each entry's corresponding on-disk
        /// Assets/Barcade/Content/Microgames/*.asset (matched by id+difficulty) and
        /// match exactly -- no diff expected in those four fields for the two
        /// mechanics that remain.
        ///
        /// A regression lock (MicrogameContentGeneratorSpecsTests, fast suite)
        /// parses this array's literal source text and asserts every duration
        /// stays in bound and that every entry migrates to a valid v2 definition,
        /// so this class does not need to compile against Barcade.Core.Tests --
        /// see that file's class doc for why a source-text regex is used instead of
        /// a normal reference (this Editor-only file depends on UnityEditor/
        /// UnityEngine and is not linked into the fast-test dotnet project).
        ///
        /// TASK-045 AC2 ordering decision (generator vs. v1-&gt;v2 migration): this
        /// generator is NOT retired. MicrogameDefinitionMigrationTool.MigrateAll
        /// reads v1 MicrogameDefinition assets from disk as its *input* -- there is
        /// no valid "migrate before generate" ordering, since migration has nothing
        /// to read until generation (or hand-authoring) has produced v1 assets.
        /// The pipeline order is therefore, and remains: GenerateAll (this class,
        /// writes/refreshes the on-disk .asset files) -&gt; MigrateAll (writes v2
        /// JSON from those v1 files) -&gt; ValidateAll (checks the v2 JSON against
        /// MicrogameDefinitionValidator). TASK-061's Unity-gate window (NOT done by
        /// this Specs-array edit alone) still needs to: delete the 6 now-orphaned
        /// on-disk assets (aporrea-d1-03/d3-04, timing-d1-07/d3-08, apunta-d1-05/
        /// d3-06 + .meta -- GenerateAll's LoadOrCreate pattern does not prune
        /// assets no longer in Specs, it only stops re-touching them), then run
        /// GenerateAll -&gt; MigrateAll -&gt; ValidateAll so MicrogamePool.asset's
        /// serialized `definitions` list drops the 3 retired ids too (see hand-off
        /// for the exact commands).
        /// </summary>
        private static readonly DefSpec[] Specs = new DefSpec[]
        {
            // ── Esquiva (dodge) ───────────────────────────────────────────────────
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=5f, Difficulty=1 },
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=4f, Difficulty=2 },
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA RÁPIDO!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=3f, Difficulty=3 },

            // ── Recolecta (collect) ───────────────────────────────────────────────
            new DefSpec { Id="recolecta", VerbText="¡RECOLECTA!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=6f, Difficulty=1 },
            new DefSpec { Id="recolecta", VerbText="¡RECOGE TODO!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=5f, Difficulty=2 },
            new DefSpec { Id="recolecta", VerbText="¡RECOLECTA TODO!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=4f, Difficulty=3 },
        };

        // ── Public entry point (headless + Editor menu) ────────────────────────

        /// <summary>
        /// Creates or overwrites all definition assets and the pool asset.
        /// Safe to call multiple times — idempotent.
        /// </summary>
        [MenuItem("Barcade/Generate Microgame Content")]
        public static void GenerateAll()
        {
            EnsureFolders();

            var pool = LoadOrCreatePool();
            pool.definitions.Clear();

            for (int i = 0; i < Specs.Length; i++)
            {
                DefSpec spec = Specs[i];
                // Build a unique filename: id + difficulty tier + index to avoid collisions.
                string assetName = $"{spec.Id}-d{spec.Difficulty}-{i:D2}.asset";
                string assetPath = $"{MicrogameFolder}/{assetName}";

                MicrogameDefinition def = LoadOrCreate<MicrogameDefinition>(assetPath);
                def.id           = spec.Id;
                def.verbText     = spec.VerbText;
                def.hintText     = spec.HintText;
                def.baseDuration = spec.BaseDuration;
                def.difficulty   = spec.Difficulty;

                EditorUtility.SetDirty(def);
                pool.definitions.Add(def);
            }

            EditorUtility.SetDirty(pool);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MicrogameContentGenerator] Generated {Specs.Length} definitions " +
                      $"and pool at {PoolAssetPath}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ContentFolder))
                AssetDatabase.CreateFolder("Assets/Barcade", "Content");

            if (!AssetDatabase.IsValidFolder(MicrogameFolder))
                AssetDatabase.CreateFolder(ContentFolder, "Microgames");
        }

        private static MicrogamePool LoadOrCreatePool()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MicrogamePool>(PoolAssetPath);
            if (existing != null) return existing;

            var pool = ScriptableObject.CreateInstance<MicrogamePool>();
            AssetDatabase.CreateAsset(pool, PoolAssetPath);
            return pool;
        }

        private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }
    }
}
