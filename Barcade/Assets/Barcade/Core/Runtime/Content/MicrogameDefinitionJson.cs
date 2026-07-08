using System;
using System.Collections.Generic;
using System.Globalization;

namespace Barcade.Core.Content
{
    /// <summary>
    /// Serializes/deserializes <see cref="MicrogameDefinitionV2"/> to/from the exact
    /// JSON shape in GDD §11.1, via the engine-free <see cref="MiniJson"/> codec
    /// (see its class doc for why this isn't System.Text.Json/Newtonsoft.Json).
    /// This is the "capa remota" (§12) wire format: the Editor migrator tool
    /// writes it, and the eventual on-cabinet remote-content loader reads it --
    /// both paths go through this one class so they can never drift apart.
    /// </summary>
    public static class MicrogameDefinitionJson
    {
        public static string Serialize(MicrogameDefinitionV2 def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            var assetsObj = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in def.Assets ?? new Dictionary<string, string>())
                assetsObj[kv.Key] = kv.Value;

            PlaneMapping planeMapping = def.StageProfile?.PlaneMapping ?? new PlaneMapping();
            var stageProfileObj = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["camera"] = def.StageProfile?.Camera ?? string.Empty,
                ["environment"] = def.StageProfile?.Environment ?? string.Empty,
                ["entityPrefabSet"] = def.StageProfile?.EntityPrefabSet ?? string.Empty,
                // TASK-027 (DESIGN CONTRACT RATIFIED 2026-07-08): stageProfile.planeMapping,
                // additive to the GDD §11.1 wire shape.
                ["planeMapping"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["worldSize"] = new List<object> { (double)planeMapping.WorldSizeX, (double)planeMapping.WorldSizeZ },
                    ["worldOrigin"] = new List<object>
                    {
                        (double)planeMapping.WorldOriginX,
                        (double)planeMapping.WorldOriginY,
                        (double)planeMapping.WorldOriginZ,
                    },
                },
            };

            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = (double)def.SchemaVersion,
                ["id"] = def.Id,
                ["mechanic"] = def.Mechanic,
                ["displayVerb"] = def.DisplayVerb,
                ["dynamics"] = DynamicsToString(def.Dynamics),
                ["duration"] = (double)def.Duration,
                ["difficultyScaling"] = ToObjectList(def.DifficultyScaling),
                ["params"] = def.Params ?? new Dictionary<string, object>(),
                ["payoutTable"] = ToObjectList(def.PayoutTable),
                ["assets"] = assetsObj,
                ["stageProfile"] = stageProfileObj,
                ["minPlayers"] = (double)def.MinPlayers,
                ["tags"] = ToObjectList(def.Tags),
                ["legacyHintText"] = def.LegacyHintText ?? string.Empty,
                ["legacyDifficultyTier"] = def.LegacyDifficultyTier.HasValue
                    ? (object)(double)def.LegacyDifficultyTier.Value
                    : null,
            };

            return MiniJson.Write(root);
        }

        public static MicrogameDefinitionV2 Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("json must be non-empty", nameof(json));

            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("Root JSON value must be an object.");

            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = (int)GetDouble(root, "schemaVersion"),
                Id = GetString(root, "id"),
                Mechanic = GetString(root, "mechanic"),
                DisplayVerb = GetString(root, "displayVerb"),
                Dynamics = DynamicsFromString(GetString(root, "dynamics")),
                Duration = (float)GetDouble(root, "duration"),
                DifficultyScaling = GetStringArray(root, "difficultyScaling"),
                Params = GetObject(root, "params"),
                PayoutTable = GetIntArray(root, "payoutTable"),
                Assets = GetStringDictionary(root, "assets"),
                StageProfile = GetStageProfile(root, "stageProfile"),
                MinPlayers = (int)GetDouble(root, "minPlayers"),
                Tags = GetStringArray(root, "tags"),
                LegacyHintText = root.ContainsKey("legacyHintText") ? GetString(root, "legacyHintText") : string.Empty,
            };

            if (root.TryGetValue("legacyDifficultyTier", out object ldt) && ldt != null)
                def.LegacyDifficultyTier = (int)Convert.ToDouble(ldt, CultureInfo.InvariantCulture);

            return def;
        }

        // ── dynamics <-> string (GDD §11.1: "competitive" | "asym1v3" | "coop") ────

        private static string DynamicsToString(MicrogameDynamics dynamics)
        {
            switch (dynamics)
            {
                case MicrogameDynamics.Competitive: return "competitive";
                case MicrogameDynamics.Asym1v3: return "asym1v3";
                case MicrogameDynamics.Coop: return "coop";
                default: throw new ArgumentOutOfRangeException(nameof(dynamics));
            }
        }

        private static MicrogameDynamics DynamicsFromString(string value)
        {
            switch (value)
            {
                case "competitive": return MicrogameDynamics.Competitive;
                case "asym1v3": return MicrogameDynamics.Asym1v3;
                case "coop": return MicrogameDynamics.Coop;
                default: throw new FormatException($"Unknown dynamics value '{value}'.");
            }
        }

        // ── tree readers/writers ─────────────────────────────────────────────────

        private static List<object> ToObjectList(IEnumerable<string> values)
        {
            var list = new List<object>();
            if (values != null)
                foreach (string v in values) list.Add(v);
            return list;
        }

        private static List<object> ToObjectList(IEnumerable<int> values)
        {
            var list = new List<object>();
            if (values != null)
                foreach (int v in values) list.Add((double)v);
            return list;
        }

        private static double GetDouble(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out object v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0.0;

        private static string GetString(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out object v) ? v as string : null;

        private static string[] GetStringArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object v) || !(v is List<object> list))
                return Array.Empty<string>();

            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = (string)list[i];
            return result;
        }

        private static int[] GetIntArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object v) || !(v is List<object> list))
                return Array.Empty<int>();

            var result = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                double d = Convert.ToDouble(list[i], CultureInfo.InvariantCulture);

                // Review LOW-4: coins are integral (GDD §6.1) -- a fractional
                // entry is authoring corruption; fail loudly instead of the
                // previous silent (int) truncation (1.5 -> 1).
                if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
                    throw new FormatException($"{key}[{i}]={d} is not a whole number within Int32 range.");

                result[i] = (int)d;
            }
            return result;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            if (obj.TryGetValue(key, out object v) && v is Dictionary<string, object> nested)
                return nested;
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private static Dictionary<string, string> GetStringDictionary(Dictionary<string, object> obj, string key)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (obj.TryGetValue(key, out object v) && v is Dictionary<string, object> nested)
                foreach (KeyValuePair<string, object> kv in nested)
                    result[kv.Key] = kv.Value as string;
            return result;
        }

        private static StageProfile GetStageProfile(Dictionary<string, object> obj, string key)
        {
            if (obj.TryGetValue(key, out object v) && v is Dictionary<string, object> nested)
            {
                return new StageProfile(
                    camera: GetString(nested, "camera"),
                    environment: GetString(nested, "environment"),
                    entityPrefabSet: GetString(nested, "entityPrefabSet"))
                {
                    // Additive (TASK-027): absent on JSON written before this ticket --
                    // GetPlaneMapping defaults it, so old payloads still deserialize.
                    PlaneMapping = GetPlaneMapping(nested),
                };
            }
            return new StageProfile(string.Empty, string.Empty, string.Empty);
        }

        private static PlaneMapping GetPlaneMapping(Dictionary<string, object> stageProfileObj)
        {
            if (!stageProfileObj.TryGetValue("planeMapping", out object v) || !(v is Dictionary<string, object> nested))
                return new PlaneMapping();

            float[] worldSize = GetFloatArray(nested, "worldSize");
            float[] worldOrigin = GetFloatArray(nested, "worldOrigin");

            return new PlaneMapping(
                worldSizeX: worldSize.Length > 0 ? worldSize[0] : PlaneMapping.DefaultWorldSize,
                worldSizeZ: worldSize.Length > 1 ? worldSize[1] : PlaneMapping.DefaultWorldSize,
                worldOriginX: worldOrigin.Length > 0 ? worldOrigin[0] : 0f,
                worldOriginY: worldOrigin.Length > 1 ? worldOrigin[1] : 0f,
                worldOriginZ: worldOrigin.Length > 2 ? worldOrigin[2] : 0f);
        }

        private static float[] GetFloatArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object v) || !(v is List<object> list))
                return Array.Empty<float>();

            var result = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = (float)Convert.ToDouble(list[i], CultureInfo.InvariantCulture);
            return result;
        }
    }
}
