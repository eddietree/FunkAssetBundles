using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(AssetBundleService))]
public class AssetBundleServiceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        // note: can we update this as build time? pre process build step 
        if(GUILayout.Button("Refresh bundle list"))
        {
            AssetBundleService.EditorRefreshAssetBundleListOnPrefab(); 
        }
        
        if(GUILayout.Button("Refresh references"))
        {
            AssetBundleService.EditorUpdateBundleReferencesForBuilds(); 
        }
    }
}
#endif

[DefaultExecutionOrder(-10000)]
public class AssetBundleService : MonoBehaviour
{
    public List<AssetBundleData> AssetBundleDatas = new List<AssetBundleData>(); 

    public static string GetBundlesDeployFolder()
    {
        return System.IO.Path.Combine(Application.streamingAssetsPath, "bundles");
    }


#if UNITY_EDITOR
    // when true, the editor will load things from AssetDatabase instead of from asset bundles. 
    // this is useful because asset bundles will not need to be built while making editor changes.
    public const bool USE_ASSETDATABASE = true;
    public const bool DEBUG_DELAY_RANDOMIZE = false;
    public const float DEBUG_DELAY_CONSTANT = 0.00f;

    public T LoadFromAssetBundle<T>(AssetReference<T> reference) where T : Object
    {
        var bundle = EditorFindContainerBundle(reference.Guid);
        if(bundle == null)
        {
            Debug.LogError($"[{reference}] is not in any bundle. this is okay in the editor - but not in builds. Add it to a bundle!");
        }

        return reference.EditorLoadAsset(this);
    }
#endif

    [System.NonSerialized] private Dictionary<string, AssetBundle> _bundleCache = new Dictionary<string, AssetBundle>();
    [System.NonSerialized] private Dictionary<string, AssetCache> _assetCache = new Dictionary<string, AssetCache>();

    [System.NonSerialized] private Coroutine _prewarmRoutine;

    private struct AssetCache
    {
        public string Guid;
        public AssetBundle Bundle;
        public AssetBundleRequest Request;
        public Object Asset;
    }

#if UNITY_EDITOR
    private static AssetBundleService _editorStaticInstance;
    [System.NonSerialized] private bool _editorHasRefreshedBundleData;

    public static void EditorRefreshAssetBundleListOnPrefab()
    {
        if (_editorStaticInstance == null)
        {
            _editorStaticInstance = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbBootstrap");
        }

        if(_editorStaticInstance != null)
        {
            AssetDatabaseE.LoadScriptableObjects(_editorStaticInstance.AssetBundleDatas);
            EditorUtility.SetDirty(_editorStaticInstance);
        }
    }

    public static AssetBundleData EditorFindContainerBundle(string guid)
    {
        if(_editorStaticInstance == null)
        {
            _editorStaticInstance = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbBootstrap");
        }

        if(!_editorStaticInstance._editorHasRefreshedBundleData)
        {
            _editorStaticInstance._editorHasRefreshedBundleData = true;

            foreach (var data in _editorStaticInstance.AssetBundleDatas)
            {
                data.RefreshLookupTable();
            }
        }

        if(_editorStaticInstance != null)
        {
            foreach(var data in _editorStaticInstance.AssetBundleDatas)
            {
                if(data.ContainsAssetRef(guid))
                {
                    return data;
                }
            }
        }

        return null; 
    }

    public static void EnsureReferenceInBundle(AssetBundleData bundleData, string guid)
    {
        var assetData = new AssetBundleReferenceData()
        {
            GUID = guid,
            AssetBundleReference = AssetDatabase.GUIDToAssetPath(guid).ToLowerInvariant(),
        };

        if (!bundleData.Assets.Contains(assetData))
        {
            bundleData.Assets.Add(assetData);
        }

        EditorUtility.SetDirty(bundleData);
    }

    public static void EnsureReferenceInAnyBundle(string guid, AssetBundleData defaultBundleData = null)
    {
        var existingContainer = EditorFindContainerBundle(guid);
        if(existingContainer == null)
        {
            if (defaultBundleData == null && _editorStaticInstance != null && _editorStaticInstance.AssetBundleDatas.Count > 0)
            {
                defaultBundleData = _editorStaticInstance.AssetBundleDatas[0];
            }

            if(defaultBundleData != null)
            {
                EnsureReferenceInBundle(defaultBundleData, guid); 
            }            
        }
    }

    public static void EditorUpdateBundleReferencesForBuilds()
    {
        if (_editorStaticInstance == null)
        {
            _editorStaticInstance = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbBootstrap");
        }

        if(_editorStaticInstance != null)
        {
            foreach (var bundleData in _editorStaticInstance.AssetBundleDatas)
            {
                foreach(var asset in bundleData.Assets)
                {
                    asset.AssetBundleReference = AssetDatabase.GUIDToAssetPath(asset.GUID).ToLowerInvariant();
                }

                EditorUtility.SetDirty(bundleData); 
            }

            UnityEditor.EditorUtility.SetDirty(_editorStaticInstance); 
        }
    }
#endif

    public static string GetRuntimePlatformName(RuntimePlatform platform)
    {
        string platformName;

        switch (platform)
        {
            case RuntimePlatform.WindowsEditor:
            case RuntimePlatform.WindowsPlayer:
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

        return platformName;
    }

    public static string GetBundleFilenameFromBundleName(RuntimePlatform platform, string bundle)
    {
        var platformName = GetRuntimePlatformName(platform); 
        return $"{platformName}/{bundle.ToLowerInvariant()}.bundle";
    }

    public void UnloadAllCachedAssets(bool destroyInstancesToo)
    {
#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            Debug.Log($"Editor is not using AssetBundles. This UnloadAllCachedAssets() has been skipped.");
            return;
        }
#endif

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

        if(_prewarmRoutine != null)
        {
            StopCoroutine(_prewarmRoutine);
            _prewarmRoutine = null;
        }

        foreach(var request in _prewarmAssetRequests)
        {
            asyncLoading.Add(new AssetReference<Object>() { Guid = request.Guid });
        }

        Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - completeting {asyncLoading.Count} async loads NOW.");

        foreach(var reference in asyncLoading)
        {
            LoadSync(reference); 
        }

        Debug.Log($"UnloadAllCachedAssets({destroyInstancesToo}) - unloading asset bundles");

        foreach (var entry in _bundleCache)
        {
            var bundle = entry.Value;
            bundle.Unload(destroyInstancesToo);
        }

        foreach(var entry in _assetCache)
        {
            var cache = entry.Value;
                cache.Request = null; 
        }

        _bundleCache.Clear();
        _assetCache.Clear();
        _prewarmAssetRequests.Clear();

        // crashes in il2cpp sometimes 
        // Caching.ClearCache(); 
    }

    public void Initialize()
    {
#if UNITY_EDITOR
        if(USE_ASSETDATABASE)
        {
            //Debug.Log($"Editor is not using AssetBundles. All requests will be artificially {DEBUG_DELAY_CONSTANT} seconds long. Set USE_ASSETDATABASE to false to use AssetBundles in the editor.");

            for (var i = 0; i < AssetBundleDatas.Count; ++i)
            {
                var bundle = AssetBundleDatas[i];
                bundle.RefreshLookupTable(); 
            }

            return;
        }
#endif

        var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

        Debug.LogFormat("AssetBundleService: preloading {0} asset bundles (not their contents)...", AssetBundleDatas.Count);

        for (var i = 0; i < AssetBundleDatas.Count; ++i)
        {
            var assetBundleData = AssetBundleDatas[i];

            // initialize the quick lookup table 
            assetBundleData.RefreshLookupTable();

            var assetBundleDataName = GetBundleFilenameFromBundleName(Application.platform, assetBundleData.name);
            var assetBundleRoot = GetBundlesDeployFolder();
            var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";

            Debug.LogFormat("AssetBundleService: {0}: preloading {1}", i, assetBundleRef);

            try
            {
                var assetBundle = AssetBundle.LoadFromFile(assetBundleRef);
                if(assetBundle == null)
                {
                    Debug.LogErrorFormat("AssetBundleService: * failed to load {0}", assetBundleRef);
                    continue;
                }

                var assetNames = assetBundle.GetAllAssetNames();

                Debug.LogFormat("AssetBundleService: * loaded {0} which contains {1} assets.", assetBundle.name, assetNames.Length);

                // debug print names 
                // var assetNames = assetBundle.GetAllAssetNames();
                // foreach(var name in assetNames)
                // {
                //     Debug.Log(name);
                // }

                _bundleCache.Add(assetBundleData.name, assetBundle);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        stopwatch.Stop();

        Debug.LogFormat("AssetBundleService: finished preloading {0} asset bundles (completed in {1}ms)", AssetBundleDatas.Count, stopwatch.ElapsedMilliseconds);

        if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine); 
        _prewarmRoutine = StartCoroutine(DoHandlePrewarmAssets());
    }

    public void DeInitialize()
    {
        if (_prewarmRoutine != null) StopCoroutine(_prewarmRoutine); 
        UnloadAllCachedAssets(true);

#if UNITY_EDITOR
        if(USE_ASSETDATABASE)
        {
            // todo: clear GenericDictionary<T>? 
        }
#endif
    }

    public T LoadSync<T>(AssetReference<T> reference) where T : Object
    {
        if (string.IsNullOrEmpty(reference.Guid))
        {
            Debug.LogError($"LoadAsync() Requested null reference? {reference.Name} ({reference.Guid}:{reference.LocalFileId})");
            return null;
        }

#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            return LoadFromAssetBundle(reference); 
        }
#endif

        var cachedResult = TryGetCachedResult(reference);
        if (cachedResult != null)
        {
            return GetSubAsset<T>(reference, cachedResult);
        }

        // if previously required with an async that has not yet finished, block the main thread to fetch it. 
        var previousRequest = GetAsyncRequest(reference);
        if (previousRequest != null)
        {
            Debug.Log($"Tried to sync load asset currently being async loaded. {reference}");
        }

        var assetBundle = TryGetAssetBundle(reference);
        if (assetBundle == null)
        {
            Debug.LogError($"no asset bundle found for {reference}");
            return null;
        }

        // convert guid to reference string 
        var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
        if(string.IsNullOrEmpty(assetBundleReference))
        {
            Debug.LogError($"Failed to fetch reference for {reference.Guid}, {reference.Name}");
            return null; 
        }

        Object obj;
        if (typeof(T).IsSubclassOf(typeof(Component)))
        {
            // Debug.Log($"{typeof(T)} is a subclass of {typeof(Component)}");

            obj = assetBundle.LoadAsset<GameObject>(assetBundleReference);
        }
        else
        {
            // Debug.Log($"{typeof(T)} is not a subclass of {typeof(Component)}");
            obj = assetBundle.LoadAsset<T>(assetBundleReference);
        }

        if (obj == null)
        {
            Debug.LogError($"{reference.Name} ({reference.Guid}) [{assetBundleReference}] returned null from bundle {assetBundle.name}?");
            return null;
        }

        if (previousRequest != null)
        {
            CacheResult(reference, (T) obj);
        }
        else
        {
            _assetCache.Add(reference.Guid, new AssetCache()
            {
                Asset = obj,
                Guid = reference.Guid,
                Request = default,
                Bundle = assetBundle,
            });
        }

        return GetSubAsset<T>(reference, obj);
    }

    public AssetBundleData GetAssetBundleContainer<T>(AssetReference<T> reference) where T : Object
    {
        foreach (var bundle in AssetBundleDatas)
        {
            if(bundle.ContainsAssetRef(reference.Guid))
            {
                return bundle;
            }
        }

        return null; 
    }

    public string GetAssetBundleReferenceFromGuid<T>(AssetReference<T> reference) where T : Object
    {
        var bundle = GetAssetBundleContainer(reference);

        if(bundle == null)
        {
            Debug.LogError($"could not find bundle for asset: {reference.Name} ({reference.Guid})");
            return null;
        }

        var assetData = bundle.FindByGuid(reference.Guid);
        if(assetData != null)
        {
            return assetData.AssetBundleReference;
        }

        return null;
    }

    private Object TryGetCachedResult<T>(AssetReference<T> reference) where T : Object 
    {
        if(_assetCache.TryGetValue(reference.Guid, out AssetCache cache))
        {
            return cache.Asset;
        }

        else
        {
            return null; 
        }
    }

    private void CacheResult<T>(AssetReference<T> reference, T obj) where T : Object
    {
        var cache = _assetCache[reference.Guid];

        if(cache.Asset != null)
        {
            Debug.LogWarning($"This asset was already cached? {reference}");
        }

        // reset the local position of parent GameObjects (prefab roots) 
        if(obj != null && obj.GetType() == typeof(GameObject))
        {
            var prefab = (GameObject) (Object) obj;
            if(prefab != null)
            {
                prefab.transform.ResetLocalPos();
                prefab.transform.ResetLocalRot();
            }
        }

        cache.Asset = obj;

        _assetCache[reference.Guid] = cache;
    }

    /// <summary>
    /// oh. so THIS is why addressables was so slow 🔪
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reference"></param>
    /// <param name="bundle"></param>
    /// <param name="asset"></param>
    /// <returns></returns>
    private T GetSubAsset<T>(AssetReference<T> reference, Object asset) where T : Object
    {
        // we don't live in a world this pure.
        // every day, we stray further from God's light.
        // return (T) asset;

#if UNITY_EDITOR
        if(USE_ASSETDATABASE)
        {
            if(!string.IsNullOrEmpty(reference.SubAssetReference))
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(GetAssetBundleReferenceFromGuid(reference));
                for(var i = 0; i < subAssets.Length; ++i)
                {
                    var subAsset = subAssets[i];
                    if(subAsset.name == reference.SubAssetReference)
                    {
                        if(subAsset as T != null)
                        {
                            return (T) subAsset;
                        }
                    }
                }

                return (T) asset; 
            }
        }
#endif

        // if we have a sub asset reference, load all the subassets and find by name 
        // currently, only Sprites should be refereced via sub asset references 
        if(!string.IsNullOrEmpty(reference.SubAssetReference))
        {
            var assetBundle = TryGetAssetBundle(reference);
            if (assetBundle == null)
            {
                Debug.LogError($"no asset bundle found for {reference}");
                return null;
            }

            var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
            if(string.IsNullOrEmpty(assetBundleReference))
            {
                Debug.LogError($"did not find asset bundle reference for {reference}");
                return null;
            }

            var subAssets = assetBundle.LoadAssetWithSubAssets<T>(assetBundleReference);
            var subAssetCount = subAssets.Length;
            for(var i = 0; i < subAssetCount; ++i)
            {
                var subAsset = subAssets[i];
                if(subAsset.name == reference.SubAssetReference)
                {
                    if(subAsset as T != null)
                    {
                        return (T) subAsset;
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
            return (T) asset; 
        }
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
#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            yield break;
        }
#endif

        var bundle = TryGetBundleFromBundleData(data);
        yield return bundle.LoadAllAssetsAsync();
    }

    public void LoadSyncWholeBundle(AssetBundleData data)
    {
#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            return;
        }
#endif

        var bundle = TryGetBundleFromBundleData(data);
        bundle.LoadAllAssets();
    }

    public IEnumerator LoadAsyncBatched<T>(List<AssetReference<T>> references) where T : Object
    {
        Debug.LogFormat("AssetBundleService.LoadAsyncBatched: loading {0} assets...", references.Count);

        var referenceCount = references.Count;
        var awaiting = new List<Coroutine>(referenceCount);

        for(var i = 0; i < referenceCount; ++i)
        {
            var reference = references[i];

            var func = LoadAsync(reference);
            var routine = StartCoroutine(func);

            Debug.LogFormat("AssetBundleService.LoadAsyncBatched: > {0}: added", reference);

            awaiting.Add(routine);
        }

        for(var i = 0; i < referenceCount; ++i)
        {
            var reference = references[i];

            // TODO: change with CheckIfReady but that doesnt work for some reason
            var cached = TryGetCachedResult(reference);
            if (cached != null)
            {
                Debug.LogFormat("AssetBundleService.LoadAsyncBatched: skipped {0}, already loaded", references[i].Name);
                continue;
            }

            var time0 = Time.realtimeSinceStartup;
            yield return awaiting[i];
            var time1 = Time.realtimeSinceStartup;

            Debug.LogFormat("AssetBundleService.LoadAsyncBatched: loaded {0}, took {1}ms", references[i].Name, (int)((time1 - time0) * 1000.0f));

            GetAsyncResult(reference);
        }
    }

    public AssetBundle TryGetBundleFromBundleData(AssetBundleData assetBundleData)
    {
        if(_bundleCache.TryGetValue(assetBundleData.name, out AssetBundle bundle))
        {
            return bundle;
        }

        return null; 
    }

    public AssetBundle TryGetAssetBundle<T>(AssetReference<T> reference) where T : Object
    {
        var assetBundleData = GetAssetBundleContainer(reference);
        if(assetBundleData == null)
        {
            Debug.LogError($"Asset bundle not found for: {reference.Name} ({reference}). loaded bundles count: {_bundleCache.Count}");
            return null;
        }

        var found = _bundleCache.TryGetValue(assetBundleData.name, out AssetBundle bundle);
        if(found)
        {
            return bundle;
        }
        else
        {
            Debug.LogError($"Asset bundle '{assetBundleData.name}' not found? for: '{reference}'. loaded bundles count: {_bundleCache.Count}");
            foreach (var entry in _bundleCache)
            {
                Debug.LogError($"loaded: {entry.Key}");
            }
        
            return null;
        }
    }

    /// <summary>
    /// Queues up an async bundle request. The bundle requested is parsed from the reference string passed in. Returns the handle, which can be yielded. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reference"></param>
    /// <returns></returns>
    public IEnumerator LoadAsync<T>(AssetReference<T> reference) where T : UnityEngine.Object
    {
        // reference.Reference = reference.Reference.ToLowerInvariant();

#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            if (DEBUG_DELAY_RANDOMIZE)
                yield return new WaitForSeconds(Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f)));
            if (DEBUG_DELAY_CONSTANT > 0.0f)
                yield return new WaitForSeconds(DEBUG_DELAY_CONSTANT);

            yield break; 
        }
#endif

        if (string.IsNullOrEmpty(reference.Guid))
        {
            Debug.LogError("LoadAsync() Requested null reference?");
            yield break; 
        }

#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            if (DEBUG_DELAY_RANDOMIZE)
                yield return new WaitForSeconds(Mathf.Max(0.0f, DEBUG_DELAY_CONSTANT + Random.Range(0.0f, 0.10f)));
            if (DEBUG_DELAY_CONSTANT > 0.0f)
                yield return new WaitForSeconds(DEBUG_DELAY_CONSTANT);

            yield break;
        }
#endif

        if (CheckIfRequestedLoad(reference))
        {
            var asyncRequest = GetAsyncRequest(reference);
            if (asyncRequest != null)
            {
                if(asyncRequest.isDone)
                {
                    Debug.Log($"{reference} was already requested, so skip waiting.");

                    yield break;
                }

                yield return new WaitUntilAsyncIsDone(asyncRequest);
                yield break; 
            }

            Debug.LogError($"{reference} was requested, but the async request could not be found?");

            yield break; 
        }

        var assetBundle = TryGetAssetBundle(reference);
        if (assetBundle == null)
        {
            yield break; 
        }

        var assetBundleReference = GetAssetBundleReferenceFromGuid(reference);
        if(string.IsNullOrEmpty(assetBundleReference))
        {
            Debug.LogError($"did not find asset bundle reference for {reference}");
            yield break; 
        }

        AssetBundleRequest handle;
        if (typeof(T).IsSubclassOf(typeof(Component)))
        {
            handle = assetBundle.LoadAssetAsync<GameObject>(assetBundleReference);
        }
        else
        {
            handle = assetBundle.LoadAssetAsync<T>(assetBundleReference);
        }

        _assetCache.Add(reference.Guid, new AssetCache()
        {
            Asset = null,
            Guid = reference.Guid,
            Request = handle,
            Bundle = assetBundle,
        });

        yield return handle; 
    }

    /// <summary>
    /// After yielding the handle from LoadAsync() or awaiting CheckIfRequestReady() is true, this will return the asset requested from the reference. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="reference"></param>
    /// <returns></returns>
    public T GetAsyncResult<T>(AssetReference<T> reference) where T : Object
    {
        // reference.Reference = reference.Reference.ToLowerInvariant();

        if(string.IsNullOrEmpty(reference.Guid))
        {
            Debug.LogWarning($"requested null guid reference? {reference}");
            return null;
        }

#if UNITY_EDITOR
        if (USE_ASSETDATABASE)
        {
            return LoadFromAssetBundle(reference);
        }
#endif

        var cachedResult = TryGetCachedResult(reference);
        if (cachedResult != null)
        {
            return GetSubAsset(reference, cachedResult);
        }

        var asyncRequest = GetAsyncRequest(reference);
        if (asyncRequest == null)
        {
            Debug.LogError($"Tried to get async request for '{reference}', but it was not first requested!");
            return LoadSync(reference);
        }

        if (!asyncRequest.isDone)
        {
            Debug.LogError($"Tried to get async request for '{reference}', but it's not finished loading!");
            return LoadSync(reference);
        }

        CacheResult(reference, (T) asyncRequest.asset);
        return GetSubAsset(reference, asyncRequest.asset);
    }

    /// <summary>
    /// Returns an existing AssetBundleRequest from the last LoadAsync() with a matching reference string. 
    /// </summary>
    /// <param name="reference"></param>
    /// <returns></returns>
    public AssetBundleRequest GetAsyncRequest<T>(AssetReference<T> reference) where T : Object
    {
        if(_assetCache.TryGetValue(reference.Guid, out AssetCache cache))
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
        return _assetCache.ContainsKey(reference.Guid); 
    }

    /// <summary>
    /// Returns true if the previous LoadAsync() is ready to be returned by GetAsyncResult().
    /// </summary>
    /// <param name="reference"></param>
    /// <returns></returns>
    public bool CheckIfRequestReady<T>(AssetReference<T> reference) where T : Object
    {
        var asyncRequest = GetAsyncRequest(reference);
        if(asyncRequest != null)
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
        foreach(var reference in assetReferences)
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
        while(true)
        {
            yield return null;

            if(_prewarmAssetRequests.Count == 0)
            {
                yield break; 
            }

            yield return LoadAsyncBatched(_prewarmAssetRequests);

            _prewarmAssetRequests.Clear(); 
        }
    }

    public static Coroutine InstantiatePrefab(AssetReference<GameObject> prefabRef, Vector3 position, Quaternion rotation, System.Action<GameObject> onComplete = null)
    {
        var assetBundleManager = LB.Services.assetBundles;
        return assetBundleManager.StartCoroutine(assetBundleManager.DoInstantiatePrefab(prefabRef, position, rotation, onComplete)); 
    }

    private IEnumerator DoInstantiatePrefab(AssetReference<GameObject> prefabRef, Vector3 position, Quaternion rotation, System.Action<GameObject> onComplete)
    {
        if(!prefabRef.IsValid())
        {
            Debug.LogError($"{prefabRef} is not a valid AssetReference<GameObject>");
            
            if(onComplete != null)
            {
                onComplete.Invoke(null); 
            }

            yield break;
        }

        yield return LoadAsync(prefabRef);
        var prefab = GetAsyncResult(prefabRef);

        if(prefab == null)
        {
            Debug.LogError($"GetAsyncResult({prefabRef}) failed to load prefab!");

            if (onComplete != null)
            {
                onComplete.Invoke(null);
            }

            yield break;
        }

        var result = GameObject.Instantiate(prefab, position, rotation);

        if(onComplete != null)
        {
            onComplete.Invoke(result);
        }
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
            foreach(var request in requests)
            {
                if(request != null && !request.isDone)
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