using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that configures the two Addressables groups required for
    /// the remote-content architecture:
    ///
    ///   "Barcade-Core" (local, built-in)
    ///   — Framework prefabs and assets that must always be available.
    ///   — "Prevent Updates" ON (static group).
    ///   — Build and Load path: Built-In (StreamingAssets).
    ///
    ///   "Barcade-Microgames" (remote, updatable)
    ///   — All 12 <see cref="MicrogameDefinition"/> assets under
    ///     Assets/Barcade/Content/Microgames/ and the pool asset.
    ///   — "Prevent Updates" ON (static group; only deltas re-bundled).
    ///   — Build path: ServerData/[BuildTarget]
    ///   — Load path: {RemoteLoadPath}/[BuildTarget]
    ///
    /// Also enables the remote catalog so the runtime catalog check has something
    /// to poll.
    ///
    /// Safe to re-run: existing groups are cleared and re-populated
    /// (idempotent, consistent with MicrogameContentGenerator).
    ///
    /// Invoke headless via Unity batchmode:
    /// <code>
    ///   -executeMethod Barcade.EditorTools.AddressablesGroupConfigurator.Configure
    /// </code>
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class AddressablesGroupConfigurator
    {
        // ── Group names (stable, never rename after first player release) ──────────

        private const string GroupCore       = "Barcade-Core";
        private const string GroupMicrogames = "Barcade-Microgames";

        // ── Remote profile values ─────────────────────────────────────────────────

        private const string ProfileName        = "Production";
        private const string RemoteBuildPathKey  = "RemoteBuildPath";
        private const string RemoteLoadPathKey   = "RemoteLoadPath";
        private const string RemoteBuildPathVal  = "ServerData/[BuildTarget]";
        // Placeholder URL — replace with your actual CCD bucket or static host.
        private const string RemoteLoadPathVal   = "https://content.barcade.game/v1/[BuildTarget]";

        // ── Label applied to every microgame asset in the remote group ────────────

        private const string MicrogameLabel = "microgame";

        // ── Asset paths ───────────────────────────────────────────────────────────

        private const string MicrogameFolder = "Assets/Barcade/Content/Microgames";
        private const string PoolAssetPath   = "Assets/Barcade/Content/DefaultMicrogamePool.asset";

        // ── Entry point ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates the two Addressables groups and wires the production
        /// remote profile. Idempotent.
        /// </summary>
        [MenuItem("Barcade/Configure Addressables Groups")]
        public static void Configure()
        {
            Debug.Log("[AddressablesGroupConfigurator] Starting Configure...");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(createIfNull: true);
            if (settings == null)
            {
                Debug.LogError("[AddressablesGroupConfigurator] Could not get/create AddressableAssetSettings.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // ── 1. Ensure the Production profile with remote paths ─────────────────
            EnsureProductionProfile(settings);

            // ── 2. Enable remote catalog ──────────────────────────────────────────
            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath = new ProfileValueReference();
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, RemoteBuildPathKey);
            settings.RemoteCatalogLoadPath = new ProfileValueReference();
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, RemoteLoadPathKey);

            // ── 3. Ensure the two groups ──────────────────────────────────────────
            AddressableAssetGroup coreGroup       = EnsureLocalGroup(settings, GroupCore);
            AddressableAssetGroup microgamesGroup = EnsureRemoteGroup(settings, GroupMicrogames);

            // ── 4. Mark microgame definition assets as Addressable in the remote group
            int marked = MarkMicrogameAssets(settings, microgamesGroup);

            // ── 5. Persist ────────────────────────────────────────────────────────
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[AddressablesGroupConfigurator] Done. " +
                $"Groups: '{GroupCore}' (local), '{GroupMicrogames}' (remote). " +
                $"Marked {marked} microgame assets in remote group. " +
                $"Remote catalog enabled. " +
                $"Run 'Barcade/Generate Microgame Content' first if the Content folder is empty.");

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        // ── Group setup ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or retrieves the local (built-in) Core group.
        /// Uses Built-In bundle and load paths; Prevent Updates ON.
        /// </summary>
        private static AddressableAssetGroup EnsureLocalGroup(
            AddressableAssetSettings settings, string groupName)
        {
            var group = settings.FindGroup(groupName)
                     ?? settings.CreateGroup(groupName, setAsDefaultGroup: false,
                                             readOnly: false, postEvent: false,
                                             schemasToCopy: null,
                                             typeof(ContentUpdateGroupSchema),
                                             typeof(BundledAssetGroupSchema));

            // BundledAssetGroupSchema — local (built-in) paths.
            var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
                bundleSchema = group.AddSchema<BundledAssetGroupSchema>();

            bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundleSchema.LoadPath.SetVariableByName(settings,  AddressableAssetSettings.kLocalLoadPath);
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

            // ContentUpdateGroupSchema — Prevent Updates ON (static group).
            var updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
            if (updateSchema == null)
                updateSchema = group.AddSchema<ContentUpdateGroupSchema>();

            updateSchema.StaticContent = true;

            EditorUtility.SetDirty(group);
            Debug.Log($"[AddressablesGroupConfigurator] Local group '{groupName}' configured.");
            return group;
        }

        /// <summary>
        /// Creates or retrieves the remote (updatable) Microgames group.
        /// Uses Production profile remote paths; Prevent Updates ON (delta-only updates).
        /// </summary>
        private static AddressableAssetGroup EnsureRemoteGroup(
            AddressableAssetSettings settings, string groupName)
        {
            var group = settings.FindGroup(groupName)
                     ?? settings.CreateGroup(groupName, setAsDefaultGroup: false,
                                             readOnly: false, postEvent: false,
                                             schemasToCopy: null,
                                             typeof(ContentUpdateGroupSchema),
                                             typeof(BundledAssetGroupSchema));

            // BundledAssetGroupSchema — remote (Production profile) paths.
            var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
                bundleSchema = group.AddSchema<BundledAssetGroupSchema>();

            bundleSchema.BuildPath.SetVariableByName(settings, RemoteBuildPathKey);
            bundleSchema.LoadPath.SetVariableByName(settings,  RemoteLoadPathKey);
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

            // ContentUpdateGroupSchema — Prevent Updates ON (delta-only rebuild).
            var updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
            if (updateSchema == null)
                updateSchema = group.AddSchema<ContentUpdateGroupSchema>();

            updateSchema.StaticContent = true;

            EditorUtility.SetDirty(group);
            Debug.Log($"[AddressablesGroupConfigurator] Remote group '{groupName}' configured.");
            return group;
        }

        // ── Asset marking ─────────────────────────────────────────────────────────

        /// <summary>
        /// Marks every <c>.asset</c> under <c>Assets/Barcade/Content/Microgames/</c>
        /// and the pool asset as Addressable in <paramref name="remoteGroup"/>.
        /// Existing entries are moved to the remote group if they were in another group.
        /// The label <c>microgame</c> is applied to each.
        /// </summary>
        private static int MarkMicrogameAssets(
            AddressableAssetSettings settings, AddressableAssetGroup remoteGroup)
        {
            // Ensure the label exists.
            settings.AddLabel(MicrogameLabel);

            var assetPaths = new List<string>();

            // All .asset files in the microgame folder.
            if (AssetDatabase.IsValidFolder(MicrogameFolder))
            {
                string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { MicrogameFolder });
                foreach (string guid in guids)
                    assetPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else
            {
                Debug.LogWarning(
                    "[AddressablesGroupConfigurator] Microgame content folder not found: " +
                    MicrogameFolder + ". Run 'Barcade/Generate Microgame Content' first.");
            }

            // Pool asset.
            if (File.Exists(Path.GetFullPath(PoolAssetPath)))
                assetPaths.Add(PoolAssetPath);

            int count = 0;
            foreach (string assetPath in assetPaths)
            {
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(assetGuid)) continue;

                // GetOrCreate the entry (moves it to remoteGroup if it was elsewhere).
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                    assetGuid, remoteGroup, readOnly: false, postEvent: false);

                if (entry == null) continue;

                // Stable address: use the asset filename without extension.
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                entry.address = "Microgames/" + fileName;

                // Apply label so the remote pipeline can batch-update just this group.
                entry.SetLabel(MicrogameLabel, enable: true, postEvent: false);

                count++;
            }

            Debug.Log($"[AddressablesGroupConfigurator] Marked {count} assets in '{remoteGroup.Name}'.");
            return count;
        }

        // ── Profile setup ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures a "Production" profile exists with the correct remote build/load paths.
        /// Creates the profile if absent; updates the path variables if already present.
        /// </summary>
        private static void EnsureProductionProfile(AddressableAssetSettings settings)
        {
            var profiles   = settings.profileSettings;
            string profileId = profiles.GetProfileId(ProfileName);

            if (string.IsNullOrEmpty(profileId))
            {
                // Create from the Default profile.
                string defaultId = profiles.GetProfileId("Default");
                profileId = profiles.AddProfile(ProfileName, defaultId);
                Debug.Log($"[AddressablesGroupConfigurator] Created profile '{ProfileName}'.");
            }
            else
            {
                Debug.Log($"[AddressablesGroupConfigurator] Profile '{ProfileName}' already exists.");
            }

            // Ensure variable exists and set its value for the Production profile.
            EnsureProfileVariable(settings, profileId, RemoteBuildPathKey, RemoteBuildPathVal);
            EnsureProfileVariable(settings, profileId, RemoteLoadPathKey,  RemoteLoadPathVal);
        }

        private static void EnsureProfileVariable(
            AddressableAssetSettings settings,
            string profileId,
            string key,
            string value)
        {
            var profiles = settings.profileSettings;

            // If the variable doesn't exist at all, add it.
            if (!profiles.GetVariableNames().Contains(key))
                profiles.CreateValue(key, value);

            // Set the value for this specific profile.
            profiles.SetValue(profileId, key, value);
        }
    }
}
