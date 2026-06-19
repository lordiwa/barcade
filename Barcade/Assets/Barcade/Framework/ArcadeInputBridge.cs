using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Produces a <see cref="Barcade.Core.InputSnapshot"/> for each of the four
    /// fixed player slots every frame.
    ///
    /// Device assignment is DETERMINISTIC for a fixed arcade cabinet:
    ///   1. Joystick.all — zero-delay USB encoders enumerate as HID Joystick.
    ///   2. Gamepad.all  — USB gamepad / dual-mode encoder boards.
    ///   3. Keyboard.current (repeated) — dev fallback when fewer than 4 physical
    ///      devices are connected.
    ///
    /// Each frame this component reads the raw action values from the
    /// Unity Input System and converts them to the engine-agnostic
    /// <see cref="Barcade.Core.InputSnapshot"/> struct.
    ///
    /// Keyboard dev-fallback key layout (for testing without cabinet hardware):
    ///   P0 (Rojo)    — WASD + Left Shift
    ///   P1 (Azul)    — Arrow keys + Right Ctrl
    ///   P2 (Amarillo)— IJKL + U
    ///   P3 (Verde)   — Numpad 8456 + Numpad 0
    ///
    /// Lives in Barcade.Framework (UnityEngine + UnityEngine.InputSystem allowed).
    /// Microgame code reads snapshots via <see cref="GetSnapshot"/> or the
    /// <see cref="IReadOnlyPlayerInputs"/> adapter via <see cref="AsReadOnly"/>.
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

        private InputAction[] _moveActions;
        private InputAction[] _actionButtonActions;
        private bool[]        _buttonWasHeld;
        private InputSnapshot[] _snapshots;

        // We keep a per-slot InputActionAsset copy so each player's actions
        // can be independently device-constrained.
        private InputActionAsset[] _assetCopies;

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

            _buttonWasHeld        = new bool[SlotCount];
            _snapshots            = new InputSnapshot[SlotCount];
            _moveActions          = new InputAction[SlotCount];
            _actionButtonActions  = new InputAction[SlotCount];
            _assetCopies          = new InputActionAsset[SlotCount];

            InitialiseDeviceSlots();
        }

        private void Update()
        {
            for (int i = 0; i < SlotCount; i++)
                UpdateSlot(i);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            for (int i = 0; i < SlotCount; i++)
            {
                _moveActions?[i]?.Disable();
                _actionButtonActions?[i]?.Disable();
                if (_assetCopies?[i] != null)
                    Destroy(_assetCopies[i]);
            }
        }

        // ── Public API ───────────────────────────────────────────────────────────

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
        /// Gathers exactly 4 devices for slots 0–3 using stable priority order:
        ///   1. Joystick.all  — USB arcade HID joystick encoders
        ///   2. Gamepad.all   — USB gamepad controllers
        ///   3. Keyboard.current (dev fallback, may appear multiple times if fewer than 4 physical devices)
        ///
        /// Entries may be null if Keyboard.current is also unavailable.
        /// </summary>
        public static InputDevice[] GatherDevices()
        {
            var result = new List<InputDevice>(SlotCount);

            foreach (var j in Joystick.all)
                if (result.Count < SlotCount) result.Add(j);

            foreach (var g in Gamepad.all)
                if (result.Count < SlotCount) result.Add(g);

            // Fill remaining slots with keyboard (dev fallback).
            while (result.Count < SlotCount)
                result.Add(Keyboard.current); // may be null in headless/batchmode

            return result.ToArray();
        }

        // ── Initialisation ───────────────────────────────────────────────────────

        private void InitialiseDeviceSlots()
        {
            if (_actionsAsset == null)
            {
                Debug.LogError(
                    "[ArcadeInputBridge] _actionsAsset is not assigned. " +
                    "Drag ArcadeControls.inputactions onto the component in the Inspector.");
                return;
            }

            InputDevice[] devices = GatherDevices();

            for (int i = 0; i < SlotCount; i++)
            {
                InputDevice device = devices[i];

                // Clone the asset so each player has an independent action-map instance.
                InputActionAsset copy = Instantiate(_actionsAsset);
                copy.name = $"ArcadeControls_P{i}";
                _assetCopies[i] = copy;

                var map = copy.FindActionMap("Gameplay", throwIfNotFound: true);
                _moveActions[i]         = map.FindAction("Move",         throwIfNotFound: true);
                _actionButtonActions[i] = map.FindAction("ActionButton", throwIfNotFound: true);

                // Bind the map to a specific device using an override group so only the
                // correct keyboard scheme (or joystick/gamepad) responds.
                string scheme = PickScheme(device, i);

                // Apply a binding mask so only the correct control-scheme group fires.
                copy.bindingMask = InputBinding.MaskByGroup(scheme);

                map.Enable();

                Debug.Log(
                    $"[ArcadeInputBridge] Slot {i} ({(PlayerSlot)i}) " +
                    $"device='{device?.displayName ?? "none"}' " +
                    $"scheme='{scheme}' " +
                    $"product='{device?.description.product}' " +
                    $"serial='{device?.description.serial}'");
            }
        }

        private static string PickScheme(InputDevice device, int slotIndex)
        {
            if (device is Joystick) return "Joystick";
            if (device is Gamepad)  return "Gamepad";
            // Keyboard or null — use per-player keyboard scheme.
            return KeyboardSchemeNames[slotIndex];
        }

        // ── Per-slot snapshot update ──────────────────────────────────────────────

        private void UpdateSlot(int i)
        {
            if (_moveActions == null || _moveActions[i] == null ||
                _actionButtonActions == null || _actionButtonActions[i] == null)
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
