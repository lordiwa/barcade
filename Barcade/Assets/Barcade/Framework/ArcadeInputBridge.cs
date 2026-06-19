using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Produces a <see cref="Barcade.Core.InputSnapshot"/> for each of the four
    /// fixed player slots every frame.
    ///
    /// DEVICE ASSIGNMENT — DETERMINISTIC, PER-DEVICE-INSTANCE:
    ///   Priority tier order: Joystick.all first, Gamepad.all second, Keyboard fallback.
    ///   Within each tier devices are sorted by a stable identity key so the same
    ///   physical device lands in the same slot across reboots:
    ///     1. description.serial  (USB serial string — most stable)
    ///     2. description.product (product name — stable for single-model cabinets)
    ///     3. deviceId            (runtime ordinal — last resort; not stable across reboots)
    ///   NOTE: final physical-port→slot binding will be configured per-cabinet via a
    ///   serial→slot config map (a later milestone). Stable sort is the interim guarantee.
    ///
    ///   Each slot's InputActionAsset copy has its Gameplay map pinned to EXACTLY ONE
    ///   device via <c>InputActionMap.devices</c>. This prevents all-joystick cabinets
    ///   from routing every joystick's movement to every slot.
    ///
    /// KEYBOARD DEV-FALLBACK (single-developer testing without cabinet hardware):
    ///   P0 (Rojo)    — WASD + Left Shift
    ///   P1 (Azul)    — Arrow keys + Right Ctrl
    ///   P2 (Amarillo)— IJKL + U
    ///   P3 (Verde)   — Numpad 8456 + Numpad 0
    ///   NOTE: all 4 keyboard slots share Keyboard.current; intentional on dev machines.
    ///
    /// Lives in Barcade.Framework (UnityEngine + UnityEngine.InputSystem allowed).
    /// Microgame code reads snapshots via <see cref="GetSnapshot"/> or the
    /// <see cref="IReadOnlyPlayerInputs"/> interface from <see cref="AsReadOnly"/>.
    /// </summary>
    public sealed class ArcadeInputBridge : MonoBehaviour, IReadOnlyPlayerInputs
    {
        // ── Inspector ────────────────────────────────────────────────────────────

        [Tooltip("The ArcadeControls.inputactions asset. Drag from Assets/Barcade/Input/.")]
        [SerializeField] private InputActionAsset _actionsAsset;

        // ── Constants ─────────────────────────────────────────────────────────────

        private const int SlotCount = 4;

        /// <summary>Keyboard control-scheme names per slot, matching ArcadeControls.inputactions.</summary>
        private static readonly string[] KeyboardSchemeNames =
        {
            "Keyboard-P0",   // P0 Rojo    — WASD + Left Shift
            "Keyboard-P1",   // P1 Azul    — Arrow keys + Right Ctrl
            "Keyboard-P2",   // P2 Amarillo — IJKL + U
            "Keyboard-P3",   // P3 Verde    — Numpad 8456 + Numpad 0
        };

        // ── Runtime state ────────────────────────────────────────────────────────

        private InputAction[]      _moveActions;
        private InputAction[]      _actionButtonActions;
        private bool[]             _buttonWasHeld;
        private InputSnapshot[]    _snapshots;

        // Per-slot InputActionAsset copies (each pinned to one device instance).
        private InputActionAsset[] _assetCopies;

        // Set to true after Initialize() completes successfully. Awake checks
        // this to avoid double-initialisation when tests call Initialize() before
        // activating the GameObject (which defers Awake until SetActive(true)).
        private bool _initialized;

        // ── Static accessor ──────────────────────────────────────────────────────

        /// <summary>
        /// Singleton reference set in Awake. Persists across scene loads.
        /// </summary>
        public static ArcadeInputBridge Instance { get; private set; }

        // ── Unity lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // If Initialize() was already called explicitly (test path: inactive GO
            // → Initialize() → SetActive(true) → Awake fires here), skip
            // re-initialisation so the test-injected state is preserved.
            if (_initialized)
                return;

            // Production path: Inspector-assigned asset is available.
            if (_actionsAsset != null)
            {
                InputDevice[] devices = GatherDevices();
                Initialize(_actionsAsset, devices);
            }
            else
            {
                // Production misconfiguration: warn loudly, leave bridge inert.
                Debug.LogError(
                    "[ArcadeInputBridge] _actionsAsset is not assigned. " +
                    "Drag ArcadeControls.inputactions onto the component in the Inspector.");

                // Still allocate arrays so UpdateSlot's null checks don't throw.
                AllocateArrays();
            }
        }

        private void Update()
        {
            if (_snapshots == null) return;
            for (int i = 0; i < SlotCount; i++)
                UpdateSlot(i);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_assetCopies == null) return;
            for (int i = 0; i < SlotCount; i++)
            {
                _moveActions?[i]?.Disable();
                _actionButtonActions?[i]?.Disable();
                if (_assetCopies[i] != null)
                    Destroy(_assetCopies[i]);
            }
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Testability seam and explicit initialiser.
        ///
        /// Assigns <paramref name="actionsAsset"/> and sets up each of the four
        /// player slots using the devices in <paramref name="orderedDevices"/>:
        ///   - Clones the asset once per slot so action maps are independent.
        ///   - Pins each map to exactly one device via <c>InputActionMap.devices</c>.
        ///   - Applies the correct binding-group mask (Joystick / Gamepad / Keyboard-Pn).
        ///   - Enables the map.
        ///
        /// Production: called from <see cref="Awake"/> with the Inspector-assigned asset
        /// and <see cref="GatherDevices"/> result.
        ///
        /// Tests: call this directly on a component attached to an INACTIVE GameObject
        /// (so <c>Awake</c> has not fired), then activate the object. This avoids the
        /// null-asset error path entirely and ensures production and test paths are identical.
        /// </summary>
        /// <param name="actionsAsset">The InputActionAsset to clone per slot.</param>
        /// <param name="orderedDevices">
        /// Exactly 4 devices in slot order (index 0 = Rojo … index 3 = Verde).
        /// Entries may be null for keyboard-fallback slots.
        /// </param>
        public void Initialize(InputActionAsset actionsAsset, IReadOnlyList<InputDevice> orderedDevices)
        {
            if (actionsAsset == null)
                throw new ArgumentNullException(nameof(actionsAsset));
            if (orderedDevices == null)
                throw new ArgumentNullException(nameof(orderedDevices));
            if (orderedDevices.Count != SlotCount)
                throw new ArgumentException(
                    $"orderedDevices must have exactly {SlotCount} entries, got {orderedDevices.Count}.",
                    nameof(orderedDevices));

            // Tear down any previously initialised state (safe to call multiple times).
            TearDownSlots();
            AllocateArrays();

            for (int i = 0; i < SlotCount; i++)
            {
                InputDevice device = orderedDevices[i];

                // Clone the asset so each slot has a completely independent
                // action-map instance with its own device list and enabled state.
                InputActionAsset copy = Instantiate(actionsAsset);
                copy.name = $"ArcadeControls_P{i}";
                _assetCopies[i] = copy;

                var map = copy.FindActionMap("Gameplay", throwIfNotFound: true);
                _moveActions[i]         = map.FindAction("Move",         throwIfNotFound: true);
                _actionButtonActions[i] = map.FindAction("ActionButton", throwIfNotFound: true);

                // Pin this slot's map to EXACTLY ONE device instance.
                // InputActionMap.devices restricts which physical device the map reads.
                // Without this, every slot on scheme "Joystick" hears ALL joysticks —
                // moving stick 0 would drive slots 0-3 on a 4-joystick cabinet.
                if (device != null)
                    map.devices = new InputDevice[] { device };

                // Narrow by binding group so per-player keyboard schemes (Keyboard-P0
                // vs Keyboard-P1) only fire their own key set, even when device is shared.
                string scheme = PickScheme(device, i);
                copy.bindingMask = InputBinding.MaskByGroup(scheme);

                map.Enable();

                Debug.Log(
                    $"[ArcadeInputBridge] Slot {i} ({(PlayerSlot)i}) " +
                    $"device='{device?.displayName ?? "none"}' " +
                    $"scheme='{scheme}' " +
                    $"product='{device?.description.product}' " +
                    $"serial='{device?.description.serial}'");
            }

            // Mark as initialised so Awake (fired when the host GameObject is later
            // activated) skips the production init path and preserves our state.
            _initialized = true;
        }

        /// <summary>
        /// Returns the most-recent <see cref="InputSnapshot"/> for <paramref name="slot"/>.
        /// Safe to call from any game-logic code during Update (after this bridge's Update runs).
        /// </summary>
        public InputSnapshot GetSnapshot(PlayerSlot slot) => _snapshots[(int)slot];

        /// <summary>
        /// Returns this bridge cast to <see cref="IReadOnlyPlayerInputs"/> so microgame
        /// logic can consume it without a hard Framework dependency.
        /// </summary>
        public IReadOnlyPlayerInputs AsReadOnly() => this;

        // IReadOnlyPlayerInputs implementation.
        InputSnapshot IReadOnlyPlayerInputs.For(PlayerSlot slot) => _snapshots[(int)slot];

        // ── Device gathering ─────────────────────────────────────────────────────

        /// <summary>
        /// Gathers exactly 4 devices for slots 0–3 using stable priority + sort:
        ///
        ///   Tier 1: Joystick.all — sorted by stable key (serial → product → deviceId).
        ///   Tier 2: Gamepad.all  — sorted by stable key (serial → product → deviceId).
        ///   Tier 3: Keyboard.current (dev fallback) — repeated to fill remaining slots.
        ///
        /// Sorting within each tier ensures the same physical device maps to the same
        /// slot across power cycles, provided the OS assigns the same serial/product string.
        /// Final cabinet-specific slot assignment (serial→slot config) is a later milestone.
        ///
        /// Entries may be null only if Keyboard.current is also unavailable (headless CI).
        /// </summary>
        public static InputDevice[] GatherDevices()
        {
            var result = new List<InputDevice>(SlotCount);

            // Tier 1 — Joystick (HID arcade encoders). Sort within tier for stability.
            var joysticks = new List<Joystick>(Joystick.all);
            joysticks.Sort(StableDeviceCompare);
            foreach (var j in joysticks)
                if (result.Count < SlotCount) result.Add(j);

            // Tier 2 — Gamepad. Sort within tier for stability.
            var gamepads = new List<Gamepad>(Gamepad.all);
            gamepads.Sort(StableDeviceCompare);
            foreach (var g in gamepads)
                if (result.Count < SlotCount) result.Add(g);

            // Tier 3 — Keyboard dev fallback. Fill remaining slots.
            while (result.Count < SlotCount)
                result.Add(Keyboard.current); // null in fully headless CI — slot reads zero

            return result.ToArray();
        }

        // ── Stable sort comparator ────────────────────────────────────────────────

        /// <summary>
        /// Compares two input devices by a stable, boot-invariant identity:
        ///   1. description.serial  — USB serial string (most stable).
        ///   2. description.product — product name (stable within single-model installs).
        ///   3. deviceId            — runtime-assigned ordinal (last resort; not boot-stable).
        /// </summary>
        private static int StableDeviceCompare(InputDevice a, InputDevice b)
        {
            string sa = a.description.serial  ?? string.Empty;
            string sb = b.description.serial  ?? string.Empty;
            int cmp = string.Compare(sa, sb, StringComparison.Ordinal);
            if (cmp != 0) return cmp;

            string pa = a.description.product ?? string.Empty;
            string pb = b.description.product ?? string.Empty;
            cmp = string.Compare(pa, pb, StringComparison.Ordinal);
            if (cmp != 0) return cmp;

            return a.deviceId.CompareTo(b.deviceId);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static string PickScheme(InputDevice device, int slotIndex)
        {
            if (device is Joystick) return "Joystick";
            if (device is Gamepad)  return "Gamepad";
            return KeyboardSchemeNames[slotIndex];
        }

        private void AllocateArrays()
        {
            _buttonWasHeld       = new bool[SlotCount];
            _snapshots           = new InputSnapshot[SlotCount];
            _moveActions         = new InputAction[SlotCount];
            _actionButtonActions = new InputAction[SlotCount];
            _assetCopies         = new InputActionAsset[SlotCount];
        }

        private void TearDownSlots()
        {
            if (_assetCopies == null) return;
            for (int i = 0; i < SlotCount; i++)
            {
                _moveActions?[i]?.Disable();
                _actionButtonActions?[i]?.Disable();
                if (_assetCopies[i] != null)
                    Destroy(_assetCopies[i]);
            }
        }

        // ── Per-slot snapshot update ──────────────────────────────────────────────

        private void UpdateSlot(int i)
        {
            if (_moveActions[i] == null || _actionButtonActions[i] == null)
            {
                _snapshots[i] = new InputSnapshot(0f, 0f, ButtonState.Released);
                return;
            }

            Vector2 stick = _moveActions[i].ReadValue<Vector2>();
            bool held = _actionButtonActions[i].IsPressed();

            ButtonState btnState;
            if (held && !_buttonWasHeld[i])
                btnState = ButtonState.Pressed;
            else if (held)
                btnState = ButtonState.Held;
            else
                btnState = ButtonState.Released;

            _buttonWasHeld[i] = held;
            _snapshots[i] = new InputSnapshot(stick.x, stick.y, btnState);
        }
    }
}
