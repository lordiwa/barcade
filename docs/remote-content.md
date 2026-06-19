# Remote Content Architecture — Runbook

**Ticket:** TASK-016  
**Last updated:** 2026-06-19  
**Milestone:** 1 (thin proof-path)

---

## Overview

Barcade uses a two-layer Addressables layout to let operators push microgame
content updates to all cabinets over WiFi without reinstalling the binary.

| Layer | Group name | Hosting | Content |
|---|---|---|---|
| **Local (built-in)** | `Barcade-Core` | Baked into the player binary (StreamingAssets) | Framework runtime assets, base prefabs — the minimum viable game |
| **Remote (updatable)** | `Barcade-Microgames` | Content server (see Hosting Options) | All `MicrogameDefinition` ScriptableObjects, the `DefaultMicrogamePool` asset |

Both groups use **Prevent Updates ON** (static groups). When assets in a static
group change, only the changed assets are moved to a generated `content_update_group`
delta bundle — unchanged bundles are never re-downloaded by clients.

---

## One-Time Setup (first player release)

### Step 1 — Generate content assets (if not already done)

```
Unity.exe -batchmode -projectPath D:/barcade/Barcade \
  -executeMethod Barcade.EditorTools.MicrogameContentGenerator.GenerateAll \
  -logFile generate.log -quit
```

Creates 12 `MicrogameDefinition` assets under `Assets/Barcade/Content/Microgames/`
and `Assets/Barcade/Content/DefaultMicrogamePool.asset`.

### Step 2 — Configure Addressables groups

```
Unity.exe -batchmode -projectPath D:/barcade/Barcade \
  -executeMethod Barcade.EditorTools.AddressablesGroupConfigurator.Configure \
  -logFile configure.log -quit
```

This tool (idempotent, re-runnable):
- Creates or updates the `Barcade-Core` group (local, StreamingAssets paths).
- Creates or updates the `Barcade-Microgames` group (remote, Production profile paths).
- Marks all 12 microgame definition assets + the pool asset as Addressable in
  `Barcade-Microgames`, each with the `microgame` label and address
  `Microgames/<filename>`.
- Creates (or verifies) a **Production profile** with:
  - `RemoteBuildPath` = `ServerData/[BuildTarget]`
  - `RemoteLoadPath`  = `https://content.barcade.game/v1/[BuildTarget]`
    *(replace this URL with your actual host before building)*
- Enables **Build Remote Catalog** in Addressable Asset Settings.

> **Before your first real build:** open the Production profile in
> `Window > Asset Management > Addressables > Profiles` and update
> `RemoteLoadPath` to your actual hosting URL.

### Step 3 — New Full Build

*This step must be run in the interactive Unity Editor or via a build script that
calls `AddressableAssetSettings.BuildPlayerContent()`. The batchmode Addressables
build API requires the Editor scripting backend to be active.*

In the Unity Editor:
1. Open `Window > Asset Management > Addressables > Groups`.
2. Select the **Production** profile in the Profile dropdown.
3. Click **Build > New Build > Default Build Script**.

Output files:
- `ServerData/StandaloneWindows64/catalog_<hash>.json`
- `ServerData/StandaloneWindows64/catalog_<hash>.hash`
- `ServerData/StandaloneWindows64/*.bundle` files
- `Assets/AddressableAssetsData/addressables_content_state.bin`

**Archive `addressables_content_state.bin` with this player release.** It is
required for all future content-update builds. Without it you must do a full
player rebuild.

### Step 4 — Upload to host

Upload the entire `ServerData/StandaloneWindows64/` directory to your hosting
server so files are reachable at `RemoteLoadPath/StandaloneWindows64/`.

Verify the catalog URL in a browser:
```
https://content.barcade.game/v1/StandaloneWindows64/catalog_<hash>.json
```
Should return JSON (the Addressables catalog).

---

## Hosting Options

| Option | Best for | Notes |
|---|---|---|
| **Unity Cloud Content Delivery (CCD)** | Lowest ops overhead | Managed CDN; Environments + Badges release model makes rollback trivial (re-badge "latest" to prior release). |
| **AWS S3 + CloudFront** | Full control, predictable cost | Set `RemoteLoadPath` to the CloudFront URL. Manual upload, scriptable via AWS CLI. |
| **Any HTTP static host** (nginx, Azure Blob, GCS) | Self-hosted / existing infra | Addressables needs only a standard HTTP GET. No special server logic. |

For Milestone 1 a local nginx or any S3-compatible bucket is sufficient.
CCD is recommended for production fleet management.

### CCD quick-start

```bash
# Install Unity CCD CLI
npm install -g @unity/ucd-cli

# Upload a release
ucd release create --bucket <bucket-id> --entry-path "ServerData/StandaloneWindows64" \
  --notes "Milestone 1 initial release"

# Badge it as the active release
ucd badge update --badge-name latest --release-num 1 --bucket <bucket-id>
```

Set `RemoteLoadPath` to the CCD bucket URL:
`https://<project-id>.client-api.unity3dusercontent.com/client_api/v1/buckets/<bucket-id>/releases/latest/entries/`

---

## Content Update Workflow (no binary rebuild)

Use this when microgame SOs, pool config, or any asset in `Barcade-Microgames`
changed but **no new C# types were added**.

### Step 1 — Check for content update restrictions

In Unity Editor:
`Addressables Groups window > Tools > Check for Content Update Restrictions`

Provide the `addressables_content_state.bin` from the last player release.
The tool moves changed assets in static groups into `content_update_group` bundles.

### Step 2 — Build the delta

`Addressables Groups window > Build > Update a Previous Build`

Provide the same `addressables_content_state.bin`.

Output: a new `catalog_<hash>.json`, `catalog_<hash>.hash`, and only the
changed/new bundle files. Unchanged bundles keep their original filenames —
clients do not re-download them.

### Step 3 — Upload the delta

Copy the new catalog files and any new/changed `.bundle` files to your host,
replacing the old catalog pair. Unchanged bundles remain on the server untouched.

### Step 4 — Cabinets pick it up automatically

On the next boot the `ContentUpdater` coroutine will detect the new catalog,
download and apply the update within the 8-second timeout window.

---

## Versioning and Rollback

### URL versioning

The Production profile `RemoteLoadPath` is versioned:
`https://content.barcade.game/v1/[BuildTarget]/`

When a binary rebuild changes IL2CPP type layouts, create a new path prefix
(`v2/`, etc.) and point the new binary at it. Old cabinets still on v1 continue
loading from the v1 prefix; new cabinets load from v2.

**Never change the `v1` prefix's content in a way that breaks existing binaries.**

### CCD rollback (recommended)

CCD Environments + Badges makes rollback a single command:

```bash
# Roll back to release 3
ucd badge update --badge-name latest --release-num 3 --bucket <bucket-id>
```

Cabinets pick up the rolled-back catalog on next boot.

### S3 rollback

Keep dated prefixes in S3 and swap the catalog pair:

```
s3://content-bucket/v1/StandaloneWindows64/2026-06-19/   ← archived release
s3://content-bucket/v1/StandaloneWindows64/              ← active (symlinked / redirected)
```

To roll back, copy the archived catalog + hash into the active path.

---

## Data-vs-Binary Boundary

This table defines what must happen when content changes. Consult it before
every update to decide whether a full binary rebuild is required.

| Change type | Delivery method | Requires binary rebuild? |
|---|---|---|
| `MicrogameDefinition` ScriptableObject field values (title, timer, difficulty) | Remote content update | No |
| `DefaultMicrogamePool` asset (add/remove entries) | Remote content update | No |
| Prefab re-wired with *existing* MonoBehaviour components | Remote content update | No |
| New scene using *only* existing component types | Remote content update | No |
| Textures, sprites, audio clips, animation clips | Remote content update | No |
| Localization tables, config JSON/CSV as TextAssets | Remote content update | No |
| New variant microgame (same mechanic, different data) | Remote content update | No |
| **New C# class, struct, or interface** | Full binary rebuild required | **Yes — IL2CPP compiles C# ahead-of-time; bundles cannot ship new managed types** |
| **Field added/removed/renamed on an existing serialized class** | Full binary rebuild required | **Yes — breaks deserialization of live bundles on cabinets** |
| Unity engine version upgrade | Full binary rebuild required | Yes |
| Addressables package version change | Full binary rebuild required | Yes |
| New MonoBehaviour type used in a prefab | Full binary rebuild required | Yes — the type must exist in the binary before the bundle referencing it can load |

### Implication for microgame authoring

Each microgame mechanic is a **MonoBehaviour class in the binary**. A "new
microgame" is a new `MicrogameDefinition` ScriptableObject asset pointing at an
existing mechanic class with different parameters — deliverable as a remote
content update with no binary push.

Only adding a genuinely new *mechanic* (new C# game logic class) requires a
binary rebuild. New mechanics are infrequent; new microgame variants are
frequent. This split keeps over-the-air updates fast and cheap.

---

## Boot-Time Update Check

`ContentUpdater` (in `Barcade.Framework`) runs a coroutine at boot:

1. `Addressables.InitializeAsync()` — contacts the remote catalog URL.
2. `Addressables.CheckForCatalogUpdates()` — discovers stale catalogs.
3. `Addressables.UpdateCatalogs()` — downloads and applies the delta.

Each step is bounded by a **8-second timeout** (Inspector-configurable via
`_catalogTimeoutSeconds`). If any step times out or fails, the coroutine logs
a warning and returns immediately — the game proceeds with cached/local content.

The decision logic lives in `ContentUpdatePolicy` (pure C#, no Unity refs) and
is covered by the fast-test suite (`ContentUpdatePolicyTests`).

### Wiring ContentUpdater in Boot.unity

Add `ContentUpdater` to a GameObject in Boot.unity alongside `GameBootstrapper`.
Call the update check before loading the Manager scene:

```csharp
// In GameBootstrapper.Start():
var updater = GetComponent<ContentUpdater>();
if (updater != null)
    yield return StartCoroutine(updater.CheckAndUpdateCatalogs());

// Then load the Manager scene...
var op = SceneManager.LoadSceneAsync(_managerScene, LoadSceneMode.Additive);
yield return op;
```

### Offline resilience

- Downloaded bundles are cached on-device by Unity's `Caching` system.
- If the server is unreachable the next session, the runtime loads from cache
  automatically — no special code needed.
- The `Barcade-Core` local group bakes the minimum viable game into the binary,
  so the cabinet is playable with zero network access from day one.

---

## Verification Checklist (post-configure)

After running `AddressablesGroupConfigurator.Configure` and building:

1. Open `Window > Asset Management > Addressables > Groups`. Confirm:
   - `Barcade-Core` group exists, schema shows Local paths.
   - `Barcade-Microgames` group exists, schema shows Remote/Production paths.
   - All 12 microgame definition assets + pool appear in `Barcade-Microgames`.
2. Open `Window > Asset Management > Addressables > Settings`. Confirm:
   - "Build Remote Catalog" is enabled.
3. Run New Full Build (Production profile). Confirm `ServerData/` contains
   `catalog_*.json`, `catalog_*.hash`, and bundle files.
4. Upload to host. Confirm catalog URL returns JSON in browser.
5. In a development build, open `Window > Asset Management > Addressables > Event Viewer`.
   Load a microgame definition by address (e.g. `Microgames/esquiva-d1-00`).
   Confirm it downloads from the remote URL (not from StreamingAssets).
6. Simulate offline: block the host in your firewall, relaunch. Confirm the game
   starts within 8 seconds using cached bundles.
7. Make a small asset change, run "Check for Content Update Restrictions", then
   "Update a Previous Build". Upload only the new files. Relaunch; confirm the
   change appears without a binary reinstall.
