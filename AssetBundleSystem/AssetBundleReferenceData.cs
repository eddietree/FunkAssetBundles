using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CustomPropertyDrawer(typeof(AssetBundleReferenceData))]
public class AsseyBundleReferenceDataPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // load the existing asset 
        Object asset = null;

        var guidProperty = property.FindPropertyRelative("GUID");
        var guid = guidProperty.stringValue;

        var assetBundleReferenceProperty= property.FindPropertyRelative("AssetBundleReference");
        var assetBundleReference = assetBundleReferenceProperty.stringValue;

        if (!string.IsNullOrEmpty(guid))
        {
            var assetpath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(assetpath))
            {
                asset = AssetDatabase.LoadAssetAtPath<Object>(assetpath);
            }

            if(asset == null)
            {
                var labelRect = position;
                labelRect.width /= 2;


                GUI.Label(labelRect, $"[missing guid {guid}: {assetBundleReference}]"); 

                position.width /= 2;
                position.x += position.width;
            }
        }

        var newAsset = EditorGUI.ObjectField(position, asset, typeof(Object), false); 
        if(newAsset != asset)
        {
            if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(newAsset, out string newGuid, out long newLocalId))
            {
                guidProperty.stringValue = newGuid;
                guidProperty.serializedObject.ApplyModifiedProperties(); 
            }
        }

        if(GUI.changed)
        {
            property.serializedObject.ApplyModifiedProperties(); 
        }
    }
}

#endif

[System.Serializable]
public class AssetBundleReferenceData
{
    // this is how we actually referencce things in our asset bundle system 
    public string GUID = string.Empty;

    // editor slots this in at asset bundle build time 
    public string AssetBundleReference = string.Empty;

    public override bool Equals(object obj)
    {
        var otherData = obj as AssetBundleReferenceData;
        if (otherData == null) return false;
        return GUID.Equals(otherData.GUID, System.StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return GUID.GetHashCode(); 
    }
}
