using System;
using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    // ── Registry types ────────────────────────────────────────────────────────────

    /// <summary>
    /// Factory pair that creates an <see cref="IMicrogame"/> and its matching view
    /// for a given microgame id. Both factories must be non-null.
    /// </summary>
    public sealed class MicrogameEntry
    {
        public readonly Func<IMicrogame>     CreateLogic;
        public readonly Action<IMicrogame, PlayerSlot[], GameObject> AttachView;

        public MicrogameEntry(
            Func<IMicrogame> createLogic,
            Action<IMicrogame, PlayerSlot[], GameObject> attachView)
        {
            CreateLogic = createLogic ?? throw new ArgumentNullException(nameof(createLogic));
            AttachView  = attachView  ?? throw new ArgumentNullException(nameof(attachView));
        }
    }

    /// <summary>
    /// Registry mapping microgame id strings to <see cref="MicrogameEntry"/> pairs.
    /// Populated at startup — no Addressables required.
    /// </summary>
    public sealed class MicrogameRegistry
    {
        private readonly Dictionary<string, MicrogameEntry> _entries =
            new Dictionary<string, MicrogameEntry>(StringComparer.Ordinal);

        /// <summary>Registers a microgame entry. Overwrites any existing entry with the same id.</summary>
        public void Register(string id, MicrogameEntry entry)
        {
            if (id == null)    throw new ArgumentNullException(nameof(id));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _entries[id] = entry;
        }

        /// <summary>
        /// Returns true and the entry if the id is registered.
        /// </summary>
        public bool TryGet(string id, out MicrogameEntry entry) =>
            _entries.TryGetValue(id ?? string.Empty, out entry);

        /// <summary>True if the id has a registered entry.</summary>
        public bool Contains(string id) => _entries.ContainsKey(id ?? string.Empty);
    }

    // ── MicrogameHost ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs ONE microgame's full lifecycle for a single round.
    ///
    /// Responsibilities:
    ///   1. Builds an <see cref="IMicrogameContext"/> (deterministic seed, all 4 slots).
    ///   2. Calls <see cref="IMicrogame.Prepare"/> then each frame calls
    ///      <see cref="IMicrogame.Tick"/> with the current <see cref="IReadOnlyPlayerInputs"/>.
    ///   3. Drives the matching view (found via <see cref="MicrogameRegistry"/>).
    ///   4. After <paramref name="playDuration"/> seconds calls <see cref="IMicrogame.Evaluate"/>,
    ///      stores the result, and calls <see cref="IMicrogame.Cleanup"/>.
    ///   5. Exposes the result via <see cref="LastResult"/>.
    ///
    /// Usage (called by MicrogameLoopController):
    /// <code>
    ///   host.StartRound(id, seed, playDuration, inputs);
    ///   // each frame:
    ///   host.Tick(Time.deltaTime);
    ///   if (host.IsComplete) var result = host.LastResult;
    /// </code>
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class MicrogameHost : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Tooltip("Parent transform for view GameObjects spawned per round.")]
        [SerializeField] private Transform _viewParent;

        // ── Registry (injected or built in Awake) ─────────────────────────────────

        private MicrogameRegistry _registry;

        // ── Runtime state ─────────────────────────────────────────────────────────

        private IMicrogame             _currentGame;
        private IReadOnlyPlayerInputs  _inputs;
        private float                  _elapsed;
        private float                  _playDuration;
        private bool                   _running;
        private GameObject             _viewRoot;

        // ── Result ────────────────────────────────────────────────────────────────

        /// <summary>
        /// True when the current round's microgame has finished (Evaluate called).
        /// Resets to false on the next <see cref="StartRound"/> call.
        /// </summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// The per-player result from the most recent completed round.
        /// Null before the first round completes.
        /// </summary>
        public MicrogameResult LastResult { get; private set; }

        // ── Default player set ────────────────────────────────────────────────────

        private static readonly PlayerSlot[] AllPlayers = new PlayerSlot[]
        {
            PlayerSlot.Rojo,
            PlayerSlot.Azul,
            PlayerSlot.Amarillo,
            PlayerSlot.Verde,
        };

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (_registry == null)
                _registry = BuildDefaultRegistry();
        }

        private void Update()
        {
            if (!_running) return;
            Tick(Time.deltaTime);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Injects a custom registry (for testing or runtime configuration).
        /// Must be called before <see cref="StartRound"/>.
        /// </summary>
        public void SetRegistry(MicrogameRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Begins a new microgame round.
        /// </summary>
        /// <param name="microgameId">Id that resolves to a registered entry.</param>
        /// <param name="seed">Deterministic seed for this round's RNG.</param>
        /// <param name="playDuration">Effective play window in seconds.</param>
        /// <param name="inputs">Live input provider; polled each Tick.</param>
        /// <param name="difficulty">
        /// Normalised difficulty [0,1] passed into the microgame context.
        /// Derived from <c>MicrogameDefinition.difficulty</c> mapped [1,3]→[0,1].
        /// Defaults to 0 (easiest) when not provided.
        /// </param>
        public void StartRound(
            string microgameId,
            int seed,
            float playDuration,
            IReadOnlyPlayerInputs inputs,
            float difficulty = 0f)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            StopCurrentRound();

            IsComplete    = false;
            LastResult    = null;
            _elapsed      = 0f;
            _playDuration = Mathf.Max(0f, playDuration);
            _inputs       = inputs;

            if (_registry == null)
                _registry = BuildDefaultRegistry();

            if (!_registry.TryGet(microgameId, out MicrogameEntry entry))
            {
                Debug.LogWarning(
                    $"[MicrogameHost] No registry entry for id '{microgameId}'. " +
                    "Round will complete immediately with all-loss result.");
                CompleteWithAllLoss();
                return;
            }

            // Build the logic instance.
            _currentGame = entry.CreateLogic();

            // Build per-round context (clamp difficulty to [0,1]).
            float clampedDifficulty = difficulty < 0f ? 0f : (difficulty > 1f ? 1f : difficulty);
            var ctx = new MicrogameContext(new SeededRandom(seed), AllPlayers, clampedDifficulty);

            // Prepare.
            _currentGame.Prepare(ctx);

            // Spawn the view root.
            _viewRoot = new GameObject($"MicrogameView_{microgameId}");
            if (_viewParent != null)
                _viewRoot.transform.SetParent(_viewParent, worldPositionStays: false);

            // Attach view.
            entry.AttachView(_currentGame, AllPlayers, _viewRoot);

            _running = true;
        }

        /// <summary>
        /// Advances the microgame simulation by one step.
        /// Called automatically from Update() while running.
        /// Can also be driven manually in tests by setting _running via StartRound.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_running) return;

            _currentGame.Tick(deltaTime, _inputs);
            _elapsed += deltaTime;

            if (_elapsed >= _playDuration)
                FinishRound();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void FinishRound()
        {
            _running = false;

            LastResult = _currentGame.Evaluate();
            _currentGame.Cleanup();
            _currentGame = null;

            if (_viewRoot != null)
            {
                Destroy(_viewRoot);
                _viewRoot = null;
            }

            IsComplete = true;
        }

        private void StopCurrentRound()
        {
            if (!_running) return;

            _running = false;
            _currentGame?.Cleanup();
            _currentGame = null;

            if (_viewRoot != null)
            {
                Destroy(_viewRoot);
                _viewRoot = null;
            }
        }

        private void CompleteWithAllLoss()
        {
            LastResult = BuildAllLossResult();
            IsComplete = true;
        }

        private static MicrogameResult BuildAllLossResult()
        {
            var r = new MicrogameResult(AllPlayers);
            foreach (PlayerSlot slot in AllPlayers)
                r.SetOutcome(slot, false);
            r.Freeze();
            return r;
        }

        // ── Default registry ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds the canonical registry mapping all 5 mechanic ids to their
        /// logic + view factories. Called once in Awake if no registry has been injected.
        /// </summary>
        public static MicrogameRegistry BuildDefaultRegistry()
        {
            var reg = new MicrogameRegistry();

            // ── esquiva ───────────────────────────────────────────────────────────
            reg.Register("esquiva", new MicrogameEntry(
                createLogic: () => new EsquivaMicrogame(),
                attachView:  (game, players, root) =>
                {
                    var view = root.AddComponent<EsquivaView>();
                    view.Bind((EsquivaMicrogame)game, players);
                }
            ));

            // ── aporrea ───────────────────────────────────────────────────────────
            reg.Register("aporrea", new MicrogameEntry(
                createLogic: () => new AporreaMicrogame(threshold: 5, timeLimit: 5f),
                attachView:  (game, players, root) =>
                {
                    var view = root.AddComponent<AporreaView>();
                    view.Bind((AporreaMicrogame)game, players);
                }
            ));

            // ── apunta ────────────────────────────────────────────────────────────
            reg.Register("apunta", new MicrogameEntry(
                createLogic: () => new ApuntaMicrogame(angleTolerance: 15f),
                attachView:  (game, players, root) =>
                {
                    var view = root.AddComponent<ApuntaView>();
                    view.Bind((ApuntaMicrogame)game, players);
                }
            ));

            // ── timing ────────────────────────────────────────────────────────────
            reg.Register("timing", new MicrogameEntry(
                createLogic: () => new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f),
                attachView:  (game, players, root) =>
                {
                    var view = root.AddComponent<TimingView>();
                    view.Bind((TimingMicrogame)game, players, zoneMin: 0.4f, zoneMax: 0.6f);
                }
            ));

            // ── recolecta ─────────────────────────────────────────────────────────
            reg.Register("recolecta", new MicrogameEntry(
                createLogic: () => new RecolectaMicrogame(
                    quota:             3,
                    collectRadius:     1.0f,
                    avatarSpeed:       5f,
                    playAreaHalfExtent: 4.5f),   // reduced so avatars stay within camera orthoSize=5
                attachView: (game, players, root) =>
                {
                    var recolecta = (RecolectaMicrogame)game;

                    // Pass the collectibles seeded during Prepare() so the view
                    // can render them.  Bind is called after Prepare, so the array
                    // is already populated.
                    var collectibles = System.Linq.Enumerable.ToArray(recolecta.Collectibles);
                    var view = root.AddComponent<RecolectaView>();
                    view.Bind(recolecta, players, collectibles);
                }
            ));

            return reg;
        }
    }
}
