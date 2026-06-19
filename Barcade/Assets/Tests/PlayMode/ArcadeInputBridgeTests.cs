using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEditor;
using Barcade.Core;
using Barcade.Framework;

namespace Barcade.Input.Tests
{
    /// <summary>
    /// PlayMode tests for <see cref="ArcadeInputBridge"/>.
    ///
    /// Uses the Unity Input System's <see cref="InputTestFixture"/> to simulate
    /// device input without physical hardware.
    ///
    /// Acceptance criteria verified:
    ///   AC1: GatherDevices returns Joystick first (stable-sorted), then Gamepad,
    ///        then Keyboard fallback; always exactly 4 slots.
    ///   AC2: ButtonState edge transitions (Pressed / Held / Released) through the
    ///        REAL ArcadeInputBridge.UpdateSlot path, not a copy.
    ///   AC3: Per-player slot routing — driving device assigned to slot 0 (Rojo) ONLY
    ///        affects slot 0; other slots stay zero/Released.
    ///   AC4: Stick values (StickX/StickY) round-trip through the bridge correctly.
    ///   AC5: Keyboard fallback populates all slots when no physical devices exist.
    ///   MEDIUM-1: GatherDevices stable-sorts within each tier by serial→product→deviceId.
    /// </summary>
    [TestFixture]
    public class ArcadeInputBridgeTests : InputTestFixture
    {
        // ── Path to the ArcadeControls actions asset ─────────────────────────────

        private const string ActionsAssetPath =
            "Assets/Barcade/Input/ArcadeControls.inputactions";

        // ── Helper: create a bridge with explicit device list ────────────────────

        /// <summary>
        /// Creates an <see cref="ArcadeInputBridge"/> MonoBehaviour using the
        /// testability seam introduced to fix HIGH-2 / the null-asset-at-Awake bug.
        ///
        /// Construction sequence:
        ///   1. Create an INACTIVE GameObject — AddComponent defers Awake.
        ///   2. AddComponent(ArcadeInputBridge) — component exists but Awake has NOT run.
        ///   3. Load ArcadeControls.inputactions from disk.
        ///   4. Call bridge.Initialize(asset, orderedDevices) — sets up all slots;
        ///      sets _initialized = true.
        ///   5. Activate the GameObject — Awake fires; it finds _initialized == true
        ///      and returns immediately, preserving the injected state.
        ///
        /// This approach avoids all SerializedObject injection hacks and correctly
        /// exercises the same Initialize() code path used in production (Awake calls
        /// Initialize() when _actionsAsset != null in the production flow).
        /// </summary>
        private static ArcadeInputBridge CreateBridge(IReadOnlyList<InputDevice> orderedDevices)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsAssetPath);
            Assert.IsNotNull(asset,
                $"ArcadeControls.inputactions not found at '{ActionsAssetPath}'. " +
                "Ensure the asset is committed under Assets/Barcade/Input/.");

            // Step 1: inactive GO — Awake is deferred until SetActive(true).
            var go = new GameObject("TestBridge");
            go.SetActive(false);

            // Step 2: add component while GO is inactive.
            var bridge = go.AddComponent<ArcadeInputBridge>();

            // Step 3+4: inject asset and device list via the public seam.
            // This sets _initialized = true so the deferred Awake is a no-op.
            bridge.Initialize(asset, orderedDevices);

            // Step 5: activate — Awake fires but skips initialisation (_initialized == true).
            // Instance gets set and DontDestroyOnLoad applies.
            go.SetActive(true);

            return bridge;
        }

        // TearDown ensures no bridge singleton persists between tests.
        [TearDown]
        public override void TearDown()
        {
            var existing = ArcadeInputBridge.Instance;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            base.TearDown(); // resets the Input System
        }

        // ── AC1: GatherDevices priority ordering ─────────────────────────────────

        [Test]
        public void GatherDevices_PrefersJoysticksOverGamepads()
        {
            var joystick = InputSystem.AddDevice<Joystick>();
            var gamepad  = InputSystem.AddDevice<Gamepad>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices[0], Is.SameAs(joystick),
                "Slot 0 should be the Joystick (higher priority than Gamepad)");
            Assert.That(devices[1], Is.SameAs(gamepad),
                "Slot 1 should be the Gamepad");
        }

        [Test]
        public void GatherDevices_FillsRemainingWithKeyboard_WhenFewerThanFourPhysicalDevices()
        {
            var joystick = InputSystem.AddDevice<Joystick>();
            var keyboard = InputSystem.AddDevice<Keyboard>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices.Length, Is.EqualTo(4));
            Assert.That(devices[0], Is.SameAs(joystick));
            for (int i = 1; i < 4; i++)
                Assert.That(devices[i], Is.SameAs(keyboard),
                    $"Slot {i} should be keyboard fallback");
        }

        [Test]
        public void GatherDevices_WithFourJoysticks_ReturnsFourJoysticks()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var j2 = InputSystem.AddDevice<Joystick>();
            var j3 = InputSystem.AddDevice<Joystick>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices.Length, Is.EqualTo(4));
            // The stable sort uses deviceId as tie-breaker; j0..j3 are added in order
            // and all have empty serial/product in the test fixture, so deviceId order
            // is the sort order — same as insertion order for this case.
            Assert.That(devices[0], Is.SameAs(j0));
            Assert.That(devices[1], Is.SameAs(j1));
            Assert.That(devices[2], Is.SameAs(j2));
            Assert.That(devices[3], Is.SameAs(j3));
        }

        [Test]
        public void GatherDevices_ReturnsExactlyFourSlots()
        {
            for (int i = 0; i < 5; i++)
                InputSystem.AddDevice<Joystick>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices.Length, Is.EqualTo(4),
                "GatherDevices must always return exactly 4 slots");
        }

        // ── MEDIUM-1: Stable sort within tier (deviceId fallback) ───────────────

        [Test]
        public void GatherDevices_StableSort_WithinTier_SortsByDeviceId_AsTieBreak()
        {
            // Add 3 joysticks in order. All have empty serial/product in the test fixture,
            // so the stable sort falls through to deviceId (runtime ordinal).
            // deviceId is assigned in add-order, so j0.deviceId < j1.deviceId < j2.deviceId.
            // GatherDevices must return them in the same add-order (sort is stable on deviceId).
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var j2 = InputSystem.AddDevice<Joystick>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            // Stable sort by deviceId ascending preserves add-order when serial/product are equal.
            Assert.That(devices[0], Is.SameAs(j0), "Slot 0: lowest deviceId joystick");
            Assert.That(devices[1], Is.SameAs(j1), "Slot 1: middle deviceId joystick");
            Assert.That(devices[2], Is.SameAs(j2), "Slot 2: highest deviceId joystick");
        }

        // ── AC3 + HIGH-2: End-to-end device isolation through the real bridge ────

        /// <summary>
        /// CRITICAL routing test (HIGH-2 fix verification).
        ///
        /// Creates two simulated joysticks. Instantiates a real <see cref="ArcadeInputBridge"/>
        /// via the Initialize() seam with explicit device assignment: j0→slot0, j1→slot1,
        /// kb→slot2, kb→slot3. Drives stick input ONLY on j0. Pumps a frame. Asserts:
        ///   - Rojo's StickX reflects the driven value  (map.devices pinned correctly).
        ///   - Azul's StickX remains zero               (device isolation working).
        ///   - Amarillo and Verde remain zero.
        ///
        /// This test FAILS when <c>map.devices</c> is not set (HIGH-1 bug) because the
        /// "Joystick" binding group is shared and all slots hear all joysticks.
        /// It PASSES after the HIGH-1 fix pins each map to exactly one device instance.
        /// </summary>
        [UnityTest]
        public IEnumerator Bridge_DeviceIsolation_Slot0Input_DoesNotAffectOtherSlots()
        {
            // Arrange: two joysticks, one keyboard fallback.
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var kb = InputSystem.AddDevice<Keyboard>();

            // Create bridge with explicit slot assignment: j0→Rojo, j1→Azul, kb→Amarillo, kb→Verde.
            var bridge = CreateBridge(new InputDevice[] { j0, j1, kb, kb });

            // Pump one frame — Awake already ran, Update hasn't run yet in this frame.
            yield return null;

            // Act: drive the stick on j0 (slot 0 / Rojo) to the right.
            Set(j0.stick, new Vector2(0.8f, 0f));

            // Pump a frame so ArcadeInputBridge.Update() runs and reads the value.
            yield return null;

            // Assert: slot 0 (Rojo) sees the movement.
            var rojoSnap = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(rojoSnap.StickX, Is.GreaterThan(0.5f),
                "Rojo's StickX must reflect j0's stick input (device correctly routed to slot 0)");

            // Assert: slot 1 (Azul) is NOT affected — j1 was not moved.
            var azulSnap = bridge.GetSnapshot(PlayerSlot.Azul);
            Assert.That(azulSnap.StickX, Is.EqualTo(0f).Within(0.01f),
                "Azul's StickX must be zero — j0's input must NOT leak to slot 1 (HIGH-1 fix)");

            // Assert: slots 2 and 3 are also unaffected.
            Assert.That(bridge.GetSnapshot(PlayerSlot.Amarillo).StickX, Is.EqualTo(0f).Within(0.01f),
                "Amarillo must be zero — no cross-slot input leakage");
            Assert.That(bridge.GetSnapshot(PlayerSlot.Verde).StickX, Is.EqualTo(0f).Within(0.01f),
                "Verde must be zero — no cross-slot input leakage");
        }

        /// <summary>
        /// Drives slot 1 (Azul) while slot 0 stays idle, confirming isolation is
        /// bidirectional — slot 1's device does not spill into slot 0.
        /// </summary>
        [UnityTest]
        public IEnumerator Bridge_DeviceIsolation_Slot1Input_DoesNotAffectSlot0()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var kb = InputSystem.AddDevice<Keyboard>();

            var bridge = CreateBridge(new InputDevice[] { j0, j1, kb, kb });
            yield return null;

            // Drive only j1 (slot 1 / Azul).
            Set(j1.stick, new Vector2(-0.7f, 0.3f));
            yield return null;

            // Azul should see the input.
            var azulSnap = bridge.GetSnapshot(PlayerSlot.Azul);
            Assert.That(azulSnap.StickX, Is.LessThan(-0.5f),
                "Azul's StickX must reflect j1's stick input");

            // Rojo must stay zero.
            var rojoSnap = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(rojoSnap.StickX, Is.EqualTo(0f).Within(0.01f),
                "Rojo's StickX must be zero — j1's input must not leak to slot 0");
        }

        // ── AC2 + HIGH-2: Button edge states through the REAL UpdateSlot path ────

        /// <summary>
        /// Drives a joystick trigger through the real ArcadeInputBridge.UpdateSlot path
        /// (not the ComputeState test copy) and asserts Pressed→Held→Released transitions.
        /// </summary>
        [UnityTest]
        public IEnumerator Bridge_ButtonEdges_ThroughRealUpdateSlot_PressedHeldReleased()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var kb = InputSystem.AddDevice<Keyboard>();

            var bridge = CreateBridge(new InputDevice[] { j0, j1, kb, kb });
            yield return null;

            // Frame 1: press the trigger (button down on j0).
            Set(j0.trigger, 1f);
            yield return null;

            var snap1 = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(snap1.Button, Is.EqualTo(ButtonState.Pressed),
                "First frame with trigger held must be Pressed (through real UpdateSlot)");

            // Frame 2: keep holding.
            yield return null;

            var snap2 = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(snap2.Button, Is.EqualTo(ButtonState.Held),
                "Second consecutive frame while trigger held must be Held");

            // Frame 3: release the trigger.
            Set(j0.trigger, 0f);
            yield return null;

            var snap3 = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(snap3.Button, Is.EqualTo(ButtonState.Released),
                "Frame after releasing trigger must be Released");

            // Frame 4: still released.
            yield return null;

            var snap4 = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(snap4.Button, Is.EqualTo(ButtonState.Released),
                "Subsequent frames after release must stay Released");
        }

        /// <summary>
        /// A quick-press (one frame held then released) must produce Pressed then Released
        /// — never Held — through the real bridge.
        /// </summary>
        [UnityTest]
        public IEnumerator Bridge_ButtonEdges_QuickPress_PressedThenReleasedNeverHeld()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var kb = InputSystem.AddDevice<Keyboard>();

            var bridge = CreateBridge(new InputDevice[] { j0, j1, kb, kb });
            yield return null;

            Set(j0.trigger, 1f);
            yield return null;
            Assert.That(bridge.GetSnapshot(PlayerSlot.Rojo).Button, Is.EqualTo(ButtonState.Pressed));

            Set(j0.trigger, 0f);
            yield return null;
            Assert.That(bridge.GetSnapshot(PlayerSlot.Rojo).Button, Is.EqualTo(ButtonState.Released));
        }

        // ── AC1: GatherDevices slot routing ─────────────────────────────────────

        [Test]
        public void GatherDevices_SlotZero_IsFirstJoystick_ByStableSort()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            // Both joysticks have empty serial/product — sorted by deviceId, which
            // is assigned in add-order, so j0 < j1.
            Assert.That(devices[(int)PlayerSlot.Rojo], Is.SameAs(j0),
                "First Joystick (lower deviceId) must be slot Rojo (0)");
            Assert.That(devices[(int)PlayerSlot.Azul], Is.SameAs(j1),
                "Second Joystick must be slot Azul (1)");
        }

        [Test]
        public void GatherDevices_WithTwoJoysticksOneGamepad_CorrectSlotOrder()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var gp = InputSystem.AddDevice<Gamepad>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices[0], Is.SameAs(j0), "Slot 0 = first joystick");
            Assert.That(devices[1], Is.SameAs(j1), "Slot 1 = second joystick");
            Assert.That(devices[2], Is.SameAs(gp), "Slot 2 = gamepad");
            // Slot 3: Keyboard.current (may be null in headless — must equal Keyboard.current).
            Assert.That(devices[3], Is.EqualTo(Keyboard.current),
                "Slot 3 must be Keyboard.current (keyboard dev-fallback)");
        }

        // ── AC4: Stick values via bridge ─────────────────────────────────────────

        [UnityTest]
        public IEnumerator Bridge_StickValues_ReflectDrivenInput()
        {
            var j0 = InputSystem.AddDevice<Joystick>();
            var j1 = InputSystem.AddDevice<Joystick>();
            var kb = InputSystem.AddDevice<Keyboard>();

            var bridge = CreateBridge(new InputDevice[] { j0, j1, kb, kb });
            yield return null;

            Set(j0.stick, new Vector2(0.6f, -0.4f));
            yield return null;

            var snap = bridge.GetSnapshot(PlayerSlot.Rojo);
            Assert.That(snap.StickX, Is.GreaterThan(0.4f),  "StickX must reflect driven x");
            Assert.That(snap.StickY, Is.LessThan(-0.2f),    "StickY must reflect driven y");
        }

        // ── AC5: Keyboard fallback when no physical devices ──────────────────────

        [Test]
        public void GatherDevices_NoPhysicalDevices_AllSlotsGetKeyboard()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            InputDevice[] devices = ArcadeInputBridge.GatherDevices();

            Assert.That(devices.Length, Is.EqualTo(4));
            foreach (var device in devices)
                Assert.That(device, Is.SameAs(keyboard),
                    "Every slot should fall back to keyboard when no physical devices exist");
        }

        // ── InputSnapshot value tests (pure data — no bridge needed) ─────────────

        [Test]
        public void InputSnapshot_StickValues_StoredAsXY()
        {
            var snap = new InputSnapshot(0.75f, -0.5f, ButtonState.Released);
            Assert.That(snap.StickX, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(snap.StickY, Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(snap.Button, Is.EqualTo(ButtonState.Released));
        }

        [Test]
        public void InputSnapshot_ZeroStick_ReturnsZeroMagnitude()
        {
            var snap = new InputSnapshot(0f, 0f, ButtonState.Released);
            Assert.That(snap.Magnitude(), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void InputSnapshot_UnitStick_ReturnsUnitMagnitude()
        {
            var snap = new InputSnapshot(1f, 0f, ButtonState.Released);
            Assert.That(snap.Magnitude(), Is.EqualTo(1f).Within(0.0001f));
        }

        // ── Retained: ButtonStateMachine inline tests (kept as fast sanity checks) ─

        [Test]
        public void ButtonStateMachine_Pressed_Held_Released_Cycle()
        {
            bool wasHeld = false;

            Assert.That(ComputeState(true,  ref wasHeld), Is.EqualTo(ButtonState.Pressed),
                "First frame held = Pressed");
            Assert.That(ComputeState(true,  ref wasHeld), Is.EqualTo(ButtonState.Held),
                "Second frame held = Held");
            Assert.That(ComputeState(false, ref wasHeld), Is.EqualTo(ButtonState.Released),
                "Release frame = Released");
            Assert.That(ComputeState(false, ref wasHeld), Is.EqualTo(ButtonState.Released),
                "Subsequent release = Released");
        }

        [Test]
        public void ButtonStateMachine_ShortPress_PressedThenReleasedAfterOneFrame()
        {
            bool wasHeld = false;
            Assert.That(ComputeState(true,  ref wasHeld), Is.EqualTo(ButtonState.Pressed));
            Assert.That(ComputeState(false, ref wasHeld), Is.EqualTo(ButtonState.Released));
        }

        [Test]
        public void ButtonStateMachine_NeverPressed_AlwaysReleased()
        {
            bool wasHeld = false;
            for (int i = 0; i < 10; i++)
                Assert.That(ComputeState(false, ref wasHeld), Is.EqualTo(ButtonState.Released),
                    $"Frame {i}: should be Released when button never pressed");
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors the edge-detection logic from ArcadeInputBridge.UpdateSlot for
        /// inline sanity checks that don't need a full scene.
        /// </summary>
        private static ButtonState ComputeState(bool held, ref bool wasHeld)
        {
            ButtonState result =
                held && !wasHeld ? ButtonState.Pressed :
                held             ? ButtonState.Held    :
                                   ButtonState.Released;
            wasHeld = held;
            return result;
        }
    }
}
