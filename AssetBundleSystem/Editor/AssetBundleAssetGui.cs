namespace FunkAssetBundles
{
#if UNITY_EDITOR
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;
    using UnityEditorInternal;

    [InitializeOnLoad]
    internal static class AssetBundleAssetGui
    {
        static AssetBundleAssetGui()
        {
            Editor.finishedDefaultHeaderGUI += OnPostHeaderGui;
        }

        public static string[] _assetBundleDropdownOptions;
        public static List<AssetBundleData> _assetBundleCache = new List<AssetBundleData>();
        public static List<AssetBundleData> _allAssetBundles = new List<AssetBundleData>();

        private static Object _previouslySelected = null;
        private static int _assetBundleIndex;
        private static string _cachedGuid;
        private static bool _inNonVisibleBundle;

        [InitializeOnLoadMethod]
        private static void Reinitialize()
        {
            _assetBundleDropdownOptions = null;
            _assetBundleCache = new List<AssetBundleData>();
            _allAssetBundles = new List<AssetBundleData>();

            _previouslySelected = null;
            _assetBundleIndex = 0;
            _cachedGuid = null; 
            _inNonVisibleBundle = false;
        }

        private static void TryRefreshBundleGuiCache(Editor editor, bool force = false)
        {
            var currentTarget = editor.target;
            if (currentTarget == _previouslySelected && string.IsNullOrEmpty(_cachedGuid) && !force)
            {
                return;
            }

            _previouslySelected = currentTarget;
            _assetBundleIndex = -1;

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_previouslySelected, out _cachedGuid, out long localId);
            RefreshBundleOptions(forceRefresh: force);
            RefreshSelectedBundle(); 
        }

        private static void OnPostHeaderGui(Editor editor)
        {
            if (!EditorUtility.IsPersistent(editor.target))
            {
                _previouslySelected = null;
                return;
            }

            // only do anything if we're selecting exactly 1 asset 
            // if(editor.targets == null || editor.targets.Length != 1)
            // {
            //     return;
            // }

            var isMultiTarget = editor.targets != null && editor.targets.Length > 1;

            var targetObject = editor.target;
            var targetType = targetObject.GetType();

            // ignore folders or other objects that do not make sense to put in bundles 
            if (targetType == typeof(DefaultAsset)
                || targetType == typeof(Animation)
                || targetType == typeof(AnimationClip)
                || targetType == typeof(AssetBundleData)
                || targetType == typeof(MonoImporter)
                || targetType == typeof(AssemblyDefinitionImporter)
                || targetType == typeof(PluginImporter))
            {
                _previouslySelected = null;
                return;
            }

            var targetIsSceneAsset = targetType == typeof(SceneAsset); 

            // ignore instances of gameobjects / prefabs,  
            var targetGameobject = targetObject as GameObject;
            if (targetGameobject != null)
            {
                var prefabAssetType = PrefabUtility.GetPrefabAssetType(targetGameobject);
                if (prefabAssetType == PrefabAssetType.NotAPrefab || targetGameobject.scene != null)
                {
                    _previouslySelected = null;
                    return;
                }
            }

            TryRefreshBundleGuiCache(editor);

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();

                EditorGUILayout.BeginHorizontal("GroupBox");
                {
                    // EditorGUILayout.LabelField($"selected: {targetObject.GetType().Name}"); 

                    if(GUILayout.Button(EditorGUIUtility.IconContent("d_Refresh"), GUILayout.Width(32f)))
                    {
                        TryRefreshBundleGuiCache(editor, force: true); 
                    }

                    EditorGUILayout.LabelField("[bundle]", GUILayout.Width(98f));


                    if (_inNonVisibleBundle)
                    {
                        EditorGUILayout.LabelField("[in hidden bundle]", GUILayout.Width(128f));
                    }
                    else
                    {
                        var newBundleIndex = EditorGUILayout.Popup(_assetBundleIndex, _assetBundleDropdownOptions, GUILayout.Width(128f));
                        if (newBundleIndex != _assetBundleIndex)
                        {
                            // remove from bundles
                            if (newBundleIndex == 0)
                            {
                                var previousBundle = _assetBundleCache[_assetBundleIndex - 1];
                                Undo.RecordObject(previousBundle, "removed asset");
                                previousBundle.EditorRemoveAssetReference(_cachedGuid);

                                if (isMultiTarget)
                                {
                                    foreach (var target in editor.targets)
                                    {
                                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string targetGuid, out long targetFileId);
                                        previousBundle.EditorRemoveAssetReference(targetGuid);
                                    }
                                }

                                EditorUtility.SetDirty(previousBundle);

                                RecacheBundleLookupTables();
                            }

                            // add to bundle 
                            else if (_assetBundleIndex == 0)
                            {
                                var newBundle = _assetBundleCache[newBundleIndex - 1];

                                if (newBundle.SceneBundle && !targetIsSceneAsset)
                                {
                                    Debug.LogError($"Tried to add non SceneAsset to a SceneBundle.", newBundle);
                                    EditorUtility.DisplayDialog("STOP.", "Tried to add non SceneAsset to a SceneBundle. This is not allowed.", "oh........");
                                    return;
                                }

                                else if (!newBundle.SceneBundle && targetIsSceneAsset)
                                {
                                    Debug.LogError($"Tried to add SceneAsset to a non SceneBundle.", newBundle);
                                    EditorUtility.DisplayDialog("STOP.", "Tried to add SceneAsset to a non SceneBundle. This is not allowed.", "oh........");
                                    return;
                                }


                                Undo.RecordObject(newBundle, "added asset");
                                newBundle.EditorAddAssetReference(_cachedGuid);

                                if (isMultiTarget)
                                {
                                    foreach (var target in editor.targets)
                                    {
                                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string targetGuid, out long targetFileId);
                                        newBundle.EditorAddAssetReference(targetGuid);
                                    }
                                }

                                EditorUtility.SetDirty(newBundle);

                                RecacheBundleLookupTables();
                            }

                            // move from one bundle to another 
                            else
                            {
                                var previousBundle = _assetBundleCache[_assetBundleIndex - 1];
                                var newBundle = _assetBundleCache[newBundleIndex - 1];

                                if (newBundle.SceneBundle && !targetIsSceneAsset)
                                {
                                    Debug.LogError($"Tried to add non SceneAsset to a SceneBundle.", newBundle);
                                    EditorUtility.DisplayDialog("STOP.", "Tried to add non SceneAsset to a SceneBundle. This is not allowed.", "oh........");
                                    return;
                                }

                                else if (!newBundle.SceneBundle && targetIsSceneAsset)
                                {
                                    Debug.LogError($"Tried to add SceneAsset to a non SceneBundle.", newBundle);
                                    EditorUtility.DisplayDialog("STOP.", "Tried to add SceneAsset to a non SceneBundle. This is not allowed.", "oh........");
                                    return;
                                }

                                Undo.RecordObjects(new Object[] { previousBundle, newBundle }, "moved asset");

                                previousBundle.EditorRemoveAssetReference(_cachedGuid);
                                newBundle.EditorAddAssetReference(_cachedGuid);

                                if (isMultiTarget)
                                {
                                    foreach (var target in editor.targets)
                                    {
                                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string targetGuid, out long targetFileId);
                                        previousBundle.EditorRemoveAssetReference(targetGuid);
                                        newBundle.EditorAddAssetReference(targetGuid);
                                    }

                                }

                                EditorUtility.SetDirty(previousBundle);
                                EditorUtility.SetDirty(newBundle);

                                RecacheBundleLookupTables();
                            }

                            // update stored index 
                            _assetBundleIndex = newBundleIndex;
                        }

                        EditorGUI.BeginDisabledGroup(_assetBundleIndex == 0);
                        if (GUILayout.Button("view", GUILayout.Width(128f)))
                        {
                            var bundle = _assetBundleCache[_assetBundleIndex - 1];
                            Selection.activeObject = bundle;
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndHorizontal();
        }

        public static void RefreshBundleOptions(bool forceRefresh = false)
        {
            if(!forceRefresh && _assetBundleCache != null && _assetBundleCache.Count > 0)
            {
                return;
            }

            _assetBundleCache.Clear();
            AssetDatabaseE.LoadAssetsOfType(_assetBundleCache);
            _allAssetBundles = _assetBundleCache.Clone();

            var options = new List<string>(_assetBundleCache.Count + 1);
                options.Add("no bundle");

            for (var i = 0; i < _assetBundleCache.Count; ++i)
            {
                var bundle = _assetBundleCache[i];
                if(bundle.HideInLists)
                {
                    _assetBundleCache.RemoveAt(i);
                    --i;
                    continue;
                }

                options.Add(bundle.name);
            }

            _assetBundleDropdownOptions = options.ToArray();
            RecacheBundleLookupTables();
        }

        private static void RefreshSelectedBundle()
        {
            _assetBundleIndex = -1;
            _inNonVisibleBundle = false;

            // first, scan visible bundles 
            for (var i = 0; i < _assetBundleCache.Count; ++i)
            {
                var bundle = _assetBundleCache[i];

                if (bundle.ContainsAssetRef(_cachedGuid))
                {
                    _assetBundleIndex = i + 1;
                    return; 
                }
            }


            // then, scan all bundles
            foreach(var bundle in _allAssetBundles)
            {
                if(bundle.ContainsAssetRef(_cachedGuid))
                {
                    _inNonVisibleBundle = true;
                    return; 
                }
            }

            // in no bundle 
            _assetBundleIndex = 0;
            _inNonVisibleBundle = false; 
        }

        private static void RecacheBundleLookupTables()
        {
            // ensure lookup table works for all bundles 
            foreach (var bundle in _allAssetBundles)
            {
                bundle.RefreshLookupTable();
            }
        }
    }
#endif
}