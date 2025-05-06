namespace FunkAssetBundles
{
#if UNITY_EDITOR
    using UnityEditor;
#endif
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    [CreateAssetMenu(menuName = "Data/New/AssetBundleData", fileName = "default")]
    public class AssetBundleData : ScriptableObject
    {
        public List<AssetBundleReferenceData> Assets = new List<AssetBundleReferenceData>();
        [Tooltip("Disable to NOT include this bundle in builds. Only do this for editor-specific bundles (advanced usage).")] public bool EnabledInBuild = true;
        [Tooltip("When enabled, all necessary dependencies for every asset in this bundle will be included in this bundle, rather than being dependant on another bundle.")] public bool NoDependencies;
        [Tooltip("When enabled, this bundle will immediately be initialized on startup.")] public bool LoadBundleOnInitialize;
        [Tooltip("When enabled, assets in this bundle will be built as individual bundles.")] public bool PackSeparately;
        [Tooltip("When enabled, only scenes can be placed in this bundle. When disabled, you cannot place scenes in this bundle. This is a Unity limitation.")] public bool SceneBundle;
        [Tooltip("Hide this bundle from the FunkyBundle dropdown list.")] public bool HideInLists;
        [Tooltip("In Editor, loads from this bundle will NOT use AssetDatabase, and instead will only use real asset bundles. Only enable this if you understand the implications.")] public bool ForceLoadInEditor;
        [Tooltip("When the 'isDedicatedServer' is set in the AssetBundleExporter API, this bundle will be skipped. Only enable this if you understand the implications.")] public bool DoNotBuildForDedicatedServer;

        public PackSeparatelyMode PackMode = PackSeparatelyMode.EachFile;
        public List<string> PackCategories = new List<string>()
        {
            "default",
        }; 

        [System.Serializable]
        public enum PackSeparatelyMode
        {
            EachFile    = 1,
            ByCategory  = 2
        }

        [System.NonSerialized] private Dictionary<string, int> _lookupTable = new Dictionary<string, int>(System.StringComparer.Ordinal);

        public void RefreshLookupTable()
        {
#if UNITY_EDITOR
            var refreshAssetDatabsePaths = true;

            if(Application.isPlaying && AssetBundleService.EditorGetAssetDatabaseEnabled())
            {
                refreshAssetDatabsePaths = false; 
            }

            if (refreshAssetDatabsePaths)
            {
                EditorRefreshAssetDatabasePaths();
            }
#endif

            _lookupTable.Clear();

            for (var i = 0; i < Assets.Count; ++i)
            {
                var asset = Assets[i];
                if(_lookupTable.ContainsKey(asset.GUID))
                {
                    Debug.LogError($"Duplicate asset detected in bundle {name}: {asset.AssetBundleReference} {asset.GUID}", this);
                    continue;
                }

                _lookupTable.Add(asset.GUID, i);
            }
        }

        public AssetBundleReferenceData FindByGuidRaw(string guid)
        {
            foreach(var assetReference in Assets)
            {
                if(assetReference.GUID == guid)
                {
                    return assetReference;
                }
            }

            return null;
        }

        public AssetBundleReferenceData FindByGuid(string guid)
        {
            if (_lookupTable == null || _lookupTable.Count == 0)
            {
                RefreshLookupTable();
            }

            if (_lookupTable.TryGetValue(guid, out int index))
            {
                return Assets[index];
            }

            return null;
        }

        public bool ContainsAssetRef(string guid)
        {
            if(_lookupTable == null || _lookupTable.Count == 0)
            {
                RefreshLookupTable();
            }

            return _lookupTable.ContainsKey(guid);
        }

        public string GetPackedBundleDataName(AssetBundleReferenceData dataReference, string platformName, string assetBundleRoot, string defaultBundleFilename)
        {
            if (!PackSeparately)
            {
                return defaultBundleFilename; 
            }

            switch(PackMode)
            {
                case PackSeparatelyMode.EachFile:
                    return $"{assetBundleRoot}/{platformName}/{dataReference.GUID.ToLowerInvariant()}.bundle";
                case PackSeparatelyMode.ByCategory:
                    var packCategory = dataReference.PackCategory;
                    if (string.IsNullOrEmpty(packCategory)) packCategory = "default";
                    return $"{assetBundleRoot}/{platformName}/{name.ToLowerInvariant()}_{packCategory.ToLower()}.bundle";
            }

            return defaultBundleFilename;
        }

#if UNITY_EDITOR
        public void EditorRemoveDuplicateReferences()
        {
            Undo.RecordObject(this, "removed duplicate references");

            for (var i = 0; i < Assets.Count; ++i)
            {
                var assetData = Assets[i];

                for (var j = i + 1; j < Assets.Count; ++j)
                {
                    var otherAssetData = Assets[j];

                    if(assetData.GUID == otherAssetData.GUID)
                    {
                        Assets.RemoveAt(j);
                        --j;

                        continue;
                    }
                }
            }

            EditorUtility.SetDirty(this);
        }

        public void EditorRemoveNullAssetReferences()
        {
            Undo.RecordObject(this, "removed null references");

            for (var i = Assets.Count - 1; i >= 0; --i)
            {
                var assetData = Assets[i];

                var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                if (string.IsNullOrEmpty(assetPath))
                {
                    Assets.RemoveAt(i);
                }
            }

            EditorUtility.SetDirty(this);
        }

        public void EditorAddAssetReference(string guid)
        {
            var bundleReference = AssetBundleService.AssetPathLowerCase(AssetDatabase.GUIDToAssetPath(guid));
            var data = new AssetBundleReferenceData()
            {
                GUID = guid,
                AssetBundleReference = bundleReference,
            };

            var exists = Assets.FindIndex(result => result.GUID.Equals(guid, System.StringComparison.Ordinal)) > -1;
            if (exists)
            {
                // Debug.LogWarning($"[SKIPPED] {bundleReference} [{guid}] is already in this bundle!");
                return;
            }

            Assets.Add(data);

            Debug.Log($"[ADDED] {bundleReference} [{guid}] to bundle {name}", this);

            EditorUtility.SetDirty(this);
        }

        public void EditorRemoveAssetReference(string guid)
        {
            Assets.RemoveAll(result => result.GUID.Equals(guid, System.StringComparison.Ordinal));
            EditorUtility.SetDirty(this);
        }

        public void EditorRemoveOurAssetsFromOtherBundles()
        {
            if (this.NoDependencies)
            {
                Debug.LogWarning($"Because this bundle has 'NoDependencies' enabled, this action was skipped. It's not necessary.");
                return;
            }

            var assetList = new List<AssetBundleData>();
            AssetDatabaseE.LoadAssetsOfType(assetList);

            var any_duplicates = false;

            foreach (var otherBundle in assetList)
            {
                if (otherBundle == this)
                {
                    continue;
                }

                if (otherBundle.NoDependencies)
                {
                    continue;
                }

                var toRemove = new List<AssetBundleReferenceData>();

                foreach (var ourAsset in this.Assets)
                {
                    foreach (var theirAsset in otherBundle.Assets)
                    {
                        if (ourAsset.GUID.Equals(theirAsset.GUID, System.StringComparison.Ordinal))
                        {
                            toRemove.Add(theirAsset);
                        }
                    }
                }

                foreach (var remove in toRemove)
                {
                    otherBundle.Assets.Remove(remove);
                    any_duplicates = true;
                    Debug.Log($"removed {remove.GUID} ({remove.AssetBundleReference}) from {otherBundle.name}", otherBundle);
                }

                EditorUtility.SetDirty(otherBundle);
            }

            if (!any_duplicates)
            {
                Debug.Log($"No duplicates found for {this.name}'s assets.");
            }
        }

        public void EditorRefreshAssetDatabasePaths()
        {
            if (Assets == null)
            {
                Debug.LogError($"null assets?", this);
                return;
            }

            foreach (var asset in Assets)
            {
                if (asset == null)
                {
                    continue;
                }

                if(string.IsNullOrEmpty(asset.GUID))
                {
                    continue;
                }

                var assetPathRaw = AssetDatabase.GUIDToAssetPath(asset.GUID);
                if(string.IsNullOrEmpty(assetPathRaw))
                {
                    continue;
                }

                var assetPath = AssetBundleService.AssetPathLowerCase(assetPathRaw);
                if(string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                asset.AssetBundleReference = assetPath;
            }
        }
#endif

    }
}