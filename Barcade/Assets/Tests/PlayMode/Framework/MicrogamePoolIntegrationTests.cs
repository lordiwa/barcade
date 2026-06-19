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
    /// The pool is built in-code from the 5 canonical registry ids, so this test is
    /// self-contained and does NOT require GenerateAll to have run first.
    ///
    /// The orchestrator MUST run GenerateAll before running any test that loads the
    /// generated .asset files from disk.
    ///
    /// Acceptance criteria verified:
    ///   AC1: SequencerDirector can pick each of the 5 mechanic ids.
    ///   AC2: MicrogameHost.StartRound succeeds for all 5 ids without exception.
    ///   AC3: Difficulty propagates: MicrogameHost.StartRound accepts difficulty param
    ///        without error for each id at difficulty=0, 0.5, and 1.0.
    /// </summary>
    [TestFixture]
    public class MicrogamePoolIntegrationTests
    {
        private static readonly string[] AllMechanicIds =
        {
            "esquiva", "aporrea", "apunta", "timing", "recolecta"
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
        public void SequencerDirector_CanPickAllFiveMechanicIds()
        {
            // Build a pool with one descriptor per mechanic id.
            var descriptors = new List<MicrogameDescriptor>();
            foreach (string id in AllMechanicIds)
                descriptors.Add(new MicrogameDescriptor(id, baseDuration: 5f, difficulty: 1));

            var director = new SequencerDirector(descriptors, new SeededRandom(42), RampSettings.Default);

            // Pick enough rounds to have each id selected at least once.
            // With anti-repeat and 5 entries, picking 20 rounds guarantees coverage.
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

        // ── AC2: MicrogameHost.StartRound succeeds for all 5 ids ──────────────────

        [UnityTest]
        public IEnumerator MicrogameHost_StartsRound_ForAllFiveMechanicIds()
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

                // StartRound must not throw for any of the 5 ids.
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

        // ── AC3: Difficulty parameter accepted for all 5 ids ─────────────────────

        [UnityTest]
        public IEnumerator MicrogameHost_AcceptsDifficultyParam_ForAllFiveIdsAndLevels()
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
            // Build descriptors for all 5 ids at 3 difficulty levels (15 total, like generated pool).
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
            }, "SequencerDirector must handle 30 rounds from a 15-entry pool without error");
        }
    }
}
