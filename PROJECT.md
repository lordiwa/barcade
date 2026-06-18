---
name: barcade
type: other
created_at: 2026-06-18T22:24:58.113Z
schema_version: 1
---

# barcade

## Description
Zero-setup 4-player arcade-bar party game built around fast microgames, designed for a 4-stick + 1-button cabinet

## Target users
Bar patrons playing a shared 4-control arcade cabinet

## Primary use cases
- collaboration
- other

## Success criteria
A crowd can walk up and play many distinct microgames back-to-back with zero instructions; the 5 core mechanics feel good and recombine into varied games

## Problem
Bars need a zero-setup, instantly-readable 4-player party game that turns volatile, distracted patrons into a competitive crowd in under 60 seconds, playable on a 4-stick + 1-button arcade cabinet.

## Goals
- Establish 5 strong core microgame mechanics as the reusable foundation
- Produce many microgames from those mechanics using simple geometric figures/shapes that are fast to author and instantly legible
- Chain microgames via a WarioWare-style intermission -> microgame cycle with 4 simultaneous controls (stick + A)
- Bar-grade UI: big icons, primary colors (Rojo/Azul/Amarillo/Verde), 3-tier feedback hierarchy, corner-anchored HUD
- Architect for remote-updatable cabinets from a central server (engine: Unity, locked 2026-06-18)

## Tech stack
- Engine: Unity (C#) — decision locked 2026-06-18
- Target platform: dedicated arcade cabinet (4 joysticks + 1 action button per player)
- Remote updates: cabinets must be updatable from a central server (mechanism under research — likely Unity Addressables remote content; fleet-management implementation deferred to a later milestone)
- Unity MCP available for agent-driven editor automation

## Scope (in)
- 5 core microgame mechanics plus a microgame framework (Ficcion/Meta/Agencia) that makes adding new microgames cheap
- A large set of microgames using simple geometric figures
- Intermission -> microgame core loop
- 4-player local input handling
- Readable HUD with the 3-tier feedback hierarchy

## Scope (out)
- Board phase, capital sinks, traps, and NPC asesinos (planned as milestone 2)
- Crab-mentality sabotage arsenal and sandbox modifiers
- 1v3 asymmetric and Overcooked-style co-op phases
- Bonus-star comeback scoring
- Remote fleet-management implementation (research the approach now, build later)
