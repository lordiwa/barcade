---
name: unity-remote-content
description: >
  Load this skill when working on remote updates, over-the-air content delivery,
  Unity Addressables setup, remote catalogs, content update builds, hosting
  AssetBundles (CCD, S3, self-hosted), loading by address at runtime, the
  content_update_group / Check for Content Update Restrictions / Update a
  Previous Build workflow, or the boot-time "cabinet checks server for new
  catalog" pattern. Also covers the hard boundary between what can be pushed
  remotely (assets, ScriptableObjects, prefabs, scenes) versus what requires a
  full binary rebuild (new C# code under IL2CPP). Read before touching any
  remote-update, fleet-management, or Addressables task in barcade.
---

# unity-remote-content

## When to Use This Skill

- Before configuring any Addressable group, profile, or build path.
- When authoring the cabinet boot sequence that checks for new content.
- When deciding whether a microgame change needs a binary push or just an asset push.
- When setting up the content-hosting server (CCD, S3, or self-hosted).
- When implementing offline resilience (bar WiFi is unreliable; cabinet must run
  on last-known-good content if the server is unreachable).
- When designing new microgame types: use this to decide what goes in data vs.
  compiled code.

---

## Core Workflows

### 1. Make an asset remotely updatable

**Package:** `com.unity.addressables` **3.1.0** (released 2026-05-15; requires
Unity 6.0 or later). Install via `Window > Package Manager > Add package by
name`.

**Step-by-step:**

1. **Mark the asset as Addressable.**
   Select any asset in the Project window, tick the "Addressable" checkbox in
   the Inspector, and assign it a stable address string (e.g.
   `Microgames/SpeedTap/Definition`). Address strings are the keys used at
   runtime — keep them namespaced and consistent.

2. **Create a Remote group.**
   `Window > Asset Management > Addressables > Groups > right-click > Create
   New Group > Packed Assets`. In the Group Inspector, set
   **Build & Load Paths = Remote**.

3. **Enable the remote catalog.**
   `Window > Asset Management > Addressables > Settings`. Enable
   **Build Remote Catalog** and set its **Build & Load Paths = Remote**.

4. **Configure the Remote profile.**
   `Window > Asset Management > Addressables > Profiles`. Create a "Production"
   profile. Set `RemoteBuildPath` to a local output folder (e.g.
   `ServerData/[BuildTarget]`) and `RemoteLoadPath` to the public URL where
   bundles will be hosted (e.g. `https://content.barcade.game/[BuildTarget]`
   or a CCD bucket URL).

5. **Set Content Update Restriction per group.**
   - **"Prevent Updates" ON** (recommended for large or rarely-changed bundles):
     if the group's assets change, only the delta moves to a new remote group —
     unchanged bundles are not re-downloaded.
   - **"Prevent Updates" OFF** (for small, frequently-changing groups): any
     change rebuilds the whole bundle; keep these bundles small.

6. **New Full Build (first release).**
   `Window > Asset Management > Addressables > Groups > Build > New Build >
   Default Build Script`. This emits:
   - `catalog_[hash].json` + `catalog_[hash].hash` (the remote catalog + hash
     file)
   - `*.bundle` files
   - `addressables_content_state.bin` (required for future content-update builds
     — commit this to version control or archive it with the release).

7. **Upload all output files** under `RemoteBuildPath` to your hosting server.
   The catalog and hash file must be reachable at the URL in `RemoteLoadPath`.

---

### 2. Build and host a content update (no binary rebuild)

Use this when assets, ScriptableObject data, or prefabs changed but no new C#
types were added and no existing types had their field layout changed.

1. **Prepare: run Check for Content Update Restrictions.**
   `Addressables Groups window > Tools > Check for Content Update Restrictions`.
   Provide the `addressables_content_state.bin` from the last published build.
   The tool moves changed assets in static ("Prevent Updates" ON) groups into a
   new `content_update_group`, so only deltas are re-bundled.

2. **Build: Update a Previous Build.**
   `Addressables Groups window > Build > Update a Previous Build`.
   Provide the same `addressables_content_state.bin`.
   Output: a new `catalog_[hash].json`, `catalog_[hash].hash`, and only the
   changed/new bundle files. Unchanged bundles keep their original filenames —
   clients do not re-download them.

3. **Upload the delta.**
   Copy the new catalog files and any new/changed `*.bundle` files to your
   hosting server, replacing the old catalog pair. Unchanged bundles remain on
   the server untouched.

4. **Cabinets pick it up automatically** on the next boot (see Workflow 3).

---

### 3. Cabinet boot-time update check with offline fallback

Run this coroutine early in your boot scene (before any microgame scene load):

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ContentUpdater : MonoBehaviour
{
    // Set in Inspector or Addressable Settings — seconds before we give up
    // waiting for the remote catalog. Keep low for bar/kiosk use (5-10s).
    [SerializeField] private float catalogTimeoutSeconds = 8f;

    public IEnumerator CheckAndUpdateCatalogs()
    {
        // 1. Initialize Addressables (downloads remote catalog if available).
        //    By default, Addressables auto-checks for a new catalog on Init.
        var initHandle = Addressables.InitializeAsync();
        float elapsed = 0f;
        while (!initHandle.IsDone)
        {
            elapsed += Time.deltaTime;
            if (elapsed > catalogTimeoutSeconds)
            {
                Debug.LogWarning("[ContentUpdater] Catalog check timed out — " +
                                 "running on last-known-good content.");
                Addressables.Release(initHandle);
                yield break;          // <-- offline fallback: use cached bundles
            }
            yield return null;
        }

        if (initHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogWarning("[ContentUpdater] Init failed — offline fallback.");
            Addressables.Release(initHandle);
            yield break;
        }
        Addressables.Release(initHandle);

        // 2. Ask which catalogs have updates available.
        var checkHandle = Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);
        yield return checkHandle;
        if (checkHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Addressables.Release(checkHandle);
            yield break;
        }

        List<string> stale = checkHandle.Result;
        Addressables.Release(checkHandle);

        if (stale == null || stale.Count == 0)
            yield break;   // already up-to-date

        // 3. Download and apply the updated catalog(s).
        var updateHandle = Addressables.UpdateCatalogs(stale, autoCleanBundleCache: true);
        yield return updateHandle;

        if (updateHandle.Status == AsyncOperationStatus.Succeeded)
            Debug.Log($"[ContentUpdater] Catalog updated. {stale.Count} catalog(s) refreshed.");
        else
            Debug.LogWarning("[ContentUpdater] Catalog update failed — offline fallback.");

        Addressables.Release(updateHandle);
        // Assets resolved after this point will use the new catalog.
    }
}
```

**Offline resilience notes:**
- Downloaded AssetBundles are cached on-device by default (Unity's `Caching`
  system). If the server is unreachable the next session, the runtime loads from
  that cache automatically — no special code needed.
- Configure **"Catalog Download Timeout"** in Addressable Asset Settings (and
  per-group timeout) to a low value (5-10 seconds). Without this, the default
  timeout is ~300 seconds, causing a multi-minute freeze on bad WiFi.
- Ship a **baseline set of content baked into the player build** (local
  Addressable groups) so the cabinet is playable from day one with zero network
  access. Remote groups add/replace; local groups are the permanent floor.
- Use `Addressables.DownloadDependenciesAsync(key)` during an idle period (e.g.,
  attract-mode screen) to pre-cache newly-listed bundles before a game session
  starts.

---

## Recommendation for barcade

### Architecture decision

Use a **two-layer Addressables layout** per cabinet build:

| Layer | Group type | Hosting | Content |
|---|---|---|---|
| **Local (built-in)** | Local, Prevent Updates ON | Baked into player binary | 5 core microgame mechanics + framework code-backing prefabs; the minimum viable game |
| **Remote (updatable)** | Remote, Prevent Updates ON | Content server (see Hosting Options) | All microgame-definition ScriptableObjects, variant prefabs, textures, audio, new microgame scenes |

### Data-vs-binary boundary for microgames

**Can be pushed as a remote content update (no binary rebuild):**
- `MicrogameDefinition` ScriptableObjects (title, timer, win-condition
  parameters, difficulty curve data)
- Prefabs that use MonoBehaviour types already compiled into the binary
  (new prefab wiring the same C# scripts differently)
- Scenes that are entirely composed of existing component types
- Textures, sprites, audio clips, animation clips, materials
- Localization tables, config JSON/CSV baked into TextAssets
- New variants of existing microgames (data-only permutations of the 5 core
  mechanics)

**Requires a full binary (player) rebuild:**
- Any new C# class, struct, or interface that doesn't already exist in the
  player binary — IL2CPP compiles C# ahead-of-time; bundles cannot ship new
  managed types
- Changes to the field layout of existing serialized classes (breaks
  deserialization of live bundles)
- Changes to the Addressables package version itself
- Anything that modifies the Unity engine version or platform target

### Implication for microgame authoring

Structure each microgame as: **one base MonoBehaviour class per mechanic** (in
the binary) + **one ScriptableObject definition** (remote). A "new microgame"
is a new definition asset pointing at an existing mechanic class with different
parameters, scenes, and prefabs — deliverable as a remote content update with
no binary push. Only adding a genuinely new *mechanic* (new C# logic) requires
a binary rebuild.

### Catalog-check-on-boot pattern (summary)

Cabinet boots → `ContentUpdater.CheckAndUpdateCatalogs()` runs → sets a short
timeout (8s) → if new catalog available, downloads and applies it → if server
unreachable, falls through to cached bundles silently → game session starts with
the best available content.

---

## Hosting Options

| Option | Best for | Notes |
|---|---|---|
| **Unity Cloud Content Delivery (CCD)** | Lowest ops overhead; tight Editor integration | Managed CDN; CCD Management SDK lets you build and release from inside Unity Editor. Free tier exists; paid tiers by GB delivered. Unity handles geo-distribution. |
| **AWS S3 + CloudFront** | Full control, predictable costs at scale | Set `RemoteLoadPath` to the CloudFront distribution URL. Standard approach; manual upload step (can be scripted). |
| **Any HTTP static file host** (nginx, Azure Blob, GCS) | Self-hosted / existing infrastructure | Addressables only needs a standard HTTP GET on the catalog and bundle files. No special server logic required. |
| **Unity Gaming Services (UGS) Remote Config** | Thin feature-flag layer on top of Addressables | Not a bundle host; use alongside Addressables to control which catalog version to point at. |

For barcade Milestone 1 (thin proof-path), an S3 bucket or any static HTTP host
is sufficient. CCD is recommended for production fleet management because of
the Environments/Badges release model (easy rollback: re-badge "latest" to a
prior release).

---

## Best Practices

- **Always archive `addressables_content_state.bin`** with each full player
  release. Without it you cannot run "Update a Previous Build" — you must do a
  full rebuild.
- **Never rename group names or asset addresses after the first player release.**
  Addresses are the stable keys in the catalog; renaming them breaks live
  cabinets pointing at the old catalog.
- **Keep remote bundles small.** Each "Prevent Updates ON" group that changes
  forces clients to re-download its entire bundle. Prefer many small groups over
  one large group.
- **Version-gate the catalog URL.** Use a profile variable like
  `https://content.barcade.game/v1/[BuildTarget]/` so a future binary rebuild
  with a new type layout can point at a fresh `v2/` path without corrupting
  cabinets still on v1.
- **Test rollback before shipping.** CCD Badges make rollback trivial: point the
  "latest" badge at a prior release snapshot. For S3, keep old catalog+bundle
  sets in a dated prefix and swap the latest symlink/redirect.
- **Use `autoCleanBundleCache: true` in `UpdateCatalogs`** to evict stale
  bundles from the device cache and reclaim disk space on the cabinet.
- **Set catalog and group download timeouts** to 5-10 seconds in
  Addressable Asset Settings to avoid long freezes on bad WiFi.

---

## Common Pitfalls

| Pitfall | Fix |
|---|---|
| `addressables_content_state.bin` lost after a full rebuild | Commit it to git (or archive it) at every player release — it is required for all future content-update builds |
| New C# class shipped in a remote bundle | Crashes on IL2CPP targets — move the type to the player binary first, then ship the bundle |
| Catalog timeout at default (300s) | Cabinet freezes ~5 minutes on lost WiFi — set "Catalog Download Timeout" to 5-10 s |
| Remote catalog not enabled in player build | Addressables never checks for updates — enable "Build Remote Catalog" before the full player build |
| Group renamed or address changed after first release | Old catalogs on live cabinets break — addresses must be treated as permanent public API |
| Bundle with "Prevent Updates" OFF grows large | One-asset change forces full bundle re-download — split into smaller groups |
| `autoReleaseHandle: false` forgotten in `CheckForCatalogUpdates` | Operation handle leaks memory — always release after reading the result |
| Bundles built on one Unity version, player updated to another | Type tree mismatch causes load failures — content-update builds require the same Unity version as the player binary |

---

## Verification

After configuring remote content for the first time:

1. Run "New Build" with the Production profile active. Confirm:
   - `catalog_[hash].json` and `catalog_[hash].hash` appear in `ServerData/`.
   - `addressables_content_state.bin` appears in the Content State Build Path.
2. Upload catalog + bundles to your host. Open the catalog URL in a browser —
   it should return JSON.
3. In a development build, open `Window > Asset Management > Addressables >
   Event Viewer` and load an asset by address. Confirm it downloads from the
   remote URL (not from StreamingAssets).
4. Simulate offline: block the host in your firewall, relaunch. Confirm the
   game starts within `catalogTimeoutSeconds` using cached bundles.
5. Make a small asset change, run "Check for Content Update Restrictions", then
   "Update a Previous Build". Upload only the new files. Relaunch the game;
   confirm the change appears without a binary reinstall.

---

## References

Heavy reference material with official docs URLs and fetch dates:
`.claude/skills/unity-remote-content/references/official-docs-snapshot.md`

---

## Provenance

- Researcher: Claude Sonnet (Researcher subagent)
- Ticket: BOOTSTRAP-UNITY
- Last verified: 2026-06-18
- Sources:
  - https://docs.unity3d.com/Packages/com.unity.addressables@3.1/
  - https://docs.unity3d.com/Packages/com.unity.addressables@3.1/changelog/CHANGELOG.html
  - https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/build-content-catalogs.html
  - https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/LoadContentCatalogAsync.html
  - https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/ContentUpdateWorkflow.html
  - https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/RemoteContentDistribution.html
  - https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/remote-content-assetbundle-cache.html
  - https://docs.unity.com/en-us/ccd
  - https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/ccd-configure.html
  - https://discussions.unity.com/t/addressables-and-loading-remote-bundles-from-cache-if-no-internet/790389
  - https://docs.unity3d.com/6000.3/Documentation/Manual/il2cpp-introduction.html
