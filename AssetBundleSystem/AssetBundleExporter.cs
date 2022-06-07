using System.Reflection;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif

// asset bundles are built locally to library cache folder
// final builds will 'deploy' them to streaming assets before building

public class AssetBundleExporter
    : MonoBehaviour
{
#if UNITY_EDITOR
    public static string GetBundlesBuildFolder()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/FunkAssetBundlesCache"));
    }

    public static string GetBundlesDeployFolder()
    {
        return AssetBundleService.GetBundlesDeployFolder();
    }

    [MenuItem("Build System/Asset Bundles/Helper/Force Reserialize All .meta")]
    public static void UnityForceReserializeAllAssets()
    {
        var confirm = EditorUtility.DisplayDialog("Confirm", "Are you sure you want reserialize EVERYTHING? It will take a very long time.", "Yes", "No");
        if (confirm)
        {
            var guids = AssetDatabase.FindAssets("*");

            var confirm2 = EditorUtility.DisplayDialog("Confirm", string.Format("Proceed with touching {0} assets?", guids.Length), "Yes", "No");
            if (confirm2)
            {
                var paths = new List<string>(guids.Length);

                foreach (var guid in guids)
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));

                var stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeMetadata);
                AssetDatabase.StopAssetEditing();
                stopwatch.Stop();
                Debug.LogFormat("Reserialized all assets in {0} seconds", stopwatch.Elapsed.Seconds);
            }
        }
    }

    [MenuItem("Build System/Asset Bundles/Clean (Delete)")]
    public static void CleanBundlesMenu()
    {
        CleanBundles(false);
    }

    public static void CleanBundles(bool force = true)
    {
        if (Application.isPlaying != false)
        {
            Debug.LogError("Cannot export in play mode");
            return;
        }

        var confirmed = force;

        if (force == false)
        {
            confirmed = EditorUtility.DisplayDialog("Confirm", "Delete all exported bundles?", "Delete", "Cancel");
        }

        if (confirmed)
        {
            var bundlesFolder = GetBundlesBuildFolder();
            var bundlesFolderMeta = $"{bundlesFolder}.meta";

            Debug.LogFormat("AssetBundleExporter.CleanBundles: cleaning bundles at {0}...", bundlesFolder);

            if (Directory.Exists(bundlesFolder))
                Directory.Delete(bundlesFolder, true);
            if (File.Exists(bundlesFolderMeta))
                File.Delete(bundlesFolderMeta); 
        }

        AssetDatabase.Refresh(); 
    }

    [MenuItem("Build System/Asset Bundles/Build (Windows)")] public static void BuildBundlesPlatformWindows() { BuildBundlesForTarget(BuildTarget.StandaloneWindows64); }
    [MenuItem("Build System/Asset Bundles/Build (Android)")] public static void BuildBundlesPlatformAndroid() { BuildBundlesForTarget(BuildTarget.Android); }
    // TODO: PSVR check if this works - Zach
    [MenuItem("Build System/Asset Bundles/Build (PS4)")] public static void BuildBundlesPlatformPS4() { BuildBundlesForTarget(BuildTarget.PS4); }
    //[MenuItem("Build System/Asset Bundles/Build (PS5)")] public static void BuildBundlesPlatformPS5() { BuildBundlesForTarget(BuildTarget.PS5); }

    [MenuItem("Build System/Asset Bundles/Build")]
    public static AssetBundleManifest BuildBundles()
    {
        if (Application.isPlaying != false)
        {
            Debug.LogError("Cannot build in play mode");
            return null;
        }

        var buildTarget = EditorUserBuildSettings.activeBuildTarget;

        Debug.Log($"Exporting bundles. Current build target: {buildTarget}");

        // active build target 
        var manifest = BuildBundlesForTarget(buildTarget);

        return manifest;
    }

    public static AssetBundleManifest BuildBundlesForTarget(BuildTarget platform)
    {
        Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: building for {0}...", platform);

        if (Application.isPlaying != false)
        {
            Debug.LogErrorFormat("AssetBundleExporter.BuildBundlesForTarget: > cannot build in play mode");
            return null;
        }

        Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: refreshing asset references...");
        AssetBundleService.EditorRefreshAssetBundleListOnPrefab();
        AssetBundleService.EditorUpdateBundleReferencesForBuilds(); 

        // prepare assets for export 
        Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: tagging assets for bundle inclusion...");
        TagAssetsForAssetBundles(platform);

        AssetDatabase.SaveAssets();

        var bundleOptions = BuildAssetBundleOptions.None;
            bundleOptions |= BuildAssetBundleOptions.ChunkBasedCompression;         // important, ensures only assets we care about are pulled into memory + for full loads: disabling this explodes loading time (ex from 4s to 20s)
            // bundleOptions |= BuildAssetBundleOptions.UncompressedAssetBundle;    // explodes filesize and does not really help by by much 
            // bundleOptions |= BuildAssetBundleOptions.DeterministicAssetBundle;      // important, keeps patch sizes smaller 
            bundleOptions |= BuildAssetBundleOptions.ForceRebuildAssetBundle;
            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileName;                // saves lookup time 
            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;   // saves lookup time 
            // bundleOptions |= BuildAssetBundleOptions.AssetBundleStripUnityVersion;
            // bundleOptions |= BuildAssetBundleOptions.DisableWriteTypeTree;

        var runtime = ConvertBuildTargetToRuntime(platform);
        var runtimeName = AssetBundleService.GetRuntimePlatformName(runtime);

        var buildRoot = GetBundlesBuildFolder() + $"/{runtimeName}";
        if (Directory.Exists(buildRoot) == false)
        {
            Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: creating build folder...");
            Directory.CreateDirectory(buildRoot);
        }

        var stopwatch = new System.Diagnostics.Stopwatch();

        stopwatch.Start();

        var manifest = BuildPipeline.BuildAssetBundles(buildRoot, bundleOptions, platform);

        stopwatch.Stop();

        Debug.LogFormat("AssetBundleExporter.ExportBundle: completed in {0} seconds", stopwatch.Elapsed.Seconds);

        if (manifest != null)
        {
            foreach (var name in manifest.GetAllAssetBundles())
                Debug.LogFormat("AssetBundleExporter.ExportBundle: bundle hash: {0} = {1}", name, manifest.GetAssetBundleHash(name));
        }
        else
        {
            Debug.LogFormat("AssetBundleExporter.ExportBundle: manifest was null");
        }

        // prepare assets for export 
        Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: removing asset tags...");
        RemoveAssetBundleTagsFromAssets();

        return manifest;
    }

    [MenuItem("Build System/Asset Bundles/Deploy (Windows)")] public static void DeployBundlesPlatformWindows() { DeployBundlesForTarget(BuildTarget.StandaloneWindows64); }
    [MenuItem("Build System/Asset Bundles/Deploy (Android)")] public static void DeployBundlesPlatformAndroid() { DeployBundlesForTarget(BuildTarget.Android); }
    // TODO: psvr check if this works - Zach
    [MenuItem("Build System/Asset Bundles/Deploy (PS4)")] public static void DeployBundlesPlatformPS4() { DeployBundlesForTarget(BuildTarget.PS4); }
    [MenuItem("Build System/Asset Bundles/Deploy")] public static void DeployBundles() { DeployBundlesForTarget(EditorUserBuildSettings.activeBuildTarget); }

    public static void DeployBundlesForTarget(BuildTarget platform)
    {
        var runtime = ConvertBuildTargetToRuntime(platform);
        var runtimeName = AssetBundleService.GetRuntimePlatformName(runtime);

        Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: deploying for platform {0} (runtime {1})...", platform, runtimeName);

        if (Application.isPlaying != false)
        {
            Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: > cannot deploy in play mode");
            return;
        }

        AssetDatabase.SaveAssets();

        var buildPath = GetBundlesBuildFolder();
        var deployPath = GetBundlesDeployFolder();

        Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: cleaning existing bundles...");

        if (Directory.Exists(deployPath))
        {
            Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: clearing existing deployment...");
            Directory.Delete(deployPath, true);
        }

        // no real way to know if the delete is still pending
        for (int i = 0; i < 3; ++i)
        {
            if (Directory.Exists(deployPath))
                System.Threading.Thread.Sleep(100);
        }

        if (Directory.Exists(deployPath))
        {
            Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: unable to delete existing deploy folder, aborting...");
            return;
        }

        Directory.CreateDirectory(deployPath);

        if (Directory.Exists(deployPath) == false)
        {
            Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: unable to create deploy folder, aborting...");
            return;
        }

        // thanks C#

        var copySrc = Path.Combine(buildPath, runtimeName.ToString().ToLowerInvariant());
        var copyDst = Path.Combine(deployPath, runtimeName.ToString().ToLowerInvariant());

        var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: copying {0} to {1}...", copySrc.Replace(root, ""), copyDst.Replace(root, ""));              

        RecursiveFolderCopy(copySrc, copyDst);
    }

    public static RuntimePlatform ConvertBuildTargetToRuntime(BuildTarget platform)
    {
        switch(platform)
        {
            case BuildTarget.Android:
                return RuntimePlatform.Android;
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return RuntimePlatform.WindowsPlayer;
            // TODO: PSVR
            case BuildTarget.PS4:
                return RuntimePlatform.PS4;
            default:
                Debug.LogError($"platform not configured");
                return Application.platform;
        }
    }

    [MenuItem("Build System/Asset Bundles/Helper/Remove All Tags")]
    public static void RemoveAssetBundleTagsFromAssets()
    {
        AssetDatabase.StartAssetEditing();

        try
        {
            var allAssetGuids = AssetDatabase.FindAssets(string.Empty);

            foreach(var assetGuid in allAssetGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                
                var assetImporter = AssetImporter.GetAtPath(assetPath);
                if(assetImporter != null && !string.IsNullOrEmpty(assetImporter.assetBundleName))
                {
                    assetImporter.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); 
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.SaveAssets();

        Debug.Log("AssetBundleExporter.RemoveAssetBundleTagsFromAssets: Removed all asset bundle tags from all assets.");
    }

    [MenuItem("Build System/Asset Bundles/Helper/Tag All (Current Platform)")]
    public static void TagAssetsForCurrentPlatform()
    {
        TagAssetsForAssetBundles(EditorUserBuildSettings.activeBuildTarget); 
    }

    public static void TagAssetsForAssetBundles(BuildTarget platform)
    {
        AssetDatabase.StartAssetEditing();

        try
        {
            var assetBundleService = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbBootstrap");
            var assetBundleDatas = assetBundleService.AssetBundleDatas;

            foreach (var assetBundleData in assetBundleDatas)
            {
                var bundleName = $"{assetBundleData.name}.bundle";

                // find all the paths 
                var paths = new List<string>();
                foreach(var assetData in assetBundleData.Assets)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                    if(!string.IsNullOrEmpty(assetPath))
                    {
                        paths.Add(assetPath); 
                    }
                }

                Debug.LogFormat("AssetBundleExporter.ExportBundle: found {0} assets...", paths.Count);
                Debug.LogFormat("AssetBundleExporter.ExportBundle: setting asset bundle names to {0}...", bundleName);

                foreach (var path in paths)
                {
                    var assetImporter = AssetImporter.GetAtPath(path);
                    if(assetImporter != null)
                    {
                        assetImporter.SetAssetBundleNameAndVariant(bundleName, string.Empty);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); 
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.SaveAssets();
    }

    public static void RecursiveFolderCopy(string srcFolder, string dstFolder)
    {
        Debug.LogFormat("AssetBundleExporter: copy folder {0} -> {1}", srcFolder, dstFolder);

        if (Directory.Exists(dstFolder) == false)
            Directory.CreateDirectory(dstFolder);

        var files = Directory.GetFiles(srcFolder);

        foreach (var file in files)
        {
            var srcFile = Path.Combine(srcFolder, Path.GetFileName(file));
            var dstFile = Path.Combine(dstFolder, Path.GetFileName(file));

            Debug.LogFormat("AssetBundleExporter: copy file {0} -> {1}", srcFile, dstFile);

            File.Copy(srcFile, dstFile);
        }

        var folders = Directory.GetDirectories(srcFolder);

        foreach (var folder in folders)
        {
            var childSrcFolder = Path.GetFileName(folder);
            var childDstFolder = Path.Combine(dstFolder, childSrcFolder);

            RecursiveFolderCopy(childSrcFolder, childDstFolder);
        }
    }
#endif
}