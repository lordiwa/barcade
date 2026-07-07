using System.Collections.Generic;

namespace Barcade.Core.Content
{
    /// <summary>
    /// Result of validating a <see cref="MicrogameDefinitionV2"/> (AC2/AC3): on
    /// failure names the offending field so a build-pipeline failure is loud and
    /// actionable, never a silent skip.
    /// </summary>
    public readonly struct ValidationResult
    {
        public readonly bool IsValid;
        public readonly string OffendingField;
        public readonly string Message;

        private ValidationResult(bool isValid, string offendingField, string message)
        {
            IsValid = isValid;
            OffendingField = offendingField;
            Message = message;
        }

        public static ValidationResult Ok() => new ValidationResult(true, null, null);

        public static ValidationResult Fail(string offendingField, string message) =>
            new ValidationResult(false, offendingField, message);
    }

    /// <summary>
    /// Validates a v2 microgame definition against GDD §11.1's declared rules:
    /// duration in [3, 8], payoutTable respects the §6.1 minimum-reward invariant
    /// (no zero payout for any place -- a coop failure included), and every
    /// declared mechanic param stays within the range the mechanic declares
    /// (§11.1, <see cref="MechanicParamSchemas"/>).
    ///
    /// Runs in the fast suite (AC3) and is the same code the Editor build-pipeline
    /// hook (Barcade.EditorTools.MicrogameDefinitionMigrationTool.ValidateAll)
    /// calls -- one rule set, never duplicated between test and build tooling.
    ///
    /// Deliberately NOT checked here: SchemaVersion. Version dispatch (reject or
    /// migrate a payload whose schemaVersion != 2) belongs to the §12 remote
    /// content loader, which owns wire-format versioning -- next-ticket scope
    /// (review LOW-5, reviewer concurred).
    /// </summary>
    public static class MicrogameDefinitionValidator
    {
        public const float MinDurationSeconds = 3f;
        public const float MaxDurationSeconds = 8f;
        public const int MinPayout = 1;

        public static ValidationResult Validate(MicrogameDefinitionV2 def)
        {
            if (def == null)
                return ValidationResult.Fail("definition", "definition must not be null");

            if (string.IsNullOrEmpty(def.Id))
                return ValidationResult.Fail("id", "id must be non-empty");

            if (string.IsNullOrEmpty(def.Mechanic))
                return ValidationResult.Fail("mechanic", "mechanic must be non-empty");

            // Negated inclusion (rather than `d < min || d > max`) so NaN -- for
            // which every comparison is false -- is rejected too (review LOW-3).
            if (!(def.Duration >= MinDurationSeconds && def.Duration <= MaxDurationSeconds))
            {
                return ValidationResult.Fail(
                    "duration",
                    $"duration {def.Duration} is outside the GDD §11.1 allowed range " +
                    $"[{MinDurationSeconds}, {MaxDurationSeconds}]");
            }

            ValidationResult payoutResult = ValidatePayoutTable(def.PayoutTable);
            if (!payoutResult.IsValid) return payoutResult;

            return ValidateParams(def.Mechanic, def.Params);
        }

        private static ValidationResult ValidatePayoutTable(int[] payoutTable)
        {
            if (payoutTable == null || payoutTable.Length == 0)
                return ValidationResult.Fail("payoutTable", "payoutTable must declare at least one payout");

            for (int i = 0; i < payoutTable.Length; i++)
            {
                if (payoutTable[i] < MinPayout)
                {
                    return ValidationResult.Fail(
                        "payoutTable",
                        $"payoutTable[{i}]={payoutTable[i]} violates the GDD §6.1 minimum-reward invariant " +
                        "(no zero payout for any place; a coop failure must still pay)");
                }
            }

            return ValidationResult.Ok();
        }

        private static ValidationResult ValidateParams(string mechanic, Dictionary<string, object> parameters)
        {
            if (parameters == null || !MechanicParamSchemas.TryGet(mechanic, out MechanicParamSchema schema))
                return ValidationResult.Ok();

            foreach (ParamRange range in schema.Ranges)
            {
                if (!parameters.TryGetValue(range.Name, out object raw)) continue;

                // Review MEDIUM-2: a non-numeric value for a param the mechanic
                // declares a numeric range for must fail loudly, not silently
                // bypass the range gate (e.g. "chargeCycleSec": "10.0" as a
                // quoted string). Params the schema does NOT declare a range for
                // stay free-form (the GDD §11.1 example itself nests an object
                // under params.targetMoving).
                if (!TryToDouble(raw, out double value))
                {
                    string got = raw == null ? "null" : $"{raw.GetType().Name} '{raw}'";
                    return ValidationResult.Fail(
                        $"params.{range.Name}",
                        $"params.{range.Name} must be a number (mechanic {mechanic} declares the range " +
                        $"[{range.Min}, {range.Max}]); got {got}");
                }

                if (value < range.Min || value > range.Max)
                {
                    return ValidationResult.Fail(
                        $"params.{range.Name}",
                        $"params.{range.Name}={value} is outside the range [{range.Min}, {range.Max}] " +
                        $"declared by mechanic {mechanic}");
                }
            }

            return ValidationResult.Ok();
        }

        private static bool TryToDouble(object raw, out double value)
        {
            switch (raw)
            {
                case double d: value = d; return true;
                case float f: value = f; return true;
                case int n: value = n; return true;
                default: value = 0.0; return false;
            }
        }
    }
}
