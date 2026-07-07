using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Barcade.Core.Content;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-030 / GDD T-106 (§11.1, Annex D.1) AC4/AC6: the engine-free migration
    /// mapping carries every v1 field of all 12 real
    /// Assets/Barcade/Content/Microgames/*.asset definitions into a valid v2
    /// definition with no data loss, and every migrated definition passes the
    /// validator.
    ///
    /// This reads the real .asset YAML from disk (per the ticket's own guidance)
    /// rather than a hand-copied fixture, so it breaks loudly if a content author
    /// edits an asset without re-running the migrator. It does not attempt to
    /// decode Unity's YAML unicode escaping (e.g. "\xA1") -- it only needs the raw
    /// v1 string to reach the v2 field unchanged (an identity copy), which the
    /// equality assertions below verify regardless of how the escape decodes.
    ///
    /// No Unity scene required -- pure C#; the actual ScriptableObject fields are
    /// read by the Editor-side migrator tool
    /// (Barcade.EditorTools.MicrogameDefinitionMigrationTool), which is not
    /// testable without Unity (see hand-off).
    /// </summary>
    [TestFixture]
    public class MicrogameDefinitionMigratorTests
    {
        private static readonly Regex FieldPattern = new Regex(
            @"^  (id|verbText|hintText|baseDuration|difficulty|prefabAddress):\s*(.*)$",
            RegexOptions.Compiled);

        private static string FindMicrogamesFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "Barcade", "Assets", "Barcade", "Content", "Microgames");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate Barcade/Assets/Barcade/Content/Microgames by walking up from " +
                AppContext.BaseDirectory);
        }

        private static string[] AllAssetPaths() =>
            Directory.GetFiles(FindMicrogamesFolder(), "*.asset", SearchOption.TopDirectoryOnly);

        private static LegacyMicrogameDefinitionFields ParseAsset(string path)
        {
            string id = string.Empty, verbText = string.Empty, hintText = string.Empty, prefabAddress = string.Empty;
            float baseDuration = 0f;
            int difficulty = 0;

            foreach (string line in File.ReadAllLines(path))
            {
                Match m = FieldPattern.Match(line);
                if (!m.Success) continue;

                string key = m.Groups[1].Value;
                string value = m.Groups[2].Value.Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2);

                switch (key)
                {
                    case "id": id = value; break;
                    case "verbText": verbText = value; break;
                    case "hintText": hintText = value; break;
                    case "baseDuration": baseDuration = float.Parse(value, CultureInfo.InvariantCulture); break;
                    case "difficulty": difficulty = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "prefabAddress": prefabAddress = value; break;
                }
            }

            return new LegacyMicrogameDefinitionFields(id, verbText, hintText, baseDuration, difficulty, prefabAddress);
        }

        [Test]
        public void ExactlyTwelveDefinitionAssetsExist()
        {
            // Guards the fixture itself: if this drifts, the test below is silently
            // validating fewer/more definitions than Annex D.1's "12 existing
            // definition assets" (AC4).
            Assert.That(AllAssetPaths(), Has.Length.EqualTo(12));
        }

        [Test]
        public void EveryDefinition_MigratesToAValidV2Definition_WithNoFieldLoss()
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in AllAssetPaths())
            {
                LegacyMicrogameDefinitionFields v1 = ParseAsset(path);
                MicrogameDefinitionV2 v2 = MicrogameDefinitionMigrator.MapV1ToV2(v1);

                // ── no data loss: every v1 field is reachable on the v2 definition ──
                Assert.That(v2.SchemaVersion, Is.EqualTo(2), path);
                Assert.That(v2.DisplayVerb, Is.EqualTo(v1.VerbText), path + " displayVerb");
                Assert.That(v2.Duration, Is.EqualTo(v1.BaseDuration), path + " duration");
                Assert.That(v2.LegacyHintText, Is.EqualTo(v1.HintText), path + " hintText");
                Assert.That(v2.LegacyDifficultyTier, Is.EqualTo(v1.Difficulty), path + " difficulty");
                if (!string.IsNullOrEmpty(v1.PrefabAddress))
                    Assert.That(v2.Assets["prefabAddress"], Is.EqualTo(v1.PrefabAddress), path + " prefabAddress");

                // ── migrated assets pass the validator (AC4) ────────────────────────
                ValidationResult result = MicrogameDefinitionValidator.Validate(v2);
                Assert.That(result.IsValid, Is.True,
                    $"{path} migrated to an invalid v2 definition: field '{result.OffendingField}' -- {result.Message}");

                Assert.That(seenIds.Add(v2.Id), Is.True, $"{path} produced duplicate v2 id '{v2.Id}'");
            }
        }

        [Test]
        public void Esquiva_MapsToCanonicalGddMechanicCode()
        {
            var v1 = new LegacyMicrogameDefinitionFields("esquiva", "¡ESQUIVA!", string.Empty, 5f, 1, string.Empty);

            MicrogameDefinitionV2 v2 = MicrogameDefinitionMigrator.MapV1ToV2(v1);

            // Only esquiva has a confirmed GDD MECH_XX identity (Annex D.1: MECH_02).
            Assert.That(v2.Mechanic, Is.EqualTo("MECH_02"));
        }

        [Test]
        public void LegacyMechanicWithNoGddIdentity_CarriesIdForwardUnchanged()
        {
            // aporrea/apunta/timing/recolecta predate the GDD's MECH_XX set and are
            // not listed in Annex D.1 as an existing implementation of any of them
            // (T-107 reconciles this later) -- the migrator must not invent a code.
            var v1 = new LegacyMicrogameDefinitionFields("aporrea", "¡APORREA!", string.Empty, 5f, 1, string.Empty);

            MicrogameDefinitionV2 v2 = MicrogameDefinitionMigrator.MapV1ToV2(v1);

            Assert.That(v2.Mechanic, Is.EqualTo("aporrea"));
        }
    }
}
