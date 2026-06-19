namespace Barcade.Core
{
    /// <summary>
    /// Contract every microgame logic class must implement.
    ///
    /// Lifecycle (called in order each round):
    ///   1. <see cref="Prepare"/>   — reset state, seed objects from context RNG.
    ///   2. <see cref="CommandVerb"/> — the verb string shown to players (e.g. "¡TOCA!").
    ///   3. <see cref="Tick"/>      — advance simulation one frame; called once per update.
    ///   4. <see cref="Evaluate"/>  — compute and return per-player win/lose results.
    ///   5. <see cref="Cleanup"/>   — tear down any state; called even after early termination.
    ///
    /// All methods are pure C# — no UnityEngine dependency.
    /// The Unity adapter (MicrogameBehaviour) calls these from MonoBehaviour lifecycle events.
    /// </summary>
    public interface IMicrogame
    {
        /// <summary>
        /// Initialises the microgame for a new round.
        /// Called before the first <see cref="Tick"/>.
        /// </summary>
        void Prepare(IMicrogameContext ctx);

        /// <summary>
        /// The action verb displayed to players at the start of the round.
        /// Example: "¡TOCA!" (Spanish) or "TAP!" (English fallback).
        /// Must be non-null and non-empty after <see cref="Prepare"/> is called;
        /// implementations may return a constant even before Prepare.
        /// </summary>
        string CommandVerb { get; }

        /// <summary>
        /// Advances the microgame simulation by one step.
        /// Called every game-loop frame between <see cref="Prepare"/> and <see cref="Evaluate"/>.
        /// </summary>
        /// <param name="deltaTime">Seconds elapsed since the previous Tick.</param>
        /// <param name="inputs">Snapshot of all players' input this frame.</param>
        void Tick(float deltaTime, IReadOnlyPlayerInputs inputs);

        /// <summary>
        /// Computes and returns the final per-player win/lose result for this round.
        /// Called once after all <see cref="Tick"/> calls for the round complete.
        /// </summary>
        MicrogameResult Evaluate();

        /// <summary>
        /// Tears down any state allocated during <see cref="Prepare"/>.
        /// Called after <see cref="Evaluate"/>; must be safe to call even if
        /// the round was cut short.
        /// </summary>
        void Cleanup();
    }
}
