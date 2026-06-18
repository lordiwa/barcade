---
name: unity-testing
description: >
  Load this skill when writing or running Unity tests: creating NUnit EditMode
  tests for microgame logic (win/lose rules, scoring, RNG), adding PlayMode
  tests for input or scene integration, setting up a .Tests.asmdef, running the
  full suite headless in CI with the Unity CLI, or verifying a logic change with
  a failing test first. Also covers the Code Coverage package and the project's
  default test command.
---

# unity-testing

## When to Use This Skill

- Before writing any new microgame rule, scoring function, or RNG wrapper —
  write the EditMode test first.
- When wiring up a `.Tests.asmdef` for a new subsystem.
- When debugging a CI failure on the headless test run.
- When adding PlayMode tests for scene loading, player-input routing, or
  coroutine-driven timers.
- When configuring code-coverage reporting.

## Core Workflows

### 1. Verify the package is present

Open `Window > Package Manager`, search "Test Framework". The bundled version
for Unity 6 is **com.unity.test-framework 1.4.6** (released 2025-02-05, last
stable as of 2026-06-18). It ships pre-installed with every Unity 6 Editor —
no manual add needed. If missing, install via Add package by name:
`com.unity.test-framework`.

NUnit version bundled: **3.5** (Unity's custom fork — do not install the NuGet
NUnit package alongside it).

---

### 2. Set up the game-logic assembly definition

Every pure-logic class (microgame rules, scoring, RNG) must live in its own
asmdef so the test asmdef can reference it.

`Assets/Barcade.Core/Barcade.Core.asmdef`:
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

---

### 3. Create an EditMode test assembly

`Assets/Tests/EditMode/Barcade.Core.Tests.asmdef`:
```json
{
  "name": "Barcade.Core.Tests",
  "rootNamespace": "Barcade.Core.Tests",
  "references": ["Barcade.Core"],
  "optionalUnityReferences": ["TestAssemblies"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false
}
```

Key points:
- `"includePlatforms": ["Editor"]` — Unity strips this assembly from every
  player build automatically. No extra step required.
- `"optionalUnityReferences": ["TestAssemblies"]` — registers the assembly with
  the Test Runner.
- `"autoReferenced": false` — prevents accidental inclusion in other assemblies.
- For EditMode tests, `UnityEditor.TestRunner` is available; it is not available
  in PlayMode assemblies.

---

### 4. Create a PlayMode test assembly

`Assets/Tests/PlayMode/Barcade.Integration.Tests.asmdef`:
```json
{
  "name": "Barcade.Integration.Tests",
  "rootNamespace": "Barcade.Integration.Tests",
  "references": ["Barcade.Core"],
  "optionalUnityReferences": ["TestAssemblies"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false
}
```

Empty `includePlatforms` allows PlayMode tests to run on device targets when
needed (e.g., `-testPlatform StandaloneWindows64`). Unity still strips
TestAssemblies-marked asmdefs from shipping builds.

---

### 5. Write an EditMode test (NUnit)

```csharp
// Assets/Tests/EditMode/ScoringSystemTests.cs
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    [TestFixture]
    public class ScoringSystemTests
    {
        [Test]
        public void AddPoints_IncreasesTotalByAmount()
        {
            var scoring = new ScoringSystem();
            scoring.AddPoints(playerId: 0, amount: 10);
            Assert.That(scoring.GetTotal(0), Is.EqualTo(10));
        }

        [Test]
        public void AddPoints_MultiplePlayersAreIndependent()
        {
            var scoring = new ScoringSystem();
            scoring.AddPoints(0, 5);
            scoring.AddPoints(1, 20);
            Assert.That(scoring.GetTotal(0), Is.EqualTo(5));
            Assert.That(scoring.GetTotal(1), Is.EqualTo(20));
        }
    }
}
```

---

### 6. Write a seeded-RNG test for microgame variance logic

Keep all RNG in a plain C# wrapper over `System.Random` (never call
`UnityEngine.Random` from logic classes — it is global state and
non-deterministic in tests). Pass the `IRandom` interface into microgame
classes via constructor injection.

```csharp
// Assets/Barcade.Core/Runtime/IRandom.cs
namespace Barcade.Core
{
    public interface IRandom
    {
        int Next(int minInclusive, int maxExclusive);
        float NextFloat(); // 0f..1f
    }
}

// Assets/Barcade.Core/Runtime/SeededRandom.cs
using System;
namespace Barcade.Core
{
    public sealed class SeededRandom : IRandom
    {
        private readonly Random _rng;
        public SeededRandom(int seed) => _rng = new Random(seed);
        public int Next(int min, int max) => _rng.Next(min, max);
        public float NextFloat() => (float)_rng.NextDouble();
    }
}
```

Test — deterministic variance selection:
```csharp
// Assets/Tests/EditMode/MicrogameSequencerTests.cs
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    [TestFixture]
    public class MicrogameSequencerTests
    {
        [Test]
        public void SelectNext_WithFixedSeed_ProducesSameSequence()
        {
            var rng = new SeededRandom(seed: 42);
            var sequencer = new MicrogameSequencer(rng, microgameCount: 5);

            int first  = sequencer.SelectNext();
            int second = sequencer.SelectNext();

            // Re-create with same seed — must reproduce identical picks
            var rng2 = new SeededRandom(seed: 42);
            var sequencer2 = new MicrogameSequencer(rng2, microgameCount: 5);
            Assert.That(sequencer2.SelectNext(), Is.EqualTo(first));
            Assert.That(sequencer2.SelectNext(), Is.EqualTo(second));
        }

        [Test]
        public void SelectNext_DifferentSeeds_ProduceDifferentResults()
        {
            var s1 = new MicrogameSequencer(new SeededRandom(1), 5);
            var s2 = new MicrogameSequencer(new SeededRandom(999), 5);
            // Not guaranteed by math but true in practice for well-separated seeds
            bool anyDifference = false;
            for (int i = 0; i < 10; i++)
                if (s1.SelectNext() != s2.SelectNext()) { anyDifference = true; break; }
            Assert.That(anyDifference, Is.True);
        }
    }
}
```

---

### 7. Run tests headless from the CLI (CI)

**Verified flags** from `com.unity.test-framework@1.4` command-line reference:

```powershell
# Windows — EditMode (fast, no rendering, default CI suite)
& "C:\Program Files\Unity\Hub\Editor\6000.x.x\Editor\Unity.exe" `
  -runTests `
  -batchmode `
  -nographics `
  -projectPath "D:\barcade" `
  -testPlatform EditMode `
  -testResults "D:\barcade\TestResults\editmode-results.xml" `
  -logFile "D:\barcade\TestResults\editmode.log"
```

```bash
# Linux/macOS CI (GitHub Actions / Docker)
$UNITY_PATH/Unity \
  -runTests \
  -batchmode \
  -nographics \
  -projectPath "$GITHUB_WORKSPACE" \
  -testPlatform EditMode \
  -testResults "$GITHUB_WORKSPACE/TestResults/editmode-results.xml" \
  -logFile "$GITHUB_WORKSPACE/TestResults/editmode.log"
```

Exit code is `0` on all-pass, non-zero on any failure. Parse results with any
NUnit XML reporter (e.g., `junit-report` action, `nunit-to-junit` converter).

**Optional useful flags:**
- `-testFilter "Barcade.Core.Tests"` — run only one namespace.
- `-testCategory "Scoring;RNG"` — filter by `[Category("Scoring")]` attribute.
- `-assemblyNames "Barcade.Core.Tests"` — run only one assembly.
- `-runSynchronously` — force single-update execution (EditMode only; speeds up
  simple suites but prevents yield-based tests).

**Project default test command** (add to `PROJECT.md` and CI yaml):
```
Unity -runTests -batchmode -nographics -projectPath . -testPlatform EditMode -testResults TestResults/editmode-results.xml
```

---

### 8. Enable Code Coverage (optional, CI)

Install `com.unity.testtools.codecoverage` **1.3.0** (2026-01-22) via Package
Manager. Then extend the CI command:

```bash
$UNITY_PATH/Unity \
  -runTests -batchmode -nographics \
  -projectPath "$GITHUB_WORKSPACE" \
  -testPlatform EditMode \
  -testResults TestResults/editmode-results.xml \
  -enableCodeCoverage \
  -coverageResultsPath TestResults/coverage \
  -coverageOptions "generateHtmlReport;assemblyFilters:+<Barcade.Core>,-<*.Tests>"
```

HTML report lands in `TestResults/coverage/`. Upload as a CI artifact.

---

## Best Practices

- **Test-first for all pure logic.** Write a failing test before implementing
  any microgame win/lose condition, scoring rule, or sequencer change.
- **Never call `UnityEngine.Random` in logic classes.** Use `IRandom` with
  `SeededRandom` so tests are deterministic.
- **One asmdef per concern.** `Barcade.Core` (runtime logic), `Barcade.MonoBehaviours`
  (thin adapters), `Barcade.Core.Tests` (EditMode), `Barcade.Integration.Tests`
  (PlayMode). Keeping them separate enforces the architecture boundary.
- **Use `[TestFixture]` + `[Test]` for synchronous tests.** Reserve `[UnityTest]`
  + `IEnumerator` (PlayMode) for coroutine-driven flows that genuinely need
  frame skips.
- **Name tests with the `Method_Condition_ExpectedResult` pattern.** Makes CI
  output self-documenting.
- **Pin microgame contracts with `[Category("Contract")]` tests.** These are the
  last to break and the first to review in a PR.

---

## Common Pitfalls

| Pitfall | Fix |
|---|---|
| Test asmdef missing `optionalUnityReferences: ["TestAssemblies"]` | Tests invisible to Test Runner — add the field |
| `UnityEditor.TestRunner` referenced in a PlayMode asmdef | Compile error on device builds — remove it; PlayMode only needs `UnityEngine.TestRunner` |
| `includePlatforms` left empty in an EditMode asmdef | Tests may appear in player builds — set `["Editor"]` explicitly |
| Calling `UnityEngine.Random` in a logic class | Non-deterministic test results — use the `IRandom` abstraction |
| `-runTests` without `-batchmode` on a headless server | Unity opens a GUI window and hangs — always pair them |
| Project path has spaces and is unquoted on CLI | Path parsing fails — always quote `-projectPath` |
| `System.Random` seeded with `Environment.TickCount` in tests | Tests become non-deterministic — always pass explicit seed from test |

---

## Verification

After adding or changing tests:

1. Open `Window > General > Test Runner` in Editor — run All in EditMode; all
   green before merging.
2. Run the headless command locally (Workflow 7) and confirm exit code `0`.
3. Check `editmode-results.xml` exists and has `<test-run result="Passed">`.
4. If a test was added for a new rule: confirm it fails before the implementation
   lands, then passes after — this validates the test exercises the real code.

---

## References

Full CLI flag table, code-coverage flags, and asmdef platform semantics:
`.claude/skills/unity-testing/references/official-docs-snapshot.md`

---

## Provenance

- Researcher: Claude Sonnet (Researcher subagent)
- Ticket: BOOTSTRAP-UNITY
- Last verified: 2026-06-18
- Sources:
  - https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/index.html
  - https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html
  - https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html
  - https://docs.unity3d.com/Packages/com.unity.test-framework@2.0/manual/edit-mode-vs-play-mode-tests.html
  - https://docs.unity3d.com/Manual/cus-tests.html
  - https://github.com/needle-mirror/com.unity.test-framework/releases
  - https://docs.unity3d.com/Packages/com.unity.testtools.codecoverage@1.3/changelog/CHANGELOG.html
