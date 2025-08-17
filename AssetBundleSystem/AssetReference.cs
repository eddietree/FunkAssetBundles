#pragma warning disable CS0162 // Unreachable code detected

namespace FunkAssetBundles
{


    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using System.Linq; // forgive me 

#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.UIElements;

    [CustomPropertyDrawer(typeof(AssetReference<>), true)]
    public class AssetReferencePropertyDrawerNonGeneric : PropertyDrawer
    {
        // note: cannot cache data: will cause issues in lists.. 
        // [System.NonSerialized] private Object _referencedObject;
        [System.NonSerialized] private bool _replaceAsked;

        public static bool HasSubtypes(System.Type instanceType)
        {
            return instanceType == typeof(Sprite) || instanceType == typeof(Mesh);
        }

        private System.Type GetGenericType()
        {
            // fetch the generic type of the actual property 
            // this generic is used for the object selection (limits type, to avoid user error) 
            var type = fieldInfo.FieldType;

            // if we're a property of a T[], then yoink out the element type 
            if (type.IsArray)
            {
                type = type.GetElementType();
            }

            // if we're a property of a List<T>, then rip out the element type 
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var genericListElementTypes = type.GetGenericArguments();
                if (genericListElementTypes != null && genericListElementTypes.Length > 0)
                {
                    type = genericListElementTypes[0];
                }
            }

            // try to figure out what Object we're allowed to use in the object picker, based on the <T> 
            var genericType = typeof(Object);
            var typeGenericArguments = type.GetGenericArguments();
            if (typeGenericArguments != null && typeGenericArguments.Length > 0)
            {
                genericType = typeGenericArguments[0];
            }

            return genericType; 
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {

            var nameProperty = property.FindPropertyRelative("Name");
            var guidProperty = property.FindPropertyRelative("Guid");
            var localFileIdProperty = property.FindPropertyRelative("LocalFileId");
            var subAssetReferenceProperty = property.FindPropertyRelative("SubAssetReference");

            // move the property into the actual string value.. 
            // var referenceProperty = property.FindPropertyRelative("Reference");

            // using 3x the rect size, so each half can be used 
            position.height /= 3f;

            var labelPosition = position;

            var objectFieldPos = labelPosition;
            objectFieldPos.y += position.height;

            var propertyPosition = objectFieldPos;
                propertyPosition.y += position.height;

            // draw the name of this property 
            EditorGUI.LabelField(labelPosition, label);

            var multipleValues = property.hasMultipleDifferentValues;
            if (multipleValues)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.TextField(objectFieldPos, "[multiple values]");
                EditorGUI.EndDisabledGroup();
                return;
            }

            var genericType = GetGenericType();
            var genericTypeHasSubtypes = HasSubtypes(genericType);
            if (genericTypeHasSubtypes)
            {
                objectFieldPos.width /= 2f;
                propertyPosition.width /= 2f; 
            }

            Object _referencedObject = null;

            var guidString = guidProperty.stringValue;
            if (!string.IsNullOrEmpty(guidString))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guidString);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    _referencedObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                }
            }

            // check if the object is (missing) 
            if (!string.IsNullOrEmpty(guidString) && _referencedObject == null)
            {
                _referencedObject = EditorGUI.ObjectField(objectFieldPos, new GUIContent($"MISSING: {nameProperty.stringValue} [{guidString}]"), _referencedObject, genericType, false);

                if (!GUI.changed)
                {

                    return;
                }
            }

            // draw the object field 
            else
            {
                _referencedObject = EditorGUI.ObjectField(objectFieldPos, _referencedObject, genericType, false);
            }

            if (GUI.changed && _referencedObject != null)
            {
                var path = AssetDatabase.GetAssetPath(_referencedObject);

                if (path.StartsWith("Resources/"))
                {
                    Debug.LogError($"Cannot reference {_referencedObject.name}, beause it is a built-in resource.");
                }
                else
                {
                    // remember guid..
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_referencedObject, out string GUID, out long localID))
                    {
                        guidProperty.stringValue = GUID;
                        localFileIdProperty.longValue = localID;

                        if (AssetBundleService.EditorFindContainerBundle(GUID) == null)
                        {
                            AssetBundleData defaultBundle = null;

                            var attributes = fieldInfo.GetCustomAttributes(true);
                            foreach (var attribute in attributes)
                            {
                                if (attribute as AssetReferenceTargetBundleAttribute != null)
                                {
                                    var targetBundleAttribute = (AssetReferenceTargetBundleAttribute)attribute;
                                    var targetBundle = targetBundleAttribute.defaultBundleName;
                                    defaultBundle = AssetBundleService.EditorFindBundleByName(targetBundle);
                                    break;
                                }
                            }

                            AssetBundleService.EnsureReferenceInAnyBundle(GUID, defaultBundle);
                        }

                        // subtypes?
                        if(genericTypeHasSubtypes)
                        {
                            subAssetReferenceProperty.stringValue = _referencedObject.name;
                        }
                    }

                    // remember name 
                    nameProperty.stringValue = _referencedObject.name;
                }
            }

            if (genericTypeHasSubtypes && _referencedObject != null)
            {
                Object _referencedSprite = null;

                // try and find the current sprite (if defined) 
                if (_referencedSprite == null && !string.IsNullOrEmpty(subAssetReferenceProperty.stringValue))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guidString);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

                        foreach (var asset in allAssets)
                        {
                            if (asset.name == subAssetReferenceProperty.stringValue)
                            {
                                var spriteAsset = asset as Object;
                                if (spriteAsset != null)
                                {
                                    _referencedSprite = spriteAsset;
                                    break;
                                }
                            }
                        }
                    }
                }

                // draw the sprite picker 
                var spriteFieldRect = objectFieldPos;
                spriteFieldRect.x += spriteFieldRect.width;
                _referencedSprite = (Object) EditorGUI.ObjectField(spriteFieldRect, (Object)_referencedSprite, typeof(Object), false);

                // store the sprite 
                if (_referencedSprite != null)
                {
                    subAssetReferenceProperty.stringValue = _referencedSprite.name;
                }
            }

            if (_referencedObject == null)
            {
                nameProperty.stringValue = string.Empty;
                localFileIdProperty.longValue = 0;
                guidProperty.stringValue = string.Empty;
                subAssetReferenceProperty.stringValue = string.Empty;
            }

            // draw the actual property
            EditorGUI.BeginDisabledGroup(true);
            {
                EditorGUI.PropertyField(propertyPosition, guidProperty, new GUIContent(), true);
                if (genericTypeHasSubtypes)
                {
                    var subNameRect = propertyPosition;
                        subNameRect.x += propertyPosition.width;

                    EditorGUI.PropertyField(subNameRect, subAssetReferenceProperty, new GUIContent(), true);
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return base.GetPropertyHeight(property, label) + 32f;
        }
    }

    public class AssetReferenceCacheAndImporter : AssetPostprocessor
    {
        private static Dictionary<string, Object> _editorAssetCache = new Dictionary<string, Object>();
        private static Dictionary<string, List<string>> _cachedAssetPaths = new Dictionary<string, List<string>>();

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach(var assetPath in importedAssets) TryRemoveCacheEntry(assetPath);
            foreach(var assetPath in deletedAssets) TryRemoveCacheEntry(assetPath);
            foreach(var assetPath in movedAssets) TryRemoveCacheEntry(assetPath);
            foreach(var assetPath in movedFromAssetPaths) TryRemoveCacheEntry(assetPath);

            // note: don't do this anymore 
            // // we could probably be more clever here: only remove keys which were affected
            // // for now though, just clear everything when any asset is imported
            // _editorAssetCache.Clear(); 
        }

        private static void TryRemoveCacheEntry(string assetPath)
        {
            if(_cachedAssetPaths.TryGetValue(assetPath, out var assetCacheKeyList))
            {
                foreach(var assetCacheKey in assetCacheKeyList)
                {
                    _editorAssetCache.Remove(assetCacheKey);
                }

                _cachedAssetPaths.Remove(assetPath); 
            }
        }

        public static void AddToCache(string assetCacheKey, string guid, Object asset)
        {
            if (_editorAssetCache.TryGetValue(assetCacheKey, out var existingAsset))
            {
                _editorAssetCache[assetCacheKey] = asset;
            }
            else
            {
                _editorAssetCache.Add(assetCacheKey, asset);

                var assetPathRaw = AssetDatabase.GUIDToAssetPath(guid);

                if (!string.IsNullOrEmpty(assetPathRaw))
                {
                    if(_cachedAssetPaths.TryGetValue(assetPathRaw, out var assetCacheKeyList))
                    {
                        assetCacheKeyList.Add(assetCacheKey); 
                    }
                    else
                    {
                        _cachedAssetPaths.Add(assetPathRaw, new List<string>() { assetCacheKey });
                    }
                }
            }
        }

        public static void RemoveFromCache(string assetCacheKey, string guid)
        {
            AssetReferenceCacheAndImporter._editorAssetCache.Remove(assetCacheKey);

            var assetPathRaw = AssetDatabase.GUIDToAssetPath(guid);

            if (!string.IsNullOrEmpty(assetPathRaw))
            {
                if(AssetReferenceCacheAndImporter._cachedAssetPaths.TryGetValue(assetPathRaw, out var assetCacheKeyList))
                {
                    assetCacheKeyList.Remove(assetCacheKey);

                    if(assetCacheKeyList.Count == 0)
                    {
                        AssetReferenceCacheAndImporter._cachedAssetPaths.Remove(assetPathRaw);
                    }
                }
            }
        }

        public static bool TryGetFromAssetCache<T>(string assetCacheKey, out T asset) where T : Object
        {
            if(_editorAssetCache.TryGetValue(assetCacheKey, out var assetObject))
            {
                asset = (T)assetObject;
                return true;
            }

            asset = null; 
            return false; 
        }

        public static void EditorClearCache()
        {
            _editorAssetCache.Clear();
            _cachedAssetPaths.Clear();
        }
    }
#endif

    public class AssetReferenceTargetBundleAttribute : System.Attribute
    {
        public string defaultBundleName;

        public AssetReferenceTargetBundleAttribute(string defaultBundleName)
        {
            this.defaultBundleName = defaultBundleName;
        }
    }

    /// <summary>
    /// Use either this struct or its string to load objects from asset bundles via Services.assetBundles.LoadAsync(); 
    /// </summary>
    [System.Serializable]
    public struct AssetReference<T> : System.IEquatable<AssetReference<T>>
        where T : Object
    {
        // to fix serialization bug 
        // public string Reference;

        /// <summary>
        /// Just a display name. Only to be used in Editor. 
        /// </summary>
        public string Name;

        /// <summary>
        /// For referencing sub assets within an asset. One asset inside of an AssetBundle can contain many sub-assets, same as in Unity.
        /// </summary>
        public string SubAssetReference;

        /// <summary>
        /// Unity defined GUID of the asset.
        /// </summary>
        public string Guid;

        /// <summary>
        /// Unity defined FileID within the asset. 
        /// </summary>
        public long LocalFileId;

#if UNITY_EDITOR
        public static AssetReference<T> CreateFromObject(Object obj, bool ensureInBundle, AssetBundleData targetBundle = null) // todo: allow specifying bundle 
        {
            if (obj == null)
            {
                return default;
            }

            var assetReference = new AssetReference<T>()
            {
                Name = obj.name,
                SubAssetReference = string.Empty,
            };

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long fileID))
            {
                assetReference.Guid = guid;
                assetReference.LocalFileId = fileID;

                if (ensureInBundle)
                {
                    AssetBundleService.EnsureReferenceInAnyBundle(guid, targetBundle);
                }
            }

            if (AssetDatabase.IsSubAsset(obj))
            {
                assetReference.SubAssetReference = obj.name;
            }

            return assetReference;
        }

        public static AssetReference<T> CreateFromGuid(string guid, bool ensureInBundle, AssetBundleData targetBundle = null) // todo: allow specifying bundle 
        {
            if (string.IsNullOrEmpty(guid))
            {
                return default;
            }

            var assetReference = new AssetReference<T>()
            {
                Name = string.Empty,
                SubAssetReference = string.Empty,
                Guid = guid,
                LocalFileId = 0,
            };

            if (ensureInBundle)
            {
                AssetBundleService.EnsureReferenceInAnyBundle(guid, targetBundle);
            }

            return assetReference;
        }

        public static AssetReference<T> CreateFromAssetPath(string assetPath, bool ensureInBundle, AssetBundleData targetBundle = null, string subAssetName = null) // todo: allow specifying bundle 
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return default;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var assetReference = CreateFromGuid(guid, ensureInBundle, targetBundle: targetBundle);

            if(!string.IsNullOrEmpty(subAssetName))
            {
                assetReference.SubAssetReference = subAssetName;
            }

            return assetReference;
        }
#endif

        public override string ToString()
        {
            return string.Format("{0} {1} [{2}:{3}]", Name, SubAssetReference, Guid, LocalFileId);
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Guid);
        }

        public string GetName()
        {
            if (!IsValid())
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(Name))
            {
                return Name;
            }

#if UNITY_EDITOR
            var asset = EditorLoadAsset(null);
            if (asset == null)
            {
                Debug.LogError("null reference?");
                return Guid;
            }

            return asset.name;
#endif

            return Name;
        }

        public string GetCacheKey()
        {
            return Guid + SubAssetReference;
        }

#if UNITY_EDITOR

        public string GetAssetCacheKey()
        {
            return $"{Guid}_{LocalFileId}_{SubAssetReference}";
        }

        /// <summary>
        /// Only use in Editor scripts. 
        /// loadingFrom is required for updating moved references. optional otherwise. 
        /// </summary>
        /// <returns></returns>
        public T EditorLoadAsset(Object loadingFrom, bool logDeleted = true, bool useAssetCache = true)
        {
            UnityEngine.Profiling.Profiler.BeginSample("EditorLoadAsset");

            if (IsValid())
            {
                var assetCacheKey = GetAssetCacheKey();

                if(useAssetCache)
                {
                    if(AssetReferenceCacheAndImporter.TryGetFromAssetCache(assetCacheKey, out T cacheResult))
                    {
                        if(cacheResult != null)
                        {
                            UnityEngine.Profiling.Profiler.EndSample(); 
                            return cacheResult;
                        }
                        else
                        {
                            AssetReferenceCacheAndImporter.RemoveFromCache(assetCacheKey, Guid); 
                        }
                    }
                }

                var assetPath = AssetDatabase.GUIDToAssetPath(Guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);

                if (asset != null && string.IsNullOrEmpty(Guid))
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier<T>(asset, out string foundGuid, out long foundFileId))
                    {
                        Name = asset.name;
                        Guid = foundGuid;
                        LocalFileId = foundFileId;

                        if (loadingFrom != null)
                        {
                            EditorUtility.SetDirty(loadingFrom);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(SubAssetReference))
                {
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var subAsset in subAssets)
                    {
                        if (subAsset.name == SubAssetReference && subAsset as T != null)
                        {
                            asset = (T)subAsset;
                            break;
                        }
                    }
                }

                if (asset == null)
                {
                    if(logDeleted)
                    {
                        Debug.LogError($"{Name} [{Guid}] has been deleted? {assetPath}", loadingFrom);
                    }
                }

                if (asset != null && asset.name != Name)
                {
                    // if (!string.IsNullOrEmpty(Name))
                    // {
                    //     Debug.LogWarning($"AssetReference's Asset was renamed from {Name} to {asset.name} - guid: [{Guid}]", loadingFrom);
                    // }

                    Name = asset.name;

                    if (loadingFrom != null)
                    {
                        EditorUtility.SetDirty(loadingFrom);
                    }
                }

                if(useAssetCache)
                {
                    AssetReferenceCacheAndImporter.AddToCache(assetCacheKey, Guid, asset); 
                }

                UnityEngine.Profiling.Profiler.EndSample();
                return asset;
            }
            else
            {
                Debug.LogError($"Requested to load an invalid reference? {this}");

                UnityEngine.Profiling.Profiler.EndSample();
                return null;
            }
        }
#endif

        public AssetReference<T> Clone()
        {
            var newAssetReference = new AssetReference<T>();

            newAssetReference.Name = Name + string.Empty;
            newAssetReference.SubAssetReference = SubAssetReference + string.Empty;
            newAssetReference.Guid = Guid + string.Empty;
            newAssetReference.LocalFileId = LocalFileId;

            return newAssetReference;
        }

        public override bool Equals(object obj)
        {
            // if(obj is not AssetReference<T>)
            // {
            //     return false;
            // }

            var otherRef = (AssetReference<T>)obj;

            if (this.Guid == null && otherRef.Guid == null)
            {
                return true;
            }

            return this.Equals(otherRef);
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(Guid, SubAssetReference, LocalFileId);
        }

        public bool Equals(AssetReference<T> other)
        {
            return Guid.Equals(other.Guid, System.StringComparison.Ordinal) && SubAssetReference.Equals(other.SubAssetReference, System.StringComparison.Ordinal) && LocalFileId == other.LocalFileId;
        }

        // 🔪 AssetReference<Object> cant be cast to AssetReference<GameObject> 🔪
        public static implicit operator AssetReference<T>(AssetReference<GameObject> v)
        {
            return new AssetReference<T>()
            {
                Guid = v.Guid,
                LocalFileId = v.LocalFileId,
                Name = v.Name,
                SubAssetReference = v.SubAssetReference,
            };
        }

        public static implicit operator AssetReference<T>(AssetReference<Sprite> v)
        {
            return new AssetReference<T>()
            {
                Guid = v.Guid,
                LocalFileId = v.LocalFileId,
                Name = v.Name,
                SubAssetReference = v.SubAssetReference,
            };
        }

        public static implicit operator AssetReference<T>(AssetReference<Object> v)
        {
            return new AssetReference<T>()
            {
                Guid = v.Guid,
                LocalFileId = v.LocalFileId,
                Name = v.Name,
                SubAssetReference = v.SubAssetReference,
            };
        }
    }


#if UNITY_EDITOR
    public static class AssetReferenceE
    {
        private static void FixSerializedObject(Object objectAsset, List<string> guidList, int stackDepth)
        {
            if (objectAsset == null) return;

            stackDepth++;
            if (stackDepth >= 4)
            {
                return;
            }

            if (objectAsset as GameObject != null)
            {
                var gameobject = objectAsset as GameObject;

                // iterate over prefab's components too..
                var components = gameobject.GetComponents<Component>();
                foreach (var component in components)
                {
                    FixSerializedObject(component, guidList, stackDepth);
                }

                // child gameobjects.. 
                var transform = gameobject.transform;
                var childCount = transform.childCount;
                for (var t = 0; t < childCount; ++t)
                {
                    var childTransform = transform.GetChild(t);
                    var childGameObject = childTransform.gameObject;
                    FixSerializedObject(childGameObject, guidList, stackDepth);
                }
            }

            var changed = false;

            // LogService.Log($"searching {objectAsset.name}..");

            var objectSerialized = new SerializedObject(objectAsset);
            var objectProperty = objectSerialized.GetIterator();
            while (objectProperty.NextVisible(true))
            {
                // LogService.Log($"{objectProperty.name} | {objectProperty.type} | {objectProperty.displayName}");

                changed = FixAssetBundle(objectAsset, objectProperty, guidList) || changed;
            }

            if (changed)
            {
                objectSerialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(objectAsset);

                if (objectAsset as Component != null)
                {
                    var component = objectAsset as Component;
                    var originalGameobject = component.gameObject;
                    EditorUtility.SetDirty(originalGameobject);
                }
            }
        }

        private static bool FixAssetBundle(Object asset, SerializedProperty objectProperty, List<string> guidList)
        {
            var isAssetReference = objectProperty.type.ToLowerInvariant().Contains("assetreference");

            var changed = false;

            // asset reference tracking 
            if (isAssetReference)
            {


                var guidProperty = objectProperty.FindPropertyRelative("Guid");
                if (guidProperty != null)
                {
                    var assetGuid = guidProperty.stringValue;
                    if (!string.IsNullOrEmpty(assetGuid) && !guidList.Contains(assetGuid))
                    {
                        if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(assetGuid)))
                        {
                            Debug.LogError($"'{asset.name}' contains a GUID reference [{assetGuid}] for an asset that has been deleted. ", asset);
                        }
                        else
                        {
                            guidList.Add(assetGuid);
                        }
                    }

                    // local id fix 
                    // var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                    // var assetAtPath = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    // if (assetAtPath != null)
                    // {
                    //     if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetAtPath, out string guid, out long localid))
                    //     {
                    //         var localFileIdProperty = objectProperty.FindPropertyRelative("LocalFileId");
                    //         localFileIdProperty.longValue = localid;
                    //         localFileIdProperty.serializedObject.ApplyModifiedProperties();
                    //     }
                    // }

                    // the move from references to GUIDs
                    // else
                    // {
                    //     var referenceProperty = objectProperty.FindPropertyRelative("Reference");
                    //     if (referenceProperty != null)
                    //     {
                    //         var referenceString = referenceProperty.stringValue;
                    //         if (!string.IsNullOrEmpty(referenceString))
                    //         {
                    //             var guidFromPath = AssetDatabase.GUIDFromAssetPath(referenceString);
                    //             var guidString = guidFromPath.ToString();
                    // 
                    //             if (!string.IsNullOrEmpty(guidString))
                    //             {
                    //                 guidProperty.stringValue = guidString;
                    //                 guidProperty.serializedObject.ApplyModifiedProperties();
                    // 
                    //                 LogService.Log($"fixed broken reference: {guidString}");
                    //             }
                    //         }
                    //     }
                    // }
                }
            }

            return changed;
        }

        public static void FindDanglingReferencesInsertIntoBundleData(AssetBundleData bundleData)
        {
            EditorUtility.DisplayProgressBar("Refreshing All AssetReferences", "Searching for assets", 0f);

            AssetDatabase.StartAssetEditing();

            var guidList = new List<string>();

            try
            {
                var objectGuids = AssetDatabase.FindAssets("t:Object");
                for (var i = 0; i < objectGuids.Length; ++i)
                {
                    var objectGuid = objectGuids[i];

                    if (string.IsNullOrEmpty(objectGuid)) continue;

                    var objectPath = AssetDatabase.GUIDToAssetPath(objectGuid);
                    if (string.IsNullOrEmpty(objectPath)) continue;

                    var objectAsset = AssetDatabase.LoadAssetAtPath<Object>(objectPath);
                    if (objectAsset == null) continue;

                    if (EditorUtility.DisplayCancelableProgressBar($"Refreshing All AssetReferences [{i:N0}/{objectGuids.Length:N0}]",
                        objectAsset.name, (float)i / objectGuids.Length))
                    {
                        break;
                    }

                    FixSerializedObject(objectAsset, guidList, 0);
                }

                guidList = guidList.Distinct().ToList();

                for (var i = 0; i < guidList.Count; ++i)
                {
                    var guid = guidList[i];

                    if (EditorUtility.DisplayCancelableProgressBar($"storing GUIDs [{i:N0}/{guidList.Count:N0}]", guid, (float)i / guidList.Count))
                    {
                        break;
                    }

                    AssetBundleService.EnsureReferenceInAnyBundle(guid, bundleData);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
        }
    }
#endif
}