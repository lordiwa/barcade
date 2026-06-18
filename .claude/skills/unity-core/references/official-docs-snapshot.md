# Unity Core — Official Docs Snapshot

Fetch date: 2026-06-18
Maintained by: Researcher subagent (ticket BOOTSTRAP-UNITY)

---

## Unity 6.3 LTS — Version & Support

| Item | Value |
|---|---|
| Marketing name | Unity 6.3 LTS |
| Internal version string | **6000.3.18f1** (latest patch, 2026-06-17) |
| Initial LTS release | 6000.3.0f1, released 2025-12-04 |
| Security support until | 2027-12-04 |
| Extended support until | 2028-12-04 (Enterprise/Industry) |
| Unity 6.0 LTS EOL | 2026-10-16 — do NOT use for new work |

Install stream: choose "Unity 6.3 LTS" in Unity Hub. Hub CLI:
```
unity-hub install --version 6000.3.18f1
```

Sources:
- https://endoflife.date/unity
- https://unity.com/releases/unity-6/support
- https://unity.com/releases/editor/whats-new/6000.3.0f1

---

## Assembly Definitions (.asmdef)

Source: https://docs.unity3d.com/6000.3/Documentation/Manual/cus-asmdef.html

### Key fields

| Field | Type | Purpose |
|---|---|---|
| `name` | string | Assembly identity (referenced by other asmdefs) |
| `rootNamespace` | string | Default namespace for scripts in this folder |
| `references` | string[] | Names of other assemblies this one depends on |
| `includePlatforms` | string[] | Restrict to platforms; `["Editor"]` = editor-only |
| `excludePlatforms` | string[] | Block specific platforms |
| `allowUnsafeCode` | bool | Enables `unsafe` C# blocks |
| `autoReferenced` | bool | Whether `Assembly-CSharp` auto-refs this assembly |
| `optionalUnityReferences` | string[] | `["TestAssemblies"]` marks as test assembly (stripped from non-test builds) |

### Barcade assembly dependency graph

```
Barcade.Core                  (pure C#, no engine deps beyond UnityEngine math)
  └── Barcade.Core.Editor     (editor tools for Core; Editor platform only)
  └── Barcade.Core.Tests.EditMode  (NUnit, Editor platform only)
  └── Barcade.Core.Tests.PlayMode  (NUnit, TestAssemblies, all platforms)

Barcade.Framework             (references Barcade.Core)
  └── Barcade.Framework.Editor
  └── Barcade.Framework.Tests.EditMode

Barcade.Microgames.*          (one asmdef per microgame, references Barcade.Framework)
```

### Full EditMode test asmdef

```json
{
  "name": "Barcade.Core.Tests.EditMode",
  "references": ["Barcade.Core"],
  "optionalUnityReferences": ["TestAssemblies"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false
}
```

### Full PlayMode test asmdef

```json
{
  "name": "Barcade.Core.Tests.PlayMode",
  "references": ["Barcade.Core", "Barcade.Framework"],
  "optionalUnityReferences": ["TestAssemblies"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false
}
```

---

## SceneManager.LoadSceneAsync API

Source: https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html

```csharp
// Signatures
AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
AsyncOperation LoadSceneAsync(int buildIndex,   LoadSceneMode mode = LoadSceneMode.Single)
AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
AsyncOperation LoadSceneAsync(int buildIndex,   LoadSceneParameters parameters)
```

- Returns `AsyncOperation`; monitor `isDone` or `yield return op` in a Coroutine.
- `LoadSceneMode.Single` (default): unloads all current scenes before loading.
- `LoadSceneMode.Additive`: preserves existing scenes; multiple scenes active simultaneously.
- Unload: `SceneManager.UnloadSceneAsync(string sceneName)`.

For Addressable scenes (preferred for hot-swap microgames):
```csharp
// Load
AsyncOperationHandle<SceneInstance> handle =
    Addressables.LoadSceneAsync(assetRef, LoadSceneMode.Additive);
await handle.Task; // or yield return handle

// Unload
await Addressables.UnloadSceneAsync(handle).Task;
```

---

## PlayerInputManager (Unity Input System)

Package: `com.unity.inputsystem` (bundled in Unity 6.3)
Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.5/manual/PlayerInputManager.html

### Component settings relevant to barcade

| Setting | Recommended value | Reason |
|---|---|---|
| Join Behavior | Join Players Manually | Fixed 4-player cabinet; no "press to join" needed |
| Player Prefab | PlayerPrefab.prefab (has PlayerInput) | Manager instantiates all 4 at startup |
| Notification Behavior | Invoke Unity Events | Decoupled; no SendMessage string coupling |
| Max Player Count | 4 | Hard cap for cabinet |

### Manual spawn (startup)

```csharp
// Called once from GameManager.Start()
for (int i = 0; i < 4; i++)
{
    playerInputManager.JoinPlayer(
        playerIndex:    i,
        splitScreenIndex: -1,          // -1 = no split screen
        controlScheme:  "Joystick",    // must match scheme name in InputActionAsset
        pairWithDevice: joystickDevices[i]
    );
}
```

`joystickDevices[i]` is populated by iterating `InputSystem.devices` filtered by type
`Joystick` at startup, or assigned in the Inspector for dev builds.

---

## ScriptableObject — Data / Logic Separation

Sources:
- https://unity.com/how-to/separate-game-data-logic-scriptable-objects
- https://unity.com/resources/create-modular-game-architecture-with-scriptable-objects-ebook

### Key rules

1. SOs inherit from `UnityEngine.ScriptableObject`, not `MonoBehaviour`.
2. Decorated with `[CreateAssetMenu]` to appear in the Asset menu.
3. Created as `.asset` files; live in the Project, not in scenes.
4. Shared across scenes — one SO asset, many consumers.
5. **Do not mutate SO fields at runtime** without creating a clone first:
   ```csharp
   var runtimeCopy = Instantiate(originalSO); // safe to modify
   ```
6. Use `[Header]`, `[Tooltip]`, `[Range]`, and `[TextArea]` attributes for
   designer-friendly Inspector layout.

### Pattern: SO as event channel

```csharp
[CreateAssetMenu(menuName = "Barcade/Events/RoundEndedEvent")]
public class RoundEndedEvent : ScriptableObject
{
    private readonly List<System.Action<bool[]>> _listeners = new();

    public void Raise(bool[] results)
    {
        foreach (var l in _listeners) l.Invoke(results);
    }

    public void Register(System.Action<bool[]> cb)   => _listeners.Add(cb);
    public void Unregister(System.Action<bool[]> cb) => _listeners.Remove(cb);
}
```

This replaces singleton event buses: each system holds a reference to the SO asset,
not a static class, making it testable and inspector-wirable.

---

## 2D Rendering: Primitives Reference

No external sprite art required for milestone 1.

### Quad (rectangle/square)

```csharp
var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
go.transform.localScale = new Vector3(width, height, 1f);
go.GetComponent<MeshRenderer>().material =
    new Material(Shader.Find("Unlit/Color")) { color = Color.red };
```

### Circle approximation

Use `PrimitiveType.Sphere` with an orthographic camera and `z = 0` so it reads as a circle.
Or generate a circle mesh:

```csharp
static Mesh CreateCircleMesh(float radius, int segments = 32)
{
    var verts = new Vector3[segments + 1];
    var tris  = new int[segments * 3];
    verts[0] = Vector3.zero;
    for (int i = 0; i < segments; i++)
    {
        float angle = 2 * Mathf.PI * i / segments;
        verts[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
        tris[i * 3]     = 0;
        tris[i * 3 + 1] = (i + 1);
        tris[i * 3 + 2] = (i + 2 > segments) ? 1 : (i + 2);
    }
    var m = new Mesh { vertices = verts, triangles = tris };
    m.RecalculateNormals();
    return m;
}
```

### Player brand colors (hex)

| Player | Color | Hex |
|---|---|---|
| P1 (Rojo) | Red | `#E8212B` |
| P2 (Azul) | Blue | `#1E5FBE` |
| P3 (Amarillo) | Yellow | `#F5C400` |
| P4 (Verde) | Green | `#2BA84A` |

---

## Addressables Quick-Start

Package: `com.unity.addressables` (install via Package Manager if not present)

1. Window > Asset Management > Addressables > Groups — creates `AddressableAssetSettings`.
2. Select a prefab/scene in Project; tick "Addressable" in Inspector; assign a label (e.g., `microgames`).
3. Build: Addressables > Build > New Build > Default Build Script.
4. Remote catalogue URL set in `AddressableAssetSettings` > Profiles — use for cabinet OTA updates.

Load pattern:
```csharp
var handle = Addressables.InstantiateAsync("dodge_the_ball"); // key = address string or AssetReference
await handle.Task;
// use handle.Result (GameObject)
// release when done:
Addressables.ReleaseInstance(handle.Result);
```

Source: https://docs.unity3d.com/Packages/com.unity.addressables@2.3/manual/index.html
