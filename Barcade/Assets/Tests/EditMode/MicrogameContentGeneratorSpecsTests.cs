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
    /// TASK-045: locks MicrogameContentGenerator.Specs against the exact defect
    /// class this ticket fixed -- a 7-10 s duration entry that violates the GDD
    /// §11.1 [3, 8] bound and would fail
    /// MicrogameDefinitionMigrationTool.ValidateAll the moment GenerateAll was
    /// re-run and the assets re-migrated.
    ///
    /// Barcade/Assets/Barcade/Editor/MicrogameContentGenerator.cs lives in
    /// Barcade.EditorTools (UnityEditor/UnityEngine dependency) and, unlike
    /// Barcade.Core.Content, is not linked into the fast-test dotnet project --
    /// it cannot be referenced directly from here. Following the same pattern
    /// MicrogameDefinitionMigratorTests.cs already uses for the 12 real on-disk
    /// .asset files (reading the real artifact's text rather than a hand-copied
    /// fixture, so it breaks loudly if the source drifts from what this test
    /// assumes), this reads MicrogameContentGenerator.cs's own source text and
    /// regex-parses each DefSpec entry's five fields out of the Specs array
    /// literal. Each parsed entry is then run through the exact same
    /// MicrogameDefinitionMigrator/MicrogameDefinitionValidator pipeline
    /// MicrogameDefinitionMigrationTool.MigrateAll uses on the real assets, so
    /// "every generated definition passes the v2 validator" (AC1) is checked
    /// against the literal values the generator will write next time it runs,
    /// not a second hand-maintained copy of them.
    /// </summary>
    [TestFixture]
    public class MicrogameContentGeneratorSpecsTests
    {
        private static readonly Regex SpecBlockPattern = new Regex(
            @"new DefSpec \{(.*?)\},", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex IdPattern = new Regex(@"Id\s*=\s*""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex VerbTextPattern = new Regex(@"VerbText\s*=\s*""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex HintTextPattern = new Regex(@"HintText\s*=\s*""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex BaseDurationPattern = new Regex(@"BaseDuration\s*=\s*([\d.]+)f", RegexOptions.Compiled);
        private static readonly Regex DifficultyPattern = new Regex(@"Difficulty\s*=\s*(\d+)", RegexOptions.Compiled);

        private static string FindGeneratorSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(
                    dir.FullName, "Barcade", "Assets", "Barcade", "Editor", "MicrogameContentGenerator.cs");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate Barcade/Assets/Barcade/Editor/MicrogameContentGenerator.cs by walking up from " +
                AppContext.BaseDirectory);
        }

        private static List<LegacyMicrogameDefinitionFields> ParseSpecs()
        {
            string source = File.ReadAllText(FindGeneratorSource());

            // Isolate the Specs array body so nothing outside it (e.g. this very
            // class doc, which mentions "DefSpec") can ever be picked up.
            int specsStart = source.IndexOf("Specs = new DefSpec[]", StringComparison.Ordinal);
            Assert.That(specsStart, Is.GreaterThanOrEqualTo(0),
                "Could not find 'Specs = new DefSpec[]' in MicrogameContentGenerator.cs");
            int bodyStart = source.IndexOf('{', specsStart);
            int bodyEnd = source.IndexOf("};", bodyStart, StringComparison.Ordinal);
            string body = source.Substring(bodyStart, bodyEnd - bodyStart);

            var result = new List<LegacyMicrogameDefinitionFields>();
            foreach (Match block in SpecBlockPattern.Matches(body))
            {
                string entry = block.Groups[1].Value;
                string id = MatchField(IdPattern, entry, "Id");
                string verbText = MatchField(VerbTextPattern, entry, "VerbText");
                string hintText = MatchField(HintTextPattern, entry, "HintText");
                string baseDurationRaw = MatchField(BaseDurationPattern, entry, "BaseDuration");
                string difficultyRaw = MatchField(DifficultyPattern, entry, "Difficulty");

                float baseDuration = float.Parse(baseDurationRaw, CultureInfo.InvariantCulture);
                int difficulty = int.Parse(difficultyRaw, CultureInfo.InvariantCulture);

                result.Add(new LegacyMicrogameDefinitionFields(id, verbText, hintText, baseDuration, difficulty, string.Empty));
            }

            return result;
        }

        /// <summary>
        /// rev-t045 LOW-1: a bare <c>Regex.Match(entry).Groups[1].Value</c> returns
        /// "" silently when the pattern simply fails to match (e.g. a future edit
        /// renames VerbText to Verb, or reformats the literal) -- indistinguishable
        /// from a genuinely empty field and easy to miss in a failure message. This
        /// asserts the match itself succeeded before ever reading its value, so a
        /// parser/source mismatch fails loudly with the offending entry text rather
        /// than silently feeding "" into MapV1ToV2/Validate.
        /// </summary>
        private static string MatchField(Regex pattern, string entry, string fieldName)
        {
            Match m = pattern.Match(entry);
            Assert.That(m.Success, Is.True,
                $"Could not parse '{fieldName}' out of a DefSpec entry -- the parser regex and the generator's " +
                $"source no longer agree:\n{entry}");
            return m.Groups[1].Value;
        }

        [Test]
        public void ExactlyTwelveSpecsExist()
        {
            // Guards the fixture/parse itself (mirrors
            // MicrogameDefinitionMigratorTests.ExactlyTwelveDefinitionAssetsExist):
            // if this drifts, the tests below would silently check fewer/more
            // entries than the generator actually declares.
            Assert.That(ParseSpecs(), Has.Count.EqualTo(12));
        }

        [Test]
        public void EverySpec_DurationIsWithinGdd11_1Bound()
        {
            foreach (LegacyMicrogameDefinitionFields spec in ParseSpecs())
            {
                Assert.That(
                    spec.BaseDuration,
                    Is.InRange(MicrogameDefinitionValidator.MinDurationSeconds, MicrogameDefinitionValidator.MaxDurationSeconds),
                    $"{spec.Id} (difficulty {spec.Difficulty}) baseDuration {spec.BaseDuration} violates the " +
                    "GDD §11.1 bound -- this is the exact TASK-045 defect class and must never silently return.");
            }
        }

        [Test]
        public void EverySpec_MigratesToAValidV2Definition()
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (LegacyMicrogameDefinitionFields spec in ParseSpecs())
            {
                MicrogameDefinitionV2 v2 = MicrogameDefinitionMigrator.MapV1ToV2(spec);

                ValidationResult result = MicrogameDefinitionValidator.Validate(v2);
                Assert.That(result.IsValid, Is.True,
                    $"Spec '{spec.Id}' (difficulty {spec.Difficulty}) migrates to an invalid v2 definition: " +
                    $"field '{result.OffendingField}' -- {result.Message}");

                Assert.That(seenIds.Add(v2.Id), Is.True, $"Spec produced duplicate v2 id '{v2.Id}'");
            }
        }
    }
}
