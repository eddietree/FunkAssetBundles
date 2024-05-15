#if UNITY_EDITOR
namespace FunkAssetBundles
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using System.Linq;
    using UnityEditor;

    [CanEditMultipleObjects]
    [CustomEditor(typeof(AssetBundleData))]
    public class AssetBundleDataEditor : Editor
    {
        // browser 
        private int _assetPageIndex;
        private const int _assetsPerPage = 25;
        private int _queryPageIndex;
        private string _searchQuery;
        private List<int> _searchResults = new List<int>();

        // bundle cache 
        private AssetBundleData _previouslySelectedInstance;
        private List<AssetBundleData> _assetBundleListCache = new List<AssetBundleData>();
        private string[] _assetBundlesList = null;
        private int _thisAssetBundleIndex = -1;
        private int _dependencySetBundleIndex = 0;

        private SerializedProperty EnabledInBuild;
        private SerializedProperty NoDependencies;
        private SerializedProperty LoadBundleOnInitialize;
        private SerializedProperty PackSeparately;
        private SerializedProperty SceneBundle;
        private SerializedProperty HideInLists;
        private SerializedProperty ForceLoadInEditor;
        private SerializedProperty DoNotBuildForDedicatedServer;

        private static bool _editorFoldout_browse_tools;

        private void OnEnable()
        {
            EnabledInBuild = serializedObject.FindProperty("EnabledInBuild");
            NoDependencies = serializedObject.FindProperty("NoDependencies");
            LoadBundleOnInitialize = serializedObject.FindProperty("LoadBundleOnInitialize");
            PackSeparately = serializedObject.FindProperty("PackSeparately");
            SceneBundle = serializedObject.FindProperty("SceneBundle");
            HideInLists = serializedObject.FindProperty("HideInLists");
            ForceLoadInEditor = serializedObject.FindProperty("ForceLoadInEditor");
            DoNotBuildForDedicatedServer = serializedObject.FindProperty("DoNotBuildForDedicatedServer");
        }

        // dependency stuff 
        private List<AssetDependency> _implicitDependencyCache = new List<AssetDependency>();

        // scene stuff
        private List<AssetDependency> _sceneRedundancyCache = new List<AssetDependency>();

        private void RefreshSceneRedundancies(AssetBundleData instance, int maxDepth)
        {
            EditorUtility.DisplayProgressBar("Scanning redundancies..", "Starting", 0f);

            _sceneRedundancyCache.Clear();

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene == null)
            {
                return;
            }

            // var rootGameobjects = activeScene.GetRootGameObjects();
            var allSceneObjects = GameObject.FindObjectsOfType<GameObject>();
            for (var i = 0; i < allSceneObjects.Length; ++i)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Scanning redundancies..", $"{i} / {allSceneObjects.Length}", (float)i / allSceneObjects.Length))
                {
                    break;
                }

                var sceneGameobject = allSceneObjects[i];
                var prefabType = PrefabUtility.GetPrefabAssetType(sceneGameobject);
                if (prefabType == PrefabAssetType.NotAPrefab)
                {
                    continue;
                }

                var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneGameobject);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);

                RecursiveDependencyScan(_sceneRedundancyCache, sceneGameobject, prefab, 0, maxDepth);
            }

            Debug.Log($"Scanned {allSceneObjects.Length} and found {_sceneRedundancyCache.Count} data Objects. Filtering for redundancies.");

            EditorUtility.DisplayProgressBar("Scanning redundancies..", "Filtering..", 1f);

            for (var i = _sceneRedundancyCache.Count - 1; i >= 0; --i)
            {
                var redundancy = _sceneRedundancyCache[i];
                var checkAsset = redundancy.dependency;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(checkAsset, out string guid, out long localId))
                {
                    _sceneRedundancyCache.RemoveAt(i);
                    continue;
                }

                var anyMatch = false;

                foreach (var assetRef in instance.Assets)
                {
                    if (string.IsNullOrEmpty(assetRef.GUID))
                    {
                        continue;
                    }

                    if (assetRef.GUID.Equals(guid, System.StringComparison.Ordinal))
                    {
                        anyMatch = true;
                        break;
                    }
                }

                if (!anyMatch)
                {
                    _sceneRedundancyCache.RemoveAt(i);
                }
            }

            Debug.Log($"Found {_sceneRedundancyCache.Count} redundancies.");

            EditorUtility.ClearProgressBar();
        }

        private void UpdateAssetBundleList(AssetBundleData instance, bool forceRefresh = false)
        {
            if (_previouslySelectedInstance != instance)
            {
                _previouslySelectedInstance = instance;
                _thisAssetBundleIndex = -1;
            }

            if (_assetBundlesList != null && _thisAssetBundleIndex != -1 && !forceRefresh)
            {
                return;
            }

            _assetBundleListCache.Clear();
            AssetDatabaseE.LoadAssetsOfType(_assetBundleListCache);

            _assetBundlesList = new string[_assetBundleListCache.Count];
            for (var i = 0; i < _assetBundleListCache.Count; ++i)
            {
                _assetBundlesList[i] = _assetBundleListCache[i].name;

                if (_assetBundleListCache[i] == instance)
                {
                    _thisAssetBundleIndex = i;
                }
            }

            foreach (var bundleData in _assetBundleListCache)
            {
                bundleData.RefreshLookupTable();
            }
        }

        private void UpdateImplicitDependencies(AssetBundleData instance, int maxDepth)
        {
            _implicitDependencyCache.Clear();

            EditorUtility.DisplayProgressBar("Scanning dependencies..", "Starting", 0f);

            for (var i = 0; i < instance.Assets.Count; ++i)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Scanning dependencies..", $"{i}/{instance.Assets.Count}", (float)i / instance.Assets.Count))
                {
                    break;
                }

                var assetRef = instance.Assets[i];
                var guid = assetRef.GUID;

                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

                if (asset == null)
                {
                    continue;
                }

                RecursiveDependencyScan(_implicitDependencyCache, asset, asset, 0, maxDepth);
            }

            EditorUtility.DisplayProgressBar("Scanning dependencies..", "removing duplicates", 1f);

            foreach (var assetRef in instance.Assets)
            {
                var guid = assetRef.GUID;

                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

                if (asset == null)
                {
                    continue;
                }

                // don't count as dependency 
                _implicitDependencyCache.RemoveAll(dependency => dependency.dependency == asset);
            }

            EditorUtility.ClearProgressBar();

            RemoveAssetsInAnyBundleFromDependencyList();
        }

        private class AssetDependency
        {
            public List<Object> owners = new List<Object>();
            public Object dependency;
        }

        private readonly System.Type[] _importantAssetDependencyTypes = new System.Type[]
        {
        typeof(AudioClip),
        typeof(Texture),
        typeof(Shader),
        typeof(Mesh),
        typeof(GameObject),
        };

        private void RecursiveDependencyScan(List<AssetDependency> list, Object topLevelAsset, Object asset, int stackDepthCounter, int maxDepth)
        {
            stackDepthCounter++;
            if (stackDepthCounter > maxDepth)
            {
                return;
            }

            if (asset == null)
            {
                return;
            }

            var serializedAsset = new SerializedObject(asset);
            var serializedProperty = serializedAsset.GetIterator();

            while (serializedProperty.Next(true))
            {
                if (serializedProperty.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var unityObject = serializedProperty.objectReferenceValue;
                    if (unityObject == null)
                    {
                        continue;
                    }

                    if (!typeof(Component).IsInstanceOfType(unityObject))
                    {
                        var unnamedObject = string.IsNullOrEmpty(unityObject.name);

                        var isTypeWeCareAbout = false;
                        foreach (var typeQuery in _importantAssetDependencyTypes)
                        {
                            if (typeQuery == unityObject.GetType())
                            {
                                isTypeWeCareAbout = true;
                                break;
                            }

                            if (typeQuery.IsInstanceOfType(unityObject))
                            {
                                isTypeWeCareAbout = true;
                                break;
                            }
                        }

                        if (isTypeWeCareAbout && !unnamedObject && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unityObject, out string guid, out long localid))
                        {
                            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                unityObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

                                if (unityObject != null)
                                {
                                    // LogService.Log(unityObject.GetType());

                                    var index = list.FindIndex(other =>
                                    {
                                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(other.dependency, out string otherGuid, out long otherLocalFileId))
                                        {
                                            return guid.Equals(otherGuid, System.StringComparison.Ordinal);
                                        }

                                        return false;
                                    });
                                    if (index == -1)
                                    {
                                        list.Add(new AssetDependency()
                                        {
                                            dependency = unityObject,
                                            owners = new List<Object>() { topLevelAsset }
                                        });
                                    }
                                    else
                                    {
                                        var dependencyPair = list[index];
                                        if (!dependencyPair.owners.Contains(topLevelAsset))
                                        {
                                            dependencyPair.owners.Add(topLevelAsset);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // keep goin.. 
                    RecursiveDependencyScan(list, topLevelAsset, unityObject, stackDepthCounter, maxDepth);
                }
            }
        }

        private void UpdateDependenciesUsingUnityQuery(AssetBundleData instance)
        {
            UpdateAssetBundleList(instance, true);

            _implicitDependencyCache.Clear();

            for (var i = 0; i < instance.Assets.Count; ++i)
            {
                if (EditorUtility.DisplayCancelableProgressBar("searching", $"{i} / {instance.Assets.Count}", (float)i / instance.Assets.Count))
                {
                    break;
                }

                var assetRef = instance.Assets[i];

                var assetPath = AssetDatabase.GUIDToAssetPath(assetRef.GUID);
                var dependencies = AssetDatabase.GetDependencies(assetPath);

                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                foreach (var dependencyPath in dependencies)
                {

                    var dependencyAsset = AssetDatabase.LoadAssetAtPath<Object>(dependencyPath);
                    var dependencyGuid = AssetDatabase.AssetPathToGUID(dependencyPath);

                    var index = _implicitDependencyCache.FindIndex(other =>
                    {
                        if (other == null || other.dependency == null) return false;

                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(other.dependency, out string otherGuid, out long otherLocalFileId))
                        {
                            return dependencyGuid.Equals(otherGuid, System.StringComparison.Ordinal);
                        }

                        return false;
                    });

                    if (index == -1)
                    {
                        _implicitDependencyCache.Add(new AssetDependency()
                        {
                            dependency = dependencyAsset,
                            owners = new List<Object>() { asset }
                        });
                    }
                    else
                    {
                        var dependencyPair = _implicitDependencyCache[index];
                        if (!dependencyPair.owners.Contains(asset))
                        {
                            dependencyPair.owners.Add(asset);
                        }
                    }
                }

            }

            EditorUtility.ClearProgressBar();

            RemoveAssetsInAnyBundleFromDependencyList();
        }

        private void RemoveAssetsInAnyBundleFromDependencyList()
        {
            for (var i = _implicitDependencyCache.Count - 1; i >= 0; --i)
            {
                if (EditorUtility.DisplayCancelableProgressBar("scanning for redundancies", $"{i} / {_implicitDependencyCache.Count}", (float)i / _implicitDependencyCache.Count))
                {
                    break;
                }

                var dependencyData = _implicitDependencyCache[i];
                var dependencyAsset = dependencyData.dependency;

                if (dependencyAsset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(dependencyAsset, out string guid, out long localId))
                {
                    foreach (var bundleData in _assetBundleListCache)
                    {
                        if (bundleData.ContainsAssetRef(guid))
                        {
                            _implicitDependencyCache.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            EditorUtility.ClearProgressBar();
        }

        private bool _showDependencyOwners;
        private bool _onlyShowPrefabs;

        private Tab _tabIndex;

        private static readonly string[] _tabs = new string[]
        {
        "Browse",
        "Dependencies",
        "Scene Redundancies",
        "Ghost Data",
        };


        private string _dependencyQuery;

        private enum Tab
        {
            Browse              = 0,
            Dependencies        = 1,
            SceneRedundancies   = 2,
            GhostData           = 3,
        }

        private void DrawTabView()
        {
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < _tabs.Length; ++i)
            {
                var tabLabel = _tabs[i];

                if(DrawTabButton(tabLabel, i == (int) _tabIndex))
                {
                    _tabIndex = (Tab) i;
                    _dependencySetBundleIndex = _thisAssetBundleIndex;
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(-14f); // shift the next thing up 
        }

        private readonly Color _selectedColor = new Color(0.5f, 0.9f, 1, 1);

        private bool DrawTabButton(string displayLabel, bool selected)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = selected ? _selectedColor : previousColor;

            GUILayout.BeginVertical(GUILayout.Height(24f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(displayLabel, GUILayout.Height(selected ? 24f : 20f)))
            {
                return true; 
            }

            GUILayout.EndVertical();
            GUI.backgroundColor = previousColor;

            return false; 
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();

            var instance = (AssetBundleData)target;
            var hasMultipleSelected = targets.Length > 1;

            UpdateAssetBundleList(instance);

            // draw header group 
            EditorGUILayout.BeginVertical("GroupBox");
            {
                if (hasMultipleSelected)
                {
                    EditorGUILayout.LabelField($"{targets.Length} AssetBundleDatas selected.");
                }
                else
                {
                    EditorGUILayout.LabelField($"{instance.name}.bundle", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{instance.Assets.Count} assets in bundle.");
                }
            }
            EditorGUILayout.EndVertical();

            // draw tab buttons 
            if (hasMultipleSelected)
            {
                _tabIndex = 0;
            }
            else
            {
                DrawTabView();
            }

            // draw current tab content 
            switch(_tabIndex)
            {
                case Tab.Browse:
                    DrawTab_Browse(instance, hasMultipleSelected);
                    break;
                case Tab.Dependencies:
                    DrawTab_Dependencies(instance); 
                    break;
                case Tab.SceneRedundancies:
                    DrawTab_SceneRedundancies(instance); 
                    break;
                case Tab.GhostData:
                    DrawTab_GhostData(instance); 
                    break;
            }

            if(GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(instance); 
            }
        }

        private void DrawTab_Browse(AssetBundleData instance, bool hasMultipleSelected)
        {
            // browser 
            EditorGUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.LabelField("AssetBundleData Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(EnabledInBuild);
                    EditorGUILayout.PropertyField(NoDependencies);
                    EditorGUILayout.PropertyField(LoadBundleOnInitialize);
                    EditorGUILayout.PropertyField(PackSeparately);
                    EditorGUILayout.PropertyField(SceneBundle);
                    EditorGUILayout.PropertyField(HideInLists);
                    EditorGUILayout.PropertyField(ForceLoadInEditor);
                    EditorGUILayout.PropertyField(DoNotBuildForDedicatedServer);
                }
                EditorGUILayout.EndVertical();

                if (hasMultipleSelected)
                {
                    // stop drawing 
                }
                else
                {
                    // page controls and data 
                    EditorGUILayout.BeginVertical("GroupBox");
                    {
                        var assetsProperty = serializedObject.FindProperty("Assets");

                        EditorGUILayout.LabelField("AssetBundleData Data", EditorStyles.boldLabel);

                        // draw search query 
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField("search", GUILayout.Width(128f)); 
                            var newSearchQuery = EditorGUILayout.TextField(_searchQuery);
                            if (newSearchQuery != _searchQuery)
                            {
                                _searchQuery = newSearchQuery;
                                _searchResults.Clear();

                                var searchInvariant = _searchQuery.ToLowerInvariant();

                                if (!string.IsNullOrEmpty(_searchQuery))
                                {
                                    var count = assetsProperty.arraySize;
                                    for (var i = 0; i < count; ++i)
                                    {
                                        var assetData = instance.Assets[i];

                                        if (!assetData.AssetBundleReference.ToLowerInvariant().Contains(searchInvariant))
                                        {
                                            continue;
                                        }

                                        _searchResults.Add(i);
                                    }
                                }
                            }

                            if (GUILayout.Button("x", GUILayout.Width(32f)))
                            {
                                _searchQuery = string.Empty;
                                _searchResults.Clear();

                                // force clear that text box 
                                Repaint();
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        // divider 
                        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                        // draw page controls 
                        EditorGUILayout.BeginHorizontal();
                        {
                            var pageCount = assetsProperty.arraySize / _assetsPerPage;
                            var queryPagecount = _searchResults.Count / _assetsPerPage;

                            if (GUILayout.Button("<<"))
                            {
                                _assetPageIndex = 0;
                                _queryPageIndex = 0;
                            }

                            if (GUILayout.Button("<"))
                            {
                                if (_assetPageIndex > 0)
                                {
                                    _assetPageIndex--;
                                }

                                if (_queryPageIndex > 0)
                                {
                                    _queryPageIndex--;
                                }
                            }

                            GUILayout.FlexibleSpace();

                            if (string.IsNullOrEmpty(_searchQuery))
                            {
                                EditorGUILayout.LabelField($"{_assetPageIndex + 1} / {pageCount + 1}");
                            }
                            else
                            {
                                EditorGUILayout.LabelField($"{_queryPageIndex + 1} / {queryPagecount + 1}");
                            }

                            if (GUILayout.Button(">"))
                            {
                                if (_assetPageIndex < pageCount)
                                {
                                    _assetPageIndex++;
                                }

                                if (_queryPageIndex < queryPagecount)
                                {
                                    _queryPageIndex++;
                                }
                            }

                            if (GUILayout.Button(">>"))
                            {
                                _assetPageIndex = Mathf.Max(0, pageCount);
                                _queryPageIndex = Mathf.Max(0, queryPagecount);
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        // divider 
                        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                        // no search = paginated list of all assets in bundle 
                        if (string.IsNullOrEmpty(_searchQuery))
                        {
                            var startAtIndex = _assetPageIndex * _assetsPerPage;
                            for (var i = startAtIndex; i < startAtIndex + _assetsPerPage && i < assetsProperty.arraySize; ++i)
                            {
                                if (DrawAsset(instance, assetsProperty, i))
                                {
                                    break;
                                }
                            }

                            if (assetsProperty.arraySize == 0)
                            {
                                EditorGUILayout.HelpBox("There are no assets in this bundle", MessageType.Info);
                            }
                        }

                        // search results 
                        else
                        {
                            var startAtIndex = _queryPageIndex * _assetsPerPage;
                            for (var i = startAtIndex; i < startAtIndex + _assetsPerPage && i < _searchResults.Count; ++i)
                            {
                                var assetIndex = _searchResults[i];
                                if (DrawAsset(instance, assetsProperty, assetIndex))
                                {
                                    break;
                                }
                            }

                            if (_searchResults.Count == 0)
                            {
                                EditorGUILayout.HelpBox("There are no results for your query.", MessageType.Info);
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            EditorGUILayout.EndVertical();

            if (!hasMultipleSelected)
            {
                DrawTabSubgroup_Tools(instance); 
            }
        }

        private void DrawTabSubgroup_Tools(AssetBundleData instance)
        {
            // tools 
            EditorGUILayout.BeginVertical("GroupBox");
            {
                // extra controls related to adding things to this bundle 
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16f); 
                _editorFoldout_browse_tools = EditorGUILayout.Foldout(_editorFoldout_browse_tools, "Debugging Tools", true);
                EditorGUILayout.EndHorizontal();

                if(_editorFoldout_browse_tools)
                {
                    // reference / asset fixing tools 
                    EditorGUILayout.LabelField("Reference Tools");
                    EditorGUILayout.BeginHorizontal();
                    {
                        // the old mega search (fixes dangling references) 
                        if (GUILayout.Button("add dangling references"))
                        {
                            AssetReferenceE.FindDanglingReferencesInsertIntoBundleData(instance);
                        }

                        // updates stuff for builds, and allows queries 
                        if (GUILayout.Button("update internal references"))
                        {
                            AssetBundleService.EditorUpdateBundleReferencesForBuilds();
                        }

                        // if one asset is somehow in two bundles, remove it from the other bundles (stay in selected bundle) 
                        if (GUILayout.Button("remove our assets from other bundles"))
                        {
                            instance.EditorRemoveOurAssetsFromOtherBundles();
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Print NULL References"))
                        {
                            foreach (var assetData in instance.Assets)
                            {
                                var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                                if (string.IsNullOrEmpty(assetPath))
                                {
                                    Debug.LogError($"Found NULL! [{assetData.GUID}] (last known reference: {assetData.AssetBundleReference})");
                                }
                            }
                        }

                        if (GUILayout.Button("DELETE NULL References") && EditorUtility.DisplayDialog("delete nulls?", "are you sure????", "yes i am kinda sure", "no no no no no"))
                        {
                            instance.EditorRemoveNullAssetReferences();
                        }

                        if (GUILayout.Button("Delete duplicate refs"))
                        {
                            instance.EditorRemoveDuplicateReferences();
                        }
                    }

                    EditorGUILayout.EndHorizontal();


                    EditorGUILayout.LabelField("Misc Tools");
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Debug Build Bundle"))
                        {
                            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
                            AssetBundleExporter.BuildBundlesForTarget(buildTarget, forceSingleBundleBuildDebug: instance);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTab_Dependencies(AssetBundleData instance)
        {
            EditorGUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.HelpBox("This tool can help you find missing AssetBundleReference dependencies. " +
                    "For example, maybe one developer committed a prefab which has a reference to an asset which is expected to be in an asset bundle, but forgot to commit their .bundle file! " +
                    "This tool will help you find any assets which may be 'dangling' in this way, so you can add them to your bundles. ", MessageType.Info);

                // data query tools 
                EditorGUILayout.BeginHorizontal("GroupBox");
                {
                    EditorGUILayout.LabelField("Tools: ");

                    if (GUILayout.Button("unity search"))
                    {
                        UpdateDependenciesUsingUnityQuery(instance);
                    }

                    if (GUILayout.Button("refresh"))
                    {
                        UpdateImplicitDependencies(instance, 1);
                    }

                    if (GUILayout.Button("deep search"))
                    {
                        UpdateImplicitDependencies(instance, 3);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    var newIndex = EditorGUILayout.Popup(_dependencySetBundleIndex, _assetBundlesList, GUILayout.Width(128f));
                    if (newIndex != _dependencySetBundleIndex)
                    {
                        _dependencySetBundleIndex = newIndex;
                    }

                    EditorGUILayout.LabelField($"Dependencies ({_implicitDependencyCache.Count})", GUILayout.Width(128f));
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginVertical();
                _showDependencyOwners = EditorGUILayout.Toggle("Show Sources", _showDependencyOwners, GUILayout.Width(128f));
                _onlyShowPrefabs = EditorGUILayout.Toggle("Only Show Prefabs", _onlyShowPrefabs, GUILayout.Width(128f));
                _dependencyQuery = EditorGUILayout.TextField("Search: ", _dependencyQuery);
                EditorGUILayout.EndVertical();

                var dependencyQuery = _dependencyQuery != null ? _dependencyQuery.ToLowerInvariant() : string.Empty;

                // foreach (var dependency in _implicitDependencyCache)
                for (var d = 0; d < _implicitDependencyCache.Count; ++d)
                {
                    var dependency = _implicitDependencyCache[d];

                    if (_onlyShowPrefabs && dependency.dependency as GameObject == null)
                    {
                        continue;
                    }

                    if (dependency == null || dependency.dependency == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(_dependencyQuery) && !dependency.dependency.name.ToLowerInvariant().Contains(dependencyQuery))
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("add to bundle", GUILayout.Width(98f)))
                    {
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(dependency.dependency, out string guid, out long localid))
                        {
                            var addToBundle = _assetBundleListCache[_dependencySetBundleIndex];
                            addToBundle.EditorAddAssetReference(guid);
                            _implicitDependencyCache.RemoveAt(d);
                        }

                        break;
                    }

                    EditorGUI.BeginDisabledGroup(true);

                    EditorGUILayout.ObjectField(dependency.dependency, typeof(Object), false);

                    if (_showDependencyOwners)
                    {
                        EditorGUILayout.BeginVertical();

                        foreach (var owner in dependency.owners)
                        {
                            EditorGUILayout.ObjectField(owner, typeof(Object), false);
                        }

                        EditorGUILayout.EndVertical();
                    }

                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.EndHorizontal();
                }

            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTab_SceneRedundancies(AssetBundleData instance)
        {
            EditorGUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.HelpBox("This tool can help you find assets which are both in the current scene and in any of your bundles. " +
                    "If you have assets directly referenced in your scene and reference in asset bundles, it is highly likely that you will end up " +
                    "duplicating these assets in memory, at runtime. ", MessageType.Info);

                // scene query tools 
                EditorGUILayout.BeginHorizontal("GroupBox");
                {
                    EditorGUILayout.LabelField("Tools: ");

                    if (GUILayout.Button("refresh"))
                    {
                        RefreshSceneRedundancies(instance, 1);
                    }

                    if (GUILayout.Button("deep search"))
                    {
                        RefreshSceneRedundancies(instance, 3);
                    }
                }
                EditorGUILayout.EndHorizontal();

                // display scene data 
                EditorGUILayout.BeginVertical("GroupBox");
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Redundancies");
                        _showDependencyOwners = EditorGUILayout.Toggle("Show Sources", _showDependencyOwners);
                    }
                    EditorGUILayout.EndHorizontal();

                    for (var i = 0; i < _sceneRedundancyCache.Count; ++i)
                    {
                        var redundancy = _sceneRedundancyCache[i];

                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(redundancy.dependency, typeof(Object), true);

                        if (_showDependencyOwners)
                        {
                            EditorGUILayout.BeginVertical();
                            foreach (var owner in redundancy.owners)
                            {
                                EditorGUILayout.ObjectField(owner, typeof(Object), true);
                            }
                            EditorGUILayout.EndVertical();
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTab_GhostData(AssetBundleData instance)
        {
            EditorGUILayout.BeginVertical("GroupBox");
            {
                EditorGUILayout.HelpBox("This tool is experimental, it is used to find \"ghost data\" inside of your project's prefabs. " +
                    "By ghost data, I mean data which is serialized in your prefabs but does not actually contribute to the project at all, simply bloat. " +
                    "Sometimes you'll find yourself in a situation where clearing ghost data can save you a large amount of memory on disk, speeding up Unity and " +
                    "sometimes even improving load times at runtime.", MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                _propertyQuery = EditorGUILayout.TextField(_propertyQuery);
                _queryGhostArrayData = EditorGUILayout.Toggle("only out of bounds arrays", _queryGhostArrayData);

                if (GUILayout.Button("Search"))
                {
                    _foundProperties.Clear();
                    var searchQuery = string.IsNullOrEmpty(_propertyQuery) ? string.Empty : _propertyQuery.ToLowerInvariant();

                    for (var i = 0; i < instance.Assets.Count; ++i)
                    {
                        var assetRef = instance.Assets[i];
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetRef.GUID);
                        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (prefabAsset == null) continue;

                        var components = prefabAsset.GetComponentsInChildren<Component>(true);
                        for (var c = 0; c < components.Length; ++c)
                        {
                            var component = components[c];
                            var prefabModifications = PrefabUtility.GetPropertyModifications(component);
                            if (prefabModifications == null) continue;

                            for (var p = 0; p < prefabModifications.Length; ++p)
                            {
                                var prefabModification = prefabModifications[p];
                                var propertyPath = prefabModification.propertyPath;

                                var append = false;

                                if (propertyPath != null && propertyPath.ToLowerInvariant().Contains(searchQuery))
                                {
                                    append = true;
                                }

                                if (_queryGhostArrayData)
                                {
                                    if (!PropertyIsOutOfBoundsArrayElement(prefabModifications, p))
                                    {
                                        append = false;
                                    }
                                }

                                if (append)
                                {
                                    _foundProperties.Add(new PropertyModificationData()
                                    {
                                        prefab = prefabAsset,
                                        component = component,
                                        propertyPath = propertyPath,
                                    });
                                }
                            }

                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField($"{_foundProperties.Count} properties. {_selectedPropertyIndices.Count} selected.");

                    if (GUILayout.Button("select all"))
                    {
                        _selectedPropertyIndices.Clear();

                        for (var i = 0; i < _foundProperties.Count; ++i)
                        {
                            _selectedPropertyIndices.Add(i);
                        }

                        Repaint();
                    }

                    if (GUILayout.Button("select none"))
                    {
                        _selectedPropertyIndices.Clear();
                        Repaint();
                    }

                    if (GUILayout.Button("clean selected", GUILayout.Width(128f)))
                    {
                        _selectedPropertyIndices.Sort();

                        for (var i = _selectedPropertyIndices.Count - 1; i >= 0; --i)
                        {
                            var propertyIndex = _selectedPropertyIndices[i];

                            var data = _foundProperties[propertyIndex];

                            var prefabModifications = PrefabUtility.GetPropertyModifications(data.component);
                            for (var p = prefabModifications.Length - 1; p >= 0; --p)
                            {
                                var prefabModification = prefabModifications[p];
                                if (prefabModification.propertyPath == data.propertyPath)
                                {
                                    // lol 
                                    var list = prefabModifications.ToList();
                                    list.RemoveAt(p);

                                    PrefabUtility.SetPropertyModifications(data.component, list.ToArray());
                                    EditorUtility.SetDirty(data.component);
                                }
                            }

                            _foundProperties.RemoveAt(propertyIndex);
                        }

                        _selectedPropertyIndices.Clear();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _leftMosueDrag = true;
                }

                if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
                {
                    _leftMosueDrag = false;
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                {
                    _rightMouseDrag = true;
                }

                if (Event.current.type == EventType.MouseUp && Event.current.button == 1)
                {
                    _rightMouseDrag = false;
                }

                var modified = false;
                var normalColor = GUI.backgroundColor;
                var selectedColor = Color.red;

                for (var p = 0; p < _foundProperties.Count; ++p)
                {
                    var propertyPath = _foundProperties[p];

                    GUI.backgroundColor = _selectedPropertyIndices.Contains(p) ? selectedColor : normalColor;
                    EditorGUI.BeginDisabledGroup(true);
                    var rect = EditorGUILayout.BeginHorizontal();
                    {
                        if (_selectedPropertyIndices.Contains(p))
                        {
                            EditorGUILayout.LabelField("*", GUILayout.Width(32f));
                        }

                        EditorGUILayout.LabelField(propertyPath.prefab.name, GUILayout.Width(256f));
                        EditorGUILayout.TextField(propertyPath.propertyPath);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndDisabledGroup();

                    if (rect.Contains(Event.current.mousePosition))
                    {
                        if (_leftMosueDrag)
                        {
                            if (!_selectedPropertyIndices.Contains(p))
                            {
                                _selectedPropertyIndices.Add(p);
                                modified = true;
                            }
                        }

                        if (_rightMouseDrag)
                        {
                            if (_selectedPropertyIndices.Contains(p))
                            {
                                _selectedPropertyIndices.Remove(p);
                                modified = true;
                            }
                        }
                    }
                }

                // reset 
                GUI.backgroundColor = normalColor;

                if (modified)
                {
                    Repaint();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private bool PropertyIsOutOfBoundsArrayElement(PropertyModification[] modifications, int index)
        {
            var modification = modifications[index];

            var propertyArrayElementIndex = TryParseArrayElementIndex(modification.propertyPath);
            if (propertyArrayElementIndex == -1)
            {
                return false;
            }

            var arraySizeIndex = TryFindArraySizeIndex(modifications, modification.propertyPath);
            if (arraySizeIndex == -1)
            {
                return false;
            }

            var sizeModification = modifications[arraySizeIndex];
            var valueIntStr = sizeModification.value;
            if (!int.TryParse(valueIntStr, out int sizeValue))
            {
                return false;
            }

            return sizeValue < propertyArrayElementIndex;
        }

        private int TryFindArraySizeIndex(PropertyModification[] modifications, string propertyPath)
        {
            var firstPropertySplitIndex = propertyPath.IndexOf('.');
            var propertNameRoot = propertyPath.Substring(0, firstPropertySplitIndex);

            for (var i = 0; i < modifications.Length; i++)
            {
                var modification = modifications[i];
                if (modification.propertyPath.Contains(propertNameRoot) && modification.propertyPath.Contains("size"))
                {
                    return i;
                }
            }

            return -1;
        }

        private int TryParseArrayElementIndex(string propertyPath)
        {
            var indexOfLeftBracket = propertyPath.IndexOf('[');
            var indexOfRightBracket = propertyPath.IndexOf(']');

            if (indexOfLeftBracket == -1 || indexOfRightBracket == -1)
            {
                return -1;
            }

            var intStr = propertyPath.Substring(indexOfLeftBracket + 1, indexOfRightBracket - indexOfLeftBracket - 1);
            if (!int.TryParse(intStr, out int arrayElementIndex))
            {
                return -1;
            }

            return arrayElementIndex;
        }

        private string _propertyQuery;
        private bool _leftMosueDrag;
        private bool _rightMouseDrag;
        private bool _queryGhostArrayData;

        private List<PropertyModificationData> _foundProperties = new List<PropertyModificationData>();
        private List<int> _selectedPropertyIndices = new List<int>();

        private struct PropertyModificationData
        {
            public GameObject prefab;
            public Component component;
            public string propertyPath;
        }

        private bool DrawAsset(AssetBundleData instance, SerializedProperty assetsProperty, int i)
        {
            var assetProperty = assetsProperty.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PropertyField(assetProperty);

                var newIndex = EditorGUILayout.Popup(_thisAssetBundleIndex, _assetBundlesList, GUILayout.Width(128f));
                if (newIndex != _thisAssetBundleIndex)
                {

                    // add to the other list 
                    var newAssetBundleData = _assetBundleListCache[newIndex];
                    Undo.RecordObject(newAssetBundleData, "adding");
                    newAssetBundleData.Assets.Add(instance.Assets[i]);
                    EditorUtility.SetDirty(newAssetBundleData);

                    // remove from this list 
                    assetsProperty.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    return true;
                }

                if (GUILayout.Button("x", GUILayout.Width(64f)))
                {
                    if (EditorUtility.DisplayDialog("Remove from all AssetBundles", "are you sure?", "yes", "no", DialogOptOutDecisionType.ForThisSession, "assetbundledataoptoutassetbundledelete"))
                    {
                        assetsProperty.DeleteArrayElementAtIndex(i);
                        EditorGUILayout.EndHorizontal();
                        return true;
                    }
                }

            }
            EditorGUILayout.EndHorizontal();

            return false;
        }
    }
}
#endif