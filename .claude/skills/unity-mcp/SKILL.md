---
name: unity-mcp
description: >
  Load this skill when: driving the Unity Editor via MCP from an agent, automating
  GameObject or scene creation, reading Unity console errors or logs from an agent
  session, setting up Unity MCP for the first time, or any task that uses MCP tools
  to interact with the Unity Editor instead of writing code by hand.
---

# unity-mcp

## When to Use This Skill

Load this skill before any agent task that involves:
- Creating or modifying GameObjects, scenes, prefabs, or components via MCP tools
- Reading Unity console output (errors, warnings, logs) from within an agent run
- Triggering play mode or script recompilation from an agent
- First-time setup of Unity MCP on a barcade developer machine
- Diagnosing MCP connection or tool-invocation failures

Do NOT use MCP as a substitute for writing C# — use it to drive the editor (hierarchy
manipulation, asset wiring, console inspection) while the agent generates and edits
actual source files through normal file-write tools.

---

## Core Workflows

### 1. Choose the right MCP implementation

Two verified options exist as of 2026-06-18:

| Option | Repo | Maturity | External runtime |
|--------|------|----------|-----------------|
| **CoderGamester/mcp-unity** (recommended) | https://github.com/CoderGamester/mcp-unity | v1.3.0 stable (Apr 2026), MIT | Node.js 18+ |
| Unity official `com.unity.ai.assistant` | https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.10/ | pre-release (2.10.0-pre.1) | relay binary auto-installed |

**Recommendation for barcade: CoderGamester/mcp-unity.**
It is MIT-licensed, stable, explicitly supports Claude Code, ships 30+ tools covering
every editor operation the team needs, and its `.mcp.json` can be committed to the
repo so every developer and agent gets the same configuration. The official Unity
package is still in pre-release and requires approving each new client connection
manually through Project Settings UI, which is friction for agents.

---

### 2. Install mcp-unity (one-time, per machine)

**Prerequisites**
- Unity 2022.3 LTS or Unity 6 (6000.x)
- Node.js 18+ — verify with `node --version`

**Step A — Add the Unity Editor package**

Window > Package Manager > "+" > Add package from git URL:

```
https://github.com/CoderGamester/mcp-unity.git
```

Unity will install the package under `Library/PackageCache/com.gamelovers.mcp-unity@<hash>/`.
The hash changes on upgrade; see step C for a path-stable alternative.

**Step B — Build the Node server (first install only)**

```powershell
# From the Unity project root
$pkg = (Get-ChildItem "Library\PackageCache" -Filter "com.gamelovers.mcp-unity*" | Select-Object -First 1).FullName
cd "$pkg\Server~"
npm install
npm run build
```

Or use the auto-button: Tools > MCP Unity > Server Window > "Build Server".

**Step C — Register with Claude Code**

Add `.mcp.json` to the Unity project root (commit this file):

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": [
        "Library/PackageCache/com.gamelovers.mcp-unity@<hash>/Server~/build/index.js"
      ],
      "env": {
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

Replace `<hash>` with the actual directory name under `Library/PackageCache/`.
The Unity Editor auto-generates this config when you click
Tools > MCP Unity > Server Window > Configure > Claude Code — use that button
to avoid manual hash-hunting.

**Step D — Start the bridge inside Unity**

Tools > MCP Unity > Server Window > "Start Server"

The status indicator must show green / "Connected" before running any agent.

Full detail: `references/setup.md`

---

### 3. Verify the connection

```bash
# From project root — should print tool list JSON
node Library/PackageCache/com.gamelovers.mcp-unity@*/Server~/build/index.js --list-tools
```

Or in Claude Code, ask: "List available mcp-unity tools." A valid response enumerates
tools like `get_gameobject`, `create_scene`, `run_tests`.

---

### 4. Sample agent automation — scaffold a microgame scene

```
# Prompt to Claude Code agent (pseudocode intent, not literal MCP calls)

1. mcp-unity: create_scene "MicrogameDodge"
2. mcp-unity: create_gameobject "Arena" (parent: root, position: 0,0,0)
3. Write C# file Assets/Barcade/Microgames/Dodge/DodgeController.cs
4. mcp-unity: update_component on "Arena" → add DodgeController
5. mcp-unity: create_prefab from "Arena" → Assets/Barcade/Prefabs/Dodge/Arena.prefab
6. mcp-unity: get_console_logs → check for compile errors
7. If errors: read error text, edit C# file, mcp-unity: recompile_scripts, repeat
8. mcp-unity: run_tests filter:"Dodge" → confirm EditMode tests pass
```

---

### 5. Reading compile errors in an agent loop

```
# Agent pattern
loop:
  write/edit C# file
  mcp-unity: recompile_scripts
  mcp-unity: get_console_logs (filter: Error)
  if no errors: break
  else: parse error, fix C# file, continue
```

`get_console_logs` returns log entries with type (Error/Warning/Log), message text,
stack trace, and timestamp. The agent can parse the message field directly without
human copy-paste.

---

## Best Practices

- **Commit `.mcp.json`** — use the hash-path generated by the editor button so all
  agents on the team share an identical config.
- **Keep the Unity Editor open** while running agent sessions; the bridge is in-process
  and dies when the editor closes.
- **Save scenes before agent runs** (`Ctrl+S` / `save_scene` tool) — MCP operations
  modify the in-memory scene; unsaved changes are lost if Unity crashes.
- **Use `batch_execute`** for multi-step operations (create + parent + add component)
  to reduce round-trips and avoid intermediate dirty-state issues.
- **Write C# files normally** via agent file tools, then call `recompile_scripts` to
  trigger Unity's Roslyn compiler. MCP drives the editor; it does not compile code.
- **Pin microgame test coverage** — after scaffolding a scene via MCP, verify
  win/lose logic with EditMode C# tests, not just by visually checking the hierarchy.

---

## Common Pitfalls

| Pitfall | Cause | Fix |
|---------|-------|-----|
| `Connection refused` on port 8090 | Server not started | Tools > MCP Unity > Server Window > Start Server |
| `<hash>` in path is stale | Package upgraded | Re-run Configure button in Server Window |
| Domain reload disconnects bridge | Play Mode test with domain reload | Disable "Reload Domain" in Editor > Project Settings > Editor |
| `get_console_logs` returns empty | Log cleared before agent ran | Call immediately after the failing operation |
| Agent loops forever on compile error | C# error references a type that doesn't exist yet | Write the missing type file first, then recompile |
| Tools > MCP Unity menu missing | Package not imported | Check Package Manager; re-add git URL |

---

## Verification

After setup, confirm all three layers work:

1. **Unity side** — Server Window shows green "Connected" status.
2. **Node side** — `node .../index.js --list-tools` returns JSON without errors.
3. **Claude Code side** — MCP tool `get_console_logs` returns a valid (possibly empty)
   array when Unity is open.

---

## References

- `references/setup.md` — extended setup steps, WSL2 networking note, upgrade
  procedure, and troubleshooting checklist (fetched 2026-06-18).

---

## Provenance

- Researcher: Claude (Researcher subagent), ticket BOOTSTRAP-UNITY
- Last verified: 2026-06-18
- Sources:
  - https://github.com/CoderGamester/mcp-unity
  - https://github.com/CoderGamester/mcp-unity/blob/main/CLAUDE.md
  - https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.10/manual/integration/unity-mcp-get-started.html
  - https://unity.com/blog/unity-ai-mcp-how-to-get-started
  - https://github.com/CoplayDev/unity-mcp
  - https://github.com/AnkleBreaker-Studio/unity-mcp-server
