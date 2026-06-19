using UnityEngine;

namespace Barcade.Framework
{
    /// <summary>
    /// Designer-facing data asset for a single microgame entry.
    ///
    /// Fields:
    ///   id           — unique machine-readable key (e.g. "sample_tap").
    ///   verbText     — the action command shown to players (e.g. "¡TOCA!").
    ///   baseDuration — round length in seconds (before speed multiplier).
    ///   difficulty   — 1 = easy, 2 = medium, 3 = hard; used by sequencer pool filter.
    ///   prefabAddress — Addressables key (string) for the microgame root prefab.
    ///                   Using a plain string rather than AssetReference avoids a
    ///                   hard Addressables package coupling at this milestone; the
    ///                   sequencer calls Addressables.InstantiateAsync(prefabAddress).
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed) — NOT linked into the
    /// dotnet fast-test runner. Unity compile is verified in the integration pass.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MicrogameDef_",
        menuName  = "Barcade/MicrogameDefinition")]
    public class MicrogameDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id;

        [Header("Command")]
        [TextArea]
        public string verbText;          // e.g. "¡TOCA!" shown to all players

        [Header("Timing")]
        [Range(2f, 8f)]
        public float baseDuration = 5f;  // seconds; actual = baseDuration / speedMultiplier

        [Header("Difficulty")]
        [Range(1, 3)]
        public int difficulty = 1;

        [Header("Addressable Content")]
        // Plain string address — swap for AssetReference when Addressables is integrated.
        public string prefabAddress;
    }
}
