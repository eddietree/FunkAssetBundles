#if UNITY_EDITOR
namespace FunkAssetBundles
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;

    [CustomEditor(typeof(AssetBundleService))]
    public class AssetBundleServiceEditor : Editor
    {

        private string _previousDebugData;
        private float _previousDebugQueryAt;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            // note: can we update this as build time? pre process build step 
            if (GUILayout.Button("Refresh bundle list"))
            {
                AssetBundleService.EditorRefreshAssetBundleListOnPrefab();
            }

            if (GUILayout.Button("Refresh references"))
            {
                AssetBundleService.EditorUpdateBundleReferencesForBuilds();
            }

            if(Application.isPlaying)
            {
                var instance = (AssetBundleService) target;

                if(Time.time > _previousDebugQueryAt + 1f)
                {
                    _previousDebugData = instance.BuildDebugStats();
                    _previousDebugQueryAt = Time.time;
                }

                EditorGUILayout.HelpBox(_previousDebugData, MessageType.Info, true); 
            }
        }
    }
}
#endif