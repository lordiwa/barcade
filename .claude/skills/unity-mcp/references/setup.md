# Unity MCP — Extended Setup Reference

Fetched / verified: 2026-06-18
Primary source: https://github.com/CoderGamester/mcp-unity (v1.3.0, MIT)

---

## Full Installation Walkthrough

### Prerequisites

| Requirement | Version | Check |
|-------------|---------|-------|
| Unity | 2022.3 LTS or 6.x (6000.x) | Unity Hub |
| Node.js | 18 LTS or later | `node --version` |
| npm | 9+ | `npm --version` |

### 1. Add the Unity Package

Open Unity. In the menu:

```
Window > Package Manager > [+] > Add package from git URL
```

Paste:

```
https://github.com/CoderGamester/mcp-unity.git
```

Unity resolves the package and places it in:

```
Library/PackageCache/com.gamelovers.mcp-unity@<HASH>/
```

Where `<HASH>` is a content hash that changes on package upgrades.

### 2. Build the Node.js MCP Server

The Node server lives in `Server~/` inside the package. It is NOT auto-built on import.

**Option A — Use the Unity button (recommended):**
```
Tools > MCP Unity > Server Window > Build Server
```

**Option B — Build manually (PowerShell):**
```powershell
$pkg = (Get-ChildItem "Library\PackageCache" -Filter "com.gamelovers.mcp-unity*" -Directory | Select-Object -First 1).FullName
Push-Location "$pkg\Server~"
npm install
npm run build
Pop-Location
```

Output: `Server~/build/index.js`

### 3. Generate .mcp.json via the Unity UI

```
Tools > MCP Unity > Server Window > Configure > Claude Code
```

This writes `.mcp.json` to the Unity project root with the correct hash-pinned path.
**Commit this file** so CI and other developers get the same config without repeating
the setup.

The generated file looks like:

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": [
        "Library/PackageCache/com.gamelovers.mcp-unity@abc123def456/Server~/build/index.js"
      ],
      "env": {
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

If you need a global (user-level) config instead, add the same block to:
- macOS/Linux: `~/.claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

### 4. Start the WebSocket Bridge in Unity

```
Tools > MCP Unity > Server Window > Start Server
```

The status LED turns green. The bridge runs on `ws://localhost:8090/McpUnity`.
It stays alive as long as the Unity Editor is open.

### 5. Confirm the Connection from Claude Code

In a Claude Code session, run any mcp-unity tool, e.g.:

```
Use mcp-unity to call get_console_logs.
```

A valid (possibly empty) array response confirms end-to-end connectivity.

---

## Changing the Port

Default: `8090`. To change:

1. In Unity: Tools > MCP Unity > Server Window — edit the port field.
2. In `.mcp.json` — update `"UNITY_PORT": "<new-port>"`.
3. Restart the server in Unity.

Port is persisted in `ProjectSettings/McpUnitySettings.json` (commit this file).

---

## WSL2 / Remote Networking

If Claude Code runs inside WSL2 and Unity runs on the Windows host:

1. Set `AllowRemoteConnections: true` in `ProjectSettings/McpUnitySettings.json`.
2. In `.mcp.json` add `"UNITY_HOST": "<windows-host-ip>"` to the `env` block.
3. Use WSL2 mirrored-networking mode (Windows 11 22H2+) to avoid IP changes on
   reboot: edit `%USERPROFILE%\.wslconfig` and add `networkingMode=mirrored`.

---

## Upgrading the Package

1. Package Manager > mcp-unity > Update
2. The `Library/PackageCache` hash changes.
3. Re-run: Tools > MCP Unity > Server Window > Build Server
4. Re-run: Configure > Claude Code (overwrites `.mcp.json` with new hash)
5. Commit updated `.mcp.json` and `ProjectSettings/McpUnitySettings.json`.

---

## Full Tool Inventory (v1.3.0)

### Scene Management
- `create_scene` — create a new scene asset
- `load_scene` — load additively or exclusively
- `save_scene` — flush in-memory changes to disk
- `delete_scene` — remove scene asset
- `unload_scene` — unload without deleting
- `get_scene_info` — query active scene state

### GameObject & Hierarchy
- `select_gameobject` — select in editor hierarchy
- `get_gameobject` — read full component/property data
- `update_gameobject` — set name, active state, tag, layer
- `duplicate_gameobject` — clone an object
- `delete_gameobject` — remove from scene
- `reparent_gameobject` — change parent in hierarchy
- `add_asset_to_scene` — instantiate a prefab/asset

### Transform
- `move_gameobject` — set world/local position
- `rotate_gameobject` — set world/local rotation
- `scale_gameobject` — set local scale
- `set_transform` — batch position + rotation + scale

### Components
- `update_component` — add component or set field values

### Materials & Rendering
- `create_material` — generate a material with a given shader
- `assign_material` — attach material to a Renderer
- `modify_material` — edit material properties
- `get_material_info` — inspect material

### Prefabs & Assets
- `create_prefab` — save GameObject as prefab asset

### Editor & Package Management
- `execute_menu_item` — invoke any Unity menu command by path
- `add_package` — install UPM package

### Testing
- `run_tests` — execute Unity Test Runner (EditMode or PlayMode)

### Scripting
- `recompile_scripts` — trigger Roslyn compilation

### Debugging
- `get_console_logs` — retrieve paginated console entries (type, message, stacktrace)
- `send_console_log` — write a message to the Unity console

### Batch
- `batch_execute` — run multiple tool calls atomically

---

## Known Limitations

- **Domain reload disconnects bridge.** During PlayMode tests with domain reload
  enabled, the WebSocket bridge may drop. Workaround: Edit > Project Settings >
  Editor > "Reload Domain" = off (acceptable for barcade's unit-test style).
- **Compilation must succeed before play mode.** MCP will not enter play mode if
  there are compile errors — which is the correct behaviour; fix errors first.
- **Recompile latency.** `recompile_scripts` + `get_console_logs` has ~2-12 s
  round-trip depending on project size. Agent loops should poll or wait.
- **No C# execution.** MCP drives the editor UI surface; it cannot run arbitrary
  C# at runtime. For that, use Play Mode + a test or a custom Editor script.
- **Path hash fragility.** The `Library/PackageCache` hash changes on package
  upgrade; the `.mcp.json` must be regenerated and committed after upgrades.

---

## Alternative: Unity Official `com.unity.ai.assistant`

Unity ships its own MCP bridge as `com.unity.ai.assistant` (v2.10.0-pre.1 as of
2026-06-18). It uses a relay binary at `~/.unity/relay/relay_win.exe` (Windows) and
exposes tools like `Unity_ManageScene`, `Unity_ManageGameObject`, `Unity_ReadConsole`.

Windows `.mcp.json` entry:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "%USERPROFILE%\\.unity\\relay\\relay_win.exe",
      "args": ["--mcp"]
    }
  }
}
```

Status: still pre-release; each new MCP client must be manually approved in
Edit > Project Settings > AI > Unity MCP > Pending Connections. Not recommended
for barcade until it reaches a stable release. Docs:
https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.10/manual/integration/unity-mcp-get-started.html

---

## References

- CoderGamester/mcp-unity README: https://github.com/CoderGamester/mcp-unity
- CLAUDE.md (agent-specific guidance): https://github.com/CoderGamester/mcp-unity/blob/main/CLAUDE.md
- Unity official MCP docs: https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.10/manual/integration/unity-mcp-get-started.html
- Unity MCP blog post: https://unity.com/blog/unity-ai-mcp-how-to-get-started
- CoplayDev/unity-mcp (alternative, v9.7.3): https://github.com/CoplayDev/unity-mcp
- AnkleBreaker-Studio/unity-mcp-server (268-tool option): https://github.com/AnkleBreaker-Studio/unity-mcp-server
