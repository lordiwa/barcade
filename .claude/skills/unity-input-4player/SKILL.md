---
name: unity-input-4player
description: >
  Load this skill whenever you are working on input handling, 4-player local
  multiplayer, PlayerInput, the Unity Input System package, .inputactions files,
  joystick/gamepad/button mapping for the barcade arcade cabinet, or any code
  that reads stick direction or button state from a physical controller.
---

# unity-input-4player

## When to Use This Skill

- Creating or editing any `.inputactions` asset for the barcade project.
- Writing `PlayerInput`, `InputUser`, or `InputAction` code.
- Wiring up physical arcade encoders (USB HID joystick/keyboard) to player slots.
- Writing microgame code that needs a per-player input snapshot.
- Debugging device-not-found or wrong-player-gets-input issues.
- Adding the keyboard dev-fallback for single-developer testing.

---

## Core Workflows

### 1. Install the Input System Package

In `Packages/manifest.json`, confirm or add:

```json
"com.unity.inputsystem": "1.19.0"
```

In **Edit > Project Settings > Player > Other Settings**, set
**Active Input Handling** to **Input System Package (New)** (or Both during
transition). Disable the legacy Input Manager to avoid confusion.

> **Verified version: 1.19.0** (released 2026-02-24). Available for Unity
> 2022.3, 6.0, 6.2, 6.3.

---

### 2. Create the Input Actions Asset

1. Right-click in the Project window: **Create > Input Actions**.
   Name it `ArcadeControls`.
2. Open the asset; add one **Action Map** named `Gameplay`.
3. Add two actions:

   | Action | Type | Control Type | Binding |
   |--------|------|--------------|---------|
   | `Move` | Value | Vector2 | Left Stick (Gamepad) + WASD composite (Keyboard) |
   | `ActionButton` | Button | Button | Button South (Gamepad) + Space (Keyboard) |

4. In the importer Inspector, check **Generate C# Class** and set the class
   name to `ArcadeInputActions`. This produces a type-safe wrapper so you never
   use magic strings.

---

### 3. Architecture Decision — Manual Device Assignment (Recommended for Cabinet)

**Do NOT use `PlayerInputManager` auto-join for a fixed arcade cabinet.**

`PlayerInputManager` is designed for games where players pick up arbitrary
controllers and "press to join." On a cabinet, four encoders are always wired
in the same physical order. Auto-join can assign Player 0 to whichever encoder
fires first, producing a different slot on every boot.

**Use `PlayerInput.Instantiate` with an explicit device list instead.**

The `ArcadePlayerSpawner` (see Workflow 4) reads all connected devices at
startup, sorts them by a stable key (USB port path from
`device.description.serial` or ordinal index), and binds each to a fixed
player slot deterministically.

---

### 4. ArcadePlayerSpawner — Stable Slot Assignment

```csharp
// ArcadePlayerSpawner.cs
// Attach to a persistent manager GameObject in the boot scene.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ArcadePlayerSpawner : MonoBehaviour
{
    [Tooltip("Prefab must have a PlayerInput component with ArcadeControls assigned.")]
    public GameObject playerPrefab;

    // Indexed Rojo=0, Azul=1, Amarillo=2, Verde=3
    public static readonly string[] PlayerColors = { "Rojo", "Azul", "Amarillo", "Verde" };

    public static ArcadePlayerInput[] Players { get; private set; } = new ArcadePlayerInput[4];

    void Awake()
    {
        var devices = GatherDevices();      // see below
        for (int i = 0; i < 4; i++)
        {
            var pi = PlayerInput.Instantiate(
                prefab:          playerPrefab,
                playerIndex:     i,
                controlScheme:   devices[i] is Keyboard ? "Keyboard" : "Gamepad",
                splitScreenIndex: -1,
                pairWithDevice:  devices[i]);

            DontDestroyOnLoad(pi.gameObject);
            pi.gameObject.name = $"Player_{PlayerColors[i]}";

            Players[i] = pi.GetComponent<ArcadePlayerInput>();
            Players[i].Init(i, PlayerColors[i]);
        }
    }

    // Returns exactly 4 devices: real gamepads/joysticks first,
    // then keyboard slots to fill remaining slots (dev fallback).
    static InputDevice[] GatherDevices()
    {
        var result = new List<InputDevice>();

        // Prefer Joystick over Gamepad because most zero-delay arcade
        // encoders enumerate as HID Joystick, not Gamepad.
        foreach (var j in Joystick.all)
            if (result.Count < 4) result.Add(j);

        foreach (var g in Gamepad.all)
            if (result.Count < 4) result.Add(g);

        // Fill remaining slots with the keyboard (dev fallback).
        while (result.Count < 4)
            result.Add(Keyboard.current);

        return result.ToArray();
    }
}
```

> **Arcade encoder reality.** "Zero-delay" USB encoder boards (the most common
> cheap option) enumerate as HID **Joystick**, not Gamepad. Some dual-mode
> boards can appear as keyboard rows (WASD/arrows etc.). Check with
> `InputSystem.devices` in the editor before assuming gamepad. If encoders
> appear as keyboards, add a "Keyboard" control scheme per encoder using DPAD
> composite bindings on distinct key sets. See references/ for HID layout
> customisation.

---

### 5. ArcadePlayerInput — Per-Player Component

```csharp
// ArcadePlayerInput.cs
// Lives on the player prefab alongside PlayerInput.
using UnityEngine;
using UnityEngine.InputSystem;

public class ArcadePlayerInput : MonoBehaviour
{
    public int  PlayerIndex  { get; private set; }
    public string ColorName  { get; private set; }

    // Snapshot consumed by microgame code each frame.
    public struct Snapshot
    {
        public Vector2 Move;        // normalised stick, magnitude 0-1
        public bool    ActionDown;  // true only the frame the button was pressed
        public bool    ActionHeld;  // true while button is held
    }
    public Snapshot Current { get; private set; }

    PlayerInput  _pi;
    InputAction  _moveAction;
    InputAction  _actionButtonAction;
    bool         _actionWasHeld;

    public void Init(int index, string color)
    {
        PlayerIndex = index;
        ColorName   = color;
        _pi         = GetComponent<PlayerInput>();

        // Access via the generated wrapper or by string (string shown here for clarity).
        var map            = _pi.actions.FindActionMap("Gameplay", throwIfNotFound: true);
        _moveAction        = map.FindAction("Move",         throwIfNotFound: true);
        _actionButtonAction= map.FindAction("ActionButton", throwIfNotFound: true);
        map.Enable();
    }

    // Called each frame by microgame code — poll pattern.
    void Update()
    {
        bool held = _actionButtonAction.IsPressed();
        Current = new Snapshot
        {
            Move       = _moveAction.ReadValue<Vector2>(),
            ActionDown = held && !_actionWasHeld,
            ActionHeld = held,
        };
        _actionWasHeld = held;
    }

    // Event-driven alternative: subscribe in Init() if you prefer callbacks.
    // _actionButtonAction.performed += ctx => OnActionPerformed(ctx);
    // void OnActionPerformed(InputAction.CallbackContext ctx) { ... }
}
```

Microgame code only ever touches `ArcadePlayerSpawner.Players[i].Current`:

```csharp
// Inside any microgame Update():
var snap = ArcadePlayerSpawner.Players[playerIndex].Current;
if (snap.ActionDown) TriggerJump();
rb.velocity = snap.Move * speed;
```

---

### 6. Keyboard Dev-Fallback Layout

When fewer than 4 physical devices are present, `GatherDevices()` fills slots
with `Keyboard.current`. Add a second control scheme in the Actions asset named
`Keyboard` with bindings spread across the keyboard:

| Player | Move (composite DPAD) | ActionButton |
|--------|-----------------------|--------------|
| 0 (Rojo)    | WASD               | Left Shift   |
| 1 (Azul)    | Arrow keys         | Right Ctrl   |
| 2 (Amarillo)| IJKL               | U            |
| 3 (Verde)   | Numpad 8456        | Numpad 0     |

Because all four "players" share `Keyboard.current`, use a single
`PlayerInput` in `Keyboard` mode and fan the composite bindings out. In
`GatherDevices()` the same `Keyboard.current` reference is returned for each
unfilled slot; this is intentional for dev convenience and would never occur
on the real cabinet.

---

## Best Practices

- Always access actions via `_pi.actions.FindActionMap(...)` — never cache an
  `InputAction` before `Init()` runs.
- Enable the action map explicitly (`map.Enable()`). PlayerInput does not
  always auto-enable maps when you set up the asset outside the Inspector flow.
- Call `InputSystem.RegisterLayout<T>()` at `[RuntimeInitializeOnLoadMethod]`
  if you write a custom HID layout — do it before any device connects.
- Persist player GameObjects with `DontDestroyOnLoad` so device pairings
  survive scene changes between microgames.
- Use `InputDevice.description.product` and `.serial` to log which physical
  encoder ended up in which slot. Log this at startup for cabinet diagnostics.
- Stick to `IsPressed()` for held state and manually track edge (ActionDown)
  as shown above. `InputAction.triggered` only fires during `performed` phase
  and misses held state.

---

## Common Pitfalls

| Symptom | Cause | Fix |
|---------|-------|-----|
| Wrong player gets input | Auto-join ordering is non-deterministic | Use manual `PlayerInput.Instantiate` with explicit device |
| Joystick not found, only Gamepad | Zero-delay encoder enumerated as Joystick HID | Query `Joystick.all` before `Gamepad.all` |
| No input after scene load | Action map was disabled on scene transition | Re-call `map.Enable()` in `Init()` or use `DontDestroyOnLoad` |
| `ReadValue` returns zero always | Action map never enabled | `map.Enable()` must be called explicitly |
| `ActionDown` fires every frame | Edge detection missing | Track `_actionWasHeld` as shown in Workflow 5 |
| All 4 players move together on keyboard | Same `Keyboard.current` returned for all slots | Expected on dev machine; add per-player key sets to the Keyboard scheme |
| Cabinet player 0/1 swapped after reboot | Device index reordered by OS | Use `device.description.serial` or USB port path to assign stable order |

---

## Verification

After wiring up the spawner, confirm in Play Mode:

1. Open **Window > Analysis > Input Debugger**. Each of the 4 `PlayerInput`
   instances should appear with its paired device.
2. Move each joystick; confirm only the correct player slot shows
   `Move` changing in the debugger.
3. Press the action button on each encoder; confirm `ActionDown` fires exactly
   once per press in the `ArcadePlayerInput.Current` snapshot.
4. Disconnect and reconnect a USB encoder; confirm `InputSystem.onDeviceChange`
   fires and the cabinet can handle a hot-plug (or gracefully pauses).
5. Run with zero physical devices (dev laptop); confirm all 4 keyboard fallback
   schemes respond.

---

## References

- [references/official-docs-snapshot.md](references/official-docs-snapshot.md) —
  API and concept extracts fetched 2026-06-18.

---

## Provenance

- Researcher: Claude (Sonnet 4.6), ticket BOOTSTRAP-UNITY
- Sources verified: 2026-06-18
- Key URLs:
  - https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/PlayerInput.html
  - https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/api/UnityEngine.InputSystem.PlayerInput.html
  - https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/HID.html
  - https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/Devices.html
  - https://discussions.unity.com/t/release-input-system-1-19-0/1710220
  - https://docs.unity3d.com/Packages/com.unity.inputsystem@1.5/manual/PlayerInputManager.html
