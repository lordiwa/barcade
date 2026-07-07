using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core.Content;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-030 / GDD T-106 (§11.1) AC1: the v2 MicrogameDefinition schema carries
    /// every GDD §11.1 field and round-trips through JSON without loss.
    ///
    /// The fixture is the exact worked example from GDD §11.1
    /// ("mg_apunta_viento_01") so this test is traceable to the spec text rather
    /// than to an invented shape.
    ///
    /// No Unity scene required — pure C#.
    /// </summary>
    [TestFixture]
    public class MicrogameDefinitionV2Tests
    {
        private static MicrogameDefinitionV2 BuildGddExample()
        {
            return new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_apunta_viento_01",
                Mechanic = "MECH_04",
                DisplayVerb = "¡APUNTA!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 5.0f,
                DifficultyScaling = new[] { "targetMoving.speed", "windAccel" },
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["chargeCycleSec"] = 1.2,
                    ["targetCount"] = 3.0,
                    ["targetMoving"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["enabled"] = true,
                        ["speed"] = 0.15,
                    },
                    ["windAccel"] = 0.05,
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                Assets = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["palette"] = "default",
                    ["sfxStinger"] = "stinger_apunta",
                },
                StageProfile = new StageProfile("frontFixed", "env_range_01", "set_apunta"),
                MinPlayers = 2,
                Tags = new[] { "aim", "charge", "wind" },
            };
        }

        [Test]
        public void GddExample_HasEveryGdd11_1Field()
        {
            MicrogameDefinitionV2 def = BuildGddExample();

            Assert.That(def.SchemaVersion, Is.EqualTo(2));
            Assert.That(def.Id, Is.EqualTo("mg_apunta_viento_01"));
            Assert.That(def.Mechanic, Is.EqualTo("MECH_04"));
            Assert.That(def.DisplayVerb, Is.EqualTo("¡APUNTA!"));
            Assert.That(def.Dynamics, Is.EqualTo(MicrogameDynamics.Competitive));
            Assert.That(def.Duration, Is.EqualTo(5.0f));
            Assert.That(def.DifficultyScaling, Is.EqualTo(new[] { "targetMoving.speed", "windAccel" }));
            Assert.That(def.Params.ContainsKey("chargeCycleSec"), Is.True);
            Assert.That(def.PayoutTable, Is.EqualTo(new[] { 6, 4, 2, 1 }));
            Assert.That(def.Assets["palette"], Is.EqualTo("default"));
            Assert.That(def.StageProfile.Camera, Is.EqualTo("frontFixed"));
            Assert.That(def.MinPlayers, Is.EqualTo(2));
            Assert.That(def.Tags, Is.EqualTo(new[] { "aim", "charge", "wind" }));
        }

        [Test]
        public void Serialize_Deserialize_RoundTrips_AllFields()
        {
            MicrogameDefinitionV2 original = BuildGddExample();

            string json = MicrogameDefinitionJson.Serialize(original);
            MicrogameDefinitionV2 roundTripped = MicrogameDefinitionJson.Deserialize(json);

            AssertDefinitionsEqual(original, roundTripped);
        }

        [Test]
        public void Deserialize_GddLiteralJsonExample_MatchesHandBuiltPoco()
        {
            // The literal §11.1 example text, so the reader is proven against the
            // spec itself and not just against its own writer.
            const string gddJson = @"{
                ""schemaVersion"": 2,
                ""id"": ""mg_apunta_viento_01"",
                ""mechanic"": ""MECH_04"",
                ""displayVerb"": ""¡APUNTA!"",
                ""dynamics"": ""competitive"",
                ""duration"": 5.0,
                ""difficultyScaling"": [""targetMoving.speed"", ""windAccel""],
                ""params"": {
                    ""chargeCycleSec"": 1.2,
                    ""targetCount"": 3,
                    ""targetMoving"": { ""enabled"": true, ""speed"": 0.15 },
                    ""windAccel"": 0.05
                },
                ""payoutTable"": [6, 4, 2, 1],
                ""assets"": { ""palette"": ""default"", ""sfxStinger"": ""stinger_apunta"" },
                ""stageProfile"": { ""camera"": ""frontFixed"", ""environment"": ""env_range_01"", ""entityPrefabSet"": ""set_apunta"" },
                ""minPlayers"": 2,
                ""tags"": [""aim"", ""charge"", ""wind""]
            }";

            MicrogameDefinitionV2 fromGdd = MicrogameDefinitionJson.Deserialize(gddJson);
            MicrogameDefinitionV2 handBuilt = BuildGddExample();

            AssertDefinitionsEqual(handBuilt, fromGdd);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static void AssertDefinitionsEqual(MicrogameDefinitionV2 expected, MicrogameDefinitionV2 actual)
        {
            Assert.That(actual.SchemaVersion, Is.EqualTo(expected.SchemaVersion));
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Mechanic, Is.EqualTo(expected.Mechanic));
            Assert.That(actual.DisplayVerb, Is.EqualTo(expected.DisplayVerb));
            Assert.That(actual.Dynamics, Is.EqualTo(expected.Dynamics));
            Assert.That(actual.Duration, Is.EqualTo(expected.Duration));
            Assert.That(actual.DifficultyScaling, Is.EqualTo(expected.DifficultyScaling));
            Assert.That(DeepEqual(actual.Params, expected.Params), Is.True, "params mismatch");
            Assert.That(actual.PayoutTable, Is.EqualTo(expected.PayoutTable));
            Assert.That(actual.Assets, Is.EquivalentTo(expected.Assets));
            Assert.That(actual.StageProfile.Camera, Is.EqualTo(expected.StageProfile.Camera));
            Assert.That(actual.StageProfile.Environment, Is.EqualTo(expected.StageProfile.Environment));
            Assert.That(actual.StageProfile.EntityPrefabSet, Is.EqualTo(expected.StageProfile.EntityPrefabSet));
            Assert.That(actual.MinPlayers, Is.EqualTo(expected.MinPlayers));
            Assert.That(actual.Tags, Is.EqualTo(expected.Tags));
        }

        private static bool DeepEqual(object a, object b)
        {
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db)
            {
                if (da.Count != db.Count) return false;
                foreach (KeyValuePair<string, object> kv in da)
                {
                    if (!db.TryGetValue(kv.Key, out object bv)) return false;
                    if (!DeepEqual(kv.Value, bv)) return false;
                }
                return true;
            }

            if (a is List<object> la && b is List<object> lb)
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                {
                    if (!DeepEqual(la[i], lb[i])) return false;
                }
                return true;
            }

            if (a is double ad && b is double bd) return Math.Abs(ad - bd) < 1e-9;
            if (a is bool ab && b is bool bb) return ab == bb;
            if (a is string astr && b is string bstr) return astr == bstr;

            return Equals(a, b);
        }
    }
}
