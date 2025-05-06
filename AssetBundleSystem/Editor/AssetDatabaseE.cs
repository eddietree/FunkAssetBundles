#if UNITY_EDITOR
namespace FunkAssetBundles
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public static class AssetDatabaseE
    {
        public static T FindSingletonAsset<T>() where T : UnityEngine.Object
        {
            var filter = string.Format("t:{0}", typeof(T).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
                return asset;
            }

            return null;
        }
        public static GameObject FindSingletonPrefab(string name)
        {
            var filter = name;
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                if (asset != null)
                {
                    return asset;
                }
            }

            return null;
        }

        public static T FindSingletonAsset<T>(string name) where T : MonoBehaviour
        {
            var filter = name;
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                if (asset != null)
                {
                    return asset.GetComponent<T>();
                }
            }

            return null;
        } 

        public static void LoadAssetsOfType<T>(List<T> results)
            where T : UnityEngine.Object
        {
            var filter = string.Format("t:{0}", typeof(T).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);

                if (!results.Contains(asset))
                {
                    results.Add(asset);
                }
            }
        }

        public static void LoadAssetsOfType<T>(List<T> results, params System.Type[] validTypes)
            where T : UnityEngine.Object
        {
            foreach(var validType in validTypes)
            {
                var filter = string.Format("t:{0}", validType.Name);
                var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

                foreach (var assetGuid in assetGuids)
                {
                    var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);

                    if(asset == null)
                    {
                        continue;
                    }

                    if (!results.Contains(asset))
                    {
                        results.Add(asset);
                    }
                }
            }
        }

        public static void LoadAssetsOfTypeToObjectList<T>(List<Object> results)
            where T : UnityEngine.Object
        {
            var filter = string.Format("t:{0}", typeof(T).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);

                if (!results.Contains(asset))
                {
                    results.Add(asset);
                }
            }
        }

        public static void LoadGameObjectsWithComponent<C>(List<Object> results)
            where C : Component
        {
            var filter = string.Format("t:{0}", typeof(GameObject).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                var component = (asset as GameObject).GetComponent<C>();
                if (component == null)
                    continue;

                if (results.Contains(asset))
                    continue;

                results.Add(asset);
            }
        }

        public static void LoadGameObjectComponents<C>(List<C> results)
            where C : Component
        {
            var filter = string.Format("t:{0}", typeof(GameObject).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                var component = (asset as GameObject).GetComponent<C>();
                if (component == null)
                    continue;

                if (results.Contains(component))
                    continue;

                results.Add(component);
            }
        }

        public static void LoadScriptableObjectsAsObjects<C>(List<Object> results)
            where C : ScriptableObject
        {
            var filter = string.Format("t:{0}", typeof(C).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

                var scriptable = asset as C;
                if (scriptable == null)
                    continue;

                if (results.Contains(asset))
                    continue;

                results.Add(asset);
            }
        }

        public static void LoadScriptableObjects<C>(List<C> results, bool removeNullEntries = false)
            where C : ScriptableObject
        {
            if(removeNullEntries)
            {
                for(var i = results.Count - 1; i >= 0; --i)
                {
                    var original = results[i];
                    if(original == null)
                    {
                        results.RemoveAt(i);
                    }
                }
            }

            var filter = string.Format("t:{0}", typeof(C).Name);
            var assetGuids = UnityEditor.AssetDatabase.FindAssets(filter);

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

                var scriptable = asset as C;
                if (scriptable == null)
                    continue;

                if (results.Contains(scriptable))
                    continue;

                results.Add(scriptable);
            }
        }

        public static bool TryDeleteAsset(Object asset)
        {
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(asset);
            if(string.IsNullOrEmpty(assetPath))
            {
                return false; 
            }

            UnityEditor.AssetDatabase.DeleteAsset(assetPath); 
            return true; 
        }
    }
}
#endif