using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Barcade.Core;
using Barcade.Framework;

namespace Barcade.Framework.Tests
{
    /// <summary>
    /// TASK-014 Part C — Verification that SequencerDirector can select every mechanic id
    /// and MicrogameHost can build each without error.
    ///
    /// The pool is built in-code from the registry's own ids, so this test is
    /// self-contained and does NOT require GenerateAll to have run first.
    ///
    /// TASK-061 (T-107 slice 4): AllMechanicIds shrank from 5 to 1 (esquiva only)
    /// -- aporrea/timing/apunta-v1 retired as pure engineering calls (mapped to
    /// no canonical GDD MECH_01-09 mechanic), recolecta retired by human ruling
    /// (also mapped to none; code preserved in git history, TASK-065 filed for a
    /// future ¡RECOGE! design). With a single registered id, SequencerDirector's
    /// anti-repeat condition is a no-op by design (see that class's own doc: "if
    /// the pool has exactly one entry that entry is always returned") -- the
    /// AC1 test below still passes but no longer exercises any actual selection
    /// variety, an unavoidable consequence of retiring 4 of the 5 ids down to 1.
    ///
    /// The orchestrator MUST run GenerateAll before running any test that loads the
    /// generated .asset files from disk.
    ///
    /// Acceptance criteria verified:
    ///   AC1: SequencerDirector can pick every registered mechanic id.
    ///   AC2: MicrogameHost.StartRound succeeds for every registered id without exception.
    ///   AC3: Difficulty propagates: MicrogameHost.StartRound accepts difficulty param
    ///        without error for each id at difficulty=0, 0.5, and 1.0.
    /// </summary>
    [TestFixture]
    public class MicrogamePoolIntegrationTests
    {
        private static readonly string[] AllMechanicIds =
        {
            "esquiva"
        };

        // ── Teardown ──────────────────────────────────────────────────────────────

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _roots)
                if (go != null) Object.Destroy(go);
            _roots.Clear();
        }

        // ── Stubs ─────────────────────────────────────────────────────────────────

        private sealed class ZeroInputs : IReadOnlyPlayerInputs
        {
            private static readonly InputSnapshot Zero =
                new InputSnapshot(0f, 0f, ButtonState.Released);
            public InputSnapshot For(PlayerSlot slot) => Zero;
        }

        // ── AC1: SequencerDirector picks each mechanic id ─────────────────────────

        [Test]
        public void SequencerDirector_CanPickAllRegisteredMechanicIds()
        {
            // Build a pool with one descriptor per mechanic id.
            var descriptors = new List<MicrogameDescriptor>();
            foreach (string id in AllMechanicIds)
                descriptors.Add(new MicrogameDescriptor(id, baseDuration: 5f, difficulty: 1));

            var director = new SequencerDirector(descriptors, new SeededRandom(42), RampSettings.Default);

            // Pick enough rounds to have each id selected at least once. With a
            // single registered id, every pick trivially satisfies this (anti-repeat
            // is a no-op for a single-item pool) -- 20 rounds is still a harmless
            // sanity margin, not load-bearing coverage math anymore.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 20; i++)
            {
                MicrogameDescriptor picked = director.PickNext();
                seen.Add(picked.Id);
                director.AdvanceRound(new bool[] { true, true, true, true });
            }

            foreach (string id in AllMechanicIds)
                Assert.That(seen.Contains(id), Is.True,
                    $"SequencerDirector must be able to pick id '{id}' from the pool");
        }

        // ── AC2: MicrogameHost.StartRound succeeds for every registered id ────────

        [UnityTest]
        public IEnumerator MicrogameHost_StartsRound_ForAllRegisteredMechanicIds()
        {
            var registry = MicrogameHost.BuildDefaultRegistry();

            foreach (string id in AllMechanicIds)
            {
                var hostGO = new GameObject($"TestHost_{id}");
                hostGO.SetActive(false);
                _roots.Add(hostGO);

                var host = hostGO.AddComponent<MicrogameHost>();
                host.SetRegistry(registry);
                hostGO.SetActive(true);

                // StartRound must not throw for any registered id.
                Assert.DoesNotThrow(() =>
                {
                    host.StartRound(
                        microgameId:  id,
                        seed:         42,
                        playDuration: 0.05f,
                        inputs:       new ZeroInputs(),
                        difficulty:   0f);
                }, $"MicrogameHost.StartRound must not throw for id '{id}'");

                yield return null; // let it tick once

                // After one frame, IsComplete may or may not be true yet; but there
                // must be no exception and the host must be in a valid state.
                Assert.That(host, Is.Not.Null);
            }
        }

        // ── AC3: Difficulty parameter accepted for every registered id ───────────

        [UnityTest]
        public IEnumerator MicrogameHost_AcceptsDifficultyParam_ForAllRegisteredIdsAndLevels()
        {
            var registry = MicrogameHost.BuildDefaultRegistry();
            float[] difficulties = { 0f, 0.5f, 1f };

            foreach (string id in AllMechanicIds)
            {
                foreach (float diff in difficulties)
                {
                    var hostGO = new GameObject($"TestHost_{id}_d{diff}");
                    hostGO.SetActive(false);
                    _roots.Add(hostGO);

                    var host = hostGO.AddComponent<MicrogameHost>();
                    host.SetRegistry(registry);
                    hostGO.SetActive(true);

                    float capturedDiff = diff;
                    string capturedId  = id;

                    Assert.DoesNotThrow(() =>
                    {
                        host.StartRound(
                            microgameId:  capturedId,
                            seed:         1,
                            playDuration: 0.05f,
                            inputs:       new ZeroInputs(),
                            difficulty:   capturedDiff);
                    }, $"StartRound(id={capturedId}, difficulty={capturedDiff}) must not throw");

                    yield return null;
                }
            }
        }

        // ── AC2 + AC3: Pool built from in-code definitions feeds the sequencer ─────

        [Test]
        public void MicrogameLoopController_CanBuildDirector_FromInCodePool()
        {
            // Build descriptors for all registered ids at 3 difficulty levels (3 total, like generated pool).
            var descriptors = new List<MicrogameDescriptor>();
            int[] difficulties = { 1, 2, 3 };
            foreach (string id in AllMechanicIds)
            {
                foreach (int diff in difficulties)
                    descriptors.Add(new MicrogameDescriptor(id, baseDuration: 5f, difficulty: diff));
            }

            var director = new SequencerDirector(descriptors, new SeededRandom(7), RampSettings.Default);

            // Run 30 rounds — must not throw.
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    MicrogameDescriptor picked = director.PickNext();
                    Assert.That(picked, Is.Not.Null);
                    Assert.That(picked.Id, Is.Not.Null.And.Not.Empty);
                    director.AdvanceRound(new bool[] { true, false, true, false });
                }
            }, "SequencerDirector must handle 30 rounds from a 3-entry pool without error");
        }
    }
}
