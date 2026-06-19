# Barcade Fast Tests

Fast `dotnet test` runner for pure-logic `Barcade.Core` — no Unity required.

## Quick start

```powershell
"$HOME/.dotnet/dotnet" test D:/barcade/fast-tests/Barcade.Core.FastTests
```

On first use, install the .NET 8 SDK user-scoped (no admin):

```powershell
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$env:USERPROFILE\dotnet-install.ps1" -UseBasicParsing
& "$env:USERPROFILE\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
```

## When to use which runner

Use **dotnet test** (this project) for all pure `Barcade.Core` logic: player-slot rules,
input snapshot helpers, seeded-RNG contracts, and any future logic class that has no
`UnityEngine` dependency. The suite runs in under two seconds and is the default inner
loop for TDD on pure-logic tickets. Use **Unity batchmode** (EditMode / PlayMode) for
engine-dependent tests — input system integration, scene setup, HUD MonoBehaviours,
and Addressables — and for a final integration pass before shipping. Test files shared
between the two runners must stay pure NUnit (no `using UnityEngine` or `[UnityTest]`)
to remain dotnet-runnable; engine-specific test files stay Unity-only and are never
linked into this project.
