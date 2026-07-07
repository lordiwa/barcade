using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Barcade.Core.Content;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that migrates the 12 v1 <see cref="MicrogameDefinition"/>
    /// ScriptableObject assets under Assets/Barcade/Content/Microgames/ to the GDD
    /// §11.1 v2 schema, and validates any v2 JSON definition with the same Core
    /// validator the fast test suite uses (GDD T-106, Annex D.1).
    ///
    /// The v1 SO stays exactly as-is (unchanged, AC5 -- existing consumers like
    /// MicrogameLoopController keep compiling against it). This tool reads each v1
    /// asset's six fields, delegates the mapping to the engine-free
    /// <see cref="Barcade.Core.Content.MicrogameDefinitionMigrator"/> (unit-tested
    /// in the fast suite without Unity, see MicrogameDefinitionMigratorTests), and
    /// writes one v2 JSON file per definition under
    /// Assets/Barcade/Content/MicrogamesV2/ -- the wire shape GDD §12 calls the
    /// "capa remota" (remote layer) representation. No new ScriptableObject type
    /// is introduced by this ticket -- see the TASK-030 hand-off for why.
    ///
    /// Safe to re-run: existing v2 JSON files are overwritten deterministically.
    ///
    /// Invoke headless via Unity batchmode:
    /// <code>
    ///   -executeMethod Barcade.EditorTools.MicrogameDefinitionMigrationTool.MigrateAll
    ///   -executeMethod Barcade.EditorTools.MicrogameDefinitionMigrationTool.ValidateAll
    /// </code>
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class MicrogameDefinitionMigrationTool
    {
        private const string SourceFolder = "Assets/Barcade/Content/Microgames";
        private const string OutputFolder = "Assets/Barcade/Content/MicrogamesV2";

        // ── Migration (AC4) ──────────────────────────────────────────────────────

        /// <summary>
        /// Maps every v1 MicrogameDefinition asset under <see cref="SourceFolder"/>
        /// to a v2 JSON file under <see cref="OutputFolder"/>. Idempotent.
        /// </summary>
        [MenuItem("Barcade/Migrate MicrogameDefinitions To V2")]
        public static void MigrateAll()
        {
            Debug.Log("[MicrogameDefinitionMigrationTool] Starting MigrateAll...");

            EnsureOutputFolder();

            string[] guids = AssetDatabase.FindAssets("t:MicrogameDefinition", new[] { SourceFolder });
            int migrated = 0;
            int failed = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var v1 = AssetDatabase.LoadAssetAtPath<MicrogameDefinition>(path);
                if (v1 == null)
                {
                    // Review LOW-6: FindAssets said this is a MicrogameDefinition
                    // but it failed to load -- that is a broken asset, not a
                    // definition to silently leave out of the migrated set.
                    Debug.LogError(
                        $"[MicrogameDefinitionMigrationTool] '{path}' matched t:MicrogameDefinition " +
                        "but failed to load -- counting as a migration failure.");
                    failed++;
                    continue;
                }

                var fields = new LegacyMicrogameDefinitionFields(
                    id: v1.id,
                    verbText: v1.verbText,
                    hintText: v1.hintText,
                    baseDuration: v1.baseDuration,
                    difficulty: v1.difficulty,
                    prefabAddress: v1.prefabAddress);

                MicrogameDefinitionV2 v2 = MicrogameDefinitionMigrator.MapV1ToV2(fields);

                ValidationResult result = MicrogameDefinitionValidator.Validate(v2);
                if (!result.IsValid)
                {
                    Debug.LogError(
                        $"[MicrogameDefinitionMigrationTool] '{path}' migrated to an INVALID v2 " +
                        $"definition: field '{result.OffendingField}' -- {result.Message}");
                    failed++;
                    continue;
                }

                string json = MicrogameDefinitionJson.Serialize(v2);
                string outPath = $"{OutputFolder}/{Path.GetFileNameWithoutExtension(path)}.json";
                File.WriteAllText(outPath, json);
                migrated++;
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"[MicrogameDefinitionMigrationTool] Migrated {migrated} definitions to v2 JSON " +
                $"under {OutputFolder}. Failures: {failed}.");

            if (Application.isBatchMode)
                EditorApplication.Exit(failed > 0 ? 1 : 0);
        }

        // ── Build-pipeline validation hook (AC3) ─────────────────────────────────

        /// <summary>
        /// Validates every v2 JSON definition under <see cref="OutputFolder"/>
        /// with <see cref="MicrogameDefinitionValidator"/> -- the same rules the
        /// fast suite exercises. Fails loudly (batchmode exit code 1, offending
        /// field named in the log) on any invalid definition.
        /// </summary>
        [MenuItem("Barcade/Validate MicrogameDefinitions V2")]
        public static void ValidateAll()
        {
            Debug.Log("[MicrogameDefinitionMigrationTool] Starting ValidateAll...");

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                Debug.LogError(
                    $"[MicrogameDefinitionMigrationTool] {OutputFolder} does not exist -- run " +
                    "'Barcade/Migrate MicrogameDefinitions To V2' first.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            string[] files = Directory.GetFiles(OutputFolder, "*.json", SearchOption.TopDirectoryOnly);
            int invalid = 0;

            foreach (string file in files)
            {
                string json = File.ReadAllText(file);
                MicrogameDefinitionV2 def;
                try
                {
                    def = MicrogameDefinitionJson.Deserialize(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MicrogameDefinitionMigrationTool] {file}: failed to parse -- {ex.Message}");
                    invalid++;
                    continue;
                }

                ValidationResult result = MicrogameDefinitionValidator.Validate(def);
                if (!result.IsValid)
                {
                    Debug.LogError(
                        $"[MicrogameDefinitionMigrationTool] {file}: INVALID -- " +
                        $"field '{result.OffendingField}': {result.Message}");
                    invalid++;
                }
            }

            Debug.Log(
                $"[MicrogameDefinitionMigrationTool] Validated {files.Length} v2 definitions. " +
                $"Invalid: {invalid}.");

            if (Application.isBatchMode)
                EditorApplication.Exit(invalid > 0 ? 1 : 0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder)) return;
            AssetDatabase.CreateFolder("Assets/Barcade/Content", "MicrogamesV2");
        }
    }
}
