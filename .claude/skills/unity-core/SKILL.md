---
name: unity-core
description: >
  Load this skill before any Unity C# work on the barcade project. Covers project
  setup, folder layout, Assembly Definition (.asmdef) structure, MonoBehaviour
  lifecycle, prefabs, ScriptableObjects, scene management, game-state bootstrapping,
  2D geometric rendering, and the thin-MonoBehaviour architecture pattern.
  Trigger files: .cs, .asmdef, .unity, .prefab, .asset, and any time the words
  ScriptableObject, MonoBehaviour, Assembly Definition, or Input System appear.
---

# unity-core

## When to Use This Skill

- Starting or restructuring a Unity project for barcade.
- Writing or reviewing any `.cs`, `.asmdef`, `.unity`, `.prefab`, or `.asset` file.
- Designing a new system that touches MonoBehaviour, ScriptableObject, scene loading,
  or the Input System.
- Adding or wiring a new microgame skeleton (pair with `unity-microgame-framework`).
- Choosing how to render geometric shapes without art assets.


## Core Workflows

### 1. Verified Unity Version

**Use Unity 6.3 LTS — version string `6000.3.18f1` (latest patch as of 2026-06-18).**
Install via Unity Hub; select "Unity 6.3 LTS" stream.
Unity 6.3 LTS receives security fixes until December 2027 (extended to December 2028
for Enterprise). Do not use Unity 6.0 LTS (EOL October 2026) or the non-LTS 6.4 stream
for a production cabinet.

```
# Install from Hub CLI (optional, for CI)
unity-hub install --version 6000.3.18f1 --module windows-il2cpp
```

---

### 2. Folder Structure & Assembly Definitions

```
Assets/
  Barcade/
    Core/                     # Barcade.Core.asmdef  (pure C#, no UnityEngine refs except allowed)
      Runtime/
        Scoring/
        RNG/
        Input/                # input state data structs only
      Editor/                 # Barcade.Core.Editor.asmdef
      Tests/
        EditMode/             # Barcade.Core.Tests.EditMode.asmdef
        PlayMode/             # Barcade.Core.Tests.PlayMode.asmdef
    Framework/                # Barcade.Framework.asmdef  (MonoBehaviour layer)
      MicrogameBase.cs
      MicrogameSequencer.cs
      MicrogameDefinition.cs  # ScriptableObject
    Microgames/               # one subfolder per microgame
      DodgeBall/
    HUD/
    Input/                    # InputActionAsset, PlayerInput prefabs
  Plugins/                    # third-party (UniTask, etc.)
  Scenes/
    Boot.unity                # entry point — loads Manager additively
    Manager.unity             # persists for full session
  ScriptableObjects/          # runtime SO assets (.asset files)
  Settings/
    InputSystem_Actions.inputactions
```

**Barcade.Core.asmdef** — pure game logic, no MonoBehaviour coupling:

```json
{
  "name": "Barcade.Core",
  "rootNamespace": "Barcade.Core",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true
}
```

**Barcade.Core.Tests.EditMode.asmdef** — EditMode unit tests:

```json
{
  "name": "Barcade.Core.Tests.EditMode",
  "references": ["Barcade.Core"],
  "optionalUnityReferences": ["TestAssemblies"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": []
}
```

**Barcade.Framework.asmdef** — MonoBehaviour/engine layer, references Core:

```json
{
  "name": "Barcade.Framework",
  "rootNamespace": "Barcade.Framework",
  "references": ["Barcade.Core"],
  "includePlatforms": [],
  "excludePlatforms": []
}
```

Rule: `Barcade.Core` never references `Barcade.Framework`. Data flows up via interfaces.

---

### 3. Bootstrap / Game-State Pattern

Boot.unity loads at startup (index 0 in Build Settings). It creates the single
`GameBootstrapper` MonoBehaviour, which loads Manager.unity additively, then
unloads Boot itself. Nothing else lives in Boot.

```csharp
// Assets/Barcade/Framework/GameBootstrapper.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private string _managerScene = "Manager";

    private IEnumerator Start()
    {
        // Keep this object alive across the additive load
        DontDestroyOnLoad(gameObject);

        var op = SceneManager.LoadSceneAsync(_managerScene, LoadSceneMode.Additive);
        yield return op;

        // Hand off to GameManager in the loaded scene, then destroy self
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(_managerScene));
        Destroy(gameObject); // Boot done; Manager owns the loop
    }
}
```

`GameManager` (in Manager.unity) is a plain `MonoBehaviour` marked
`DontDestroyOnLoad`. It holds references to `MicrogameSequencer`,
`HUDController`, and the four `PlayerInput` prefab instances.
Prefer passing explicit references over static singletons.

---

### 4. ScriptableObjects for Microgame Config

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
    public string id;
    [TextArea] public string verbEs;  // "¡ESQUIVA!"
    [TextArea] public string verbEn;

    [Header("Timing")]
    [Range(2f, 8f)] public float baseDuration = 5f;

    [Header("Difficulty")]
    [Range(1, 3)] public int difficulty = 1;

    [Header("Addressable Content")]
    public AssetReference microgamePrefab;  // preferred for geometric microgames
    public AssetReference microgameScene;   // for complex physics/lighting setups
}
```

Create assets via **Assets > Create > Barcade > MicrogameDefinition**.
Register them in `MicrogameSequencer._pool` via the Inspector.
See `references/official-docs-snapshot.md` for extended SO guidance.

---

### 5. Microgame Scene Loading (Additive / Prefab)

For milestone 1 geometric microgames, prefer **Addressable prefab instantiation**
over additive scene loading — simpler memory management, no scene build-index bookkeeping.

```csharp
// Pseudocode — full version in unity-microgame-framework skill references
var handle = Addressables.InstantiateAsync(def.microgamePrefab);
await handle.Task;
IMicrogame game = handle.Result.GetComponent<IMicrogame>();
// ... play ...
Addressables.ReleaseInstance(handle.Result); // unloads automatically
```

Use **additive scene loading** only when a microgame needs its own lighting or
physics settings:

```csharp
var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
// yield return op or await with UniTask
// Unload when done:
SceneManager.UnloadSceneAsync(sceneName);
```

---

### 6. 4-Player Input (Unity Input System)

Package: `com.unity.inputsystem` (already in Unity 6.3 default packages).

- Create one `InputActionAsset` (`.inputactions`) with a single action map named
  `Gameplay` containing two actions: `Move` (Value, Vector2) and `Action` (Button).
- Create a `PlayerPrefab` with a `PlayerInput` component pointing at that asset.
- Use `PlayerInputManager` with **Join Behavior = Join Players Manually** for
  an arcade cabinet with four fixed joysticks:

```csharp
// Assets/Barcade/Input/PlayerSpawner.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private PlayerInputManager _manager;
    [SerializeField] private InputDevice[]      _devices; // assign 4 joysticks in Inspector

    private void Start()
    {
        for (int i = 0; i < 4; i++)
            _manager.JoinPlayer(playerIndex: i, splitScreenIndex: -1,
                                controlScheme: "Joystick", pairWithDevice: _devices[i]);
    }
}
```

Set `PlayerInput.notificationBehavior = PlayerNotifications.InvokeUnityEvents`
and wire per-player event handlers in each player prefab instance.

---

### 7. 2D Geometric Rendering (No Art Assets)

Milestone 1 uses **Unity primitives + SpriteRenderer** — no external art required.

| Need | Approach |
|---|---|
| Filled rectangle / square | `GameObject.CreatePrimitive(PrimitiveType.Quad)`, scale to size |
| Filled circle | `GameObject.CreatePrimitive(PrimitiveType.Sphere)` with orthographic camera |
| Colored shape | Set `MeshRenderer.material.color` on a default Unlit/Color material |
| Thin line | `LineRenderer` component, set `startWidth` / `endWidth` |
| Sprite-based shape | Create a 1x1 white PNG, use `SpriteRenderer`; tint at runtime |
| Procedural polygon | Generate a `Mesh` with `vertices` / `triangles` arrays at runtime |

Use an orthographic camera (Size = half of play-area height in world units).
Player colors: Red `#E8212B`, Blue `#1E5FBE`, Yellow `#F5C400`, Green `#2BA84A`.

---

### 8. Thin-MonoBehaviour Principle

Keep all rules, scoring, and RNG in **plain C# classes** (no `MonoBehaviour`).
MonoBehaviours are thin wrappers that relay Unity lifecycle events into those classes.

```
                ┌──────────────────────────────────┐
  Unity loop →  │  SomeMicrogameMono : MonoBehaviour│  (thin adapter)
                │  Update() → _logic.Tick(dt)       │
                └──────────┬───────────────────────┘
                           │ calls
                ┌──────────▼───────────────────────┐
                │  SomeMicrogameLogic : plain C#    │  (testable, no Unity dep)
                │  Tick(float dt), CheckWin() ...   │
                └──────────────────────────────────┘
```

EditMode tests target `SomeMicrogameLogic` directly — no scene required.
PlayMode tests handle input/physics integration.


## Best Practices

| Do | Don't | Rationale |
|---|---|---|
| Put rules/scoring/RNG in plain C# classes | Put game logic inside `MonoBehaviour.Update` | Plain classes are NUnit-testable without a running scene |
| Use `[CreateAssetMenu]` ScriptableObjects for microgame config | Hardcode timings / difficulty in Prefab fields | SOs are hot-swappable, Inspector-editable, remote-updatable via Addressables |
| Use Assembly Definitions for every logical layer | Leave all scripts in the default `Assembly-CSharp` | Shorter incremental compile times; enforces dependency direction |
| Use `Addressables.InstantiateAsync` for microgame prefabs | Use `Resources.Load` | Addressables support remote content delivery for cabinet updates |
| Use Unity Input System per-player action maps | Hardcode `Input.GetKey` or assume keyboard | Generalizes to any joystick device; no single-player keyboard assumption |
| Use orthographic camera for 2D arcade view | Use perspective camera | Consistent on any aspect ratio; simpler coordinate math |
| Mark Bootstrap objects `DontDestroyOnLoad` and destroy them after handoff | Keep Boot scene objects alive indefinitely | Prevents memory leak; single source of truth is Manager scene |


## Common Pitfalls

| Symptom | Fix |
|---|---|
| Compile error: "type or namespace not found" after adding `.asmdef` | Add explicit `references` entries in the `.asmdef` that needs the type; asmdef files opt out of auto-reference |
| `Editor`-only code compiles into player build and breaks | Set `"includePlatforms": ["Editor"]` in the Editor `.asmdef` |
| Addressables scene loads but no `IMicrogame` component found | Ensure the root GameObject in the scene has a component implementing `IMicrogame`; use `GetComponentInChildren` as fallback |
| `PlayerInput` events not firing for a specific player | Verify `notificationBehavior` matches how you subscribed (SendMessages vs InvokeUnityEvents vs InvokeCSharpEvents) |
| Geometric shape flickers or renders behind UI | Set `Sorting Layer` on the `SpriteRenderer` / `MeshRenderer`; UI Canvas defaults to overlay |
| `DontDestroyOnLoad` creates duplicate managers on scene reload | Guard with `if (instance != null && instance != this) { Destroy(gameObject); return; }` at the top of `Awake` |
| SO asset changes in Play mode don't persist | SOs are shared assets — edits in Play mode do persist to disk; use a runtime copy (`Instantiate(so)`) if you need per-session mutation |


## Verification

After project setup, run this checklist:

1. **Compile** — zero errors in Unity Console after importing project.
2. **Assembly isolation** — in any `.asmdef` Inspector, verify `Barcade.Core` does not list `Barcade.Framework` as a reference.
3. **Tests pass** — open Window > General > Test Runner; run EditMode suite; all `ScoreModel` tests green.
4. **Input** — Play Manager.unity; connect four joysticks (or use Unity Input System's on-screen controls in Editor); verify `PlayerInput.all.Count == 4`.
5. **Addressables** — Window > Asset Management > Addressables > Groups; confirm each microgame prefab/scene is in a labeled group; Build > New Build > Default Build Script succeeds.
6. **Boot flow** — Enter Play mode from Boot.unity; verify Console logs "Manager loaded" and Boot scene is unloaded within one frame.


## References

Heavy API material, asmdef JSON templates, and source snapshots:
- `references/official-docs-snapshot.md` — Unity 6.3 Manual excerpts, Input System notes, ScriptableObject guidance. Fetch date: 2026-06-18.

Companion skills:
- `.claude/skills/unity-microgame-framework/` — full microgame lifecycle, Sequencer, IMicrogame interface, ScoreModel.
- `.claude/skills/unity-testing/` — EditMode/PlayMode test setup, CLI test runner flags, code coverage.


## Provenance

Authored by Researcher subagent for ticket BOOTSTRAP-UNITY.
Last verified: 2026-06-18.

Primary sources:
- https://endoflife.date/unity (version/EOL data, fetched 2026-06-18)
- https://unity.com/releases/unity-6/support (LTS stream confirmation)
- https://unity.com/releases/editor/whats-new/6000.3.0f1 (Unity 6.3 release notes)
- https://docs.unity3d.com/6000.3/Documentation/Manual/cus-asmdef.html (Assembly Definitions)
- https://docs.unity3d.com/Manual/cus-tests.html (test asmdef layout)
- https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html (LoadSceneAsync API)
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.5/manual/PlayerInputManager.html (PlayerInputManager)
- https://unity.com/how-to/separate-game-data-logic-scriptable-objects (ScriptableObject data/logic separation)
- https://unity.com/resources/create-modular-game-architecture-with-scriptable-objects-ebook (SO architecture e-book)
