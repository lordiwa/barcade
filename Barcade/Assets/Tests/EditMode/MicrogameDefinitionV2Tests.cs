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

        // ── §12 remote-layer codec hardening (fix round MEDIUM-3 + LOW-1/2/4) ────
        // MiniJson is internal to the Core assembly, so all of these exercise it
        // through the public MicrogameDefinitionJson surface -- the same path the
        // eventual on-cabinet remote-content loader takes.

        [Test]
        public void LegacyFields_RoundTrip_WithRealisticDesignerCopy()
        {
            // Multi-line [TextArea]-style hint copy with quotes, a backslash, a
            // tab, Spanish punctuation/accents, and an emoji -- the realistic
            // worst case a designer types into the v1 hintText box.
            MicrogameDefinitionV2 original = BuildGddExample();
            original.LegacyHintText =
                "Mantén pulsado \"A\" para cargar.\n\tSuéltalo en el pico \\ dispara ¡ya! 🎯";
            original.LegacyDifficultyTier = 3;

            string json = MicrogameDefinitionJson.Serialize(original);
            MicrogameDefinitionV2 roundTripped = MicrogameDefinitionJson.Deserialize(json);

            AssertDefinitionsEqual(original, roundTripped);
        }

        [Test]
        public void EscapedSurrogatePair_Deserializes_AndRoundTrips()
        {
            // \uXXXX escapes including a surrogate pair (U+1F600) must decode to
            // the same string a C# literal produces, and survive a second trip
            // through the writer (which emits the characters raw).
            string json = MicrogameDefinitionJson.Serialize(BuildGddExample())
                .Replace("\"legacyHintText\":\"\"", "\"legacyHintText\":\"tab\\u0009cara\\uD83D\\uDE00\"");

            MicrogameDefinitionV2 def = MicrogameDefinitionJson.Deserialize(json);
            Assert.That(def.LegacyHintText, Is.EqualTo("tab\tcara😀"));

            MicrogameDefinitionV2 again = MicrogameDefinitionJson.Deserialize(MicrogameDefinitionJson.Serialize(def));
            Assert.That(again.LegacyHintText, Is.EqualTo(def.LegacyHintText));
        }

        [Test]
        public void ControlCharacters_RoundTrip()
        {
            MicrogameDefinitionV2 original = BuildGddExample();
            original.LegacyHintText = "a\nb\rc\td\u0001e"; // U+0001 forces the \uXXXX writer path

            MicrogameDefinitionV2 roundTripped =
                MicrogameDefinitionJson.Deserialize(MicrogameDefinitionJson.Serialize(original));

            Assert.That(roundTripped.LegacyHintText, Is.EqualTo(original.LegacyHintText));
        }

        [Test]
        public void Numbers_NegativeExponentAndIntegerForms_RoundTripExactly()
        {
            MicrogameDefinitionV2 original = BuildGddExample();
            original.Mechanic = "MECH_99"; // no declared schema -- params are free-form here
            original.Params = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["negative"] = -0.05,
                ["tinyExponent"] = 1.5e-7,   // serializes in exponent form
                ["bigInteger"] = 123456789.0,
            };
            original.PayoutTable = new[] { 2147483647, 6, 4, 1 }; // exact int round-trip incl. int.MaxValue

            MicrogameDefinitionV2 roundTripped =
                MicrogameDefinitionJson.Deserialize(MicrogameDefinitionJson.Serialize(original));

            Assert.That((double)roundTripped.Params["negative"], Is.EqualTo(-0.05));
            Assert.That((double)roundTripped.Params["tinyExponent"], Is.EqualTo(1.5e-7));
            Assert.That((double)roundTripped.Params["bigInteger"], Is.EqualTo(123456789.0));
            Assert.That(roundTripped.PayoutTable, Is.EqualTo(original.PayoutTable));
        }

        [Test]
        public void Deserialize_ExponentInJsonText_ParsesAsNumber()
        {
            // The reader must accept exponent notation it did not itself write.
            string json = MicrogameDefinitionJson.Serialize(BuildGddExample())
                .Replace("\"windAccel\":0.05", "\"windAccel\":5e-2");

            MicrogameDefinitionV2 def = MicrogameDefinitionJson.Deserialize(json);

            Assert.That((double)def.Params["windAccel"], Is.EqualTo(0.05));
        }

        [Test]
        public void Deserialize_TruncatedString_ThrowsFormatException()
        {
            // Cut off mid string-literal: must be a loud FormatException, not an
            // IndexOutOfRangeException from running off the end of the input.
            const string truncated = "{\"schemaVersion\":2,\"id\":\"mg_apunta";

            Assert.Throws<FormatException>(() => MicrogameDefinitionJson.Deserialize(truncated));
        }

        [Test]
        public void Deserialize_TruncatedUnicodeEscape_ThrowsFormatException()
        {
            const string truncated = "{\"id\":\"a\\u12";

            Assert.Throws<FormatException>(() => MicrogameDefinitionJson.Deserialize(truncated));
        }

        [Test]
        public void Deserialize_BareGarbage_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => MicrogameDefinitionJson.Deserialize("garbage"));
        }

        // ── stageProfile.planeMapping (TASK-027, DESIGN CONTRACT RATIFIED 2026-07-08) ──

        [Test]
        public void StageProfile_DefaultPlaneMapping_Is10x10AtOrigin()
        {
            // [ASSUMED] default world size per the ratified contract ("e.g. 10x10").
            var profile = new StageProfile();

            Assert.That(profile.PlaneMapping.WorldSizeX, Is.EqualTo(10f));
            Assert.That(profile.PlaneMapping.WorldSizeZ, Is.EqualTo(10f));
            Assert.That(profile.PlaneMapping.WorldOriginX, Is.EqualTo(0f));
            Assert.That(profile.PlaneMapping.WorldOriginY, Is.EqualTo(0f));
            Assert.That(profile.PlaneMapping.WorldOriginZ, Is.EqualTo(0f));
        }

        [Test]
        public void PlaneMapping_RoundTrips_ThroughJson()
        {
            MicrogameDefinitionV2 original = BuildGddExample();
            original.StageProfile.PlaneMapping = new PlaneMapping(
                worldSizeX: 12.5f, worldSizeZ: 7f,
                worldOriginX: 1f, worldOriginY: -0.5f, worldOriginZ: 2f);

            MicrogameDefinitionV2 roundTripped =
                MicrogameDefinitionJson.Deserialize(MicrogameDefinitionJson.Serialize(original));

            PlaneMapping expected = original.StageProfile.PlaneMapping;
            PlaneMapping actual = roundTripped.StageProfile.PlaneMapping;
            Assert.That(actual.WorldSizeX, Is.EqualTo(expected.WorldSizeX));
            Assert.That(actual.WorldSizeZ, Is.EqualTo(expected.WorldSizeZ));
            Assert.That(actual.WorldOriginX, Is.EqualTo(expected.WorldOriginX));
            Assert.That(actual.WorldOriginY, Is.EqualTo(expected.WorldOriginY));
            Assert.That(actual.WorldOriginZ, Is.EqualTo(expected.WorldOriginZ));
        }

        [Test]
        public void Deserialize_JsonWithoutPlaneMapping_DefaultsIt()
        {
            // Backward compatibility: JSON written before TASK-027 has no
            // planeMapping key under stageProfile at all.
            const string gddJson = @"{
                ""schemaVersion"": 2, ""id"": ""mg_x"", ""mechanic"": ""MECH_04"",
                ""displayVerb"": ""¡APUNTA!"", ""dynamics"": ""competitive"", ""duration"": 5.0,
                ""difficultyScaling"": [], ""params"": {}, ""payoutTable"": [6,4,2,1],
                ""assets"": {}, ""stageProfile"": { ""camera"": ""frontFixed"", ""environment"": """", ""entityPrefabSet"": """" },
                ""minPlayers"": 2, ""tags"": []
            }";

            MicrogameDefinitionV2 def = MicrogameDefinitionJson.Deserialize(gddJson);

            Assert.That(def.StageProfile.PlaneMapping.WorldSizeX, Is.EqualTo(10f));
            Assert.That(def.StageProfile.PlaneMapping.WorldSizeZ, Is.EqualTo(10f));
        }

        [Test]
        public void Deserialize_TrailingGarbageAfterDocument_ThrowsFormatException()
        {
            // Fix round LOW-1: a valid document followed by junk indicates a
            // corrupt/truncated-then-concatenated remote payload -- reject it
            // rather than silently using the first value.
            string json = MicrogameDefinitionJson.Serialize(BuildGddExample()) + " trailing-garbage";

            Assert.Throws<FormatException>(() => MicrogameDefinitionJson.Deserialize(json));
        }

        [Test]
        public void Deserialize_FractionalPayoutEntry_ThrowsFormatException()
        {
            // Fix round LOW-4: a fractional payoutTable entry was previously
            // truncated silently (1.5 -> 1). Coins are integral (GDD §6.1) -- a
            // fractional entry is authoring corruption and must fail loudly.
            string json = MicrogameDefinitionJson.Serialize(BuildGddExample())
                .Replace("\"payoutTable\":[6,4,2,1]", "\"payoutTable\":[6,4,2,1.5]");

            Assert.Throws<FormatException>(() => MicrogameDefinitionJson.Deserialize(json));
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
            Assert.That(actual.LegacyHintText, Is.EqualTo(expected.LegacyHintText));
            Assert.That(actual.LegacyDifficultyTier, Is.EqualTo(expected.LegacyDifficultyTier));
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
