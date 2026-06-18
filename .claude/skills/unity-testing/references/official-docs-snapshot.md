# Unity Test Framework — Official Docs Snapshot

Fetched: 2026-06-18

## Primary Sources

- Unity Test Framework package overview (v1.4):
  https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/index.html

- EditMode vs PlayMode tests (v1.4 / 2.0-exp):
  https://docs.unity3d.com/Packages/com.unity.test-framework@2.0/manual/edit-mode-vs-play-mode-tests.html

- Running tests from the command line (v1.4):
  https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html

- Unity 6.3 Manual: Run tests from the command line:
  https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html

- Workflow: Create a new test assembly (v1.1):
  https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/workflow-create-test-assembly.html

- Unity Manual: Add tests to a package (asmdef examples):
  https://docs.unity3d.com/Manual/cus-tests.html

- Code Coverage package changelog (v1.3.0):
  https://docs.unity3d.com/Packages/com.unity.testtools.codecoverage@1.3/changelog/CHANGELOG.html

- Unity Manual: Code Coverage:
  https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.testtools.codecoverage.html

- needle-mirror GitHub (release history):
  https://github.com/needle-mirror/com.unity.test-framework/releases

## Version Summary (as of 2026-06-18)

| Package | Latest Stable | NUnit Version |
|---|---|---|
| com.unity.test-framework | 1.4.6 (2025-02-05) | 3.5 (custom fork) |
| com.unity.testtools.codecoverage | 1.3.0 (2026-01-22) | — |

## Full CLI Flag Reference

Flags from `com.unity.test-framework@1.4/manual/reference-command-line.html`:

| Flag | Type | Description |
|---|---|---|
| `-runTests` | bool | Execute tests from the command line |
| `-batchmode` | bool | Suppress manual user-input prompts |
| `-projectPath` | string | Path to the Unity project root |
| `-testResults` | string | Output path for NUnit-compatible XML results |
| `-testPlatform` | enum | `EditMode`, `PlayMode`, or a BuildTarget (e.g. `StandaloneWindows64`) |
| `-testCategory` | string | Semicolon-separated categories or regex; prefix `!` to negate |
| `-testFilter` | string | Semicolon-separated test names or regex; supports parameterized tests |
| `-assemblyNames` | string | Semicolon-separated assembly names to include |
| `-forgetProjectPath` | bool | Prevent saving project to Hub history |
| `-playerHeartbeatTimeout` | int | Seconds to wait for player heartbeats (default: 600) |
| `-runSynchronously` | bool | Run all tests in one editor update loop (EditMode only) |
| `-testSettingsFile` | string | Path to a `TestSettings.json` file |
| `-orderedTestListFile` | string | Path to `.txt` file listing tests in execution order |
| `-randomOrderSeed` | int | Non-zero seed to randomize test execution order |
| `-retry` | int | Max retry count for failing tests |
| `-repeat` | int | Max repetition count for passing tests |

## Code Coverage CLI Flags (com.unity.testtools.codecoverage@1.3)

| Flag | Description |
|---|---|
| `-enableCodeCoverage` | Activates coverage collection (required) |
| `-coverageResultsPath <path>` | Where to write results (default: `<ProjectPath>/CodeCoverage`) |
| `-coverageOptions <options>` | Semicolon-separated options string |

Key `-coverageOptions` values:
- `generateHtmlReport` — produces browsable HTML output
- `assemblyFilters:+<Barcade.Core>,-<*.Tests>` — include/exclude assemblies
- `pathFilters:-<*/Tests/*>` — exclude by path

Full coverage command example:
```
Unity.exe -runTests -batchmode -nographics \
  -projectPath . \
  -testPlatform EditMode \
  -testResults results/editmode.xml \
  -enableCodeCoverage \
  -coverageResultsPath results/coverage \
  -coverageOptions "generateHtmlReport;assemblyFilters:+<Barcade.Core>"
```

## asmdef Platform Key for "Editor only"

Setting `"includePlatforms": ["Editor"]` in the `.asmdef` JSON restricts the assembly to the Unity Editor; Unity strips it from every player build automatically. PlayMode test asmdefs leave `includePlatforms` as `[]` but mark `optionalUnityReferences` with `"TestAssemblies"` so the build pipeline still strips them in non-test builds.
