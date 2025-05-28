#pragma warning disable CS0162 // Unreachable code detected
#if !UNITY_EDITOR && UNITY_SWITCH 
    #define NO_UNITYENGINE_CACHING
#endif

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
        public const bool UNLOAD_BUNDLES_ON_INITIALIZE = true;
        public const bool ASYNC_INITIALIZE_FROM_LOADS = false; // deadlock issues? 

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

        [System.NonSerialized] private Dictionary<string, AssetBundleData> _bundleDataLookup = new Dictionary<string, AssetBundleData>(System.StringComparer.Ordinal);
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
            if(bundleData == null)
            {
                Debug.LogError("EnsureReferenceInBundle() null bundle?");
                return; 
            }

            var assetData = new AssetBundleReferenceData()
            {
                GUID = guid,
                AssetBundleReference = AssetPathLowerCase(AssetDatabase.GUIDToAssetPath(guid)),
            };

            if(bundleData.Assets == null)
            {
                bundleData.Assets = new List<AssetBundleReferenceData>(); 
            }

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
                case RuntimePlatform.IPhonePlayer:
                    platformName = "iphone";
                    break;
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    platformName = "osx";
                    break;
                case RuntimePlatform.Switch:
                    platformName = "switch";
                    break;
                case RuntimePlatform.XboxOne:
                    platformName = "xbox_one";
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
            // Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - ensuring in-progress async loads are completed");


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

            // Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - completeting {asyncLoading.Count} async loads NOW.");

            foreach (var reference in asyncLoading)
            {
                LoadSync(reference);
            }

            // Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - unloading asset bundles");

            // not necessary because of AssetBundle.UnloadAllAssetBundles
            // foreach (var entry in _bundleCache)
            // {
            //     var bundle = entry.Value;
            //     if(bundle != null)
            //     {
            //         bundle.Unload(destroyInstancesToo);
            //         AssetBundle.Destroy(bundle);
            //     }
            // }

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
            _bundleDataLookup.Clear();

            _assetCache.Clear();
            _prewarmAssetRequests.Clear();

            // unload anything left over 
            AssetBundle.UnloadAllAssetBundles(destroyInstancesToo);

            // crashes in il2cpp sometimes (?) 
#if !NO_UNITYENGINE_CACHING
            Caching.ClearCache();
#endif

            // actually frees memory 
            if(runResourcesUnload)
            {
                Resources.UnloadUnusedAssets();
            }
        }

        private void UnloadSingleRealBundle(AssetBundle assetBundle, bool destroyInstancesToo, string bundleCacheKey)
        {
            var bundleName = assetBundle.name;

            // unload this bundle 
            assetBundle.Unload(destroyInstancesToo);
            
            _bundleCache.Remove(bundleCacheKey);

            // remove cache entries related to this bundle 
            var cacheToClear = new List<string>();

            foreach (var cache in _assetCache)
            {
                var data = cache.Value;
                if (bundleName.Equals(data.BundleName, System.StringComparison.Ordinal))
                {
                    cacheToClear.Add(cache.Key);
                }
            }

            foreach (var key in cacheToClear)
            {
                _assetCache.Remove(key);
            }
        }

        public void UnloadSingleBundleData(AssetBundleData bundleData, bool destroyInstancesToo)
        {
            if(bundleData.PackSeparately)
            {
                foreach(var assetReferenceData in bundleData.Assets)
                {
                    var bundleName = BuildPackedAssetBundleName(bundleData, assetReferenceData.GUID);

                    if(!_bundleCache.TryGetValue(bundleName, out var assetBundle))
                    {
                        continue;
                    }

                    UnloadSingleRealBundle(assetBundle, destroyInstancesToo, bundleName);
                }
            }
            else
            {
                if(!_bundleCache.TryGetValue(bundleData.name, out var assetBundle))
                {
                    return;
                }

                if(assetBundle == null)
                {
                    return; 
                }

                UnloadSingleRealBundle(assetBundle, destroyInstancesToo, bundleData.name); 
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

#if UNITY_ANDROID && !UNITY_EDITOR
            // the streaming assets should be in the obb file, if not then the uploaded obb file is likely wrongly named
            // the filename must be EXACTLY: main.$VERSION.com.{CompanyName}.{AppName}.obb
            // if the version is wrong, it will not load

            // Application.streamingAssetsPath comparison with regular and split builds 
            // JAR: jar:file:///data/app/com.{CompanyName}.{AppName}-{XXX}==/base.apk
            // OBB: /sdcard/Android/obb/com.{CompanyName}.{AppName}/main.1.com.{CompanyName}.{AppName}.obb

            
            if (Application.streamingAssetsPath.Contains($"{Application.productName}.obb") == false)
        {
            Debug.LogErrorFormat("AssetBundleService: streaming assets path is not an .obb file, likely did not upload the obb file correctly");
            Debug.LogErrorFormat("AssetBundleService: terminating now, because nothing will work");
            //Application.Quit();
        }
#endif

            // fixes an issue in the editor where bundles sometimes do not unload while shutting down play mode 
            if(UNLOAD_BUNDLES_ON_INITIALIZE)
            {
                AssetBundle.UnloadAllAssetBundles(true);
            }

            var stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();

            // Debug.LogFormat("AssetBundleService: preloading {0} asset bundles (not their contents)...", AssetBundleDatas.Count);

            _bundleDataLookup.Clear();

            // batch together loading bundles, async 
            Profiler.BeginSample("AssetBundleService.DoInitializeAsync");
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

                Profiler.BeginSample("BundleDataLookup");
                foreach (var asset in assetBundleData.Assets)
                {
                    if(_bundleDataLookup.ContainsKey(asset.GUID))
                    {
                        Debug.LogError($"Duplicate key detected {asset.AssetBundleReference} ({asset.GUID}) - is this asset assigned to two bundles?");
                        continue;
                    }

                    _bundleDataLookup.Add(asset.GUID, assetBundleData); 
                }
                Profiler.EndSample();

                if (assetBundleData.LoadBundleOnInitialize)
                {
                    var routine = InitializeSingleBundle(assetBundleData);
                    assetBundleRequests.Add(routine);
                }
            }
            Profiler.EndSample();

            // wait on all bundles to be loaded 
            for (var i = 0; i < assetBundleRequests.Count; ++i)
            {
                yield return assetBundleRequests[i];
            }

            Debug.Log($"[AssetBundleService]: Took {stopwatch.Elapsed.TotalMilliseconds:N2}ms to initialize.");
            _initialized = true;
            stopwatch.Stop();

            // Debug.Log($"AssetBundleService: finished processing {AssetBundleDatas.Count} asset bundles (completed in {stopwatch.ElapsedMilliseconds}ms) - initialized {_bundleCache.Count} bundles.");

            if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine);
            _prewarmRoutine = StartCoroutine(DoHandlePrewarmAssets());
        }

        public Coroutine InitializeSingleBundle(AssetBundleData assetBundleData, string specificAssetGuid = null)
        {
            return StartCoroutine(DoAsyncInitializeBundle(assetBundleData, specificAssetGuid: specificAssetGuid));
        }

        public bool GetBundleInitialized(AssetBundleData assetBundleData, string specificAssetGuid = null)
        {
            if (assetBundleData.PackSeparately)
            {
                if (!string.IsNullOrEmpty(specificAssetGuid))
                {
                    return _bundleCache.ContainsKey(BuildPackedAssetBundleName(assetBundleData, specificAssetGuid));
                }
                else
                {
                    Debug.LogError($"[AssetBundleService]: Tried to GetBundleInitialized() on a 'PackSeparately' bundle without specifying an asset guid. Please define a 'specificAssetGuid' when calling this function for this bundle.");
                }
            }

            return _bundleCache.ContainsKey(assetBundleData.name); 
        }

        public AssetBundle TryGetInitializedBundle(AssetBundleData assetBundleData, string specificAssetGuid = null)
        {
            if (assetBundleData.PackSeparately)
            {
                if (!string.IsNullOrEmpty(specificAssetGuid))
                {
                    if(_bundleCache.TryGetValue(BuildPackedAssetBundleName(assetBundleData, specificAssetGuid), out var psBundle))
                    {
                        return psBundle; 
                    }
                }
                else
                {
                    Debug.LogError($"[AssetBundleService]: Tried to GetBundleInitialized() on a 'PackSeparately' bundle without specifying an asset guid. Please define a 'specificAssetGuid' when calling this function for this bundle.");
                }
            }

            if(_bundleCache.TryGetValue(assetBundleData.name, out var bundle))
            {
                return bundle;
            }

            return null; 
        }

        private Dictionary<string, AssetBundleCreateRequest> _inProgressBundleAsyncLoads = new Dictionary<string, AssetBundleCreateRequest>(); 

        private IEnumerator DoAsyncInitializeBundle(AssetBundleData assetBundleData, string specificAssetGuid = null)
        {
            if (assetBundleData == null)
            {
                Debug.LogError($"[AssetBundleService]: Tried to initialize a null bundle.");
                yield break;
            }

            if(GetBundleInitialized(assetBundleData, specificAssetGuid: specificAssetGuid))
            {
                Debug.LogWarning($"[AssetBundleService]: Tried to initialzie {assetBundleData.name} ({specificAssetGuid}) twice!");
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
            Profiler.BeginSample("RefreshLookupTable");
            {
                assetBundleData.RefreshLookupTable();
            }
            Profiler.EndSample();

            var platformName = GetRuntimePlatformName(Application.platform, isDedicatedServer);
            var assetBundleDataName = GetBundleFilenameFromBundleName(Application.platform, isDedicatedServer, assetBundleData.name);
            var assetBundleRoot = GetBundlesDeployFolder();
            var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";

            if (assetBundleData.PackSeparately)
            {
                foreach (var bundleAsset in assetBundleData.Assets)
                {
                    if(!string.IsNullOrEmpty(specificAssetGuid) && !specificAssetGuid.Equals(bundleAsset.GUID, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    assetBundleRef = assetBundleData.GetPackedBundleDataName(bundleAsset, platformName, assetBundleRoot, assetBundleRef);

                    if(_inProgressBundleAsyncLoads.TryGetValue(assetBundleRef, out var ongoingAsyncRequest))
                    {
                        Debug.LogWarning($"[AssetBundleService]: Tried to async load the same bundle twice ({assetBundleRef}). Waiting on the previous load before doing anything else.");
                        
                        while (!ongoingAsyncRequest.isDone)
                        {
                            yield return null;
                        }
                    }

                    if(!_bundleCache.TryGetValue(assetBundleRef, out var psAssetBundle))
                    {
                        Debug.LogFormat("AssetBundleService preloading (packed separately bundle) {0}: {1}", assetBundleRef, bundleAsset.AssetBundleReference);

                        if (PRELOAD_BUNDLES_IN_MEMORY)
                        {
                            Profiler.BeginSample("ReadAllBytes");
                            var bundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                            Profiler.EndSample();

                            var assetBundleRequest = AssetBundle.LoadFromMemoryAsync(bundleBytes);
                            _inProgressBundleAsyncLoads.Add(assetBundleRef, assetBundleRequest);

                            while (!assetBundleRequest.isDone)
                            {
                                yield return null;
                            }

                            TryLoadAssetBundleFromAsyncRequest(assetBundleRequest, out psAssetBundle);
                            _inProgressBundleAsyncLoads.Remove(assetBundleRef);
                        }
                        else
                        {

                            var assetBundleRequest = AssetBundle.LoadFromFileAsync(assetBundleRef);
                            _inProgressBundleAsyncLoads.Add(assetBundleRef, assetBundleRequest);

                            while (!assetBundleRequest.isDone)
                            {
                                yield return null;
                            }

                            TryLoadAssetBundleFromAsyncRequest(assetBundleRequest, out psAssetBundle);
                            _inProgressBundleAsyncLoads.Remove(assetBundleRef);
                        }

                        if (psAssetBundle == null)
                        {
                            Debug.LogErrorFormat("AssetBundleService: * failed to load {0}", assetBundleRef);
                            continue;
                        }

                        var assetNames = psAssetBundle.GetAllAssetNames();

                        Debug.LogFormat("AssetBundleService: * loaded {0} which contains {1} assets.", psAssetBundle.name, assetNames.Length);
                        _bundleCache.Add(assetBundleRef, psAssetBundle); 
                    }
                }
            }
            else
            {
                if (_inProgressBundleAsyncLoads.TryGetValue(assetBundleRef, out var ongoingAsyncRequest))
                {
                    Debug.LogWarning($"[AssetBundleService]: Tried to async load the same bundle twice ({assetBundleRef}). Waiting on the previous load before doing anything else.");

                    while (!ongoingAsyncRequest.isDone)
                    {
                        yield return null;
                    }
                }

                if (!_bundleCache.TryGetValue(assetBundleRef, out var psAssetBundle))
                {
                    Debug.LogFormat("AssetBundleService preloading {0}", assetBundleRef);

                    AssetBundle assetBundle;
                    if (PRELOAD_BUNDLES_IN_MEMORY)
                    {
                        Profiler.BeginSample("ReadAllBytes");
                        var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                        Profiler.EndSample(); 

                        var assetBundleRequest = AssetBundle.LoadFromMemoryAsync(assetBundleBytes);
                        _inProgressBundleAsyncLoads.Add(assetBundleRef, assetBundleRequest);

                        while (!assetBundleRequest.isDone)
                        {
                            yield return null;
                        }

                        TryLoadAssetBundleFromAsyncRequest(assetBundleRequest, out assetBundle);
                        _inProgressBundleAsyncLoads.Remove(assetBundleRef);
                    }
                    else
                    {
                        var assetBundleRequest = AssetBundle.LoadFromFileAsync(assetBundleRef);
                        _inProgressBundleAsyncLoads.Add(assetBundleRef, assetBundleRequest);

                        while (!assetBundleRequest.isDone)
                        {
                            yield return null;
                        }

                        TryLoadAssetBundleFromAsyncRequest(assetBundleRequest, out assetBundle);
                        _inProgressBundleAsyncLoads.Remove(assetBundleRef);
                    }

                    if (assetBundle == null)
                    {
                        Debug.LogErrorFormat("AssetBundleService: * failed to load {0}", assetBundleRef);
                        // _asyncInitializingBundle = false;
                        yield break;
                    }

                    var assetNames = assetBundle.GetAllAssetNames();

                    Debug.LogFormat("AssetBundleService: * loaded {0} which contains {1} assets.", assetBundle.name, assetNames.Length);

                    _bundleCache.Add(assetBundleData.name, assetBundle);
                }
            }
        }

        private static bool TryLoadAssetBundleFromAsyncRequest(AssetBundleCreateRequest request, out AssetBundle bundle)
        {
            try
            {
                bundle = request.assetBundle;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);

                bundle = null;
                return false; 
            }
        }

        /// <summary>
        /// Initializes only a single bundle. If you specify specificAssetGuid and a "packed separately" AssetBundleData, you will only initialize the bundle containing specificAssetGuid.
        /// </summary>
        /// <param name="assetBundleData"></param>
        /// <param name="specificAssetGuid"></param>
        public AssetBundle SyncInitializeBundle(AssetBundleData assetBundleData, string specificAssetGuid = null)
        {
            if (assetBundleData == null)
            {
                Debug.LogError($"[AssetBundleService]: Tried to initialize a null bundle.");
                return null; 
            }

            if (GetBundleInitialized(assetBundleData, specificAssetGuid: specificAssetGuid))
            {
                Debug.LogWarning($"[AssetBundleService]: Tried to initialzie {assetBundleData.name} ({specificAssetGuid}) twice!");

                var loadedBundle = TryGetInitializedBundle(assetBundleData, specificAssetGuid: specificAssetGuid);
                return loadedBundle;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                if(!assetBundleData.ForceLoadInEditor)
                {
                    assetBundleData.RefreshLookupTable();
                    _bundleCache.Add(assetBundleData.name, null); 

                    return null; 
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

            AssetBundle assetBundle = null;

            if (assetBundleData.PackSeparately)
            {
                foreach (var assetData in assetBundleData.Assets)
                {
                    if(!string.IsNullOrEmpty(specificAssetGuid) && !specificAssetGuid.Equals(assetData.GUID, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    assetBundleRef = assetBundleData.GetPackedBundleDataName(assetData, platformName, assetBundleRoot, assetBundleRef);

                    if(_inProgressBundleAsyncLoads.TryGetValue(assetBundleRef, out var asyncRequest))
                    {
                        Debug.LogError($"[AssetBundleService]: Tried to sync load a bundle while an async load on that same bundle was in progress. Aborting the sync load. bundle: {assetBundleRef}");
                        return null; 
                    }

                    if(!_bundleCache.TryGetValue(assetBundleRef, out assetBundle))
                    {
                        if(PRELOAD_BUNDLES_IN_MEMORY)
                        {
                            var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                            assetBundle = AssetBundle.LoadFromMemory(assetBundleBytes);
                        }
                        else
                        {
                            assetBundle = AssetBundle.LoadFromFile(assetBundleRef);
                        }

                        if(assetBundle == null)
                        {
                            Debug.LogError($"[AssetBundleService]: Failed to sync load bundle {assetBundleRef}");
                            return null; 
                        }

                        var assetNames = assetBundle.GetAllAssetNames();
                        _bundleCache.Add(assetBundleRef, assetBundle); 
                    }
                }
            }
            else
            {
                if (_inProgressBundleAsyncLoads.TryGetValue(assetBundleRef, out var asyncRequest))
                {
                    Debug.LogError($"[AssetBundleService]: Tried to sync load a bundle while an async load on that same bundle was in progress. Aborting the sync load. bundle: {assetBundleRef}");
                    return null;
                }


                if(PRELOAD_BUNDLES_IN_MEMORY)
                {
                    var assetBundleBytes = System.IO.File.ReadAllBytes(assetBundleRef);
                    assetBundle = AssetBundle.LoadFromMemory(assetBundleBytes);
                }
                else
                {
                    assetBundle = AssetBundle.LoadFromFile(assetBundleRef);
                }

                if (assetBundle == null)
                {
                    Debug.LogError($"[AssetBundleService]: Failed to sync load bundle {assetBundleRef}");
                    return null;
                }

                var assetNames = assetBundle.GetAllAssetNames();
                _bundleCache.Add(assetBundleData.name, assetBundle);
            }

            return assetBundle; 
        }

        public Coroutine DeInitializeAsync(bool clearBundleAssetData, bool runResourcesUnload)
        {
            return StartCoroutine(DoDeinitializeAsync(clearBundleAssetData, runResourcesUnload));
        }

        private IEnumerator DoDeinitializeAsync(bool clearBundleAssetData, bool runResourcesUnload)
        {
            _initialized = false; // consider deinitialized immediately (to prevent loads or other initializations from starting) 

            var awaitBundleLoadTimeout = Time.time + 10; 

            while(_inProgressBundleAsyncLoads.Count > 0 && Time.time < awaitBundleLoadTimeout)
            {
                yield return null; 
            }

            if(Time.time >= awaitBundleLoadTimeout)
            {
                Debug.LogError($"[AssetBundleService]: Timed out awaiting async bundle initializations. You want initializations to complete before we deinitialize the plugin.");
            }

            if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine);
            UnloadAllCachedAssets(clearBundleAssetData, runResourcesUnload);

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                // todo: clear GenericDictionary<T>? 
            }
#endif
        }

        public T LoadSync<T>(AssetReference<T> reference, bool logErrors = true, bool allowInitializeBundle = true) where T : Object
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)
            {
                return reference.EditorLoadAsset(this, logDeleted: false);
            }
#endif

            if (!GetIsInitialized())
            {
                Debug.LogError($"[AssetBundleService] Tried to LoadSync, but we're not yet initialized.");
                return null; 
            }

            if (string.IsNullOrEmpty(reference.Guid))
            {
                if(logErrors)
                {
                    Debug.LogError($"[AssetBundleService] LoadAsync() Requested null reference? {reference.Name} ({reference.Guid}:{reference.LocalFileId})");
                }

                return null;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var assetBundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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
            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors);
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
                var cacheKey = reference.GetCacheKey();
                if(_assetCache.TryGetValue(cacheKey, out var existingAssetCache))
                {
                    existingAssetCache.Asset = (Object) subObj;
                    existingAssetCache.Guid = reference.Guid;
                    existingAssetCache.SubAssetReference = reference.SubAssetReference;
                    existingAssetCache.Request = null;
                    existingAssetCache.BundleName = assetBundle.name;
                }
                else
                {
                    _assetCache.Add(cacheKey, new AssetCache()
                    {
                        Asset = (Object)subObj,
                        Guid = reference.Guid,
                        SubAssetReference = reference.SubAssetReference,
                        Request = null,
                        BundleName = assetBundle.name,
                    });
                }
            }

            return subObj;
        }

        public AssetBundleData GetAssetBundleContainer<T>(AssetReference<T> reference, bool logErrors = true) where T : Object
        {
            return GetAssetBundleContainer(reference.Guid, logErrors: logErrors);
        }

        public AssetBundleData GetAssetBundleContainer(string guid, bool logErrors = true) 
        {
            if(_bundleDataLookup.TryGetValue(guid, out var bundleData))
            {
                return bundleData;
            }

            // Debug.LogError($"[AssetBundleService] Failed to quickly locate guid {guid}'s bundleData? Looking manually.");

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

            if(logErrors)
            {
                Debug.LogError($"[AssetBundleService] Failed to slowly locate guid {guid}.");
            }

            return null;
        }

        public string GetAssetBundleReferenceFromGuid<T>(AssetReference<T> reference, bool logErrors = true) where T : Object
        {
            var bundle = GetAssetBundleContainer(reference, logErrors: logErrors);

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
            var cacheKey = reference.GetCacheKey();
            if (_assetCache.TryGetValue(cacheKey, out AssetCache cache))
            {
                // release the original request 
                // failing to release this eventually will result in a memory leak 
                if(cache.Asset != null && cache.Request != null)
                {
                    cache.Request = null;
                    _assetCache[cacheKey] = cache; 
                }

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
            cache.Request = null; // do not need this anymore 

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
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (!string.IsNullOrEmpty(reference.SubAssetReference))
                    {
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors));
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

                var assetBundleReference = GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors);
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
        private T GetSubAssetAsyncResult<T>(AssetReference<T> reference, Object[] assets, bool logErrors = true) where T : Object
        {
            // we don't live in a world this pure.
            // every day, we stray further from God's light.
            // return (T) asset;

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
                if (bundleData == null || !bundleData.ForceLoadInEditor)
                {
                    if (!string.IsNullOrEmpty(reference.SubAssetReference))
                    {
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors));
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
                var assetBundle = SyncInitializeBundle(data);
                if(assetBundle == null)
                {
                    Debug.LogError($"[AssetBundleService]: Failed to LoadSyncWholeBundle() because bundle could not be initialized.");
                    return; 
                }
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
        public IEnumerator DoPrewarmBatched<T>(List<AssetReference<T>> references, bool logErrors = true) where T : Object
        {
            var requests = new List<AssetBundleRequest>();

            foreach (var reference in references)
            {
#if UNITY_EDITOR
                if (EditorGetAssetDatabaseEnabled())
                {

                    var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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

                var bundleReference = GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors);
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
        public IEnumerator LoadAsyncBatched<T>(List<AssetReference<T>> references, bool logErrors = false) where T : Object
        {
            // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: loading {0} assets...", references.Count);

            if(Instance == null || !_initialized)
            {
                yield return new WaitAssetBundleServiceReady();
            }

            var awaiting = new List<Coroutine>(references.Count);

            for (var i = references.Count - 1; i >= 0; --i)
            {
                var reference = references[i];

#if UNITY_EDITOR
                if (EditorGetAssetDatabaseEnabled())
                {
                    var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
                    if (bundleData == null || !bundleData.ForceLoadInEditor)
                    {
                        awaiting.Add(null);
                        continue;
                    }
                }
#endif

                var cached = TryGetCachedAsset(reference);
                if (cached != null)
                {
                    // Debug.LogFormat("AssetBundleService.LoadAsyncBatched: skipped {0}, already loaded", references[i].Name);
                    // references.RemoveAt(i);
                    awaiting.Add(null);
                    continue;
                }

                var func = LoadAsync(reference, logErrors: logErrors);
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

                GetAsyncResult(reference, logErrors: logErrors);
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

        public string BuildPackedAssetBundleName(AssetBundleData assetBundleData, string assetGuid)
        {
            var isDedicatedServer = false;

#if UNITY_SERVER
            isDedicatedServer = true;
#endif

            var bundleAssetReferenceData = assetBundleData.FindByGuid(assetGuid);

            if(bundleAssetReferenceData == null)
            {
                Debug.LogError($"[AssetBundleService] asset {assetGuid} not found in bundle {assetBundleData.name}?");
                return string.Empty;
            }

            var platformName = GetRuntimePlatformName(Application.platform, isDedicatedServer);
            var assetBundleDataName = GetBundleFilenameFromBundleName(Application.platform, isDedicatedServer, assetBundleData.name);
            var assetBundleRoot = GetBundlesDeployFolder();
            var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";
            assetBundleRef = assetBundleData.GetPackedBundleDataName(bundleAssetReferenceData, platformName, assetBundleRoot, assetBundleRef);

            return assetBundleRef; 
        }

        public AssetBundle TryGetAssetBundle<T>(AssetReference<T> reference, bool allowInitializeBundle = false, bool logErrors = true) where T : Object
        {
            var assetBundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
            if (assetBundleData == null)
            {
                if(logErrors)
                {
                    Debug.LogError($"AssetBundleData not found for: {reference.Name} ({reference}). loaded bundles count: {_bundleCache.Count}");
                }

                return null;
            }

            bool found = false;
            AssetBundle bundle = null;

            if (assetBundleData.PackSeparately)
            {
                found = _bundleCache.TryGetValue(BuildPackedAssetBundleName(assetBundleData, reference.Guid), out bundle);
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
            
                var loadedAssetBundle = SyncInitializeBundle(assetBundleData, specificAssetGuid: reference.Guid);
                if(loadedAssetBundle == null)
                {
                    Debug.LogError($"[AssetBundleService]: Failed to TryGetAssetBundle() because SyncInitializeBundle() failed.");
                    return null; 
                }

                if (assetBundleData.PackSeparately)
                {
                    found = _bundleCache.TryGetValue(BuildPackedAssetBundleName(assetBundleData, reference.Guid), out bundle);
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
        public T TryGetCachedAsset<T>(AssetReference<T> reference, bool logErrors = true) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(reference.Guid))
            {
                Debug.LogWarning($"requested null guid reference? {reference}");
                return null;
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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

        public void LoadAsyncCallback<T>(AssetReference<T> reference, bool logErrors = true, bool allowInitializeBundle = true, object context = null, System.Action<T, object> onComplete = null) where T : UnityEngine.Object
        {
            StartCoroutine(DoLoadAsyncWithCallback(reference, logErrors, allowInitializeBundle, context, onComplete));
        }

        private IEnumerator DoLoadAsyncWithCallback<T>(AssetReference<T> reference, bool logErrors, bool allowInitializeBundle, object context, System.Action<T, object> onComplete) where T : UnityEngine.Object
        {
            yield return LoadAsync(reference, logErrors: logErrors, allowInitializeBundle: allowInitializeBundle);
            var result = GetAsyncResult(reference, logErrors: logErrors);

            if(onComplete != null)
            {
                onComplete.Invoke(result, context); 
            }

            yield break; 
        }

        public bool GetBundleInitializedForReference<T>(AssetReference<T> reference, bool logErrors = true) where T : UnityEngine.Object
        {
            var assetBundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
            if (assetBundleData != null)
            {
                if (assetBundleData.PackSeparately)
                {
                    return _bundleCache.TryGetValue(BuildPackedAssetBundleName(assetBundleData, reference.Guid), out _);
                }
                else
                {
                    return _bundleCache.TryGetValue(assetBundleData.name, out _);
                }
            }

            return false; 
        }

        public IEnumerator InitializeBundleForReferenceIfNecessaryAsync<T>(AssetReference<T> reference, bool logErrors = true) where T : UnityEngine.Object
        {
            var assetBundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
            if (assetBundleData == null)
            {
                if (logErrors)
                {
                    Debug.LogError($"AssetBundleData not found for: {reference.Name} ({reference}). loaded bundles count: {_bundleCache.Count}");
                }

                yield break;
            }

            bool found;
            if (assetBundleData.PackSeparately)
            {
                found = _bundleCache.TryGetValue(BuildPackedAssetBundleName(assetBundleData, reference.Guid), out _);
            }
            else
            {
                found = _bundleCache.TryGetValue(assetBundleData.name, out _);
            }

            if (!found)
            {
                _assetCache.Add(reference.GetCacheKey(), new AssetCache()
                {
                    Guid = reference.Guid,
                    SubAssetReference = reference.SubAssetReference,
                });

                yield return InitializeSingleBundle(assetBundleData, specificAssetGuid: reference.Guid);

                if (!GetIsInitialized())
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Queues up an async bundle request. The bundle requested is parsed from the reference string passed in. Returns the handle, which can be yielded. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reference"></param>
        /// <returns></returns>
        public IEnumerator LoadAsync<T>(AssetReference<T> reference, bool logErrors = true, bool allowInitializeBundle = true) where T : UnityEngine.Object
        {
            // reference.Reference = reference.Reference.ToLowerInvariant();

            if (!GetIsInitialized())
            {
                yield return new WaitAssetBundleServiceReady();
            }

#if UNITY_EDITOR
            if (EditorGetAssetDatabaseEnabled())
            {
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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

                // if(logErrors)
                // {
                //     Debug.LogWarning($"{reference} was requested, but the async request could not be found?");
                // }

                yield break;
            }

            if(allowInitializeBundle && ASYNC_INITIALIZE_FROM_LOADS)
            {
                yield return InitializeBundleForReferenceIfNecessaryAsync(reference, logErrors: logErrors);
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

            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors);
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

            var cacheKey = reference.GetCacheKey();
            if (_assetCache.TryGetValue(cacheKey, out var existingAssetCache))
            {
                existingAssetCache.Asset = (Object) null;
                existingAssetCache.Guid = reference.Guid;
                existingAssetCache.SubAssetReference = reference.SubAssetReference;
                existingAssetCache.Request = handle;
                existingAssetCache.BundleName = assetBundle.name;
            }
            else
            {
                _assetCache.Add(cacheKey, new AssetCache()
                {
                    Asset = (Object)null,
                    Guid = reference.Guid,
                    SubAssetReference = reference.SubAssetReference,
                    Request = handle,
                    BundleName = assetBundle.name,
                });
            }

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
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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
                var bundleData = GetAssetBundleContainer(reference, logErrors: logErrors);
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

            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference, logErrors: logErrors);
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
                var bundleData = GetAssetBundleContainer(reference.Guid, logErrors: logErrors);
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
        public string GetSceneNameFromSceneReference<T>(AssetReference<T> sceneReference, bool logErrors = true) where T : Object
        {
            var reference = GetAssetBundleReferenceFromGuid(sceneReference, logErrors: logErrors);
            if (string.IsNullOrEmpty(reference)) return null;

            var split = reference.Split('/');
            if (split == null || split.Length == 0) return null;

            var name = split[split.Length - 1];
                name = name.Replace(".unity", string.Empty);

            return name;
        }

        /// <summary>
        /// Note: This will not automatically unload bundle data. It just destroys the runtime data associated with this asset. The data may still be cached by the internal asset bundle system. 
        /// </summary>
        public void UnloadSingleAsset<T>(T asset, AssetReference<T> assetReferencce) where T : Object
        {
            Resources.UnloadAsset(asset);
            var cacheKey = assetReferencce.GetCacheKey();
            _assetCache.Remove(cacheKey); 
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
                return assetBundleOperation != null && !assetBundleOperation.isDone && AssetBundleService.Instance.GetIsInitialized();
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