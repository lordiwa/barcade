---
project_name: barcade
project_type: other
generated_at: 2026-06-18T22:24:58.119Z
schema_version: 1
---

## Problem
Bars need a zero-setup, instantly-readable 4-player party game that turns volatile, distracted patrons into a competitive crowd in under 60 seconds, playable on a 4-stick + 1-button arcade cabinet.

### Goals
- Establish 5 strong core microgame mechanics as the reusable foundation
- Produce many microgames from those mechanics using simple geometric figures/shapes that are fast to author and instantly legible
- Chain microgames via a WarioWare-style intermission -> microgame cycle with 4 simultaneous controls (stick + A)
- Bar-grade UI: big icons, primary colors (Rojo/Azul/Amarillo/Verde), 3-tier feedback hierarchy, corner-anchored HUD
- Architect for remote-updatable cabinets from a central server (engine choice TBD via research)

### Scope (in)
- 5 core microgame mechanics plus a microgame framework (Ficcion/Meta/Agencia) that makes adding new microgames cheap
- A large set of microgames using simple geometric figures
- Intermission -> microgame core loop
- 4-player local input handling
- Readable HUD with the 3-tier feedback hierarchy

### Scope (out)
- Board phase, capital sinks, traps, and NPC asesinos (planned as milestone 2)
- Crab-mentality sabotage arsenal and sandbox modifiers
- 1v3 asymmetric and Overcooked-style co-op phases
- Bonus-star comeback scoring
- Remote fleet-management implementation (research the approach now, build later)

## Stack
- Engine: Unity (C#) — decision locked 2026-06-18
- Target platform: dedicated arcade cabinet, 4 local players, each with a joystick + single action button
- Remote updates: cabinets updatable from a central server (mechanism under research — likely Unity Addressables remote content)
- Unity MCP available for agent-driven editor automation
- Tech-training Agent Skills for this stack live under `.claude/skills/` — read the relevant skill before doing Unity work

## Testing conventions
Use the testing tool that fits this stack — the project standard is to keep a fast unit suite runnable via the project's default test command, and to write a failing test before any new behavior lands. Tests live next to the code they exercise (or under a top-level tests/ tree, whichever already exists in this repo); follow the local convention rather than introducing a new one.

## Linting and formatting
Run the project's linter and formatter before every commit. If the repo ships a config (e.g., .eslintrc, ruff.toml, .prettierrc, gofmt defaults), defer to it without arguing; if no config exists yet, use the ecosystem-standard tool and add a minimal config rather than reformatting the whole tree in a drive-by change.

## Type-specific guidance (Unity game)
- This is a Unity (C#) game. Read the relevant `.claude/skills/unity-*` training skill before writing engine code.
- Keep game logic decoupled from MonoBehaviour where practical (plain C# classes for microgame rules, scoring, RNG) so it is unit-testable with EditMode tests; reserve PlayMode tests for input/scene integration.
- The microgame framework is the core asset: a new microgame must be cheap to add. Favor a data-driven/base-class pattern over copy-paste scenes.
- Input is fixed: 4 players, each a joystick (stick) + one action button (A). Use the Unity Input System with per-player action maps; never hardcode a single-player keyboard assumption.
- Visuals are simple geometric figures — do not block on art assets; primitives and shapes are the intended aesthetic for milestone 1.
- When in doubt, write the test first for the pure-logic layer; pin microgame win/lose conditions with EditMode tests.
