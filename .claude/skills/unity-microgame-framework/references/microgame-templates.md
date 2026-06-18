# Microgame Framework — Reference Templates

Full C# shapes and extended notes. Loaded on demand; do not inline into SKILL.md.

---

## MicrogameDefinition ScriptableObject

```csharp
// Assets/Barcade/Framework/MicrogameDefinition.cs
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(
    fileName = "MicrogameDef_",
    menuName  = "Barcade/MicrogameDefinition")]
public class MicrogameDefinition : ScriptableObject
{
    [Header("Identity")]
    public string id;               // e.g. "dodge_the_ball"
    [TextArea] public string verbEs; // e.g. "¡ESQUIVA!"
    [TextArea] public string verbEn; // fallback

    [Header("Timing")]
    [Range(2f, 8f)] public float baseDuration = 5f;
    // Actual duration = baseDuration / speedMultiplier (applied by sequencer)

    [Header("Difficulty")]
    [Range(1, 3)] public int difficulty = 1;

    [Header("Content")]
    // Tag the microgame scene in Addressables with its id.
    public AssetReference microgameScene;
    // OR use a prefab root (set scene null if prefab-only):
    public AssetReference microgamePrefab;

    [Header("Audio")]
    public AudioClip previewClip;   // short loop during intermission
}
```

**Why both scene and prefab refs?** Scenes handle complex lighting/physics setups;
prefabs are simpler for geometric-shape microgames. Prefer prefab for milestone 1
(see SKILL.md §Scene vs Prefab decision).

---

## IMicrogame Interface (pure C#)

```csharp
// Assets/Barcade/Framework/IMicrogame.cs
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Contract every microgame root MonoBehaviour must implement.
/// Win/lose rules live in subclasses of MicrogameBase, not here.
/// </summary>
public interface IMicrogame
{
    /// Called before the "GO!" flash. Spawn objects, reset state.
    void Prepare(MicrogameContext ctx);

    /// Called when the timer starts. Return when the microgame ends
    /// (by time-out or early resolution). Cancelled on timeout.
    UniTask Play(CancellationToken ct);

    /// Read per-player results after Play completes.
    bool[] GetResults(); // index 0-3, true = win

    /// Tear down spawned objects. Called even if cancelled.
    void Cleanup();
}
```

> `UniTask` comes from [Cysharp/UniTask](https://github.com/Cysharp/UniTask).
> Prefer it over Coroutines for awaitable microgame lifecycles.
> If UniTask is not yet in the project, use `Task` from `System.Threading.Tasks`
> or a coroutine with a callback — the interface is the same shape.

---

## MicrogameBase MonoBehaviour (abstract)

```csharp
// Assets/Barcade/Framework/MicrogameBase.cs
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(MicrogameInputBridge))]
public abstract class MicrogameBase : MonoBehaviour, IMicrogame
{
    protected MicrogameContext Ctx { get; private set; }
    private bool[] _results = new bool[4];
    protected bool[] Results => _results;

    // ── IMicrogame ──────────────────────────────────────────────

    public void Prepare(MicrogameContext ctx)
    {
        Ctx = ctx;
        _results = new bool[ctx.PlayerCount];
        OnPrepare();
    }

    public async UniTask Play(CancellationToken ct)
    {
        await OnPlay(ct);
    }

    public bool[] GetResults() => _results;

    public void Cleanup() => OnCleanup();

    // ── Override surface ────────────────────────────────────────

    protected virtual void OnPrepare() { }
    protected abstract UniTask OnPlay(CancellationToken ct);
    protected virtual void OnCleanup() { }

    // ── Helpers ─────────────────────────────────────────────────

    protected void SetResult(int playerIndex, bool win)
        => _results[playerIndex] = win;

    protected void SetAllResults(bool win)
    {
        for (int i = 0; i < _results.Length; i++)
            _results[i] = win;
    }
}
```

---

## MicrogameContext (plain C# data bag)

```csharp
// Assets/Barcade/Framework/MicrogameContext.cs
public class MicrogameContext
{
    public int   PlayerCount;       // always 4 for barcade
    public float TimeLimit;         // baseDuration / speedMultiplier
    public int   RoundNumber;       // used to seed per-microgame difficulty
    public float SpeedMultiplier;
    public MicrogameDefinition Definition;
}
```

---

## MicrogameSequencer (MonoBehaviour, manager scene)

```csharp
// Assets/Barcade/Framework/MicrogameSequencer.cs
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class MicrogameSequencer : MonoBehaviour
{
    [Header("Microgame Registry")]
    [SerializeField] private List<MicrogameDefinition> _pool;

    [Header("Pacing")]
    [SerializeField] private float _baseSpeed        = 1f;
    [SerializeField] private float _speedIncrement   = 0.05f;  // per round
    [SerializeField] private float _maxSpeed         = 2.5f;

    [Header("Intermission")]
    [SerializeField] private IntermissionController _intermission;
    [SerializeField] private CommandDisplay         _commandDisplay;

    private ScoreModel       _scores      = new ScoreModel(4);
    private int              _roundNumber = 0;
    private float            _speed       => Mathf.Min(_baseSpeed + _roundNumber * _speedIncrement, _maxSpeed);
    private int              _lastIndex   = -1;
    private GameObject       _activeMicrogameRoot;
    private AsyncOperationHandle<SceneInstance> _activeSceneHandle;
    private bool             _usingScene;

    private void Start() => RunLoop(destroyCancellationToken).Forget();

    private async UniTaskVoid RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1. Pick
            MicrogameDefinition def = PickNext();

            // 2. Intermission
            await _intermission.PlayAsync(def, ct);

            // 3. Show verb
            float timeLimit = def.baseDuration / _speed;
            await _commandDisplay.ShowAsync(def.verbEs, timeLimit, ct);

            // 4. Load microgame
            IMicrogame game = await LoadMicrogame(def, ct);
            if (ct.IsCancellationRequested) break;

            // 5. Prepare + Play
            var ctx = new MicrogameContext
            {
                PlayerCount     = 4,
                TimeLimit       = timeLimit,
                RoundNumber     = _roundNumber,
                SpeedMultiplier = _speed,
                Definition      = def,
            };
            game.Prepare(ctx);

            using var timeout = new CancellationTokenSource();
            timeout.CancelAfter((int)(timeLimit * 1000));
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await game.Play(linked.Token);

            // 6. Collect results
            bool[] results = game.GetResults();
            _scores.Record(results);

            // 7. Feedback (brief flash — implement in IntermissionController)
            await _intermission.ShowResultsAsync(results, ct);

            // 8. Cleanup + unload
            game.Cleanup();
            await UnloadMicrogame();

            _roundNumber++;
        }
    }

    // ── Selection ───────────────────────────────────────────────

    private MicrogameDefinition PickNext()
    {
        if (_pool.Count == 1) return _pool[0];
        int idx;
        do { idx = Random.Range(0, _pool.Count); }
        while (idx == _lastIndex);
        _lastIndex = idx;
        return _pool[idx];
    }

    // ── Load / Unload ────────────────────────────────────────────

    private async UniTask<IMicrogame> LoadMicrogame(MicrogameDefinition def, CancellationToken ct)
    {
        if (def.microgameScene != null && def.microgameScene.RuntimeKeyIsValid())
        {
            _usingScene = true;
            _activeSceneHandle = Addressables.LoadSceneAsync(
                def.microgameScene, LoadSceneMode.Additive);
            await _activeSceneHandle.ToUniTask(cancellationToken: ct);
            // The scene's root must have a component implementing IMicrogame.
            Scene loaded = _activeSceneHandle.Result.Scene;
            foreach (var root in loaded.GetRootGameObjects())
            {
                var mg = root.GetComponent<IMicrogame>();
                if (mg != null) return mg;
            }
            throw new System.Exception($"No IMicrogame found in scene {def.id}");
        }
        else
        {
            _usingScene = false;
            var handle = Addressables.InstantiateAsync(def.microgamePrefab);
            await handle.ToUniTask(cancellationToken: ct);
            _activeMicrogameRoot = handle.Result;
            return _activeMicrogameRoot.GetComponent<IMicrogame>();
        }
    }

    private async UniTask UnloadMicrogame()
    {
        if (_usingScene)
        {
            await Addressables.UnloadSceneAsync(_activeSceneHandle).ToUniTask();
        }
        else if (_activeMicrogameRoot != null)
        {
            Addressables.ReleaseInstance(_activeMicrogameRoot);
            _activeMicrogameRoot = null;
        }
        // Free asset memory from unloaded scene
        await Resources.UnloadUnusedAssets();
    }
}
```

---

## ScoreModel (pure C#, unit-testable)

```csharp
// Assets/Barcade/Framework/ScoreModel.cs
public class ScoreModel
{
    private readonly int[] _wins;
    private readonly int[] _losses;
    private readonly int   _playerCount;

    public ScoreModel(int playerCount)
    {
        _playerCount = playerCount;
        _wins   = new int[playerCount];
        _losses = new int[playerCount];
    }

    public void Record(bool[] results)
    {
        for (int i = 0; i < _playerCount; i++)
        {
            if (results[i]) _wins[i]++;
            else            _losses[i]++;
        }
    }

    public int   Wins(int player)       => _wins[player];
    public int   Losses(int player)     => _losses[player];
    public int   TotalRounds(int p)     => _wins[p] + _losses[p];
    public float WinRate(int player)    => TotalRounds(player) == 0 ? 0f
                                          : (float)_wins[player] / TotalRounds(player);
    public int   LeadingPlayer()
    {
        int best = 0;
        for (int i = 1; i < _playerCount; i++)
            if (_wins[i] > _wins[best]) best = i;
        return best;
    }
}
```

---

## Minimal Example Microgame ("Dodge the Ball")

```csharp
// Assets/Barcade/Microgames/DodgeBall/DodgeBallMicrogame.cs
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class DodgeBallMicrogame : MicrogameBase
{
    [SerializeField] private Transform[] _playerSlots;   // 4 positions
    [SerializeField] private GameObject  _ballPrefab;

    private GameObject   _ball;
    private bool[]       _alive; // tracks per-player alive state

    protected override void OnPrepare()
    {
        _alive = new bool[Ctx.PlayerCount];
        for (int i = 0; i < Ctx.PlayerCount; i++) _alive[i] = true;
        _ball  = Instantiate(_ballPrefab);
    }

    protected override async UniTask OnPlay(CancellationToken ct)
    {
        // Run until timeout (ct cancelled) or all players resolved
        while (!ct.IsCancellationRequested)
        {
            // Check collisions — pure transform math, no physics required
            for (int i = 0; i < Ctx.PlayerCount; i++)
            {
                if (!_alive[i]) continue;
                float dist = Vector3.Distance(_ball.transform.position,
                                              _playerSlots[i].position);
                if (dist < 0.5f)
                {
                    _alive[i] = false;
                    SetResult(i, false); // hit = loss
                }
            }
            // Survivors at timeout win
            bool anyAlive = false;
            foreach (bool a in _alive) if (a) { anyAlive = true; break; }
            if (!anyAlive) break;

            await UniTask.Yield(ct);
        }
        // Everyone still alive at timeout wins
        for (int i = 0; i < Ctx.PlayerCount; i++)
            if (_alive[i]) SetResult(i, true);
    }

    protected override void OnCleanup()
    {
        if (_ball != null) Destroy(_ball);
    }
}
```

---

## EditMode Tests (ScoreModel)

```csharp
// Assets/Tests/EditMode/ScoreModelTests.cs
using NUnit.Framework;

public class ScoreModelTests
{
    [Test]
    public void Record_SingleWin_IncreasesWinsOnly()
    {
        var sm = new ScoreModel(4);
        sm.Record(new[] { true, false, false, false });
        Assert.AreEqual(1, sm.Wins(0));
        Assert.AreEqual(1, sm.Losses(1));
        Assert.AreEqual(0, sm.Wins(1));
    }

    [Test]
    public void WinRate_AfterTwoRounds_IsCorrect()
    {
        var sm = new ScoreModel(2);
        sm.Record(new[] { true, false });
        sm.Record(new[] { true, true  });
        Assert.AreEqual(1f,   sm.WinRate(0), 0.001f);
        Assert.AreEqual(0.5f, sm.WinRate(1), 0.001f);
    }

    [Test]
    public void LeadingPlayer_ReturnsIndexWithMostWins()
    {
        var sm = new ScoreModel(4);
        sm.Record(new[] { false, false, true, false });
        sm.Record(new[] { false, false, true, false });
        Assert.AreEqual(2, sm.LeadingPlayer());
    }
}
```

Assembly definition for tests must reference `Barcade.Framework` and include
`"testPlatforms": ["EditMode"]` in the .asmdef.

---

## Input Bridge Stub

```csharp
// Assets/Barcade/Framework/MicrogameInputBridge.cs
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Translates PlayerInput events into a simple poll-able state.
/// Attach to the microgame root. PlayerInput components live on persistent
/// player prefabs in the Manager scene; they send messages via SendMessage
/// or Unity Events — configure PlayerInput.notificationBehavior =
/// PlayerNotifications.SendMessages and add this component as a receiver,
/// OR use the static event PlayerInput.all to iterate.
/// </summary>
public class MicrogameInputBridge : MonoBehaviour
{
    // Indexed by player slot 0-3
    private readonly Vector2[] _stickValues = new Vector2[4];
    private readonly bool[]    _buttonDown  = new bool[4];

    public Vector2 Stick(int player)  => _stickValues[player];
    public bool    Button(int player) => _buttonDown[player];

    // Called by PlayerInput SendMessages (action name "Move")
    public void OnMove(InputValue v)    { /* route by player index */ }
    public void OnAction(InputValue v)  { /* route by player index */ }

    private void Update()
    {
        // Clear frame-transient states
        for (int i = 0; i < 4; i++) _buttonDown[i] = false;
    }
}
```

> Full per-player routing using `PlayerInput.all` is documented in the Unity
> Input System manual under "The PlayerInput component."
