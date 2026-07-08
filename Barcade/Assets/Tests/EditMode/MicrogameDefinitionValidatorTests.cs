using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core.Content;
using Barcade.Core.Scoring;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-030 / GDD T-106 (§11.1) AC2/AC3: the validator rejects mechanic params
    /// outside the mechanic's declared range, duration outside [3, 8], and any
    /// payoutTable entry that violates the GDD §6.1 minimum-reward invariant (no
    /// zero payout for any place — including a coop failure payout). Every
    /// rejection names the offending field so a build-pipeline failure is loud and
    /// actionable (AC3).
    ///
    /// No Unity scene required — pure C#; this is the same code the Editor
    /// build-pipeline hook (Barcade.EditorTools.MicrogameDefinitionMigrationTool)
    /// calls.
    /// </summary>
    [TestFixture]
    public class MicrogameDefinitionValidatorTests
    {
        private static MicrogameDefinitionV2 ValidApuntaDefinition()
        {
            return new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_apunta_viento_01",
                Mechanic = "MECH_04",
                DisplayVerb = "¡APUNTA!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 5.0f,
                DifficultyScaling = new[] { "windAccel" },
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["chargeCycleSec"] = 1.2,
                    ["targetCount"] = 3.0,
                    ["windAccel"] = 0.05,
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                Assets = new Dictionary<string, string>(StringComparer.Ordinal),
                StageProfile = new StageProfile("frontFixed", string.Empty, string.Empty),
                MinPlayers = 2,
                Tags = Array.Empty<string>(),
            };
        }

        [Test]
        public void ValidDefinition_Passes()
        {
            ValidationResult result = MicrogameDefinitionValidator.Validate(ValidApuntaDefinition());
            Assert.That(result.IsValid, Is.True);
        }

        // ── duration [3, 8] (GDD §11.1) ──────────────────────────────────────────

        [TestCase(2.99f)]
        [TestCase(0f)]
        [TestCase(-1f)]
        public void Duration_BelowThree_Rejected(float duration)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Duration = duration;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("duration"));
        }

        [TestCase(8.01f)]
        [TestCase(20f)]
        public void Duration_AboveEight_Rejected(float duration)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Duration = duration;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("duration"));
        }

        [TestCase(3f)]
        [TestCase(8f)]
        public void Duration_AtBoundary_Accepted(float duration)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Duration = duration;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Duration_NaN_Rejected()
        {
            // Fix round LOW-3: NaN sails through a naive (d < 3 || d > 8) gate
            // because every comparison with NaN is false. It must be rejected
            // like any other out-of-range duration.
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Duration = float.NaN;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("duration"));
        }

        // ── payoutTable minimum-reward invariant (GDD §6.1) ─────────────────────

        [Test]
        public void PayoutTable_ZeroForAnyPlace_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.PayoutTable = new[] { 6, 4, 2, 0 }; // 4th place pays nothing

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"));
        }

        [Test]
        public void PayoutTable_CoopFailPaysZero_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Dynamics = MicrogameDynamics.Coop;
            def.PayoutTable = new[] { 4, 0 }; // success=4, fail=0 -- GDD requires fail=1

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"));
        }

        [Test]
        public void PayoutTable_CoopFailPaysOne_Accepted()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Dynamics = MicrogameDynamics.Coop;
            def.PayoutTable = new[] { 4, 1 }; // GDD §6.1 default coop table

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void PayoutTable_Empty_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.PayoutTable = Array.Empty<int>();

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"));
        }

        // ── payoutTable length must match its dynamics' shape (TASK-050) ─────────
        // GDD §6.1: "Reparto por microjuego competitivo (dato payoutTable): 1o=6,
        // 2o=4, 3o=2, 4o=1. Cooperativo: exito=4 a todos, fallo=1 a todos" -- two
        // DIFFERENT shapes under the same field name: 4 entries (one per place)
        // for competitive/asym1v3 (both produce a ranked 1..4 podium -- the GDD
        // gives no separate asym1v3 payout shape), 2 entries [success, fail] for
        // coop. Found by rev-c037 (TASK-037 review): the validator accepted ANY
        // length >= 1 while PayoutRules.ApplyCompetitive throws unless length==4,
        // so a [6,4,2] definition validated green and crashed mid-session.

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void PayoutTable_CompetitiveWithWrongLength_Rejected(int length)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition(); // Dynamics = Competitive
            def.PayoutTable = MakeTable(length);

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False, $"length {length}");
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"), $"length {length}");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void PayoutTable_Asym1v3WithWrongLength_Rejected(int length)
        {
            // Asym1v3 rounds still resolve to a ranked 1..4 podium (RoundRecord.Places
            // in TASK-035 uses the same 1..4 shape for every dynamics) and have no
            // GDD-described payout shape of their own -- same 4-entry contract as
            // competitive.
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Dynamics = MicrogameDynamics.Asym1v3;
            def.PayoutTable = MakeTable(length);

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False, $"length {length}");
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"), $"length {length}");
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        public void PayoutTable_CoopWithWrongLength_Rejected(int length)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Dynamics = MicrogameDynamics.Coop;
            def.PayoutTable = MakeTable(length);

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False, $"length {length}");
            Assert.That(result.OffendingField, Is.EqualTo("payoutTable"), $"length {length}");
        }

        [Test]
        public void PayoutTable_LengthContractMatchesPayoutRules_NoValidatedInputEverThrows()
        {
            // AC2: validator and PayoutRules.Apply* must agree on shape -- no
            // payoutTable that validates green may throw when actually applied.
            for (int length = 0; length <= 6; length++)
            {
                int[] table = MakeTable(length);

                foreach (MicrogameDynamics dynamics in new[]
                         { MicrogameDynamics.Competitive, MicrogameDynamics.Asym1v3, MicrogameDynamics.Coop })
                {
                    MicrogameDefinitionV2 def = ValidApuntaDefinition();
                    def.Dynamics = dynamics;
                    def.PayoutTable = table;

                    ValidationResult result = MicrogameDefinitionValidator.Validate(def);
                    if (!result.IsValid) continue; // only checking the "validates green" side

                    if (dynamics == MicrogameDynamics.Coop)
                    {
                        Assert.DoesNotThrow(() => PayoutRules.ApplyCoop(new int[4], true, table[0], 0b1111),
                            $"length {length}, {dynamics}: validated green but ApplyCoop(success) threw");
                        Assert.DoesNotThrow(() => PayoutRules.ApplyCoop(new int[4], false, table[1], 0b1111),
                            $"length {length}, {dynamics}: validated green but ApplyCoop(fail) threw");
                    }
                    else
                    {
                        Assert.DoesNotThrow(() => PayoutRules.ApplyCompetitive(new int[4], new[] { 1, 2, 3, 4 }, table),
                            $"length {length}, {dynamics}: validated green but ApplyCompetitive threw");
                    }
                }
            }
        }

        /// <summary>A payoutTable of the given length, every entry the minimum-valid 1 (shape-only test helper).</summary>
        private static int[] MakeTable(int length)
        {
            var table = new int[length];
            for (int i = 0; i < length; i++) table[i] = 1;
            return table;
        }

        // ── stageProfile.planeMapping (TASK-027, DESIGN CONTRACT RATIFIED 2026-07-08) ──

        [TestCase(0f, 10f)]
        [TestCase(-1f, 10f)]
        [TestCase(10f, 0f)]
        [TestCase(10f, -1f)]
        [TestCase(float.NaN, 10f)]
        public void PlaneMapping_NonPositiveWorldSize_Rejected(float worldSizeX, float worldSizeZ)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.StageProfile.PlaneMapping = new PlaneMapping(worldSizeX, worldSizeZ, 0f, 0f, 0f);

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("stageProfile.planeMapping.worldSize"));
        }

        [Test]
        public void PlaneMapping_PositiveWorldSize_Accepted()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.StageProfile.PlaneMapping = new PlaneMapping(12f, 8f, 1f, 0f, -1f);

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }

        // ── params outside the range declared by the mechanic (GDD §11.1) ───────

        [Test]
        public void Params_ChargeCycleSecOutsideMechanicRange_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Params["chargeCycleSec"] = 10.0; // MECH_04 schema caps at 3.0

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("params.chargeCycleSec"));
        }

        [Test]
        public void Params_WindAccelBelowMechanicRange_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Params["windAccel"] = -0.1;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("params.windAccel"));
        }

        [Test]
        public void Params_UnknownMechanic_SkipsRangeCheck()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Mechanic = "MECH_99"; // no declared schema
            def.Params["chargeCycleSec"] = 999.0;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }

        // ── type-invalid values for a DECLARED range param (fix round MEDIUM-2) ──
        // A non-numeric value for a param the mechanic declares a numeric range
        // for must fail loudly, not silently bypass the range gate (e.g.
        // "chargeCycleSec": "10.0" as a quoted string previously validated).

        private static readonly object[] TypeInvalidRangeParamValues =
        {
            new object[] { "quoted numeric string", "10.0" },
            new object[] { "bool", true },
            new object[] { "null", null },
            new object[]
            {
                "nested object",
                new Dictionary<string, object>(StringComparer.Ordinal) { ["value"] = 1.2 },
            },
        };

        [TestCaseSource(nameof(TypeInvalidRangeParamValues))]
        public void Params_DeclaredRangeParam_WithNonNumericValue_RejectedLoudly(string label, object value)
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Params["chargeCycleSec"] = value;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False, label);
            Assert.That(result.OffendingField, Is.EqualTo("params.chargeCycleSec"), label);
        }

        [Test]
        public void Params_UndeclaredParamName_NonNumericValue_StillAccepted()
        {
            // Pins the boundary of the type gate: it applies only to names the
            // mechanic declares a numeric range for. The GDD §11.1 example itself
            // carries a nested object param (targetMoving) on MECH_04 -- that must
            // keep validating.
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Params["targetMoving"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["enabled"] = true,
                ["speed"] = 0.15,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }

        // ── structural guards ────────────────────────────────────────────────────

        [Test]
        public void MissingId_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Id = string.Empty;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("id"));
        }

        [Test]
        public void MissingMechanic_Rejected()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Mechanic = null;

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.OffendingField, Is.EqualTo("mechanic"));
        }

        // ── MECH_05 reserved (non-numeric) params must not close the door ───────
        // Mid-flight note (TASK-025 review, relayed during TASK-030): GDD §4
        // MECH_05 declares signalMode (visual/audio/both) and colorFilter (bool)
        // as variants. Neither is implemented in ReaccionaMicrogame yet -- these
        // two tests only prove the v2 schema/validator reserve the names so a
        // future definition using them validates; no enum/bool validation
        // semantics are implemented here (none were requested).

        [Test]
        public void Mech05Schema_ReservesSignalModeAndColorFilterByName()
        {
            bool found = MechanicParamSchemas.TryGet("MECH_05", out MechanicParamSchema schema);

            Assert.That(found, Is.True);
            Assert.That(schema.ReservedParamNames, Does.Contain("signalMode"));
            Assert.That(schema.ReservedParamNames, Does.Contain("colorFilter"));
        }

        [Test]
        public void Mech05ReservedParams_SignalModeAndColorFilter_DoNotFailValidation()
        {
            MicrogameDefinitionV2 def = ValidApuntaDefinition();
            def.Mechanic = "MECH_05";
            def.Params = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["signalMode"] = "both",
                ["colorFilter"] = true,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);

            Assert.That(result.IsValid, Is.True);
        }
    }
}
