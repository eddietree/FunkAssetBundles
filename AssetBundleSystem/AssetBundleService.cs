#pragma warning disable CS0162 // Unreachable code detected

namespace FunkAssetBundles
{
#if UNITY_EDITOR
    using UnityEditor;
#endif
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Profiling;

    [DefaultExecutionOrder(-10000)]
    public class AssetBundleService : MonoBehaviour
    {
        public List<AssetBundleData> AssetBundleDatas = new List<AssetBundleData>();
        public static AssetBundleService Instance;

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Multiple AssetBundleServices are enabled. Disabling this one.", gameObject);
#endif

                this.enabled = false;
                return;
            }

            Instance = this;
            // Instance.Initialize(); 
        }

        public string BuildDebugStats()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"{_bundleCache.Count:N0} bundles currently initialized.");
            sb.AppendLine($"{_assetCache.Count:N0} assets currently cached.");

            long memoryFromAssets = 0;

            foreach(var assetCache in _assetCache)
            {
                var data = assetCache.Value;
                if(data.Asset != null)
                {
                    memoryFromAssets += Profiler.GetRuntimeMemorySizeLong(data.Asset);
                }
            }

            sb.AppendLine($"{memoryFromAssets / 1000:N0} KB of runtime memory used by cached assets.");

            return sb.ToString();
        }

        public static string GetBundlesDeployFolder()
        {
            return System.IO.Path.Combine(Application.streamingAssetsPath, "bundles");
        }

        public const bool PRELOAD_BUNDLES_IN_MEMORY = false;

#if UNITY_EDITOR
        // when true, the editor will load things from AssetDatabase instead of from asset bundles. 
        // this is useful because asset bundles will not need to be built while making editor changes.
        // public const bool USE_ASSETDATABASE = true;
        public const bool CACHE_IN_EDITOR = true;
        public const bool DEBUG_DELAY_RANDOMIZE = false;
        public const float DEBUG_DELAY_CONSTANT = 0.00f;

        public T EditorLoadFromAssetDatabase<T>(AssetReference<T> reference, bool logErrors = true) where T : Object
        {
            var bundle = EditorFindContainerBundle(reference.Guid);
            if (bundle == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"[{reference}] is not in any bundle. this is okay in the editor - but not in builds. Add it to a bundle!");
                }
            }

            if (CACHE_IN_EDITOR && Application.isPlaying)
            {
                var cachedResult = TryGetCachedResult(reference);
                if (cachedResult != null)
                {
                    return cachedResult;
                }
            }

            var asset = reference.EditorLoadAsset(this);

            if (CACHE_IN_EDITOR && Application.isPlaying)
            {
                if(!_assetCache.ContainsKey(reference.GetCacheKey()))
                {
                    _assetCache.Add(reference.GetCacheKey(), new AssetCache()
                    {
                        Guid = reference.Guid,
                        SubAssetReference = reference.SubAssetReference,

                        // setup by CacheResult()
                        Asset = null,

                        // not used in editor 
                        // Bundle = null,
                        Request = null,
                    });
                }

                CacheResult(reference, asset);
            }

            return asset;
        }
#endif

#if UNITY_EDITOR
        [MenuItem("Build System/Asset Bundles/Settings/Enable Asset Database (no bundles in editor)")]
        public static void EditorCommandEnableAssetDatabase()
        {
            EditorPrefs.SetBool("FunkAssetBundles.EnableAssetDatabase", true);
        }

        [MenuItem("Build System/Asset Bundles/Settings/Disable Asset Database (use asset bundles in editor)")]
        public static void EditorCommandDisableAssetDatabase()
        {
            EditorPrefs.SetBool("FunkAssetBundles.EnableAssetDatabase", false);
        }

        [MenuItem("Build System/Asset Bundles/Settings/Enable Asset Database (no bundles in editor)", validate = true)]
        public static bool EditorCommandEnableAssetDatabaseValidate()
        {
            return !EditorGetAssetDatabaseEnabled();
        }

        [MenuItem("Build System/Asset Bundles/Settings/Disable Asset Database (use asset bundles in editor)", validate = true)]
        public static bool EditorCommandDisableAssetDatabaseValidate()
        {
            return EditorGetAssetDatabaseEnabled();
        }

        public static bool EditorGetAssetDatabaseEnabled()
        {
            return EditorPrefs.GetBool("FunkAssetBundles.EnableAssetDatabase", true);
        }
#endif

        [System.NonSerialized] private Dictionary<string, AssetBundle> _bundleCache = new Dictionary<string, AssetBundle>(System.StringComparer.Ordinal);
        [System.NonSerialized] private Dictionary<string, AssetCache> _assetCache = new Dictionary<string, AssetCache>(System.StringComparer.Ordinal); // key = GUID+SubAssetReference
        [System.NonSerialized] private Coroutine _prewarmRoutine;

        private struct AssetCache
        {
            public string Guid;
            public string SubAssetReference;
            // public AssetBundle Bundle;
            public string BundleName;
            public AssetBundleRequest Request;
            public Object Asset;
        }

#if UNITY_EDITOR
        private static AssetBundleService _editorStaticInstance;
        [System.NonSerialized] private bool _editorHasRefreshedBundleData;

        public static void EditorRefreshAssetBundleListOnPrefab()
        {
            EditorFindBundleService();

            if (_editorStaticInstance != null)
            {
                AssetDatabaseE.LoadScriptableObjects(_editorStaticInstance.AssetBundleDatas, removeNullEntries: true);
                EditorUtility.SetDirty(_editorStaticInstance);
            }
        }

        public static AssetBundleData EditorFindContainerBundle(string guid)
        {
            EditorFindBundleService();

            if (!_editorStaticInstance._editorHasRefreshedBundleData)
            {
                _editorStaticInstance._editorHasRefreshedBundleData = true;

                foreach (var data in _editorStaticInstance.AssetBundleDatas)
                {
                    if (data == null)
                    {
                        continue;
                    }

                    data.RefreshLookupTable();
                }
            }

            if (_editorStaticInstance != null)
            {
                foreach (var data in _editorStaticInstance.AssetBundleDatas)
                {
                    if(data == null)
                    {
                        continue;
                    }

                    if (data.ContainsAssetRef(guid))
                    {
                        return data;
                    }
                }
            }

            return null;
        }

        public static string AssetPathLowerCase(string s)
        {
            return s.ToLowerInvariant();
        }

        public static void EnsureReferenceInBundle(AssetBundleData bundleData, string guid, bool removeFromOtherBundles = false)
        {
            var assetData = new AssetBundleReferenceData()
            {
                GUID = guid,
                AssetBundleReference = AssetPathLowerCase(AssetDatabase.GUIDToAssetPath(guid)),
            };

            if (!bundleData.Assets.Contains(assetData))
            {
                bundleData.Assets.Add(assetData);
            }

            EditorUtility.SetDirty(bundleData);

            if(removeFromOtherBundles)
            {
                var bundleService = EditorFindBundleService();
                foreach(var otherBundleData in bundleService.AssetBundleDatas)
                {
                    if(otherBundleData == null)
                    {
                        continue;
                    }

                    if(otherBundleData == bundleData)
                    {
                        continue;
                    }

                    otherBundleData.EditorRemoveAssetReference(guid); 
                }
            }
        }

        public static AssetBundleData EditorFindBundleByName(string bundleName)
        {
            EditorFindBundleService();

            if (_editorStaticInstance != null && _editorStaticInstance.AssetBundleDatas != null)
            {
                foreach (var bundleData in _editorStaticInstance.AssetBundleDatas)
                {
                    if (bundleData.name == bundleName)
                    {
                        return bundleData;
                    }
                }
            }

            return null;
        }

        public static void EnsureReferenceInAnyBundle(string guid, AssetBundleData defaultBundleData = null)
        {
            var existingContainer = EditorFindContainerBundle(guid);
            if (existingContainer == null)
            {
                if (defaultBundleData == null && _editorStaticInstance != null && _editorStaticInstance.AssetBundleDatas.Count > 0)
                {
                    defaultBundleData = _editorStaticInstance.AssetBundleDatas[0];
                }

                if (defaultBundleData != null)
                {
                    EnsureReferenceInBundle(defaultBundleData, guid);
                }
            }
        }

        public static void EditorUpdateBundleReferencesForBuilds()
        {
            EditorFindBundleService();

            var any_change = false;

            if (_editorStaticInstance != null)
            {
                foreach (var bundleData in _editorStaticInstance.AssetBundleDatas)
                {
                    if(bundleData == null)
                    {
                        continue;
                    }

                    foreach (var asset in bundleData.Assets)
                    {
                        var assetPath = AssetPathLowerCase(AssetDatabase.GUIDToAssetPath(asset.GUID));
                        if (string.IsNullOrEmpty(assetPath)) continue;

                        asset.AssetBundleReference = assetPath;
                        any_change = true;

                        EditorUtility.SetDirty(bundleData);
                    }
                }

                if(any_change)
                {
                    UnityEditor.EditorUtility.SetDirty(_editorStaticInstance);
                }
            }
        }

        public static AssetBundleService EditorFindBundleService()
        {
            if (_editorStaticInstance == null)
            {
                _editorStaticInstance = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbAssetBundleService");
            }

            return _editorStaticInstance; 
        }
#endif

        public static string GetRuntimePlatformName(RuntimePlatform platform, bool isDedicatedServer)
        {
            string platformName;

            switch (platform)
            {
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxServer:
                case RuntimePlatform.LinuxEditor:
                    platformName = "linux";
                    break;

                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsServer:
                    platformName = "windows";
                    break;

                case RuntimePlatform.Android:
                    platformName = "android";
                    break;

                case RuntimePlatform.PS4:
                    platformName = "ps4";
                    break;

                case RuntimePlatform.PS5:
                    platformName = "ps5";
                    break;

                default:
                    platformName = "other";
                    Debug.LogError($"platform not configured");
                    break;
            }

#if UNITY_SERVER && !UNITY_EDITOR
            isDedicatedServer = true;
#endif

            if (isDedicatedServer)
            {
                platformName = $"{platformName}_ds";
            }

            return platformName;
        }

        public static string GetBundleFilenameFromBundleName(RuntimePlatform platform, bool isDedicatedServer, string bundle)
        {
            var platformName = GetRuntimePlatformName(platform, isDedicatedServer);
            return $"{platformName}/{bundle.ToLowerInvariant()}.bundle";
        }

        private void UnloadAllCachedAssets(bool destroyInstancesToo, bool runResourcesUnload)
        {
            Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - ensuring in-progress async loads are completed");

            var asyncLoading = new List<AssetReference<Object>>();
            foreach (var entry in _assetCache)
            {
                var cache = entry.Value;
                if (cache.Asset == null)
                {
                    asyncLoading.Add(new AssetReference<Object>() { Guid = cache.Guid });
                }
            }

            if (_prewarmRoutine != null)
            {
                StopCoroutine(_prewarmRoutine);
                _prewarmRoutine = null;
            }

            foreach (var request in _prewarmAssetRequests)
            {
                asyncLoading.Add(new AssetReference<Object>() { Guid = request.Guid });
            }

            Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - completeting {asyncLoading.Count} async loads NOW.");

            foreach (var reference in asyncLoading)
            {
                LoadSync(reference);
            }

            Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - unloading asset bundles");

            foreach (var entry in _bundleCache)
            {
                var bundle = entry.Value;
                if(bundle != null)
                {
                    bundle.Unload(destroyInstancesToo);
                    AssetBundle.Destroy(bundle);
                }
            }

            foreach (var entry in _assetCache)
            {
                var cache = entry.Value;
                    cache.Request = null;

                if(cache.Asset != null)
                {
                    cache.Asset = null; 
                }
            }

            _bundleCache.Clear();
            _assetCache.Clear();
            _prewarmAssetRequests.Clear();

            // unload anything left over 
            AssetBundle.UnloadAllAssetBundles(true); 

            // crashes in il2cpp sometimes (?) 
            Caching.ClearCache();

            // actually frees memory 
            if(runResourcesUnload)
            {
                Resources.UnloadUnusedAssets();
            }
        }

        public void UnloadSingleBundle(AssetBundleData bundleData, bool destroyInstancesToo)
        {
            if(!_bundleCache.TryGetValue(bundleData.name, out var assetBundle))
            {
                return;
            }

            if(assetBundle == null)
            {
                return; 
            }

            var bundleName = assetBundle.name;

            // unload this bundle 
            assetBundle.Unload(destroyInstancesToo);
            _bundleCache.Remove(bundleData.name);

            // remove cache entries related to this bundle 
            var cacheToClear = new List<string>();

            foreach(var cache in _assetCache)
            {
                var data = cache.Value;
                if(bundleName.Equals(data.BundleName, System.StringComparison.Ordinal))
                {
                    cacheToClear.Add(cache.Key);
                }
            }

            foreach(var key in cacheToClear)
            {
                _assetCache.Remove(key); 
            }
        }

        [System.NonSerialized] private Coroutine _initializeAsyncRoutine;
        [System.NonSerialized] private bool _initialized;

        public Coroutine Initialize()
        {
            if (_initializeAsyncRoutine != null)
            {
                StopCoroutine(_initializeAsyncRoutine);
            }

            _initializeAsyncRoutine = StartCoroutine(DoInitializeAsync());
            return _initializeAsyncRoutine;
        }

        public bool GetIsInitialized()
        {
            return _initialized;
        }

        private IEnumerator DoInitializeAsync()
        {
            _initialized = false;

#if UNITY_ANDROID

        // the streaming assets should be in the obb file, if not then the uploaded obb file is likely wrongly named
        // the filename must be EXACTLY: main.$VERSION.com.FunktronicLabs.TheLightBrigade.obb
        // if the version is wrong, it will not load

        // Application.streamingAssetsPath comparison with regular and split builds 
        // JAR: jar:file:///data/app/com.FunktronicLabs.TheLightBrigade-H3fov1HMiE6OmEDHQjjGEw==/base.apk
        // OBB: /sdcard/Android/obb/com.FunktronicLabs.TheLightBrigade/main.1.com.FunktronicLabs.TheLightBrigade.obb

        if (Application.streamingAssetsPath.Contains("TheLightBrigade.obb") == false)
        {
            Debug.LogErrorFormat("AssetBundleService: streaming assets path is not an .obb file, likely did not upload the obb file correctly");
            Debug.LogErrorFormat("AssetBundleService: terminating now, because nothing will work");
            //Application.Quit();
        }
#endif

            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            Debug.LogFormat("AssetBundleService: preloading {0} asset bundles (not their contents)...", AssetBundleDatas.Count);

            // batch together loading bundles, async 
            var assetBundleRequests = new List<Coroutine>();
            for (var i = 0; i < AssetBundleDatas.Count; ++i)
            {
                var assetBundleData = AssetBundleDatas[i];


#if UNITY_EDITOR
                if (EditorGetAssetDatabaseEnabled())
                {
                    if (!assetBundleData.ForceLoadInEditor)
                    {
                        assetBundleData.RefreshLookupTable();
                        continue;
                    }
                }
#endif

                if (assetBundleData.LoadBundleOnInitialize)
                {
                    var routine = InitializeSingleBundle(assetBundleData);
                    assetBundleRequests.Add(routine);
                }
            }

            // wait on all bundles to be loaded 
            for (var i = 0; i < assetBundleRequests.Count; ++i)
            {
                yield return assetBundleRequests[i];
            }

            _initialized = true;
            stopwatch.Stop();

            Debug.Log($"AssetBundleService: finished processing {AssetBundleDatas.Count} asset bundles (completed in {stopwatch.ElapsedMilliseconds}ms) - initialized {_bundleCache.Count} bundles.");

            if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine);
            _prewarmRoutine = StartCoroutine(DoHandlePrewarmAssets());
        }

        public Coroutine InitializeSingleBundle(AssetBundleData assetBundleData)
        {
            return StartCoroutine(DoAsyncInitializeBundle(assetBundleData));
        }

        public bool GetBundleInitialized(AssetBundleData assetBundleData)
        {
            return _bundleCache.ContainsKey(assetBundleData.name); 
        }

        private IEnumerator DoAsyncInitializeBundle(AssetBundleData assetBundleData)
        {
            if (assetBundleData == null)
            {
                Debug.LogError($"[AssetBundleService]: Tried to initialize a null bundle.");
                yield break;
            }

            if(GetBundleInitialized(assetBundleData))
            {
                Debug.LogWarning($"[AssetBundleService]: Tried to initialzie {assetBundleData.name} twice!");
                yield break;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                if(!assetBundleData.ForceLoadInEditor)
                {
                    assetBundleData.RefreshLookupTable();
                    _bundleCache.Add(assetBundleData.name, null); 
                    yield break; 
                }
            }
#endif

            var isDedicatedServer = false;

#if UNITY_SERVER
            isDedicatedServer = true;
#endif

            // initialize the quick lookup table 
            assetBundleData.RefreshLookupTable();

            var platformName = GetRuntimePlatformName(Application.platform, isDedicatedServer);
            var assetBundleDataName = GetBundleFilenameFromBundleName(Application.platform, isDedicatedServer, assetBundleData.name);
            var assetBundleRoot = GetBundlesDeployFolder();
            var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";

            if (assetBundleData.PackSeparately)
            {
                foreach (var bundleAsset in assetBundleData.Assets)
                {
                    if (_bundleCache.ContainsKey(bundleAsset.GUID))
                    {
                        continue;
                    }

                    assetBundleRef = assetBundleData.GetPackedBundleDataName(bundleAsset, platformName, assetBundleRoot, assetBundleRef);

                    Debug.LogFormat("AssetBundleService preloading (packed separately bundle) {0}: {1}", assetBundleRef, bundleAsset.AssetBundleReference);

                    AssetBundle assetBundle;

                    if(PRELOAD_BUNDLES_IN_MEMORY)
                    {
                        var bundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                        var assetBundleRequest = AssetBundle.LoadFromMemoryAsync(bundleBytes);
                        
                        while (!assetBundleRequest.isDone)
                        {
                            yield return null;
                        }

                        assetBundle = assetBundleRequest.assetBundle;
                    }
                    else
                    {

                        var assetBundleRequest = AssetBundle.LoadFromFileAsync(assetBundleRef);

                        while (!assetBundleRequest.isDone)
                        {
                            yield return null;
                        }

                        assetBundle = assetBundleRequest.assetBundle;
                    }

                    if (assetBundle == null)
                    {
                        Debug.LogErrorFormat("AssetBundleService: * failed to load {0}", assetBundleRef);
                        continue;
                    }

                    var assetNames = assetBundle.GetAllAssetNames();

                    Debug.LogFormat("AssetBundleService: * loaded {0} which contains {1} assets.", assetBundle.name, assetNames.Length);

                    _bundleCache.Add(bundleAsset.GUID, assetBundle);
                }
            }
            else
            {
                Debug.LogFormat("AssetBundleService preloading {0}", assetBundleRef);

                AssetBundle assetBundle;
                if (PRELOAD_BUNDLES_IN_MEMORY)
                {
                    var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                    var assetBundleRequest = AssetBundle.LoadFromMemoryAsync(assetBundleBytes);

                    while (!assetBundleRequest.isDone)
                    {
                        yield return null;
                    }

                    assetBundle = assetBundleRequest.assetBundle;
                }
                else
                {
                    var assetBundleRequest = AssetBundle.LoadFromFileAsync(assetBundleRef);

                    while (!assetBundleRequest.isDone)
                    {
                        yield return null;
                    }

                    assetBundle = assetBundleRequest.assetBundle;
                }

                if (assetBundle == null)
                {
                    Debug.LogErrorFormat("AssetBundleService: * failed to load {0}", assetBundleRef);
                    yield break;
                }

                var assetNames = assetBundle.GetAllAssetNames();

                Debug.LogFormat("AssetBundleService: * loaded {0} which contains {1} assets.", assetBundle.name, assetNames.Length);

                _bundleCache.Add(assetBundleData.name, assetBundle);
            }
        }

        public void SyncInitializeBundle(AssetBundleData assetBundleData)
        {
            if (assetBundleData == null)
            {
                Debug.LogError($"[AssetBundleService]: Tried to initialize a null bundle.");
                return; 
            }

            if (GetBundleInitialized(assetBundleData))
            {
                Debug.LogWarning($"[AssetBundleService]: Tried to initialzie {assetBundleData.name} twice!");
                return; 
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                if(!assetBundleData.ForceLoadInEditor)
                {
                    assetBundleData.RefreshLookupTable();
                    _bundleCache.Add(assetBundleData.name, null); 
                    return; 
                }
            }
#endif

            var isDedicatedServer = false;

#if UNITY_SERVER
            isDedicatedServer = true;
#endif

            // initialize the quick lookup table 
            assetBundleData.RefreshLookupTable();

            var platformName = GetRuntimePlatformName(Application.platform, isDedicatedServer);
            var assetBundleDataName = GetBundleFilenameFromBundleName(Application.platform, isDedicatedServer, assetBundleData.name);
            var assetBundleRoot = GetBundlesDeployFolder();
            var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";

            if (assetBundleData.PackSeparately)
            {
                foreach (var assetData in assetBundleData.Assets)
                {
                    assetBundleRef = assetBundleData.GetPackedBundleDataName(assetData, platformName, assetBundleRoot, assetBundleRef);

                    AssetBundle assetBundle;
                    if(PRELOAD_BUNDLES_IN_MEMORY)
                    {
                        var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                        assetBundle = AssetBundle.LoadFromMemory(assetBundleBytes);
                    }
                    else
                    {
                        assetBundle = AssetBundle.LoadFromFile(assetBundleRef);
                    }

                    var assetNames = assetBundle.GetAllAssetNames();
                    _bundleCache.Add(assetData.GUID, assetBundle);
                }
            }
            else
            {
                AssetBundle assetBundle;

                if(PRELOAD_BUNDLES_IN_MEMORY)
                {
                    var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                    assetBundle = AssetBundle.LoadFromMemory(assetBundleBytes);
                }
                else
                {
                    assetBundle = AssetBundle.LoadFromFile(assetBundleRef);
                }


                var assetNames = assetBundle.GetAllAssetNames();
                _bundleCache.Add(assetBundleData.name, assetBundle);
            }
        }

        public void DeInitialize(bool clearBundleAssetData, bool runResourcesUnload)
        {
            if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine);
            UnloadAllCachedAssets(clearBundleAssetData, runResourcesUnload);

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                // todo: clear GenericDictionary<T>? 
            }
#endif
        }

        public T LoadSync<T>(AssetReference<T> reference, bool logErrors = true, bool allowInitializeBundle = false) where T : Object
        {
            if (string.IsNullOrEmpty(reference.Guid))
            {
                if(logErrors)
                {
                    Debug.LogError($"LoadAsync() Requested null reference? {reference.Name} ({reference.Guid}:{reference.LocalFileId})");
                }

                return null;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var assetBundleData = GetAssetBundleContainer(reference);
                if(assetBundleData == null || !assetBundleData.ForceLoadInEditor)
                {
                    return EditorLoadFromAssetDatabase(reference);
                }
            }
#endif

            var cachedResult = TryGetCachedResult(reference);
            if (cachedResult != null)
            {
                return cachedResult; //  GetSubAsset<T>(reference, cachedResult);
            }

            // if previously required with an async that has not yet finished, block the main thread to fetch it. 
            var previousRequest = GetAsyncRequest(reference);
            if (previousRequest != null)
            {
                if(logErrors)
                {
                    Debug.Log($"Tried to sync load asset currently being async loaded. {reference}");
                }
            }

            var assetBundle = TryGetAssetBundle(reference, allowInitializeBundle: allowInitializeBundle, logErrors: logErrors);
            if (assetBundle == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"no asset bundle found for {reference}");
                }

                return null;
            }

            // convert guid to reference string 
            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
            if (string.IsNullOrEmpty(assetBundleReference))
            {
                if(logErrors)
                {
                    Debug.LogError($"Failed to fetch reference for {reference.Guid}, {reference.Name}");
                }

                return null;
            }

            Object obj;
            if (typeof(T).IsSubclassOf(typeof(Component)))
            {
                // LogService.Log($"{typeof(T)} is a subclass of {typeof(Component)}");
                obj = assetBundle.LoadAsset<GameObject>(assetBundleReference);
            }
            else
            {
                // LogService.Log($"{typeof(T)} is not a subclass of {typeof(Component)}");
                obj = assetBundle.LoadAsset<T>(assetBundleReference);
            }

            if (obj == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"{reference.Name} ({reference.Guid}) [{assetBundleReference}] returned null from bundle {assetBundle.name}?");
                }

                return null;
            }

            var subObj = GetSubAsset(reference, obj, allowInitializeBundle: allowInitializeBundle, logErrors: logErrors);
            if (previousRequest != null)
            {
                CacheResult(reference, subObj);
            }
            else
            {
                _assetCache.Add(reference.GetCacheKey(), new AssetCache()
                {
                    Asset = (Object)subObj,
                    Guid = reference.Guid,
                    SubAssetReference = reference.SubAssetReference,
                    Request = default,
                    // Bundle = assetBundle,
                    BundleName = assetBundle.name,
                });
            }

            return subObj;
        }

        public AssetBundleData GetAssetBundleContainer<T>(AssetReference<T> reference) where T : Object
        {
            return GetAssetBundleContainer(reference.Guid);
        }

        public AssetBundleData GetAssetBundleContainer(string guid) 
        {
            foreach (var bundle in AssetBundleDatas)
            {
                if(bundle == null)
                {
                    continue;
                }

                if (bundle.ContainsAssetRef(guid))
                {
                    return bundle;
                }
            }

            return null;
        }

        public string GetAssetBundleReferenceFromGuid<T>(AssetReference<T> reference) where T : Object
        {
            var bundle = GetAssetBundleContainer(reference);

            if (bundle == null)
            {
                Debug.LogError($"could not find bundle for asset: {reference.Name} ({reference.Guid})");
                return null;
            }

            var assetData = bundle.FindByGuid(reference.Guid);
            if (assetData != null)
            {
                return assetData.AssetBundleReference;
            }

            return null;
        }

        private T TryGetCachedResult<T>(AssetReference<T> reference) where T : Object
        {
            if (_assetCache.TryGetValue(reference.GetCacheKey(), out AssetCache cache))
            {
                // this can happen if we end up caching the same reference as another type! 
                if (cache.Asset as GameObject != null && typeof(T).IsSubclassOf(typeof(Component)))
                {
                    var gameObject = cache.Asset as GameObject;
                    return gameObject.GetComponent<T>();
                }

                return (T)cache.Asset;
            }

            else
            {
                return null;
            }
        }

        private void CacheResult<T>(AssetReference<T> reference, T obj) where T : Object
        {
            var cacheKey = reference.GetCacheKey();
            var cache = _assetCache[cacheKey];

            if (cache.Asset != null)
            {
                Debug.LogWarning($"This asset was already cached? {reference}");
            }

            // reset the local position of parent GameObjects (prefab roots) 
            // if(obj != null && obj.GetType() == typeof(GameObject))
            // {
            //     var prefab = (GameObject) (Object) obj;
            //     if(prefab != null)
            //     {
            //         prefab.transform.ResetLocalPos();
            //         prefab.transform.ResetLocalRot();
            //     }
            // }

            if (obj as GameObject != null && typeof(T).IsSubclassOf(typeof(Component)))
            {
                var gameObject = obj as GameObject;
                obj = gameObject.GetComponent<T>();
            }

            cache.Asset = (Object)obj;

            _assetCache[cacheKey] = cache;
        }

        /// <summary>
        /// oh. so THIS is why addressables was so slow 🔪
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <param name="bundle"></param>
        /// <param name="asset"></param>
        /// <returns></returns>
        private T GetSubAsset<T>(AssetReference<T> reference, Object asset, bool allowInitializeBundle = false, bool logErrors = true) where T : Object
        {
            // we don't live in a world this pure.
            // every day, we stray further from God's light.
            // return (T) asset;

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (!string.IsNullOrEmpty(reference.SubAssetReference))
                    {
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(GetAssetBundleReferenceFromGuid(reference));
                        for (var i = 0; i < subAssets.Length; ++i)
                        {
                            var subAsset = subAssets[i];
                            if (subAsset.name == reference.SubAssetReference)
                            {
                                if (subAsset as T != null)
                                {
                                    return (T)subAsset;
                                }
                            }
                        }

                        return (T)asset;
                    }
                }
            }
#endif

            // if we have a sub asset reference, load all the subassets and find by name 
            // currently, only Sprites should be refereced via sub asset references 
            if (!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                var assetBundle = TryGetAssetBundle(reference, allowInitializeBundle: allowInitializeBundle, logErrors: logErrors);
                if (assetBundle == null)
                {
                    Debug.LogError($"no asset bundle found for {reference}");
                    return null;
                }

                var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
                if (string.IsNullOrEmpty(assetBundleReference))
                {
                    Debug.LogError($"did not find asset bundle reference for {reference}");
                    return null;
                }

                var subAssets = assetBundle.LoadAssetWithSubAssets<T>(assetBundleReference);
                var subAssetCount = subAssets.Length;
                for (var i = 0; i < subAssetCount; ++i)
                {
                    var subAsset = subAssets[i];
                    if (subAsset.name == reference.SubAssetReference)
                    {
                        if (subAsset as T != null)
                        {
                            return (T)subAsset;
                        }
                    }
                }
            }

            if (asset as GameObject != null && typeof(T).IsSubclassOf(typeof(Component)))
            {
                var gameobject = asset as GameObject;
                return gameobject.GetComponent<T>();
            }
            else
            {
                return (T)asset;
            }
        }

        /// <summary>
        /// oh. so THIS is why addressables was so slow 🔪
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <param name="bundle"></param>
        /// <param name="asset"></param>
        /// <returns></returns>
        private T GetSubAssetAsyncResult<T>(AssetReference<T> reference, Object[] assets) where T : Object
        {
            // we don't live in a world this pure.
            // every day, we stray further from God's light.
            // return (T) asset;

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (!string.IsNullOrEmpty(reference.SubAssetReference))
                    {
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(GetAssetBundleReferenceFromGuid(reference));
                        for (var i = 0; i < subAssets.Length; ++i)
                        {
                            var subAsset = subAssets[i];
                            if (subAsset.name == reference.SubAssetReference)
                            {
                                if (subAsset as T != null)
                                {
                                    return (T)subAsset;
                                }
                            }
                        }

                        return null;
                    }
                }
            }
#endif

            // if we have a sub asset reference, load all the subassets and find by name 
            // currently, only Sprites should be refereced via sub asset references 
            if (!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                var subAssetCount = assets.Length;
                for (var i = 0; i < subAssetCount; ++i)
                {
                    var subAsset = assets[i];
                    if (subAsset.name == reference.SubAssetReference)
                    {
                        if (subAsset as T != null)
                        {
                            return (T)subAsset;
                        }
                    }
                }
            }

            return null;
        }

        public void LoadSyncBatched<T>(List<AssetReference<T>> references) where T : Object
        {
            foreach (var reference in references)
            {
                LoadSync(reference);
            }
        }

        public IEnumerator LoadAsyncWholeBundle(AssetBundleData data)
        {
            yield return new WaitAssetBundleServiceReady();

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                if(!data.ForceLoadInEditor)
                {
                    yield break;
                }
            }
#endif

            // if this bundle has not been loaded during initialization, load it now
            if (!_bundleCache.ContainsKey(data.name))
            {
                yield return InitializeSingleBundle(data);
            }

            var bundle = TryGetBundleFromBundleData(data);
            yield return bundle.LoadAllAssetsAsync();

            foreach (var bundleAssetReference in data.Assets)
            {
                if (bundleAssetReference == null)
                {
                    continue;
                }

                var assetReference = new AssetReference<Object>()
                {
                    Guid = bundleAssetReference.GUID,
                };

                if (TryGetCachedAsset(assetReference) != null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(bundleAssetReference.AssetBundleReference))
                {
                    continue;
                }

                var bundleAsset = bundle.LoadAsset(bundleAssetReference.AssetBundleReference);
                if (bundleAsset == null)
                {
                    continue;
                }

                _assetCache.Add(assetReference.GetCacheKey(), new AssetCache()
                {
                    Asset = bundleAsset,
                    Guid = assetReference.Guid,
                    SubAssetReference = assetReference.SubAssetReference,
                    Request = default,
                    // Bundle = bundle,
                    BundleName = bundle.name,
                });
            }
        }

        public void LoadSyncWholeBundle(AssetBundleData data)
        {
#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                if(!data.ForceLoadInEditor)
                {
                    return;
                }
            }
#endif

            // if this bundle has not been loaded during initialization, load it now
            if (!_bundleCache.ContainsKey(data.name))
            {
                SyncInitializeBundle(data);
            }

            var bundle = TryGetBundleFromBundleData(data);
            bundle.LoadAllAssets();

            foreach (var bundleAssetReference in data.Assets)
            {
                if (bundleAssetReference == null)
                {
                    continue;
                }

                var assetReference = new AssetReference<Object>()
                {
                    Guid = bundleAssetReference.GUID,
                };

                if (TryGetCachedAsset(assetReference) != null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(bundleAssetReference.AssetBundleReference))
                {
                    continue;
                }

                var bundleAsset = bundle.LoadAsset(bundleAssetReference.AssetBundleReference);
                if (bundleAsset == null)
                {
                    continue;
                }

                _assetCache.Add(assetReference.GetCacheKey(), new AssetCache()
                {
                    Asset = bundleAsset,
                    Guid = assetReference.Guid,
                    SubAssetReference = assetReference.SubAssetReference,
                    Request = default,
                    // Bundle = bundle,
                    BundleName = bundle.name,
                });
            }
        }

        /// <summary>
        /// This will have unity's internal asset bundle system load assets, which unity will cache in memory. 
        /// Useful for loading screens, where you want to prewarm a bunch of assets from disk without 'loading' them or sub assets normally.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="references"></param>
        /// <returns></returns>
        public IEnumerator DoPrewarmBatched<T>(List<AssetReference<T>> references) where T : Object
        {
            var requests = new List<AssetBundleRequest>();

            foreach (var reference in references)
            {
#if UNITY_EDITOR
                if (EditorGetAssetDatabaseEnabled())
                {

                    var bundleData = GetAssetBundleContainer(reference);
                    if (bundleData == null || !bundleData.ForceLoadInEditor)
                    {
                        continue;
                    }
                }
#endif

                var bundle = TryGetAssetBundle(reference);
                if (bundle == null)
                {
                    continue;
                }

                var bundleReference = GetAssetBundleReferenceFromGuid(reference);
                var request = bundle.LoadAssetAsync(bundleReference);
                requests.Add(request);
            }

            while (requests.Count > 0)
            {
                yield return null;

                for (var i = requests.Count - 1; i >= 0; --i)
                {
                    if (requests[i].isDone)
                    {
                        requests.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Warning!! This deletes entries from the list that is input, be sure to clone if this data is important outside of bundle context. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="references"></param>
        /// <returns></returns>
        public IEnumerator LoadAsyncBatched<T>(List<AssetReference<T>> references) where T : Object
        {

            Debug.LogFormat("AssetBundleService.LoadAsyncBatched: loading {0} assets...", references.Count);

            yield return new WaitAssetBundleServiceReady();

            var awaiting = new List<Coroutine>(references.Count);

            for (var i = references.Count - 1; i >= 0; --i)
            {
                var reference = references[i];

#if UNITY_EDITOR
                if (EditorGetAssetDatabaseEnabled())
                {
                    var bundleData = GetAssetBundleContainer(reference);
                    if (bundleData == null || !bundleData.ForceLoadInEditor)
                    {
                        continue;
                    }
                }
#endif

                var cached = TryGetCachedAsset(reference);
                if (cached != null)
                {
                    // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: skipped {0}, already loaded", references[i].Name);
                    references.RemoveAt(i);
                    continue;
                }

                var func = LoadAsync(reference);
                var routine = StartCoroutine(func);

                // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: > {0}: added", reference);

                awaiting.Add(routine);
            }

            for (var i = references.Count - 1; i >= 0; --i)
            {
                var reference = references[i];

                var time0 = Time.realtimeSinceStartup;
                yield return awaiting[i];
                var time1 = Time.realtimeSinceStartup;

                // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: loaded {0}, took {1}ms", references[i].Name, (int)((time1 - time0) * 1000.0f));

                GetAsyncResult(reference);
            }

            // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: finished loading {0} assets.", references.Count);
        }

        public AssetBundle TryGetBundleFromBundleData(AssetBundleData assetBundleData)
        {
            if (_bundleCache.TryGetValue(assetBundleData.name, out AssetBundle bundle))
            {
                return bundle;
            }

            return null;
        }

        public AssetBundle TryGetAssetBundle<T>(AssetReference<T> reference, bool allowInitializeBundle = false, bool logErrors = true) where T : Object
        {
            var assetBundleData = GetAssetBundleContainer(reference);
            if (assetBundleData == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"AssetBundleData not found for: {reference.Name} ({reference}). loaded bundles count: {_bundleCache.Count}");
                }

                return null;
            }

            bool found;
            AssetBundle bundle;
            if (assetBundleData.PackSeparately)
            {
                found = _bundleCache.TryGetValue(reference.Guid, out bundle);
            }
            else
            {
                found = _bundleCache.TryGetValue(assetBundleData.name, out bundle);
            }

            // don't do this: if its not yet loaded, it means we're trying to load asset bundles at a bad time (loading screens?) 
            // if not found, but we know the bundle.. just load the bundle? 
            if(!found && allowInitializeBundle)
            {
                if(logErrors)
                {
                    Debug.LogWarning($"Loading an asset from uninitialized bundle {assetBundleData.name}, initializing it now!");
                }
            
                SyncInitializeBundle(assetBundleData);
            
                if (assetBundleData.PackSeparately)
                {
                    found = _bundleCache.TryGetValue(reference.Guid, out bundle);
                }
                else
                {
                    found = _bundleCache.TryGetValue(assetBundleData.name, out bundle);
                }
            }

            // if still not found, something is seriously wrong 
            if (found)
            {
                return bundle;
            }
            else
            {
                if(logErrors)
                {
                    Debug.LogError($"AssetBundle '{assetBundleData.name}' not found? for: '{reference}'. loaded bundles count: {_bundleCache.Count}");

                    foreach (var entry in _bundleCache)
                    {
                        Debug.LogError($"loaded: {entry.Key}");
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// If you want to check if an asset has already been loaded from a bundle, use this and check if it is null or not. 
        /// This will not sync load if the asset is not yet loaded.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <returns></returns>
        public T TryGetCachedAsset<T>(AssetReference<T> reference) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(reference.Guid))
            {
                Debug.LogWarning($"requested null guid reference? {reference}");
                return null;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    return EditorLoadFromAssetDatabase(reference);
                }
            }
#endif

            var cachedResult = TryGetCachedResult(reference);
            if (cachedResult != null)
            {
                return cachedResult; // GetSubAsset(reference, cachedResult);
            }

            return null;
        }

        /// <summary>
        /// Queues up an async bundle request. The bundle requested is parsed from the reference string passed in. Returns the handle, which can be yielded. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <returns></returns>
        public IEnumerator LoadAsync<T>(AssetReference<T> reference, bool logErrors = true, bool allowInitializeBundle = false) where T : UnityEngine.Object
        {
            // reference.Reference = reference.Reference.ToLowerInvariant();

            yield return new WaitAssetBundleServiceReady();

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (DEBUG_DELAY_RANDOMIZE)
                        yield return new WaitForSeconds(Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f)));
                    if (DEBUG_DELAY_CONSTANT > 0.0f)
                        yield return new WaitForSeconds(DEBUG_DELAY_CONSTANT);

                    yield break;
                }
            }
#endif

            if (string.IsNullOrEmpty(reference.Guid))
            {
                if(logErrors)
                {
                    Debug.LogError("LoadAsync() Requested null reference?");
                }

                yield break;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (DEBUG_DELAY_RANDOMIZE)
                        yield return new WaitForSeconds(Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f)));
                    if (DEBUG_DELAY_CONSTANT > 0.0f)
                        yield return new WaitForSeconds(DEBUG_DELAY_CONSTANT);

                    yield break;
                }
            }
#endif

            if (CheckIfRequestedLoad(reference))
            {
                var asyncRequest = GetAsyncRequest(reference);
                if (asyncRequest != null)
                {
                    if (asyncRequest.isDone)
                    {
                        // Debug.Log($"{reference} was already requested, so skip waiting.");
                        yield break;
                    }

                    yield return new WaitUntilAsyncIsDone(asyncRequest);
                    yield break;
                }

                if(logErrors)
                {
                    Debug.LogWarning($"{reference} was requested, but the async request could not be found?");
                }

                yield break;
            }

            var assetBundle = TryGetAssetBundle(reference, allowInitializeBundle: allowInitializeBundle, logErrors: logErrors);
            if (assetBundle == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"did not find asset bundle containing the reference {reference}");
                }

                yield break;
            }

            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
            if (string.IsNullOrEmpty(assetBundleReference))
            {
                if(logErrors)
                {
                    Debug.LogError($"did not find asset bundle reference for {reference}");
                }

                yield break;
            }

            AssetBundleRequest handle;

            if (!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                handle = assetBundle.LoadAssetWithSubAssetsAsync<T>(assetBundleReference);
            }
            else if (typeof(T).IsSubclassOf(typeof(Component)))
            {
                handle = assetBundle.LoadAssetAsync<GameObject>(assetBundleReference);
            }
            else
            {
                handle = assetBundle.LoadAssetAsync<T>(assetBundleReference);
            }

            _assetCache.Add(reference.GetCacheKey(), new AssetCache()
            {
                Asset = (Object)null,
                Guid = reference.Guid,
                SubAssetReference = reference.SubAssetReference,
                Request = handle,
                // Bundle = assetBundle,
                BundleName = assetBundle.name,
            });

            yield return handle;
        }


        /// <summary>
        /// Queues up an async bundle request. The bundle requested is parsed from the reference string passed in. Returns the handle, which can be yielded. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <returns></returns>
        public IEnumerator LoadAsyncCancellable<T>(AssetReference<T> reference, System.Func<bool> cancelIfTrue, bool allowInitializeBundle = false, bool logErrors = true) where T : UnityEngine.Object
        {
            // reference.Reference = reference.Reference.ToLowerInvariant();

            yield return new WaitAssetBundleServiceReady();

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    var fakeDelay = 0f;

                    if (DEBUG_DELAY_RANDOMIZE)
                    {
                        fakeDelay = Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f));
                    }
                    else if (DEBUG_DELAY_CONSTANT > 0.0f)
                    {
                        fakeDelay = DEBUG_DELAY_CONSTANT;
                    }

                    var waitUntil = Time.time + fakeDelay;
                    while(Time.time < waitUntil && !cancelIfTrue())
                    {
                        yield return null; 
                    }
    ;
                    yield break;
                }
            }
#endif

            if (string.IsNullOrEmpty(reference.Guid))
            {
                Debug.LogError("LoadAsync() Requested null reference?");
                yield break;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    var fakeDelay = 0f;

                    if (DEBUG_DELAY_RANDOMIZE)
                    {
                        fakeDelay = Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f));
                    }
                    else if (DEBUG_DELAY_CONSTANT > 0.0f)
                    {
                        fakeDelay = DEBUG_DELAY_CONSTANT;
                    }

                    var waitUntil = Time.time + fakeDelay;
                    while (Time.time < waitUntil && !cancelIfTrue())
                    {
                        yield return null;
                    }

                    yield break;
                }
            }
#endif

            if (CheckIfRequestedLoad(reference))
            {
                var asyncRequest = GetAsyncRequest(reference);
                if (asyncRequest != null)
                {
                    if (asyncRequest.isDone)
                    {
                        // Debug.Log($"{reference} was already requested, so skip waiting.");
                        yield break;
                    }

                    yield return new WaitUntilAsyncIsDone(asyncRequest);
                    yield break;
                }

                Debug.LogWarning($"{reference} was requested, but the async request could not be found?");

                yield break;
            }

            var assetBundle = TryGetAssetBundle(reference, allowInitializeBundle: allowInitializeBundle, logErrors: logErrors);
            if (assetBundle == null)
            {
                Debug.LogError($"did not find asset bundle containing the reference {reference}");
                yield break;
            }

            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
            if (string.IsNullOrEmpty(assetBundleReference))
            {
                Debug.LogError($"did not find asset bundle reference for {reference}");
                yield break;
            }

            AssetBundleRequest handle;

            if (!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                handle = assetBundle.LoadAssetWithSubAssetsAsync<T>(assetBundleReference);
            }
            else if (typeof(T).IsSubclassOf(typeof(Component)))
            {
                handle = assetBundle.LoadAssetAsync<GameObject>(assetBundleReference);
            }
            else
            {
                handle = assetBundle.LoadAssetAsync<T>(assetBundleReference);
            }

            _assetCache.Add(reference.GetCacheKey(), new AssetCache()
            {
                Asset = (Object)null,
                Guid = reference.Guid,
                SubAssetReference = reference.SubAssetReference,
                Request = handle,
                // Bundle = assetBundle,
                BundleName = assetBundle.name,
            });


            while(handle.isDone)
            {
                yield return null;

                if(cancelIfTrue.Invoke())
                {
                    yield break; 
                }
            }

            yield return handle;
        }

        /// <summary>
        /// After yielding the handle from LoadAsync() or awaiting CheckIfRequestReady() is true, this will return the asset requested from the reference. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <returns></returns>
        public T GetAsyncResult<T>(AssetReference<T> reference, bool logErrors = true) where T : Object
        {
            // reference.Reference = reference.Reference.ToLowerInvariant();

            if (string.IsNullOrEmpty(reference.Guid))
            {
                if(logErrors)
                {
                    Debug.LogWarning($"requested null guid reference? {reference}");
                }

                return null;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference.Guid);
                if(bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    return EditorLoadFromAssetDatabase(reference, logErrors: logErrors);
                }
            }
#endif

            var cachedResult = TryGetCachedResult(reference);
            if (cachedResult != null)
            {
                return cachedResult;
            }

            var asyncRequest = GetAsyncRequest(reference);
            if (asyncRequest == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"Tried to get async request for '{reference}', but it was not first requested!");
                }

                return LoadSync(reference);
            }

            if (!asyncRequest.isDone)
            {
                if(logErrors)
                {
                    Debug.LogError($"Tried to get async request for '{reference}', but it's not finished loading!");
                }

                return LoadSync(reference);
            }

            T objResult;

            if (!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                objResult = GetSubAssetAsyncResult(reference, asyncRequest.allAssets);
            }
            else
            {
                objResult = GetSubAsset(reference, asyncRequest.asset, logErrors: logErrors);
            }

            CacheResult(reference, objResult);

            return objResult;
        }

        /// <summary>
        /// Returns an existing AssetBundleRequest from the last LoadAsync() with a matching reference string. 
        /// </summary>
        /// <param name="reference"></param>
        /// <returns></returns>
        public AssetBundleRequest GetAsyncRequest<T>(AssetReference<T> reference) where T : Object
        {
            if (_assetCache.TryGetValue(reference.GetCacheKey(), out AssetCache cache))
            {
                return cache.Request;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the reference was ever requested to be loaded. If false, you should LoadAsync() before trying to GetAsyncResult(). 
        /// </summary>
        /// <param name="reference"></param>
        /// <returns></returns>
        public bool CheckIfRequestedLoad<T>(AssetReference<T> reference) where T : Object
        {
            return _assetCache.ContainsKey(reference.GetCacheKey());
        }

        /// <summary>
        /// Returns true if the previous LoadAsync() is ready to be returned by GetAsyncResult().
        /// </summary>
        /// <param name="reference"></param>
        /// <returns></returns>
        public bool CheckIfRequestReady<T>(AssetReference<T> reference) where T : Object
        {
            var asyncRequest = GetAsyncRequest(reference);
            if (asyncRequest != null)
            {
                return asyncRequest.isDone;
            }

            return false;
        }

        /// <summary>
        /// Batched RequestPrewarmAsset().
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetReferences"></param>
        public void RequestPrewarmAssets<T>(List<AssetReference<T>> assetReferences) where T : Object
        {
            foreach (var reference in assetReferences)
            {
                RequestPrewarmAsset(reference);
            }
        }

        /// <summary>
        /// Requests the AssetReference to be lazily asyncronously loaded. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetReference"></param>
        public void RequestPrewarmAsset<T>(AssetReference<T> assetReference) where T : Object
        {
            // not possible..? 
            // _prewarmAssetRequests.Add(assetReference);

            // hmm 
            _prewarmAssetRequests.Add(new AssetReference<Object>()
            {
                // Reference = assetReference.Reference,
                Guid = assetReference.Guid,
                LocalFileId = assetReference.LocalFileId,
                Name = assetReference.Name,
                SubAssetReference = assetReference.SubAssetReference,
            });
        }

        [System.NonSerialized]
        private List<AssetReference<Object>> _prewarmAssetRequests = new List<AssetReference<Object>>();

        private IEnumerator DoHandlePrewarmAssets()
        {
            yield return new WaitAssetBundleServiceReady();

            while (true)
            {
                yield return null;

                if (_prewarmAssetRequests.Count == 0)
                {
                    continue;
                }

                var prewarmBactch = _prewarmAssetRequests.Clone();
                _prewarmAssetRequests.Clear();

                yield return LoadAsyncBatched(prewarmBactch);
            }
        }

        public static Coroutine InstantiatePrefab(AssetReference<GameObject> prefabRef, Vector3 position, Quaternion rotation, System.Action<GameObject> onComplete = null)
        {
            var assetBundleManager = AssetBundleService.Instance;
            return assetBundleManager.StartCoroutine(assetBundleManager.DoInstantiatePrefab(prefabRef, position, rotation, onComplete));
        }

        private IEnumerator DoInstantiatePrefab(AssetReference<GameObject> prefabRef, Vector3 position, Quaternion rotation, System.Action<GameObject> onComplete)
        {
            yield return new WaitAssetBundleServiceReady();

            if (!prefabRef.IsValid())
            {
                Debug.LogError($"{prefabRef} is not a valid AssetReference<GameObject>");

                if (onComplete != null)
                {
                    onComplete.Invoke(null);
                }

                yield break;
            }

            yield return LoadAsync(prefabRef);
            var prefab = GetAsyncResult(prefabRef);

            if (prefab == null)
            {
                Debug.LogError($"GetAsyncResult({prefabRef}) failed to load prefab!");

                if (onComplete != null)
                {
                    onComplete.Invoke(null);
                }

                yield break;
            }

            var result = GameObject.Instantiate(prefab, position, rotation);

            if (onComplete != null)
            {
                onComplete.Invoke(result);
            }
        }

        /// <summary>
        /// Using the guid of the reference, parse the lookup string to find the actual scene name. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sceneReference"></param>
        /// <returns></returns>
        public string GetSceneNameFromSceneReference<T>(AssetReference<T> sceneReference) where T : Object
        {
            var reference = GetAssetBundleReferenceFromGuid(sceneReference);
            if (string.IsNullOrEmpty(reference)) return null;

            var split = reference.Split('/');
            if (split == null || split.Length == 0) return null;

            var name = split[split.Length - 1];
            return name;
        }
    }

    public class WaitUntilReadyBatched : CustomYieldInstruction
    {
        private List<AssetBundleRequest> requests;

        public WaitUntilReadyBatched(List<AssetBundleRequest> requests)
        {
            this.requests = requests;
        }

        public override bool keepWaiting
        {
            get
            {
                foreach (var request in requests)
                {
                    if (request != null && !request.isDone)
                    {
                        return true;
                    }
                }

                return false;
            }

        }
    }

    public class WaitUntilAsyncIsDone : CustomYieldInstruction
    {
        public AssetBundleRequest assetBundleOperation;

        public WaitUntilAsyncIsDone(AssetBundleRequest assetBundleOperation)
        {
            this.assetBundleOperation = assetBundleOperation;
        }

        public override bool keepWaiting
        {
            get
            {
                return assetBundleOperation != null && !assetBundleOperation.isDone;
            }
        }
    }

    public class WaitAssetBundleServiceReady : CustomYieldInstruction
    {
        public WaitAssetBundleServiceReady()
        {

        }

        public override bool keepWaiting
        {
            get
            {
                return AssetBundleService.Instance == null || !AssetBundleService.Instance.GetIsInitialized();
            }
        }
    }
}