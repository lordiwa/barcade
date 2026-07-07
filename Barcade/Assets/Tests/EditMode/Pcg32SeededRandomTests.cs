using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-044 — <see cref="SeededRandom"/> must be a genuine PCG32 (GDD §13:
    /// "Generador: PCG32... Prohibido System.Random y UnityEngine.Random en
    /// Barcade.Core"). Covers: reference vectors from the canonical pcg32 minimal
    /// C demo, the preserved public API, §13 stream derivation (sessionSeed →
    /// roundSeed(r) → named per-subsystem streams) with independence, bounded
    /// NextInt uniform semantics, and the CI analyzer rule as an executable test
    /// (no System.Random / UnityEngine.Random anywhere in Core sources).
    ///
    /// Cross-process determinism scope (GDD §13): the pinned constants below ARE
    /// the cross-run guarantee — PCG32 is a fixed algorithm with published
    /// outputs, unlike System.Random whose sequence is an implementation detail
    /// of the .NET runtime. Same-platform float caveats do not apply to the
    /// integer core.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class Pcg32SeededRandomTests
    {
        // ── Reference vectors (pcg32_srandom_r(42, 54), pcg-random.org minimal C demo) ──

        private static readonly uint[] ReferenceVectors42_54 =
        {
            0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu,
        };

        [Test]
        public void RawOutput_MatchesPublishedPcg32ReferenceVectors()
        {
            var rng = new SeededRandom(42UL, 54UL);
            for (int i = 0; i < ReferenceVectors42_54.Length; i++)
                Assert.That(rng.NextUInt32(), Is.EqualTo(ReferenceVectors42_54[i]),
                    $"output {i} diverged from the canonical pcg32 demo — this is not PCG32");
        }

        [Test]
        public void IntSeedCtor_IsThePcg32ReferenceStream()
        {
            // SeededRandom(int) must route through the same PCG32 engine on the
            // documented default stream, so the published vectors pin it too.
            var byInt = new SeededRandom(42);
            var byState = new SeededRandom(42UL, SeededRandom.DefaultStream);
            for (int i = 0; i < 10; i++)
                Assert.That(byInt.NextUInt32(), Is.EqualTo(byState.NextUInt32()));
        }

        // ── Preserved public API semantics ────────────────────────────────────

        [Test]
        public void NextFloat_IsInZeroOneAndDeterministic()
        {
            var a = new SeededRandom(7);
            var b = new SeededRandom(7);
            for (int i = 0; i < 1000; i++)
            {
                float v = a.NextFloat();
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(b.NextFloat(), Is.EqualTo(v));
            }
        }

        [Test]
        public void NextInt_PreservesSystemRandomEdgeSemantics()
        {
            var rng = new SeededRandom(3);
            Assert.That(rng.NextInt(5, 5), Is.EqualTo(5), "empty range returns min (System.Random.Next contract)");
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(10, 5));
            for (int i = 0; i < 1000; i++)
            {
                int v = rng.NextInt(-3, 4);
                Assert.That(v, Is.GreaterThanOrEqualTo(-3).And.LessThan(4));
            }
        }

        [Test]
        public void NextInt_IsRoughlyUniform_NoModuloBias()
        {
            // 3 buckets over 30k draws: modulo-biased implementations skew low buckets.
            var rng = new SeededRandom(1234);
            var counts = new int[3];
            const int draws = 30_000;
            for (int i = 0; i < draws; i++)
                counts[rng.NextInt(0, 3)]++;
            foreach (int c in counts)
                Assert.That(c, Is.EqualTo(draws / 3).Within(draws / 30),
                    "bucket counts deviate way beyond statistical noise");
        }

        [Test]
        public void NextDirection_UnitVectorAndDeterministic()
        {
            var a = new SeededRandom(55);
            var b = new SeededRandom(55);
            for (int i = 0; i < 50; i++)
            {
                Direction2D da = a.NextDirection();
                Direction2D db = b.NextDirection();
                Assert.That(MathF.Sqrt(da.X * da.X + da.Y * da.Y), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(da.X, Is.EqualTo(db.X));
                Assert.That(da.Y, Is.EqualTo(db.Y));
            }
        }

        // ── §13 stream derivation ─────────────────────────────────────────────

        [Test]
        public void Derive_SameArgs_SameSequence()
        {
            SeededRandom a = SeededRandom.Derive(1001, 3, RngStream.Spawner);
            SeededRandom b = SeededRandom.Derive(1001, 3, RngStream.Spawner);
            for (int i = 0; i < 20; i++)
                Assert.That(a.NextUInt32(), Is.EqualTo(b.NextUInt32()));
        }

        [Test]
        public void Derive_StreamsOfTheSameRound_AreIndependent()
        {
            // Consuming heavily from one subsystem's stream must not shift another's
            // (GDD 13: "streams separados evitan que consumir azar en un subsistema
            // altere otro").
            SeededRandom spawner = SeededRandom.Derive(500, 2, RngStream.Spawner);
            SeededRandom board = SeededRandom.Derive(500, 2, RngStream.Board);
            for (int i = 0; i < 1000; i++) spawner.NextUInt32();

            SeededRandom boardFresh = SeededRandom.Derive(500, 2, RngStream.Board);
            for (int i = 0; i < 20; i++)
                Assert.That(board.NextUInt32(), Is.EqualTo(boardFresh.NextUInt32()));
        }

        [Test]
        public void Derive_DifferentRoundOrStreamOrSeed_DifferentSequences()
        {
            uint[] Take(SeededRandom r)
            {
                var vals = new uint[8];
                for (int i = 0; i < 8; i++) vals[i] = r.NextUInt32();
                return vals;
            }

            uint[] baseline = Take(SeededRandom.Derive(42, 1, RngStream.Draws));
            Assert.That(Take(SeededRandom.Derive(42, 2, RngStream.Draws)), Is.Not.EqualTo(baseline), "round must feed the derivation");
            Assert.That(Take(SeededRandom.Derive(42, 1, RngStream.Spawner)), Is.Not.EqualTo(baseline), "stream must feed the derivation");
            Assert.That(Take(SeededRandom.Derive(43, 1, RngStream.Draws)), Is.Not.EqualTo(baseline), "session seed must feed the derivation");
        }

        // ── GDD §13 analyzer rule as an executable check ─────────────────────

        [Test]
        public void CoreSources_NeverReferenceSystemRandomOrUnityRandom()
        {
            string coreDir = FindCoreRuntimeDir();
            var offenders = new System.Collections.Generic.List<string>();
            var forbidden = new Regex(@"(?<!Seeded)(?<!ISeeded)\bRandom\b|UnityEngine\.Random", RegexOptions.Compiled);

            foreach (string file in Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories))
            {
                string code = StripComments(File.ReadAllText(file));
                if (forbidden.IsMatch(code))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.That(offenders, Is.Empty,
                "GDD 13: System.Random / UnityEngine.Random are banned in Barcade.Core — all randomness flows through SeededRandom (PCG32)");
        }

        private static string FindCoreRuntimeDir()
        {
            // Works from both the dotnet test bin dir and the Unity project CWD:
            // walk up until the repo layout is visible.
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory ?? Environment.CurrentDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Barcade", "Assets", "Barcade", "Core", "Runtime");
                if (Directory.Exists(candidate)) return candidate;
                string unityCandidate = Path.Combine(dir.FullName, "Assets", "Barcade", "Core", "Runtime");
                if (Directory.Exists(unityCandidate)) return unityCandidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("could not locate Barcade Core Runtime sources from " + Environment.CurrentDirectory);
        }

        /// <summary>Removes // line and /* block */ comments (and string literals, to dodge false positives).</summary>
        private static string StripComments(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            code = Regex.Replace(code, @"//[^\r\n]*", " ");
            code = Regex.Replace(code, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return code;
        }
    }
}
