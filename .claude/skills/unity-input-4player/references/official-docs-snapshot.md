# Unity Input System — Official Docs Snapshot

Fetched: 2026-06-18  
Package version covered: com.unity.inputsystem 1.19.0 (latest as of fetch date)  
Docs version used for fetch: 1.8.2 (latest docs set available; 1.19 API is
backwards-compatible for all APIs cited here)

---

## Package Version History (recent)

| Version | Release date | Notes |
|---------|--------------|-------|
| 1.19.0  | 2026-02-24   | Bug fixes; available in Unity 2022.3, 6.0, 6.2, 6.3 |
| 1.15.0  | 2025-10      | Available in upcoming editor versions |
| 1.11.0  | 2024-08      | — |
| 1.8.2   | 2024-04-29   | UI interaction fix, DualSense Edge fix |
| 1.8.0   | 2024-03-12   | — |

Source: https://discussions.unity.com/t/release-input-system-1-19-0/1710220
        https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/changelog/CHANGELOG.html

---

## PlayerInput API

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/api/UnityEngine.InputSystem.PlayerInput.html  
Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/PlayerInput.html

### Static Methods

```csharp
// Instantiate prefab and pair a single device.
public static PlayerInput Instantiate(
    GameObject prefab,
    int playerIndex = -1,
    string controlScheme = null,
    int splitScreenIndex = -1,
    InputDevice pairWithDevice = null);

// Instantiate prefab and pair multiple devices.
public static PlayerInput Instantiate(
    GameObject prefab,
    int playerIndex = -1,
    string controlScheme = null,
    int splitScreenIndex = -1,
    params InputDevice[] pairWithDevices);
```

### Instance Properties

```csharp
int playerIndex               // zero-based, unique per player
InputActionAsset actions      // the action asset assigned to this player
ReadOnlyArray<InputDevice> devices  // devices currently paired to player
string currentControlScheme   // name of active scheme, or null
InputUser user                // the underlying InputUser
```

### Instance Methods

```csharp
// Switch scheme and re-pair devices.
void SwitchCurrentControlScheme(string controlScheme, params InputDevice[] devices);
bool SwitchCurrentControlScheme(params InputDevice[] devices);
```

### Notification Behaviors (set via Inspector or PlayerInput.notificationBehavior)

- `PlayerNotifications.SendMessages` — `GameObject.SendMessage` on this GO
- `PlayerNotifications.BroadcastMessages` — `BroadcastMessage` down hierarchy
- `PlayerNotifications.InvokeUnityEvents` — per-action UnityEvent in Inspector
- `PlayerNotifications.InvokeCSharpEvents` — C# events (recommended for code)

#### C# Events (available when behavior = InvokeCSharpEvents)

```csharp
event Action<InputAction.CallbackContext> onActionTriggered;
event Action<PlayerInput> onDeviceLost;
event Action<PlayerInput> onDeviceRegained;
```

### Reading Input from a Callback

```csharp
// SendMessages / BroadcastMessages notification mode:
public void OnMove(InputValue value)
{
    Vector2 v = value.Get<Vector2>();
}

// C# Events / InvokeUnityEvents mode:
public void OnMove(InputAction.CallbackContext ctx)
{
    Vector2 v = ctx.ReadValue<Vector2>();
}
```

---

## InputAction API

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/api/UnityEngine.InputSystem.InputAction.html

### Polling (call in Update or FixedUpdate)

```csharp
// Read current value (any value type).
TValue ReadValue<TValue>() where TValue : struct;

// True if the action was performed at any point this frame.
bool triggered { get; }

// True while the action control is in "actuated" state (held).
bool IsPressed();
```

### Phases

An action passes through: `Waiting -> Started -> Performed -> Canceled`

- `performed` fires on button press (for Button type) or whenever a Value
  changes.
- `canceled` fires on button release.
- `triggered` is a shorthand: `phase == Performed` in the current frame.

---

## Device Enumeration

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/Devices.html

```csharp
// All currently recognised devices.
ReadOnlyArray<InputDevice> InputSystem.devices;

// Per-type shortlists.
ReadOnlyArray<Gamepad>   Gamepad.all;
ReadOnlyArray<Joystick>  Joystick.all;
Keyboard                 Keyboard.current;   // null if no keyboard present

// Device description fields (for identifying encoders).
string device.description.product;       // product name string
string device.description.manufacturer;
string device.description.serial;        // USB serial (may be empty on cheap boards)
string device.description.interfaceName; // e.g. "HID", "XInput"

// Monitor adds/removes.
InputSystem.onDeviceChange += (InputDevice device, InputDeviceChange change) => { ... };
```

---

## PlayerInputManager API

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.5/manual/PlayerInputManager.html

**Not recommended for the barcade cabinet** (see SKILL.md architecture decision).
Documented here for reference.

```
Component: PlayerInputManager
Properties:
  joinBehavior          JoinBehavior enum:
                          JoinPlayers_WhenButtonIsPressed
                          JoinPlayers_WhenJoinActionIsTriggered
                          JoinPlayers_Manually
  joiningEnabled        bool
  playerPrefab          GameObject (must have PlayerInput)
  maxPlayerCount        int
  playerCount           int (read-only)

Events (sent to a companion component implementing these interfaces):
  OnPlayerJoined(PlayerInput)
  OnPlayerLeft(PlayerInput)

Manual join:
  void JoinPlayer(int playerIndex=-1, int splitScreenIndex=-1,
                  string controlScheme=null, InputDevice pairWithDevice=null)
```

---

## HID Support

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/HID.html

Unity auto-generates a layout for any HID reporting usage
`GenericDesktop/Joystick`, `GenericDesktop/Gamepad`, or
`GenericDesktop/MultiAxisController`. The auto-generated device class is
**Joystick**. X/Y axes map to `stick`; Button 1 maps to `trigger`.

### Custom HID Layout Registration

```csharp
// Register before any device connects — use [RuntimeInitializeOnLoadMethod].
InputSystem.RegisterLayout<MyArcadeEncoder>(
    new InputDeviceMatcher()
        .WithInterface("HID")
        .WithProduct("Zero Delay USB Joystick"));  // match encoder product string

// Or match by vendor/product ID:
new InputDeviceMatcher()
    .WithCapability("vendorId", 0x0079)
    .WithCapability("productId", 0x0006);
```

### Keyboard-Emulating Encoders

Some encoders present as a keyboard (common on cheap dual-mode boards). Check
`device.description.interfaceName == "HID"` and product name. If the encoder
appears as a `Keyboard`, bind player slots to distinct key rows and use a
"Keyboard" control scheme with DPAD composites.

---

## InputUser API (low-level, for reference)

Source: https://github.com/Unity-Technologies/InputSystem/blob/develop/Packages/com.unity.inputsystem/Documentation~/UserManagement.md

```csharp
// Pair a device to an existing or new user.
InputUser user = InputUser.PerformPairingWithDevice(device);
InputUser user = InputUser.PerformPairingWithDevice(device, user: existingUser);

// Associate an action asset with the user.
user.AssociateActionsWithUser(actionAsset);

// Activate a control scheme.
user.ActivateControlScheme("Gamepad");

// Query paired devices.
ReadOnlyArray<InputDevice> user.pairedDevices;

// Remove pairing.
user.UnpairDevice(device);
```

`PlayerInput.user` exposes the underlying `InputUser` for the component-based
approach.

---

## InputActionAsset

Source: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/ActionAssets.html

Create: **Assets > Create > Input Actions** (produces `.inputactions` JSON file)

```csharp
// Find map and actions by name at runtime.
InputActionMap map    = asset.FindActionMap("Gameplay", throwIfNotFound: true);
InputAction    move   = map.FindAction("Move",         throwIfNotFound: true);

// Enable/disable.
map.Enable();
map.Disable();

// PlayerInput clones the asset internally per instance when multiple players
// share the same asset reference — access always via _pi.actions, not the
// original asset, to get the per-player copy.
```

Enable **Generate C# Class** in the importer to get a type-safe wrapper
(avoids all string lookups at runtime).

---

*End of snapshot. Re-fetch if upgrading beyond com.unity.inputsystem 1.19.0.*
