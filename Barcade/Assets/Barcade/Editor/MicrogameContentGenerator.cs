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
        /// Base durations are deliberately generous (7–10 s) so the game reads well on
        /// a first play-through. The speed ramp compresses them across a session, but a
        /// minimum effective duration (set in MicrogameLoopController) prevents any round
        /// from feeling like a blink.
        /// </summary>
        private static readonly DefSpec[] Specs = new DefSpec[]
        {
            // ── Esquiva (dodge) ───────────────────────────────────────────────────
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=9f, Difficulty=1 },
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=8f, Difficulty=2 },
            new DefSpec { Id="esquiva", VerbText="¡ESQUIVA RÁPIDO!",
                HintText="Mueve tu figura y evita los obstáculos",
                BaseDuration=7f, Difficulty=3 },

            // ── Aporrea (mash) ────────────────────────────────────────────────────
            new DefSpec { Id="aporrea", VerbText="¡APORREA!",
                HintText="Aporrea el botón lo más rápido posible",
                BaseDuration=9f, Difficulty=1 },
            new DefSpec { Id="aporrea", VerbText="¡APORREA FUERTE!",
                HintText="Aporrea el botón lo más rápido posible",
                BaseDuration=8f, Difficulty=3 },

            // ── Apunta (aim) ──────────────────────────────────────────────────────
            new DefSpec { Id="apunta", VerbText="¡APUNTA!",
                HintText="Apunta tu marca al objetivo y pulsa",
                BaseDuration=9f, Difficulty=1 },
            new DefSpec { Id="apunta", VerbText="¡APUNTA BIEN!",
                HintText="Apunta tu marca al objetivo y pulsa",
                BaseDuration=8f, Difficulty=3 },

            // ── Timing (press on cue) ─────────────────────────────────────────────
            new DefSpec { Id="timing", VerbText="¡AHORA!",
                HintText="Pulsa cuando la marca esté en la zona verde",
                BaseDuration=8f, Difficulty=1 },
            new DefSpec { Id="timing", VerbText="¡YA!",
                HintText="Pulsa cuando la marca esté en la zona verde",
                BaseDuration=7f, Difficulty=3 },

            // ── Recolecta (collect) ───────────────────────────────────────────────
            new DefSpec { Id="recolecta", VerbText="¡RECOLECTA!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=10f, Difficulty=1 },
            new DefSpec { Id="recolecta", VerbText="¡RECOGE TODO!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=9f, Difficulty=2 },
            new DefSpec { Id="recolecta", VerbText="¡RECOLECTA TODO!",
                HintText="Recoge los objetos verdes antes de que acabe el tiempo",
                BaseDuration=8f, Difficulty=3 },
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
