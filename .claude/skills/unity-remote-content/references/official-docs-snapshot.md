# Unity Addressables — Official Docs Snapshot

Fetch date: 2026-06-18
Package: com.unity.addressables 3.1.0 (released 2026-05-15, requires Unity 6.0+)

---

## Package version history (recent)

| Version | Date | Notes |
|---|---|---|
| 3.1.0 | 2026-05-15 | Latest stable; reverts breaking changes; fixes Build Report duplication count |
| 2.9.1 | 2026-02-16 | Final 2.x stable |
| 2.8.1 | 2026-01-05 | NullRef fix in Addressables Report window; perf for moving assets between groups |
| 2.7.6 | 2025-11-10 | Null ref on graph node undo; leak fix on pooled InstanceOperation |
| 2.7.4 | 2025-10-06 | Empty custom load path spam fix; Inspector refresh fix |

Unity 6 (6.0+) is required for the 3.x package series. Use 2.x for Unity 2022/2023 LTS.

---

## Key APIs (verified against docs, 2026-06-18)

### Initialization
```csharp
AsyncOperationHandle initHandle = Addressables.InitializeAsync();
// By default, auto-downloads the remote catalog if "Build Remote Catalog" is
// enabled and the hash differs. Set "Only update catalogs manually" in
// Addressable Asset Settings to disable this auto-behavior.
```

### Catalog update check
```csharp
// Returns list<string> of catalog IDs that have newer versions available.
AsyncOperationHandle<List<string>> checkHandle =
    Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);
yield return checkHandle;
List<string> stale = checkHandle.Result;
Addressables.Release(checkHandle);
```

### Catalog update apply
```csharp
// Provide specific list or null to update all. autoCleanBundleCache removes
// stale bundle entries from the local cache.
AsyncOperationHandle<List<IResourceLocator>> updateHandle =
    Addressables.UpdateCatalogs(stale, autoCleanBundleCache: true);
yield return updateHandle;
Addressables.Release(updateHandle);
// Note: UpdateCatalogs blocks all other Addressable requests until complete.
```

### Manual catalog load (secondary/multi-project catalogs)
```csharp
// autoReleaseHandle: true releases after load, preventing future cache conflicts.
AsyncOperationHandle<IResourceLocator> handle =
    Addressables.LoadContentCatalogAsync("https://example.com/catalog.json",
                                         autoReleaseHandle: true);
yield return handle;
```

### Asset loading
```csharp
AsyncOperationHandle<MicrogameDefinition> handle =
    Addressables.LoadAssetAsync<MicrogameDefinition>("Microgames/SpeedTap/Definition");
yield return handle;
MicrogameDefinition def = handle.Result;
// Release when done:
Addressables.Release(handle);
```

### Scene loading
```csharp
AsyncOperationHandle<SceneInstance> handle =
    Addressables.LoadSceneAsync("Microgames/SpeedTap/Scene",
                                LoadSceneMode.Additive);
yield return handle;
```

### Pre-caching (download without loading)
```csharp
// Downloads all dependencies of an address to the local cache.
// Use during attract-mode to pre-warm new content silently.
var sizeHandle = Addressables.GetDownloadSizeAsync("Microgames/SpeedTap/Scene");
yield return sizeHandle;
long bytesNeeded = sizeHandle.Result;
Addressables.Release(sizeHandle);

if (bytesNeeded > 0)
{
    var dlHandle = Addressables.DownloadDependenciesAsync(
        "Microgames/SpeedTap/Scene", autoReleaseHandle: true);
    yield return dlHandle;
}
```

---

## Content update build steps (editor menu paths)

1. `Window > Asset Management > Addressables > Groups`
2. To check which assets moved: `Tools > Check for Content Update Restrictions`
   — provide `addressables_content_state.bin` when prompted.
   - Moves changed assets in "Prevent Updates ON" groups into `content_update_group`.
3. To build: `Build > Update a Previous Build`
   — provide `addressables_content_state.bin` when prompted.
   - Outputs: new `catalog_[hash].json`, `catalog_[hash].hash`, changed bundles only.
4. Upload new catalog pair + changed bundles to host. Do NOT delete unchanged bundles.

---

## Profile variables (default names)

| Variable | Default value | Usage |
|---|---|---|
| `LocalBuildPath` | `[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]` | Output path for local bundles inside the player |
| `LocalLoadPath` | `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]` | Runtime load path for local bundles |
| `RemoteBuildPath` | `ServerData/[BuildTarget]` | Output folder for remote bundles (upload this) |
| `RemoteLoadPath` | `http://localhost/[BuildTarget]` | URL from which the cabinet downloads bundles — **set this to your CDN/server URL** |

---

## Content State file

File: `addressables_content_state.bin`
Default location: `Assets/AddressableAssetsData/[BuildTarget]/`
(configurable via "Content State Build Path" in Addressable Asset Settings)

Must be saved from every published player build. Required input for both:
- "Check for Content Update Restrictions"
- "Update a Previous Build"

Treat it like a release artifact — archive alongside the player binary.

---

## IL2CPP hard boundary (verified 2026-06-18)

Source: https://docs.unity3d.com/6000.3/Documentation/Manual/il2cpp-introduction.html

IL2CPP translates managed C# assemblies to C++ and compiles them into the
platform binary (AOT — ahead-of-time). Consequences for remote content:

**CAN ship remotely (no binary rebuild):**
- Asset bundles containing: prefabs, scenes, ScriptableObjects, textures,
  audio, animation, materials, TextAssets, video
- Prefabs using MonoBehaviour types that already exist in the binary, wired
  differently
- New ScriptableObject instances of existing [Serializable] types with new data
- Scenes composed entirely of existing component types
- Addressable labels/catalog entries pointing at the above

**CANNOT ship remotely (requires full player binary rebuild):**
- New C# classes, structs, interfaces not present in the original binary
- Changed field layout (added/removed/reordered serialized fields) on existing
  types — breaks deserialization of live bundles
- New Unity package versions (e.g., upgrading Addressables itself)
- Engine version changes
- New platform targets

Rule of thumb for barcade: **one C# class per mechanic archetype; infinite
ScriptableObject definitions per class.** New mechanics = binary push (rare).
New microgame variants = asset push (continuous, no cabinet visit needed).

---

## Unity Cloud Content Delivery (CCD) — key facts

Source: https://docs.unity.com/en-us/ccd

- Managed CDN hosted by Unity; global distribution out of the box.
- Concepts: **Environments** (production/staging/dev) > **Buckets** (per
  platform or content type) > **Entries** (individual files) > **Releases**
  (point-in-time snapshots) > **Badges** (named pointers to releases, e.g.
  "latest").
- **Rollback:** re-badge "latest" to a prior release — takes effect on next
  cabinet boot without touching the binary.
- **Editor integration:** CCD Management SDK (`com.unity.services.ccd.management`)
  + Addressables = build and upload from a single editor menu action.
- **`RemoteLoadPath` format for CCD:**
  `https://[project-id].client-api.unity3dusercontent.com/client_api/v1/
  environments/[env-name]/buckets/[bucket-id]/releases/[badge-name]/content/`
  (or use the `[CCDInfo.*]` profile variable helpers).

---

## Addressables Event Viewer (debugging)

`Window > Asset Management > Addressables > Event Viewer`
Requires "Send Profiler Events" enabled in Addressable Asset Settings (dev
builds only). Shows per-frame asset load/unload events, reference counts, and
download progress. Use this to confirm assets are loading from the remote URL
and not from the local StreamingAssets copy.

---

## Offline resilience — implementation notes

1. **Short timeout:** Set "Catalog Download Timeout" in Addressable Asset
   Settings to 5-10 seconds. Also set the group-level timeout > 0.
2. **Cached bundles:** Once downloaded, AssetBundles are cached by Unity's
   `Caching` system. On next launch with no network, the runtime loads from
   cache without any special code.
3. **Local floor:** Always include a local Addressable group (baked into the
   player) with the minimum content set. The cabinet must be playable offline
   from day one.
4. **`Caching.AddCache(directory)`:** If you want to ship a pre-populated cache
   (e.g., bundle the latest remote content into the initial install image),
   call this with the directory containing pre-built bundles. Unity will use
   them as if they were downloaded, and only pull updates when a new hash is
   detected.

---

## Reference URLs (all accessed 2026-06-18)

- Addressables 3.1.0 home: https://docs.unity3d.com/Packages/com.unity.addressables@3.1/
- Changelog 3.1: https://docs.unity3d.com/Packages/com.unity.addressables@3.1/changelog/CHANGELOG.html
- Changelog 2.8: https://docs.unity3d.com/Packages/com.unity.addressables@2.8/changelog/CHANGELOG.html
- Content catalogs (2.0): https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/build-content-catalogs.html
- Manage catalogs at runtime (2.0): https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/LoadContentCatalogAsync.html
- Content update workflow (1.20): https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/ContentUpdateWorkflow.html
- Remote content distribution (1.20): https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/RemoteContentDistribution.html
- AssetBundle caching (2.0): https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/remote-content-assetbundle-cache.html
- Configure CCD (2.7): https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/ccd-configure.html
- CCD overview: https://docs.unity.com/en-us/ccd
- IL2CPP introduction (Unity 6): https://docs.unity3d.com/6000.3/Documentation/Manual/il2cpp-introduction.html
- Offline cache discussion: https://discussions.unity.com/t/addressables-and-loading-remote-bundles-from-cache-if-no-internet/790389
- Content update builds (1.21 overview): https://docs.unity3d.com/Packages/com.unity.addressables@1.21/manual/content-update-builds-overview.html
