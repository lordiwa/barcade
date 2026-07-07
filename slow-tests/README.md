# Barcade Slow Sweep (statistical fairness tier)

Slow-tier `dotnet test` project (`Barcade.Core.SlowTests`) — the statistical
escapability / fairness sweep for the competitive mechanics. **Kept out of the
per-push fast leg on purpose:** a 1000+-seed × multi-mechanic sweep takes several
seconds to minutes, so it never runs on `push`.

## What it does (TASK-063)

For each competitive mechanic it runs the mechanic's **optimal-play oracle** — the
same solver bot the fast suite proved on its ~20-seed set, extracted verbatim
under `Oracles/` — across **≥1000 seeds** (per hazard pattern where the mechanic
has patterns) and asserts the GDD fairness property on **every** seed:

| Mechanic | Oracle | Property asserted | Seeds |
|----------|--------|-------------------|-------|
| MECH_02 ¡ESQUIVA! | `EsquivaEscapeBot` (reactive dodger) | optimal bot survives the full duration — no forced loss | 1000 × 4 patterns (Rain/Sides/Cross/HomingSoft) |
| MECH_01 ¡MANTÉN! | `MantenCorrectionOracle` (bang-bang balancer) | perfect correction survives the full duration | 1000 × 4 seats |
| MECH_01 ¡MANTÉN! | none (null input) | with no input every pendulum falls *before* time — non-vacuity | 1000 × 4 seats |
| MECH_03 ¡CORRE! | `CorrePerfectRunner` (6 Hz mash + timed jumps) | perfect jumps finish unstunned — track always jumpable | 1000 × 4 seats |
| MECH_03 ¡CORRE! | none (identical mash) | rubber-band never inverts two identical-mash players | 1000 |

The oracles reuse the **same shipped sims** (`EsquivaMicrogame` /
`MantenMicrogame` / `CorreMicrogame`) and the **same PCG32 `SeededRandom`** as the
fast suite, so results are meaningful against the real mechanics. Seeds `0..19`
overlap the fast suite's own sweeps, so they double as a parity check on the
extracted oracles.

Asymmetric mechanics (TASK-040) are a **deferred addition** — add a sweep here as
that mechanic's optimal-play oracle lands, following the same surface-and-fail
shape.

## Surfacing (not hiding)

A violated property does **not** fail-fast and hide the rest. Every failing seed
is collected, its `seed + property + diagnostic` logged (so a human can reproduce
it), and only then is the test failed with the full tally. A real fairness
regression surfaces the *whole* bad set for design review, not just the first bad
seed.

## Run it on demand

```powershell
"$HOME/.dotnet/dotnet" test D:/barcade/slow-tests/Barcade.Core.SlowTests
```

Filter to one mechanic:

```powershell
"$HOME/.dotnet/dotnet" test D:/barcade/slow-tests/Barcade.Core.SlowTests --filter "FullyQualifiedName~Esquiva"
```

Scale the seed count: bump `SweepSeeds` in `FairnessSweepTests.cs` (the sweeps run
in a few seconds at 1000 and scale linearly).

## CI

`.github/workflows/slow-sweep.yml` runs this project on **`workflow_dispatch`** and
a **weekly `schedule`** — never on `push`. The per-push fast leg
(`.github/workflows/fast-tests.yml`, `Barcade.Core.FastTests`, 592 green) is
untouched by this project and stays byte-identical.
