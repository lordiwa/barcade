---
name: unity-microgame-framework
description: >
  Load this skill when: creating or editing a microgame, implementing the
  microgame loop or sequencer, building intermission/transition screens,
  defining a MicrogameDefinition ScriptableObject, working on scoring or
  win/lose logic, or adding any new minigame to the barcade project.
---

# unity-microgame-framework

## When to Use This Skill

Load this skill before touching any of the following:
- `MicrogameDefinition` ScriptableObjects (designer data)
- `MicrogameBase` / `IMicrogame` implementations
- `MicrogameSequencer` (the director/runner)
- Intermission screens and command-verb display
- `ScoreModel` or per-player win/loss tracking
- Adding a brand-new microgame from scratch

---

## Architecture at a Glance

```
Manager Scene (persistent, never unloaded)
├── MicrogameSequencer       — orchestrates the full loop
├── IntermissionController   — rhythmic buffer between games
├── CommandDisplay           — shows the giant verb (e.g. "¡ESQUIVA!")
├── HUD (4 player corners)
└── PlayerInput × 4          — Input System, one per physical controller

Addressable Prefabs (loaded/released per round)
└── [MicrogameRoot]
    ├── MicrogameBase subclass  — lifecycle + win/lose rules
    ├── MicrogameInputBridge    — translates PlayerInput → poll API
    └── Visual geometry (Unity primitives only for M1)
```

### Scene vs Prefab decision — PREFAB for Milestone 1

Use **Addressable prefabs**, not additive scenes, for every microgame during
Milestone 1. Rationale:
- Simple geometric shapes need no per-microgame lighting, audio mixer, or
  physics layer overrides — the shared Manager scene supplies all of that.
- Prefab load/release is faster and simpler to reason about than
  `LoadSceneAsync` / `UnloadSceneAsync` + `UnloadUnusedAssets`.
- `MicrogameDefinition.microgameScene` exists for future complex games;
  leave it null until needed.

Switch to **additive scenes** only when a microgame needs a distinct lighting
setup, separate Physics2D layer matrix, or a large baked navmesh.

---

## Core Workflows

### (1) Add a new microgame end-to-end

**Step 1 — Write the logic class (pure C# first, add MonoBehaviour wrapping after tests pass).**

Create `Assets/Barcade/Microgames/<YourGame>/<YourGame>Microgame.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class YourGameMicrogame : MicrogameBase
{
    // Serialized fields for Unity shapes, speeds, etc.

    protected override void OnPrepare()
    {
        // Spawn objects, reset state. Use Ctx.RoundNumber for seeding.
    }

    protected override async UniTask OnPlay(CancellationToken ct)
    {
        // Main loop: poll input via GetComponent<MicrogameInputBridge>(),
        // move objects, call SetResult(playerIndex, win) when resolved.
        // Returning (or ct cancellation) signals time-out — set survivors' wins.
        while (!ct.IsCancellationRequested)
        {
            // ... game logic ...
            await UniTask.Yield(ct);
        }
        // finalise any unresolved players here
    }

    protected override void OnCleanup()
    {
        // Destroy any Instantiate'd objects
    }
}
```

**Step 2 — Write EditMode tests for win/lose rules before wiring in Unity.**

```csharp
// Assets/Tests/EditMode/<YourGame>Tests.cs
[Test]
public void PlayerHit_SetsLoss()
{
    // Create a plain C# helper that encapsulates the rule,
    // then assert. Keep Unity dependencies out of this layer.
}
```

**Step 3 — Build the prefab.**

- Create `Assets/Barcade/Microgames/<YourGame>/<YourGame>.prefab`.
- Root GameObject: add `YourGameMicrogame` + `MicrogameInputBridge`.
- Child GameObjects: Unity primitives only (Cube, Sphere, Cylinder, Quad).
  Use `[SerializeField]` references — no `GameObject.Find`.
- Mark the prefab as Addressable with label `microgame`.

**Step 4 — Create the ScriptableObject.**

`Assets/Barcade/Data/Microgames/MicrogameDef_<YourGame>.asset`

```
id:            your_game
verbEs:        ¡VERBO!
verbEn:        VERB!
baseDuration:  5
difficulty:    1
microgamePrefab: [AssetReference → your prefab]
```

**Step 5 — Register in sequencer.**

Drag `MicrogameDef_<YourGame>` into `MicrogameSequencer._pool` in the
Manager scene Inspector. Done.

---

### (2) Wire a microgame into the sequencer

The sequencer calls the lifecycle in this order every round:

```
PickNext()          → MicrogameDefinition selected (anti-repeat RNG)
IntermissionController.PlayAsync()  → rhythmic buffer + preview audio
CommandDisplay.ShowAsync()          → giant verb displayed
Addressables.InstantiateAsync()     → prefab loaded
IMicrogame.Prepare(ctx)             → objects spawned, state reset
IMicrogame.Play(ct)                 → game runs until ct cancelled or resolved
IMicrogame.GetResults()             → bool[4] collected
IMicrogame.Cleanup()                → objects destroyed
Addressables.ReleaseInstance()      → prefab memory released
ScoreModel.Record(results)          → persistent scores updated
IntermissionController.ShowResultsAsync() → per-player win/loss flash
```

`MicrogameContext` carries `TimeLimit = def.baseDuration / speedMultiplier`,
`RoundNumber`, and `SpeedMultiplier` into the microgame. Read `Ctx` inside
`MicrogameBase`; do not re-read `Time.time` or hardcode durations.

---

### (3) Tune difficulty / variance

**Speed ramp** — `MicrogameSequencer._speedIncrement` (default 0.05 per round).
The effective time limit shrinks automatically. Adjust per-playtesting.

**Per-game difficulty** — use `Ctx.RoundNumber` inside `OnPrepare()` to seed
harder spawns, faster projectiles, or tighter tolerances. Keep the branch count
low (e.g., `easy if round < 10, hard otherwise`).

**Pool composition** — register multiple `MicrogameDefinition` assets for the
same mechanic at different `difficulty` values and filter in `PickNext()` by
`RoundNumber` thresholds to shift the distribution over a session.

**Scoring variance** — `ScoreModel` tracks raw wins/losses per player. A player
can win a round through luck but the skilled player accumulates wins over many
rounds (law of large numbers). Do NOT add bonus multipliers or nested random
events on top of the basic W/L record — that defeats the variance design goal.

---

## Best Practices

- **No Unity APIs in win/lose logic.** Put rules in plain C# methods or
  helper classes; call them from `OnPlay`. This enables EditMode tests.
- **No `Awake()` for spawn logic.** NitoriWare hard-learned this: use
  `OnPrepare()` (mapped to `Start`-equivalent timing), not `Awake`.
- **One asset per microgame.** One `.cs` + one `.prefab` + one `.asset`.
  No sub-scenes, no nested ScriptableObjects, no singletons inside a microgame.
- **CancellationToken discipline.** Pass `ct` through every `await`. When
  `ct` fires, the sequencer is ending the round; call `SetResult` for survivors
  then let `OnPlay` return cleanly — do not throw.
- **Input is read-only inside microgames.** `MicrogameInputBridge` provides
  `Stick(playerIndex)` and `Button(playerIndex)`. Never call `Input.GetKey`
  or add a second `PlayerInput` component.
- **Addressables labels.** Tag all microgame prefabs `microgame` so a future
  remote-update pipeline can refresh just that group.
- **Assembly definitions.** `Barcade.Framework` asmdef covers all framework
  types. Each microgame's folder gets its own asmdef referencing
  `Barcade.Framework`. Tests live in `Barcade.Tests.EditMode` (no Unity engine
  reference, only `Barcade.Framework`).

---

## Common Pitfalls

| Pitfall | Fix |
|---|---|
| `Awake()` runs before sequencer Prepare | Move all spawn logic to `OnPrepare()` |
| Microgame holds a reference after Cleanup | Null checks + `Destroy()` in `OnCleanup` |
| RNG same seed → same first game every session | Seed `Random.InitState(System.Environment.TickCount)` once at app start |
| Two players share a `PlayerInput` | Each player gets its own `PlayerInput` instance via `PlayerInputManager` |
| `TimeLimit` ignored, microgame loops forever | Always respect `ct.IsCancellationRequested` in the `while` loop |
| Nested random (random speed AND random count) | Pick ONE random axis per microgame; others are fixed |
| SpeedMultiplier not applied | Always derive `timeLimit` from `Ctx.TimeLimit`, not `def.baseDuration` directly |

---

## Verification

Before merging any new microgame:

1. **EditMode tests pass** — `Window > General > Test Runner > EditMode > Run All`.
2. **Prefab loads and unloads** — run the Manager scene, skip to the new game
   via Inspector, confirm no memory leak in Profiler after 3 cycles.
3. **4-player input** — connect 4 controllers; confirm each only controls its own slot.
4. **Verb readable at 2 m** — display the verb fullscreen, stand back, squint.
   If it needs a second read, make it bigger or simpler.
5. **5-second ceiling** — time the longest possible round with `baseDuration = 5`
   and `speedMultiplier = 1`. Confirm it ends.
6. **Speed ramp** — set `_roundNumber = 30` in Inspector play mode; confirm
   `Ctx.TimeLimit` has shrunk meaningfully and the game still resolves cleanly.

---

## References

Full C# class bodies, complete test suite stubs, and InputBridge wiring:
- `references/microgame-templates.md` — `IMicrogame`, `MicrogameBase`,
  `MicrogameDefinition`, `MicrogameSequencer`, `ScoreModel`, example microgame,
  EditMode test suite, `MicrogameInputBridge` stub.

---

## Provenance

- Researcher: Researcher subagent (claude-sonnet-4-6)
- Ticket: BOOTSTRAP-UNITY
- Last verified: 2026-06-18

### Sources

- [NitoriWare CONTRIBUTING-MICROGAME.md](https://github.com/NitorInc/NitoriWare/blob/develop/CONTRIBUTING-MICROGAME.md) — battle-tested WarioWare Unity architecture; validates ScriptableObject-per-game + per-scene approach; informed anti-Awake rule.
- [Unity SceneManager.LoadSceneAsync docs](https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html) — confirmed `AsyncOperation` return type, `LoadSceneMode.Additive` signature.
- [Unity SceneManager.UnloadSceneAsync docs](https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html) — confirmed 6 overloads, `UnloadSceneOptions`, note on `Resources.UnloadUnusedAssets`.
- [Unity Input System — PlayerInput component](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/PlayerInput.html) — `PlayerInputManager` for 4-player pairing, `currentActionMap`, `SwitchCurrentActionMap`.
- [Unity Addressables LoadAssetAsync docs](https://docs.unity3d.com/Packages/com.unity.addressables@1.15/manual/LoadingAddressableAssets.html) — `AssetReference`, remote bundle fetch pattern for remote-updatable cabinets.
- [Unity Test Framework — EditMode tests](https://docs.unity3d.com/6000.4/Documentation/Manual/test-framework/edit-mode-vs-play-mode-tests.html) — asmdef requirements, `[Test]` vs `[UnityTest]`, pure C# isolation.
- [Unity forums: additive scene vs prefab instantiate performance](https://discussions.unity.com/t/performance-differences-in-loadlevelasync-additive-vs-prefab-instantiate/685813) — confirmed prefab is faster for simple content; scene preferred for complex setups.
- [Cysharp/UniTask](https://github.com/Cysharp/UniTask) — async microgame lifecycle without coroutine overhead.
