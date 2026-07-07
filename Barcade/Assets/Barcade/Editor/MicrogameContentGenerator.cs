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
        /// All 12+ definitions across the 5 mechanics, with meaningfully varied
        /// difficulty, duration and verbText. Each id matches a MicrogameRegistry key.
        ///
        /// TASK-045 (GDD-canonical rule): durations must stay within GDD §11.1's
        /// [<see cref="Barcade.Core.Content.MicrogameDefinitionValidator.MinDurationSeconds"/>,
        /// <see cref="Barcade.Core.Content.MicrogameDefinitionValidator.MaxDurationSeconds"/>]
        /// bound. An earlier revision of this array carried 7-10 s "deliberately
        /// generous" values that were never applied to the 12 on-disk assets (which
        /// already sat at 3-6 s) -- that stale copy would have failed the v2
        /// validator's duration check the moment
        /// MicrogameDefinitionMigrationTool.MigrateAll ran against a fresh
        /// GenerateAll output. The values below were brought back into the [3, 8]
        /// bound by matching each entry to its corresponding on-disk
        /// Assets/Barcade/Content/Microgames/*.asset duration exactly (verified
        /// 1:1 by id+difficulty), so re-running GenerateAll now reproduces the
        /// current on-disk assets byte-for-byte rather than introducing new churn.
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
        /// writes/refreshes the 12 v1 .asset files) -&gt; MigrateAll (writes v2 JSON
        /// from those v1 files) -&gt; ValidateAll (checks the v2 JSON against
        /// MicrogameDefinitionValidator). Because Specs now matches the current
        /// on-disk assets exactly, the next Unity-gate window can re-run all three
        /// steps as a confirmation pass with no expected diff to the 12 assets and
        /// no migrator fixture churn (see hand-off for the exact commands).
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

            // ── Aporrea (mash) ────────────────────────────────────────────────────
            new DefSpec { Id="aporrea", VerbText="¡APORREA!",
                HintText="Aporrea el botón lo más rápido posible",
                BaseDuration=5f, Difficulty=1 },
            new DefSpec { Id="aporrea", VerbText="¡APORREA FUERTE!",
                HintText="Aporrea el botón lo más rápido posible",
                BaseDuration=4f, Difficulty=3 },

            // ── Apunta (aim) ──────────────────────────────────────────────────────
            new DefSpec { Id="apunta", VerbText="¡APUNTA!",
                HintText="Apunta tu marca al objetivo y pulsa",
                BaseDuration=5f, Difficulty=1 },
            new DefSpec { Id="apunta", VerbText="¡APUNTA BIEN!",
                HintText="Apunta tu marca al objetivo y pulsa",
                BaseDuration=4f, Difficulty=3 },

            // ── Timing (press on cue) ─────────────────────────────────────────────
            new DefSpec { Id="timing", VerbText="¡AHORA!",
                HintText="Pulsa cuando la marca esté en la zona verde",
                BaseDuration=5f, Difficulty=1 },
            new DefSpec { Id="timing", VerbText="¡YA!",
                HintText="Pulsa cuando la marca esté en la zona verde",
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
